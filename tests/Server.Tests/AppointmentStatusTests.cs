using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Scheduling;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class AppointmentStatusTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static string Path(Guid id, string action) => $"/api/appointments/{id}/{action}";
    private static async Task<AppointmentEndpoints.AppointmentResponse> Body(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
    }
    private static async Task<AppointmentEndpoints.AppointmentResponse> Book(HttpClient owner, AppDbContext db)
    {
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        using var response = await PostAsync(owner, "/api/appointments/", new AppointmentEndpoints.CreateRequest(Guid.NewGuid(), service.Id, service.Version, member.Id, "2026-10-15T10:00:00+03:00", "Sentetik müşteri", "05000000001"), await GetCsrfAsync(owner));
        return await Body(response, HttpStatusCode.Created);
    }

    [Fact]
    public async Task CancellationReleasesBuffersAndTwentyReplaysDoNotRepeatAuditOrVersion()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10; await db.SaveChangesAsync(Token);
        var original = await Book(owner, db); var request = new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid());
        var writes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(owner, Path(original.Id, "cancel"), request, csrf)));
        Guid? version = null;
        try { foreach (var write in writes) { var result = await Body(write); Assert.Equal("Cancelled", result.Status); version ??= result.Version; Assert.Equal(version, result.Version); Assert.Equal(original.Price, result.Price); Assert.Equal(original.OccupiedStartsAt, result.OccupiedStartsAt); Assert.Equal(original.OccupiedEndsAt, result.OccupiedEndsAt); } }
        finally { foreach (var write in writes) write.Dispose(); }
        Assert.NotEqual(original.Version, version); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token)); Assert.Single(await db.AppointmentStatusCommands.ToArrayAsync(Token));
        foreach (var changed in new[] { request with { Version = Guid.NewGuid() }, request with { RequestKey = Guid.NewGuid() } })
        { using var rejected = await PostAsync(owner, Path(original.Id, "cancel"), changed, csrf); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode); }
        using var changedAction = await PostAsync(owner, Path(original.Id, "complete"), request, csrf); Assert.Equal(HttpStatusCode.Conflict, changedAction.StatusCode);
        var replacement = await Book(owner, db); Assert.NotEqual(original.Id, replacement.Id);
        using var reusedForOther = await PostAsync(owner, Path(replacement.Id, "cancel"), request, csrf); Assert.Equal(HttpStatusCode.Conflict, reusedForOther.StatusCode);
        await AssertOriginalStateAsync(app, seed);
    }

    [Theory]
    [InlineData("complete", "Completed")]
    [InlineData("no-show", "NoShow")]
    public async Task HistoricalStatusRequiresStartAndCannotBeCancelledOrLoseSnapshot(string action, string status)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        var request = new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid());
        using var early = await PostAsync(owner, Path(original.Id, action), request, csrf); Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Empty(await db.AppointmentStatusCommands.ToArrayAsync(Token));
        clock.Now = new DateTimeOffset(original.StartsAt);
        using var cancelStarted = await PostAsync(owner, Path(original.Id, "cancel"), request, csrf); Assert.Equal(HttpStatusCode.Conflict, cancelStarted.StatusCode);
        using var changed = await PostAsync(owner, Path(original.Id, action), request, csrf); var saved = await Body(changed); Assert.Equal(status, saved.Status); Assert.NotEqual(original.Version, saved.Version);
        using var forbidden = await PostAsync(owner, Path(original.Id, "cancel"), new AppointmentStatusEndpoints.Request(saved.Version, Guid.NewGuid()), csrf); Assert.Equal(HttpStatusCode.Conflict, forbidden.StatusCode);
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.Price += 1; service.DurationMinutes += 5; await db.SaveChangesAsync(Token);
        using var replay = await PostAsync(owner, Path(original.Id, action), request, csrf); var repeated = await Body(replay); Assert.Equal(saved, repeated);
        Assert.Equal(status, (await db.AppointmentAudits.AsNoTracking().SingleAsync(item => item.Kind != "Created", Token)).Kind);
        clock.Now = original.StartsAt.AddHours(-1);
        using var availability = await owner.GetAsync($"/api/appointments/availability?serviceId={original.ServiceId}&staffMemberId={original.StaffMemberId}&date=2026-10-15", Token);
        var slots = Assert.IsType<AppointmentEndpoints.AvailabilityResponse>(await availability.Content.ReadFromJsonAsync<AppointmentEndpoints.AvailabilityResponse>(Token)); Assert.DoesNotContain(original.StartsAt, slots.StartsAt);
        await AssertOriginalStateAsync(app, seed);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("complete")]
    [InlineData("no-show")]
    public async Task StatusWritesPreserveAuthorityAndRollbackOnAuditFailure(string action)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        if (action != "cancel") clock.Now = new DateTimeOffset(original.StartsAt);
        var request = new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()); var path = Path(original.Id, action);
        using var anonymous = app.CreateClient(); using var unauthorized = await PostAsync(anonymous, path, request, await GetCsrfAsync(anonymous)); Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        using var denied = await PostAsync(staff, path, request, await GetCsrfAsync(staff)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var noCsrf = await PostAsync(owner, path, request, null); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        foreach (var invalid in new[] { request with { Version = Guid.Empty }, request with { RequestKey = Guid.Empty } })
        { using var bad = await PostAsync(owner, path, invalid, csrf); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); }
        using var missing = await PostAsync(owner, Path(Guid.NewGuid(), action), request, csrf); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var stale = await PostAsync(owner, path, request with { Version = Guid.NewGuid() }, csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_status_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic audit failure'; END $$; CREATE TRIGGER reject_status BEFORE INSERT ON \"AppointmentAudits\" FOR EACH ROW EXECUTE FUNCTION reject_status_audit()", Token);
        using var failed = await PostAsync(owner, path, request, csrf); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        using var retained = await owner.GetAsync($"/api/appointments/{original.Id}", Token); Assert.Equal(original, await Body(retained));
        Assert.Empty(await db.AppointmentStatusCommands.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_status ON \"AppointmentAudits\"", Token);
        var user = await db.Users.SingleAsync(item => item.Id == seed.OwnerId, Token); user.SecurityStamp = Guid.NewGuid().ToString(); await db.SaveChangesAsync(Token);
        using var revoked = await PostAsync(owner, path, request, csrf); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TwoOwnersCannotApplyDifferentStatusesToTheSameVersion(bool completeFirst)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); using var other = await AppointmentTests.OtherOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); var otherCsrf = await GetCsrfAsync(other);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db); clock.Now = new DateTimeOffset(original.StartsAt);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"StaffMembers\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction)) { command.Parameters.AddWithValue("id", original.StaffMemberId); await command.ExecuteScalarAsync(Token); }
        var first = PostAsync(owner, Path(original.Id, completeFirst ? "complete" : "no-show"), new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()), csrf);
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) < 1) await Task.Delay(20, wait.Token);
        var second = PostAsync(other, Path(original.Id, completeFirst ? "no-show" : "complete"), new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()), otherCsrf);
        await AwaitInviteLocksAsync(app); await transaction.CommitAsync(Token);
        using var saved = await first; Assert.Equal(completeFirst ? "Completed" : "NoShow", (await Body(saved)).Status);
        using var rejected = await second; Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Single(await db.AppointmentStatusCommands.ToArrayAsync(Token)); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
    }

    [Fact]
    public async Task AdditiveMigrationPreservesExistingRecordsAndRefusesDownAfterStatusHistory()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261014212842_DatedCalendar", Token); await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        using var read = await owner.GetAsync($"/api/appointments/{original.Id}", Token); Assert.Equal(original, await Body(read));
        Assert.Equal("Created", (await db.AppointmentAudits.AsNoTracking().SingleAsync(Token)).Kind); Assert.False(db.Database.HasPendingModelChanges());
        using var cancelled = await PostAsync(owner, Path(original.Id, "cancel"), new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()), await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261014212842_DatedCalendar", Token)); Assert.Equal("P0001", refusal.SqlState);
        Assert.Single(await db.AppointmentStatusCommands.ToArrayAsync(Token)); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token)); await AssertOriginalStateAsync(app, seed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationAndReplacementSerializeWithoutTwoOccupiedAppointments(bool cancelFirst)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); using var other = await AppointmentTests.OtherOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); var otherCsrf = await GetCsrfAsync(other);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        var create = new AppointmentEndpoints.CreateRequest(Guid.NewGuid(), original.ServiceId, (await db.ServiceDefinitions.SingleAsync(Token)).Version, original.StaffMemberId, "2026-10-15T10:00:00+03:00", "Sentetik müşteri", "05000000001");
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"StaffMembers\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction)) { command.Parameters.AddWithValue("id", original.StaffMemberId); await command.ExecuteScalarAsync(Token); }
        Task<HttpResponseMessage> Cancel() => PostAsync(owner, Path(original.Id, "cancel"), new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()), csrf);
        Task<HttpResponseMessage> Replace() => PostAsync(other, "/api/appointments/", create, otherCsrf);
        var first = cancelFirst ? Cancel() : Replace(); using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) < 1) await Task.Delay(20, wait.Token);
        var second = cancelFirst ? Replace() : Cancel(); await AwaitInviteLocksAsync(app); await transaction.CommitAsync(Token);
        using var firstResult = await first; using var secondResult = await second;
        Assert.Equal(cancelFirst ? HttpStatusCode.OK : HttpStatusCode.Conflict, firstResult.StatusCode); Assert.Equal(cancelFirst ? HttpStatusCode.Created : HttpStatusCode.OK, secondResult.StatusCode);
        Assert.Equal(cancelFirst ? 1 : 0, await db.AppointmentTimeBlocks.CountAsync(item => item.Status != "Cancelled", Token));
        Assert.Single(await db.AppointmentStatusCommands.ToArrayAsync(Token));
    }

    [Fact]
    public async Task CancelTimeoutAndRateLimitCannotPartiallyChangeTheAppointment()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        var request = new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid());
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"StaffMembers\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction)) { command.Parameters.AddWithValue("id", original.StaffMemberId); await command.ExecuteScalarAsync(Token); }
        using var timeout = await PostAsync(owner, Path(original.Id, "cancel"), request, csrf).WaitAsync(TimeSpan.FromSeconds(25), Token); Assert.Equal(HttpStatusCode.InternalServerError, timeout.StatusCode);
        await transaction.CommitAsync(Token); Assert.Empty(await db.AppointmentStatusCommands.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
        Assert.Equal(original.Version, (await db.Appointments.AsNoTracking().SingleAsync(Token)).Version); Assert.Equal("Confirmed", (await db.AppointmentTimeBlocks.AsNoTracking().SingleAsync(Token)).Status);
        using var retry = await PostAsync(owner, Path(original.Id, "cancel"), request, csrf); var saved = await Body(retry);
        // Oluşturma, timeout ve tekrar üç izin tüketir; son 57 istek kalan kotayı doldurur.
        for (var attempt = 0; attempt < 57; attempt++) { using var invalid = await PostAsync(owner, Path(original.Id, "cancel"), request with { Version = Guid.Empty }, csrf); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using var limited = await PostAsync(owner, Path(original.Id, "cancel"), request, csrf); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(saved.Version, (await db.Appointments.AsNoTracking().SingleAsync(Token)).Version); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token)); Assert.Single(await db.AppointmentStatusCommands.ToArrayAsync(Token));
    }
}
