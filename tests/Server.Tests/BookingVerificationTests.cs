using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Server.Features.Scheduling;
using Server.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class BookingVerificationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string Path = "/api/public-booking/verification/";
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    internal sealed class Sms : IBookingSmsDelivery
    {
        public bool Available { get; set; } = true;
        public decimal MaximumUnitPriceTry { get; set; } = 0.40m;
        public string? ProviderReference { get; set; }
        public ConcurrentQueue<(string Phone, string Code, Guid Id)> Calls { get; } = new();
        public Func<CancellationToken, Task<BookingSmsOutcome>> Result { get; set; } = _ => Task.FromResult(BookingSmsOutcome.Accepted);
        public async Task<BookingSmsResult> SendAsync(string phone, string code, Guid deliveryId, CancellationToken token)
        {
            Calls.Enqueue((phone, code, deliveryId)); return new(await Result(token), ProviderReference);
        }
    }
    internal sealed class Fixture : IAsyncDisposable
    {
        public PostgreSqlContainer Database { get; } = RecoveryDatabase();
        public Clock Clock { get; } = new();
        public Sms Sms { get; } = new();
        public WebApplicationFactory<Program> App { get; internal set; } = new();
        public Guid ServiceId { get; } = Guid.NewGuid();
        public Guid StaffId { get; } = Guid.NewGuid();
        public Guid ServiceVersion { get; } = Guid.NewGuid();
        public Guid PolicyVersion { get; private set; }
        public bool Enabled { get; set; } = true;
        public bool AccessEnabled { get; set; }
        public bool RemindersEnabled { get; set; }
        public IAppointmentReminderDelivery? ReminderDelivery { get; set; }
        public bool DispatchEnabled { get; set; } = true;
        public Guid BudgetApprovalId { get; set; } = Guid.NewGuid();
        public string? AppConnectionString { get; set; }
        public string HmacKey { get; set; } = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray());
        public string DatabaseName { get; set; } = "p02_recovery";
        private string ConnectionString => new Npgsql.NpgsqlConnectionStringBuilder(Database.GetConnectionString()) { Database = DatabaseName }.ConnectionString;
        public AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);
        public async Task StartAsync()
        {
            await Database.StartAsync(Token);
            await using var db = Db(); await db.Database.MigrateAsync(Token);
            db.StaffMembers.Add(new() { Id = StaffId, Name = "Sentetik çalışan", Version = Guid.NewGuid() });
            db.ServiceDefinitions.Add(new() { Id = ServiceId, Name = "Sentetik hizmet", DurationMinutes = 30, Price = 100, Currency = "TRY", Version = ServiceVersion });
            db.StaffServiceAssignments.Add(new() { StaffMemberId = StaffId, ServiceDefinitionId = ServiceId });
            var schedule = await db.BusinessHoursSchedules.SingleAsync(Token); schedule.IsConfigured = true;
            var businessDays = await db.BusinessOpeningDays.ToArrayAsync(Token);
            for (var day = 0; day < 7; day++)
            {
                var entry = businessDays.SingleOrDefault(x => x.Day == day);
                if (entry is null) { entry = new() { ScheduleId = 1, Day = day }; db.BusinessOpeningDays.Add(entry); }
                entry.IsClosed = false; entry.OpensAtMinute = 540; entry.ClosesAtMinute = 1200;
            }
            for (var day = 0; day < 7; day++) db.StaffWorkingDays.Add(new() { StaffMemberId = StaffId, Day = day, OpensAtMinute = 540, ClosesAtMinute = 1200 });
            PolicyVersion = (await db.BookingPolicies.SingleAsync(Token)).Version;
            await db.SaveChangesAsync(Token);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:InstanceId"] = "p05-synthetic",
                ["BookingVerification:Enabled"] = "true",
                ["BookingVerification:HmacKey"] = HmacKey
            }).Build();
            db.BookingSmsBudgetPolicies.Add(new()
            {
                Id = BudgetApprovalId,
                InstanceId = "p05-synthetic",
                KeyId = new BookingVerificationSecurity(config, Sms).KeyId,
                UnitPriceTry = 0.50m,
                DailyCalls = 20,
                MonthlyCalls = 200,
                TotalCalls = 200,
                DailyTry = 10m,
                MonthlyTry = 100m,
                TotalTry = 100m,
                StartsAt = Clock.Now,
                EndsAt = Clock.Now.AddDays(31)
            });
            await db.SaveChangesAsync(Token);
            await RestartAsync();
        }
        public async Task RestartAsync()
        {
            await App.DisposeAsync();
            App = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:AppDatabase"] = AppConnectionString ?? ConnectionString,
                    ["Auth:InstanceId"] = "p05-synthetic",
                    ["BookingVerification:Enabled"] = Enabled.ToString(),
                    ["PublicBooking:AccessEnabled"] = AccessEnabled.ToString(),
                    ["AppointmentReminders:Enabled"] = RemindersEnabled.ToString(),
                    ["BookingSms:DispatchEnabled"] = DispatchEnabled.ToString(),
                    ["BookingSms:BudgetApprovalId"] = BudgetApprovalId.ToString(),
                    ["BookingVerification:HmacKey"] = HmacKey
                }));
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<TimeProvider>(Clock); services.AddSingleton<IBookingSmsDelivery>(Sms);
                    if (ReminderDelivery is not null) services.AddSingleton<IAppointmentReminderDelivery>(ReminderDelivery);
                    foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService) &&
                        (x.ImplementationType == typeof(BookingSmsWorker) || x.ImplementationType == typeof(AppointmentPendingWorker) ||
                         x.ImplementationType == typeof(AppointmentReminderWorker))).ToArray()) services.Remove(descriptor);
                });
            });
        }
        public BookingVerificationEndpoints.Request Request(string phone = "05000000001") =>
            new(Guid.NewGuid(), ServiceId, ServiceVersion, PolicyVersion, StaffId, "2026-10-18T12:00:00+03:00", "Sentetik müşteri", phone);
        public HttpClient Client()
        {
            var client = App.CreateClient(); client.DefaultRequestHeaders.Add("Origin", "http://localhost"); return client;
        }
        public async Task<HttpResponseMessage> Post(HttpClient client, string action, object body) =>
            await PostAsync(client, Path + action, body, await GetCsrfAsync(client));
        public async Task<BookingVerificationEndpoints.Requested> RequestAsync(HttpClient client, string phone = "05000000001")
        {
            using var response = await Post(client, "request", Request(phone));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
            return Assert.IsType<BookingVerificationEndpoints.Requested>(await response.Content.ReadFromJsonAsync<BookingVerificationEndpoints.Requested>(Token));
        }
        public Task Work() => ActivatorUtilities.CreateInstance<BookingSmsWorker>(App.Services).ProcessOneAsync(Token);
        public async ValueTask DisposeAsync() { await App.DisposeAsync(); await Database.DisposeAsync(); }
    }

    [Fact]
    public async Task RequestContactErrorsAreScopedAndNeverWrite()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        var cases = new (string? Name, string? Phone, string[] Fields)[]
        {
            ("Sentetik müşteri", "123", ["customerPhone"]),
            ("Sentetik müşteri", "02120000001", ["customerPhone"]),
            ("Sentetik müşteri", null, ["customerPhone"]),
            (" ", "05000000001", ["customerName"]),
            (new string('x', 101), "05000000001", ["customerName"]),
            ("Ad\nSoyad", "05000000001", ["customerName"]),
            ("A", "abc", ["customerName", "customerPhone"])
        };
        foreach (var entry in cases)
        {
            using var response = await f.Post(client, "request", f.Request() with { CustomerName = entry.Name, CustomerPhone = entry.Phone });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
            Assert.True(body.TryGetProperty("errors", out var errors));
            Assert.Equal(entry.Fields.Order(), errors.EnumerateObject().Select(x => x.Name).Order());
            Assert.All(errors.EnumerateObject(), field => Assert.All(field.Value.EnumerateArray(), message => Assert.False(string.IsNullOrWhiteSpace(message.GetString()))));
            if (errors.TryGetProperty("customerPhone", out var phoneErrors))
            {
                Assert.Contains("cep", phoneErrors[0].GetString());
                Assert.DoesNotContain("sabit", phoneErrors[0].GetString());
            }
        }
        using var malformed = await f.Post(client, "request", f.Request("abc") with { RequestKey = Guid.Empty });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.False((await malformed.Content.ReadFromJsonAsync<JsonElement>(Token)).TryGetProperty("errors", out _));
        await using var db = f.Db();
        Assert.Empty(await db.BookingVerifications.ToArrayAsync(Token)); Assert.Empty(await db.BookingSmsJobs.ToArrayAsync(Token));
        Assert.Empty(await db.BookingVerificationQuotaEvents.ToArrayAsync(Token)); Assert.Empty(await db.BookingSmsPeriods.ToArrayAsync(Token));
        Assert.Empty(await db.BookingSmsAttempts.ToArrayAsync(Token)); Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(f.Sms.Calls);
    }

    [Fact]
    public async Task DifferentValidKeyCannotHidePersistedQuotasAtStartupOrWhileRunning()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        var originalKey = f.HmacKey;
        var differentKey = Convert.ToBase64String(new byte[32]);
        await using var other = f.App.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["BookingVerification:HmacKey"] = differentKey, ["BookingSms:DispatchEnabled"] = "false" })));
        using var otherClient = other.CreateClient(); otherClient.DefaultRequestHeaders.Add("Origin", "http://localhost");
        await f.RequestAsync(client);
        using var rejected = await f.Post(otherClient, "verify", new BookingVerificationEndpoints.Verify(Guid.NewGuid(), new string('a', 43), "123456"));
        Assert.Equal(HttpStatusCode.InternalServerError, rejected.StatusCode);
        f.HmacKey = differentKey; await f.RestartAsync();
        Assert.Throws<InvalidOperationException>(() => f.Client());
        f.HmacKey = originalKey; await f.RestartAsync(); using var restored = f.Client();
        using var limited = await f.Post(restored, "request", f.Request());
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(60, limited.Headers.RetryAfter?.Delta?.TotalSeconds);
        await using var db = f.Db(); Assert.Single(await db.BookingVerifications.ToArrayAsync(Token));
        Assert.Single(await db.BookingVerificationQuotaEvents.ToArrayAsync(Token)); Assert.Empty(f.Sms.Calls);
    }

    [Fact]
    public async Task RequestDeliveryVerificationAndResendPersistWithoutCreatingAppointments()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        var request = f.Request(); using var response = await f.Post(client, "request", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var challenge = Assert.IsType<BookingVerificationEndpoints.Requested>(await response.Content.ReadFromJsonAsync<BookingVerificationEndpoints.Requested>(Token));
        var body = await response.Content.ReadAsStringAsync(Token); Assert.DoesNotContain("+905000000001", body);
        Assert.Equal(43, challenge.ChallengeSecret.Length);
        using var duplicate = await f.Post(client, "request", request); Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var different = await f.Post(client, "request", request with { CustomerName = "Başka kişi" }); Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        using var early = await f.Post(client, "resend", new BookingVerificationEndpoints.Resend(challenge.ChallengeId, challenge.ChallengeSecret, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.TooManyRequests, early.StatusCode); Assert.Equal(60, early.Headers.RetryAfter?.Delta?.TotalSeconds);
        await f.Work(); var first = Assert.Single(f.Sms.Calls); Assert.Equal("+905000000001", first.Phone);
        await using (var db = f.Db())
        {
            var row = await db.BookingVerifications.SingleAsync(Token); Assert.NotEqual(challenge.ChallengeSecret, row.SecretHash);
            Assert.DoesNotContain(first.Code, row.CodeHash ?? ""); Assert.DoesNotContain(first.Phone, row.ProtectedPhone ?? "");
            var job = await db.BookingSmsJobs.SingleAsync(Token); Assert.Equal("Accepted", job.Status); Assert.Null(job.ProtectedPayload);
        }
        f.Clock.Now += TimeSpan.FromSeconds(60);
        var resend = new BookingVerificationEndpoints.Resend(challenge.ChallengeId, challenge.ChallengeSecret, Guid.NewGuid());
        using var sentAgain = await f.Post(client, "resend", resend); Assert.Equal(HttpStatusCode.Accepted, sentAgain.StatusCode);
        using var repeat = await f.Post(client, "resend", resend); Assert.Equal(await sentAgain.Content.ReadAsStringAsync(Token), await repeat.Content.ReadAsStringAsync(Token));
        Assert.Equal(challenge.ExpiresAt, (await sentAgain.Content.ReadFromJsonAsync<BookingVerificationEndpoints.Resent>(Token))?.ExpiresAt);
        await f.Work(); Assert.Equal(2, f.Sms.Calls.Count); var last = f.Sms.Calls.Last();
        using var oldCode = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, first.Code));
        Assert.NotEqual(first.Code, last.Code); Assert.Equal(HttpStatusCode.BadRequest, oldCode.StatusCode);
        using var verified = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, last.Code));
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        var result = await verified.Content.ReadAsStringAsync(Token);
        f.Clock.Now += TimeSpan.FromSeconds(30);
        using var replay = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, null));
        Assert.Equal(result, await replay.Content.ReadAsStringAsync(Token));
        await using var final = f.Db(); var stored = await final.BookingVerifications.SingleAsync(Token);
        Assert.Null(stored.CodeHash); Assert.Null(stored.ProtectedPhone); Assert.Equal(2, await final.BookingVerificationQuotaEvents.CountAsync(x => x.Kind == "Send", Token));
        Assert.Empty(await final.Appointments.ToArrayAsync(Token)); Assert.Empty(await final.AppointmentTimeBlocks.ToArrayAsync(Token)); Assert.Empty(await final.AppointmentAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task TwentyParallelRequestsAndResendsAndVerifiesHaveSingleEffects()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var csrf = await GetCsrfAsync(client); var request = f.Request();
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(client, Path + "request", request, csrf)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Accepted); Assert.Equal(19, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        var challenge = Assert.IsType<BookingVerificationEndpoints.Requested>(await responses.Single(x => x.StatusCode == HttpStatusCode.Accepted).Content.ReadFromJsonAsync<BookingVerificationEndpoints.Requested>(Token));
        foreach (var response in responses) response.Dispose();
        f.Clock.Now += TimeSpan.FromMinutes(1); await f.RestartAsync(); using var again = f.Client(); csrf = await GetCsrfAsync(again);
        var resend = new BookingVerificationEndpoints.Resend(challenge.ChallengeId, challenge.ChallengeSecret, Guid.NewGuid());
        responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(again, Path + "resend", resend, csrf)));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.Accepted, x.StatusCode)); foreach (var response in responses) response.Dispose();
        await f.Work(); Assert.Single(f.Sms.Calls);
        await f.RestartAsync(); using var verify = f.Client(); csrf = await GetCsrfAsync(verify);
        var code = new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, f.Sms.Calls.Single().Code);
        responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(verify, Path + "verify", code, csrf)));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        var results = await Task.WhenAll(responses.Select(x => x.Content.ReadAsStringAsync(Token))); Assert.Single(results.Distinct()); foreach (var response in responses) response.Dispose();
        await using var db = f.Db(); Assert.Equal(2, await db.BookingSmsJobs.CountAsync(Token)); Assert.Equal(2, await db.BookingVerificationQuotaEvents.CountAsync(Token));
    }

    [Fact]
    public async Task DisabledAndInvalidOriginsCsrfBodiesAndSecretsNeverWrite()
    {
        await using var f = new Fixture { Enabled = false }; await f.StartAsync(); using var client = f.Client();
        foreach (var action in new[] { "request", "resend", "verify" })
        {
            using var response = await f.Post(client, action, new { garbage = "not-a-number" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        }
        await f.Work(); Assert.Empty(f.Sms.Calls);
        f.Enabled = true; await f.RestartAsync(); using var enabled = f.Client();
        using var noCsrf = await PostAsync(enabled, Path + "request", f.Request(), null); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var noOrigin = f.App.CreateClient(); using var missing = await f.Post(noOrigin, "request", f.Request()); Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        enabled.DefaultRequestHeaders.Remove("Origin"); enabled.DefaultRequestHeaders.Add("Origin", "http://other.test");
        using var wrongOrigin = await f.Post(enabled, "request", f.Request()); Assert.Equal(HttpStatusCode.BadRequest, wrongOrigin.StatusCode);
        enabled.DefaultRequestHeaders.Remove("Origin"); enabled.DefaultRequestHeaders.Add("Origin", "http://localhost");
        using var unknown = await f.Post(enabled, "request", new { requestKey = Guid.NewGuid(), unexpected = true }); Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        using var large = await f.Post(enabled, "request", new { payload = new string('x', 4100) }); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        using var secret = await f.Post(enabled, "verify", new BookingVerificationEndpoints.Verify(Guid.NewGuid(), new string('a', 43), "123456")); Assert.Equal(HttpStatusCode.Forbidden, secret.StatusCode);
        foreach (var response in new[] { noCsrf, missing, wrongOrigin, unknown, large, secret })
            Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>(Token)).TryGetProperty("errors", out _));
        foreach (var phone in new[] { "02120000001", "+12000000000", "abc" })
        { using var invalid = await f.Post(enabled, "request", f.Request(phone)); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        await using var db = f.Db(); Assert.Empty(await db.BookingVerifications.ToArrayAsync(Token)); Assert.Empty(await db.BookingSmsJobs.ToArrayAsync(Token)); Assert.Empty(await db.BookingVerificationQuotaEvents.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ExpiryAndFiveFailuresCannotBeResetByResendOrRestart()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var challenge = await f.RequestAsync(client); await f.Work();
        var actual = f.Sms.Calls.Single().Code; var wrong = actual == "000000" ? "000001" : "000000";
        for (var i = 0; i < 4; i++) { using var failure = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, wrong)); Assert.Equal(HttpStatusCode.BadRequest, failure.StatusCode); }
        f.Clock.Now += TimeSpan.FromMinutes(1); var resend = new BookingVerificationEndpoints.Resend(challenge.ChallengeId, challenge.ChallengeSecret, Guid.NewGuid());
        using var resent = await f.Post(client, "resend", resend); Assert.Equal(HttpStatusCode.Accepted, resent.StatusCode);
        await f.RestartAsync(); using var restarted = f.Client();
        await f.Work(); var newest = f.Sms.Calls.Last().Code; wrong = newest == "000000" ? "000001" : "000000";
        using var fifth = await f.Post(restarted, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, wrong)); Assert.Equal(HttpStatusCode.BadRequest, fifth.StatusCode);
        using var locked = await f.Post(restarted, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, newest)); Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        using var cannotResend = await f.Post(restarted, "resend", resend); Assert.Equal(HttpStatusCode.BadRequest, cannotResend.StatusCode);
        f.Clock.Now = challenge.ExpiresAt; await f.Work();
        await using var db = f.Db(); var row = await db.BookingVerifications.SingleAsync(Token); Assert.Equal(5, row.FailedAttempts); Assert.Null(row.CodeHash); Assert.Null(row.ProtectedPhone);
        Assert.All(await db.BookingSmsJobs.ToArrayAsync(Token), x => Assert.Null(x.ProtectedPayload));
    }

    [Theory]
    [InlineData(BookingSmsOutcome.Accepted, "Accepted", 1)]
    [InlineData(BookingSmsOutcome.PermanentFailure, "Failed", 1)]
    [InlineData(BookingSmsOutcome.Unknown, "Unknown", 1)]
    [InlineData(BookingSmsOutcome.TemporaryFailure, "Failed", 3)]
    public async Task WorkerOutcomesHaveBoundedRetriesAndClearPayloads(BookingSmsOutcome outcome, string status, int calls)
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); await f.RequestAsync(client);
        f.Sms.Result = _ => Task.FromResult(outcome);
        await Task.WhenAll(f.Work(), f.Work()); f.Clock.Now += TimeSpan.FromSeconds(15); await f.Work(); f.Clock.Now += TimeSpan.FromSeconds(30); await f.Work();
        Assert.Equal(calls, f.Sms.Calls.Count); Assert.Single(f.Sms.Calls.Select(x => x.Id).Distinct());
        await using var db = f.Db(); var job = await db.BookingSmsJobs.SingleAsync(Token); Assert.Equal(status, job.Status); Assert.Equal(calls, job.Attempts); Assert.Null(job.ProtectedPayload); Assert.Null(job.LeaseId);
        Assert.Equal(calls, (await db.BookingSmsPeriods.SingleAsync(x => x.Key == "D20261017", Token)).Attempts);
    }

    [Fact]
    public async Task UnknownLeaseAndNetworkTimeoutAreNeverBlindlyRetried()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); await f.RequestAsync(client);
        f.Sms.Result = _ => throw new IOException("Synthetic response loss"); await f.Work(); await f.Work(); Assert.Single(f.Sms.Calls);
        f.Clock.Now += TimeSpan.FromSeconds(61); await f.RequestAsync(client, "05000000002");
        await using (var db = f.Db())
        {
            var pending = await db.BookingSmsJobs.SingleAsync(x => x.Status == "Pending", Token);
            pending.Status = "Processing"; pending.LeaseId = Guid.NewGuid(); pending.LeaseUntil = f.Clock.Now.AddSeconds(-1); await db.SaveChangesAsync(Token);
        }
        await f.RestartAsync(); await f.Work(); Assert.Single(f.Sms.Calls);
        await using var final = f.Db(); Assert.All(await final.BookingSmsJobs.ToArrayAsync(Token), x => { Assert.Equal("Unknown", x.Status); Assert.Null(x.ProtectedPayload); });
    }

    [Fact]
    public async Task PhoneSendQuotaAndWrongCodeQuotaSurviveNewKeysAndRestart()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        var first = await f.RequestAsync(client); await f.Work(); var actual = f.Sms.Calls.Last().Code;
        for (var i = 0; i < 5; i++) { using var response = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(first.ChallengeId, first.ChallengeSecret, actual == "000000" ? "000001" : "000000")); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        f.Clock.Now += TimeSpan.FromSeconds(60); var second = await f.RequestAsync(client); await f.Work(); actual = f.Sms.Calls.Last().Code;
        for (var i = 0; i < 5; i++) { using var response = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(second.ChallengeId, second.ChallengeSecret, actual == "000000" ? "000001" : "000000")); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        f.Clock.Now += TimeSpan.FromSeconds(60); var third = await f.RequestAsync(client); await f.Work(); actual = f.Sms.Calls.Last().Code;
        await f.RestartAsync(); using var restarted = f.Client();
        using var wait = await f.Post(restarted, "verify", new BookingVerificationEndpoints.Verify(third.ChallengeId, third.ChallengeSecret, actual)); Assert.Equal(HttpStatusCode.TooManyRequests, wait.StatusCode);
        using var sendWait = await f.Post(restarted, "request", f.Request()); Assert.Equal(HttpStatusCode.TooManyRequests, sendWait.StatusCode);
        Assert.Equal(780, sendWait.Headers.RetryAfter?.Delta?.TotalSeconds);
        await using var db = f.Db(); Assert.Equal(3, await db.BookingVerifications.CountAsync(Token)); Assert.Equal(10, await db.BookingVerificationQuotaEvents.CountAsync(x => x.Kind == "Wrong", Token));
    }

    [Fact]
    public async Task VerifiedGrantExpiresAndExpiredMaterialIsBatchCleaned()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var challenge = await f.RequestAsync(client); await f.Work();
        f.Clock.Now = challenge.ExpiresAt.AddSeconds(-1);
        using var response = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, f.Sms.Calls.Single().Code)); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var verified = Assert.IsType<BookingVerificationEndpoints.VerifiedResponse>(await response.Content.ReadFromJsonAsync<BookingVerificationEndpoints.VerifiedResponse>(Token));
        Assert.Equal(f.Clock.Now.AddMinutes(5), verified.VerifiedUntil);
        f.Clock.Now = verified.VerifiedUntil;
        using var expired = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, "000000")); Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        f.Clock.Now += TimeSpan.FromDays(33); await f.Work(); await f.Work();
        await using var db = f.Db(); Assert.Empty(await db.BookingVerifications.ToArrayAsync(Token)); Assert.Empty(await db.BookingSmsJobs.ToArrayAsync(Token)); Assert.Empty(await db.BookingVerificationQuotaEvents.ToArrayAsync(Token)); Assert.Empty(await db.BookingSmsPeriods.ToArrayAsync(Token));
    }

    [Fact]
    public async Task MigrationIsRepeatableDownRejectsRecordsAndModelMatches()
    {
        await using var f = new Fixture(); await f.StartAsync(); await using var db = f.Db();
        Assert.False(db.Database.HasPendingModelChanges()); await db.Database.MigrateAsync(Token); await db.Database.MigrateAsync(Token);
        var migrator = db.GetService<IMigrator>();
        var budget = await db.BookingSmsBudgetPolicies.AsNoTracking().SingleAsync(Token);
        // Bu senaryo eski boş OTP migration'ını sınar; yeni bütçe guard'ı ayrı sınıfta sınanır.
        await db.BookingSmsBudgetPolicies.ExecuteDeleteAsync(Token);
        await migrator.MigrateAsync("20261016222045_CustomerBookingPolicy", Token);
        await db.Database.MigrateAsync(Token);
        db.BookingSmsBudgetPolicies.Add(budget); await db.SaveChangesAsync(Token);
        Assert.Equal(f.StaffId, (await db.StaffMembers.SingleAsync(Token)).Id);
        using var client = f.Client(); await f.RequestAsync(client);
        // Yeni bütçe guard'ının eski OTP veri korumasını maskelemesine izin verme.
        var down = migrator.GenerateScript("20261017095913_BookingPhoneVerification", "20261016222045_CustomerBookingPolicy", MigrationsSqlGenerationOptions.NoTransactions);
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(down, Token));
        Assert.Equal("Telefon doğrulama kayıtları veya kota varken Down uygulanamaz.", error.MessageText);
        Assert.Single(await db.BookingVerifications.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("phone-window", 3, 2, 778)]
    [InlineData("phone-day", 10, 30, 54000)]
    [InlineData("ip-window", 5, 2, 776)]
    [InlineData("ip-day", 20, 30, 54000)]
    [InlineData("global-window", 30, 2, 751)]
    [InlineData("global-day", 200, 30, 54000)]
    public async Task PersistentQuotaUsesEveryScopeAndTheActualNextAllowedTime(string kind, int count, int minutes, int seconds)
    {
        await using var f = new Fixture(); await f.StartAsync();
        var security = f.App.Services.GetRequiredService<BookingVerificationSecurity>();
        await using (var db = f.Db())
        {
            for (var i = 0; i < count; i++) db.BookingVerificationQuotaEvents.Add(new()
            {
                Id = Guid.NewGuid(),
                Kind = "Send",
                KeyId = security.KeyId,
                PhoneHash = security.Hash("phone", kind.StartsWith("phone", StringComparison.Ordinal) ? "+905000000001" : "other" + i),
                IpHash = security.Hash("ip", kind.StartsWith("ip", StringComparison.Ordinal) ? "unknown" : "other" + i),
                OccurredAt = f.Clock.Now.AddMinutes(-minutes).AddSeconds(-i),
                RetainUntil = f.Clock.Now.AddDays(2)
            });
            await db.SaveChangesAsync(Token);
        }
        await f.RestartAsync(); using var client = f.Client();
        using var response = await f.Post(client, "request", f.Request());
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode); Assert.Equal(seconds, response.Headers.RetryAfter?.Delta?.TotalSeconds);
        await using var check = f.Db(); Assert.Empty(await check.BookingVerifications.ToArrayAsync(Token)); Assert.Empty(await check.BookingSmsJobs.ToArrayAsync(Token)); Assert.Equal(count, await check.BookingVerificationQuotaEvents.CountAsync(Token));
    }

    [Theory]
    [InlineData("D20261031", 200, "2026-10-31T21:00:00Z")]
    [InlineData("M202610", 2000, "2026-10-31T21:00:00Z")]
    public async Task ProviderCallQuotaCrossesIstanbulMonthBoundaryWithoutResettingHistory(string key, int attempts, string end)
    {
        await using var f = new Fixture(); await f.StartAsync();
        f.Clock.Now = DateTimeOffset.Parse("2026-10-31T20:59:00Z", System.Globalization.CultureInfo.InvariantCulture);
        using var client = f.Client();
        using var response = await f.Post(client, "request", f.Request() with { StartsAt = "2026-11-01T12:00:00+03:00" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using (var db = f.Db())
        {
            db.BookingSmsPeriods.Add(new() { Key = key, Attempts = attempts, EndsAt = DateTimeOffset.Parse(end, System.Globalization.CultureInfo.InvariantCulture) });
            await db.SaveChangesAsync(Token);
        }
        await f.Work(); Assert.Empty(f.Sms.Calls);
        await f.RestartAsync(); f.Clock.Now += TimeSpan.FromMinutes(1); await f.Work(); Assert.Single(f.Sms.Calls);
        await using var check = f.Db(); Assert.Equal(attempts, (await check.BookingSmsPeriods.FindAsync([key], Token))?.Attempts);
        Assert.Equal(1, (await check.BookingSmsPeriods.FindAsync(["D20261101"], Token))?.Attempts);
        Assert.Equal(1, (await check.BookingSmsPeriods.FindAsync(["M202611"], Token))?.Attempts);
    }

    [Fact]
    public async Task ExactExpiryRejectsCorrectCodeAndChangedSelectionNeverQueues()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client();
        foreach (var request in new[] { f.Request() with { PolicyVersion = Guid.NewGuid() }, f.Request() with { ServiceVersion = Guid.NewGuid() }, f.Request() with { StartsAt = "2026-10-18T08:00:00+03:00" } })
        { using var response = await f.Post(client, "request", request); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); }
        using var invisible = await f.Post(client, "request", f.Request() with { StaffMemberId = Guid.NewGuid() }); Assert.Equal(HttpStatusCode.NotFound, invisible.StatusCode);
        var challenge = await f.RequestAsync(client); await f.Work(); f.Clock.Now = challenge.ExpiresAt;
        using var expired = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, f.Sms.Calls.Single().Code)); Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        await f.Work(); await using var db = f.Db(); Assert.Single(await db.BookingVerifications.ToArrayAsync(Token));
        var row = await db.BookingVerifications.SingleAsync(Token); Assert.Null(row.CodeHash); Assert.Null(row.ProtectedPhone); Assert.Empty(row.UsedCodeHashes);
    }

    [Fact]
    public async Task PostHttpLimitIsIndependentOfGetLimitAndForwardedHeadersCannotBypassIt()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var csrf = await GetCsrfAsync(client);
        for (var i = 0; i < 20; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For"); client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100." + i);
            using var response = await PostAsync(client, Path + "verify", new BookingVerificationEndpoints.Verify(Guid.NewGuid(), new string('a', 43), "123456"), csrf);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var limited = await PostAsync(client, Path + "verify", new { }, csrf); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.CacheControl?.NoStore); Assert.InRange(limited.Headers.RetryAfter?.Delta?.TotalSeconds ?? 0, 1, 60);
        using var catalog = await client.GetAsync("/api/public-booking/catalog", Token); Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("U3ludGhldGlj")]
    public void EnabledWithoutValidKeyFailsClosed(string key)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["BookingVerification:Enabled"] = "true", ["Auth:InstanceId"] = "synthetic", ["BookingVerification:HmacKey"] = key }).Build();
        Assert.Throws<InvalidOperationException>(() => new BookingVerificationSecurity(config, new Sms()));
    }

    [Fact]
    public void MissingCarrierFailsClosedAndDisabledModeDoesNotNeedASecret()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BookingVerification:Enabled"] = "true",
            ["Auth:InstanceId"] = "synthetic",
            ["BookingVerification:HmacKey"] = Convert.ToBase64String(new byte[32])
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new BookingVerificationSecurity(config, new Sms { Available = false }));
        config["BookingVerification:Enabled"] = "false"; config["BookingVerification:HmacKey"] = null;
        Assert.False(new BookingVerificationSecurity(config, new Sms { Available = false }).Enabled);
    }

    internal static async Task<Dictionary<string, string>> Fingerprints(AppDbContext db)
    {
        await using var connection = new Npgsql.NpgsqlConnection(db.Database.GetConnectionString()); await connection.OpenAsync(Token);
        var tables = new List<string>();
        await using (var command = new Npgsql.NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename", connection))
        await using (var reader = await command.ExecuteReaderAsync(Token))
            while (await reader.ReadAsync(Token)) tables.Add(reader.GetString(0));
        var result = new Dictionary<string, string>();
        foreach (var table in tables)
        {
            var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
            await using var command = new Npgsql.NpgsqlCommand($"SELECT md5(coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text,'')) FROM {quoted} t", connection);
            result[table] = Assert.IsType<string>(await command.ExecuteScalarAsync(Token));
        }
        return result;
    }

    [Fact]
    public async Task FullSyntheticDumpRestoresEveryTableAndDisabledRestartCannotResendOldJobs()
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); await f.RequestAsync(client);
        Dictionary<string, string> before;
        await using (var db = f.Db()) before = await Fingerprints(db);
        Assert.Equal(56, before.Count);
        Assert.Contains("SubscriptionPeriods", before.Keys);
        Assert.Contains("SubscriptionPayments", before.Keys);
        Assert.Contains("AppointmentReminderJobs", before.Keys);
        foreach (var command in new[]
        {
            new[] { "pg_dump", "-U", "postgres", "-d", "p02_recovery", "-Fc", "-f", "/tmp/p05.dump" },
            new[] { "createdb", "-U", "postgres", "p05_restored" },
            new[] { "pg_restore", "-U", "postgres", "-d", "p05_restored", "/tmp/p05.dump" }
        })
        {
            var result = await f.Database.ExecAsync(command, Token); Assert.Equal(0, result.ExitCode);
        }
        f.DatabaseName = "p05_restored"; f.Enabled = false;
        for (var restart = 0; restart < 2; restart++)
        {
            await f.RestartAsync(); using var restoredClient = f.Client();
            using var disabled = await f.Post(restoredClient, "verify", new { }); Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
            await f.Work(); Assert.Empty(f.Sms.Calls);
            await using var restored = f.Db(); Assert.Equal(before, await Fingerprints(restored));
        }
    }

    [Fact]
    public async Task RealKestrelHttpCompletesVerificationAndRejectsOversizedChunkedBodies()
    {
        await using var f = new Fixture(); await f.StartAsync(); f.App.UseKestrel(0);
        using var client = f.App.CreateClient(); Assert.NotEqual(80, client.BaseAddress?.Port);
        client.DefaultRequestHeaders.Add("Origin", client.BaseAddress?.GetLeftPart(UriPartial.Authority));
        var challenge = await f.RequestAsync(client); await f.Work();
        f.Clock.Now += TimeSpan.FromSeconds(60);
        using var resend = await f.Post(client, "resend", new BookingVerificationEndpoints.Resend(challenge.ChallengeId, challenge.ChallengeSecret, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode); await f.Work();
        using var verified = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, f.Sms.Calls.Last().Code));
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using var body = new HttpRequestMessage(HttpMethod.Post, Path + "request") { Content = new StringContent("{\"payload\":\"" + new string('x', 4100) + "\"}", System.Text.Encoding.UTF8, "application/json") };
        body.Headers.TransferEncodingChunked = true; body.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        using var large = await client.SendAsync(body, Token); Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
        Assert.Equal(2, f.Sms.Calls.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InFlightAcknowledgementCannotOverwriteVerificationOrNewCode(bool verify)
    {
        await using var f = new Fixture(); await f.StartAsync(); using var client = f.Client(); var challenge = await f.RequestAsync(client);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<BookingSmsOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Sms.Result = token => { entered.SetResult(); return release.Task.WaitAsync(token); };
        var work = f.Work(); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        var first = f.Sms.Calls.Single();
        if (verify)
        {
            using var response = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, first.Code));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        else
        {
            f.Clock.Now += TimeSpan.FromSeconds(60);
            using var response = await f.Post(client, "resend", new BookingVerificationEndpoints.Resend(challenge.ChallengeId, challenge.ChallengeSecret, Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        release.SetResult(BookingSmsOutcome.Accepted); await work;
        await using (var db = f.Db()) { var old = await db.BookingSmsJobs.SingleAsync(x => x.Id == first.Id, Token); Assert.Equal("Cancelled", old.Status); Assert.Null(old.ProtectedPayload); }
        f.Sms.Result = _ => Task.FromResult(BookingSmsOutcome.Accepted); await f.Work();
        Assert.Equal(verify ? 1 : 2, f.Sms.Calls.Count);
        if (!verify)
        {
            Assert.NotEqual(first.Code, f.Sms.Calls.Last().Code);
            using var oldCode = await f.Post(client, "verify", new BookingVerificationEndpoints.Verify(challenge.ChallengeId, challenge.ChallengeSecret, first.Code));
            Assert.Equal(HttpStatusCode.BadRequest, oldCode.StatusCode);
        }
    }

    [Fact]
    public async Task CleanupProcessesOnlyHundredExpiredChallengesAndQuotaEventsPerBatch()
    {
        await using var f = new Fixture(); await f.StartAsync();
        await using (var db = f.Db())
        {
            for (var i = 0; i < 105; i++)
            {
                db.BookingVerifications.Add(new()
                {
                    Id = Guid.NewGuid(),
                    RequestKey = Guid.NewGuid(),
                    RequestHash = new string('A', 64),
                    SecretHash = new string('B', 64),
                    PhoneHash = new string('C', 64),
                    IpHash = new string('D', 64),
                    CreatedAt = f.Clock.Now.AddDays(-3),
                    ExpiresAt = f.Clock.Now.AddDays(-3).AddMinutes(5),
                    ResendAt = f.Clock.Now.AddDays(-3).AddMinutes(1),
                    UpdatedAt = f.Clock.Now.AddDays(-3)
                });
                db.BookingVerificationQuotaEvents.Add(new() { Id = Guid.NewGuid(), KeyId = f.App.Services.GetRequiredService<BookingVerificationSecurity>().KeyId, Kind = "Send", PhoneHash = "old", IpHash = "old", OccurredAt = f.Clock.Now.AddDays(-3), RetainUntil = f.Clock.Now.AddDays(-1) });
            }
            await db.SaveChangesAsync(Token);
        }
        await f.Work(); await using (var db = f.Db()) { Assert.Equal(5, await db.BookingVerifications.CountAsync(Token)); Assert.Equal(5, await db.BookingVerificationQuotaEvents.CountAsync(Token)); }
        await f.RestartAsync(); await f.Work(); await using var final = f.Db(); Assert.Empty(await final.BookingVerifications.ToArrayAsync(Token)); Assert.Empty(await final.BookingVerificationQuotaEvents.ToArrayAsync(Token));
    }
}
