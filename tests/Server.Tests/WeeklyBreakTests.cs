using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Audit;
using Server.Features.Business;
using Server.Features.Scheduling;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class WeeklyBreakTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 6, 0, 0, TimeSpan.Zero); }
    private static string Path(bool business, Guid member) => business ? "/api/business-hours/breaks" : $"/api/staff-members/{member}/breaks";
    private static WeeklyBreaks.DayRequest[] Days(string? start = "12:00", string end = "13:00") => Enumerable.Range(0, 7)
        .Select(day => new WeeklyBreaks.DayRequest(day, day == 3 && start is not null ? [new(start, end)] : [])).ToArray();
    private static async Task<T> Body<T>(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode); return Assert.IsType<T>(await response.Content.ReadFromJsonAsync<T>(Token));
    }
    private static async Task<WeeklyBreaks.Response> Read(HttpClient owner, string path)
    {
        using var response = await owner.GetAsync(path, Token); Assert.True(response.Headers.CacheControl?.NoStore); return await Body<WeeklyBreaks.Response>(response);
    }
    private static AppointmentEndpoints.CreateRequest Booking(ServiceDefinition service, Guid member, int hour, int minute = 0) =>
        new(Guid.NewGuid(), service.Id, service.Version, member, new DateTimeOffset(2026, 10, 15, hour, minute, 0, TimeSpan.FromHours(3)).ToString("O", CultureInfo.InvariantCulture), "Sentetik müşteri", "05000000001");
    private static async Task InsertAsync(string connectionString, bool business, Guid member, int day, int position, int start, int end)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(business
            ? "INSERT INTO \"BusinessWeeklyBreaks\" (\"ScheduleId\",\"Day\",\"Position\",\"StartsAtMinute\",\"EndsAtMinute\") VALUES (1,@day,@position,@start,@end)"
            : "INSERT INTO \"StaffWeeklyBreaks\" (\"StaffMemberId\",\"Day\",\"Position\",\"StartsAtMinute\",\"EndsAtMinute\") VALUES (@member,@day,@position,@start,@end)", connection);
        if (!business) command.Parameters.AddWithValue("member", member);
        command.Parameters.AddWithValue("day", day); command.Parameters.AddWithValue("position", position);
        command.Parameters.AddWithValue("start", start); command.Parameters.AddWithValue("end", end); await command.ExecuteNonQueryAsync(Token);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidatedWeekVersionNoOpAndAuditAreProtected(bool business)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var path = Path(business, member.Id); var original = await Read(owner, path); Assert.All(original.Days, day => Assert.Empty(day.Breaks));
        using var anonymous = app.CreateClient(); using var denied = await anonymous.GetAsync(path, Token); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        using var staffRead = await staff.GetAsync(path, Token); Assert.Equal(HttpStatusCode.Forbidden, staffRead.StatusCode);
        using var staffWrite = await PostAsync(staff, path, new { version = original.Version, days = Days() }, await GetCsrfAsync(staff)); Assert.Equal(HttpStatusCode.Forbidden, staffWrite.StatusCode);
        using var noCsrf = await PostAsync(owner, path, new { version = original.Version, days = Days() }, null); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        var invalid = new[] { Days("08:00"), Days("13:00", "12:00"), Days("12:00", "24:00"), Days("12:00", "12:00"), Days("12:0"), Days() };
        invalid[^1][3] = new(3, [new("12:00", "13:00"), new("12:30", "14:00")]);
        foreach (var days in invalid) { using var rejected = await PostAsync(owner, path, new { version = original.Version, days }, csrf); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); }
        foreach (var days in new[] { Array.Empty<WeeklyBreaks.DayRequest>(), Days().Select(_ => new WeeklyBreaks.DayRequest(3, [])).ToArray(), Days().Select(item => item.Day == 3 ? new WeeklyBreaks.DayRequest(3, null) : item).ToArray(), Days().Select(item => item.Day == 3 ? new WeeklyBreaks.DayRequest(3, Enumerable.Repeat(new WeeklyBreaks.BreakRequest("12:00", "13:00"), 9).ToArray()) : item).ToArray() })
        { using var rejected = await PostAsync(owner, path, new { version = original.Version, days }, csrf); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); }
        using var noVersion = await PostAsync(owner, path, new { version = Guid.Empty, days = Days() }, csrf); Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
        using var empty = await PostAsync(owner, path, new { version = original.Version, days = Days(null) }, csrf); Assert.Equal(original.Version, (await Body<WeeklyBreaks.Response>(empty)).Version);
        using var write = await PostAsync(owner, path, new { version = original.Version, days = Days() }, csrf); var updated = await Body<WeeklyBreaks.Response>(write); Assert.NotEqual(original.Version, updated.Version);
        using var stale = await PostAsync(owner, path, new { version = original.Version, days = Days(null) }, csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var noOp = await PostAsync(owner, path, new { version = updated.Version, days = Days() }, csrf); Assert.Equal(updated.Version, (await Body<WeeklyBreaks.Response>(noOp)).Version);
        var hoursPath = business ? "/api/business-hours/" : $"/api/staff-members/{member.Id}/hours";
        var hours = Enumerable.Range(0, 7).Select(day => new WeeklyHours.DayRequest(day, false, "09:00", "12:00")).ToArray();
        using var outside = await PostAsync(owner, hoursPath, new { version = updated.Version, days = hours }, csrf); Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode); Assert.Contains("breaks_outside_hours", await outside.Content.ReadAsStringAsync(Token));
        Assert.Equal(updated.Version, (await Read(owner, path)).Version);
        if (business) Assert.Single(await db.BusinessHoursAudits.Where(item => item.Kind == "Breaks").ToArrayAsync(Token)); else Assert.Single(await db.StaffHoursAudits.Where(item => item.Kind == "Breaks").ToArrayAsync(Token));
        using var log = await owner.GetAsync("/api/audit-log/?category=definitions", Token); Assert.Contains((await Body<AuditLogEndpoints.Page>(log)).Items, item => item.Action == "Molalar güncellendi");
        using var clear = await PostAsync(owner, path, new { version = updated.Version, days = Days(null) }, csrf); Assert.All((await Body<WeeklyBreaks.Response>(clear)).Days, day => Assert.Empty(day.Breaks));
        await AssertOriginalStateAsync(app, seed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuffersAndBreakBoundariesMatchSlotsAndBookingsAndProtectExistingRecords(bool business)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.DurationMinutes = 25; service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10; await db.SaveChangesAsync(Token);
        var path = Path(business, member.Id); var current = await Read(owner, path);
        using var write = await PostAsync(owner, path, new { version = current.Version, days = Days() }, csrf); var saved = await Body<WeeklyBreaks.Response>(write);
        using var preview = await owner.GetAsync($"/api/appointments/availability?serviceId={service.Id}&staffMemberId={member.Id}&date=2026-10-15", Token); var slots = await Body<AppointmentEndpoints.AvailabilityResponse>(preview);
        Assert.DoesNotContain(new DateTime(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc), slots.StartsAt); Assert.DoesNotContain(new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc), slots.StartsAt); Assert.Contains(new DateTime(2026, 10, 15, 10, 15, 0, DateTimeKind.Utc), slots.StartsAt);
        using var tail = await PostAsync(owner, "/api/appointments/", Booking(service, member.Id, 11, 30), csrf); Assert.Equal(HttpStatusCode.Conflict, tail.StatusCode);
        using var front = await PostAsync(owner, "/api/appointments/", Booking(service, member.Id, 13), csrf); Assert.Equal(HttpStatusCode.Conflict, front.StatusCode);
        using var allowed = await PostAsync(owner, "/api/appointments/", Booking(service, member.Id, 13, 15), csrf); var appointment = await Body<AppointmentEndpoints.AppointmentResponse>(allowed, HttpStatusCode.Created);
        using var conflicting = await PostAsync(owner, path, new { version = saved.Version, days = Days("13:40", "14:00") }, csrf); Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        Assert.Contains("appointments_outside_hours", await conflicting.Content.ReadAsStringAsync(Token)); Assert.Equal(saved.Version, (await Read(owner, path)).Version);
        Assert.Single(await db.Appointments.ToArrayAsync(Token)); Assert.Equal("Confirmed", (await db.AppointmentTimeBlocks.SingleAsync(Token)).Status);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AppointmentTimeBlocks\" SET \"Status\"='Cancelled' WHERE \"AppointmentId\"={appointment.Id}", Token);
        using var released = await PostAsync(owner, path, new { version = saved.Version, days = Days("13:40", "14:00") }, csrf); Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        var touching = await Read(owner, path);
        using var adjacent = await PostAsync(owner, path, new { version = touching.Version, days = Days().Select(item => item.Day == 3 ? new WeeklyBreaks.DayRequest(3, [new("12:00", "13:00"), new("13:00", "14:00")]) : item).ToArray() }, csrf); Assert.Equal(HttpStatusCode.OK, adjacent.StatusCode);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task ReservationAndBreakUseTheSameParentLock(bool business, bool breakFirst)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); using var other = await AppointmentTests.OtherOwnerAsync(seed);
        var csrf = await GetCsrfAsync(owner); var otherCsrf = await GetCsrfAsync(other);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        var path = Path(business, member.Id); var current = await Read(owner, path);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand(business ? "SELECT 1 FROM \"BusinessHoursSchedules\" WHERE \"Id\"=1 FOR UPDATE" : "SELECT 1 FROM \"StaffMembers\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction)) { if (!business) command.Parameters.AddWithValue("id", member.Id); await command.ExecuteScalarAsync(Token); }
        Task<HttpResponseMessage> Break() => PostAsync(other, path, new { version = current.Version, days = Days() }, otherCsrf);
        Task<HttpResponseMessage> Book() => PostAsync(owner, "/api/appointments/", Booking(service, member.Id, 12, 30), csrf);
        var first = breakFirst ? Break() : Book();
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) < 1) await Task.Delay(20, wait.Token);
        var second = breakFirst ? Book() : Break(); await AwaitInviteLocksAsync(app); await transaction.CommitAsync(Token);
        using var firstResult = await first; using var secondResult = await second;
        Assert.Equal(breakFirst ? HttpStatusCode.OK : HttpStatusCode.Created, firstResult.StatusCode); Assert.Equal(HttpStatusCode.Conflict, secondResult.StatusCode);
        Assert.Equal(breakFirst ? 0 : 1, await db.Appointments.CountAsync(Token));
        Assert.Equal(breakFirst ? 1 : 0, business ? await db.BusinessWeeklyBreaks.CountAsync(Token) : await db.StaffWeeklyBreaks.CountAsync(Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedAuditInsertRestoresBreaksAndParentVersion(bool business)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var path = Path(business, member.Id);
        var first = await Read(owner, path);
        using var write = await PostAsync(owner, path, new { version = first.Version, days = Days() }, csrf); var before = await Body<WeeklyBreaks.Response>(write);
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_break_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic audit failure'; END $$", Token);
        await db.Database.ExecuteSqlRawAsync(business
            ? "CREATE TRIGGER reject_audit BEFORE INSERT ON \"BusinessHoursAudits\" FOR EACH ROW EXECUTE FUNCTION reject_break_audit()"
            : "CREATE TRIGGER reject_audit BEFORE INSERT ON \"StaffHoursAudits\" FOR EACH ROW EXECUTE FUNCTION reject_break_audit()", Token);
        using var failed = await PostAsync(owner, path, new { version = before.Version, days = Days("13:00", "14:00") }, csrf);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var retained = await Read(owner, path); Assert.Equal(before.Version, retained.Version); Assert.Equal("12:00", Assert.Single(retained.Days[3].Breaks).StartsAt);
        Assert.Equal(1, business ? await db.BusinessHoursAudits.CountAsync(item => item.Kind == "Breaks", Token) : await db.StaffHoursAudits.CountAsync(item => item.Kind == "Breaks", Token));
        if (!business)
        {
            await db.Entry(member).ReloadAsync(Token);
            member.IsActive = false; await db.SaveChangesAsync(Token);
            using var inactive = await PostAsync(owner, path, new { version = retained.Version, days = Days(null) }, csrf); Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
            Assert.Equal(retained.Version, (await Read(owner, path)).Version);
            member.IsDeleted = true; await db.SaveChangesAsync(Token);
            using var deleted = await owner.GetAsync(path, Token); Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        }
    }

    [Fact]
    public async Task DatabaseChecksAndAdditiveMigrationPreserveDataAndRefuseDestructiveDown()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261014192252_AppointmentBuffers", Token); await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(7, await db.BusinessOpeningDays.CountAsync(Token)); Assert.Equal(14, await db.StaffWorkingDays.CountAsync(Token)); await AssertOriginalStateAsync(app, seed);
        foreach (var business in new[] { true, false })
        {
            await InsertAsync(database.GetConnectionString(), business, member.Id, 3, 1, 720, 780);
            var overlap = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(database.GetConnectionString(), business, member.Id, 3, 2, 750, 800)); Assert.Equal("23P01", overlap.SqlState);
            foreach (var values in new[] { (3, 9, 780, 800), (7, 3, 780, 800), (3, 3, -1, 10), (3, 3, 780, 780) })
            { var invalid = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(database.GetConnectionString(), business, member.Id, values.Item1, values.Item2, values.Item3, values.Item4)); Assert.Equal("23514", invalid.SqlState); }
            await InsertAsync(database.GetConnectionString(), business, member.Id, 3, 2, 780, 800);
        }
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261014192252_AppointmentBuffers", Token)); Assert.Equal("P0001", refusal.SqlState); Assert.Equal(2, await db.BusinessWeeklyBreaks.CountAsync(Token)); Assert.Equal(2, await db.StaffWeeklyBreaks.CountAsync(Token));
    }
}
