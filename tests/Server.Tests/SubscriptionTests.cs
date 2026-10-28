using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Subscriptions;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class SubscriptionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 21, 30, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task OwnerReadsExactBalancesWithIstanbulDateAndBoundedHistoryWithoutPrivatePaymentFieldsOrWrites()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var october = SubscriptionData.Period(new(2026, 10, 1));
        db.SubscriptionPeriods.Add(october);
        for (var month = 1; month <= 9; month++) db.SubscriptionPeriods.Add(SubscriptionData.Period(new(2026, month, 1)));
        db.SubscriptionPeriods.Add(SubscriptionData.Period(new(2025, 12, 1)));
        var november = SubscriptionData.Period(new(2026, 11, 1)); db.SubscriptionPeriods.Add(november);
        await db.SaveChangesAsync(Token);
        var first = SubscriptionData.Payment(october.Id, 600m, "SYNTHETIC-BANK-01");
        var second = SubscriptionData.Payment(october.Id, 400m, "SYNTHETIC-BANK-02");
        db.SubscriptionPayments.AddRange(first, second); await db.SaveChangesAsync(Token);
        db.SubscriptionPayments.Add(SubscriptionData.Reversal(second)); await db.SaveChangesAsync(Token);
        db.SubscriptionPayments.Add(SubscriptionData.Payment(november.Id, 1000m, "SYNTHETIC-FUTURE-PAID")); await db.SaveChangesAsync(Token);
        var before = await SubscriptionData.Snapshot(db);
        using var response = await owner.GetAsync("/api/subscription?page=1&pageSize=12", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadAsStringAsync(Token);
        var body = JsonSerializer.Deserialize<SubscriptionEndpoints.SubscriptionResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(body); Assert.Equal(new DateOnly(2026, 10, 29), body.AsOf);
        Assert.Equal("Europe/Istanbul", body.TimeZone); Assert.Equal("TRY", body.Currency);
        Assert.True(body.Recorded); Assert.False(body.RestrictionsApplied); Assert.Equal(12, body.TotalCount);
        Assert.Equal(12, body.Periods.Length); Assert.Equal(new DateOnly(2026, 11, 1), body.Periods[0].StartsOn);
        Assert.Equal("Paid", body.Periods[0].PaymentState);
        var current = Assert.IsType<SubscriptionEndpoints.PeriodResponse>(body.CurrentPeriod);
        Assert.Equal("1000.00", current.AmountTry); Assert.Equal("600.00", current.PaidTry); Assert.Equal("400.00", current.OutstandingTry);
        Assert.Equal("Partial", current.PaymentState); Assert.True(current.Overdue);
        foreach (var secret in new[] { "SYNTHETIC-BANK", "RecordedBy", "RequestKey", "RequestHash", "Reason", "Reference" })
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        using var pageResponse = await owner.GetAsync("/api/subscription?page=2&pageSize=5", Token);
        var page = await pageResponse.Content.ReadFromJsonAsync<SubscriptionEndpoints.SubscriptionResponse>(Token);
        Assert.NotNull(page); Assert.Equal(5, page.Periods.Length); Assert.Equal(2, page.Page); Assert.True(page.HasMore);
        using var lastResponse = await owner.GetAsync("/api/subscription?page=999&pageSize=5", Token);
        var last = await lastResponse.Content.ReadFromJsonAsync<SubscriptionEndpoints.SubscriptionResponse>(Token);
        Assert.NotNull(last); Assert.Equal(3, last.Page); Assert.Equal(2, last.Periods.Length); Assert.False(last.HasMore);
        Assert.Equal(before, await SubscriptionData.Snapshot(db));
        using var appointments = await owner.GetAsync("/api/appointments?date=2026-10-29", Token);
        Assert.Equal(HttpStatusCode.OK, appointments.StatusCode);
        using var export = await owner.GetAsync("/api/appointments/export?from=2026-10-29&to=2026-10-29", Token);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal(before, await SubscriptionData.Snapshot(db));
    }

    [Fact]
    public async Task MissingPaidAndDueTodayStatesDoNotRestrictBookingOrPretendAnUnrecordedPeriodExists()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed);
        var empty = await owner.GetFromJsonAsync<SubscriptionEndpoints.SubscriptionResponse>("/api/subscription", Token);
        Assert.NotNull(empty); Assert.False(empty.Recorded); Assert.False(empty.RestrictionsApplied); Assert.Null(empty.CurrentPeriod); Assert.Empty(empty.Periods);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var period = SubscriptionData.Period(new(2026, 10, 29)); period.DueOn = new(2026, 10, 29); period.AmountTry = 96.77m;
        db.SubscriptionPeriods.Add(period); await db.SaveChangesAsync(Token);
        var due = await owner.GetFromJsonAsync<SubscriptionEndpoints.SubscriptionResponse>("/api/subscription", Token);
        Assert.NotNull(due?.CurrentPeriod); Assert.Equal("Unpaid", due.CurrentPeriod.PaymentState); Assert.False(due.CurrentPeriod.Overdue);
        db.SubscriptionPayments.Add(SubscriptionData.Payment(period.Id, 96.77m, "SYNTHETIC-PRORATED")); await db.SaveChangesAsync(Token);
        var paid = await owner.GetFromJsonAsync<SubscriptionEndpoints.SubscriptionResponse>("/api/subscription", Token);
        Assert.NotNull(paid?.CurrentPeriod); Assert.Equal("Paid", paid.CurrentPeriod.PaymentState);
        Assert.Equal("96.77", paid.CurrentPeriod.PaidTry); Assert.Equal("0.00", paid.CurrentPeriod.OutstandingTry); Assert.False(paid.CurrentPeriod.Overdue); Assert.False(paid.RestrictionsApplied);
    }

    [Fact]
    public async Task AnonymousPendingMfaStaffAndWritesAreDeniedAndReadQuotaHasRealRetryAfter()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var anonymous = app.CreateClient(); using var pending = app.CreateClient(); await PasswordStepAsync(pending);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        using var owner = await InviteOwnerAsync(seed);
        foreach (var client in new[] { anonymous, pending })
        { using var denied = await client.GetAsync("/api/subscription", Token); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); }
        using (var denied = await staff.GetAsync("/api/subscription", Token)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await SubscriptionData.Snapshot(db);
        foreach (var query in new[] { "?page=0", "?page=1000001", "?pageSize=0", "?pageSize=25", "?pageSize=abc", "?instanceId=other", "?firmId=other" })
        { using var invalid = await owner.GetAsync("/api/subscription" + query, Token); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        { using var request = new HttpRequestMessage(method, "/api/subscription"); using var denied = await owner.SendAsync(request, Token); Assert.False(denied.IsSuccessStatusCode); }
        HttpResponseMessage? limited = null;
        for (var attempt = 0; attempt < 35; attempt++)
        {
            var response = await owner.GetAsync("/api/subscription", Token);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) { limited = response; break; }
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); response.Dispose();
        }
        Assert.NotNull(limited); using (limited) Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        using var otherRead = await owner.GetAsync("/api/appointments?date=2026-10-29", Token);
        Assert.Equal(HttpStatusCode.OK, otherRead.StatusCode);
        Assert.Equal(before, await SubscriptionData.Snapshot(db));
    }
}

