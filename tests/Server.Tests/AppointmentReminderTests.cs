using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
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
public sealed class AppointmentReminderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Sms : IAppointmentReminderDelivery
    {
        public bool Available => true;
        public decimal MaximumUnitPriceTry => 0.40m;
        public ConcurrentQueue<(string Phone, DateTimeOffset StartsAt, Guid Id)> Calls { get; } = new();
        public Func<CancellationToken, Task<BookingSmsOutcome>> Result { get; set; } = _ => Task.FromResult(BookingSmsOutcome.Accepted);
        public async Task<BookingSmsResult> SendAsync(string phone, DateTimeOffset startsAt, Guid id, CancellationToken token)
        { Calls.Enqueue((phone, startsAt, id)); return new(await Result(token), "123456789"); }
    }
    private static Fixture New(bool otp = true, bool enabled = true) => new()
    { Enabled = otp, RemindersEnabled = enabled, ReminderDelivery = new Sms(), AccessEnabled = true };
    private static Sms Carrier(Fixture f) => Assert.IsType<Sms>(f.ReminderDelivery);
    private static Task Work(Fixture f) => ActivatorUtilities.CreateInstance<AppointmentReminderWorker>(f.App.Services).ProcessOneAsync(Token);
    private static async Task<HttpClient> Owner(Fixture f)
    {
        using var scope = f.App.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Owner"))).Succeeded);
        var owner = new AppUser { UserName = Email, Email = Email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(owner, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(owner, "Owner")).Succeeded);
        Assert.True((await users.ResetAuthenticatorKeyAsync(owner)).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(owner, true)).Succeeded);
        var client = f.Client(); await PasswordStepAsync(client);
        await CompleteMfaAsync(client, Assert.IsType<string>(await users.GetAuthenticatorKeyAsync(owner)));
        return client;
    }
    private static AppointmentEndpoints.CreateRequest Command(Fixture f, string phone = "05000000001") =>
        new(Guid.NewGuid(), f.ServiceId, f.ServiceVersion, f.StaffId, "2026-10-18T12:00:00+03:00", "Sentetik müşteri", phone);
    private static async Task<AppointmentEndpoints.AppointmentResponse> Book(Fixture f, HttpClient owner,
        bool pending = false, string phone = "05000000001")
    {
        using var response = await PostAsync(owner, pending ? "/api/appointments/pending" : "/api/appointments/", Command(f, phone), await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
    }
    private static async Task<AppointmentEndpoints.AppointmentResponse> Move(Fixture f, HttpClient owner, AppointmentEndpoints.AppointmentResponse appointment, string start)
    {
        using var response = await PostAsync(owner, $"/api/appointments/{appointment.Id}/move",
            new AppointmentMoveEndpoints.Request(appointment.Version, Guid.NewGuid(), f.StaffId, start), await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
    }
    private static async Task Due(Fixture f)
    { await using var db = f.Db(); f.Clock.Now = (await db.AppointmentReminderJobs.SingleAsync(Token)).DueAt; }
    private static async Task Relogin(Fixture f, HttpClient owner)
    {
        using var scope = f.App.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = Assert.IsType<AppUser>(await users.FindByEmailAsync(Email));
        await PasswordStepAsync(owner); await CompleteMfaAsync(owner, Assert.IsType<string>(await users.GetAuthenticatorKeyAsync(user)));
    }

    [Fact]
    public async Task ParallelReplayWritesOneEncryptedJobAndOtpDisabledWorkerSendsOnceAcrossRestart()
    {
        await using var f = New(otp: false); await f.StartAsync(); using var owner = await Owner(f);
        var command = Command(f); var csrf = await GetCsrfAsync(owner);
        var writes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostAsync(owner, "/api/appointments/", command, csrf)));
        try { Assert.All(writes, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK)); }
        finally { foreach (var write in writes) write.Dispose(); }
        await using (var db = f.Db())
        {
            Assert.Single(await db.Appointments.ToArrayAsync(Token)); var job = Assert.Single(await db.AppointmentReminderJobs.ToArrayAsync(Token));
            Assert.Equal(job.StartsAt.AddHours(-3), job.DueAt); Assert.Equal(job.DueAt.AddMinutes(15), job.Deadline);
            Assert.DoesNotContain("+905", job.ProtectedPhone ?? ""); Assert.Empty(await db.BookingSmsAttempts.ToArrayAsync(Token));
        }
        await Work(f); Assert.Empty(Carrier(f).Calls); await Due(f);
        await Task.WhenAll(Work(f), Work(f)); await f.RestartAsync(); await Work(f);
        Assert.Single(Carrier(f).Calls); Assert.Empty(f.Sms.Calls);
        await using var check = f.Db(); var final = await check.AppointmentReminderJobs.SingleAsync(Token);
        Assert.Equal("Accepted", final.Status); Assert.Null(final.ProtectedPhone); Assert.NotNull(final.StartedOnceAt);
        var attempt = Assert.Single(await check.BookingSmsAttempts.ToArrayAsync(Token));
        Assert.Equal("Reminder", attempt.Purpose); Assert.Equal("Accepted", attempt.Outcome); Assert.Equal(0.50m, attempt.ReservedTry);
        Assert.Equal("123456789", attempt.ProviderReference); Assert.Equal(f.Clock.Now, attempt.ReservedAt);
    }

    [Theory]
    [InlineData(false, "05000000001")]
    [InlineData(true, "03200000001")]
    public async Task DisabledAndLandlineKeepValidAppointmentWithoutQueue(bool enabled, string phone)
    {
        await using var f = New(enabled: enabled); await f.StartAsync(); using var owner = await Owner(f);
        await Book(f, owner, phone: phone); await Work(f);
        await using var db = f.Db(); Assert.Single(await db.Appointments.ToArrayAsync(Token));
        Assert.Empty(await db.AppointmentReminderJobs.ToArrayAsync(Token)); Assert.Empty(await db.BookingSmsAttempts.ToArrayAsync(Token));
        Assert.Empty(Carrier(f).Calls);
    }

    [Theory]
    [InlineData(-1, "Pending")]
    [InlineData(0, "Pending")]
    [InlineData(1, "TooLate")]
    public async Task ConfirmationBoundaryIsThreeHoursAndPendingDoesNotSchedule(int seconds, string status)
    {
        await using var f = New(); await f.StartAsync();
        f.Clock.Now = DateTimeOffset.Parse("2026-10-18T05:59:00Z");
        using var owner = await Owner(f);
        var pending = await Book(f, owner, pending: true);
        await using (var db = f.Db()) Assert.Empty(await db.AppointmentReminderJobs.ToArrayAsync(Token));
        f.Clock.Now = DateTimeOffset.Parse("2026-10-18T06:00:00Z").AddSeconds(seconds);
        using var response = await PostAsync(owner, $"/api/appointments/{pending.Id}/confirm",
            new AppointmentStatusEndpoints.Request(pending.Version, Guid.NewGuid()), await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var check = f.Db(); Assert.Equal(status, (await check.AppointmentReminderJobs.SingleAsync(Token)).Status);
        if (seconds > 0) { await Work(f); Assert.Empty(Carrier(f).Calls); }
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(899, true)]
    [InlineData(900, false)]
    public async Task DispatchWindowIncludesDueAndExcludesFifteenMinuteDeadline(int seconds, bool sends)
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f);
        await Book(f, owner); await Due(f); f.Clock.Now += TimeSpan.FromSeconds(seconds);
        await Work(f); Assert.Equal(sends ? 1 : 0, Carrier(f).Calls.Count);
        if (seconds == 900) { await f.RestartAsync(); await Work(f); Assert.Empty(Carrier(f).Calls); }
    }

    [Fact]
    public async Task MoveReusesJobNoOpPreservesItAndCancellationCannotLeaveWork()
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f);
        var original = await Book(f, owner);
        var moved = await Move(f, owner, original, "2026-10-18T14:00:00+03:00");
        var noOp = await Move(f, owner, moved, "2026-10-18T14:00:00+03:00"); Assert.Equal(moved.Version, noOp.Version);
        await using (var db = f.Db())
        { var job = Assert.Single(await db.AppointmentReminderJobs.ToArrayAsync(Token)); Assert.Equal(moved.Version, job.AppointmentVersion); Assert.Equal(moved.StartsAt.AddHours(-3), job.DueAt.UtcDateTime); }
        using var cancelled = await PostAsync(owner, $"/api/appointments/{moved.Id}/cancel",
            new AppointmentStatusEndpoints.Request(moved.Version, Guid.NewGuid()), await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        await Due(f); await Work(f); Assert.Empty(Carrier(f).Calls);
        await using var check = f.Db(); Assert.Equal("Cancelled", (await check.AppointmentReminderJobs.SingleAsync(Token)).Status);
    }

    [Theory]
    [InlineData("Accepted")]
    [InlineData("Unknown")]
    [InlineData("TemporaryFailure")]
    public async Task AnyStartedCallPreventsAnotherReminderAfterMoveAndLongCleanup(string outcome)
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f);
        var original = await Book(f, owner); Carrier(f).Result = _ => Task.FromResult(Enum.Parse<BookingSmsOutcome>(outcome));
        await Due(f); await Relogin(f, owner); await Work(f);
        await Move(f, owner, original, "2026-10-18T14:00:00+03:00");
        f.Clock.Now += TimeSpan.FromHours(2); await Work(f); await f.Work();
        f.Clock.Now += TimeSpan.FromDays(33); await Work(f);
        Assert.Single(Carrier(f).Calls);
        await using var db = f.Db(); Assert.Single(await db.AppointmentReminderJobs.ToArrayAsync(Token));
        Assert.NotNull((await db.AppointmentReminderJobs.SingleAsync(Token)).StartedOnceAt);
    }

    [Fact]
    public async Task DefiniteRetriesConsumeSharedBudgetThreeTimesAndUnknownNeverRetries()
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f);
        await Book(f, owner); Carrier(f).Result = _ => Task.FromResult(BookingSmsOutcome.TemporaryFailure);
        await Due(f); await Work(f); await f.RestartAsync(); f.Clock.Now += TimeSpan.FromSeconds(15); await Work(f);
        f.Clock.Now += TimeSpan.FromSeconds(30); await Work(f); await Work(f);
        Assert.Equal(3, Carrier(f).Calls.Count);
        await using var db = f.Db(); Assert.Equal(1.50m, await db.BookingSmsAttempts.SumAsync(x => x.ReservedTry, Token));
        Assert.Equal("Failed", (await db.AppointmentReminderJobs.SingleAsync(Token)).Status);
    }

    [Fact]
    public async Task MixedOtpAndReminderCannotOverspendAndOtpMaintenanceKeepsLiveReminderAttempt()
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f); using var client = f.Client();
        await Book(f, owner); await Due(f);
        await using (var db = f.Db()) await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.DailyTry, 0.50m), Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<BookingSmsOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        Carrier(f).Result = token => { entered.SetResult(); return reply.Task.WaitAsync(token); };
        var sending = Work(f); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await f.Work();
        using var limited = await f.Post(client, "request", f.Request("05000000002") with { StartsAt = "2026-10-19T12:00:00+03:00" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await using (var db = f.Db()) Assert.Equal("Started", (await db.BookingSmsAttempts.SingleAsync(Token)).Outcome);
        reply.SetResult(BookingSmsOutcome.Accepted); await sending;
        await using var check = f.Db(); Assert.Single(await check.BookingSmsAttempts.ToArrayAsync(Token));
        Assert.Equal("Accepted", (await check.BookingSmsAttempts.SingleAsync(Token)).Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrLeaseExpiryDuringCallKeepsUnknownSpendAndLateReplyCannotResurrect(bool expire)
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f);
        var original = await Book(f, owner); await Due(f); await Relogin(f, owner);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<BookingSmsOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        Carrier(f).Result = token => { entered.SetResult(); return reply.Task.WaitAsync(token); };
        var sending = Work(f); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        if (expire) { f.Clock.Now += TimeSpan.FromSeconds(31); await Work(f); }
        else
        { using var cancelled = await PostAsync(owner, $"/api/appointments/{original.Id}/cancel", new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()), await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode); }
        reply.SetResult(BookingSmsOutcome.Accepted); await sending; await f.RestartAsync(); await Work(f);
        Assert.Single(Carrier(f).Calls); await using var db = f.Db(); var attempt = await db.BookingSmsAttempts.SingleAsync(Token);
        Assert.Equal("Unknown", attempt.Outcome); Assert.Equal("123456789", attempt.ProviderReference);
        Assert.Equal(0.50m, attempt.ReservedTry); Assert.NotNull(attempt.StartedAt);
        Assert.Equal(expire ? "Unknown" : "Cancelled", (await db.AppointmentReminderJobs.SingleAsync(Token)).Status);
    }

    [Fact]
    public async Task SuppressIsOwnerCsrfVersionProtectedIdempotentAndSurvivesMove()
    {
        await using var f = New(); await f.StartAsync(); f.App.UseKestrel(0);
        using var owner = await Owner(f); using var anonymous = f.Client();
        var original = await Book(f, owner, pending: true); var path = $"/api/appointments/{original.Id}/reminder/disable";
        var request = new AppointmentReminders.DisableRequest(original.Version);
        using var unauthorized = await PostAsync(anonymous, path, request, await GetCsrfAsync(anonymous)); Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var csrf = await PostAsync(owner, path, request, null); Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        using var conflict = await PostAsync(owner, path, request with { Version = Guid.NewGuid() }, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        for (var repeat = 0; repeat < 2; repeat++)
        { using var response = await PostAsync(owner, path, request, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); }
        using var confirm = await PostAsync(owner, $"/api/appointments/{original.Id}/confirm", new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()), await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var confirmed = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await confirm.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        await Move(f, owner, confirmed, "2026-10-18T14:00:00+03:00"); await Due(f); await Work(f);
        await using var db = f.Db(); var job = Assert.Single(await db.AppointmentReminderJobs.ToArrayAsync(Token));
        Assert.Equal("Suppressed", job.Status); Assert.NotNull(job.SuppressedBy); Assert.NotNull(job.SuppressedAt); Assert.Empty(Carrier(f).Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PublicConfirmedSchedulesAndPublicCancellationInvalidatesWhilePendingDoesNot(bool confirmed)
    {
        await using var f = New(); await f.StartAsync(); using var client = f.Client();
        await using (var db = f.Db()) await db.BookingPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.RequiresBusinessApproval, !confirmed), Token);
        var request = await PublicAppointmentTests.SeedVerified(f);
        using var created = await PostAsync(client, "/api/public-booking/appointments", request, await GetCsrfAsync(client)); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var booking = Assert.IsType<PublicAppointmentEndpoints.Response>(await created.Content.ReadFromJsonAsync<PublicAppointmentEndpoints.Response>(Token));
        await using (var db = f.Db()) Assert.Equal(confirmed ? 1 : 0, await db.AppointmentReminderJobs.CountAsync(Token));
        using var issued = await PostAsync(client, $"/api/public-booking/appointments/{booking.Id}/access", new PublicAppointmentAccessEndpoints.Issue(request.ChallengeId, request.ChallengeSecret, false), await GetCsrfAsync(client)); Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        var access = Assert.IsType<PublicAppointmentAccessEndpoints.Issued>(await issued.Content.ReadFromJsonAsync<PublicAppointmentAccessEndpoints.Issued>(Token));
        client.DefaultRequestHeaders.Authorization = new("Bearer", access.Token);
        using var cancelled = await PostAsync(client, $"/api/public-booking/appointments/{booking.Id}/cancel", new PublicAppointmentAccessEndpoints.Cancel(booking.Version, Guid.NewGuid()), await GetCsrfAsync(client)); Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        f.Clock.Now = new DateTimeOffset(booking.StartsAt).AddHours(-3); await Work(f); Assert.Empty(Carrier(f).Calls);
        await using var check = f.Db(); if (confirmed) Assert.Equal("Cancelled", (await check.AppointmentReminderJobs.SingleAsync(Token)).Status);
    }

    [Fact]
    public async Task RestoreRevokesBudgetAndSuppressesQueueWithoutResettingStartedHistory()
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f);
        await Book(f, owner); await Due(f); await Work(f);
        foreach (var operation in new[] { new[] { "pg_dump", "-U", "postgres", "-d", "p02_recovery", "-Fc", "-f", "/tmp/reminder.dump" },
            new[] { "pg_restore", "--file=/tmp/reminder-check.sql", "/tmp/reminder.dump" },
            new[] { "createdb", "-U", "postgres", "reminder_restored" },
            new[] { "pg_restore", "-U", "postgres", "-d", "reminder_restored", "/tmp/reminder.dump" } })
            Assert.Equal(0, (await f.Database.ExecAsync(operation, Token)).ExitCode);
        f.DatabaseName = "reminder_restored"; f.DispatchEnabled = false; await f.RestartAsync();
        var sql = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/Invalidate-RestoredPublicAccess.sql")), Token);
        await using (var db = f.Db()) { await db.Database.ExecuteSqlRawAsync(sql, Token); await db.Database.ExecuteSqlRawAsync(sql, Token); }
        await Work(f); Assert.Single(Carrier(f).Calls);
        await using var check = f.Db(); var job = await check.AppointmentReminderJobs.SingleAsync(Token);
        Assert.NotNull(job.StartedOnceAt); Assert.NotNull(job.SuppressedAt); Assert.Null(job.ProtectedPhone);
        Assert.NotNull((await check.BookingSmsBudgetPolicies.SingleAsync(Token)).RevokedAt);
        Assert.Single(await check.Appointments.ToArrayAsync(Token)); Assert.Single(await check.AppointmentAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task MigrationDownRejectsReminderRowsAndPurposeHistoryIndependently()
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f); await Book(f, owner);
        await using var db = f.Db(); var migrator = db.GetService<IMigrator>();
        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261020012834_BookingSmsBudget", Token));
        await Due(f); await Work(f); await db.AppointmentReminderJobs.ExecuteDeleteAsync(Token);
        await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261020012834_BookingSmsBudget", Token));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void MissingCarrierFailsClosedAndDisabledModeNeedsNoCredentials()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var security = new BookingVerificationSecurity(config, new BookingVerificationTests.Sms());
        var protection = new EphemeralDataProtectionProvider();
        Assert.False(new AppointmentReminders(config, protection, new DisabledAppointmentReminderDelivery(), security).Enabled);
        config["AppointmentReminders:Enabled"] = "true";
        Assert.Throws<InvalidOperationException>(() => new AppointmentReminders(config, protection, new DisabledAppointmentReminderDelivery(), security));
    }

    [Fact]
    public async Task ActualRestrictedRoleCanSendButCannotRewritePurposeIdentityOrDeleteTombstone()
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f); await Book(f, owner);
        await using (var db = f.Db())
        {
            await db.Database.ExecuteSqlRawAsync("CREATE ROLE app_user LOGIN PASSWORD 'SyntheticApp!123'; GRANT USAGE ON SCHEMA public TO app_user", Token);
            var script = await File.ReadAllTextAsync(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/Apply-LocalMigration.ps1")), Token);
            var start = script.IndexOf("@'", StringComparison.Ordinal) + 2;
            await db.Database.ExecuteSqlRawAsync(script[start..script.IndexOf("'@ |", start, StringComparison.Ordinal)], Token);
        }
        f.AppConnectionString = new NpgsqlConnectionStringBuilder(f.Database.GetConnectionString()) { Username = "app_user", Password = "SyntheticApp!123" }.ConnectionString;
        await f.RestartAsync(); await Due(f); await Work(f); Assert.Single(Carrier(f).Calls);
        await using var limited = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(f.AppConnectionString).Options);
        foreach (var sql in new[] { "UPDATE \"BookingSmsAttempts\" SET \"Purpose\"='Otp'", "UPDATE \"BookingSmsAttempts\" SET \"ReservedTry\"=0.01",
            "UPDATE \"AppointmentReminderJobs\" SET \"BudgetApprovalId\"=gen_random_uuid()", "UPDATE \"AppointmentReminderJobs\" SET \"AppointmentId\"=gen_random_uuid()",
            "DELETE FROM \"AppointmentReminderJobs\"", "TRUNCATE \"AppointmentReminderJobs\"" })
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => limited.Database.ExecuteSqlRawAsync(sql, Token))).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => limited.Database.ExecuteSqlRawAsync(
            "UPDATE \"AppointmentReminderJobs\" SET \"StartedOnceAt\"=NULL, \"Attempts\"=0", Token))).SqlState);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("price")]
    [InlineData("off")]
    public async Task BudgetRecheckOrDispatchSwitchPreventsCallWithoutRejectingAppointment(string reason)
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f); await Book(f, owner); await Due(f);
        await using (var db = f.Db())
        {
            if (reason == "revoked") await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, f.Clock.Now), Token);
            if (reason == "price") await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.UnitPriceTry, 0.10m), Token);
        }
        if (reason == "off") { f.DispatchEnabled = false; await f.RestartAsync(); }
        await Work(f); Assert.Empty(Carrier(f).Calls);
        await using var check = f.Db(); Assert.Equal("Confirmed", (await check.AppointmentTimeBlocks.SingleAsync(Token)).Status);
        Assert.Empty(await check.BookingSmsAttempts.ToArrayAsync(Token));
    }

    [Fact]
    public async Task FailedMoveRollsBackReminderAlongWithAppointment()
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f); var original = await Book(f, owner);
        using var other = await PostAsync(owner, "/api/appointments/", Command(f) with { StartsAt = "2026-10-18T14:00:00+03:00" }, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        using var rejected = await PostAsync(owner, $"/api/appointments/{original.Id}/move", new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), f.StaffId, "2026-10-18T14:00:00+03:00"), await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await using var db = f.Db(); var job = await db.AppointmentReminderJobs.SingleAsync(x => x.AppointmentId == original.Id, Token);
        Assert.Equal(original.Version, job.AppointmentVersion); Assert.Equal(original.StartsAt, job.StartsAt.UtcDateTime);
        Assert.Equal(2, await db.AppointmentReminderJobs.CountAsync(Token)); Assert.Empty(await db.AppointmentMoveCommands.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ActualCallUsesNewIstanbulDayAndMonthWhileKeepingTotalSpend()
    {
        await using var f = New(); await f.StartAsync(); f.Clock.Now = DateTimeOffset.Parse("2026-10-31T20:59:00Z");
        using var owner = await Owner(f); using var client = f.Client();
        await using (var db = f.Db())
        {
            await db.BusinessOpeningDays.ExecuteUpdateAsync(s => s.SetProperty(x => x.OpensAtMinute, 0).SetProperty(x => x.ClosesAtMinute, 1439), Token);
            await db.StaffWorkingDays.ExecuteUpdateAsync(s => s.SetProperty(x => x.OpensAtMinute, 0).SetProperty(x => x.ClosesAtMinute, 1439), Token);
            await db.BookingSmsBudgetPolicies.ExecuteUpdateAsync(s => s.SetProperty(x => x.DailyTry, 0.50m).SetProperty(x => x.MonthlyTry, 0.50m), Token);
        }
        using var booked = await PostAsync(owner, "/api/appointments/", Command(f) with { StartsAt = "2026-11-01T03:00:00+03:00" }, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        using var requested = await f.Post(client, "request", f.Request() with { StartsAt = "2026-11-01T12:00:00+03:00" }); Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        await f.Work(); await Due(f); await Work(f);
        Assert.Single(Carrier(f).Calls); Assert.Single(f.Sms.Calls);
        await using var check = f.Db(); var attempts = await check.BookingSmsAttempts.OrderBy(x => x.ReservedAt).ToArrayAsync(Token);
        Assert.Equal(2, attempts.Length); Assert.Equal(1m, attempts.Sum(x => x.ReservedTry));
        Assert.Equal(DateTimeOffset.Parse("2026-10-31T21:00:00Z"), Assert.Single(attempts, x => x.Purpose == "Reminder").ReservedAt);
    }

    [Fact]
    public async Task CrossPurposeJobIdCollisionCannotAdoptOtpReservation()
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f); using var client = f.Client();
        await Book(f, owner);
        using var requested = await f.Post(client, "request", f.Request() with { StartsAt = "2026-10-19T12:00:00+03:00" });
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode); await f.Work();
        await using (var db = f.Db())
        {
            var id = await db.BookingSmsJobs.Select(x => x.Id).SingleAsync(Token);
            await db.AppointmentReminderJobs.ExecuteUpdateAsync(s => s.SetProperty(x => x.Id, id), Token);
        }
        await Due(f); await Work(f); Assert.Empty(Carrier(f).Calls);
        await using var check = f.Db(); Assert.Single(await check.BookingSmsAttempts.ToArrayAsync(Token));
        Assert.Equal("Otp", (await check.BookingSmsAttempts.SingleAsync(Token)).Purpose);
        Assert.Equal("Budget", (await check.AppointmentReminderJobs.SingleAsync(Token)).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportExceptionIsUnknownAndCannotRetry(bool timeout)
    {
        await using var f = New(); await f.StartAsync(); using var owner = await Owner(f); await Book(f, owner);
        Carrier(f).Result = _ => throw (timeout ? new TimeoutException("Synthetic timeout") : new IOException("Synthetic response loss"));
        await Due(f); await Work(f); await Work(f); await f.RestartAsync(); await Work(f);
        Assert.Single(Carrier(f).Calls); await using var db = f.Db();
        Assert.Equal("Unknown", (await db.AppointmentReminderJobs.SingleAsync(Token)).Status);
        Assert.Equal("Unknown", (await db.BookingSmsAttempts.SingleAsync(Token)).Outcome);
    }
}
