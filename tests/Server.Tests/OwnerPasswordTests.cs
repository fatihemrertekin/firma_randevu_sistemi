using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Server.Tests;

public sealed partial class OwnerMfaTests
{
    private const string PasswordPath = "/api/auth/change-password";
    private const string NewPassword = "Synthetic!Changed456";

    [Fact]
    public async Task PasswordChangeRequiresOwnerMfaCsrfAndValidPasswordsAndRevokesAllOldSessions()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var client = app.CreateClient();
        using var anonymous = await ChangePasswordAsync(client, Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        await PasswordStepAsync(client);
        using var pendingDenied = await ChangePasswordAsync(client, Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, pendingDenied.StatusCode);
        await CompleteMfaAsync(client, seeded.Key);
        using var noCsrf = await PostAsync(client, PasswordPath,
            new { currentPassword = Password, newPassword = NewPassword, confirmPassword = NewPassword }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var wrong = await ChangePasswordAsync(client, "wrong", NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var weak = await ChangePasswordAsync(client, Password, "weak", "weak");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        using var mismatch = await ChangePasswordAsync(client, Password, NewPassword, "different");
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        using var empty = await ChangePasswordAsync(client, "", NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        using var intact = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, intact.StatusCode);

        using var otherSession = app.CreateClient();
        await PasswordStepAsync(otherSession);
        await CompleteMfaAsync(otherSession, seeded.Key);
        using var pendingCode = app.CreateClient();
        using var pendingRecovery = app.CreateClient();
        await PasswordStepAsync(pendingCode);
        await PasswordStepAsync(pendingRecovery);

        using var changed = await ChangePasswordAsync(client, Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.True(changed.Headers.CacheControl?.NoStore);
        foreach (var session in new[] { client, otherSession })
        {
            using var revoked = await session.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
        using var staleCode = await PostAsync(pendingCode, "/api/auth/mfa/login",
            new { code = GenerateAuthenticatorCode(seeded.Key) }, await GetCsrfAsync(pendingCode));
        Assert.Equal(HttpStatusCode.Unauthorized, staleCode.StatusCode);
        using var staleRecovery = await PostAsync(pendingRecovery, "/api/auth/mfa/recovery-login",
            new { code = seeded.Codes[0] }, await GetCsrfAsync(pendingRecovery));
        Assert.Equal(HttpStatusCode.Unauthorized, staleRecovery.StatusCode);

        using var fresh = app.CreateClient();
        using var oldPassword = await PostAsync(fresh, "/api/auth/login",
            new { email = Email, password = Password }, await GetCsrfAsync(fresh));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var newPassword = await PostAsync(fresh, "/api/auth/login",
            new { email = Email, password = NewPassword }, await GetCsrfAsync(fresh));
        Assert.Equal(HttpStatusCode.Accepted, newPassword.StatusCode);
        using var onlyPassword = await fresh.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, onlyPassword.StatusCode);
        await CompleteMfaAsync(fresh, seeded.Key);
        using var ownerAccess = await fresh.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ownerAccess.StatusCode);
        await LogoutAsync(fresh);
        using var recoveryPassword = await PostAsync(fresh, "/api/auth/login",
            new { email = Email, password = NewPassword }, await GetCsrfAsync(fresh));
        Assert.Equal(HttpStatusCode.Accepted, recoveryPassword.StatusCode);
        using var preservedRecovery = await PostAsync(fresh, "/api/auth/mfa/recovery-login",
            new { code = seeded.Codes[0] }, await GetCsrfAsync(fresh));
        Assert.Equal(HttpStatusCode.NoContent, preservedRecovery.StatusCode);

        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Server.Infrastructure.AppUser>>();
        var owner = await users.FindByIdAsync(seeded.OwnerId.ToString());
        Assert.NotNull(owner);
        Assert.True(owner.TwoFactorEnabled);
        Assert.Equal(seeded.Key, await users.GetAuthenticatorKeyAsync(owner));
        Assert.Equal(7, await users.CountRecoveryCodesAsync(owner));
        Assert.NotEqual(seeded.Stamp, owner.SecurityStamp);
    }

    [Fact]
    public async Task PasswordChangeRejectsSetupOnlyAndNonOwnerSessions()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        string otherKey;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Server.Infrastructure.AppUser>>();
            var owner = await users.FindByIdAsync(seeded.OwnerId.ToString());
            Assert.NotNull(owner);
            Assert.True((await users.SetTwoFactorEnabledAsync(owner, false)).Succeeded);
            var other = await users.FindByIdAsync(seeded.OtherUserId.ToString());
            Assert.NotNull(other);
            Assert.True((await users.ResetAuthenticatorKeyAsync(other)).Succeeded);
            otherKey = Assert.IsType<string>(await users.GetAuthenticatorKeyAsync(other));
        }
        using var setupOnly = app.CreateClient();
        using var setupLogin = await PostAsync(setupOnly, "/api/auth/login",
            new { email = Email, password = Password }, await GetCsrfAsync(setupOnly));
        Assert.Equal(HttpStatusCode.NoContent, setupLogin.StatusCode);
        using var setupDenied = await ChangePasswordAsync(setupOnly, Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.Forbidden, setupDenied.StatusCode);
        using var nonOwner = app.CreateClient();
        using var otherLogin = await PostAsync(nonOwner, "/api/auth/login",
            new { email = "other@example.test", password = Password }, await GetCsrfAsync(nonOwner));
        Assert.Equal(HttpStatusCode.Accepted, otherLogin.StatusCode);
        await CompleteMfaAsync(nonOwner, otherKey);
        using var otherDenied = await ChangePasswordAsync(nonOwner, Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.Forbidden, otherDenied.StatusCode);
    }

