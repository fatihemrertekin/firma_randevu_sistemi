using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Server.Features.Scheduling;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using Fixture = Server.Tests.BookingVerificationTests.Fixture;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class PublicAppointmentAccessTests
{
    private const string Path = "/api/public-booking/appointments";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static async Task<HttpResponseMessage> Post(HttpClient client, Guid id, string action, object body) =>
        await PostAsync(client, $"{Path}/{id}/{action}", body, await GetCsrfAsync(client));
    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.True(status == response.StatusCode, $"Expected {status}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync(Token)}"); Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(Token));
    }
    private static async Task<(PublicAppointmentEndpoints.Request Request, PublicAppointmentEndpoints.Response Result)> Book(Fixture f, HttpClient client, bool real = false)
    {
        var request = real ? await PublicAppointmentTests.Verified(f, client) : await PublicAppointmentTests.SeedVerified(f);
        using var created = await PostAsync(client, Path, request, await GetCsrfAsync(client));
        return (request, await Read<PublicAppointmentEndpoints.Response>(created, HttpStatusCode.Created));
    }
    private static async Task<PublicAppointmentAccessEndpoints.Issued> Issue(HttpClient client,
        (PublicAppointmentEndpoints.Request Request, PublicAppointmentEndpoints.Response Result) booking, bool replace = false)
    {
        using var response = await Post(client, booking.Result.Id, "access", new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, booking.Request.ChallengeSecret, replace));
        return await Read<PublicAppointmentAccessEndpoints.Issued>(response, replace ? HttpStatusCode.OK : HttpStatusCode.Created);
    }
    private static void Authorize(HttpClient client, string token) => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    private static async Task Confirmed(Fixture f, int minutes = 120)
    {
        await using var db = f.Db(); var policy = await db.BookingPolicies.SingleAsync(Token);
        policy.RequiresBusinessApproval = false; policy.CustomerCancellationLeadMinutes = minutes; await db.SaveChangesAsync(Token);
    }
    private sealed class RacePeers : IStartupFilter
    {
        private int peer;
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, proceed) =>
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Parse($"127.0.0.{Interlocked.Increment(ref peer)}");
                await proceed(ctx);
            });
            next(app);
        };
    }
    private static void SeparateRacePeers(Fixture f)
    {
        // Yirmi ayrı istemci DB yarışına ulaşır; üretim IP/global limitleri kapatılmaz.
        f.App = f.App.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, RacePeers>()));
    }

    private sealed class LostResponse : IStartupFilter
    {
        private int dropped;
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (ctx, proceed) =>
            {
                ctx.Response.OnStarting(() =>
                {
                    if (ctx.Request.Method == "POST" && (ctx.Request.Path.Value?.EndsWith("/access", StringComparison.Ordinal) == true ||
                        ctx.Request.Path.Value?.EndsWith("/cancel", StringComparison.Ordinal) == true) &&
                        ctx.Response.StatusCode is 200 or 201 && Interlocked.Exchange(ref dropped, 1) == 0)
                        throw new IOException("Synthetic response loss after commit.");
                    return Task.CompletedTask;
                });
                await proceed(ctx);
            });
            next(app);
        };
    }

    [Fact]
    public async Task RealFlowRotatesReadsCancelsAndReplaysWithoutDuplicatingOrLeakingContact()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync();
        using var client = f.Client(); var booking = await Book(f, client, real: true);
        var first = await Issue(client, booking); Authorize(client, first.Token);
        using var read = await client.GetAsync($"{Path}/{booking.Result.Id}", Token);
        var preview = await Read<PublicAppointmentEndpoints.Response>(read, HttpStatusCode.OK); Assert.Equal(booking.Result, preview);
        var json = await read.Content.ReadAsStringAsync(Token);
        foreach (var field in new[] { "customerName", "customerPhone", "actorId", "token", "hash" }) Assert.DoesNotContain(field, json, StringComparison.OrdinalIgnoreCase);
        using var duplicate = await Post(client, booking.Result.Id, "access", new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, booking.Request.ChallengeSecret, false));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var rotated = await Issue(client, booking, true); Assert.NotEqual(first.Token, rotated.Token); Assert.Equal(first.ExpiresAt, rotated.ExpiresAt);
        using var old = await client.GetAsync($"{Path}/{booking.Result.Id}", Token); Assert.Equal(HttpStatusCode.Forbidden, old.StatusCode);
        Authorize(client, rotated.Token); var command = new PublicAppointmentAccessEndpoints.Cancel(preview.Version, Guid.NewGuid());
        using var cancelled = await Post(client, preview.Id, "cancel", command);
        var result = await Read<PublicAppointmentEndpoints.Response>(cancelled, HttpStatusCode.OK); Assert.Equal("Cancelled", result.Status);
        using var replay = await Post(client, preview.Id, "cancel", command); Assert.Equal(result, await Read<PublicAppointmentEndpoints.Response>(replay, HttpStatusCode.OK));
        using var different = await Post(client, preview.Id, "cancel", command with { RequestKey = Guid.NewGuid() }); Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        var afterCancel = await Issue(client, booking, true); Authorize(client, afterCancel.Token);
        using var afterRotation = await Post(client, preview.Id, "cancel", command); Assert.Equal(result, await Read<PublicAppointmentEndpoints.Response>(afterRotation, HttpStatusCode.OK));
        await using var db = f.Db(); var access = await db.PublicAppointmentAccesses.SingleAsync(Token);
        Assert.Equal(BookingVerificationSecurity.SecretHash(afterCancel.Token), access.TokenHash); Assert.DoesNotContain(afterCancel.Token, access.TokenHash);
        Assert.Single(await db.AppointmentAudits.Where(x => x.Kind == "PublicCancelled" && x.ActorId == null).ToArrayAsync(Token));
        Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token)); Assert.Single(await db.Appointments.ToArrayAsync(Token));
        Assert.Equal("Cancelled", (await db.AppointmentTimeBlocks.SingleAsync(Token)).Status);
        Assert.Equal(access.CancellationAuditId, (await db.AppointmentAudits.SingleAsync(x => x.Kind == "PublicCancelled", Token)).Id);
    }

    [Theory]
    [InlineData(120, -1, true)]
    [InlineData(120, 0, true)]
    [InlineData(120, 1, false)]
    [InlineData(0, -1, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    public async Task CancellationUsesSnapshotAndExactStartAndLeadBoundaries(int minutes, int seconds, bool allowed)
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); await Confirmed(f, minutes);
        using var client = f.Client(); var booking = await Book(f, client); var access = await Issue(client, booking); Authorize(client, access.Token);
        await using (var db = f.Db()) { var policy = await db.BookingPolicies.SingleAsync(Token); policy.CustomerCancellationLeadMinutes = minutes == 0 ? 120 : 0; await db.SaveChangesAsync(Token); }
        f.Clock.Now = new DateTimeOffset(booking.Result.CancellationDeadline).AddSeconds(seconds);
        using var response = await Post(client, booking.Result.Id, "cancel", new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid()));
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Conflict, response.StatusCode);
        await using var check = f.Db(); Assert.Equal(allowed ? 1 : 0, await check.AppointmentAudits.CountAsync(x => x.Kind == "PublicCancelled", Token));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task PendingCannotBeCancelledAtOrAfterExpiry(int seconds, bool allowed)
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); using var client = f.Client();
        var booking = await Book(f, client); var access = await Issue(client, booking); Authorize(client, access.Token);
        f.Clock.Now = new DateTimeOffset(Assert.IsType<DateTime>(booking.Result.PendingExpiresAt)).AddSeconds(seconds);
        using var response = await Post(client, booking.Result.Id, "cancel", new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid()));
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwentyCancellationsHaveOneAuditAndDeterministicReplay(bool differentKeys)
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); SeparateRacePeers(f);
        using var client = f.Client(); var booking = await Book(f, client); var access = await Issue(client, booking); Authorize(client, access.Token);
        var key = Guid.NewGuid(); var csrf = await GetCsrfAsync(client);
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(client, $"{Path}/{booking.Result.Id}/cancel",
            new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, differentKeys ? Guid.NewGuid() : key), csrf)));
        Assert.Equal(differentKeys ? 1 : 20, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        Assert.Equal(differentKeys ? 19 : 0, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses) response.Dispose();
        await using var db = f.Db(); Assert.Single(await db.AppointmentAudits.Where(x => x.Kind == "PublicCancelled").ToArrayAsync(Token));
    }

    [Fact]
    public async Task TwentyFirstIssuesAndRotationsLeaveOnlyTheLastCommittedTokenValid()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); SeparateRacePeers(f);
        using var client = f.Client(); var booking = await Book(f, client); var csrf = await GetCsrfAsync(client);
        var issue = new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, booking.Request.ChallengeSecret, false);
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(client, $"{Path}/{booking.Result.Id}/access", issue, csrf)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created); Assert.Equal(19, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        var first = await Read<PublicAppointmentAccessEndpoints.Issued>(responses.Single(x => x.StatusCode == HttpStatusCode.Created), HttpStatusCode.Created);
        foreach (var response in responses) response.Dispose();
        responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(client, $"{Path}/{booking.Result.Id}/access", issue with { ReplaceExisting = true }, csrf)));
        var tokens = new List<PublicAppointmentAccessEndpoints.Issued> { first };
        foreach (var response in responses) { tokens.Add(await Read<PublicAppointmentAccessEndpoints.Issued>(response, HttpStatusCode.OK)); response.Dispose(); }
        Assert.Equal(21, tokens.Select(x => x.Token).Distinct().Count());
        await using var db = f.Db(); var row = await db.PublicAppointmentAccesses.SingleAsync(Token);
        Assert.All(tokens, x => Assert.Equal(first.ExpiresAt, x.ExpiresAt));
        foreach (var issued in tokens)
        {
            Authorize(client, issued.Token); using var result = await client.GetAsync($"{Path}/{booking.Result.Id}", Token);
            Assert.Equal(row.TokenHash == BookingVerificationSecurity.SecretHash(issued.Token) ? HttpStatusCode.OK : HttpStatusCode.Forbidden, result.StatusCode);
        }
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task ReceiptExpiryDoesNotRevokeIndependentReadButPreventsIssuance(int seconds, bool allowed)
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); await Confirmed(f);
        using var client = f.Client(); var booking = await Book(f, client); var access = await Issue(client, booking); Authorize(client, access.Token);
        f.Clock.Now += TimeSpan.FromHours(24) + TimeSpan.FromSeconds(seconds);
        using var response = await Post(client, booking.Result.Id, "access", new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, booking.Request.ChallengeSecret, true));
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        if (allowed) Authorize(client, (await Read<PublicAppointmentAccessEndpoints.Issued>(response, HttpStatusCode.OK)).Token);
        using var preview = await client.GetAsync($"{Path}/{booking.Result.Id}", Token); Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task TokenExpiryCutsReadAndCancellationReplay(int seconds, bool allowed)
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); using var client = f.Client();
        var booking = await Book(f, client); var access = await Issue(client, booking); Authorize(client, access.Token);
        var command = new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid());
        using var cancelled = await Post(client, booking.Result.Id, "cancel", command); Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        f.Clock.Now = access.ExpiresAt.AddSeconds(seconds);
        using var read = await client.GetAsync($"{Path}/{booking.Result.Id}", Token);
        using var replay = await Post(client, booking.Result.Id, "cancel", command);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, replay.StatusCode);
    }

    [Fact]
    public async Task WrongMissingAndOtherAppointmentSecretsNeverRevealOrMutateRecords()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); using var client = f.Client();
        var booking = await Book(f, client);
        using var missingRenew = await Post(client, booking.Result.Id, "access", new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, booking.Request.ChallengeSecret, true)); Assert.Equal(HttpStatusCode.Conflict, missingRenew.StatusCode);
        var access = await Issue(client, booking);
        foreach (var secret in new[] { "", "wrong", new string('x', 43) })
        {
            client.DefaultRequestHeaders.Remove("Authorization");
            if (secret.Length != 0) Authorize(client, secret);
            using var read = await client.GetAsync($"{Path}/{booking.Result.Id}?token={access.Token}", Token); Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
            using var cancel = await Post(client, booking.Result.Id, "cancel", new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid())); Assert.Equal(HttpStatusCode.Forbidden, cancel.StatusCode);
            using var issue = await Post(client, booking.Result.Id, "access", new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, secret, true)); Assert.Equal(HttpStatusCode.Forbidden, issue.StatusCode);
        }
        Authorize(client, access.Token);
        using var other = await client.GetAsync($"{Path}/{Guid.NewGuid()}", Token); Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        using var otherIssue = await Post(client, Guid.NewGuid(), "access", new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, booking.Request.ChallengeSecret, true)); Assert.Equal(HttpStatusCode.Forbidden, otherIssue.StatusCode);
        using var stale = await Post(client, booking.Result.Id, "cancel", new PublicAppointmentAccessEndpoints.Cancel(Guid.NewGuid(), Guid.NewGuid())); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await using var db = f.Db(); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token)); Assert.Null((await db.PublicAppointmentAccesses.SingleAsync(Token)).CancellationRequestKey);
    }

    [Fact]
    public async Task ExistingAccessSurvivesOtpShutdownCleanupAndRestartsAndAccessGateClosesEverything()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); await Confirmed(f);
        using var client = f.Client(); var booking = await Book(f, client); var access = await Issue(client, booking);
        f.Enabled = false; await f.RestartAsync();
        using var restarted = f.Client(); Authorize(restarted, access.Token);
        using var issue = await Post(restarted, booking.Result.Id, "access", new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, booking.Request.ChallengeSecret, true)); Assert.Equal(HttpStatusCode.ServiceUnavailable, issue.StatusCode);
        using var preview = await restarted.GetAsync($"{Path}/{booking.Result.Id}", Token); Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var command = new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid());
        using var cancelled = await Post(restarted, booking.Result.Id, "cancel", command); Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        f.Clock.Now += TimeSpan.FromHours(24) + TimeSpan.FromMinutes(6);
        f.Enabled = true; await f.RestartAsync(); await f.Work(); f.Enabled = false;
        await using (var db = f.Db()) Assert.Empty(await db.BookingVerifications.ToArrayAsync(Token));
        await f.RestartAsync(); using var again = f.Client(); Authorize(again, access.Token);
        using var replay = await Post(again, booking.Result.Id, "cancel", command); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        f.AccessEnabled = false; await f.RestartAsync(); using var disabled = f.Client(); Authorize(disabled, access.Token);
        using var read = await disabled.GetAsync($"{Path}/{booking.Result.Id}", Token); Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode);
        foreach (var action in new[] { "access", "cancel" })
        { using var response = await Post(disabled, booking.Result.Id, action, new { unknown = true }); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); }
    }

    [Fact]
    public async Task MigrationDownRefusesAccessHistoryAndDatabaseRejectsInvalidAccessAndAudit()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); using var client = f.Client();
        var booking = await Book(f, client); await Issue(client, booking);
        await using var db = f.Db();
        var downSql = db.GetService<IMigrator>().GenerateScript("20261017201619_PublicAppointmentAccess", "20261017145959_PublicAppointmentBooking", MigrationsSqlGenerationOptions.NoTransactions);
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(downSql, Token));
        Assert.Equal("Müşteri erişimi veya iptal tarihçesi varken geri migration uygulanamaz.", error.MessageText);
        Assert.Single(await db.PublicAppointmentAccesses.ToArrayAsync(Token));
        foreach (var sql in new[] {
            "UPDATE \"PublicAppointmentAccesses\" SET \"TokenHash\"='plain'",
            "UPDATE \"PublicAppointmentAccesses\" SET \"ExpiresAt\"=\"FirstIssuedAt\"",
            "UPDATE \"PublicAppointmentAccesses\" SET \"CancellationRequestKey\"=gen_random_uuid()",
            "UPDATE \"AppointmentAudits\" SET \"Kind\"='Cancelled'" })
            await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token));
    }

    [Theory]
    [InlineData("access")]
    [InlineData("cancel")]
    public async Task ResponseLossAfterCommitRequiresExplicitRotationOrSameCancellationReplay(string action)
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); using var client = f.Client();
        var booking = await Book(f, client);
        var command = new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid());
        if (action == "cancel") Authorize(client, (await Issue(client, booking)).Token);
        await using var broken = f.App.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, LostResponse>()));
        using var connection = broken.CreateClient(); connection.DefaultRequestHeaders.Add("Origin", "http://localhost");
        connection.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        object body = action == "access" ? new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, booking.Request.ChallengeSecret, false) : command;
        await Assert.ThrowsAsync<IOException>(() => Post(connection, booking.Result.Id, action, body));
        if (action == "access")
        {
            using var duplicate = await Post(client, booking.Result.Id, action, body); Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Authorize(client, (await Issue(client, booking, true)).Token);
        }
        using var result = await Post(client, booking.Result.Id, "cancel", command); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        await using var db = f.Db(); Assert.Single(await db.PublicAppointmentAccesses.ToArrayAsync(Token));
        Assert.Single(await db.AppointmentAudits.Where(x => x.Kind == "PublicCancelled").ToArrayAsync(Token));
    }

    [Fact]
    public async Task AuditFailureRollsBackStatusVersionAndCancellationReceipt()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); using var client = f.Client();
        var booking = await Book(f, client); var access = await Issue(client, booking); Authorize(client, access.Token);
        await using (var db = f.Db()) await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_customer_cancel() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
                IF NEW."Kind" = 'PublicCancelled' THEN RAISE EXCEPTION 'synthetic rollback'; END IF; RETURN NEW; END $$;
            CREATE TRIGGER reject_customer_cancel BEFORE INSERT ON "AppointmentAudits" FOR EACH ROW EXECUTE FUNCTION reject_customer_cancel();
            """, Token);
        var command = new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid());
        using var failed = await Post(client, booking.Result.Id, "cancel", command); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using (var db = f.Db())
        {
            Assert.Equal(booking.Result.Version, (await db.Appointments.SingleAsync(Token)).Version);
            Assert.Equal(booking.Result.Status, (await db.AppointmentTimeBlocks.SingleAsync(Token)).Status);
            Assert.Null((await db.PublicAppointmentAccesses.SingleAsync(Token)).CancellationAuditId); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_customer_cancel ON \"AppointmentAudits\"; DROP FUNCTION reject_customer_cancel();", Token);
        }
        using var retry = await Post(client, booking.Result.Id, "cancel", command); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Theory]
    [InlineData("access", false)]
    [InlineData("cancel", false)]
    [InlineData("access", true)]
    [InlineData("cancel", true)]
    public async Task WaitingMutationsRecheckExpiryAndLockTimeoutLeavesNoPartialWrite(string action, bool timeout)
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); await Confirmed(f);
        using var client = f.Client(); var booking = await Book(f, client);
        if (action == "cancel") Authorize(client, (await Issue(client, booking)).Token);
        await using var locked = f.Db(); await using var transaction = await locked.Database.BeginTransactionAsync(Token);
        var sql = action == "access" ? "SELECT * FROM \"Appointments\" FOR UPDATE" : "SELECT * FROM \"StaffMembers\" FOR UPDATE";
        await locked.Database.ExecuteSqlRawAsync(sql, Token);
        object body = action == "access" ? new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, booking.Request.ChallengeSecret, false)
            : new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid());
        var pending = Post(client, booking.Result.Id, action, body);
        if (!timeout)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(Token); wait.CancelAfter(TimeSpan.FromSeconds(10));
            await using var probe = f.Db();
            while (await probe.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) == 0)
                await Task.Delay(20, wait.Token);
            f.Clock.Now = action == "access" ? f.Clock.Now.AddHours(24) : new DateTimeOffset(booking.Result.CancellationDeadline).AddSeconds(1);
            await transaction.RollbackAsync(Token);
        }
        using var response = await pending;
        Assert.Equal(timeout ? HttpStatusCode.ServiceUnavailable : action == "access" ? HttpStatusCode.Forbidden : HttpStatusCode.Conflict, response.StatusCode);
        if (timeout) await transaction.RollbackAsync(Token);
        await using var db = f.Db(); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
        Assert.Equal(booking.Result.Version, (await db.Appointments.SingleAsync(Token)).Version);
        if (action == "access") Assert.Empty(await db.PublicAppointmentAccesses.ToArrayAsync(Token));
        else Assert.Null((await db.PublicAppointmentAccesses.SingleAsync(Token)).CancellationAuditId);
    }

    [Theory]
    [InlineData("confirm")]
    [InlineData("move")]
    [InlineData("cancel")]
    [InlineData("expiry")]
    public async Task OwnerAndExpiryRacesKeepCurrentSnapshotSingleCancellationAndFixedAccessEnd(string action)
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync();
        if (action == "move") await Confirmed(f);
        using var client = f.Client(); var booking = await Book(f, client); var access = await Issue(client, booking); Authorize(client, access.Token);
        using var owner = await PublicAppointmentTests.Owner(f);
        using var cookieAlone = await owner.GetAsync($"{Path}/{booking.Result.Id}", Token); Assert.Equal(HttpStatusCode.Forbidden, cookieAlone.StatusCode);
        var command = new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid());
        Task<HttpResponseMessage>? ownerMutation = null;
        Task<int>? expiry = null;
        if (action == "expiry")
        {
            f.Clock.Now = new DateTimeOffset(Assert.IsType<DateTime>(booking.Result.PendingExpiresAt));
            expiry = ActivatorUtilities.CreateInstance<AppointmentPendingWorker>(f.App.Services).ProcessBatchAsync(Token);
        }
        else
        {
            object body = action == "move" ? new { version = booking.Result.Version, requestKey = Guid.NewGuid(), staffMemberId = f.StaffId, startsAt = "2026-10-18T14:00:00+03:00" }
                : new { version = booking.Result.Version, requestKey = Guid.NewGuid() };
            ownerMutation = PostAsync(owner, $"/api/appointments/{booking.Result.Id}/{action}", body, await GetCsrfAsync(owner));
        }
        using var customer = await Post(client, booking.Result.Id, "cancel", command);
        Assert.Contains(customer.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
        if (ownerMutation is not null) { using var response = await ownerMutation; Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }); }
        if (expiry is not null) { await expiry; await ActivatorUtilities.CreateInstance<AppointmentPendingWorker>(f.App.Services).ProcessBatchAsync(Token); }
        using var read = await client.GetAsync($"{Path}/{booking.Result.Id}", Token);
        var current = await Read<PublicAppointmentEndpoints.Response>(read, HttpStatusCode.OK);
        Assert.Equal(120, current.CustomerCancellationLeadMinutes); Assert.Equal(current.StartsAt.AddMinutes(-120), current.CancellationDeadline);
        await using var db = f.Db(); Assert.Equal(access.ExpiresAt, (await db.PublicAppointmentAccesses.SingleAsync(Token)).ExpiresAt);
        Assert.InRange(await db.AppointmentAudits.CountAsync(x => x.Kind == "PublicCancelled", Token), 0, 1);
        if (action is "cancel" or "expiry") Assert.Equal("Cancelled", current.Status);
    }

    [Fact]
    public async Task RealKestrelEnforcesJsonOriginCsrfSizeAndSharedWriteLimit()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); f.App.UseKestrel(0);
        using var client = f.App.CreateClient(); client.DefaultRequestHeaders.Add("Origin", client.BaseAddress?.GetLeftPart(UriPartial.Authority));
        var booking = await Book(f, client, real: true); var access = await Issue(client, booking); Authorize(client, access.Token);
        using var read = await client.GetAsync($"{Path}/{booking.Result.Id}", Token); await Read<PublicAppointmentEndpoints.Response>(read, HttpStatusCode.OK);
        using var invalid = await Post(client, booking.Result.Id, "cancel", new { version = booking.Result.Version, requestKey = Guid.NewGuid(), status = "Confirmed" }); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var noCsrf = await PostAsync(client, $"{Path}/{booking.Result.Id}/cancel", new { }, null); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{Path}/{booking.Result.Id}/cancel") { Content = new StringContent("{\"data\":\"" + new string('x', 4100) + "\"}", System.Text.Encoding.UTF8, "application/json") };
        message.Headers.TransferEncodingChunked = true; message.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        using var large = await client.SendAsync(message, Token); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        var origin = client.DefaultRequestHeaders.GetValues("Origin").Single(); client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://other.test");
        using var wrongOrigin = await Post(client, booking.Result.Id, "cancel", new { }); Assert.Equal(HttpStatusCode.BadRequest, wrongOrigin.StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", origin);
        var csrf = await GetCsrfAsync(client); HttpResponseMessage? limited = null;
        for (var i = 0; i < 21; i++)
        {
            var path = i % 3 == 0 ? "/api/public-booking/verification/verify" : $"{Path}/{booking.Result.Id}/{(i % 3 == 1 ? "access" : "cancel")}";
            using var response = await PostAsync(client, path, new { }, csrf);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) { limited = response; Assert.True(response.Headers.CacheControl?.NoStore); Assert.NotNull(response.Headers.RetryAfter); Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single()); break; }
        }
        Assert.NotNull(limited); await using var db = f.Db(); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task FullRestoreRevokesOldTokensAndReceiptSecretsWithoutLosingCancellationHistory()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); using var client = f.Client();
        var booking = await Book(f, client, real: true); var first = await Issue(client, booking); var current = await Issue(client, booking, true); Authorize(client, current.Token);
        var command = new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid());
        using var cancelled = await Post(client, booking.Result.Id, "cancel", command); Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Dictionary<string, string> before;
        BookingSmsBudgetPolicy budgetBefore;
        await using (var db = f.Db())
        {
            before = await BookingVerificationTests.Fingerprints(db);
            budgetBefore = await db.BookingSmsBudgetPolicies.AsNoTracking().SingleAsync(Token);
        }
        foreach (var operation in new[] { new[] { "pg_dump", "-U", "postgres", "-d", "p02_recovery", "-Fc", "-f", "/tmp/access.dump" },
            new[] { "pg_restore", "--file=/tmp/access-check.sql", "/tmp/access.dump" },
            new[] { "createdb", "-U", "postgres", "access_restored" }, new[] { "pg_restore", "-U", "postgres", "-d", "access_restored", "/tmp/access.dump" } })
            Assert.Equal(0, (await f.Database.ExecAsync(operation, Token)).ExitCode);
        f.DatabaseName = "access_restored"; f.AccessEnabled = false; f.Enabled = false;
        for (var i = 0; i < 2; i++)
        {
            await f.RestartAsync(); using var restored = f.Client(); Authorize(restored, current.Token);
            using var disabled = await restored.GetAsync($"{Path}/{booking.Result.Id}", Token); Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
            await using var db = f.Db(); Assert.Equal(before, await BookingVerificationTests.Fingerprints(db));
        }
        await using (var db = f.Db())
        {
            var script = await File.ReadAllTextAsync(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/Invalidate-RestoredPublicAccess.sql")), Token);
            await db.Database.ExecuteSqlRawAsync(script, Token);
            Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token)); Assert.Equal("Cancelled", (await db.AppointmentTimeBlocks.SingleAsync(Token)).Status);
            Assert.Equal(command.RequestKey, (await db.PublicAppointmentAccesses.SingleAsync(Token)).CancellationRequestKey);
            var after = await BookingVerificationTests.Fingerprints(db);
            foreach (var table in before.Keys.Except(new[] { "PublicAppointmentAccesses", "BookingVerifications", "BookingSmsBudgetPolicies" })) Assert.Equal(before[table], after[table]);
            var budgetAfter = await db.BookingSmsBudgetPolicies.AsNoTracking().SingleAsync(Token);
            Assert.NotNull(budgetAfter.RevokedAt); budgetAfter.RevokedAt = null;
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(budgetBefore), System.Text.Json.JsonSerializer.Serialize(budgetAfter));
        }
        f.AccessEnabled = true; f.Enabled = true; f.DispatchEnabled = false; await f.RestartAsync(); using var resumed = f.Client();
        foreach (var token in new[] { first.Token, current.Token })
        {
            Authorize(resumed, token); using var read = await resumed.GetAsync($"{Path}/{booking.Result.Id}", Token); Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
            using var replay = await Post(resumed, booking.Result.Id, "cancel", command); Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        }
        using var renew = await Post(resumed, booking.Result.Id, "access", new PublicAppointmentAccessEndpoints.Issue(booking.Request.ChallengeId, booking.Request.ChallengeSecret, true)); Assert.Equal(HttpStatusCode.Forbidden, renew.StatusCode);
        Assert.Single(f.Sms.Calls);
    }

    [Fact]
    public async Task RestrictedAppRoleCanIssueRotateAndCancelButCannotExtendAccessOrRewriteHistory()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); using var client = f.Client();
        var booking = await Book(f, client);
        await using (var db = f.Db())
        {
            await db.Database.ExecuteSqlRawAsync("CREATE ROLE app_user LOGIN PASSWORD 'SyntheticApp!123'; GRANT USAGE ON SCHEMA public TO app_user", Token);
            var script = await File.ReadAllTextAsync(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/Apply-LocalMigration.ps1")), Token);
            var start = script.IndexOf("@'", StringComparison.Ordinal) + 2;
            await db.Database.ExecuteSqlRawAsync(script[start..script.IndexOf("'@ |", start, StringComparison.Ordinal)], Token);
        }
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(f.Database.GetConnectionString()) { Username = "app_user", Password = "SyntheticApp!123" }.ConnectionString;
        await using var limited = f.App.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AppDatabase"] = connection })));
        using var browser = limited.CreateClient(); browser.DefaultRequestHeaders.Add("Origin", "http://localhost");
        await Issue(browser, booking); var access = await Issue(browser, booking, true); Authorize(browser, access.Token);
        using var response = await Post(browser, booking.Result.Id, "cancel", new PublicAppointmentAccessEndpoints.Cancel(booking.Result.Version, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var restricted = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        foreach (var sql in new[] { "UPDATE \"PublicAppointmentAccesses\" SET \"ExpiresAt\"=\"ExpiresAt\" + INTERVAL '1 hour'",
            "UPDATE \"PublicAppointmentAccesses\" SET \"FirstIssuedAt\"=\"FirstIssuedAt\" - INTERVAL '1 hour'",
            "DELETE FROM \"PublicAppointmentAccesses\"", "TRUNCATE \"PublicAppointmentAccesses\"",
            "UPDATE \"Appointments\" SET \"CustomerCancellationLeadMinutes\"=0", "UPDATE \"AppointmentAudits\" SET \"Kind\"='Expired'" })
        {
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => restricted.Database.ExecuteSqlRawAsync(sql, Token));
            Assert.Equal("42501", error.SqlState);
        }
    }

    [Fact]
    public async Task DistantAppointmentCapsAccessAtThirtyOneDaysWithoutExtendingItOnRotation()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); await Confirmed(f);
        await using (var db = f.Db()) { (await db.BookingPolicies.SingleAsync(Token)).HorizonDays = 365; await db.SaveChangesAsync(Token); }
        using var client = f.Client();
        var request = await PublicAppointmentTests.SeedVerified(f, f.Request() with { StartsAt = f.Clock.Now.AddDays(60).ToString("O", System.Globalization.CultureInfo.InvariantCulture) });
        using var created = await PostAsync(client, Path, request, await GetCsrfAsync(client));
        var booking = (request, await Read<PublicAppointmentEndpoints.Response>(created, HttpStatusCode.Created));
        var first = await Issue(client, booking); Assert.Equal(f.Clock.Now.AddDays(31), first.ExpiresAt);
        f.Clock.Now += TimeSpan.FromHours(1); var rotated = await Issue(client, booking, true); Assert.Equal(first.ExpiresAt, rotated.ExpiresAt);
    }
}
