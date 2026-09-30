using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;
using Server.Features.Identity;
using Server.Infrastructure;
using Xunit;

namespace Server.Tests;

public sealed partial class OwnerMfaTests
{
    private const string ResetPath = "/api/auth/reset-password";
    private static IssueOwnerPasswordReset.IssueRequest ResetIssue(RecoverySeed seed, string reference) =>
        new(RecoveryInstance, seed.OwnerId, "operator-01", reference);

    private static async Task<IssueOwnerPasswordReset.IssuedToken> IssueResetAsync(
        WebApplicationFactory<Program> app, IssueOwnerPasswordReset.IssueRequest request) =>
        Assert.IsType<IssueOwnerPasswordReset.IssuedToken>(await IssueOwnerPasswordReset.IssueAsync(
            app.Services, request, TestContext.Current.CancellationToken));

    private static async Task<HttpResponseMessage> ResetAsync(HttpClient client, string token,
        string password = NewPassword, string? confirm = null) => await PostAsync(client, ResetPath,
            new { token, newPassword = password, confirmPassword = confirm ?? password }, await GetCsrfAsync(client));

    [Fact]
    public async Task ResetIssuanceChecksTargetConfirmationAndReferenceAndWritesOnlyPrivateFile()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        var request = ResetIssue(seeded, "reset-01");
        foreach (var invalid in new[]
        {
            request with { InstanceId = "wrong-instance" }, request with { OwnerId = Guid.NewGuid() },
            request with { OwnerId = seeded.OtherUserId }, request with { OperatorReference = "email@example.test" },
            request with { RequestReference = "" }
        }) Assert.Null(await IssueOwnerPasswordReset.IssueAsync(app.Services, invalid, TestContext.Current.CancellationToken));