    [Fact]
    public async Task PasswordChangeKeepsLockoutAndRequestLimit()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var client = app.CreateClient();
        await PasswordStepAsync(client);
        await CompleteMfaAsync(client, seeded.Key);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var denied = await ChangePasswordAsync(client, "wrong", NewPassword, NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<Server.Infrastructure.AppUser>>();
            var owner = await users.FindByIdAsync(seeded.OwnerId.ToString());
            Assert.NotNull(owner);
            Assert.True(await users.IsLockedOutAsync(owner));
        }
        for (var attempt = 5; attempt < 10; attempt++)
        {
            using var locked = await ChangePasswordAsync(client, Password, NewPassword, NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        }
        using var limited = await ChangePasswordAsync(client, Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
    }

    [Fact]
    public async Task PasswordChangeRollsBackDatabaseFailureAndSerializesParallelRequests()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var first = app.CreateClient();
        using var second = app.CreateClient();
        await PasswordStepAsync(first);
        await CompleteMfaAsync(first, seeded.Key);
        await PasswordStepAsync(second);
        await CompleteMfaAsync(second, seeded.Key);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Server.Infrastructure.AppDbContext>();
        // Fail after Identity has saved the hash/stamp but before the transaction commits.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_password_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic password failure'; END; $$;
            CREATE CONSTRAINT TRIGGER reject_password_commit AFTER UPDATE ON "AspNetUsers"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_password_commit();
            """, TestContext.Current.CancellationToken);
        using var failed = await ChangePasswordAsync(first, Password, NewPassword, NewPassword);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        using var stillActive = await first.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER reject_password_commit ON "AspNetUsers";
            DROP FUNCTION reject_password_commit();
            """, TestContext.Current.CancellationToken);

        var firstCsrf = await GetCsrfAsync(first);
        var secondCsrf = await GetCsrfAsync(second);
        await using var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {seeded.OwnerId} FOR UPDATE",
            TestContext.Current.CancellationToken);
        var body = new { currentPassword = Password, newPassword = NewPassword, confirmPassword = NewPassword };
        var firstChange = PostAsync(first, PasswordPath, body, firstCsrf);
        var secondChange = PostAsync(second, PasswordPath, body, secondCsrf);
        var waiting = 0;
        // Query outside the blocker transaction: PostgreSQL caches activity snapshots
        // within a transaction, so polling there can keep seeing the first result.
        using var monitorScope = app.Services.CreateScope();
        var monitorDb = monitorScope.ServiceProvider.GetRequiredService<Server.Infrastructure.AppDbContext>();
        using var waitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (waiting < 2)
        {
            waiting = await monitorDb.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")
                .SingleAsync(waitTimeout.Token);
            if (waiting < 2) await Task.Delay(20, waitTimeout.Token);
        }
        await blocker.CommitAsync(TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(firstChange, secondChange);
        try
        {
            Assert.Single(results, result => result.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(results, result => result.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (var result in results) result.Dispose();
        }
        using var finalScope = app.Services.CreateScope();
        var users = finalScope.ServiceProvider.GetRequiredService<UserManager<Server.Infrastructure.AppUser>>();
        var owner = await users.FindByIdAsync(seeded.OwnerId.ToString());
        Assert.NotNull(owner);
        Assert.True(await users.CheckPasswordAsync(owner, NewPassword));
        Assert.False(await users.CheckPasswordAsync(owner, Password));
        Assert.True(owner.TwoFactorEnabled);
        Assert.Equal(seeded.Key, await users.GetAuthenticatorKeyAsync(owner));
        Assert.Equal(8, await users.CountRecoveryCodesAsync(owner));
    }

    private static async Task CompleteMfaAsync(HttpClient client, string key)
    {
        using var response = await PostAsync(client, "/api/auth/mfa/login",
            new { code = GenerateAuthenticatorCode(key) }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> ChangePasswordAsync(
        HttpClient client, string currentPassword, string newPassword, string confirmPassword) =>
        await PostAsync(client, PasswordPath, new { currentPassword, newPassword, confirmPassword },
            await GetCsrfAsync(client));
}
