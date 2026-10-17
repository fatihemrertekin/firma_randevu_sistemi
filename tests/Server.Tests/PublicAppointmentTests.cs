using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Scheduling;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.TestAccounts;
using Fixture = Server.Tests.BookingVerificationTests.Fixture;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class PublicAppointmentTests
{
    private sealed class LostResponse : IStartupFilter
    {
        private int dropped;
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, proceed) =>
            {
                context.Response.OnStarting(() =>
                {
                    if (context.Request.Path == Path && context.Response.StatusCode == 201 && Interlocked.Exchange(ref dropped, 1) == 0)
                        throw new IOException("Synthetic response failure after commit.");
                    return Task.CompletedTask;
                });
                await proceed(context);
            });
            next(app);
        };
    }
    private const string Path = "/api/public-booking/appointments";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static PublicAppointmentEndpoints.Request Create(BookingVerificationEndpoints.Request intent, Guid id, string secret) =>
        new(intent.RequestKey, id, secret, intent.ServiceId, intent.ServiceVersion, intent.PolicyVersion,
            intent.StaffMemberId, intent.StartsAt, intent.CustomerName, intent.CustomerPhone);
    private static async Task<HttpResponseMessage> Post(HttpClient client, object body) =>
        await PostAsync(client, Path, body, await GetCsrfAsync(client));
    private static async Task<PublicAppointmentEndpoints.Response> Read(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(response.Headers.Location);
        var json = await response.Content.ReadAsStringAsync(Token);
        foreach (var field in new[] { "customerName", "customerPhone", "requestHash", "secret", "actorId" }) Assert.DoesNotContain(field, json, StringComparison.OrdinalIgnoreCase);
        return Assert.IsType<PublicAppointmentEndpoints.Response>(await response.Content.ReadFromJsonAsync<PublicAppointmentEndpoints.Response>(Token));
    }
    internal static async Task<PublicAppointmentEndpoints.Request> Verified(Fixture f, HttpClient client)
    {
        var intent = f.Request();
        using var requested = await f.Post(client, "request", intent); Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var challenge = Assert.IsType<BookingVerificationEndpoints.Requested>(await requested.Content.ReadFromJsonAsync<BookingVerificationEndpoints.Requested>(Token));
        await f.Work();
        using var verified = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, f.Sms.Calls.Last().Code));
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        return Create(intent, challenge.ChallengeId, challenge.ChallengeSecret);
    }
    // Yarış testleri OTP gönderim kotasını tüketmeden, gerçek doğrulama sonrası oluşan kayıt biçimini kullanır.
    internal static async Task<PublicAppointmentEndpoints.Request> SeedVerified(Fixture f, BookingVerificationEndpoints.Request? intent = null)
    {
        intent ??= f.Request();
        var security = f.App.Services.GetRequiredService<BookingVerificationSecurity>();
        var name = intent.CustomerName?.Trim() ?? "";
        var phone = "+90" + intent.CustomerPhone?.TrimStart('0');
        var start = DateTimeOffset.Parse(Assert.IsType<string>(intent.StartsAt), CultureInfo.InvariantCulture).UtcDateTime;
        var secret = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var id = Guid.NewGuid();
        await using var db = f.Db();
        db.BookingVerifications.Add(new()
        {
            Id = id,
            RequestKey = intent.RequestKey,
            RequestHash = security.Hash("request-v1", intent.ServiceId, intent.ServiceVersion,
                intent.PolicyVersion, intent.StaffMemberId, start, name, phone),
            SecretHash = BookingVerificationSecurity.SecretHash(secret),
            PhoneHash = security.Hash("phone", phone),
            IpHash = security.Hash("ip", "test"),
            CreatedAt = f.Clock.Now,
            ExpiresAt = f.Clock.Now.AddMinutes(5),
            ResendAt = f.Clock.Now.AddMinutes(1),
            VerifiedAt = f.Clock.Now,
            VerifiedUntil = f.Clock.Now.AddMinutes(5),
            UpdatedAt = f.Clock.Now
        });
        await db.SaveChangesAsync(Token); return Create(intent, id, secret);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealVerificationCreatesOneSnapshotAndReplayReturnsCurrentStatusWithoutLeakingContact(bool approval)
    {
        await using var f = new Fixture(); await f.StartAsync();
        await using (var db = f.Db())
        {
            (await db.BookingPolicies.SingleAsync(Token)).RequiresBusinessApproval = approval;
            var service = await db.ServiceDefinitions.SingleAsync(Token); service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10;
            await db.SaveChangesAsync(Token);
        }
        using var client = f.Client(); var request = await Verified(f, client);
        await using (var db = f.Db()) Assert.Empty(await db.Appointments.ToArrayAsync(Token));
        using var created = await Post(client, request); var result = await Read(created, HttpStatusCode.Created);
        Assert.Equal(approval ? "Pending" : "Confirmed", result.Status); Assert.Equal("100.00", result.Price);
        Assert.Equal(f.PolicyVersion, result.PolicyVersion); Assert.Equal(120, result.CustomerCancellationLeadMinutes);
        Assert.Equal(result.StartsAt.AddMinutes(-120), result.CancellationDeadline);
        Assert.Equal(approval ? f.Clock.Now.UtcDateTime.AddMinutes(30) : (DateTime?)null, result.PendingExpiresAt);
        await using (var db = f.Db())
        {
            var row = await db.Appointments.SingleAsync(Token); Assert.Null(row.ActorId); Assert.Equal("Public", row.Source);
            Assert.Equal(f.PolicyVersion, row.BookingPolicyVersion); Assert.Equal(approval, row.RequiresBusinessApproval);
            var block = await db.AppointmentTimeBlocks.SingleAsync(Token); Assert.Equal(result.StartsAt.AddMinutes(-5), block.OccupiedStartsAt);
            Assert.Equal(result.EndsAt.AddMinutes(10), block.OccupiedEndsAt);
            var audit = await db.AppointmentAudits.SingleAsync(Token); Assert.Equal("PublicCreated", audit.Kind); Assert.Null(audit.ActorId);
            var receipt = await db.BookingVerifications.SingleAsync(Token); Assert.Equal(row.Id, receipt.AppointmentId);
            Assert.Equal(receipt.ConsumedAt?.AddHours(24), receipt.ReplayUntil);
            var policy = await db.BookingPolicies.SingleAsync(Token); policy.Version = Guid.NewGuid(); policy.CustomerCancellationLeadMinutes = 0;
            block.Status = "Cancelled"; row.Version = Guid.NewGuid(); await db.SaveChangesAsync(Token);
        }
        f.Clock.Now += TimeSpan.FromMinutes(6);
        using var replay = await Post(client, request); var current = await Read(replay, HttpStatusCode.OK);
        Assert.Equal(result.Id, current.Id); Assert.Equal("Cancelled", current.Status); Assert.NotEqual(result.Version, current.Version);
        Assert.Equal(f.PolicyVersion, current.PolicyVersion); Assert.Equal(120, current.CustomerCancellationLeadMinutes);
        using var verifyAgain = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(request.ChallengeId, request.ChallengeSecret, null));
        Assert.Equal(HttpStatusCode.BadRequest, verifyAgain.StatusCode);
        using var resend = await f.Post(client, "resend", new BookingVerificationEndpoints.Resend(request.ChallengeId, request.ChallengeSecret, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, resend.StatusCode);
        Assert.Single(f.Sms.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwentyParallelCreatesHaveOneWinnerAndSingleConsumption(bool differentChallenges)
    {
        await using var f = new Fixture(); await f.StartAsync();
        var request = await SeedVerified(f); var requests = new List<PublicAppointmentEndpoints.Request> { request };
        for (var i = 1; i < 20; i++) requests.Add(differentChallenges ? await SeedVerified(f) : request);
        using var client = f.Client(); var csrf = await GetCsrfAsync(client);
        var responses = await Task.WhenAll(requests.Select(body => PostAsync(client, Path, body, csrf)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Equal(19, responses.Count(x => x.StatusCode == (differentChallenges ? HttpStatusCode.Conflict : HttpStatusCode.OK)));
        foreach (var response in responses) response.Dispose();
        await using var db = f.Db(); Assert.Single(await db.Appointments.ToArrayAsync(Token));
        Assert.Single(await db.AppointmentTimeBlocks.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
        Assert.Equal(1, await db.BookingVerifications.CountAsync(x => x.ConsumedAt != null, Token));
    }

    [Fact]
    public async Task SecretIntentAndDeadlineFailuresDoNotConsumeOrRevealRecords()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var request = await SeedVerified(f);
        foreach (var body in new[] { request with { ChallengeSecret = new string('a', 43) }, request with { ChallengeId = Guid.NewGuid() } })
        { using var response = await Post(client, body); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode); }
        foreach (var body in new[] { request with { RequestKey = Guid.NewGuid() }, request with { CustomerName = "Başka kişi" },
            request with { CustomerPhone = "05000000002" }, request with { StartsAt = "2026-10-18T13:00:00+03:00" },
            request with { PolicyVersion = Guid.NewGuid() }, request with { ServiceVersion = Guid.NewGuid() } })
        { using var response = await Post(client, body); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); }
        f.Clock.Now += TimeSpan.FromMinutes(5);
        using var expired = await Post(client, request); Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        await using var db = f.Db(); Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Null((await db.BookingVerifications.SingleAsync(Token)).ConsumedAt);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("service")]
    [InlineData("hours")]
    [InlineData("assignment")]
    [InlineData("inactive")]
    [InlineData("missing")]
    public async Task ChangedRulesAreRecheckedAndFailedWritePreservesGrant(string change)
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var request = await SeedVerified(f);
        await using (var db = f.Db())
        {
            if (change == "policy") (await db.BookingPolicies.SingleAsync(Token)).Version = Guid.NewGuid();
            if (change == "service") (await db.ServiceDefinitions.SingleAsync(Token)).Version = Guid.NewGuid();
            if (change == "hours") (await db.BusinessHoursSchedules.SingleAsync(Token)).IsConfigured = false;
            if (change == "assignment") db.StaffServiceAssignments.Remove(await db.StaffServiceAssignments.SingleAsync(Token));
            if (change == "inactive") (await db.StaffMembers.SingleAsync(Token)).IsActive = false;
            if (change == "missing")
            {
                var service = await db.ServiceDefinitions.SingleAsync(Token); service.IsDeleted = true; service.IsActive = false;
            }
            await db.SaveChangesAsync(Token);
        }
        using var response = await Post(client, request); Assert.Equal(change == "missing" ? HttpStatusCode.NotFound : HttpStatusCode.Conflict, response.StatusCode);
        await using var final = f.Db(); Assert.Empty(await final.Appointments.ToArrayAsync(Token)); Assert.Empty(await final.AppointmentAudits.ToArrayAsync(Token));
        Assert.Null((await final.BookingVerifications.SingleAsync(Token)).AppointmentId);
    }

    [Fact]
    public async Task CommitFailureRollsBackAppointmentBlockAuditAndConsumptionAndRetrySucceeds()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var request = await SeedVerified(f);
        await using (var db = f.Db()) await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_public_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic audit failure'; END $$;
            CREATE TRIGGER reject_public_audit BEFORE INSERT ON "AppointmentAudits" FOR EACH ROW EXECUTE FUNCTION reject_public_audit();
            """, Token);
        using (var failed = await Post(client, request)) Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using (var db = f.Db())
        {
            Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.AppointmentTimeBlocks.ToArrayAsync(Token));
            Assert.Empty(await db.AppointmentAudits.ToArrayAsync(Token)); Assert.Null((await db.BookingVerifications.SingleAsync(Token)).AppointmentId);
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_public_audit ON \"AppointmentAudits\"; DROP FUNCTION reject_public_audit();", Token);
        }
        using var retry = await Post(client, request); await Read(retry, HttpStatusCode.Created);
    }

    [Fact]
    public async Task ResponseFailureAfterCommitCanOnlyReplayThePersistedAppointment()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var request = await SeedVerified(f);
        await using var broken = f.App.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, LostResponse>()));
        using var connection = broken.CreateClient(); connection.DefaultRequestHeaders.Add("Origin", "http://localhost");
        await Assert.ThrowsAsync<IOException>(() => Post(connection, request));
        await using (var db = f.Db()) Assert.Single(await db.Appointments.ToArrayAsync(Token));
        using var replay = await Post(client, request); await Read(replay, HttpStatusCode.OK);
        await using var final = f.Db(); Assert.Single(await final.Appointments.ToArrayAsync(Token)); Assert.Single(await final.AppointmentAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task LostResponseAndRestartKeepReceiptUntilExactDeadlineAndCleanupCannotRecreateBooking()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var request = await Verified(f, client);
        using (var lost = await Post(client, request)) Assert.Equal(HttpStatusCode.Created, lost.StatusCode);
        Guid id;
        await using (var db = f.Db()) id = (await db.Appointments.SingleAsync(Token)).Id;
        await f.RestartAsync(); using var restored = f.Client();
        f.Clock.Now += TimeSpan.FromHours(24) - TimeSpan.FromTicks(10);
        await f.Work(); using var replay = await Post(restored, request); Assert.Equal(id, (await Read(replay, HttpStatusCode.OK)).Id);
        f.Clock.Now += TimeSpan.FromTicks(10);
        using var expired = await Post(restored, request); Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        f.Clock.Now += TimeSpan.FromMinutes(6); await f.Work();
        await using (var db = f.Db()) Assert.Empty(await db.BookingVerifications.ToArrayAsync(Token));
        var replacementIntent = f.Request() with { RequestKey = request.RequestKey };
        var replacement = await SeedVerified(f, replacementIntent);
        using var duplicate = await Post(restored, replacement); Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await using var final = f.Db(); Assert.Single(await final.Appointments.ToArrayAsync(Token));
    }

    [Fact]
    public async Task WriteSharesOtpHttpLimitAndCannotBypassSecurityOrDisabledMode()
    {
        await using var f = new Fixture { Enabled = false }; await f.StartAsync(); using var client = f.Client();
        using var disabled = await Post(client, new { invalid = "data" }); Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
        using var csrf = await PostAsync(client, Path, new { }, null); Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        f.Enabled = true; await f.RestartAsync(); using var enabled = f.Client(); var token = await GetCsrfAsync(enabled);
        for (var i = 0; i < 20; i++)
        {
            using var response = await PostAsync(enabled, i % 2 == 0 ? Path : "/api/public-booking/verification/verify", new { }, token);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        using var limited = await PostAsync(enabled, Path, new { }, token); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.CacheControl?.NoStore); Assert.NotNull(limited.Headers.RetryAfter);
        await using var db = f.Db(); Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.BookingVerifications.ToArrayAsync(Token));
    }

    [Fact]
    public async Task RealKestrelHandlesCreateReplayUnknownFieldsChunkedSizeAndOrigin()
    {
        await using var f = new Fixture(); await f.StartAsync(); f.App.UseKestrel(0);
        using var client = f.App.CreateClient(); client.DefaultRequestHeaders.Add("Origin", client.BaseAddress?.GetLeftPart(UriPartial.Authority));
        var request = await Verified(f, client);
        using var created = await Post(client, request); var first = await Read(created, HttpStatusCode.Created);
        using var replay = await Post(client, request); Assert.Equal(first, await Read(replay, HttpStatusCode.OK));
        using var unknown = await Post(client, new { request.RequestKey, status = "Confirmed" }); Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var body = new HttpRequestMessage(HttpMethod.Post, Path) { Content = new StringContent("{\"data\":\"" + new string('x', 4100) + "\"}", System.Text.Encoding.UTF8, "application/json") };
        body.Headers.TransferEncodingChunked = true; body.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        using var large = await client.SendAsync(body, Token); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://other.test");
        using var wrong = await Post(client, request); Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        await using var db = f.Db(); Assert.Single(await db.Appointments.ToArrayAsync(Token));
    }

    [Fact]
    public async Task DatabaseChecksAndDownRejectInvalidOrDestructivePublicState()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var request = await SeedVerified(f);
        using var created = await Post(client, request); await Read(created, HttpStatusCode.Created);
        await using var db = f.Db();
        foreach (var sql in new[] { "UPDATE \"Appointments\" SET \"Source\"='Owner'", "UPDATE \"Appointments\" SET \"BookingPolicyVersion\"=NULL",
            "UPDATE \"Appointments\" SET \"CustomerCancellationLeadMinutes\"=-1", "UPDATE \"Appointments\" SET \"RequiresBusinessApproval\"=NULL",
            "UPDATE \"AppointmentAudits\" SET \"Kind\"='Created'", "UPDATE \"BookingVerifications\" SET \"ConsumedAt\"=NULL",
            "UPDATE \"BookingVerifications\" SET \"ReplayUntil\"=\"ConsumedAt\" + INTERVAL '25 hours'" })
        { var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("23514", error.SqlState); }
        var down = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261017095913_BookingPhoneVerification", Token));
        Assert.Equal("P0001", down.SqlState); Assert.Single(await db.Appointments.ToArrayAsync(Token)); Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaitingCreateRechecksGrantAndPolicyAfterLocks(bool changePolicy)
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var request = await SeedVerified(f);
        await using var locked = f.Db(); await using var transaction = await locked.Database.BeginTransactionAsync(Token);
        await locked.Database.ExecuteSqlRawAsync(changePolicy ? "SELECT * FROM \"BookingPolicies\" FOR UPDATE" : "SELECT * FROM \"StaffMembers\" FOR UPDATE", Token);
        var pending = Post(client, request);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(Token); wait.CancelAfter(TimeSpan.FromSeconds(4));
        await using (var probe = f.Db())
        {
            while (await probe.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) == 0)
                await Task.Delay(20, wait.Token);
        }
        if (changePolicy) await locked.Database.ExecuteSqlRawAsync("UPDATE \"BookingPolicies\" SET \"Version\"=gen_random_uuid()", Token);
        else f.Clock.Now += TimeSpan.FromMinutes(5);
        await transaction.CommitAsync(Token);
        using var response = await pending; Assert.Equal(changePolicy ? HttpStatusCode.Conflict : HttpStatusCode.Forbidden, response.StatusCode);
        await using var final = f.Db(); Assert.Empty(await final.Appointments.ToArrayAsync(Token)); Assert.Null((await final.BookingVerifications.SingleAsync(Token)).ConsumedAt);
    }

    [Fact]
    public async Task LockTimeoutIsTemporaryFailureWithoutPartialWrite()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var request = await SeedVerified(f);
        await using var locked = f.Db(); await using var transaction = await locked.Database.BeginTransactionAsync(Token);
        await locked.Database.ExecuteSqlRawAsync("SELECT * FROM \"StaffMembers\" FOR UPDATE", Token);
        using var response = await Post(client, request); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        await transaction.RollbackAsync(Token);
        await using var final = f.Db(); Assert.Empty(await final.Appointments.ToArrayAsync(Token)); Assert.Null((await final.BookingVerifications.SingleAsync(Token)).ConsumedAt);
    }

    [Fact]
    public async Task PendingEarlierStartBoundaryExpiresOnceAndReceiptReportsCancellation()
    {
        await using var f = new Fixture(); await f.StartAsync();
        await using (var db = f.Db()) { (await db.BookingPolicies.SingleAsync(Token)).LeadMinutes = 0; await db.SaveChangesAsync(Token); }
        using var client = f.Client();
        var request = await SeedVerified(f, f.Request() with { StartsAt = f.Clock.Now.AddMinutes(15).ToString("O", CultureInfo.InvariantCulture) });
        using var created = await Post(client, request); var result = await Read(created, HttpStatusCode.Created);
        Assert.Equal(result.StartsAt, result.PendingExpiresAt); f.Clock.Now += TimeSpan.FromMinutes(15);
        var worker = ActivatorUtilities.CreateInstance<AppointmentPendingWorker>(f.App.Services);
        Assert.Equal(1, await worker.ProcessBatchAsync(Token)); Assert.Equal(0, await worker.ProcessBatchAsync(Token));
        using var replay = await Post(client, request); Assert.Equal("Cancelled", (await Read(replay, HttpStatusCode.OK)).Status);
        await using var final = f.Db(); Assert.Equal(2, await final.AppointmentAudits.CountAsync(Token));
        var audit = await final.AppointmentAudits.SingleAsync(x => x.Kind == "Expired", Token); Assert.Null(audit.ActorId);
    }

    internal static async Task<HttpClient> Owner(Fixture f)
    {
        using var scope = f.App.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Assert.True((await roles.CreateAsync(new("Owner"))).Succeeded);
        var user = new AppUser { UserName = Email, Email = Email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Owner")).Succeeded);
        Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded); Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        var key = Assert.IsType<string>(await users.GetAuthenticatorKeyAsync(user));
        var client = f.Client(); await PasswordStepAsync(client); await CompleteMfaAsync(client, key); return client;
    }

    [Fact]
    public async Task OwnerCanListConfirmMoveAndCancelPublicBookingWithoutChangingSnapshot()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var publicClient = f.Client(); var request = await SeedVerified(f);
        using var created = await Post(publicClient, request); var first = await Read(created, HttpStatusCode.Created);
        using var owner = await Owner(f);
        using var list = await owner.GetAsync("/api/appointments/?date=2026-10-18", Token); Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(first.Id.ToString(), await list.Content.ReadAsStringAsync(Token));
        using var confirm = await PostAsync(owner, $"/api/appointments/{first.Id}/confirm", new { version = first.Version, requestKey = Guid.NewGuid() }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var confirmed = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await confirm.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        using var moved = await PostAsync(owner, $"/api/appointments/{first.Id}/move", new { version = confirmed.Version, requestKey = Guid.NewGuid(), staffMemberId = f.StaffId, startsAt = "2026-10-18T14:00:00+03:00" }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var current = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await moved.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        using var cancel = await PostAsync(owner, $"/api/appointments/{first.Id}/cancel", new { version = current.Version, requestKey = Guid.NewGuid() }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        using var replay = await Post(publicClient, request); var result = await Read(replay, HttpStatusCode.OK);
        Assert.Equal("Cancelled", result.Status); Assert.Equal(current.StartsAt.AddMinutes(-120), result.CancellationDeadline); Assert.Equal(f.PolicyVersion, result.PolicyVersion);
        await using var db = f.Db(); Assert.Equal(4, await db.AppointmentAudits.CountAsync(Token));
        Assert.Equal(3, await db.AppointmentAudits.CountAsync(x => x.ActorId != null, Token));
    }

    [Fact]
    public async Task FullPublicDumpRestoreAndTwoDisabledRestartsPreserveConsumptionAndNeverSendSms()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var request = await Verified(f, client);
        using var created = await Post(client, request); var first = await Read(created, HttpStatusCode.Created);
        Dictionary<string, string> before;
        await using (var db = f.Db()) before = await BookingVerificationTests.Fingerprints(db);
        foreach (var command in new[] { new[] { "pg_dump", "-U", "postgres", "-d", "p02_recovery", "-Fc", "-f", "/tmp/public.dump" },
            new[] { "createdb", "-U", "postgres", "public_restored" }, new[] { "pg_restore", "-U", "postgres", "-d", "public_restored", "/tmp/public.dump" } })
            Assert.Equal(0, (await f.Database.ExecAsync(command, Token)).ExitCode);
        f.DatabaseName = "public_restored"; f.Enabled = false;
        for (var i = 0; i < 2; i++)
        {
            await f.RestartAsync(); using var restored = f.Client(); using var disabled = await Post(restored, request);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode); await f.Work();
            await using var db = f.Db(); Assert.Equal(before, await BookingVerificationTests.Fingerprints(db)); Assert.Equal(first.Id, (await db.BookingVerifications.SingleAsync(Token)).AppointmentId);
        }
        Assert.Single(f.Sms.Calls);
        f.Enabled = true; await f.RestartAsync(); using var resumed = f.Client();
        using var replay = await Post(resumed, request); Assert.Equal(first.Id, (await Read(replay, HttpStatusCode.OK)).Id);
    }
}
