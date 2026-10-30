using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Npgsql;
using Server.Infrastructure;
using Server.Tests.Support;
using SubscriptionOperator;
using Xunit;
using static Server.Tests.Support.OperatorTestEnvironment;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class SubscriptionTargetsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Dictionary<string, string?> Settings(string a = "Host=127.0.0.1;Database=firm_a;Username=subscription_operator;SSL Mode=VerifyFull", string b = "Host=127.0.0.1;Database=firm_b;Username=subscription_operator;SSL Mode=VerifyFull") => new()
    {
        ["ConnectionStrings:OperatorIdentity"] = "Host=127.0.0.1;Database=identity;Username=operator_identity_user",
        ["ConnectionStrings:SubscriptionTarget"] = null,
        ["SubscriptionTarget:InstanceId"] = null,
        ["SubscriptionTarget:Name"] = null,
        ["SubscriptionTargets:0:InstanceId"] = "firm-a",
        ["SubscriptionTargets:0:Name"] = "Örnek A",
        ["SubscriptionTargets:0:ConnectionString"] = a,
        ["SubscriptionTargets:1:InstanceId"] = "firm-b",
        ["SubscriptionTargets:1:Name"] = "Örnek B",
        ["SubscriptionTargets:1:ConnectionString"] = b
    };
    private static SubscriptionTargets Registry(Dictionary<string, string?> settings, string environment = "Production") =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new HostingEnvironment { EnvironmentName = environment });

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("case-id")]
    [InlineData("duplicate-database")]
    [InlineData("identity-database")]
    [InlineData("mixed-legacy")]
    [InlineData("missing-name")]
    [InlineData("missing-connection")]
    [InlineData("invalid-id")]
    [InlineData("invalid-connection")]
    [InlineData("unverified-tls")]
    public void InvalidAllowlistFailsBeforeAnyConnection(string variant)
    {
        var settings = Settings();
        switch (variant)
        {
            case "duplicate-id": settings["SubscriptionTargets:1:InstanceId"] = "firm-a"; break;
            case "case-id": settings["SubscriptionTargets:1:InstanceId"] = "FIRM-A"; break;
            case "duplicate-database": settings["SubscriptionTargets:1:ConnectionString"] = settings["SubscriptionTargets:0:ConnectionString"]; break;
            case "identity-database": settings["SubscriptionTargets:1:ConnectionString"] = "Host=127.0.0.1;Database=identity;Username=subscription_operator;SSL Mode=VerifyFull"; break;
            case "mixed-legacy": settings["SubscriptionTarget:InstanceId"] = "old-firm"; break;
            case "missing-name": settings["SubscriptionTargets:1:Name"] = " "; break;
            case "missing-connection": settings["SubscriptionTargets:1:ConnectionString"] = null; break;
            case "invalid-id": settings["SubscriptionTargets:1:InstanceId"] = "../firm"; break;
            case "invalid-connection": settings["SubscriptionTargets:1:ConnectionString"] = "Password=SecretNotForLogs;UnsupportedKey=SecretNotForLogs"; break;
            case "unverified-tls": settings["SubscriptionTargets:1:ConnectionString"] = "Host=private-db;Database=firm_b;Username=subscription_operator;SSL Mode=Require"; break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => Registry(settings));
        Assert.DoesNotContain("SecretNotForLogs", error.ToString());
    }

    [Theory]
    [InlineData("Development", "127.0.0.1", true)]
    [InlineData("Development", "::1", true)]
    [InlineData("Development", "localhost", false)]
    [InlineData("Development", "private-db", false)]
    [InlineData("Production", "127.0.0.1", false)]
    public void PlaintextExceptionIsOnlyDevelopmentNumericLoopback(string environment, string host, bool allowed)
    {
        var settings = Settings($"Host={host};Database=firm_a;Username=subscription_operator;SSL Mode=Disable");
        if (allowed) Assert.NotNull(Registry(settings, environment).Find("firm-a"));
        else Assert.Throws<InvalidOperationException>(() => Registry(settings, environment));
    }

    [Fact]
    public void ExplicitTargetsHaveNoImplicitDefaultAndUnknownIdsNeverFallBack()
    {
        var targets = Registry(Settings());
        Assert.Null(targets.Single); Assert.Null(targets.Find("unknown")); Assert.Null(targets.Find("FIRM-A"));
        Assert.Equal("Örnek A", targets.Find("firm-a")?.Name);
        Assert.Equal("Örnek B", targets.Find("firm-b")?.Name);
    }

    [Fact]
    public async Task TwoRealFirmsRemainIsolatedAndLegacyFinancialRoutesFailClosed()
    {
        await using var a = new OperatorTestEnvironment("firm-a"); await a.StartAsync();
        await using var b = new OperatorTestEnvironment("firm-b"); await b.StartAsync();
        var settings = Settings(a.TargetConnection, b.TargetConnection);
        settings["ConnectionStrings:OperatorIdentity"] = a.IdentityConnection;
        await using var central = a.Factory(settings: settings); using var anonymous = central.CreateClient();
        using (var denied = await anonymous.GetAsync("/api/targets", Token)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var passwordOnly = central.CreateClient())
        {
            using var login = await Post(passwordOnly, "/api/auth/login", new { Email, Password }); login.EnsureSuccessStatusCode();
            using var denied = await passwordOnly.GetAsync("/api/targets", Token); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using var client = await a.LoginAsync(central);
        await using var adminA = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(a.Database.GetConnectionString()).Options);
        await using var adminB = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(b.Database.GetConnectionString()).Options);
        var beforeA = await SubscriptionData.Snapshot(adminA); var beforeB = await SubscriptionData.Snapshot(adminB);
        Assert.Equal(56, beforeA.Count); Assert.Equal(56, beforeB.Count);
        using (var list = await client.GetAsync("/api/targets", Token))
        {
            list.EnsureSuccessStatusCode(); Assert.Equal("no-store", list.Headers.CacheControl?.ToString());
            var json = await list.Content.ReadFromJsonAsync<JsonElement>(Token);
            Assert.Equal(2, json.GetProperty("total").GetInt32());
            foreach (var item in json.GetProperty("items").EnumerateArray()) Assert.Equal(["id", "name"], item.EnumerateObject().Select(p => p.Name).ToArray());
        }
        using (var one = await client.GetAsync("/api/targets/firm-b", Token)) one.EnsureSuccessStatusCode();
        using (var missing = await client.GetAsync("/api/targets/unknown", Token)) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (var query in new[] { "?page=0", "?page=1000001", "?connectionString=ignored" })
        { using var invalid = await client.GetAsync("/api/targets" + query, Token); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using (var search = await client.GetAsync("/api/targets?search=firm-b", Token))
        { search.EnsureSuccessStatusCode(); Assert.Equal(1, (await search.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("total").GetInt32()); }
        using (var read = await client.GetAsync("/api/subscription", Token)) Assert.Equal(HttpStatusCode.Conflict, read.StatusCode);
        var key = Guid.NewGuid(); var period = new { targetId = "firm-a", requestKey = key, startsOn = new DateOnly(2026, 10, 1), endsOn = new DateOnly(2026, 11, 1), dueOn = new DateOnly(2026, 10, 1), amountTry = "1000.00" };
        using (var csrf = await Post(client, "/api/subscription/periods", period, "invalid")) Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        foreach (var route in new[] { "/periods", "/payments", $"/payments/{key}/reverse", $"/payments/{key}/correct" })
        { using var blocked = await Post(client, "/api/subscription" + route, period); Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode); }
        Assert.Equal(beforeA, await SubscriptionData.Snapshot(adminA)); Assert.Equal(beforeB, await SubscriptionData.Snapshot(adminB));
        var registry = central.Services.GetRequiredService<SubscriptionTargets>();
        await using var dbA = await (registry.Find("firm-a") ?? throw new InvalidOperationException()).OpenAsync(Token);
        await using var dbB = await (registry.Find("firm-b") ?? throw new InvalidOperationException()).OpenAsync(Token);
        Assert.Empty(await dbA.SubscriptionPeriods.ToArrayAsync(Token)); Assert.Empty(await dbB.SubscriptionPeriods.ToArrayAsync(Token));
        await Assert.ThrowsAsync<PostgresException>(() => dbA.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"Appointments\"").SingleAsync(Token));
        adminA.SubscriptionPeriods.Add(new SubscriptionPeriod { Id = key, StartsOn = period.startsOn, EndsOn = period.endsOn, DueOn = period.dueOn, AmountTry = 1000, RecordedBy = a.Actor, RecordedAt = DateTime.UtcNow });
        await adminA.SaveChangesAsync(Token);
        Assert.Equal(key, await dbA.SubscriptionPeriods.Select(p => p.Id).SingleAsync(Token)); Assert.Empty(await dbB.SubscriptionPeriods.ToArrayAsync(Token));
        var wrong = Settings(a.TargetConnection, b.TargetConnection); wrong["SubscriptionTargets:0:InstanceId"] = "wrong-marker";
        var wrongRegistry = Registry(wrong, "Development");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var ignored = await (wrongRegistry.Find("wrong-marker") ?? throw new InvalidOperationException()).OpenAsync(Token); });
        var spoof = Settings(new NpgsqlConnectionStringBuilder(a.TargetConnection) { Options = "-c app.subscription_instance=forged-marker" }.ConnectionString, b.TargetConnection);
        spoof["SubscriptionTargets:0:InstanceId"] = "forged-marker";
        var spoofed = Registry(spoof, "Development");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var ignored = await (spoofed.Find("forged-marker") ?? throw new InvalidOperationException()).OpenAsync(Token); });
        var forgedIdentity = Settings(a.TargetConnection, b.TargetConnection);
        forgedIdentity["ConnectionStrings:OperatorIdentity"] = new NpgsqlConnectionStringBuilder(a.IdentityConnection) { Options = "-c app.operator_identity=forged-operator" }.ConnectionString;
        forgedIdentity["Operator:InstanceId"] = "forged-operator";
        await using (var forgedApp = a.Factory(settings: forgedIdentity))
        using (var forgedClient = forgedApp.CreateClient())
        { using var denied = await forgedClient.GetAsync("/api/auth/me", Token); Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode); }
        wrong = Settings(new NpgsqlConnectionStringBuilder(a.Database.GetConnectionString()).ConnectionString, b.TargetConnection);
        var broad = Registry(wrong, "Development");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var ignored = await (broad.Find("firm-a") ?? throw new InvalidOperationException()).OpenAsync(Token); });
        await dbB.DisposeAsync(); await b.Database.StopAsync(Token);
        using (var ready = await client.GetAsync("/health/ready", Token)) ready.EnsureSuccessStatusCode();
        using (var stillListed = await client.GetAsync("/api/targets", Token)) stillListed.EnsureSuccessStatusCode();
        await using var stillA = await (registry.Find("firm-a") ?? throw new InvalidOperationException()).OpenAsync(Token);
        Assert.Equal(key, await stillA.SubscriptionPeriods.Select(p => p.Id).SingleAsync(Token));
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token); bounded.CancelAfter(TimeSpan.FromSeconds(7));
        var offline = await Record.ExceptionAsync(async () => { await using var ignored = await (registry.Find("firm-b") ?? throw new InvalidOperationException()).OpenAsync(bounded.Token); });
        Assert.True(offline is NpgsqlException or InvalidOperationException { InnerException: NpgsqlException });
        Assert.Equal(key, await stillA.SubscriptionPeriods.Select(p => p.Id).SingleAsync(Token));
        await a.Database.StopAsync(Token);
        using var identityDown = await client.GetAsync("/api/targets", Token); Assert.Equal(HttpStatusCode.ServiceUnavailable, identityDown.StatusCode);
    }
}