internal static class SubscriptionData
{
    internal static SubscriptionPeriod Period(DateOnly start) => new()
    {
        StartsOn = start,
        EndsOn = new DateOnly(start.Year, start.Month, 1).AddMonths(1),
        DueOn = start,
        AmountTry = 1000m,
        RecordedBy = Guid.NewGuid(),
        RecordedAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
    internal static SubscriptionPayment Payment(Guid period, decimal amount, string reference) => new()
    {
        PeriodId = period,
        AmountTry = amount,
        PaidOn = new(2026, 10, 29),
        Reference = reference,
        RequestKey = Guid.NewGuid(),
        RequestHash = new('A', 64),
        RecordedBy = Guid.NewGuid(),
        RecordedAt = new(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc)
    };
    internal static SubscriptionPayment Reversal(SubscriptionPayment original) => new()
    {
        PeriodId = original.PeriodId,
        AmountTry = -original.AmountTry,
        PaidOn = original.PaidOn,
        ReversesPaymentId = original.Id,
        Reason = "Sentetik düzeltme",
        RequestKey = Guid.NewGuid(),
        RequestHash = new('B', 64),
        RecordedBy = Guid.NewGuid(),
        RecordedAt = original.RecordedAt.AddMinutes(1)
    };
    internal static async Task<Dictionary<string, string>> Snapshot(AppDbContext db)
    {
        await db.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connection = db.Database.GetDbConnection(); var names = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename";
            await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken)) names.Add(reader.GetString(0));
        }
        var result = new Dictionary<string, string>();
        foreach (var name in names)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT md5(COALESCE(string_agg(row_to_json(t)::text, '' ORDER BY row_to_json(t)::text),'')) FROM \"" + name.Replace("\"", "\"\"") + "\" t";
            result[name] = (string)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        }
        return result;
    }
}
