using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Subscriptions;
using Server.Infrastructure;
using Server.Tests.Support;
using Xunit;
using static Server.Tests.Support.OperatorTestEnvironment;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class SubscriptionTargetLedgerTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 30, 20, 59, 59, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class LostResponse(HttpClient authenticated) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content };
            foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            using var committed = await authenticated.SendAsync(forwarded, token);
            committed.EnsureSuccessStatusCode();
            throw new HttpRequestException("Sentetik commit sonrası yanıt kaybı");
        }
    }
    private static string Route(string target) => $"/api/targets/{target}/subscription";
    private static AppDbContext Admin(OperatorTestEnvironment env) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(env.Database.GetConnectionString()).Options);
    private static Dictionary<string, string?> Settings(OperatorTestEnvironment a, OperatorTestEnvironment b) => new()
    {
        ["ConnectionStrings:SubscriptionTarget"] = null,
        ["SubscriptionTarget:InstanceId"] = null,
        ["SubscriptionTarget:Name"] = null,
        ["SubscriptionTargets:0:InstanceId"] = "firm-a",
        ["SubscriptionTargets:0:Name"] = "Sentetik A",
        ["SubscriptionTargets:0:ConnectionString"] = a.TargetConnection,
        ["SubscriptionTargets:1:InstanceId"] = "firm-b",
        ["SubscriptionTargets:1:Name"] = "Sentetik B",
        ["SubscriptionTargets:1:ConnectionString"] = b.TargetConnection
    };
    private static async Task<JsonElement> Read(HttpClient client, string target, string query = "")
    {
        using var response = await client.GetAsync(Route(target) + query, Token); response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).Clone();
    }
    private static object Period(string target, Guid key, string amount) => new
    {
        targetId = target,
        requestKey = key,
        startsOn = new DateOnly(2026, 10, 1),
        endsOn = new DateOnly(2026, 11, 1),
        dueOn = new DateOnly(2026, 10, 1),
        amountTry = amount
    };
    private static object Payment(string target, JsonElement period, Guid key, string amount, string reference = "SYNTHETIC-BANK") => new
    {
        targetId = target,
        requestKey = key,
        periodId = period.GetProperty("id").GetGuid(),
        version = period.GetProperty("version").GetGuid(),
        paidOn = new DateOnly(2026, 10, 1),
        amountTry = amount,
        reference
    };
    private static async Task<Guid> Save(HttpClient client, string route, object input, string? csrf = null)
    {
        using var response = await Post(client, route, input, csrf); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid();
    }
    private static JsonElement Selected(JsonElement view) => view.GetProperty("periods")[0].Clone();

    [Fact]
    public async Task SelectedFirmWritesReplayRaceAndCorrectionsNeverTouchTheOtherFirmOrBusinessData()
    {
        await using var a = new OperatorTestEnvironment("firm-a"); await a.StartAsync();
        await using var b = new OperatorTestEnvironment("firm-b"); await b.StartAsync();
        var clock = new Clock();
        await using var app = a.Factory(settings: Settings(a, b)).WithWebHostBuilder(builder => builder.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(clock)));
        using var client = await a.LoginAsync(app); using var other = await a.LoginAsync(app);
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf", Token)).GetProperty("token").GetString(); Assert.NotNull(csrf);
        await using var dbA = Admin(a); await using var dbB = Admin(b);
        var beforeA = await SubscriptionData.Snapshot(dbA); var beforeB = await SubscriptionData.Snapshot(dbB);
        Assert.Equal(56, beforeA.Count); Assert.Equal(56, beforeB.Count);
        var periodKey = Guid.NewGuid();
        await Save(client, Route("firm-a") + "/periods", Period("firm-a", periodKey, "1000.00"), csrf);
        await Save(client, Route("firm-b") + "/periods", Period("firm-b", periodKey, "2000.00"), csrf);
        var viewA = await Read(client, "firm-a"); var viewB = await Read(client, "firm-b");
        Assert.Equal("firm-a", viewA.GetProperty("targetId").GetString()); Assert.Equal("Sentetik B", viewB.GetProperty("targetName").GetString());
        var periodA = Selected(viewA); var periodB = Selected(viewB); var key = Guid.NewGuid();
        var input = Payment("firm-a", periodA, key, "600.00");
        using var disconnected = new HttpClient(new LostResponse(client)) { BaseAddress = client.BaseAddress };
        await Assert.ThrowsAsync<HttpRequestException>(async () => { using var lost = await Post(disconnected, Route("firm-a") + "/payments", input, csrf); });
        Assert.Equal(1, await dbA.SubscriptionPayments.CountAsync(Token));
        var payment = await Save(client, Route("firm-a") + "/payments", input, csrf);
        Assert.Equal(payment, await Save(client, Route("firm-a") + "/payments", input, csrf));
        using (var changed = await Post(client, Route("firm-a") + "/payments", Payment("firm-a", periodA, key, "601.00"))) Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        await Save(client, Route("firm-b") + "/payments", Payment("firm-b", periodB, key, "1500.00"), csrf);
        Assert.Equal("500.00", (await Read(client, "firm-b")).GetProperty("summary").GetProperty("overdueOutstandingTry").GetString());
        var bAfterPayment = await SubscriptionData.Snapshot(dbB);
        using (var mismatch = await Post(client, Route("firm-b") + "/payments", input)) Assert.Equal(HttpStatusCode.NotFound, mismatch.StatusCode);
        using (var foreignPayment = await Post(client, Route("firm-b") + $"/payments/{payment}/reverse", new { targetId = "firm-b", requestKey = Guid.NewGuid(), periodId = periodKey, version = periodB.GetProperty("version").GetGuid(), reason = "Sentetik yanlış ödeme" })) Assert.Equal(HttpStatusCode.NotFound, foreignPayment.StatusCode);
        using (var foreignVersion = await Post(client, Route("firm-b") + "/payments", Payment("firm-b", periodA, Guid.NewGuid(), "1.00", "SYNTHETIC-FOREIGN"))) Assert.Equal(HttpStatusCode.Conflict, foreignVersion.StatusCode);
        using (var foreignPeriod = await Post(client, Route("firm-a") + "/payments", new { targetId = "firm-a", requestKey = Guid.NewGuid(), periodId = Guid.NewGuid(), version = Guid.NewGuid(), paidOn = new DateOnly(2026, 10, 1), amountTry = "1.00", reference = "SYNTHETIC-MISSING-PERIOD" })) Assert.Equal(HttpStatusCode.NotFound, foreignPeriod.StatusCode);
        periodA = Selected(await Read(client, "firm-a"));
        var correction = new { targetId = "firm-a", requestKey = Guid.NewGuid(), periodId = periodKey, version = periodA.GetProperty("version").GetGuid(), paidOn = new DateOnly(2026, 10, 1), amountTry = "500.00", reason = "Sentetik tutar düzeltmesi" };
        var exact = await SubscriptionData.Snapshot(dbA);
        using (var rejected = await Post(client, Route("firm-a") + $"/payments/{payment}/correct", correction with { amountTry = "1001.00" })) Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(exact, await SubscriptionData.Snapshot(dbA));
        var replacement = await Save(client, Route("firm-a") + $"/payments/{payment}/correct", correction, csrf);
        Assert.Equal(replacement, await Save(client, Route("firm-a") + $"/payments/{payment}/correct", correction, csrf));
        periodA = Selected(await Read(client, "firm-a"));
        var race = await Task.WhenAll(Post(client, Route("firm-a") + "/payments", Payment("firm-a", periodA, Guid.NewGuid(), "500.00", "SYNTHETIC-RACE-A")),
            Post(other, Route("firm-a") + "/payments", Payment("firm-a", periodA, Guid.NewGuid(), "500.00", "SYNTHETIC-RACE-B")));
        try { Assert.Single(race, r => r.IsSuccessStatusCode); Assert.Single(race, r => r.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var result in race) result.Dispose(); }
        viewA = await Read(client, "firm-a"); Assert.False(viewA.GetProperty("summary").GetProperty("restrictionsApplied").GetBoolean());
        Assert.False(await SubscriptionAccess.IsRestrictedAsync(dbA, clock, Token)); Assert.True(await SubscriptionAccess.IsRestrictedAsync(dbB, clock, Token));
        periodA = Selected(viewA);
        var reversal = new { targetId = "firm-a", requestKey = Guid.NewGuid(), periodId = periodKey, version = periodA.GetProperty("version").GetGuid(), reason = "Sentetik ters kayıt" };
        var reversed = await Save(client, Route("firm-a") + $"/payments/{replacement}/reverse", reversal, csrf);
        Assert.Equal(reversed, await Save(client, Route("firm-a") + $"/payments/{replacement}/reverse", reversal, csrf));
        viewA = await Read(client, "firm-a");
        Assert.Equal("500.00", viewA.GetProperty("summary").GetProperty("overdueOutstandingTry").GetString());
        Assert.True(await SubscriptionAccess.IsRestrictedAsync(dbA, clock, Token)); Assert.Equal(bAfterPayment, await SubscriptionData.Snapshot(dbB));
        foreach (var (db, before) in new[] { (dbA, beforeA), (dbB, beforeB) })
        {
            var after = await SubscriptionData.Snapshot(db);
            foreach (var table in before.Keys.Where(k => k is not ("SubscriptionPeriods" or "SubscriptionPayments"))) Assert.Equal(before[table], after[table]);
        }
    }

    [Fact]
    public async Task SummaryIncludesAllStartedOverduePeriodsAcrossPagesAndUsesIstanbulDayWithoutCountingFutureDebt()
    {
        await using var env = new OperatorTestEnvironment(); await env.StartAsync(); var clock = new Clock();
        await using var app = env.Factory().WithWebHostBuilder(builder => builder.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(clock)));
        using var client = await env.LoginAsync(app); await using var db = Admin(env);
        var empty = (await Read(client, Instance)).GetProperty("summary");
        Assert.False(empty.GetProperty("recorded").GetBoolean()); Assert.Equal(JsonValueKind.Null, empty.GetProperty("latestStartedPeriod").ValueKind);
        var future = SubscriptionData.Period(new(2026, 11, 1)); future.DueOn = new(2026, 9, 1);
        db.Add(future); await db.SaveChangesAsync(Token);
        var futureOnly = await Read(client, Instance);
        Assert.Equal("0.00", futureOnly.GetProperty("summary").GetProperty("overdueOutstandingTry").GetString());
        Assert.Equal(JsonValueKind.Null, futureOnly.GetProperty("summary").GetProperty("latestStartedPeriod").ValueKind);
        Assert.False(futureOnly.GetProperty("periods")[0].GetProperty("overdue").GetBoolean());
        var current = SubscriptionData.Period(new(2026, 10, 1));
        var dueToday = SubscriptionData.Period(new(2026, 9, 1)); dueToday.DueOn = new(2026, 10, 30);
        var old = Enumerable.Range(1, 13).Select(i => SubscriptionData.Period(new DateOnly(2026, 9, 1).AddMonths(-i))).ToArray();
        db.AddRange(old); db.AddRange(current, dueToday); await db.SaveChangesAsync(Token);
        db.Add(SubscriptionData.Payment(current.Id, 1000m, "SYNTHETIC-CURRENT"));
        db.Add(SubscriptionData.Payment(old[0].Id, 600m, "SYNTHETIC-OLD-PARTIAL")); await db.SaveChangesAsync(Token);
        var before = await SubscriptionData.Snapshot(db);
        var first = await Read(client, Instance); var second = await Read(client, Instance, "?page=2");
        Assert.Equal("2026-10-30", first.GetProperty("asOf").GetString()); Assert.Equal("Europe/Istanbul", first.GetProperty("timeZone").GetString());
        Assert.Equal(clock.Now, first.GetProperty("checkedAt").GetDateTimeOffset());
        var summary = first.GetProperty("summary");
        Assert.Equal(summary.GetRawText(), second.GetProperty("summary").GetRawText());
        Assert.Equal(current.Id, summary.GetProperty("latestStartedPeriod").GetProperty("id").GetGuid());
        Assert.Equal("0.00", summary.GetProperty("latestStartedPeriod").GetProperty("outstandingTry").GetString());
        Assert.Equal("12400.00", summary.GetProperty("overdueOutstandingTry").GetString()); Assert.Equal(13, summary.GetProperty("overduePeriodCount").GetInt32());
        Assert.True(summary.GetProperty("restrictionsApplied").GetBoolean()); Assert.True(await SubscriptionAccess.IsRestrictedAsync(db, clock, Token));
        clock.Now = clock.Now.AddSeconds(1);
        var tomorrow = await Read(client, Instance, "?page=1000000");
        Assert.Equal("2026-10-31", tomorrow.GetProperty("asOf").GetString());
        Assert.Equal("13400.00", tomorrow.GetProperty("summary").GetProperty("overdueOutstandingTry").GetString());
        Assert.Equal(before, await SubscriptionData.Snapshot(db));
        using (var legacy = await client.GetAsync("/api/subscription", Token))
        { legacy.EnsureSuccessStatusCode(); Assert.Equal(tomorrow.GetProperty("summary").GetRawText(), (await legacy.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("summary").GetRawText()); }
        clock.Now = new(2026, 12, 15, 0, 0, 0, TimeSpan.Zero);
        using var renewed = await env.LoginAsync(app);
        var gap = await Read(renewed, Instance, "?periodId=" + old[^1].Id + "&paymentsPage=1000000");
        Assert.Equal(future.Id, gap.GetProperty("summary").GetProperty("latestStartedPeriod").GetProperty("id").GetGuid());
        Assert.Equal("14400.00", gap.GetProperty("summary").GetProperty("overdueOutstandingTry").GetString());
        Assert.Equal(before, await SubscriptionData.Snapshot(db));
    }

    [Fact]
    public async Task SelectedRoutesKeepAuthorizationCsrfValidationAndUnavailableFirmDoesNotFallBack()
    {
        await using var a = new OperatorTestEnvironment("firm-a"); await a.StartAsync();
        await using var b = new OperatorTestEnvironment("firm-b"); await b.StartAsync();
        await using var app = a.Factory(settings: Settings(a, b));
        using var anonymous = app.CreateClient(); var input = Period("firm-a", Guid.NewGuid(), "1000.00");
        using (var denied = await anonymous.GetAsync(Route("firm-a"), Token)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await Post(anonymous, Route("firm-a") + "/periods", input)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var login = await Post(anonymous, "/api/auth/login", new { Email, Password })) login.EnsureSuccessStatusCode();
        using (var denied = await anonymous.GetAsync(Route("firm-a"), Token)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var client = await a.LoginAsync(app); await using var dbA = Admin(a); await using var dbB = Admin(b);
        var beforeA = await SubscriptionData.Snapshot(dbA); var beforeB = await SubscriptionData.Snapshot(dbB);
        foreach (var path in new[] { "/periods", "/payments", $"/payments/{Guid.NewGuid()}/reverse", $"/payments/{Guid.NewGuid()}/correct" })
        {
            using var csrf = await Post(client, Route("firm-a") + path, input, "invalid"); Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
            using var mismatch = await Post(client, Route("firm-b") + path, input); Assert.Equal(HttpStatusCode.NotFound, mismatch.StatusCode);
            using var unknown = await Post(client, Route("unknown") + path, input); Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        }
        using (var unknown = await client.GetAsync(Route("unknown"), Token)) Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        foreach (var query in new[] { "?page=0", "?paymentsPage=0", "?targetId=firm-b", "?connectionString=ignored" })
        { using var invalid = await client.GetAsync(Route("firm-a") + query, Token); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using (var missing = await client.GetAsync(Route("firm-a") + "?periodId=" + Guid.NewGuid(), Token)) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(beforeA, await SubscriptionData.Snapshot(dbA)); Assert.Equal(beforeB, await SubscriptionData.Snapshot(dbB));
        await b.Database.StopAsync(Token);
        using (var unavailable = await client.GetAsync(Route("firm-b"), Token)) Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        using (var unavailable = await Post(client, Route("firm-b") + "/periods", Period("firm-b", Guid.NewGuid(), "1000.00"))) Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.False((await Read(client, "firm-a")).GetProperty("summary").GetProperty("recorded").GetBoolean());
        Assert.Equal(beforeA, await SubscriptionData.Snapshot(dbA));
        using (var ready = await client.GetAsync("/health/ready", Token)) ready.EnsureSuccessStatusCode();
        using (var missing = await client.GetAsync(Route("unknown"), Token)) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var limited = await client.GetAsync(Route("firm-a"), Token))
        {
            var status = limited.StatusCode;
            for (var i = 0; i < 60 && status != HttpStatusCode.TooManyRequests; i++)
            { using var next = await client.GetAsync(Route("firm-a"), Token); status = next.StatusCode; if (status == HttpStatusCode.TooManyRequests) Assert.NotNull(next.Headers.RetryAfter); }
            Assert.Equal(HttpStatusCode.TooManyRequests, status);
        }
    }
}
