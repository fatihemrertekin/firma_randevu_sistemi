using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Identity;
using Server.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;
using static Server.Tests.Support.TestAccounts;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class OwnerMfaRecoveryTests
{

    [Fact]
    public async Task RecoveryRejectsWrongTargetRevokesCredentialsAndRequiresNewMfa()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var active = app.CreateClient();
        await PasswordStepAsync(active);
        using var firstMfa = await PostAsync(active, "/api/auth/mfa/login",
            new { code = GenerateAuthenticatorCode(seeded.Key) }, await GetCsrfAsync(active));
        Assert.Equal(HttpStatusCode.NoContent, firstMfa.StatusCode);

        using var pending = app.CreateClient();
        await PasswordStepAsync(pending);
        var request = new RecoverOwnerMfa.RecoveryRequest(RecoveryInstance, seeded.OwnerId, "operator-01", "ticket-01");
        Assert.False(await RecoverAsync(app, request with { InstanceId = "another-instance" }));
        Assert.False(await RecoverAsync(app, request with { OwnerId = Guid.NewGuid() }));
        Assert.False(await RecoverAsync(app, request with { OwnerId = seeded.OtherUserId }));
        Assert.False(await RecoverAsync(app, request with { OperatorReference = "someone@example.test" }));
        Assert.False(await RecoverAsync(app, request with { RequestReference = "" }));
        Assert.Equal(1, await RunRecoveryCommandAsync(app, request, confirmed: false));
        await AssertOriginalStateAsync(app, seeded);

        Assert.Equal(0, await RunRecoveryCommandAsync(app, request, confirmed: true));
        Assert.False(await RecoverAsync(app, request with { RequestReference = "ticket-repeat" }));
        using var revoked = await active.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var oldCode = await PostAsync(pending, "/api/auth/mfa/login",
            new { code = GenerateAuthenticatorCode(seeded.Key) }, await GetCsrfAsync(pending));
        Assert.Equal(HttpStatusCode.Unauthorized, oldCode.StatusCode);
        using var oldRecoveryCode = await PostAsync(pending, "/api/auth/mfa/recovery-login",
            new { code = seeded.Codes[0] }, await GetCsrfAsync(pending));
        Assert.Equal(HttpStatusCode.Unauthorized, oldRecoveryCode.StatusCode);

        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(seeded.OwnerId.ToString());
            Assert.NotNull(user);
            Assert.False(user.TwoFactorEnabled);
            Assert.NotEqual(seeded.Stamp, user.SecurityStamp);
            Assert.NotEqual(seeded.Key, await users.GetAuthenticatorKeyAsync(user));
            Assert.False(await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider,
                GenerateAuthenticatorCode(seeded.Key)));
            Assert.False((await users.RedeemTwoFactorRecoveryCodeAsync(user, seeded.Codes[0])).Succeeded);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.OwnerMfaRecoveryAudits.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(seeded.OwnerId, entry.OwnerId);
            Assert.Equal(RecoveryInstance, entry.InstanceId);
            Assert.Equal("operator-01", entry.OperatorReference);
            Assert.Equal("ticket-01", entry.RequestReference);
            Assert.InRange(entry.OccurredAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        }

        using var setupClient = app.CreateClient();
        using var passwordLogin = await PostAsync(setupClient, "/api/auth/login",
            new { email = Email, password = Password }, await GetCsrfAsync(setupClient));
        Assert.Equal(HttpStatusCode.NoContent, passwordLogin.StatusCode);
        using var setupAccount = await setupClient.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        var account = await setupAccount.Content.ReadFromJsonAsync<AccountResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(account);
        Assert.False(account.MfaEnabled);
        Assert.False(account.OwnerAccess);
        using var setup = await PostAsync(setupClient, "/api/auth/mfa/setup",
            new { password = Password }, await GetCsrfAsync(setupClient));
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var setupInfo = await setup.Content.ReadFromJsonAsync<SetupResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(setupInfo);
        Assert.NotEqual(seeded.Key, setupInfo.Key);
        using var enable = await PostAsync(setupClient, "/api/auth/mfa/enable",
            new { password = Password, code = GenerateAuthenticatorCode(setupInfo.Key) }, await GetCsrfAsync(setupClient));
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var enabled = await enable.Content.ReadFromJsonAsync<EnableResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(enabled);
        Assert.Equal(8, enabled.RecoveryCodes.Length);
        Assert.DoesNotContain(seeded.Codes[0], enabled.RecoveryCodes);

        // Even a new valid code cannot revive a temporary login from before recovery.
        using var stalePending = await PostAsync(pending, "/api/auth/mfa/login",
            new { code = GenerateAuthenticatorCode(setupInfo.Key) }, await GetCsrfAsync(pending));
        Assert.Equal(HttpStatusCode.Unauthorized, stalePending.StatusCode);
        Assert.False(await RecoverAsync(app, request));
        await PasswordStepAsync(setupClient);
        using var newMfa = await PostAsync(setupClient, "/api/auth/mfa/login",
            new { code = GenerateAuthenticatorCode(setupInfo.Key) }, await GetCsrfAsync(setupClient));
        Assert.Equal(HttpStatusCode.NoContent, newMfa.StatusCode);
        using var newAccount = await setupClient.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        var recovered = await newAccount.Content.ReadFromJsonAsync<AccountResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(recovered);
        Assert.True(recovered.MfaEnabled);
        Assert.True(recovered.OwnerAccess);
    }

    [Fact]
    public async Task RecoveryRollsBackIfAuditFailsAndConcurrentCommandsOnlyRecoverOnce()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        var request = new RecoverOwnerMfa.RecoveryRequest(RecoveryInstance, seeded.OwnerId, "operator-01", "ticket-02");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_recovery_audit() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'synthetic audit failure'; END; $$;
                CREATE TRIGGER reject_recovery_audit BEFORE INSERT ON "OwnerMfaRecoveryAudits"
                FOR EACH ROW EXECUTE FUNCTION reject_recovery_audit();
                """, TestContext.Current.CancellationToken);
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => RecoverAsync(app, request));
        await AssertOriginalStateAsync(app, seeded);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_recovery_audit ON "OwnerMfaRecoveryAudits";
                DROP FUNCTION reject_recovery_audit();
                """, TestContext.Current.CancellationToken);
        }
        var results = await Task.WhenAll(RecoverAsync(app, request),
            RecoverAsync(app, request with { RequestReference = "ticket-03" }));
        Assert.Single(results, result => result);
        using var finalScope = app.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await finalDb.OwnerMfaRecoveryAudits.CountAsync(TestContext.Current.CancellationToken));
        Assert.False((await finalDb.Users.SingleAsync(user => user.Id == seeded.OwnerId,
            TestContext.Current.CancellationToken)).TwoFactorEnabled);
    }

    private static Task<bool> RecoverAsync(WebApplicationFactory<Program> app, RecoverOwnerMfa.RecoveryRequest request) =>
        RecoverOwnerMfa.RecoverAsync(app.Services, request, TestContext.Current.CancellationToken);

    private static async Task<int> RunRecoveryCommandAsync(
        WebApplicationFactory<Program> app, RecoverOwnerMfa.RecoveryRequest request, bool confirmed)
    {
        var values = new Dictionary<string, string?>
        {
            ["APP_RECOVERY_INSTANCE_ID"] = request.InstanceId,
            ["APP_RECOVERY_OWNER_ID"] = request.OwnerId.ToString(),
            ["APP_RECOVERY_OPERATOR_REF"] = request.OperatorReference,
            ["APP_RECOVERY_REQUEST_REF"] = request.RequestReference,
            ["APP_RECOVERY_CONFIRM"] = confirmed ? $"{request.InstanceId}/{request.OwnerId}" : null
        };
        var previous = values.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (key, value) in values)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
            return await RecoverOwnerMfa.RunAsync(app.Services, TestContext.Current.CancellationToken);
        }
        finally
        {
            foreach (var (key, value) in previous)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
