using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.TestAccounts;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.PasswordTestSupport;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class StaffPasswordTests
{

    [Fact]
    public async Task StaffPasswordChangeValidatesFieldsAndOnlyRevokesItsOwnSessions()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        var staffId = await CreatePasswordStaffAsync(app);
        using var first = app.CreateClient();
        using var second = app.CreateClient();
        await LoginPasswordStaffAsync(first);
        await LoginPasswordStaffAsync(second);
        using var owner = await InviteOwnerAsync(seeded);
        using var noCsrf = await PostAsync(first, PasswordPath,
            new { currentPassword = Password, newPassword = NewPassword, confirmPassword = NewPassword }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        foreach (var values in new[]
        {
            ("wrong", NewPassword, NewPassword), (Password, "weak", "weak"),
            (Password, NewPassword, "different"), ("", NewPassword, NewPassword)
        })
        {
            using var invalid = await ChangePasswordAsync(first, values.Item1, values.Item2, values.Item3);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        await AssertStaffPasswordAsync(app, staffId, Password);
        using var intact = await first.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, intact.StatusCode);
        // Extra target/role fields cannot redirect this operation to the Owner.
        using var changed = await PostAsync(first, PasswordPath,
            new
            {
                currentPassword = Password,
                newPassword = NewPassword,
                confirmPassword = NewPassword,
                userId = seeded.OwnerId,
                email = Email,
                role = "Owner"
            }, await GetCsrfAsync(first));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.True(changed.Headers.CacheControl?.NoStore);
        foreach (var client in new[] { first, second })
        {
            using var revoked = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
        await AssertOriginalStateAsync(app, seeded);
        using var ownerIntact = await owner.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ownerIntact.StatusCode);
        using var fresh = app.CreateClient();
        using var oldLogin = await PostAsync(fresh, "/api/auth/login",
            new { email = StaffEmail, password = Password }, await GetCsrfAsync(fresh));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        await LoginPasswordStaffAsync(fresh, NewPassword);
        using var staffMe = await fresh.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, staffMe.StatusCode);
        using var forbidden = await fresh.GetAsync(InvitePath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await AssertStaffPasswordAsync(app, staffId, NewPassword);
    }

    [Fact]
    public async Task StaffPasswordChangeKeepsLockoutAndRequestLimit()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        var staffId = await CreatePasswordStaffAsync(app);
        using var client = app.CreateClient();
        await LoginPasswordStaffAsync(client);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var denied = await ChangePasswordAsync(client, attempt < 5 ? "wrong" : Password, NewPassword, NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        using var limited = await ChangePasswordAsync(client, Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await AssertStaffPasswordAsync(app, staffId, Password);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var staff = await users.FindByIdAsync(staffId.ToString());
        Assert.NotNull(staff);
        Assert.True(await users.IsLockedOutAsync(staff));
    }

    [Fact]
    public async Task StaffRoleCannotBypassOwnerMfa()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = await users.FindByIdAsync(seeded.OwnerId.ToString());
        Assert.NotNull(owner);
        Assert.True((await users.AddToRoleAsync(owner, "Staff")).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(owner, false)).Succeeded);
        using var dualRole = app.CreateClient();
        using var login = await PostAsync(dualRole, "/api/auth/login",
            new { email = Email, password = Password }, await GetCsrfAsync(dualRole));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var bypass = await ChangePasswordAsync(dualRole, Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.Forbidden, bypass.StatusCode);
    }

    [Fact]
    public async Task StaffPasswordChangeRollsBackCommitFailureAndRejectsWaitingStaleSession()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        var staffId = await CreatePasswordStaffAsync(app);
        using var first = app.CreateClient();
        using var second = app.CreateClient();
        await LoginPasswordStaffAsync(first);
        await LoginPasswordStaffAsync(second);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stamp = await db.Users.AsNoTracking().Where(user => user.Id == staffId)
            .Select(user => user.SecurityStamp).SingleAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_staff_password_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic staff password failure'; END; $$;
            CREATE CONSTRAINT TRIGGER reject_staff_password_commit AFTER UPDATE ON "AspNetUsers"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_staff_password_commit();
            """, TestContext.Current.CancellationToken);
        using var failed = await ChangePasswordAsync(first, Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await AssertStaffPasswordAsync(app, staffId, Password);
        Assert.Equal(stamp, await db.Users.AsNoTracking().Where(user => user.Id == staffId)
            .Select(user => user.SecurityStamp).SingleAsync(TestContext.Current.CancellationToken));
        using var intact = await first.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, intact.StatusCode);
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER reject_staff_password_commit ON "AspNetUsers";
            DROP FUNCTION reject_staff_password_commit();
            """, TestContext.Current.CancellationToken);
        var firstCsrf = await GetCsrfAsync(first);
        var secondCsrf = await GetCsrfAsync(second);
        await using var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {staffId} FOR UPDATE", TestContext.Current.CancellationToken);
        var body = new { currentPassword = Password, newPassword = NewPassword, confirmPassword = NewPassword };
        var firstChange = PostAsync(first, PasswordPath, body, firstCsrf);
        var secondChange = PostAsync(second, PasswordPath, body, secondCsrf);
        using var monitorScope = app.Services.CreateScope();
        var monitor = monitorScope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await monitor.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")
            .SingleAsync(timeout.Token) < 2) await Task.Delay(20, timeout.Token);
        await blocker.CommitAsync(TestContext.Current.CancellationToken);
        var responses = await Task.WhenAll(firstChange, secondChange);
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await AssertStaffPasswordAsync(app, staffId, NewPassword);
        await AssertOriginalStateAsync(app, seeded);
    }
}
