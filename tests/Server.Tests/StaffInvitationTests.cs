using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Identity;
using Server.Infrastructure;
using Xunit;

namespace Server.Tests;

public sealed partial class OwnerMfaTests
{
    private const string InvitePath = "/api/staff-invitations/";
    private const string AcceptInvitePath = "/api/staff-invitations/accept";
    private const string StaffEmail = "staff@example.test";

    private static async Task<HttpClient> InviteOwnerAsync(RecoverySeed seeded)
    {
        var client = seeded.App.CreateClient();
        await PasswordStepAsync(client);
        await CompleteMfaAsync(client, seeded.Key);
        return client;
    }
    private static async Task<StaffInvitationEndpoints.IssuedResponse> InviteAsync(HttpClient owner, string email = StaffEmail)
    {
        using var response = await PostAsync(owner, InvitePath, new { email, verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<StaffInvitationEndpoints.IssuedResponse>(await response.Content.ReadFromJsonAsync<StaffInvitationEndpoints.IssuedResponse>(TestContext.Current.CancellationToken));
    }
    private static Task<HttpResponseMessage> AcceptInviteAsync(HttpClient client, string token, string csrf, string email = StaffEmail, string password = Password) =>
        PostAsync(client, AcceptInvitePath, new { email, token, password, confirmPassword = password, role = "Owner" }, csrf);

    [Fact]
    public async Task InvitationRequiresMfaOwnerAndCsrfAndStaffOnlyGetsOwnSession()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var anonymous = app.CreateClient();
        using var denied = await PostAsync(anonymous, InvitePath, new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(anonymous));
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var setupOnly = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var setupOwner = new AppUser { Email = "setup@example.test", UserName = "setup@example.test", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(setupOwner, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(setupOwner, "Owner")).Succeeded);
        using var setupLogin = await PostAsync(setupOnly, "/api/auth/login", new { email = setupOwner.Email, password = Password }, await GetCsrfAsync(setupOnly));
        using var noMfa = await PostAsync(setupOnly, InvitePath, new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(setupOnly));
        Assert.Equal(HttpStatusCode.Forbidden, noMfa.StatusCode);
        using var owner = await InviteOwnerAsync(seeded);
        using var noCsrf = await PostAsync(owner, InvitePath, new { email = StaffEmail, verifiedRecipient = true }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var unverified = await PostAsync(owner, InvitePath, new { email = StaffEmail, verifiedRecipient = false }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.BadRequest, unverified.StatusCode);
        var issued = await InviteAsync(owner);
        Assert.Equal(43, issued.Token.Length);
        Assert.InRange(issued.ExpiresAt, DateTimeOffset.UtcNow.AddHours(24).AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(24));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invitation = await db.StaffInvitations.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(issued.Token, invitation.TokenHash);
        Assert.Null(invitation.AcceptedUserId);
        Assert.Null(await users.FindByEmailAsync(StaffEmail));
        using var list = await owner.GetAsync(InvitePath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(issued.Token, await list.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var acceptNoCsrf = await AcceptInviteAsync(anonymous, issued.Token, "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, acceptNoCsrf.StatusCode);
        using var accepted = await AcceptInviteAsync(anonymous, issued.Token, await GetCsrfAsync(anonymous));
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        using var noAutoLogin = await anonymous.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, noAutoLogin.StatusCode);
        var staff = await users.FindByEmailAsync(StaffEmail);
        Assert.NotNull(staff);
        Assert.Equal(new[] { "Staff" }, await users.GetRolesAsync(staff));
        using var login = await PostAsync(anonymous, "/api/auth/login", new { email = StaffEmail, password = Password }, await GetCsrfAsync(anonymous));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var me = await anonymous.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        var account = await me.Content.ReadFromJsonAsync<StaffAccount>(TestContext.Current.CancellationToken);
        Assert.NotNull(account);
        Assert.True(account.StaffAccess);
        Assert.False(account.OwnerAccess);
        using var staffList = await anonymous.GetAsync(InvitePath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, staffList.StatusCode);
        using var staffIssue = await PostAsync(anonymous, InvitePath, new { email = "other-staff@example.test", verifiedRecipient = true }, await GetCsrfAsync(anonymous));
        Assert.Equal(HttpStatusCode.Forbidden, staffIssue.StatusCode);
        using var mfa = await PostAsync(anonymous, "/api/auth/mfa/setup", new { password = Password }, await GetCsrfAsync(anonymous));
        Assert.Equal(HttpStatusCode.Forbidden, mfa.StatusCode);
        using var logout = await PostAsync(anonymous, "/api/auth/logout", new { }, await GetCsrfAsync(anonymous));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var after = await anonymous.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
        Assert.Equal(new[] { "Accepted", "Issued" }, await db.StaffInvitationAudits.OrderBy(entry => entry.Kind).Select(entry => entry.Kind).ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvitationIsEmailInstanceAndTimeBoundAndRevocationInvalidatesReissuedToken()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var owner = await InviteOwnerAsync(seeded);
        using var client = app.CreateClient();
        var csrf = await GetCsrfAsync(client);
        var issued = await InviteAsync(owner);
        using var duplicate = await PostAsync(owner, InvitePath, new { email = " STAFF@EXAMPLE.TEST ", verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var existing = await PostAsync(owner, InvitePath, new { email = Email, verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Conflict, existing.StatusCode);
        using var wrongEmail = await AcceptInviteAsync(client, issued.Token, csrf, "wrong@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, wrongEmail.StatusCode);
        using var tampered = await AcceptInviteAsync(client, new string('x', 43), csrf);
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        using var weak = await AcceptInviteAsync(client, issued.Token, csrf, password: "weak");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        await using var otherApp = app.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:InstanceId"] = "other-instance" })));
        using var other = otherApp.CreateClient();
        using var otherInstance = await AcceptInviteAsync(other, issued.Token, await GetCsrfAsync(other));
        Assert.Equal(HttpStatusCode.BadRequest, otherInstance.StatusCode);
        await using var expiredApp = app.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<TimeProvider>(new InviteClock(issued.ExpiresAt))));
        using var expired = expiredApp.CreateClient();
        using var atBoundary = await AcceptInviteAsync(expired, issued.Token, await GetCsrfAsync(expired));
        Assert.Equal(HttpStatusCode.BadRequest, atBoundary.StatusCode);
        using var missingCsrf = await PostAsync(owner, $"{InvitePath}{issued.Id}/revoke", new { }, null);
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        using var revoked = await PostAsync(owner, $"{InvitePath}{issued.Id}/revoke", new { }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        using var revokedAgain = await PostAsync(owner, $"{InvitePath}{issued.Id}/revoke", new { }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.NoContent, revokedAgain.StatusCode);
        using var old = await AcceptInviteAsync(client, issued.Token, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        var reissued = await InviteAsync(owner);
        Assert.Equal(issued.Id, reissued.Id);
        Assert.NotEqual(issued.Token, reissued.Token);
        using var oldAfterReissue = await AcceptInviteAsync(client, issued.Token, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, oldAfterReissue.StatusCode);
        using var accepted = await AcceptInviteAsync(client, reissued.Token, csrf, " STAFF@EXAMPLE.TEST ");
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        using var used = await AcceptInviteAsync(client, reissued.Token, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, used.StatusCode);
        using var acceptedRevoke = await PostAsync(owner, $"{InvitePath}{reissued.Id}/revoke", new { }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Conflict, acceptedRevoke.StatusCode);
    }

    [Fact]
    public async Task AuditOrCommitFailureRollsBackAccountRoleAndInvitation()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var owner = await InviteOwnerAsync(seeded);
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_invite_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic audit failure'; END; $$;
            CREATE TRIGGER reject_invite_audit BEFORE INSERT ON "StaffInvitationAudits"
            FOR EACH ROW EXECUTE FUNCTION reject_invite_audit();
            """, TestContext.Current.CancellationToken);
        using var failedIssue = await PostAsync(owner, InvitePath, new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.InternalServerError, failedIssue.StatusCode);
        Assert.False(await db.StaffInvitations.AnyAsync(TestContext.Current.CancellationToken));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_invite_audit ON \"StaffInvitationAudits\";", TestContext.Current.CancellationToken);
        var issued = await InviteAsync(owner);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_invite_audit BEFORE INSERT ON "StaffInvitationAudits"
            FOR EACH ROW EXECUTE FUNCTION reject_invite_audit();
            """, TestContext.Current.CancellationToken);
        var csrf = await GetCsrfAsync(client);
        using var failedAudit = await AcceptInviteAsync(client, issued.Token, csrf);
        Assert.Equal(HttpStatusCode.InternalServerError, failedAudit.StatusCode);
        Assert.Equal(2, await db.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Null((await db.StaffInvitations.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).AcceptedAt);
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER reject_invite_audit ON "StaffInvitationAudits";
            CREATE CONSTRAINT TRIGGER reject_invite_commit AFTER INSERT ON "StaffInvitationAudits"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_invite_audit();
            """, TestContext.Current.CancellationToken);
        using var failedCommit = await AcceptInviteAsync(client, issued.Token, csrf);
        Assert.Equal(HttpStatusCode.InternalServerError, failedCommit.StatusCode);
        Assert.Equal(2, await db.Users.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.StaffInvitationAudits.CountAsync(TestContext.Current.CancellationToken));
        Assert.Null((await db.StaffInvitations.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).AcceptedAt);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_invite_commit ON \"StaffInvitationAudits\";", TestContext.Current.CancellationToken);
        using var success = await AcceptInviteAsync(client, issued.Token, csrf);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
    }

    [Fact]
    public async Task ConcurrentIssuanceAcceptanceAndRevocationRemainAtomic()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var owner = await InviteOwnerAsync(seeded);
        using var anotherOwnerSession = await InviteOwnerAsync(seeded);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerCsrf = await GetCsrfAsync(owner);
        var anotherCsrf = await GetCsrfAsync(anotherOwnerSession);
        await using (var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {seeded.OwnerId} FOR UPDATE", TestContext.Current.CancellationToken);
            var body = new { email = StaffEmail, verifiedRecipient = true };
            var first = PostAsync(owner, InvitePath, body, ownerCsrf);
            var second = PostAsync(anotherOwnerSession, InvitePath, body, anotherCsrf);
            await AwaitInviteLocksAsync(app);
            await blocker.CommitAsync(TestContext.Current.CancellationToken);
            var responses = await Task.WhenAll(first, second);
            try
            {
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
                Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
                var issued = Assert.IsType<StaffInvitationEndpoints.IssuedResponse>(await responses.Single(response => response.IsSuccessStatusCode).Content.ReadFromJsonAsync<StaffInvitationEndpoints.IssuedResponse>(TestContext.Current.CancellationToken));
                using var client = app.CreateClient();
                using var otherClient = app.CreateClient();
                var csrf = await GetCsrfAsync(client);
                var otherCsrf = await GetCsrfAsync(otherClient);
                await using var acceptBlocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"StaffInvitations\" WHERE \"Id\" = {issued.Id} FOR UPDATE", TestContext.Current.CancellationToken);
                var firstAccept = AcceptInviteAsync(client, issued.Token, csrf);
                var secondAccept = AcceptInviteAsync(otherClient, issued.Token, otherCsrf);
                await AwaitInviteLocksAsync(app);
                await acceptBlocker.CommitAsync(TestContext.Current.CancellationToken);
                using var firstResult = await firstAccept;
                using var secondResult = await secondAccept;
                Assert.Equal(new[] { HttpStatusCode.NoContent, HttpStatusCode.BadRequest }, new[] { firstResult.StatusCode, secondResult.StatusCode }.OrderBy(value => value));
            }
            finally { foreach (var response in responses) response.Dispose(); }
        }
        var raceInvite = await InviteAsync(owner, "race@example.test");
        using var raceClient = app.CreateClient();
        var raceCsrf = await GetCsrfAsync(raceClient);
        await using var raceBlocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"StaffInvitations\" WHERE \"Id\" = {raceInvite.Id} FOR UPDATE", TestContext.Current.CancellationToken);
        var accept = AcceptInviteAsync(raceClient, raceInvite.Token, raceCsrf, "race@example.test");
        var revoke = PostAsync(owner, $"{InvitePath}{raceInvite.Id}/revoke", new { }, ownerCsrf);
        await AwaitInviteLocksAsync(app);
        await raceBlocker.CommitAsync(TestContext.Current.CancellationToken);
        using var acceptedResult = await accept;
        using var revokedResult = await revoke;
        Assert.True((acceptedResult.StatusCode == HttpStatusCode.NoContent && revokedResult.StatusCode == HttpStatusCode.Conflict) ||
            (acceptedResult.StatusCode == HttpStatusCode.BadRequest && revokedResult.StatusCode == HttpStatusCode.NoContent));
        var final = await db.StaffInvitations.AsNoTracking().SingleAsync(entry => entry.Id == raceInvite.Id, TestContext.Current.CancellationToken);
        Assert.True((final.AcceptedAt is not null) != (final.RevokedAt is not null));
        Assert.Equal(1, await db.Users.CountAsync(user => user.Email == StaffEmail, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvitationHasIssuerInvitationAndIpRateLimits()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var owner = await InviteOwnerAsync(seeded);
        var issued = await InviteAsync(owner);
        var limiter = app.Services.GetRequiredService<PartitionedRateLimiter<Guid>>();
        for (var attempt = 0; attempt < 9; attempt++) { using var lease = await limiter.AcquireAsync(seeded.OwnerId, cancellationToken: TestContext.Current.CancellationToken); Assert.True(lease.IsAcquired); }
        using var issuerLimited = await PostAsync(owner, InvitePath, new { email = "limited@example.test", verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.TooManyRequests, issuerLimited.StatusCode);
        for (var attempt = 0; attempt < 10; attempt++) { using var lease = await limiter.AcquireAsync(issued.Id, cancellationToken: TestContext.Current.CancellationToken); Assert.True(lease.IsAcquired); }
        using var client = app.CreateClient();
        var csrf = await GetCsrfAsync(client);
        using var accountLimited = await AcceptInviteAsync(client, issued.Token, csrf);
        Assert.Equal(HttpStatusCode.TooManyRequests, accountLimited.StatusCode);
        for (var attempt = 0; attempt < 9; attempt++) { using var bad = await AcceptInviteAsync(client, "bad", csrf); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); }
        using var ipLimited = await AcceptInviteAsync(client, "bad", csrf);
        Assert.Equal(HttpStatusCode.TooManyRequests, ipLimited.StatusCode);
    }

    private static async Task AwaitInviteLocksAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")
            .SingleAsync(timeout.Token) < 2) await Task.Delay(20, timeout.Token);
    }

    [Fact]
    public async Task OwnerSessionRevokedWhileWaitingCannotIssueOrRevokeInvitation()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var first = await InviteOwnerAsync(seeded);
        using var second = await InviteOwnerAsync(seeded);
        var issued = await InviteAsync(first);
        var firstCsrf = await GetCsrfAsync(first);
        var secondCsrf = await GetCsrfAsync(second);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var newStamp = Guid.NewGuid().ToString();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AspNetUsers\" SET \"SecurityStamp\" = {newStamp} WHERE \"Id\" = {seeded.OwnerId}", TestContext.Current.CancellationToken);
        var issue = PostAsync(first, InvitePath, new { email = "late@example.test", verifiedRecipient = true }, firstCsrf);
        var revoke = PostAsync(second, $"{InvitePath}{issued.Id}/revoke", new { }, secondCsrf);
        await AwaitInviteLocksAsync(app);
        await blocker.CommitAsync(TestContext.Current.CancellationToken);
        using var issuedResult = await issue;
        using var revokedResult = await revoke;
        Assert.Equal(HttpStatusCode.Unauthorized, issuedResult.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedResult.StatusCode);
        Assert.Equal(1, await db.StaffInvitationAudits.CountAsync(TestContext.Current.CancellationToken));
        Assert.Null((await db.StaffInvitations.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).RevokedAt);
    }
    private sealed record StaffAccount(bool StaffAccess, bool OwnerAccess);
    private sealed class InviteClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
