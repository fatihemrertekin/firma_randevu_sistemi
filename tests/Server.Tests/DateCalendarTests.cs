using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Business;
using Server.Features.Scheduling;
using Server.Features.Staff;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class DateCalendarTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero); }
    private const string Date = "2026-10-16";
    private static string Source(bool business, Guid id) => business ? "/api/business-hours" : $"/api/staff-members/{id}";
    private static string Path(bool business, Guid id) => Source(business, id) + "/exceptions/" + Date;
    private static string Hour(int hour, int minute = 0) => $"{Date}T{hour:00}:{minute:00}:00+03:00";
    private static DateExceptionEndpoints.UpdateRequest Day(Guid version, bool closed = false, string? end = "19:00", WeeklyBreaks.BreakRequest[]? breaks = null) => new(version, closed, closed ? null : "09:00", closed ? null : end, breaks ?? []);
    private static async Task<T> Body<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    { Assert.Equal(expected, response.StatusCode); return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(Token)); }
    private static async Task<Guid> Version(HttpClient owner, bool business, Guid id)
    { using var response = await owner.GetAsync(Source(business, id) + "/breaks", Token); return (await Body<WeeklyBreaks.Response>(response)).Version; }
    private static async Task<AppointmentEndpoints.AvailabilityResponse> Slots(HttpClient owner, Guid member, Guid service)
    { using var response = await owner.GetAsync($"/api/appointments/availability?serviceId={service}&staffMemberId={member}&date={Date}", Token); return await Body<AppointmentEndpoints.AvailabilityResponse>(response); }
    private static AppointmentEndpoints.CreateRequest Booking(ServiceDefinition service, Guid member, int hour, int minute = 0) => new(Guid.NewGuid(), service.Id, service.Version, member, Hour(hour, minute), "Sentetik müşteri", "05000000001");
    private static bool Offered(AppointmentEndpoints.AvailabilityResponse slots, int hour, int minute = 0) => slots.StartsAt.Contains(DateTimeOffset.Parse(Hour(hour, minute), System.Globalization.CultureInfo.InvariantCulture).UtcDateTime);

    [Fact]
    public async Task WeeklyBreakDateExceptionLeaveAndBothBuffersShareExactBoundaries()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        service.DurationMinutes = 25; service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10; await db.SaveChangesAsync(Token);
        object Breaks(Guid version, string start, string end) => new { version, days = Enumerable.Range(0, 7).Select(day => new WeeklyBreaks.DayRequest(day, day == 4 ? [new(start, end)] : [])).ToArray() };
        using var weekly = await PostAsync(owner, Source(true, member.Id) + "/breaks", Breaks(await Version(owner, true, member.Id), "12:00", "13:00"), csrf); Assert.Equal(HttpStatusCode.OK, weekly.StatusCode);
        using var dated = await PostAsync(owner, Path(false, member.Id), Day(await Version(owner, false, member.Id), breaks: [new("14:00", "15:00")]), csrf); Assert.Equal(HttpStatusCode.OK, dated.StatusCode);
        var leaveId = Guid.NewGuid();
        using var leave = await PostAsync(owner, Source(false, member.Id) + "/leave", new StaffLeaveEndpoints.CreateRequest(leaveId, await Version(owner, false, member.Id), Hour(15, 50), Hour(17)), csrf); Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
        var slots = await Slots(owner, member.Id, service.Id);
        foreach (var time in new[] { (11, 30), (13, 0), (13, 30), (15, 0), (15, 30), (17, 0) })
        {
            Assert.False(Offered(slots, time.Item1, time.Item2));
            using var blocked = await PostAsync(owner, "/api/appointments/", Booking(service, member.Id, time.Item1, time.Item2), csrf); Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        }
        Assert.True(Offered(slots, 13, 15)); Assert.True(Offered(slots, 15, 15));
        using var first = await PostAsync(owner, "/api/appointments/", Booking(service, member.Id, 13, 15), csrf); await Body<AppointmentEndpoints.AppointmentResponse>(first, HttpStatusCode.Created);
        var request = Booking(service, member.Id, 15, 15);
        using var second = await PostAsync(owner, "/api/appointments/", request, csrf); var appointment = await Body<AppointmentEndpoints.AppointmentResponse>(second, HttpStatusCode.Created);
        var businessVersion = await Version(owner, true, member.Id); var staffVersion = await Version(owner, false, member.Id);
        using var weeklyOverlap = await PostAsync(owner, Source(true, member.Id) + "/breaks", Breaks(businessVersion, "13:49", "14:00"), csrf); Assert.Equal(HttpStatusCode.Conflict, weeklyOverlap.StatusCode);
        using var exceptionOverlap = await PostAsync(owner, Path(false, member.Id), Day(staffVersion, breaks: [new("15:49", "16:00")]), csrf); Assert.Equal(HttpStatusCode.Conflict, exceptionOverlap.StatusCode);
        using var leaveOverlap = await PostAsync(owner, Source(false, member.Id) + "/leave/" + leaveId, new StaffLeaveEndpoints.UpdateRequest(staffVersion, Hour(15, 49), Hour(17)), csrf); Assert.Equal(HttpStatusCode.Conflict, leaveOverlap.StatusCode);
        Assert.Equal(businessVersion, await Version(owner, true, member.Id)); Assert.Equal(staffVersion, await Version(owner, false, member.Id));
        using var weeklyTouch = await PostAsync(owner, Source(true, member.Id) + "/breaks", Breaks(businessVersion, "13:50", "14:00"), csrf); Assert.Equal(HttpStatusCode.OK, weeklyTouch.StatusCode);
        using var exceptionTouch = await PostAsync(owner, Path(false, member.Id), Day(staffVersion, breaks: [new("15:50", "16:00")]), csrf); Assert.Equal(HttpStatusCode.OK, exceptionTouch.StatusCode);
        using var replay = await PostAsync(owner, "/api/appointments/", request, csrf); Assert.Equal(appointment.Id, (await Body<AppointmentEndpoints.AppointmentResponse>(replay)).Id);
        var block = await db.AppointmentTimeBlocks.AsNoTracking().SingleAsync(item => item.AppointmentId == appointment.Id, Token);
        Assert.Equal(DateTimeOffset.Parse(Hour(15, 10), System.Globalization.CultureInfo.InvariantCulture).UtcDateTime, block.OccupiedStartsAt);
        Assert.Equal(DateTimeOffset.Parse(Hour(15, 50), System.Globalization.CultureInfo.InvariantCulture).UtcDateTime, block.OccupiedEndsAt);
        Assert.Equal(2, await db.Appointments.CountAsync(Token)); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(owner, "/api/appointments/", Booking(service, member.Id, 10), csrf)));
        try { Assert.Single(results, item => item.StatusCode == HttpStatusCode.Created); Assert.Equal(19, results.Count(item => item.StatusCode == HttpStatusCode.Conflict)); }
        finally { foreach (var result in results) result.Dispose(); }
        using var bufferOnlyOverlap = await PostAsync(owner, "/api/appointments/", Booking(service, member.Id, 10, 30), csrf); Assert.Equal(HttpStatusCode.Conflict, bufferOnlyOverlap.StatusCode);
        Assert.Equal(3, await db.Appointments.CountAsync(Token)); Assert.Equal(3, await db.AppointmentAudits.CountAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CalendarRateLimitRejectsWritesWithoutChangingVersionOrAudit(int kind)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var business = kind == 0; var version = business ? (await db.BusinessHoursSchedules.SingleAsync(Token)).Version : member.Version;
        var path = kind == 2 ? Source(false, member.Id) + "/leave" : Path(business, member.Id);
        object invalid = kind == 2 ? new StaffLeaveEndpoints.CreateRequest(Guid.Empty, version, Hour(12), Hour(13)) : Day(Guid.Empty);
        for (var attempt = 0; attempt < 30; attempt++)
        { using var bad = await PostAsync(owner, path, invalid, csrf); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); }
        object valid = kind == 2 ? new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), version, Hour(12), Hour(13)) : Day(version);
        using var limited = await PostAsync(owner, path, valid, csrf); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        await db.Entry(member).ReloadAsync(Token); Assert.Equal(version, business ? (await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(Token)).Version : member.Version);
        Assert.Empty(await db.BusinessDateExceptions.ToArrayAsync(Token)); Assert.Empty(await db.StaffDateExceptions.ToArrayAsync(Token)); Assert.Empty(await db.StaffLeaves.ToArrayAsync(Token));
        Assert.Empty(await db.BusinessHoursAudits.ToArrayAsync(Token)); Assert.Empty(await db.StaffHoursAudits.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CalendarServerTimeoutRollsBackAndSameRequestCanBeRetried(int kind)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var business = kind == 0; var version = await Version(owner, business, member.Id);
        var path = kind == 2 ? Source(false, member.Id) + "/leave" : Path(business, member.Id);
        object payload = kind == 2 ? new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), version, Hour(12), Hour(13)) : Day(version);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand(business ? "SELECT 1 FROM \"BusinessHoursSchedules\" WHERE \"Id\"=1 FOR UPDATE" : "SELECT 1 FROM \"StaffMembers\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction))
        { if (!business) command.Parameters.AddWithValue("id", member.Id); await command.ExecuteScalarAsync(Token); }
        var timer = System.Diagnostics.Stopwatch.StartNew(); var write = PostAsync(owner, path, payload, csrf);
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) < 1) await Task.Delay(20, wait.Token);
        using var failed = await write.WaitAsync(TimeSpan.FromSeconds(25), Token); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.InRange(timer.Elapsed.TotalSeconds, 12, 25); await transaction.CommitAsync(Token);
        Assert.Equal(version, await Version(owner, business, member.Id));
        Assert.Empty(await db.BusinessDateExceptions.ToArrayAsync(Token)); Assert.Empty(await db.StaffDateExceptions.ToArrayAsync(Token)); Assert.Empty(await db.StaffLeaves.ToArrayAsync(Token));
        Assert.Empty(await db.BusinessHoursAudits.ToArrayAsync(Token)); Assert.Empty(await db.StaffHoursAudits.ToArrayAsync(Token));
        using var retry = await PostAsync(owner, path, payload, csrf); Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.NotEqual(version, await Version(owner, business, member.Id));
        Assert.Equal(1, await db.BusinessHoursAudits.CountAsync(Token) + await db.StaffHoursAudits.CountAsync(Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExceptionOverridesWeekAndBreaksAndRemovalCannotInvalidateAppointments(bool business)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        service.BufferAfterMinutes = 10; await db.SaveChangesAsync(Token);
        var version = await Version(owner, business, member.Id); var path = Path(business, member.Id);
        using var missing = await owner.GetAsync(path, Token); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var weekBreak = await PostAsync(owner, Source(business, member.Id) + "/breaks", new { version, days = Enumerable.Range(0, 7).Select(day => new WeeklyBreaks.DayRequest(day, day == 4 ? [new("12:00", "13:00")] : [])).ToArray() }, csrf); Assert.Equal(HttpStatusCode.OK, weekBreak.StatusCode);
        Assert.False(Offered(await Slots(owner, member.Id, service.Id), 12, 15));
        version = await Version(owner, business, member.Id);
        using var exception = await PostAsync(owner, path, Day(version), csrf); var saved = await Body<DateExceptionEndpoints.Response>(exception); Assert.NotEqual(version, saved.Version);
        Assert.True(Offered(await Slots(owner, member.Id, service.Id), 12, 15));
        using var noOp = await PostAsync(owner, path, Day(saved.Version), csrf); Assert.Equal(saved.Version, (await Body<DateExceptionEndpoints.Response>(noOp)).Version);
        using var stale = await PostAsync(owner, path, Day(version, true), csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var booked = await PostAsync(owner, "/api/appointments/", Booking(service, member.Id, 17), csrf); var appointment = await Body<AppointmentEndpoints.AppointmentResponse>(booked, HttpStatusCode.Created);
        using var changeWeekBreak = await PostAsync(owner, Source(business, member.Id) + "/breaks", new { version = saved.Version, days = Enumerable.Range(0, 7).Select(day => new WeeklyBreaks.DayRequest(day, day == 4 ? [new("17:00", "18:00")] : [])).ToArray() }, csrf); Assert.Equal(HttpStatusCode.OK, changeWeekBreak.StatusCode);
        version = await Version(owner, business, member.Id);
        using var clearWeekBreak = await PostAsync(owner, Source(business, member.Id) + "/breaks", new { version, days = Enumerable.Range(0, 7).Select(day => new WeeklyBreaks.DayRequest(day, [])).ToArray() }, csrf); Assert.Equal(HttpStatusCode.OK, clearWeekBreak.StatusCode);
        version = await Version(owner, business, member.Id);
        using var hours = await PostAsync(owner, business ? "/api/business-hours/" : Source(false, member.Id) + "/hours", new { version, days = Enumerable.Range(0, 7).Select(day => new WeeklyHours.DayRequest(day, false, "09:00", "13:30")).ToArray() }, csrf); Assert.Equal(HttpStatusCode.OK, hours.StatusCode);
        version = await Version(owner, business, member.Id);
        foreach (var desired in new[] { Day(version, true), Day(version, end: "17:10"), Day(version, breaks: [new("17:20", "17:45")]) })
        { using var rejected = await PostAsync(owner, path, desired, csrf); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode); Assert.Contains("appointments_outside_hours", await rejected.Content.ReadAsStringAsync(Token)); }
        using var remove = await PostAsync(owner, path + "/remove", new { version }, csrf); Assert.Equal(HttpStatusCode.Conflict, remove.StatusCode);
        Assert.Equal(version, await Version(owner, business, member.Id));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AppointmentTimeBlocks\" SET \"Status\"='Cancelled' WHERE \"AppointmentId\"={appointment.Id}", Token);
        using var removed = await PostAsync(owner, path + "/remove", new { version }, csrf); Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var gone = await owner.GetAsync(path, Token); Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode); Assert.False(Offered(await Slots(owner, member.Id, service.Id), 17));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task ClosedWeekNeedsBothOpenExceptionsAndLeaveAlwaysWins()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        foreach (var business in new[] { true, false })
        {
            var version = await Version(owner, business, member.Id);
            using var hours = await PostAsync(owner, business ? "/api/business-hours/" : Source(false, member.Id) + "/hours", new { version, days = Enumerable.Range(0, 7).Select(day => new WeeklyHours.DayRequest(day, true, null, null)).ToArray() }, csrf); Assert.Equal(HttpStatusCode.OK, hours.StatusCode);
        }
        var businessVersion = await Version(owner, true, member.Id);
        using var businessDay = await PostAsync(owner, Path(true, member.Id), Day(businessVersion, breaks: [new("12:00", "13:00")]), csrf); Assert.Equal(HttpStatusCode.OK, businessDay.StatusCode);
        Assert.Empty((await Slots(owner, member.Id, service.Id)).StartsAt);
        using var staffDay = await PostAsync(owner, Path(false, member.Id), Day(await Version(owner, false, member.Id)), csrf); Assert.Equal(HttpStatusCode.OK, staffDay.StatusCode);
        Assert.True(Offered(await Slots(owner, member.Id, service.Id), 13, 15)); Assert.False(Offered(await Slots(owner, member.Id, service.Id), 12, 15));
        using var leave = await PostAsync(owner, Source(false, member.Id) + "/leave", new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), await Version(owner, false, member.Id), Hour(13), Hour(14)), csrf); Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
        Assert.False(Offered(await Slots(owner, member.Id, service.Id), 13, 15));
        using var denied = await PostAsync(owner, "/api/appointments/", Booking(service, member.Id, 13, 15), csrf); Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        using var closed = await PostAsync(owner, Path(true, member.Id), Day(await Version(owner, true, member.Id), true), csrf); Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Empty((await Slots(owner, member.Id, service.Id)).StartsAt);
    }

    [Fact]
    public async Task LeaveValidationReplayEditingRemovalPaginationAndObjectScopeAreProtected()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var members = await db.StaffMembers.OrderBy(item => item.Id).ToArrayAsync(Token); var id = members[0].Id; var path = Source(false, id) + "/leave";
        var version = await Version(owner, false, id); var request = new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), version, Hour(12), Hour(13));
        foreach (var invalid in new[] { request with { Id = Guid.Empty }, request with { Version = Guid.Empty }, request with { StartsAt = Date + "T12:00:00" }, request with { EndsAt = Hour(12) }, request with { EndsAt = "2027-10-18T13:00:00+03:00" }, request with { EndsAt = Date + "T13:00:01+03:00" } })
        { using var rejected = await PostAsync(owner, path, invalid, csrf); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); }
        using var created = await PostAsync(owner, path, request, csrf); var saved = await Body<StaffLeaveEndpoints.Response>(created);
        using var replay = await PostAsync(owner, path, request, csrf); Assert.Equal(saved.Version, (await Body<StaffLeaveEndpoints.Response>(replay)).Version);
        using var changedKey = await PostAsync(owner, path, request with { EndsAt = Hour(14) }, csrf); Assert.Equal(HttpStatusCode.Conflict, changedKey.StatusCode);
        using var otherScope = await PostAsync(owner, Source(false, members[1].Id) + "/leave/" + request.Id, new StaffLeaveEndpoints.UpdateRequest(await Version(owner, false, members[1].Id), Hour(10), Hour(11)), csrf); Assert.Equal(HttpStatusCode.NotFound, otherScope.StatusCode);
        using var overlap = await PostAsync(owner, path, new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), saved.Version, Hour(12, 30), Hour(14)), csrf); Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        using var adjacent = await PostAsync(owner, path, new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), saved.Version, Hour(13), Hour(14)), csrf); var second = await Body<StaffLeaveEndpoints.Response>(adjacent);
        using var editOverlap = await PostAsync(owner, path + "/" + request.Id, new StaffLeaveEndpoints.UpdateRequest(second.Version, Hour(12), Hour(14)), csrf); Assert.Equal(HttpStatusCode.Conflict, editOverlap.StatusCode);
        using var edited = await PostAsync(owner, path + "/" + request.Id, new StaffLeaveEndpoints.UpdateRequest(second.Version, Hour(11), Hour(12)), csrf); var current = await Body<StaffLeaveEndpoints.Response>(edited);
        using var currentAsCreate = await PostAsync(owner, path, request with { Version = current.Version, StartsAt = Hour(11), EndsAt = Hour(12) }, csrf); Assert.Equal(HttpStatusCode.Conflict, currentAsCreate.StatusCode);
        using var originalReplay = await PostAsync(owner, path, request, csrf); Assert.Equal(current.Leave.StartsAt, (await Body<StaffLeaveEndpoints.Response>(originalReplay)).Leave.StartsAt);
        using var remove = await PostAsync(owner, path + "/" + request.Id + "/remove", new { version = current.Version }, csrf); var removed = await Body<StaffLeaveEndpoints.Response>(remove); Assert.True(removed.Leave.IsRemoved);
        using var repeatRemove = await PostAsync(owner, path + "/" + request.Id + "/remove", new { version = removed.Version }, csrf); Assert.Equal(removed.Version, (await Body<StaffLeaveEndpoints.Response>(repeatRemove)).Version);
        using var recreate = await PostAsync(owner, path, request with { Version = removed.Version }, csrf); Assert.Equal(HttpStatusCode.Conflict, recreate.StatusCode);
        using var page = await owner.GetAsync(path + $"?from={Uri.EscapeDataString(Hour(0))}&to={Uri.EscapeDataString(Hour(23))}&pageSize=1", Token); var listed = await Body<StaffLeaveEndpoints.Page>(page); Assert.Single(listed.Items); Assert.Equal(second.Leave.Id, listed.Items[0].Id); Assert.False(listed.HasNextPage);
        foreach (var query in new[] { "pageSize=51", "page=0" }) { using var badPage = await owner.GetAsync(path + $"?from={Uri.EscapeDataString(Hour(0))}&to={Uri.EscapeDataString(Hour(23))}&{query}", Token); Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode); }
        Assert.Equal(2, await db.StaffLeaves.CountAsync(Token)); Assert.Equal(4, await db.StaffHoursAudits.CountAsync(item => item.Kind == "Leave", Token));
        using var third = await PostAsync(owner, path, new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), removed.Version, Hour(14), Hour(15)), csrf); var latest = await Body<StaffLeaveEndpoints.Response>(third);
        using var more = await owner.GetAsync(path + $"?from={Uri.EscapeDataString(Hour(0))}&to={Uri.EscapeDataString(Hour(23))}&pageSize=1", Token); Assert.True((await Body<StaffLeaveEndpoints.Page>(more)).HasNextPage);
        using var next = await owner.GetAsync(path + $"?from={Uri.EscapeDataString(Hour(0))}&to={Uri.EscapeDataString(Hour(23))}&pageSize=1&page=2", Token); Assert.Equal(latest.Leave.Id, Assert.Single((await Body<StaffLeaveEndpoints.Page>(next)).Items).Id);
        using var staleEdit = await PostAsync(owner, path + "/" + latest.Leave.Id, new StaffLeaveEndpoints.UpdateRequest(removed.Version, Hour(15), Hour(16)), csrf); Assert.Equal(HttpStatusCode.Conflict, staleEdit.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AuthorityValidationAndAuditFailureDoNotPartiallyChangeCalendar(int kind)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var business = kind == 0; var version = await Version(owner, business, member.Id); var leaveId = Guid.NewGuid();
        var path = kind == 2 ? Source(false, member.Id) + "/leave" : Path(business, member.Id);
        var read = kind == 2 ? path + $"?from={Uri.EscapeDataString(Hour(0))}&to={Uri.EscapeDataString(Hour(23))}" : path;
        using var anonymous = app.CreateClient(); using var noLogin = await anonymous.GetAsync(read, Token); Assert.Equal(HttpStatusCode.Unauthorized, noLogin.StatusCode);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        using var staffRead = await staff.GetAsync(read, Token); Assert.Equal(HttpStatusCode.Forbidden, staffRead.StatusCode);
        object payload = kind == 2 ? new StaffLeaveEndpoints.CreateRequest(leaveId, version, Hour(12), Hour(13)) : Day(version, breaks: [new("12:00", "13:00")]);
        using var noCsrf = await PostAsync(owner, path, payload, null); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var staffWrite = await PostAsync(staff, path, payload, await GetCsrfAsync(staff)); Assert.Equal(HttpStatusCode.Forbidden, staffWrite.StatusCode);
        if (kind != 2)
        {
            foreach (var invalid in new[] { Day(version) with { IsClosed = null }, Day(version, true) with { OpensAt = "09:00" }, Day(version, true, breaks: [new("12:00", "13:00")]), Day(version) with { ClosesAt = "24:00" }, Day(version, breaks: [new("12:00", "13:00"), new("12:30", "14:00")]), Day(version, breaks: Enumerable.Repeat(new WeeklyBreaks.BreakRequest("12:00", "13:00"), 9).ToArray()) })
            { using var bad = await PostAsync(owner, path, invalid, csrf); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); }
        }
        using var created = await PostAsync(owner, path, payload, csrf); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        version = await Version(owner, business, member.Id);
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_calendar_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic calendar audit failure'; END $$", Token);
        await db.Database.ExecuteSqlRawAsync(business ? "CREATE TRIGGER reject_audit BEFORE INSERT ON \"BusinessHoursAudits\" FOR EACH ROW EXECUTE FUNCTION reject_calendar_audit()" : "CREATE TRIGGER reject_audit BEFORE INSERT ON \"StaffHoursAudits\" FOR EACH ROW EXECUTE FUNCTION reject_calendar_audit()", Token);
        using var failed = kind == 2 ? await PostAsync(owner, path + "/" + leaveId, new StaffLeaveEndpoints.UpdateRequest(version, Hour(13), Hour(14)), csrf) : await PostAsync(owner, path, Day(version, breaks: [new("13:00", "14:00")]), csrf);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); Assert.Equal(version, await Version(owner, business, member.Id));
        if (kind == 2) Assert.Equal(DateTimeOffset.Parse(Hour(12), System.Globalization.CultureInfo.InvariantCulture).UtcDateTime, (await db.StaffLeaves.SingleAsync(Token)).StartsAt);
        else { using var retained = await owner.GetAsync(path, Token); Assert.Equal("12:00", Assert.Single((await Body<DateExceptionEndpoints.Response>(retained)).Breaks).StartsAt); }
        if (!business)
        {
            await db.Entry(member).ReloadAsync(Token); member.IsActive = false; await db.SaveChangesAsync(Token);
            using var inactiveRead = await owner.GetAsync(read, Token); Assert.Equal(HttpStatusCode.OK, inactiveRead.StatusCode);
            object currentPayload = kind == 2 ? new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), version, Hour(14), Hour(15)) : Day(version);
            using var inactiveWrite = await PostAsync(owner, path, currentPayload, csrf); Assert.Equal(HttpStatusCode.Conflict, inactiveWrite.StatusCode);
            member.IsDeleted = true; await db.SaveChangesAsync(Token); using var deleted = await owner.GetAsync(read, Token); Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        }
        var user = await db.Users.SingleAsync(item => item.Id == seed.OwnerId, Token); user.SecurityStamp = Guid.NewGuid().ToString(); await db.SaveChangesAsync(Token);
        using var revoked = await PostAsync(owner, path, payload, csrf); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task MigrationPreservesWeeklyDataAndDatabaseChecksPreventOverlapsAndDestructiveDown()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        using var weekly = await PostAsync(owner, "/api/business-hours/breaks", new { version = await Version(owner, true, member.Id), days = Enumerable.Range(0, 7).Select(day => new WeeklyBreaks.DayRequest(day, day == 4 ? [new("12:00", "13:00")] : [])).ToArray() }, csrf); Assert.Equal(HttpStatusCode.OK, weekly.StatusCode);
        var oldVersion = await Version(owner, true, member.Id); var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261014202232_WeeklyBreaks", Token); await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(oldVersion, await Version(owner, true, member.Id)); Assert.Single(await db.BusinessWeeklyBreaks.ToArrayAsync(Token)); Assert.Single(await db.BusinessHoursAudits.Where(item => item.Kind == "Breaks").ToArrayAsync(Token)); await AssertOriginalStateAsync(app, seed);
        var date = new DateOnly(2026, 10, 16);
        db.BusinessDateExceptions.Add(new() { ScheduleId = 1, Date = date, OpensAtMinute = 540, ClosesAtMinute = 1140 });
        db.StaffDateExceptions.Add(new() { StaffMemberId = member.Id, Date = date, OpensAtMinute = 540, ClosesAtMinute = 1140 }); await db.SaveChangesAsync(Token);
        foreach (var business in new[] { true, false })
        {
            await InsertBreak(database.GetConnectionString(), business, member.Id, 1, 720, 780);
            var overlap = await Assert.ThrowsAsync<PostgresException>(() => InsertBreak(database.GetConnectionString(), business, member.Id, 2, 750, 800)); Assert.Equal("23P01", overlap.SqlState);
            var invalid = await Assert.ThrowsAsync<PostgresException>(() => InsertBreak(database.GetConnectionString(), business, member.Id, 9, 780, 800)); Assert.Equal("23514", invalid.SqlState);
            await InsertBreak(database.GetConnectionString(), business, member.Id, 2, 780, 800);
        }
        await InsertLeave(database.GetConnectionString(), member.Id, Hour(12), Hour(13));
        var leaveOverlap = await Assert.ThrowsAsync<PostgresException>(() => InsertLeave(database.GetConnectionString(), member.Id, Hour(12, 30), Hour(14))); Assert.Equal("23P01", leaveOverlap.SqlState);
        await InsertLeave(database.GetConnectionString(), member.Id, Hour(13), Hour(14));
        foreach (var sql in new[] { "UPDATE \"StaffLeaves\" SET \"EndsAt\"=\"StartsAt\"", "UPDATE \"StaffLeaves\" SET \"EndsAt\"=\"StartsAt\"+INTERVAL '367 days'", "UPDATE \"StaffLeaves\" SET \"StartsAt\"='-infinity'", "UPDATE \"StaffLeaves\" SET \"StartsAt\"=\"StartsAt\"+INTERVAL '1 second'", "UPDATE \"BusinessDateExceptions\" SET \"IsClosed\"=true", "UPDATE \"StaffLeaves\" SET \"RequestHash\"='bad'" })
        { var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("23514", invalid.SqlState); }
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261014202232_WeeklyBreaks", Token)); Assert.Equal("P0001", refusal.SqlState); Assert.Equal(2, await db.StaffLeaves.CountAsync(Token));
    }

    [Fact]
    public async Task ConcurrentReuseOfLeaveIdAcrossMembersReturnsConflictAndRollsBackAuditAndVersion()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var members = await db.StaffMembers.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync(Token);
        var version = await Version(owner, false, members[1]); var leaveId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("INSERT INTO \"StaffLeaves\" (\"Id\",\"StaffMemberId\",\"StartsAt\",\"EndsAt\",\"RequestHash\",\"IsRemoved\") VALUES (@id,@member,TIMESTAMPTZ '2026-10-16 09:00:00+00',TIMESTAMPTZ '2026-10-16 10:00:00+00',@hash,false)", connection, transaction))
        { command.Parameters.AddWithValue("id", leaveId); command.Parameters.AddWithValue("member", members[0]); command.Parameters.AddWithValue("hash", new string('A', 64)); await command.ExecuteNonQueryAsync(Token); }
        var write = PostAsync(owner, Source(false, members[1]) + "/leave", new StaffLeaveEndpoints.CreateRequest(leaveId, version, Hour(14), Hour(15)), csrf);
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) < 1) await Task.Delay(20, wait.Token);
        await transaction.CommitAsync(Token); using var response = await write; Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(version, await Version(owner, false, members[1])); Assert.Empty(await db.StaffHoursAudits.Where(item => item.Kind == "Leave").ToArrayAsync(Token)); Assert.Single(await db.StaffLeaves.ToArrayAsync(Token));
    }

    private static async Task InsertBreak(string connectionString, bool business, Guid member, int position, int start, int end)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(business ? "INSERT INTO \"BusinessDateBreaks\" (\"ScheduleId\",\"Date\",\"Position\",\"StartsAtMinute\",\"EndsAtMinute\") VALUES (1,DATE '2026-10-16',@position,@start,@end)" : "INSERT INTO \"StaffDateBreaks\" (\"StaffMemberId\",\"Date\",\"Position\",\"StartsAtMinute\",\"EndsAtMinute\") VALUES (@member,DATE '2026-10-16',@position,@start,@end)", connection);
        if (!business) command.Parameters.AddWithValue("member", member); command.Parameters.AddWithValue("position", position); command.Parameters.AddWithValue("start", start); command.Parameters.AddWithValue("end", end); await command.ExecuteNonQueryAsync(Token);
    }
    private static async Task InsertLeave(string connectionString, Guid member, string first, string last)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand("INSERT INTO \"StaffLeaves\" (\"Id\",\"StaffMemberId\",\"StartsAt\",\"EndsAt\",\"RequestHash\",\"IsRemoved\") VALUES (@id,@member,@start,@end,@hash,false)", connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("member", member); command.Parameters.AddWithValue("start", DateTimeOffset.Parse(first, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime); command.Parameters.AddWithValue("end", DateTimeOffset.Parse(last, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime); command.Parameters.AddWithValue("hash", new string('A', 64)); await command.ExecuteNonQueryAsync(Token);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(5, false)]
    public async Task CalendarChangeAndBookingShareTheParentLock(int kind, bool changeFirst)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); using var other = await AppointmentTests.OtherOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); var otherCsrf = await GetCsrfAsync(other);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        var business = kind is 0 or 3; var remove = kind is 3 or 4; var leaveId = Guid.NewGuid();
        if (kind == 5)
        {
            service.DurationMinutes = 25; service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10; await db.SaveChangesAsync(Token);
            using var weekly = await PostAsync(owner, Source(true, member.Id) + "/breaks", new { version = await Version(owner, true, member.Id), days = Enumerable.Range(0, 7).Select(day => new WeeklyBreaks.DayRequest(day, day == 4 ? [new("12:00", "13:00")] : [])).ToArray() }, csrf); Assert.Equal(HttpStatusCode.OK, weekly.StatusCode);
            using var dated = await PostAsync(owner, Path(false, member.Id), Day(await Version(owner, false, member.Id), breaks: [new("14:00", "15:00")]), csrf); Assert.Equal(HttpStatusCode.OK, dated.StatusCode);
            using var leave = await PostAsync(owner, Source(false, member.Id) + "/leave", new StaffLeaveEndpoints.CreateRequest(leaveId, await Version(owner, false, member.Id), Hour(15, 50), Hour(17)), csrf); Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
        }
        if (remove)
        {
            using var hours = await PostAsync(owner, business ? "/api/business-hours/" : Source(false, member.Id) + "/hours", new { version = await Version(owner, business, member.Id), days = Enumerable.Range(0, 7).Select(day => new WeeklyHours.DayRequest(day, false, "09:00", "13:00")).ToArray() }, csrf); Assert.Equal(HttpStatusCode.OK, hours.StatusCode);
            using var opened = await PostAsync(owner, Path(business, member.Id), Day(await Version(owner, business, member.Id)), csrf); Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        }
        var version = await Version(owner, business, member.Id);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand(business ? "SELECT 1 FROM \"BusinessHoursSchedules\" WHERE \"Id\"=1 FOR UPDATE" : "SELECT 1 FROM \"StaffMembers\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction)) { if (!business) command.Parameters.AddWithValue("id", member.Id); await command.ExecuteScalarAsync(Token); }
        Task<HttpResponseMessage> Change() => kind == 5 ? PostAsync(other, Source(false, member.Id) + "/leave/" + leaveId, new StaffLeaveEndpoints.UpdateRequest(version, Hour(15, 49), Hour(17)), otherCsrf) : kind == 2 ? PostAsync(other, Source(false, member.Id) + "/leave", new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), version, Hour(12), Hour(13)), otherCsrf) : remove ? PostAsync(other, Path(business, member.Id) + "/remove", new { version }, otherCsrf) : PostAsync(other, Path(business, member.Id), Day(version, true), otherCsrf);
        Task<HttpResponseMessage> Book() => PostAsync(owner, "/api/appointments/", Booking(service, member.Id, kind == 5 ? 15 : remove ? 17 : 12, kind == 5 ? 15 : 30), csrf);
        var first = changeFirst ? Change() : Book(); using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) < 1) await Task.Delay(20, wait.Token);
        var second = changeFirst ? Book() : Change(); await AwaitInviteLocksAsync(app); await transaction.CommitAsync(Token);
        using var firstResult = await first; using var secondResult = await second; Assert.Equal(changeFirst ? HttpStatusCode.OK : HttpStatusCode.Created, firstResult.StatusCode); Assert.Equal(HttpStatusCode.Conflict, secondResult.StatusCode);
        Assert.Equal(changeFirst ? 0 : 1, await db.Appointments.CountAsync(Token));
    }
}