        var path = Path.Combine(Path.GetTempPath(), $"p02-synthetic-reset-{Guid.NewGuid():N}.txt");
        var variables = new Dictionary<string, string?>
        {
            ["APP_RESET_INSTANCE_ID"] = request.InstanceId,
            ["APP_RESET_OWNER_ID"] = request.OwnerId.ToString(),
            ["APP_RESET_OPERATOR_REF"] = request.OperatorReference,
            ["APP_RESET_REQUEST_REF"] = request.RequestReference,
            ["APP_RESET_CONFIRM"] = null,
            ["APP_RESET_OUTPUT_PATH"] = path
        };
        var previous = variables.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var (key, value) in variables) Environment.SetEnvironmentVariable(key, value);
            Assert.Equal(1, await IssueOwnerPasswordReset.RunAsync(app.Services, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(path));
            Environment.SetEnvironmentVariable("APP_RESET_CONFIRM", $"{request.InstanceId}/{request.OwnerId}");
            Assert.Equal(0, await IssueOwnerPasswordReset.RunAsync(app.Services, TestContext.Current.CancellationToken));
            Assert.False(string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            await Assert.ThrowsAsync<IOException>(() => IssueOwnerPasswordReset.RunAsync(app.Services, TestContext.Current.CancellationToken));
        }
        finally
        {
            foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key, value);
            if (File.Exists(path)) File.Delete(path);
        }
        Assert.Null(await IssueOwnerPasswordReset.IssueAsync(app.Services, request, TestContext.Current.CancellationToken));
        var sameReference = request with { RequestReference = "reset-concurrent" };
        var concurrent = await Task.WhenAll(
            IssueOwnerPasswordReset.IssueAsync(app.Services, sameReference, TestContext.Current.CancellationToken),
            IssueOwnerPasswordReset.IssueAsync(app.Services, sameReference, TestContext.Current.CancellationToken));
        Assert.Single(concurrent, token => token is not null);
        await AssertOriginalStateAsync(app, seeded);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.OwnerPasswordResetAudits.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResetRejectsTamperingExpirationWrongInstanceNonOwnerCsrfAndBadPasswords()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var clock = new ResetClock();
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString(), clock);
        await using var app = seeded.App;
        var issued = await IssueResetAsync(app, ResetIssue(seeded, "reset-validation"));
        Assert.Equal(TimeSpan.FromMinutes(30), issued.ExpiresAt - clock.GetUtcNow());
        using var client = app.CreateClient();
        using var noCsrf = await PostAsync(client, ResetPath,
            new { token = issued.Token, newPassword = NewPassword, confirmPassword = NewPassword }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        foreach (var invalid in new[] { "", "broken", issued.Token[..^20] + "changed", new string('x', 8193) })
        {
            using var denied = await ResetAsync(client, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        using var weak = await ResetAsync(client, issued.Token, "weak");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        using var mismatch = await ResetAsync(client, issued.Token, NewPassword, "different");
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        using var empty = await ResetAsync(client, issued.Token, "");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        await using (var otherInstance = app.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:InstanceId"] = "another-instance" }))))
        {
            using var otherClient = otherInstance.CreateClient();
            using var wrongInstance = await ResetAsync(otherClient, issued.Token);
            Assert.Equal(HttpStatusCode.BadRequest, wrongInstance.StatusCode);
        }
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var owner = Assert.IsType<AppUser>(await users.FindByIdAsync(seeded.OwnerId.ToString()));
            Assert.True((await users.RemoveFromRoleAsync(owner, "Owner")).Succeeded);
        }
        using var nonOwner = await ResetAsync(client, issued.Token);
        Assert.Equal(HttpStatusCode.BadRequest, nonOwner.StatusCode);
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var owner = Assert.IsType<AppUser>(await users.FindByIdAsync(seeded.OwnerId.ToString()));
            Assert.True((await users.AddToRoleAsync(owner, "Owner")).Succeeded);
        }
        clock.Advance(TimeSpan.FromMinutes(30));
        using var expired = await ResetAsync(client, issued.Token);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        using var limited = await ResetAsync(client, issued.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        using var finalScope = app.Services.CreateScope();
        Assert.Equal(1, await finalScope.ServiceProvider.GetRequiredService<AppDbContext>().OwnerPasswordResetAudits
            .CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResetRevokesAllSessionsAndTokensPreservesMfaAndRequiresNormalLogin()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var active = app.CreateClient();
        using var second = app.CreateClient();
        foreach (var session in new[] { active, second })
        {
            await PasswordStepAsync(session);
            await CompleteMfaAsync(session, seeded.Key);
        }
        using var pending = app.CreateClient();
        using var pendingRecovery = app.CreateClient();
        await PasswordStepAsync(pending);
        await PasswordStepAsync(pendingRecovery);
        var issued = await IssueResetAsync(app, ResetIssue(seeded, "reset-success"));
        var otherToken = await IssueResetAsync(app, ResetIssue(seeded, "reset-other"));
        using var resetClient = app.CreateClient();
        using var success = await ResetAsync(resetClient, issued.Token);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.True(success.Headers.CacheControl?.NoStore);
        foreach (var session in new[] { active, second, resetClient })
        {
            using var revoked = await session.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
        using var oldCode = await PostAsync(pending, "/api/auth/mfa/login",
            new { code = GenerateAuthenticatorCode(seeded.Key) }, await GetCsrfAsync(pending));
        Assert.Equal(HttpStatusCode.Unauthorized, oldCode.StatusCode);
        using var oldRecovery = await PostAsync(pendingRecovery, "/api/auth/mfa/recovery-login",
            new { code = seeded.Codes[0] }, await GetCsrfAsync(pendingRecovery));
        Assert.Equal(HttpStatusCode.Unauthorized, oldRecovery.StatusCode);
        using var reused = await ResetAsync(resetClient, issued.Token);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        using var oldToken = await ResetAsync(resetClient, otherToken.Token);
        Assert.Equal(HttpStatusCode.BadRequest, oldToken.StatusCode);
        using var oldPassword = await PostAsync(resetClient, "/api/auth/login",
            new { email = Email, password = Password }, await GetCsrfAsync(resetClient));
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);
        using var newPassword = await PostAsync(resetClient, "/api/auth/login",
            new { email = Email, password = NewPassword }, await GetCsrfAsync(resetClient));
        Assert.Equal(HttpStatusCode.Accepted, newPassword.StatusCode);
        using var passwordOnly = await resetClient.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, passwordOnly.StatusCode);
        await CompleteMfaAsync(resetClient, seeded.Key);
        using var ownerAccess = await resetClient.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, ownerAccess.StatusCode);
        await LogoutAsync(resetClient);
        using var recoveryLogin = await PostAsync(resetClient, "/api/auth/login",
            new { email = Email, password = NewPassword }, await GetCsrfAsync(resetClient));
        Assert.Equal(HttpStatusCode.Accepted, recoveryLogin.StatusCode);
        using var recoveryCode = await PostAsync(resetClient, "/api/auth/mfa/recovery-login",
            new { code = seeded.Codes[0] }, await GetCsrfAsync(resetClient));
        Assert.Equal(HttpStatusCode.NoContent, recoveryCode.StatusCode);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = Assert.IsType<AppUser>(await users.FindByIdAsync(seeded.OwnerId.ToString()));
        Assert.True(owner.TwoFactorEnabled);
        Assert.Equal(seeded.Key, await users.GetAuthenticatorKeyAsync(owner));
        Assert.Equal(7, await users.CountRecoveryCodesAsync(owner));
        Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<AppDbContext>().OwnerPasswordResetAudits
            .CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResetPreservesAccountLockoutAndFailureCount()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        DateTimeOffset lockoutEnd;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var owner = Assert.IsType<AppUser>(await users.FindByIdAsync(seeded.OwnerId.ToString()));
            Assert.True((await users.AccessFailedAsync(owner)).Succeeded);
            lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
            Assert.True((await users.SetLockoutEndDateAsync(owner, lockoutEnd)).Succeeded);
        }
        var issued = await IssueResetAsync(app, ResetIssue(seeded, "reset-locked"));
        using var client = app.CreateClient();
        var otherToken = await IssueResetAsync(app, ResetIssue(seeded, "reset-account-limit"));
        using (var limitScope = app.Services.CreateScope())
        {
            var limiter = limitScope.ServiceProvider.GetRequiredService<PartitionedRateLimiter<Guid>>();
            for (var attempt = 0; attempt < 10; attempt++)
            {
                using var lease = await limiter.AcquireAsync(seeded.OwnerId, cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(lease.IsAcquired);
            }
            using var accountLimited = await ResetAsync(client, otherToken.Token);
            Assert.Equal(HttpStatusCode.TooManyRequests, accountLimited.StatusCode);
        }
        // A new host has its own limiter window; the DB lockout must still survive reset.
        await using var freshHost = app.WithWebHostBuilder(_ => { });
        using var freshClient = freshHost.CreateClient();
        using var reset = await ResetAsync(freshClient, issued.Token);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using var finalScope = app.Services.CreateScope();
        var finalUsers = finalScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var updated = Assert.IsType<AppUser>(await finalUsers.FindByIdAsync(seeded.OwnerId.ToString()));
        Assert.True(await finalUsers.IsLockedOutAsync(updated));
        Assert.Equal(lockoutEnd.ToUnixTimeMilliseconds(), updated.LockoutEnd?.ToUnixTimeMilliseconds());
        Assert.Equal(1, updated.AccessFailedCount);
        Assert.True(await finalUsers.CheckPasswordAsync(updated, NewPassword));
    }

    [Fact]
    public async Task ResetRollsBackAuditAndCommitFailuresAndOnlyOneParallelResetSucceeds()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        var issued = await IssueResetAsync(app, ResetIssue(seeded, "reset-atomic"));
        using var first = app.CreateClient();
        using var second = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_reset_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic reset failure'; END; $$;
            CREATE TRIGGER reject_reset_audit BEFORE INSERT ON "OwnerPasswordResetAudits"
            FOR EACH ROW EXECUTE FUNCTION reject_reset_audit();
            """, TestContext.Current.CancellationToken);
        using var auditFailure = await ResetAsync(first, issued.Token);
        Assert.Equal(HttpStatusCode.InternalServerError, auditFailure.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        await Assert.ThrowsAsync<DbUpdateException>(() => IssueOwnerPasswordReset.IssueAsync(app.Services,
            ResetIssue(seeded, "reset-failed-issue"), TestContext.Current.CancellationToken));
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER reject_reset_audit ON "OwnerPasswordResetAudits";
            DROP FUNCTION reject_reset_audit();
            CREATE FUNCTION reject_reset_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic reset commit failure'; END; $$;
            CREATE CONSTRAINT TRIGGER reject_reset_commit AFTER UPDATE ON "AspNetUsers"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_reset_commit();
            """, TestContext.Current.CancellationToken);
        using var commitFailure = await ResetAsync(first, issued.Token);
        Assert.Equal(HttpStatusCode.InternalServerError, commitFailure.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        Assert.Equal(1, await db.OwnerPasswordResetAudits.CountAsync(TestContext.Current.CancellationToken));
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER reject_reset_commit ON "AspNetUsers";
            DROP FUNCTION reject_reset_commit();
            """, TestContext.Current.CancellationToken);
        var csrfFirst = await GetCsrfAsync(first);
        var csrfSecond = await GetCsrfAsync(second);
        await using var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {seeded.OwnerId} FOR UPDATE", TestContext.Current.CancellationToken);
        var body = new { token = issued.Token, newPassword = NewPassword, confirmPassword = NewPassword };
        var firstReset = PostAsync(first, ResetPath, body, csrfFirst);
        var secondReset = PostAsync(second, ResetPath, body, csrfSecond);
        using var monitorScope = app.Services.CreateScope();
        var monitor = monitorScope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var waitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await monitor.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")
            .SingleAsync(waitTimeout.Token) < 2) await Task.Delay(20, waitTimeout.Token);
        await blocker.CommitAsync(TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(firstReset, secondReset);
        try
        {
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(results, response => response.StatusCode == HttpStatusCode.BadRequest);
        }
        finally { foreach (var response in results) response.Dispose(); }
        Assert.Equal(2, await db.OwnerPasswordResetAudits.CountAsync(TestContext.Current.CancellationToken));
    }

    private sealed class ResetClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
