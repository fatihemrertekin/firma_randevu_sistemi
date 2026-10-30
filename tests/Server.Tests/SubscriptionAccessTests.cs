using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Business;
using Server.Features.Scheduling;
using Server.Features.Subscriptions;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class SubscriptionAccessTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 30, 20, 59, 59, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static async Task<HttpResponseMessage> Rename(HttpClient client)
    {
        var profile = await client.GetFromJsonAsync<BusinessProfileEndpoints.ProfileResponse>("/api/business-profile/", Token);
        Assert.NotNull(profile);
        return await PostAsync(client, "/api/business-profile/", new { name = "Sentetik değişiklik", profile.Version }, await GetCsrfAsync(client));
    }
    private static async Task<bool> Restricted(HttpClient client)
    {
        using var response = await client.GetAsync("/api/subscription/access", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var value = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Single(value.EnumerateObject());
        return value.GetProperty("restrictionsApplied").GetBoolean();
    }
    private static async Task Code(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task IstanbulMidnightPartialPaymentReopeningAndReversalPreserveReadsAndRejectedData()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var clock = new Clock(); var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var period = SubscriptionData.Period(new(2026, 10, 1)); period.DueOn = new(2026, 10, 30);
        db.SubscriptionPeriods.Add(period); await db.SaveChangesAsync(Token);
        Assert.False(await Restricted(owner)); using (var allowed = await Rename(owner)) Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        clock.Now = clock.Now.AddSeconds(1); Assert.Equal(new DateOnly(2026, 10, 31), SubscriptionAccess.Today(clock)); Assert.True(await Restricted(owner));
        var before = await SubscriptionData.Snapshot(db);
        await Code(await Rename(owner), HttpStatusCode.Forbidden, "subscription_read_only");
        using (var service = await PostAsync(owner, "/api/services/", new { id = Guid.NewGuid(), name = "Sentetik", durationMinutes = 30, price = "100.00" }, await GetCsrfAsync(owner)))
            Assert.Equal(HttpStatusCode.Forbidden, service.StatusCode);
        foreach (var path in new[] { "/api/appointments?date=2026-10-31", "/api/appointments/export?from=2026-10-31&to=2026-10-31", "/api/subscription", "/api/auth/me" })
        { using var response = await owner.GetAsync(path, Token); Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
        Assert.Equal(before, await SubscriptionData.Snapshot(db));
        var partial = SubscriptionData.Payment(period.Id, 600m, "SYNTHETIC-PARTIAL"); db.SubscriptionPayments.Add(partial); await db.SaveChangesAsync(Token);
        Assert.True(await Restricted(owner)); await Code(await Rename(owner), HttpStatusCode.Forbidden, "subscription_read_only");
        var rest = SubscriptionData.Payment(period.Id, 400m, "SYNTHETIC-REST"); db.SubscriptionPayments.Add(rest); await db.SaveChangesAsync(Token);
        Assert.False(await Restricted(owner)); using (var allowed = await Rename(owner)) Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        db.SubscriptionPayments.Add(SubscriptionData.Reversal(rest)); await db.SaveChangesAsync(Token);
        Assert.True(await Restricted(owner)); await Code(await Rename(owner), HttpStatusCode.Forbidden, "subscription_read_only");
        db.SubscriptionPayments.Add(SubscriptionData.Replacement(rest, period.Id, 400m)); await db.SaveChangesAsync(Token); Assert.False(await Restricted(owner));
        using var noCsrf = await PostAsync(owner, "/api/business-profile/", new { name = "Sentetik", version = Guid.NewGuid() }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
    }

    [Fact]
    public async Task NoRecordAndFuturePeriodStayOpenButAnyStartedOverduePeriodBlocksEvenIfCurrentPaid()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await Restricted(owner)); using (var allowed = await Rename(owner)) Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var future = SubscriptionData.Period(new(2026, 11, 1)); future.DueOn = new(2026, 10, 1); db.SubscriptionPeriods.Add(future); await db.SaveChangesAsync(Token);
        Assert.False(await Restricted(owner));
        var current = SubscriptionData.Period(new(2026, 10, 1)); var past = SubscriptionData.Period(new(2026, 9, 1)); db.SubscriptionPeriods.AddRange(current, past); await db.SaveChangesAsync(Token);
        db.SubscriptionPayments.Add(SubscriptionData.Payment(current.Id, 1000m, "SYNTHETIC-CURRENT")); await db.SaveChangesAsync(Token);
        Assert.True(await Restricted(owner));
        var body = await owner.GetFromJsonAsync<SubscriptionEndpoints.SubscriptionResponse>("/api/subscription", Token);
        Assert.NotNull(body); Assert.True(body.RestrictionsApplied); Assert.Equal("Paid", body.CurrentPeriod?.PaymentState);
        db.SubscriptionPayments.Add(SubscriptionData.Payment(past.Id, 1000m, "SYNTHETIC-PAST")); await db.SaveChangesAsync(Token); Assert.False(await Restricted(owner));
        using var anonymous = app.CreateClient(); using var denied = await anonymous.GetAsync("/api/subscription/access", Token); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LedgerCommitIsObservedAfterWaitingRatherThanUsingAStaleBalance(bool reverse)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var period = SubscriptionData.Period(new(2026, 10, 1)); db.SubscriptionPeriods.Add(period); await db.SaveChangesAsync(Token);
        var payment = SubscriptionData.Payment(period.Id, 1000m, "SYNTHETIC-RACE");
        if (reverse) { db.SubscriptionPayments.Add(payment); await db.SaveChangesAsync(Token); }
        using var financeScope = app.Services.CreateScope(); var finance = financeScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await finance.Database.BeginTransactionAsync(Token);
        await finance.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({SubscriptionAccess.LedgerLock})", Token);
        finance.SubscriptionPayments.Add(reverse ? SubscriptionData.Reversal(payment) : payment); await finance.SaveChangesAsync(Token);
        var write = Rename(owner); await AwaitGate(db); Assert.False(write.IsCompleted);
        await transaction.CommitAsync(Token);
        using var result = await write; Assert.Equal(reverse ? HttpStatusCode.Forbidden : HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(reverse, await Restricted(owner));
    }

    [Fact]
    public async Task LockTimeoutFailsClosedWithoutAWriteAndDoesNotLeaveAPooledSessionLock()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await SubscriptionData.Snapshot(db);
        await using (var transaction = await db.Database.BeginTransactionAsync(Token))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({SubscriptionAccess.LedgerLock})", Token);
            await Code(await Rename(owner), HttpStatusCode.ServiceUnavailable, "subscription_unavailable");
            await transaction.RollbackAsync(Token);
        }
        Assert.Equal(before, await SubscriptionData.Snapshot(db));
        using var allowed = await Rename(owner); Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task OwnerCommittedCreateReplayResolvesWhileNewCreateMoveAndStatusAreReadOnly()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        var request = new AppointmentEndpoints.CreateRequest(Guid.NewGuid(), Guid.Parse("55359fdc-d67b-4c44-8fce-65e4ce53ceaa"), Guid.Parse("6ff08883-02a3-4d71-b206-0316c78906cc"),
            Guid.Parse("30f650b8-3614-4b29-aac2-c521f47f4d11"), "2026-10-31T10:00:00+03:00", "Sentetik müşteri", "05000000001");
        using var created = await PostAsync(owner, "/api/appointments/", request, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token); Assert.NotNull(saved);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.SubscriptionPeriods.Add(SubscriptionData.Period(new(2026, 10, 1))); await db.SaveChangesAsync(Token); var before = await SubscriptionData.Snapshot(db);
        using var replay = await PostAsync(owner, "/api/appointments/", request, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await Code(await PostAsync(owner, "/api/appointments/", request with { RequestKey = Guid.NewGuid() }, await GetCsrfAsync(owner)), HttpStatusCode.Forbidden, "subscription_read_only");
        await Code(await PostAsync(owner, $"/api/appointments/{saved.Id}/cancel", new { saved.Version, requestKey = Guid.NewGuid() }, await GetCsrfAsync(owner)), HttpStatusCode.Forbidden, "subscription_read_only");
        await Code(await PostAsync(owner, $"/api/appointments/{saved.Id}/move", new { saved.Version, requestKey = Guid.NewGuid(), request.StaffMemberId, startsAt = "2026-10-31T11:00:00+03:00" }, await GetCsrfAsync(owner)), HttpStatusCode.Forbidden, "subscription_read_only");
        Assert.Equal(before, await SubscriptionData.Snapshot(db));
    }

    private static async Task AwaitGate(AppDbContext db)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token); timeout.CancelAfter(TimeSpan.FromSeconds(4));
        while (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%pg_advisory_xact_lock_shared%'")
            .SingleAsync(timeout.Token) == 0) await Task.Delay(20, timeout.Token);
    }
}
