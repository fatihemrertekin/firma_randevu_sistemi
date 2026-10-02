using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Identity;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;
using static Server.Tests.Support.TestAccounts;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class StaffAccountTests
{
    private const string Path = "/api/staff-accounts/";
    private static async Task<StaffAccountEndpoints.StaffPage> ReadAsync(HttpClient owner, string query = "")
    {
        using var response = await owner.GetAsync(Path + query, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<StaffAccountEndpoints.StaffPage>(await response.Content.ReadFromJsonAsync<StaffAccountEndpoints.StaffPage>(TestContext.Current.CancellationToken));
    }
    private static Task<HttpResponseMessage> DisableAsync(HttpClient owner, Guid id, string version, string? csrf) =>
        PostAsync(owner, $"{Path}{id}/deactivate", new { version }, csrf);

    [Fact]
    public async Task OnlyMfaOwnerCanListAndDeactivateStaffAndOwnerTargetsAreProtected()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app);
        using var anonymous = app.CreateClient();
        using var pending = app.CreateClient();
        await PasswordStepAsync(pending);
        using var staff = app.CreateClient();
        await LoginPasswordStaffAsync(staff);
        using var owner = await InviteOwnerAsync(seed);
        var row = Assert.Single((await ReadAsync(owner)).Items);
        Assert.Equal(id, row.Id);
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        {
            using var read = await client.GetAsync(Path, TestContext.Current.CancellationToken);
            Assert.Equal(status, read.StatusCode);
            using var write = await DisableAsync(client, id, row.Version, await GetCsrfAsync(client));
            Assert.Equal(status, write.StatusCode);
        }
        using var noCsrf = await DisableAsync(owner, id, row.Version, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var invalidVersion = await DisableAsync(owner, id, "", await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.BadRequest, invalidVersion.StatusCode);
        using var outdated = await DisableAsync(owner, id, "outdated", await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Conflict, outdated.StatusCode);
        using var badPage = await owner.GetAsync(Path + "?page=0&pageSize=51", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var dual = await users.FindByIdAsync(seed.OwnerId.ToString());
            Assert.NotNull(dual);
            Assert.True((await users.AddToRoleAsync(dual, "Staff")).Succeeded);
        }
        foreach (var target in new[] { seed.OwnerId, seed.OtherUserId, Guid.NewGuid() })
        {
            using var protectedTarget = await DisableAsync(owner, target, row.Version, await GetCsrfAsync(owner));
            Assert.Equal(HttpStatusCode.NotFound, protectedTarget.StatusCode);
        }
        Assert.Equal(id, Assert.Single((await ReadAsync(owner)).Items).Id);
        using var checkScope = app.Services.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Users.SingleAsync(user => user.Id == id, TestContext.Current.CancellationToken)).IsActive);
        Assert.Empty(await db.StaffDeactivationAudits.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeactivationRevokesSessionsLoginAndResetCodesWithoutChangingOwnerOrPassword()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app);
        using var owner = await InviteOwnerAsync(seed);
        using var staff = app.CreateClient();
        await LoginPasswordStaffAsync(staff);
        using var second = app.CreateClient();
        await LoginPasswordStaffAsync(second);
        var row = Assert.Single((await ReadAsync(owner)).Items);
        using var issued = await PostAsync(owner, "/api/staff-password-resets/", new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var code = Assert.IsType<StaffPasswordResetEndpoints.IssuedResponse>(await issued.Content.ReadFromJsonAsync<StaffPasswordResetEndpoints.IssuedResponse>(TestContext.Current.CancellationToken));
        using var beforeScope = app.Services.CreateScope();
        var before = await beforeScope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == id, TestContext.Current.CancellationToken);
        using var disabled = await DisableAsync(owner, id, row.Version, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.NoContent, disabled.StatusCode);
        using var repeat = await DisableAsync(owner, id, row.Version, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
        foreach (var client in new[] { staff, second })
        {
            using var revoked = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
        using var fresh = app.CreateClient();
        using var login = await PostAsync(fresh, "/api/auth/login", new { email = StaffEmail, password = Password }, await GetCsrfAsync(fresh));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        using var reset = await PostAsync(fresh, "/api/staff-password-resets/complete", new { token = code.Token, newPassword = NewPassword, confirmPassword = NewPassword }, await GetCsrfAsync(fresh));
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        using var newCode = await PostAsync(owner, "/api/staff-password-resets/", new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.BadRequest, newCode.StatusCode);
        Assert.False(Assert.Single((await ReadAsync(owner)).Items).IsActive);
        using var afterScope = app.Services.CreateScope();
        var db = afterScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var after = await db.Users.SingleAsync(user => user.Id == id, TestContext.Current.CancellationToken);
        Assert.False(after.IsActive);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.Equal(before.LockoutEnd, after.LockoutEnd);
        var audit = await db.StaffDeactivationAudits.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(id, audit.StaffId); Assert.Equal(seed.OwnerId, audit.ActorId);
        var unchangedOwner = await db.Users.SingleAsync(user => user.Id == seed.OwnerId, TestContext.Current.CancellationToken);
        Assert.True(unchangedOwner.IsActive); Assert.True(unchangedOwner.TwoFactorEnabled); Assert.Equal(seed.Stamp, unchangedOwner.SecurityStamp);
        using var stillOwner = await owner.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillOwner.StatusCode);
    }

    [Fact]
    public async Task ConcurrentDeactivationsCreateOneAuditAndInactiveAccountsStayOnTheirPage()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var other = new AppUser { Email = "z-staff@example.test", UserName = "z-staff@example.test", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(other, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(other, "Staff")).Succeeded);
        using var owner = await InviteOwnerAsync(seed);
        var first = await ReadAsync(owner, "?pageSize=1");
        Assert.True(first.HasMore); Assert.Equal(id, Assert.Single(first.Items).Id);
        var last = await ReadAsync(owner, "?page=2&pageSize=1");
        Assert.False(last.HasMore); Assert.Equal(other.Id, Assert.Single(last.Items).Id);
        var csrf = await GetCsrfAsync(owner);
        var writes = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => DisableAsync(owner, id, first.Items[0].Version, csrf)));
        foreach (var response in writes) { Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); response.Dispose(); }
        Assert.False(Assert.Single((await ReadAsync(owner, "?pageSize=1")).Items).IsActive);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.StaffDeactivationAudits.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AuditAndDeferredCommitFailuresRollBackStatusAndStamp()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app);
        using var owner = await InviteOwnerAsync(seed);
        using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        var row = Assert.Single((await ReadAsync(owner)).Items);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.Users.AsNoTracking().SingleAsync(user => user.Id == id, TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_deactivation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic deactivation failure'; END; $$;
            CREATE TRIGGER reject_deactivation BEFORE INSERT ON "StaffDeactivationAudits"
            FOR EACH ROW EXECUTE FUNCTION reject_deactivation();
            """, TestContext.Current.CancellationToken);
        using var failure = await DisableAsync(owner, id, row.Version, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER reject_deactivation ON "StaffDeactivationAudits";
            CREATE CONSTRAINT TRIGGER reject_deactivation AFTER INSERT ON "StaffDeactivationAudits"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_deactivation();
            """, TestContext.Current.CancellationToken);
        using var commit = await DisableAsync(owner, id, row.Version, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.InternalServerError, commit.StatusCode);
        var after = await db.Users.AsNoTracking().SingleAsync(user => user.Id == id, TestContext.Current.CancellationToken);
        Assert.True(after.IsActive); Assert.Equal(before.SecurityStamp, after.SecurityStamp); Assert.Equal(before.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.Empty(await db.StaffDeactivationAudits.ToArrayAsync(TestContext.Current.CancellationToken));
        using var stillSignedIn = await staff.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
    }

    [Fact]
    public async Task InactiveFlagRejectsValidStampCookieAndPendingMfaEvenWithoutStampRotation()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app);
        string key;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var staff = await users.FindByIdAsync(id.ToString()); Assert.NotNull(staff);
            Assert.True((await users.ResetAuthenticatorKeyAsync(staff)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(staff, true)).Succeeded);
            key = Assert.IsType<string>(await users.GetAuthenticatorKeyAsync(staff));
        }
        using var active = app.CreateClient();
        using var pending = app.CreateClient();
        foreach (var client in new[] { active, pending })
        {
            using var password = await PostAsync(client, "/api/auth/login", new { email = StaffEmail, password = Password }, await GetCsrfAsync(client));
            Assert.Equal(HttpStatusCode.Accepted, password.StatusCode);
        }
        await CompleteMfaAsync(active, key);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var staff = await db.Users.SingleAsync(user => user.Id == id, TestContext.Current.CancellationToken);
            staff.IsActive = false; await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        using var revoked = await active.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var mfa = await PostAsync(pending, "/api/auth/mfa/login", new { code = GenerateAuthenticatorCode(key) }, await GetCsrfAsync(pending));
        Assert.Equal(HttpStatusCode.Unauthorized, mfa.StatusCode);
    }

    [Fact]
    public async Task ResetAndDeactivationRaceCannotReopenTheDeactivatedAccount()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app);
        using var owner = await InviteOwnerAsync(seed);
        using var anonymous = app.CreateClient();
        var row = Assert.Single((await ReadAsync(owner)).Items);
        using var issued = await PostAsync(owner, "/api/staff-password-resets/", new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var code = Assert.IsType<StaffPasswordResetEndpoints.IssuedResponse>(await issued.Content.ReadFromJsonAsync<StaffPasswordResetEndpoints.IssuedResponse>(TestContext.Current.CancellationToken));
        var ownerCsrf = await GetCsrfAsync(owner); var anonymousCsrf = await GetCsrfAsync(anonymous);
        var responses = await Task.WhenAll(DisableAsync(owner, id, row.Version, ownerCsrf),
            PostAsync(anonymous, "/api/staff-password-resets/complete", new { token = code.Token, newPassword = NewPassword, confirmPassword = NewPassword }, anonymousCsrf));
        using var deactivation = responses[0]; using var reset = responses[1];
        Assert.True(deactivation.StatusCode == HttpStatusCode.NoContent && reset.StatusCode == HttpStatusCode.BadRequest ||
            deactivation.StatusCode == HttpStatusCode.Conflict && reset.StatusCode == HttpStatusCode.NoContent);
        if (deactivation.StatusCode == HttpStatusCode.Conflict)
        {
            var fresh = Assert.Single((await ReadAsync(owner)).Items);
            using var retry = await DisableAsync(owner, id, fresh.Version, await GetCsrfAsync(owner));
            Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        }
        Assert.False(Assert.Single((await ReadAsync(owner)).Items).IsActive);
        using var replay = await PostAsync(anonymous, "/api/staff-password-resets/complete", new { token = code.Token, newPassword = NewPassword, confirmPassword = NewPassword }, await GetCsrfAsync(anonymous));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().StaffDeactivationAudits.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MigrationKeepsExistingAccountsActiveAndSupportsSyntheticDownAndUp()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.GetConnectionString()).Options;
        await using var db = new AppDbContext(options);
        var migrations = db.Database.GetMigrations().ToArray();
        Assert.EndsWith("StaffAccountDeactivation", migrations[^1]);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[^2], TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AspNetUsers" ("Id", "UserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('8fab3fce-6c1b-490d-99ac-01d39a2b12d8', 'synthetic-existing', true, false, false, true, 0);
            """, TestContext.Current.CancellationToken);
        await migrator.MigrateAsync(migrations[^1], TestContext.Current.CancellationToken);
        Assert.True((await db.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).IsActive);
        await migrator.MigrateAsync(migrations[^2], TestContext.Current.CancellationToken);
        await migrator.MigrateAsync(migrations[^1], TestContext.Current.CancellationToken);
        Assert.True((await db.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).IsActive);
        Assert.Empty(await db.StaffDeactivationAudits.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitingDeactivationRejectsAnOwnerSessionRevokedAfterAuthentication()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app);
        using var owner = await InviteOwnerAsync(seed);
        var row = Assert.Single((await ReadAsync(owner)).Items);
        var csrf = await GetCsrfAsync(owner);
        using var lockScope = app.Services.CreateScope();
        var db = lockScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var lockedOwner = await db.Users.FromSqlInterpolated(
            $"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {seed.OwnerId} FOR UPDATE").SingleAsync(TestContext.Current.CancellationToken);
        var waiting = DisableAsync(owner, id, row.Version, csrf);
        await using var observer = new NpgsqlConnection(database.GetConnectionString());
        await observer.OpenAsync(TestContext.Current.CancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        var blocked = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var query = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%AspNetUsers%FOR UPDATE%')", observer);
            blocked = (bool)(await query.ExecuteScalarAsync(TestContext.Current.CancellationToken) ?? false);
            if (blocked) break;
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        Assert.True(blocked, "Pasifleştirme isteği gerçek satır kilidini beklemeli.");
        var users = lockScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.True((await users.UpdateSecurityStampAsync(lockedOwner)).Succeeded);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        using var result = await waiting;
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.True((await db.Users.AsNoTracking().SingleAsync(user => user.Id == id, TestContext.Current.CancellationToken)).IsActive);
        Assert.Empty(await db.StaffDeactivationAudits.ToArrayAsync(TestContext.Current.CancellationToken));
    }
}
