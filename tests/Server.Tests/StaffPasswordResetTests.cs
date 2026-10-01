using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Identity;
using Server.Infrastructure;
using Xunit;

namespace Server.Tests;

public sealed partial class OwnerMfaTests
{
    private const string StaffResetIssuePath = "/api/staff-password-resets/";
    private const string StaffResetCompletePath = "/api/staff-password-resets/complete";

    private static async Task<StaffPasswordResetEndpoints.IssuedResponse> IssueStaffResetAsync(HttpClient owner)
    {
        using var response = await PostAsync(owner, StaffResetIssuePath,
            new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<StaffPasswordResetEndpoints.IssuedResponse>(
            await response.Content.ReadFromJsonAsync<StaffPasswordResetEndpoints.IssuedResponse>(TestContext.Current.CancellationToken));
    }

    private static Task<HttpResponseMessage> CompleteStaffResetAsync(HttpClient client, string token,
        string password = NewPassword, string? confirm = null) => PostStaffResetAsync(client, token, password, confirm);

    private static async Task<HttpResponseMessage> PostStaffResetAsync(HttpClient client, string token,
        string password, string? confirm) => await PostAsync(client, StaffResetCompletePath,
            new { token, newPassword = password, confirmPassword = confirm ?? password }, await GetCsrfAsync(client));

    [Fact]
    public async Task StaffResetIssuanceRequiresMfaOwnerCsrfVerifiedExistingStaffAndDoesNotChangeAccount()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        var staffId = await CreatePasswordStaffAsync(app);
        using var client = app.CreateClient();
        using var anonymous = await PostAsync(client, StaffResetIssuePath,
            new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await PasswordStepAsync(client);
        using var pending = await PostAsync(client, StaffResetIssuePath,
            new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.Unauthorized, pending.StatusCode);
        await CompleteMfaAsync(client, seeded.Key);
        using var noCsrf = await PostAsync(client, StaffResetIssuePath, new { email = StaffEmail, verifiedRecipient = true }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        foreach (var request in new[]
        {
            new { email = StaffEmail, verifiedRecipient = false },
            new { email = "invalid", verifiedRecipient = true },
            new { email = Email, verifiedRecipient = true },
            new { email = "missing@example.test", verifiedRecipient = true }
        })
        {
            using var denied = await PostAsync(client, StaffResetIssuePath, request, await GetCsrfAsync(client));
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        using var staffSession = app.CreateClient();
        await LoginPasswordStaffAsync(staffSession);
        using var forbidden = await PostAsync(staffSession, StaffResetIssuePath,
            new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(staffSession));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var issued = await IssueStaffResetAsync(client);
        await AssertStaffPasswordAsync(app, staffId, Password);
        using var intact = await staffSession.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, intact.StatusCode);
        await AssertOriginalStateAsync(app, seeded);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.StaffPasswordResetAudits.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(staffId, audit.StaffId);
        Assert.Equal(seeded.OwnerId, audit.ActorId);
        Assert.Equal("Issued", audit.Kind);
        Assert.Equal(TimeSpan.FromMinutes(30), audit.ExpiresAt - audit.OccurredAt);
        Assert.NotEqual(issued.Token, audit.GrantId.ToString());
        var entity = db.Model.FindEntityType(typeof(StaffPasswordResetAudit));
        Assert.NotNull(entity);
        Assert.DoesNotContain("Token", string.Join(',', entity.GetProperties().Select(property => property.Name)));
    }

    [Fact]
    public async Task StaffResetRejectsInvalidTokensCsrfPasswordsWrongInstanceOwnerCodesAndExpiration()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var clock = new ResetClock();
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString(), clock);
        await using var app = seeded.App;
        var staffId = await CreatePasswordStaffAsync(app);
        using var owner = await InviteOwnerAsync(seeded);
        var issued = await IssueStaffResetAsync(owner);
        using var client = app.CreateClient();
        using var noCsrf = await PostAsync(client, StaffResetCompletePath,
            new { token = issued.Token, newPassword = NewPassword, confirmPassword = NewPassword }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var weak = await CompleteStaffResetAsync(client, issued.Token, "weak");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        using var mismatch = await CompleteStaffResetAsync(client, issued.Token, NewPassword, "different");
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        using var tampered = await CompleteStaffResetAsync(client, issued.Token + "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        using var empty = await CompleteStaffResetAsync(client, "");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var ownerLoggedIn = await CompleteStaffResetAsync(owner, issued.Token);
        Assert.Equal(HttpStatusCode.Conflict, ownerLoggedIn.StatusCode);
        var ownerCode = await IssueResetAsync(app, ResetIssue(seeded, "staff-cross-purpose"));
        using var crossPurpose = await CompleteStaffResetAsync(client, ownerCode.Token);
        Assert.Equal(HttpStatusCode.BadRequest, crossPurpose.StatusCode);
        await using var otherApp = app.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:InstanceId"] = "other-staff-reset-instance" })));
        using var otherClient = otherApp.CreateClient();
        using var wrongInstance = await CompleteStaffResetAsync(otherClient, issued.Token);
        Assert.Equal(HttpStatusCode.BadRequest, wrongInstance.StatusCode);
        clock.Advance(TimeSpan.FromMinutes(30));
        using var expired = await CompleteStaffResetAsync(client, issued.Token);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        await AssertStaffPasswordAsync(app, staffId, Password);
    }

    [Fact]
    public async Task StaffResetRevokesAllOldSessionsAndCodesAndPreservesOwnerAndOtherStaff()
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
        var issued = await IssueStaffResetAsync(owner);
        var otherCode = await IssueStaffResetAsync(owner);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var other = new AppUser { Email = "second-staff@example.test", UserName = "second-staff@example.test", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(other, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(other, "Staff")).Succeeded);
        using var otherSession = app.CreateClient();
        using var otherLogin = await PostAsync(otherSession, "/api/auth/login",
            new { email = other.Email, password = Password }, await GetCsrfAsync(otherSession));
        Assert.Equal(HttpStatusCode.NoContent, otherLogin.StatusCode);
        using var reset = app.CreateClient();
        using var success = await CompleteStaffResetAsync(reset, issued.Token);
        Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.True(success.Headers.CacheControl?.NoStore);
        foreach (var session in new[] { first, second, reset })
        {
            using var revoked = await session.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        }
        foreach (var code in new[] { issued.Token, otherCode.Token })
        {
            using var reused = await CompleteStaffResetAsync(reset, code);
            Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        }
        using var oldLogin = await PostAsync(reset, "/api/auth/login",
            new { email = StaffEmail, password = Password }, await GetCsrfAsync(reset));
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        await LoginPasswordStaffAsync(reset, NewPassword);
        using var staffMe = await reset.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, staffMe.StatusCode);
        foreach (var session in new[] { owner, otherSession })
        {
            using var intact = await session.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, intact.StatusCode);
        }
        await AssertOriginalStateAsync(app, seeded);
        await AssertStaffPasswordAsync(app, staffId, NewPassword);
        Assert.True(await users.CheckPasswordAsync(other, Password));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.StaffPasswordResetAudits.CountAsync(entry => entry.Kind == "Issued", TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.StaffPasswordResetAudits.CountAsync(entry => entry.Kind == "Completed" && entry.ActorId == staffId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StaffResetRollsBackAuditAndCommitFailureAndSerializesConcurrentCompletion()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        var staffId = await CreatePasswordStaffAsync(app);
        using var owner = await InviteOwnerAsync(seeded);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_staff_reset() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic reset failure'; END; $$;
            CREATE TRIGGER reject_staff_reset BEFORE INSERT ON "StaffPasswordResetAudits"
            FOR EACH ROW EXECUTE FUNCTION reject_staff_reset();
            """, TestContext.Current.CancellationToken);
        using var failedIssue = await PostAsync(owner, StaffResetIssuePath,
            new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.InternalServerError, failedIssue.StatusCode);
        Assert.False(await db.StaffPasswordResetAudits.AnyAsync(TestContext.Current.CancellationToken));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_staff_reset ON \"StaffPasswordResetAudits\"", TestContext.Current.CancellationToken);
        var issued = await IssueStaffResetAsync(owner);
        using var first = app.CreateClient();
        using var second = app.CreateClient();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_staff_reset BEFORE INSERT ON "StaffPasswordResetAudits"
            FOR EACH ROW EXECUTE FUNCTION reject_staff_reset();
            """, TestContext.Current.CancellationToken);
        using var failedAudit = await CompleteStaffResetAsync(first, issued.Token);
        Assert.Equal(HttpStatusCode.InternalServerError, failedAudit.StatusCode);
        await AssertStaffPasswordAsync(app, staffId, Password);
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER reject_staff_reset ON "StaffPasswordResetAudits";
            CREATE CONSTRAINT TRIGGER reject_staff_reset AFTER INSERT ON "StaffPasswordResetAudits"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_staff_reset();
            """, TestContext.Current.CancellationToken);
        using var failedCommit = await CompleteStaffResetAsync(first, issued.Token);
        Assert.Equal(HttpStatusCode.InternalServerError, failedCommit.StatusCode);
        await AssertStaffPasswordAsync(app, staffId, Password);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_staff_reset ON \"StaffPasswordResetAudits\"", TestContext.Current.CancellationToken);
        var firstCsrf = await GetCsrfAsync(first);
        var secondCsrf = await GetCsrfAsync(second);
        await using var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {staffId} FOR UPDATE", TestContext.Current.CancellationToken);
        var body = new { token = issued.Token, newPassword = NewPassword, confirmPassword = NewPassword };
        var firstReset = PostAsync(first, StaffResetCompletePath, body, firstCsrf);
        var secondReset = PostAsync(second, StaffResetCompletePath, body, secondCsrf);
        using var monitorScope = app.Services.CreateScope();
        var monitor = monitorScope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await monitor.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")
            .SingleAsync(timeout.Token) < 2) await Task.Delay(20, timeout.Token);
        await blocker.CommitAsync(TestContext.Current.CancellationToken);
        var responses = await Task.WhenAll(firstReset, secondReset);
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await AssertStaffPasswordAsync(app, staffId, NewPassword);
        Assert.Equal(2, await db.StaffPasswordResetAudits.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StaffResetRejectsRevokedWaitingOwnerAndChangedTargetRole()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        var staffId = await CreatePasswordStaffAsync(app);
        using var first = await InviteOwnerAsync(seeded);
        using var second = await InviteOwnerAsync(seeded);
        var issued = await IssueStaffResetAsync(first);
        var firstCsrf = await GetCsrfAsync(first);
        var secondCsrf = await GetCsrfAsync(second);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using (var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var newStamp = Guid.NewGuid().ToString();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"AspNetUsers\" SET \"SecurityStamp\" = {newStamp} WHERE \"Id\" = {seeded.OwnerId}", TestContext.Current.CancellationToken);
            var body = new { email = StaffEmail, verifiedRecipient = true };
            var one = PostAsync(first, StaffResetIssuePath, body, firstCsrf);
            var two = PostAsync(second, StaffResetIssuePath, body, secondCsrf);
            await AwaitInviteLocksAsync(app);
            await blocker.CommitAsync(TestContext.Current.CancellationToken);
            using var oneResult = await one;
            using var twoResult = await two;
            Assert.Equal(HttpStatusCode.Unauthorized, oneResult.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, twoResult.StatusCode);
        }
        using var freshOwner = await InviteOwnerAsync(seeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var staff = Assert.IsType<AppUser>(await users.FindByIdAsync(staffId.ToString()));
        Assert.True((await users.AddToRoleAsync(staff, "Owner")).Succeeded);
        using var dualRoleIssue = await PostAsync(freshOwner, StaffResetIssuePath,
            new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(freshOwner));
        Assert.Equal(HttpStatusCode.BadRequest, dualRoleIssue.StatusCode);
        using var anonymous = app.CreateClient();
        using var dualRoleComplete = await CompleteStaffResetAsync(anonymous, issued.Token);
        Assert.Equal(HttpStatusCode.BadRequest, dualRoleComplete.StatusCode);
        using var freshScope = app.Services.CreateScope();
        var freshUsers = freshScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var unchanged = Assert.IsType<AppUser>(await freshUsers.FindByIdAsync(staffId.ToString()));
        Assert.True(await freshUsers.CheckPasswordAsync(unchanged, Password));
        Assert.Equal(2, (await freshUsers.GetRolesAsync(unchanged)).Count);
        Assert.Single(await db.StaffPasswordResetAudits.ToListAsync(TestContext.Current.CancellationToken));
        var limiter = app.Services.GetRequiredService<PartitionedRateLimiter<Guid>>();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var lease = await limiter.AcquireAsync(seeded.OwnerId, cancellationToken: TestContext.Current.CancellationToken);
        }
        using var issuerLimited = await PostAsync(freshOwner, StaffResetIssuePath,
            new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(freshOwner));
        Assert.Equal(HttpStatusCode.TooManyRequests, issuerLimited.StatusCode);
    }

    [Fact]
    public async Task StaffResetPreservesLockoutAndEnforcesAccountAndEndpointLimits()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        var staffId = await CreatePasswordStaffAsync(app);
        using var owner = await InviteOwnerAsync(seeded);
        var issued = await IssueStaffResetAsync(owner);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var staff = Assert.IsType<AppUser>(await users.FindByIdAsync(staffId.ToString()));
        Assert.True((await users.AccessFailedAsync(staff)).Succeeded);
        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        Assert.True((await users.SetLockoutEndDateAsync(staff, lockoutEnd)).Succeeded);
        using var client = app.CreateClient();
        using var completed = await CompleteStaffResetAsync(client, issued.Token);
        Assert.Equal(HttpStatusCode.NoContent, completed.StatusCode);
        using var finalScope = app.Services.CreateScope();
        var freshUsers = finalScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var fresh = Assert.IsType<AppUser>(await freshUsers.FindByIdAsync(staffId.ToString()));
        Assert.Equal(lockoutEnd.ToUnixTimeMilliseconds(), fresh.LockoutEnd?.ToUnixTimeMilliseconds());
        Assert.Equal(1, fresh.AccessFailedCount);
        var limiter = scope.ServiceProvider.GetRequiredService<PartitionedRateLimiter<Guid>>();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var lease = await limiter.AcquireAsync(staffId, cancellationToken: TestContext.Current.CancellationToken);
        }
        using var limited = await CompleteStaffResetAsync(client, issued.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        for (var attempt = 2; attempt < 10; attempt++)
        {
            using var invalid = await CompleteStaffResetAsync(client, "invalid");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var endpointLimited = await CompleteStaffResetAsync(client, "invalid");
        Assert.Equal(HttpStatusCode.TooManyRequests, endpointLimited.StatusCode);
    }
}
