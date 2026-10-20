using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Server.Features.Scheduling;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using Fixture = Server.Tests.BookingVerificationTests.Fixture;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class BookingSmsBudgetTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string FilePath(string file) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", file));

    [Fact]
    public async Task ParallelDifferentRequestsCannotOverspendAndRejectedRequestWritesNothing()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        await using (var db = f.Db()) await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.DailyTry, 0.50m), Token);
        var responses = await Task.WhenAll(f.Post(client, "request", f.Request()), f.Post(client, "request", f.Request("05000000002")));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Accepted);
            var limited = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.TooManyRequests);
            Assert.Equal(54000, limited.Headers.RetryAfter?.Delta?.TotalSeconds);
        }
        finally { foreach (var response in responses) response.Dispose(); }
        await using var check = f.Db(); Assert.Single(await check.BookingVerifications.ToArrayAsync(Token));
        Assert.Single(await check.BookingVerificationQuotaEvents.ToArrayAsync(Token));
        var attempt = Assert.Single(await check.BookingSmsAttempts.ToArrayAsync(Token));
        Assert.Equal(0.50m, attempt.ReservedTry); Assert.Null(attempt.StartedAt); Assert.Equal("Reserved", attempt.Outcome);
        Assert.Empty(f.Sms.Calls);
    }

    [Fact]
    public async Task FirstWorkerCallUsesExistingReservationAndStoresProviderReferenceOnce()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        f.Sms.ProviderReference = "17377215342605050417149344";
        await f.RequestAsync(client);
        await Task.WhenAll(f.Work(), f.Work());
        await using var db = f.Db(); var attempt = Assert.Single(await db.BookingSmsAttempts.ToArrayAsync(Token));
        Assert.Single(f.Sms.Calls); Assert.Equal(0.50m, attempt.ReservedTry); Assert.Equal(1, attempt.Number);
        Assert.NotNull(attempt.StartedAt); Assert.Equal("Accepted", attempt.Outcome); Assert.Equal(f.Sms.ProviderReference, attempt.ProviderReference);
        Assert.Equal(1, (await db.BookingSmsPeriods.SingleAsync(x => x.Key == "D20261017", Token)).Attempts);
    }

    [Fact]
    public async Task LostLeaseKeepsBudgetEvenIfLateProviderReplySaysAccepted()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        await using (var db = f.Db()) await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.DailyTry, 0.50m), Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<BookingSmsOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Sms.ProviderReference = "123456789";
        f.Sms.Result = token => { entered.SetResult(); return reply.Task.WaitAsync(token); };
        await f.RequestAsync(client); var firstWorker = f.Work();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        f.Clock.Now += TimeSpan.FromSeconds(31); await f.Work();
        reply.SetResult(BookingSmsOutcome.Accepted); await firstWorker;
        await f.RestartAsync(); using var restarted = f.Client(); await f.Work();
        using var limited = await f.Post(restarted, "request", f.Request("05000000002"));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode); Assert.Single(f.Sms.Calls);
        await using var check = f.Db(); var attempt = await check.BookingSmsAttempts.SingleAsync(Token);
        Assert.Equal("Unknown", attempt.Outcome); Assert.Equal(0.50m, attempt.ReservedTry); Assert.NotNull(attempt.StartedAt);
        Assert.Equal(f.Sms.ProviderReference, attempt.ProviderReference); Assert.Equal("Unknown", (await check.BookingSmsJobs.SingleAsync(Token)).Status);
    }

    [Fact]
    public async Task EachDefiniteRetryReservesOneAdditionalAttemptAcrossRestart()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        f.Sms.Result = _ => Task.FromResult(BookingSmsOutcome.TemporaryFailure);
        await f.RequestAsync(client); await f.Work();
        await f.RestartAsync(); f.Clock.Now += TimeSpan.FromSeconds(15); await f.Work();
        f.Clock.Now += TimeSpan.FromSeconds(30); await f.Work(); await f.Work();
        await using var db = f.Db(); var attempts = await db.BookingSmsAttempts.OrderBy(x => x.Number).ToArrayAsync(Token);
        Assert.Equal(new[] { 1, 2, 3 }, attempts.Select(x => x.Number)); Assert.Equal(1.50m, attempts.Sum(x => x.ReservedTry));
        Assert.All(attempts, x => Assert.Equal("TemporaryFailure", x.Outcome)); Assert.Equal(3, f.Sms.Calls.Count);
        Assert.Equal("Failed", (await db.BookingSmsJobs.SingleAsync(Token)).Status);
    }

    [Fact]
    public async Task RetryCannotSpendBudgetAlreadyConsumedByFirstCall()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        await using (var db = f.Db()) await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.DailyTry, 0.50m), Token);
        f.Sms.Result = _ => Task.FromResult(BookingSmsOutcome.TemporaryFailure);
        var challenge = await f.RequestAsync(client); await f.Work(); f.Clock.Now += TimeSpan.FromSeconds(15); await f.Work();
        Assert.Single(f.Sms.Calls);
        await using (var db = f.Db())
        {
            Assert.Single(await db.BookingSmsAttempts.ToArrayAsync(Token));
            Assert.Equal(1, (await db.BookingSmsJobs.SingleAsync(Token)).Attempts);
        }
        f.Clock.Now = challenge.ExpiresAt; await f.Work();
        await using var final = f.Db(); Assert.Equal("Expired", (await final.BookingSmsJobs.SingleAsync(Token)).Status);
        Assert.Equal(0.50m, await final.BookingSmsAttempts.SumAsync(x => x.ReservedTry, Token));
    }

    [Fact]
    public async Task UnstartedResendReleasesOldHoldButDoesNotReleaseAnUnknownCall()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        await using (var db = f.Db()) await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.TotalTry, 0.50m), Token);
        var challenge = await f.RequestAsync(client); f.Clock.Now += TimeSpan.FromSeconds(60);
        var command = new BookingVerificationEndpoints.Resend(challenge.ChallengeId, challenge.ChallengeSecret, Guid.NewGuid());
        using var resent = await f.Post(client, "resend", command); Assert.Equal(HttpStatusCode.Accepted, resent.StatusCode);
        using var replay = await f.Post(client, "resend", command); Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        f.Sms.Result = _ => Task.FromResult(BookingSmsOutcome.Unknown); await f.Work();
        await f.RestartAsync(); using var restarted = f.Client(); f.Clock.Now += TimeSpan.FromSeconds(60);
        using var limited = await f.Post(restarted, "resend", command with { ResendKey = Guid.NewGuid() }); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await using var check = f.Db(); var rows = await check.BookingSmsAttempts.ToArrayAsync(Token);
        Assert.Equal(2, rows.Length); Assert.Single(rows, x => x.Outcome == "Released" && x.StartedAt is null);
        Assert.Single(rows, x => x.Outcome == "Unknown" && x.StartedAt != null); Assert.Single(f.Sms.Calls);
        Assert.Equal(0.50m, rows.Where(x => x.Outcome != "Released").Sum(x => x.ReservedTry));
    }

    [Fact]
    public async Task ExpiryReleasesOnlyUnstartedHoldAndOldJobCleanupPreservesSpentHistory()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        var challenge = await f.RequestAsync(client); f.Clock.Now = challenge.ExpiresAt; await f.Work();
        await using (var db = f.Db()) Assert.Equal("Released", (await db.BookingSmsAttempts.SingleAsync(Token)).Outcome);
        await f.RequestAsync(client); await f.Work(); f.Clock.Now += TimeSpan.FromDays(2); await f.Work();
        await using var check = f.Db(); Assert.Empty(await check.BookingSmsJobs.ToArrayAsync(Token));
        Assert.Equal(2, await check.BookingSmsAttempts.CountAsync(Token));
        Assert.Equal(0.50m, await check.BookingSmsAttempts.Where(x => x.StartedAt != null).SumAsync(x => x.ReservedTry, Token));
    }

    [Fact]
    public async Task UnstartedWorkIsRecheckedAgainstActualIstanbulCallDayAndMonth()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        await using (var db = f.Db()) await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.DailyTry, 0.50m), Token);
        f.Clock.Now = DateTimeOffset.Parse("2026-10-31T20:59:00Z", System.Globalization.CultureInfo.InvariantCulture);
        using var first = await f.Post(client, "request", f.Request() with { StartsAt = "2026-11-01T12:00:00+03:00" }); Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        f.Clock.Now += TimeSpan.FromMinutes(1);
        using var next = await f.Post(client, "request", f.Request("05000000002") with { StartsAt = "2026-11-01T12:00:00+03:00" }); Assert.Equal(HttpStatusCode.Accepted, next.StatusCode);
        await f.Work(); Assert.Empty(f.Sms.Calls); await f.Work(); Assert.Single(f.Sms.Calls);
        await using var check = f.Db(); var started = Assert.Single(await check.BookingSmsAttempts.Where(x => x.StartedAt != null).ToArrayAsync(Token));
        Assert.Equal(f.Clock.Now, started.ReservedAt); Assert.Equal(0.50m, started.ReservedTry);
    }

    [Theory]
    [InlineData("calls")]
    [InlineData("money")]
    public async Task TotalPilotLimitCannotBeResetByNextDayOrCalendarMonth(string scope)
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        await using (var db = f.Db())
        {
            if (scope == "calls") await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.TotalCalls, 1), Token);
            else await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.TotalTry, 0.50m), Token);
        }
        await f.RequestAsync(client); await f.Work();
        f.Clock.Now = DateTimeOffset.Parse("2026-11-01T06:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        await f.RestartAsync(); using var restarted = f.Client();
        using var limited = await f.Post(restarted, "request", f.Request("05000000002") with { StartsAt = "2026-11-02T12:00:00+03:00" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await using var check = f.Db(); Assert.Single(await check.BookingSmsAttempts.ToArrayAsync(Token)); Assert.Single(f.Sms.Calls);
    }

    [Fact]
    public async Task NewApprovalCannotEraseInstallationMonthConsumption()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); await f.RequestAsync(client); await f.Work();
        await using (var db = f.Db())
        {
            var original = await db.BookingSmsBudgetPolicies.AsNoTracking().SingleAsync(Token);
            original.Id = Guid.NewGuid(); original.MonthlyTry = 0.50m;
            db.BookingSmsBudgetPolicies.Add(original); await db.SaveChangesAsync(Token); f.BudgetApprovalId = original.Id;
        }
        f.Clock.Now += TimeSpan.FromDays(1); await f.RestartAsync(); using var next = f.Client();
        using var limited = await f.Post(next, "request", f.Request("05000000002")); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(1177200, limited.Headers.RetryAfter?.Delta?.TotalSeconds);
        await using var check = f.Db(); Assert.Single(await check.BookingSmsAttempts.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("revoked")]
    [InlineData("price")]
    [InlineData("expired")]
    public async Task InvalidOrUnreconciledBudgetFailsAtStartupWithoutSending(string kind)
    {
        await using var f = new Fixture(); await f.StartAsync();
        await using (var db = f.Db())
        {
            if (kind == "missing") await db.BookingSmsBudgetPolicies.ExecuteDeleteAsync(Token);
            if (kind == "revoked") await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now), Token);
        }
        if (kind == "price") f.Sms.MaximumUnitPriceTry = 0.51m;
        if (kind == "expired") f.Clock.Now += TimeSpan.FromDays(31);
        Assert.Throws<InvalidOperationException>(() => f.Client()); Assert.Empty(f.Sms.Calls);
        f.DispatchEnabled = false; await f.RestartAsync(); using var stopped = f.Client();
        using var response = await f.Post(stopped, "request", f.Request()); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task DispatchKillSwitchStopsNewCodesWhileVerifiedBookingAndAccessContinue()
    {
        await using var f = new Fixture { AccessEnabled = true }; await f.StartAsync(); using var client = f.Client();
        var verified = await PublicAppointmentTests.Verified(f, client);
        f.DispatchEnabled = false; await f.RestartAsync(); using var stopped = f.Client(); await f.Work();
        using var request = await f.Post(stopped, "request", f.Request("05000000002")); Assert.Equal(HttpStatusCode.ServiceUnavailable, request.StatusCode);
        using var resend = await f.Post(stopped, "resend", new BookingVerificationEndpoints.Resend(verified.ChallengeId, verified.ChallengeSecret, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resend.StatusCode);
        using var replay = await f.Post(stopped, "verify", new BookingVerificationEndpoints.Verify(verified.ChallengeId, verified.ChallengeSecret, null)); Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var booking = await PostAsync(stopped, "/api/public-booking/appointments", verified, await GetCsrfAsync(stopped)); Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        var result = await booking.Content.ReadFromJsonAsync<PublicAppointmentEndpoints.Response>(Token); Assert.NotNull(result);
        using var access = await PostAsync(stopped, $"/api/public-booking/appointments/{result.Id}/access",
            new PublicAppointmentAccessEndpoints.Issue(verified.ChallengeId, verified.ChallengeSecret, false), await GetCsrfAsync(stopped));
        Assert.Equal(HttpStatusCode.Created, access.StatusCode); Assert.Single(f.Sms.Calls);
    }

    [Fact]
    public async Task OldUnreservedQueueAndRevokedBudgetNeverDispatch()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); await f.RequestAsync(client);
        await using (var db = f.Db()) await db.BookingSmsAttempts.ExecuteDeleteAsync(Token);
        await f.Work(); Assert.Empty(f.Sms.Calls);
        await using (var db = f.Db()) Assert.Equal("Cancelled", (await db.BookingSmsJobs.SingleAsync(Token)).Status);
        f.Clock.Now += TimeSpan.FromMinutes(1); await f.RequestAsync(client, "05000000002");
        await using (var db = f.Db()) await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now), Token);
        await f.Work(); await f.Work(); Assert.Empty(f.Sms.Calls);
        await using var check = f.Db(); Assert.Equal("Released", (await check.BookingSmsAttempts.SingleAsync(Token)).Outcome);
    }

    [Fact]
    public async Task MigrationAndActualGrantsAllowBudgetWorkButRejectApprovalAndPriceRewrites()
    {
        await using var f = new Fixture(); await f.StartAsync();
        await using (var db = f.Db())
        {
            Assert.False(db.Database.HasPendingModelChanges()); await db.Database.MigrateAsync(Token); await db.Database.MigrateAsync(Token);
            await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync("20261017201619_PublicAppointmentAccess", Token));
            await db.Database.ExecuteSqlRawAsync("CREATE ROLE app_user LOGIN PASSWORD 'SyntheticApp!123'; GRANT USAGE ON SCHEMA public TO app_user", Token);
            var script = await File.ReadAllTextAsync(FilePath("deploy/Apply-LocalMigration.ps1"), Token);
            var start = script.IndexOf("@'", StringComparison.Ordinal) + 2;
            await db.Database.ExecuteSqlRawAsync(script[start..script.IndexOf("'@ |", start, StringComparison.Ordinal)], Token);
        }
        f.AppConnectionString = new NpgsqlConnectionStringBuilder(f.Database.GetConnectionString()) { Username = "app_user", Password = "SyntheticApp!123" }.ConnectionString;
        await f.RestartAsync(); using var client = f.Client(); await f.RequestAsync(client); await f.Work();
        await using var limited = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(f.AppConnectionString).Options);
        Assert.Single(await limited.BookingSmsAttempts.ToArrayAsync(Token));
        foreach (var sql in new[] { "UPDATE \"BookingSmsBudgetPolicies\" SET \"TotalTry\"=100", "DELETE FROM \"BookingSmsBudgetPolicies\"",
            "TRUNCATE \"BookingSmsBudgetPolicies\"", "UPDATE \"BookingSmsAttempts\" SET \"ReservedTry\"=0.01",
            "UPDATE \"BookingSmsAttempts\" SET \"PolicyId\"=gen_random_uuid()", "TRUNCATE \"BookingSmsAttempts\"" })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => limited.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("42501", error.SqlState);
        }
        await using var admin = f.Db();
        foreach (var sql in new[] { "UPDATE \"BookingSmsBudgetPolicies\" SET \"DailyCalls\"=21", "UPDATE \"BookingSmsBudgetPolicies\" SET \"UnitPriceTry\"=0.51",
            "UPDATE \"BookingSmsAttempts\" SET \"ReservedTry\"=-1", "UPDATE \"BookingSmsAttempts\" SET \"Outcome\"='Released'" })
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => admin.Database.ExecuteSqlRawAsync(sql, Token))).SqlState);
    }

    [Fact]
    public async Task RestoringOlderBudgetRequiresRevocationAndCannotReuseOldApproval()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); await f.RequestAsync(client); await f.Work();
        var dump = await f.Database.ExecAsync(new[] { "pg_dump", "-U", "postgres", "-d", "p02_recovery", "-Fc", "-f", "/tmp/budget.dump" }, Token); Assert.Equal(0, dump.ExitCode);
        await f.RequestAsync(client, "05000000002"); await f.Work(); Assert.Equal(2, f.Sms.Calls.Count);
        foreach (var command in new[] { new[] { "createdb", "-U", "postgres", "budget_restored" }, new[] { "pg_restore", "-U", "postgres", "-d", "budget_restored", "/tmp/budget.dump" } })
            Assert.Equal(0, (await f.Database.ExecAsync(command, Token)).ExitCode);
        var script = await File.ReadAllTextAsync(FilePath("deploy/Invalidate-RestoredPublicAccess.sql"), Token);
        Assert.Equal(0, (await f.Database.ExecAsync(new[] { "psql", "-U", "postgres", "-d", "budget_restored", "-v", "ON_ERROR_STOP=1", "-c", script }, Token)).ExitCode);
        f.DatabaseName = "budget_restored"; await f.RestartAsync(); Assert.Throws<InvalidOperationException>(() => f.Client());
        f.DispatchEnabled = false; await f.RestartAsync(); using var stopped = f.Client(); await f.Work(); await f.Work();
        using var rejected = await f.Post(stopped, "request", f.Request("05000000003")); Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        await using var check = f.Db(); Assert.NotNull((await check.BookingSmsBudgetPolicies.SingleAsync(Token)).RevokedAt);
        Assert.Single(await check.BookingSmsAttempts.ToArrayAsync(Token)); Assert.Equal(2, f.Sms.Calls.Count);
    }
}
