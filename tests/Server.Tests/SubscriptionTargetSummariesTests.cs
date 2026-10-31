using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Infrastructure;
using Server.Tests.Support;
using Xunit;
using static Server.Tests.Support.OperatorTestEnvironment;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class SubscriptionTargetSummariesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero); }
    private static AppDbContext Admin(OperatorTestEnvironment env) => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(env.Database.GetConnectionString()).Options);
    private static Dictionary<string, string?> Settings(OperatorTestEnvironment a, OperatorTestEnvironment b) => new()
    {
        ["ConnectionStrings:SubscriptionTarget"] = null,
        ["SubscriptionTarget:InstanceId"] = null,
        ["SubscriptionTarget:Name"] = null,
        ["SubscriptionTargets:0:InstanceId"] = "firm-a",
        ["SubscriptionTargets:0:Name"] = "A Salon",
        ["SubscriptionTargets:0:ConnectionString"] = a.TargetConnection,
        ["SubscriptionTargets:1:InstanceId"] = "firm-b",
        ["SubscriptionTargets:1:Name"] = "B Salon",
        ["SubscriptionTargets:1:ConnectionString"] = b.TargetConnection
    };
    private static async Task<JsonElement> Read(HttpClient client, string query = "?includeSummary=true")
    {
        using var response = await client.GetAsync("/api/targets" + query, Token); response.EnsureSuccessStatusCode(); Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).Clone();
    }

    [Fact]
    public async Task PageBoundedSummariesRetainMetadataCompatibilityAndNeverHideOldDebtOrInventOfflineBalances()
    {
        await using var a = new OperatorTestEnvironment("firm-a"); await a.StartAsync();
        await using var b = new OperatorTestEnvironment("firm-b"); await b.StartAsync();
        var settings = Settings(a, b);
        for (var i = 2; i < 13; i++)
        {
            settings[$"SubscriptionTargets:{i}:InstanceId"] = $"offline-{i}";
            settings[$"SubscriptionTargets:{i}:Name"] = $"Z Salon {i:D2}";
            settings[$"SubscriptionTargets:{i}:ConnectionString"] = $"Host=127.0.0.1;Port=1;Database=synthetic_{i};Username=subscription_operator;SSL Mode=Disable";
        }
        await using var app = a.Factory(settings: settings).WithWebHostBuilder(builder => builder.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new Clock())));
        using var anonymous = app.CreateClient();
        using (var denied = await anonymous.GetAsync("/api/targets?includeSummary=true", Token)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var login = await Post(anonymous, "/api/auth/login", new { Email, Password })) login.EnsureSuccessStatusCode();
        using (var denied = await anonymous.GetAsync("/api/targets?includeSummary=true", Token)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var client = await a.LoginAsync(app); await using var dbA = Admin(a); await using var dbB = Admin(b);
        var past = SubscriptionData.Period(new(2026, 9, 1)); var current = SubscriptionData.Period(new(2026, 10, 1));
        dbA.AddRange(past, current); await dbA.SaveChangesAsync(Token);
        dbA.Add(SubscriptionData.Payment(current.Id, 1000m, "SYNTHETIC-CURRENT")); dbA.Add(SubscriptionData.Payment(past.Id, 600m, "SYNTHETIC-PAST")); await dbA.SaveChangesAsync(Token);
        var beforeA = await SubscriptionData.Snapshot(dbA); var beforeB = await SubscriptionData.Snapshot(dbB);
        var page = await Read(client); Assert.Equal(13, page.GetProperty("total").GetInt32()); Assert.Equal(12, page.GetProperty("items").GetArrayLength());
        Assert.Equal("page", page.GetProperty("filterScope").GetString()); Assert.Equal("2026-10-31", page.GetProperty("asOf").GetString());
        var first = page.GetProperty("items")[0]; var summary = first.GetProperty("summary");
        Assert.Equal("firm-a", first.GetProperty("id").GetString()); Assert.Equal("400.00", summary.GetProperty("overdueOutstandingTry").GetString());
        Assert.True(summary.GetProperty("restrictionsApplied").GetBoolean()); Assert.Equal("Paid", summary.GetProperty("latestStartedPeriod").GetProperty("paymentState").GetString());
        Assert.False(page.GetProperty("items")[1].GetProperty("summary").GetProperty("recorded").GetBoolean());
        foreach (var item in page.GetProperty("items").EnumerateArray().Skip(2))
        { Assert.Equal("unavailable", item.GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, item.GetProperty("summary").ValueKind); }
        using (var detail = await client.GetAsync("/api/targets/firm-a/subscription", Token))
        { detail.EnsureSuccessStatusCode(); Assert.Equal(summary.GetRawText(), (await detail.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("summary").GetRawText()); }
        var restricted = await Read(client, "?includeSummary=true&access=restricted"); Assert.Single(restricted.GetProperty("items").EnumerateArray());
        var open = await Read(client, "?includeSummary=true&access=open"); Assert.Equal("firm-b", open.GetProperty("items")[0].GetProperty("id").GetString());
        var offline = await Read(client, "?includeSummary=true&access=unavailable&page=2"); Assert.Equal(1, offline.GetProperty("items").GetArrayLength()); Assert.Equal(13, offline.GetProperty("total").GetInt32());
        var metadata = await Read(client, "?search=firm-a"); Assert.Equal(["id", "name"], metadata.GetProperty("items")[0].EnumerateObject().Select(p => p.Name).ToArray());
        foreach (var query in new[] { "?includeSummary=true&access=invalid", "?access=open", "?includeSummary=true&host=other", "?includeSummary=invalid" })
        { using var invalid = await client.GetAsync("/api/targets" + query, Token); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        var json = page.GetRawText(); foreach (var secret in new[] { "Password", "ConnectionString", "SYNTHETIC-PAST", "RecordedBy", "Host=" }) Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(56, beforeA.Count); Assert.Equal(56, beforeB.Count); Assert.Equal(beforeA, await SubscriptionData.Snapshot(dbA)); Assert.Equal(beforeB, await SubscriptionData.Snapshot(dbB));
    }

    [Fact]
    public async Task ConcurrentListRequestsShareTheThreeConnectionBound()
    {
        await using var a = new OperatorTestEnvironment("firm-a"); await a.StartAsync();
        await using var b = new OperatorTestEnvironment("firm-b"); await b.StartAsync();
        await using var app = a.Factory(settings: Settings(a, b)); using var client = await a.LoginAsync(app);
        await using var dbA = Admin(a); await using var dbB = Admin(b);
        await using var lockA = await dbA.Database.BeginTransactionAsync(Token); await using var lockB = await dbB.Database.BeginTransactionAsync(Token);
        await dbA.Database.ExecuteSqlRawAsync("LOCK TABLE \"SubscriptionPeriods\" IN ACCESS EXCLUSIVE MODE", Token);
        await dbB.Database.ExecuteSqlRawAsync("LOCK TABLE \"SubscriptionPeriods\" IN ACCESS EXCLUSIVE MODE", Token);
        var first = Read(client); var second = Read(client);
        await using var observerA = Admin(a); await using var observerB = Admin(b);
        // İstatistik gözlemi kilit transaction'ının önbelleklenmiş snapshot'ını kullanmaz.
        async Task<int> Waiting() => await observerA.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE usename='subscription_operator' AND state='active' AND wait_event_type='Lock'").SingleAsync(Token) +
            await observerB.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE usename='subscription_operator' AND state='active' AND wait_event_type='Lock'").SingleAsync(Token);
        var timer = Stopwatch.StartNew();
        while (await Waiting() < 3 && timer.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(20, Token);
        await Task.Delay(100, Token); Assert.Equal(3, await Waiting()); Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        await lockA.RollbackAsync(Token); await lockB.RollbackAsync(Token);
        foreach (var page in await Task.WhenAll(first, second))
            Assert.All(page.GetProperty("items").EnumerateArray(), item => Assert.Equal("available", item.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task BlockedFirmTimesOutIndependentlyAndNextRequestRecomputesWithoutACachedUnknownState()
    {
        await using var a = new OperatorTestEnvironment("firm-a"); await a.StartAsync();
        await using var b = new OperatorTestEnvironment("firm-b"); await b.StartAsync();
        await using var app = a.Factory(settings: Settings(a, b)); using var client = await a.LoginAsync(app);
        await using var dbB = Admin(b); var before = await SubscriptionData.Snapshot(dbB);
        await using (var transaction = await dbB.Database.BeginTransactionAsync(Token))
        {
            await dbB.Database.ExecuteSqlRawAsync("LOCK TABLE \"SubscriptionPeriods\" IN ACCESS EXCLUSIVE MODE", Token);
            var timer = Stopwatch.StartNew(); var page = await Read(client); timer.Stop();
            Assert.InRange(timer.Elapsed.TotalSeconds, 4, 12);
            Assert.Equal("available", page.GetProperty("items")[0].GetProperty("status").GetString());
            Assert.Equal("unavailable", page.GetProperty("items")[1].GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, page.GetProperty("items")[1].GetProperty("summary").ValueKind);
            await transaction.RollbackAsync(Token);
        }
        var recovered = await Read(client); Assert.Equal("available", recovered.GetProperty("items")[1].GetProperty("status").GetString());
        Assert.False(recovered.GetProperty("items")[1].GetProperty("summary").GetProperty("restrictionsApplied").GetBoolean());
        Assert.Equal(before, await SubscriptionData.Snapshot(dbB));
    }
}
