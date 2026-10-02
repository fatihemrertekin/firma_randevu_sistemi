using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.TestAccounts;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.PasswordTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class AuthReadinessTests
{
    [Fact]
    public async Task CancellingPendingMfaClearsItsCookieWithoutChangingTheAccount()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var client = app.CreateClient();
        await PasswordStepAsync(client);
        using var noCsrf = await PostAsync(client, "/api/auth/logout", new { }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        await LogoutAsync(client);
        await LogoutAsync(client);
        using var cancelled = await PostAsync(client, "/api/auth/mfa/login",
            new { code = GenerateAuthenticatorCode(seeded.Key) }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.Unauthorized, cancelled.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        await PasswordStepAsync(client);
        await CompleteMfaAsync(client, seeded.Key);
    }

    [Fact]
    public async Task RecoveryCodeReplacementRequiresMfaCsrfAndPasswordAndRevokesOldCodesAndSessions()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var client = app.CreateClient();
        const string path = "/api/auth/mfa/recovery-codes";
        using var anonymous = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await PasswordStepAsync(client);
        using var passwordOnly = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, passwordOnly.StatusCode);
        await CompleteMfaAsync(client, seeded.Key);
        using var second = app.CreateClient();
        await PasswordStepAsync(second);
        await CompleteMfaAsync(second, seeded.Key);
        using var count = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(8, (await count.Content.ReadFromJsonAsync<RecoveryCount>(TestContext.Current.CancellationToken))?.Remaining);
        using var noCsrf = await PostAsync(client, path, new { currentPassword = Password }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var wrong = await PostAsync(client, path, new { currentPassword = "wrong" }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        using var replaced = await PostAsync(client, path, new { currentPassword = Password }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.True(replaced.Headers.CacheControl?.NoStore);
        var codes = await replaced.Content.ReadFromJsonAsync<EnableResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(codes);
        Assert.Equal(8, codes.RecoveryCodes.Length);
        using var revoked = await second.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var owner = await users.FindByIdAsync(seeded.OwnerId.ToString());
            Assert.NotNull(owner);
            Assert.True(await users.CheckPasswordAsync(owner, Password));
            Assert.Equal(seeded.Key, await users.GetAuthenticatorKeyAsync(owner));
            Assert.True(owner.TwoFactorEnabled);
        }
        await PasswordStepAsync(client);
        using var old = await PostAsync(client, "/api/auth/mfa/recovery-login",
            new { code = seeded.Codes[0] }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        using var fresh = await PostAsync(client, "/api/auth/mfa/recovery-login",
            new { code = codes.RecoveryCodes[0] }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);
        using var newCount = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(7, (await newCount.Content.ReadFromJsonAsync<RecoveryCount>(TestContext.Current.CancellationToken))?.Remaining);
    }

    [Fact]
    public async Task RecoveryCodeReplacementRollsBackFailureAndLimitsWrongPasswords()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var client = app.CreateClient();
        await PasswordStepAsync(client);
        await CompleteMfaAsync(client, seeded.Key);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_code_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic code failure'; END; $$;
            CREATE CONSTRAINT TRIGGER reject_code_commit AFTER UPDATE ON "AspNetUsers"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_code_commit();
            """, TestContext.Current.CancellationToken);
        using var failed = await PostAsync(client, "/api/auth/mfa/recovery-codes", new { currentPassword = Password }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_code_commit ON \"AspNetUsers\"; DROP FUNCTION reject_code_commit();", TestContext.Current.CancellationToken);
        for (var attempt = 0; attempt < 9; attempt++)
        {
            using var wrong = await PostAsync(client, "/api/auth/mfa/recovery-codes", new { currentPassword = "wrong" }, await GetCsrfAsync(client));
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }
        using var limited = await PostAsync(client, "/api/auth/mfa/recovery-codes", new { currentPassword = Password }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.True(await users.IsLockedOutAsync(Assert.IsType<AppUser>(await users.FindByIdAsync(seeded.OwnerId.ToString()))));
    }

    [Fact]
    public async Task OwnerResetDoesNotModifyAnUnrelatedAuthenticatedAccount()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Assert.True(await roles.RoleExistsAsync("Staff"));
        var staff = new AppUser { UserName = "staff@example.test", Email = "staff@example.test", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(staff, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(staff, "Staff")).Succeeded);
        using var client = app.CreateClient();
        using var login = await PostAsync(client, "/api/auth/login", new { email = staff.Email, password = Password }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var deniedCodes = await client.GetAsync("/api/auth/mfa/recovery-codes", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, deniedCodes.StatusCode);
        using var deniedReplace = await PostAsync(client, "/api/auth/mfa/recovery-codes", new { currentPassword = Password }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.Forbidden, deniedReplace.StatusCode);
        var issued = await IssueResetAsync(app, ResetIssue(seeded, "readiness-other-account"));
        using var denied = await ResetAsync(client, issued.Token);
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        using var stillActive = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
        await LogoutAsync(client);
        using var success = await ResetAsync(client, issued.Token);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
    }

    private sealed record RecoveryCount(int Remaining);
}
