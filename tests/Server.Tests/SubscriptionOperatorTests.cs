using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Infrastructure;
using Server.Tests.Support;
using SubscriptionOperator;
using Xunit;
using static Server.Tests.Support.OperatorTestEnvironment;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class SubscriptionOperatorTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateOnly Start = new(2026, 10, 1);
    private static async Task<JsonElement> Read(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync("/api/subscription" + query, Token); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).Clone();
    }
    private static object Period(Guid key, string amount = "1000.00", string target = Instance) => new { targetId = target, requestKey = key, startsOn = Start, endsOn = Start.AddMonths(1), dueOn = Start, amountTry = amount };
    private static async Task<JsonElement> AddPeriod(HttpClient client, Guid id)
    {
        using var response = await Post(client, "/api/subscription/periods", Period(id)); response.EnsureSuccessStatusCode();
        return (await Read(client)).GetProperty("periods")[0].Clone();
    }
    private static object Payment(JsonElement period, Guid key, string amount, string reference = "SYNTHETIC-BANK-1") => new
    {
        targetId = Instance,
        requestKey = key,
        periodId = period.GetProperty("id").GetGuid(),
        version = period.GetProperty("version").GetGuid(),
        paidOn = Start,
        amountTry = amount,
        reference
    };

    [Fact]
    public async Task NormalPasswordMfaRecoveryAndImmediateRevocationProtectLedgerAndSecrets()
    {
        await using var env = new OperatorTestEnvironment(); await env.StartAsync();
        using var anonymous = env.App.CreateClient();
        using (var denied = await anonymous.GetAsync("/api/subscription", Token)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await Post(anonymous, "/api/subscription/periods", Period(Guid.NewGuid()))) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var login = await Post(anonymous, "/api/auth/login", new { Email, Password })) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using (var denied = await anonymous.GetAsync("/api/subscription", Token)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await Post(anonymous, "/api/auth/mfa/setup", new { Password }, "wrong")) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var owner = await env.LoginAsync();
        using (var me = await owner.GetAsync("/api/auth/me", Token))
        {
            var json = await me.Content.ReadAsStringAsync(Token); Assert.Contains("\"operatorAccess\":true", json); Assert.DoesNotContain("key", json, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("no-store", me.Headers.CacheControl?.ToString());
        }
        using (var denied = await owner.PostAsJsonAsync("/api/subscription/periods", Period(Guid.NewGuid()), Token)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var scope = env.App.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<OperatorUser>>();
        var actor = await users.FindByEmailAsync(Email); Assert.NotNull(actor); Assert.True(actor.TwoFactorEnabled); Assert.Equal(8, await users.CountRecoveryCodesAsync(actor));
        using (var wrongRole = env.App.CreateClient())
        {
            var user = new OperatorUser { UserName = "owner@example.test", Email = "owner@example.test", EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            using var denied = await Post(wrongRole, "/api/auth/login", new { user.Email, Password }); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        var recovery = (await users.GenerateNewTwoFactorRecoveryCodesAsync(actor, 8))?.ToArray(); Assert.NotNull(recovery);
        using var recovered = env.App.CreateClient();
        using (var login = await Post(recovered, "/api/auth/login", new { Email, Password })) Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
        using (var login = await Post(recovered, "/api/auth/mfa/login", new { value = recovery[0], recovery = true })) Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await Read(recovered);
        using var replay = env.App.CreateClient();
        using (var login = await Post(replay, "/api/auth/login", new { Email, Password })) Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
        using (var denied = await Post(replay, "/api/auth/mfa/login", new { value = recovery[0], recovery = true })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await scope.ServiceProvider.GetRequiredService<OperatorIdentityDb>().Entry(actor).ReloadAsync(Token);
        Assert.True((await users.UpdateSecurityStampAsync(actor)).Succeeded);
        using (var denied = await owner.GetAsync("/api/subscription", Token)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await recovered.GetAsync("/api/subscription", Token)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await anonymous.GetAsync("/api/subscription", Token)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var connection = new NpgsqlConnection(env.TargetConnection); await connection.OpenAsync(Token);
        foreach (var sql in new[] { "SELECT * FROM \"AspNetUsers\"", "SELECT * FROM \"Appointments\"", "DELETE FROM \"SubscriptionPeriods\"", "TRUNCATE \"SubscriptionPayments\"", "UPDATE \"SubscriptionPeriods\" SET \"AmountTry\"=1" })
        {
            await using var command = new NpgsqlCommand(sql, connection); var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token)); Assert.Equal("42501", error.SqlState);
        }
    }

    [Fact]
    public async Task ExactRetriesReferenceUniquenessVersionsRacesAndAtomicCorrectionsPreserveEveryOtherTable()
    {
        await using var env = new OperatorTestEnvironment(); await env.StartAsync(); using var client = await env.LoginAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(env.Database.GetConnectionString()).Options);
        var before = await SubscriptionData.Snapshot(db);
        var id = Guid.NewGuid(); var period = await AddPeriod(client, id);
        using (var same = await Post(client, "/api/subscription/periods", Period(id))) same.EnsureSuccessStatusCode();
        using (var different = await Post(client, "/api/subscription/periods", Period(id, "1001.00"))) Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        using (var overlap = await Post(client, "/api/subscription/periods", Period(Guid.NewGuid()))) Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        using (var wrong = await Post(client, "/api/subscription/periods", Period(Guid.NewGuid(), target: "wrong-firm"))) Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using (var invalid = await Post(client, "/api/subscription/payments", Payment(period, Guid.NewGuid(), "0.001"))) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var key = Guid.NewGuid(); var input = Payment(period, key, "600.00");
        using var payment = await Post(client, "/api/subscription/payments", input); payment.EnsureSuccessStatusCode();
        var paymentId = (await payment.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("id").GetGuid();
        using (var same = await Post(client, "/api/subscription/payments", input)) same.EnsureSuccessStatusCode();
        using (var different = await Post(client, "/api/subscription/payments", Payment(period, key, "601.00"))) Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        using (var stale = await Post(client, "/api/subscription/payments", Payment(period, Guid.NewGuid(), "400.00", "SYNTHETIC-STALE"))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        period = (await Read(client)).GetProperty("periods")[0].Clone();
        var exact = await SubscriptionData.Snapshot(db);
        var badCorrection = new { targetId = Instance, requestKey = Guid.NewGuid(), periodId = id, version = period.GetProperty("version").GetGuid(), paidOn = Start, amountTry = "1001.00", reason = "Sentetik aşım denemesi" };
        using (var failure = await Post(client, $"/api/subscription/payments/{paymentId}/correct", badCorrection)) Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
        Assert.Equal(exact, await SubscriptionData.Snapshot(db));
        var correction = badCorrection with { requestKey = Guid.NewGuid(), amountTry = "500.00" };
        using var corrected = await Post(client, $"/api/subscription/payments/{paymentId}/correct", correction); corrected.EnsureSuccessStatusCode();
        using (var same = await Post(client, $"/api/subscription/payments/{paymentId}/correct", correction)) same.EnsureSuccessStatusCode();
        var view = await Read(client); period = view.GetProperty("periods")[0].Clone();
        Assert.Equal("500.00", period.GetProperty("paidTry").GetString()); Assert.Equal(3, view.GetProperty("paymentCount").GetInt32());
        Assert.Equal(2, view.GetProperty("payments").EnumerateArray().Count(x => x.GetProperty("reference").GetString() == "SYNTHETIC-BANK-1"));
        using (var duplicate = await Post(client, "/api/subscription/payments", Payment(period, Guid.NewGuid(), "1.00"))) Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var other = await env.LoginAsync();
        var results = await Task.WhenAll(Post(client, "/api/subscription/payments", Payment(period, Guid.NewGuid(), "500.00", "SYNTHETIC-RACE-A")), Post(other, "/api/subscription/payments", Payment(period, Guid.NewGuid(), "500.00", "SYNTHETIC-RACE-B")));
        try { Assert.Single(results, r => r.IsSuccessStatusCode); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var result in results) result.Dispose(); }
        view = await Read(client); Assert.Equal("1000.00", view.GetProperty("periods")[0].GetProperty("paidTry").GetString());
        var after = await SubscriptionData.Snapshot(db);
        foreach (var table in before.Keys.Where(k => k is not ("SubscriptionPeriods" or "SubscriptionPayments"))) Assert.Equal(before[table], after[table]);
        Assert.Equal(1, await db.SubscriptionPeriods.CountAsync(Token)); Assert.Equal(env.Actor, (await db.SubscriptionPeriods.SingleAsync(Token)).RecordedBy);
    }

    [Fact]
    public async Task MisconfiguredTargetAndOwnerCookiesCannotReachAnotherDatabase()
    {
        await using var env = new OperatorTestEnvironment(); await env.StartAsync(); using var client = await env.LoginAsync();
        await using var wrong = env.Factory("wrong-instance"); using var wrongClient = wrong.CreateClient();
        // Aynı operatör cookie/anahtarlarıyla hedef işaretçisi yine de bağımsız doğrulanır.
        using var scope = wrong.Services.CreateScope(); var target = scope.ServiceProvider.GetRequiredService<SubscriptionTarget>();
        await Assert.ThrowsAsync<InvalidOperationException>(async () => { await using var db = await target.OpenAsync(Token); });
        using (var health = await wrongClient.GetAsync("/health/ready", Token)) Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
        var seed = await IdentityTestEnvironment.CreateRecoveryAppAsync(env.Database.GetConnectionString()); await using var firm = seed.App;
        using var owner = await AuthenticationTestSupport.InviteOwnerAsync(seed);
        using (var own = await owner.GetAsync("/api/subscription", Token)) own.EnsureSuccessStatusCode();
        using (var denied = await owner.PostAsJsonAsync("/api/subscription/periods", Period(Guid.NewGuid()), Token)) Assert.False(denied.IsSuccessStatusCode);
        await using var identity = new OperatorIdentityDb(new DbContextOptionsBuilder<OperatorIdentityDb>().UseNpgsql(env.IdentityConnection).Options);
        var tables = await identity.Database.SqlQueryRaw<string>("SELECT tablename AS \"Value\" FROM pg_tables WHERE schemaname='public'").ToArrayAsync(Token);
        Assert.Equal(7, tables.Length); Assert.DoesNotContain("Appointments", tables);
        using (var invalid = await client.GetAsync("/api/subscription?firmId=other", Token)) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(env.Database.GetConnectionString()).Options);
        Assert.Empty(await db.SubscriptionPeriods.ToArrayAsync(Token)); Assert.Empty(await db.SubscriptionPayments.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ProvisioningIsRepeatableAndRejectsWrongDatabaseIdentityAndWidenedRoleWithoutChangingData()
    {
        await using var env = new OperatorTestEnvironment(); await env.StartAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(env.Database.GetConnectionString()).Options);
        var before = await SubscriptionData.Snapshot(db);
        Assert.Equal(0, await env.ProvisionAsync("target", "p02_recovery"));
        Assert.NotEqual(0, await env.ProvisionAsync("target", "p02_recovery", "another-firm"));
        Assert.NotEqual(0, await env.ProvisionAsync("identity", "p02_recovery"));
        Assert.NotEqual(0, await env.ProvisionAsync("target", "operator_identity"));
        await db.Database.ExecuteSqlRawAsync("GRANT SELECT ON \"Appointments\" TO subscription_operator", Token);
        Assert.NotEqual(0, await env.ProvisionAsync("target", "p02_recovery"));
        await db.Database.ExecuteSqlRawAsync("REVOKE SELECT ON \"Appointments\" FROM subscription_operator", Token);
        Assert.Equal(0, await env.ProvisionAsync("target", "p02_recovery"));
        Assert.Equal(before, await SubscriptionData.Snapshot(db));
    }
}
