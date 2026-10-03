using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Business;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class BusinessHoursTests
{
    private const string Path = "/api/business-hours/";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static BusinessHoursEndpoints.DayRequest[] Week(string start = "09:00", string end = "19:00") =>
        Enumerable.Range(0, 7).Select(day => new BusinessHoursEndpoints.DayRequest(day, day == 6, day == 6 ? null : start, day == 6 ? null : end)).ToArray();
    private static Task<HttpResponseMessage> Save(HttpClient client, Guid version, BusinessHoursEndpoints.DayRequest[]? days, string? csrf) => PostAsync(client, Path, new { version, days }, csrf);
    private static async Task<BusinessHoursEndpoints.ScheduleResponse> Schedule(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<BusinessHoursEndpoints.ScheduleResponse>(await response.Content.ReadFromJsonAsync<BusinessHoursEndpoints.ScheduleResponse>(Token));
    }
    private static async Task<BusinessHoursEndpoints.ScheduleResponse> Read(HttpClient client)
    {
        using var response = await client.GetAsync(Path, Token); return await Schedule(response);
    }
    [Fact]
    public async Task RoutesRequireMfaOwnerAndValidCsrfWithoutTouchingAccounts()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner);
        using var anonymous = app.CreateClient(); using var pending = app.CreateClient(); await PasswordStepAsync(pending);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        {
            using var read = await client.GetAsync(Path, Token); Assert.Equal(status, read.StatusCode);
            using var write = await Save(client, initial.Version, Week(), await GetCsrfAsync(client)); Assert.Equal(status, write.StatusCode);
        }
        foreach (var csrf in new string?[] { null, "invalid-csrf" })
        { using var response = await Save(owner, initial.Version, Week(), csrf); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        Assert.False(initial.IsConfigured); Assert.Empty(initial.Days); Assert.Equal("Europe/Istanbul", initial.TimeZone);
        await AssertOriginalStateAsync(app, seed);
    }
    [Fact]
    public async Task InvalidWeekAndHoursNeverWriteAndReturnFieldErrors()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner); var csrf = await GetCsrfAsync(owner);
        var invalid = new List<BusinessHoursEndpoints.DayRequest[]?> { null, Array.Empty<BusinessHoursEndpoints.DayRequest>(), Week()[..6], Week().Append(Week()[0]).ToArray() };
        foreach (var day in new[] { -1, 7, 1 }) { var days = Week(); days[0] = days[0] with { Day = day }; invalid.Add(days); }
        foreach (var (start, end) in new (string?, string?)[] { (null, "19:00"), ("09:00", null), ("9:00", "19:00"), ("24:00", "19:00"), ("09:60", "19:00"), ("09:00:00", "19:00"), ("09:00", "09:00"), ("19:00", "09:00"), (" 09:00", "19:00") })
        { var days = Week(); days[0] = days[0] with { OpensAt = start, ClosesAt = end }; invalid.Add(days); }
        var missingFlag = Week(); missingFlag[0] = missingFlag[0] with { IsClosed = null }; invalid.Add(missingFlag);
        var closedHours = Week(); closedHours[6] = closedHours[6] with { OpensAt = "09:00" }; invalid.Add(closedHours);
        var nullDay = Week(); nullDay[0] = null!; invalid.Add(nullDay);
        foreach (var days in invalid)
        { using var result = await Save(owner, initial.Version, days, csrf); Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); Assert.Contains("\"errors\"", await result.Content.ReadAsStringAsync(Token)); }
        using var missingVersion = await Save(owner, Guid.Empty, Week(), csrf); Assert.Equal(HttpStatusCode.BadRequest, missingVersion.StatusCode);
        Assert.Equal(initial.Version, (await Read(owner)).Version);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.BusinessOpeningDays.ToArrayAsync(Token)); Assert.Empty(await db.BusinessHoursAudits.ToArrayAsync(Token));
    }
    [Fact]
    public async Task CompleteWeekNoOpAndClosurePreserveProfileDefinitionsAndIdentity()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.BusinessProfiles.AsNoTracking().SingleAsync(Token);
        using var write = await Save(owner, initial.Version, Week("00:00", "23:59").Reverse().ToArray(), csrf); var saved = await Schedule(write);
        Assert.True(saved.IsConfigured); Assert.NotEqual(initial.Version, saved.Version); Assert.Equal(Enumerable.Range(0, 7), saved.Days.Select(item => item.Day));
        Assert.Equal("00:00", saved.Days[0].OpensAt); Assert.Equal("23:59", saved.Days[0].ClosesAt); Assert.Null(saved.Days[6].OpensAt);
        using var noOp = await Save(owner, saved.Version, Week("00:00", "23:59"), csrf); Assert.Equal(saved.Version, (await Schedule(noOp)).Version);
        Assert.Single(await db.BusinessHoursAudits.ToArrayAsync(Token));
        using var close = await Save(owner, saved.Version, Enumerable.Range(0, 7).Select(day => new BusinessHoursEndpoints.DayRequest(day, true, null, null)).ToArray(), csrf); var closed = await Schedule(close);
        Assert.All(closed.Days, day => { Assert.True(day.IsClosed); Assert.Null(day.OpensAt); Assert.Null(day.ClosesAt); });
        var audits = await db.BusinessHoursAudits.AsNoTracking().ToArrayAsync(Token); Assert.Equal(2, audits.Length);
        Assert.All(audits, item => { Assert.Equal(seed.OwnerId, item.ActorId); Assert.Equal(TimeSpan.Zero, item.OccurredAt.Offset); });
        var retained = await db.BusinessProfiles.AsNoTracking().SingleAsync(Token); Assert.Equal(profile.Version, retained.Version); Assert.Equal(profile.Name, retained.Name);
        Assert.Empty(await db.StaffMembers.ToArrayAsync(Token)); Assert.Empty(await db.ServiceDefinitions.ToArrayAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }
    [Fact]
    public async Task ParallelWeeksAndStaleNoOpsCannotOverwriteEachOther()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var first = await InviteOwnerAsync(seed); using var second = await InviteOwnerAsync(seed);
        var initial = await Read(first); var csrf1 = await GetCsrfAsync(first); var csrf2 = await GetCsrfAsync(second);
        var responses = await Task.WhenAll(Save(first, initial.Version, Week("08:00", "18:00"), csrf1), Save(second, initial.Version, Week("10:00", "20:00"), csrf2));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
        var current = await Read(first); Assert.Equal(7, current.Days.Length); Assert.All(current.Days.Take(6), day => Assert.Equal(current.Days[0].OpensAt, day.OpensAt));
        using var stale = await Save(first, initial.Version, current.Days.Select(day => new BusinessHoursEndpoints.DayRequest(day.Day, day.IsClosed, day.OpensAt, day.ClosesAt)).ToArray(), csrf1);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var scope = app.Services.CreateScope(); Assert.Single(await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessHoursAudits.ToArrayAsync(Token));
    }
    [Fact]
    public async Task ImmediateAndDeferredAuditFailuresRollBackAllDaysAndVersion()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_hours_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic hours audit failure'; END; $$;
            CREATE TRIGGER reject_hours_audit BEFORE INSERT ON "BusinessHoursAudits" FOR EACH ROW EXECUTE FUNCTION reject_hours_audit();
            """, Token);
        foreach (var deferred in new[] { false, true })
        {
            if (deferred) await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_hours_audit ON "BusinessHoursAudits";
                CREATE CONSTRAINT TRIGGER reject_hours_audit AFTER INSERT ON "BusinessHoursAudits" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_hours_audit();
                """, Token);
            using var result = await Save(owner, initial.Version, Week(), csrf); Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
            var unchanged = await Read(owner); Assert.False(unchanged.IsConfigured); Assert.Equal(initial.Version, unchanged.Version); Assert.Empty(unchanged.Days);
            Assert.Empty(await db.BusinessHoursAudits.ToArrayAsync(Token));
        }
    }
    [Fact]
    public async Task WaitingWriteRechecksRevokedOwnerSession()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var locked = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {seed.OwnerId} FOR UPDATE").SingleAsync(Token);
        var waiting = Save(owner, initial.Version, Week(), csrf);
        await using var observer = new NpgsqlConnection(database.GetConnectionString()); await observer.OpenAsync(Token);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10); var blocked = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var query = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%AspNetUsers%FOR UPDATE%')", observer);
            blocked = (bool)(await query.ExecuteScalarAsync(Token) ?? false); if (blocked) break; await Task.Delay(25, Token);
        }
        Assert.True(blocked); Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().UpdateSecurityStampAsync(locked)).Succeeded);
        await transaction.CommitAsync(Token); using var result = await waiting; Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Empty(await db.BusinessOpeningDays.ToArrayAsync(Token)); Assert.Empty(await db.BusinessHoursAudits.ToArrayAsync(Token));
    }
    [Fact]
    public async Task DatabaseRejectsInvalidHoursAndMigrationRoundTripPreservesExistingData()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner);
        using var result = await Save(owner, initial.Version, Week(), await GetCsrfAsync(owner)); await Schedule(result);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var query in new[] {
            "UPDATE \"BusinessOpeningDays\" SET \"ClosesAtMinute\" = \"OpensAtMinute\" WHERE \"Day\" = 0",
            "UPDATE \"BusinessOpeningDays\" SET \"OpensAtMinute\" = NULL WHERE \"Day\" = 0",
            "UPDATE \"BusinessOpeningDays\" SET \"ClosesAtMinute\" = 1440 WHERE \"Day\" = 0",
            "UPDATE \"BusinessOpeningDays\" SET \"OpensAtMinute\" = 1 WHERE \"Day\" = 6",
            "UPDATE \"BusinessOpeningDays\" SET \"Day\" = 7 WHERE \"Day\" = 0" })
        { var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(query, Token)); Assert.Equal("23514", failure.SqlState); }
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("INSERT INTO \"BusinessOpeningDays\" VALUES (1, 0, TRUE, NULL, NULL)", Token)); Assert.Equal("23505", duplicate.SqlState);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261003004248_StaffServiceAssignments", Token);
        await AssertOriginalStateAsync(app, seed); await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        Assert.False((await Read(owner)).IsConfigured); Assert.Empty(await db.BusinessOpeningDays.ToArrayAsync(Token)); Assert.Empty(await db.BusinessHoursAudits.ToArrayAsync(Token));
    }
}
