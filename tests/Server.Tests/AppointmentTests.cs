using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Business;
using Server.Features.Scheduling;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;
using static Server.Tests.Support.TestAccounts;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class AppointmentTests
{
    private static readonly Guid MemberId = Guid.Parse("30f650b8-3614-4b29-aac2-c521f47f4d11");
    private static readonly Guid OtherMemberId = Guid.Parse("98700ea4-663f-4614-aa99-e297c14ea5d1");
    private static readonly Guid ServiceId = Guid.Parse("55359fdc-d67b-4c44-8fce-65e4ce53ceaa");
    private static readonly Guid ServiceVersion = Guid.Parse("6ff08883-02a3-4d71-b206-0316c78906cc");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class BookingClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 14, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static WeeklyHours.DayRequest[] Week(string start = "09:00", string end = "19:00") =>
        Enumerable.Range(0, 7).Select(day => new WeeklyHours.DayRequest(day, false, start, end)).ToArray();
    private static AppointmentEndpoints.CreateRequest Request(DateTimeOffset? start = null, Guid? member = null) =>
        new(Guid.NewGuid(), ServiceId, ServiceVersion, member ?? MemberId,
            (start ?? new DateTimeOffset(2026, 10, 14, 7, 0, 0, TimeSpan.Zero)).ToString("O", CultureInfo.InvariantCulture),
            "Sentetik müşteri", "05000000001");
    private static Task<HttpResponseMessage> Create(HttpClient owner, AppointmentEndpoints.CreateRequest request, string? csrf) =>
        PostAsync(owner, "/api/appointments/", request, csrf);
    private static async Task<AppointmentEndpoints.AppointmentResponse> Body(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
    }
    private static async Task<HttpClient> OtherOwnerAsync(RecoverySeed seed)
    {
        using var scope = seed.App.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = Assert.IsType<AppUser>(await users.FindByIdAsync(seed.OtherUserId.ToString()));
        Assert.True((await users.AddToRoleAsync(user, "Owner")).Succeeded);
        Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
        var key = Assert.IsType<string>(await users.GetAuthenticatorKeyAsync(user));
        var client = seed.App.CreateClient();
        using var login = await PostAsync(client, "/api/auth/login", new { email = "other@example.test", password = Password }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
        await CompleteMfaAsync(client, key);
        return client;
    }
    internal static async Task ConfigureAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.BusinessHoursSchedules.SingleAsync(Token)).IsConfigured = true;
        db.BusinessOpeningDays.AddRange(Enumerable.Range(0, 7).Select(day => new BusinessOpeningDay
        { ScheduleId = 1, Day = day, OpensAtMinute = 540, ClosesAtMinute = 1140 }));
        db.StaffMembers.AddRange(new[] { MemberId, OtherMemberId }.Select(id => new StaffMember
        { Id = id, Name = "Sentetik personel", Version = Guid.NewGuid() }));
        db.ServiceDefinitions.Add(new ServiceDefinition
        { Id = ServiceId, Name = "Sentetik hizmet", DurationMinutes = 20, Price = 123.45m, Currency = "TRY", Version = ServiceVersion });
        foreach (var id in new[] { MemberId, OtherMemberId })
        {
            db.StaffServiceAssignments.Add(new StaffServiceAssignment { StaffMemberId = id, ServiceDefinitionId = ServiceId });
            db.StaffWorkingDays.AddRange(Enumerable.Range(0, 7).Select(day => new StaffWorkingDay
            { StaffMemberId = id, Day = day, OpensAtMinute = 540, ClosesAtMinute = 1140 }));
        }
        await db.SaveChangesAsync(Token);
    }

    [Fact]
    public async Task OwnerCreationUsesSnapshotsAndReadDoesNotExposeOtherCustomers()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var response = await Create(owner, Request(), await GetCsrfAsync(owner)); var saved = await Body(response, HttpStatusCode.Created);
        Assert.Equal("Confirmed", saved.Status); Assert.Equal(20, saved.DurationMinutes); Assert.Equal(123.45m, saved.Price);
        Assert.Equal("TRY", saved.Currency); Assert.Equal("+905000000001", saved.CustomerPhone);
        Assert.Equal(TimeSpan.FromMinutes(20), saved.EndsAt - saved.StartsAt); Assert.Equal(DateTimeKind.Utc, saved.StartsAt.Kind);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.Name = "Yeni isim"; service.Price = 999m; service.DurationMinutes = 45; service.Version = Guid.NewGuid();
        await db.SaveChangesAsync(Token);
        using var read = await owner.GetAsync($"/api/appointments/{saved.Id}", Token); Assert.Equal(saved, await Body(read, HttpStatusCode.OK));
        using var missing = await owner.GetAsync($"/api/appointments/{Guid.NewGuid()}", Token); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
        Assert.DoesNotContain(typeof(AppointmentAudit).GetProperties(), property => property.Name.StartsWith("Customer", StringComparison.Ordinal));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task TwentyParallelRequestsCreateOneAppointmentAndNoPartialAudits()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Create(owner, Request(), csrf)));
        Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Created);
        Assert.Equal(19, responses.Count(item => item.StatusCode == HttpStatusCode.Conflict));
        foreach (var response in responses) response.Dispose();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.Appointments.ToArrayAsync(Token)); Assert.Single(await db.AppointmentTimeBlocks.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task IdempotencySurvivesConcurrentReplayChangedServiceAndNewAppInstance()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); var request = Request();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Create(owner, request, csrf)));
        Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Created);
        var ids = new List<Guid>();
        foreach (var response in responses)
        { ids.Add((await Body(response, response.StatusCode == HttpStatusCode.Created ? HttpStatusCode.Created : HttpStatusCode.OK)).Id); response.Dispose(); }
        Assert.Single(ids.Distinct());
        using var different = await Create(owner, request with { CustomerName = "Farklı müşteri" }, csrf); Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.Price = 500m; service.Version = Guid.NewGuid(); await db.SaveChangesAsync(Token);
        using var replay = await Create(owner, request with { CustomerPhone = "+90 (500) 000-00-01", StartsAt = "2026-10-14T10:00:00+03:00" }, csrf);
        Assert.Equal(ids[0], (await Body(replay, HttpStatusCode.OK)).Id);
        using var staleService = await Create(owner, request with { RequestKey = Guid.NewGuid() }, csrf); Assert.Equal(HttpStatusCode.Conflict, staleService.StatusCode);
        await using var restarted = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("environment", "Development");
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new BookingClock()));
            builder.UseSetting("ConnectionStrings:AppDatabase", database.GetConnectionString());
            builder.UseSetting("Auth:InstanceId", "p02-recovery-test");
        });
        using var restartedClient = restarted.CreateClient(); await PasswordStepAsync(restartedClient); await CompleteMfaAsync(restartedClient, seed.Key);
        using var afterRestart = await Create(restartedClient, request, await GetCsrfAsync(restartedClient)); Assert.Equal(ids[0], (await Body(afterRestart, HttpStatusCode.OK)).Id);
        Assert.Single(await db.Appointments.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task AvailabilityIsPrivateAndAppliesOccupiedIntervalsWithoutRoundingServiceDuration()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        var availabilityPath = $"/api/appointments/availability?serviceId={ServiceId}&staffMemberId={MemberId}&date=2026-10-14";
        using var response = await owner.GetAsync(availabilityPath, Token); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var before = Assert.IsType<AppointmentEndpoints.AvailabilityResponse>(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AvailabilityResponse>(Token));
        Assert.Equal(new DateTime(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc), before.StartsAt[0]); Assert.Equal(ServiceVersion, before.ServiceVersion);
        Assert.DoesNotContain("customer", (await response.Content.ReadAsStringAsync(Token)).ToLowerInvariant());
        using var created = await Create(owner, Request(), csrf); await Body(created, HttpStatusCode.Created);
        using var afterResponse = await owner.GetAsync(availabilityPath, Token);
        var after = Assert.IsType<AppointmentEndpoints.AvailabilityResponse>(await afterResponse.Content.ReadFromJsonAsync<AppointmentEndpoints.AvailabilityResponse>(Token));
        Assert.DoesNotContain(before.StartsAt[0], after.StartsAt); Assert.DoesNotContain(before.StartsAt[1], after.StartsAt); Assert.Contains(before.StartsAt[2], after.StartsAt);
        using var differentMember = await Create(owner, Request(member: OtherMemberId), csrf); await Body(differentMember, HttpStatusCode.Created);
        using var next = await Create(owner, Request(new DateTimeOffset(2026, 10, 14, 7, 30, 0, TimeSpan.Zero)), csrf); await Body(next, HttpStatusCode.Created);
        using var invalidDate = await owner.GetAsync(availabilityPath.Replace("2026-10-14", "9999-12-31", StringComparison.Ordinal), Token); Assert.Equal(HttpStatusCode.BadRequest, invalidDate.StatusCode);
    }

    [Fact]
    public async Task RoutesRejectAnonymousPendingStaffInvalidCsrfAndInvalidFields()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var anonymous = app.CreateClient(); using var pending = app.CreateClient(); await PasswordStepAsync(pending);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        {
            using var create = await Create(client, Request(), await GetCsrfAsync(client)); Assert.Equal(status, create.StatusCode);
            using var read = await client.GetAsync($"/api/appointments/{Guid.NewGuid()}", Token); Assert.Equal(status, read.StatusCode);
            using var slots = await client.GetAsync($"/api/appointments/availability?serviceId={ServiceId}&staffMemberId={MemberId}&date=2026-10-14", Token); Assert.Equal(status, slots.StatusCode);
        }
        foreach (var csrf in new string?[] { null, "invalid" })
        { using var response = await Create(owner, Request(), csrf); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        var token = await GetCsrfAsync(owner);
        foreach (var invalid in new[] { Request() with { RequestKey = Guid.Empty }, Request() with { ServiceVersion = Guid.Empty },
            Request() with { StaffMemberId = Guid.Empty }, Request() with { CustomerName = "\n" }, Request() with { CustomerName = new string('x', 101) },
            Request() with { CustomerPhone = "+15550000001" }, Request() with { StartsAt = "2026-10-14T10:00:00" }, Request() with { StartsAt = "2026-10-14T10:00:01+03:00" } })
        { using var response = await Create(owner, invalid, token); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        using var missing = await Create(owner, Request() with { ServiceId = Guid.NewGuid() }, token); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Empty(await db.Appointments.ToArrayAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task LeadHorizonStepClosedHoursAndMissingEligibilityAreRecheckedAtCommit()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var clock = new BookingClock(); var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        foreach (var start in new[] { clock.Now.AddMinutes(45), clock.Now.AddMinutes(61), clock.Now.AddDays(30).AddHours(1), clock.Now.AddHours(12).AddMinutes(45) })
        { using var response = await Create(owner, Request(start), csrf); Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); }
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.SingleAsync(item => item.Id == MemberId, Token); member.IsActive = false; await db.SaveChangesAsync(Token);
        using var inactive = await Create(owner, Request(), csrf); Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        member.IsActive = true; var service = await db.ServiceDefinitions.SingleAsync(Token); service.IsActive = false; await db.SaveChangesAsync(Token);
        using var inactiveService = await Create(owner, Request(), csrf); Assert.Equal(HttpStatusCode.Conflict, inactiveService.StatusCode);
        service.IsActive = true; var link = await db.StaffServiceAssignments.SingleAsync(item => item.StaffMemberId == MemberId, Token); db.Remove(link); await db.SaveChangesAsync(Token);
        using var unassigned = await Create(owner, Request(), csrf); Assert.Equal(HttpStatusCode.Conflict, unassigned.StatusCode);
        db.Add(link); (await db.BusinessHoursSchedules.SingleAsync(Token)).IsConfigured = false; await db.SaveChangesAsync(Token);
        using var unconfigured = await Create(owner, Request(), csrf); Assert.Equal(HttpStatusCode.Conflict, unconfigured.StatusCode);
        (await db.BusinessHoursSchedules.SingleAsync(Token)).IsConfigured = true;
        foreach (var day in await db.BusinessOpeningDays.ToArrayAsync(Token)) { day.OpensAtMinute = 0; day.ClosesAtMinute = 1439; }
        foreach (var day in await db.StaffWorkingDays.ToArrayAsync(Token)) { day.OpensAtMinute = 0; day.ClosesAtMinute = 1439; }
        await db.SaveChangesAsync(Token);
        using var lower = await Create(owner, Request(clock.Now.AddMinutes(60)), csrf); await Body(lower, HttpStatusCode.Created);
        using var upper = await Create(owner, Request(clock.Now.AddDays(30)), csrf); await Body(upper, HttpStatusCode.Created);
    }

    [Fact]
    public async Task HoursCannotHideFutureAppointmentsAndConflictDoesNotWriteVersionOrAudit()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var created = await Create(owner, Request(), csrf); await Body(created, HttpStatusCode.Created);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var business = await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(Token);
        var member = await db.StaffMembers.AsNoTracking().SingleAsync(item => item.Id == MemberId, Token);
        using var businessWrite = await PostAsync(owner, "/api/business-hours/", new { version = business.Version, days = Week("11:00") }, csrf); Assert.Equal(HttpStatusCode.Conflict, businessWrite.StatusCode);
        using var staffWrite = await PostAsync(owner, $"/api/staff-members/{MemberId}/hours", new { version = member.Version, days = Week("11:00") }, csrf); Assert.Equal(HttpStatusCode.Conflict, staffWrite.StatusCode);
        Assert.Contains("appointments_outside_hours", await businessWrite.Content.ReadAsStringAsync(Token));
        Assert.Contains("appointments_outside_hours", await staffWrite.Content.ReadAsStringAsync(Token));
        Assert.Equal(business.Version, (await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(Token)).Version);
        Assert.Equal(member.Version, (await db.StaffMembers.AsNoTracking().SingleAsync(item => item.Id == MemberId, Token)).Version);
        Assert.Empty(await db.BusinessHoursAudits.ToArrayAsync(Token)); Assert.Empty(await db.StaffHoursAudits.ToArrayAsync(Token));
        using var allowed = await PostAsync(owner, $"/api/staff-members/{MemberId}/hours", new { version = member.Version, days = Week("09:30") }, csrf); Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BookingAndHoursRaceHasOneConsistentWinner(bool businessHours)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var otherOwner = await OtherOwnerAsync(seed); var otherCsrf = await GetCsrfAsync(otherOwner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var version = businessHours ? (await db.BusinessHoursSchedules.SingleAsync(Token)).Version :
            (await db.StaffMembers.SingleAsync(item => item.Id == MemberId, Token)).Version;
        var path = businessHours ? "/api/business-hours/" : $"/api/staff-members/{MemberId}/hours";
        var responses = await Task.WhenAll(Create(owner, Request(), csrf), PostAsync(otherOwner, path, new { version, days = Week("11:00") }, otherCsrf));
        Assert.Single(responses, item => item.StatusCode == HttpStatusCode.Conflict);
        Assert.Single(responses, item => item.IsSuccessStatusCode);
        var bookingWon = responses[0].StatusCode == HttpStatusCode.Created;
        foreach (var response in responses) response.Dispose();
        Assert.Equal(bookingWon ? 1 : 0, await db.Appointments.CountAsync(Token));
        Assert.Equal(bookingWon ? 0 : 1, businessHours ? await db.BusinessHoursAudits.CountAsync(Token) : await db.StaffHoursAudits.CountAsync(Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WaitingBookingReadsTheHoursCommittedBeforeItsSharedResourceLock(bool businessHours)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        if (businessHours) await db.BusinessHoursSchedules.FromSqlRaw("SELECT * FROM \"BusinessHoursSchedules\" WHERE \"Id\"=1 FOR UPDATE").SingleAsync(Token);
        else await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\"={MemberId} FOR UPDATE").SingleAsync(Token);
        var write = Create(owner, Request(), csrf);
        await using var observer = new NpgsqlConnection(database.GetConnectionString()); await observer.OpenAsync(Token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pattern = businessHours ? "%BusinessHoursSchedules%FOR SHARE%" : "%StaffMembers%FOR UPDATE%";
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE wait_event_type='Lock' AND query LIKE @pattern)", observer);
            command.Parameters.AddWithValue("pattern", pattern);
            if ((bool)(await command.ExecuteScalarAsync(timeout.Token) ?? false)) break;
            await Task.Delay(20, timeout.Token);
        }
        if (businessHours) await db.Database.ExecuteSqlRawAsync("UPDATE \"BusinessOpeningDays\" SET \"OpensAtMinute\"=660 WHERE \"Day\"=2", Token);
        else await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"StaffWorkingDays\" SET \"OpensAtMinute\"=660 WHERE \"StaffMemberId\"={MemberId} AND \"Day\"=2", Token);
        await transaction.CommitAsync(Token);
        using var response = await write; Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.AppointmentAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ClosedOrMissingStaffHoursAndDeletedDefinitionsDoNotOfferOrCreateSlots()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var staffDay = await db.StaffWorkingDays.SingleAsync(item => item.StaffMemberId == MemberId && item.Day == 2, Token);
        staffDay.IsClosed = true; staffDay.OpensAtMinute = null; staffDay.ClosesAtMinute = null; await db.SaveChangesAsync(Token);
        using var closed = await Create(owner, Request(), csrf); Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        using var response = await owner.GetAsync($"/api/appointments/availability?serviceId={ServiceId}&staffMemberId={MemberId}&date=2026-10-14", Token);
        Assert.Empty(Assert.IsType<AppointmentEndpoints.AvailabilityResponse>(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AvailabilityResponse>(Token)).StartsAt);
        db.Remove(staffDay); await db.SaveChangesAsync(Token);
        using var missingHours = await Create(owner, Request(), csrf); Assert.Equal(HttpStatusCode.Conflict, missingHours.StatusCode);
        var member = await db.StaffMembers.SingleAsync(item => item.Id == MemberId, Token); member.IsDeleted = true; member.IsActive = false; await db.SaveChangesAsync(Token);
        using var deletedMember = await Create(owner, Request(), csrf); Assert.Equal(HttpStatusCode.NotFound, deletedMember.StatusCode);
        member.IsDeleted = false; member.IsActive = true;
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.IsDeleted = true; service.IsActive = false; await db.SaveChangesAsync(Token);
        using var deletedService = await Create(owner, Request(), csrf); Assert.Equal(HttpStatusCode.NotFound, deletedService.StatusCode);
        Assert.Empty(await db.Appointments.ToArrayAsync(Token));
    }

    [Fact]
    public async Task WaitingBookingRechecksRevokedOwnerSession()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {seed.OwnerId} FOR UPDATE").SingleAsync(Token);
        var write = Create(owner, Request(), csrf);
        await using var observer = new NpgsqlConnection(database.GetConnectionString()); await observer.OpenAsync(Token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var query = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE wait_event_type='Lock' AND query LIKE '%AspNetUsers%FOR UPDATE%')", observer);
            if ((bool)(await query.ExecuteScalarAsync(timeout.Token) ?? false)) break;
            await Task.Delay(20, timeout.Token);
        }
        user.SecurityStamp = Guid.NewGuid().ToString(); await db.SaveChangesAsync(Token); await transaction.CommitAsync(Token);
        using var response = await write; Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.AppointmentAudits.ToArrayAsync(Token));
    }

    [Fact]
    public void DstInvalidAndAmbiguousWallTimesAreNotGuessed()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Assert.False(AppointmentRules.TryUtc(new DateOnly(2026, 3, 8), 150, zone, out _));
        Assert.False(AppointmentRules.TryUtc(new DateOnly(2026, 11, 1), 90, zone, out _));
        Assert.True(AppointmentRules.TryUtc(new DateOnly(2026, 10, 14), 600, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"), out var utc));
        Assert.Equal(new DateTime(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public async Task CancelledRequestWaitingOnStaffLockLeavesNoPartialAppointmentAndCanBeRetried()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); var body = Request();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        await db.StaffMembers.FromSqlInterpolated($"SELECT * FROM \"StaffMembers\" WHERE \"Id\"={MemberId} FOR UPDATE").SingleAsync(Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/appointments/") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        var write = owner.SendAsync(request, cancellation.Token);
        await using var observer = new NpgsqlConnection(database.GetConnectionString()); await observer.OpenAsync(Token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE wait_event_type='Lock' AND query LIKE '%StaffMembers%FOR UPDATE%')", observer);
            if ((bool)(await command.ExecuteScalarAsync(timeout.Token) ?? false)) break;
            await Task.Delay(20, timeout.Token);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        await transaction.CommitAsync(Token);
        using var retry = await Create(owner, body, csrf); await Body(retry, HttpStatusCode.Created);
        Assert.Single(await db.Appointments.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task AppointmentRateLimitRejectsRepeatedRequestsWithoutWriting()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        for (var attempt = 0; attempt < 60; attempt++)
        {
            using var response = await Create(owner, Request() with { RequestKey = Guid.Empty }, csrf);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var limited = await Create(owner, Request(), csrf); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.AppointmentAudits.ToArrayAsync(Token));
    }
}
