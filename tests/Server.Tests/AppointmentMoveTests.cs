using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Scheduling;
using Server.Features.Staff;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class AppointmentMoveTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static string Path(Guid id) => $"/api/appointments/{id}/move";
    private static string Preview(Guid id, Guid member) => $"/api/appointments/{id}/move-availability?staffMemberId={member}&date=2026-10-16";
    private static async Task<AppointmentEndpoints.AppointmentResponse> Body(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
    }
    private static async Task<AppointmentEndpoints.AppointmentResponse> Book(HttpClient owner, AppDbContext db, Guid? member = null, string start = "2026-10-16T10:00:00+03:00")
    {
        var service = await db.ServiceDefinitions.SingleAsync(Token);
        var memberId = member ?? await db.StaffMembers.OrderBy(item => item.Id).Select(item => item.Id).FirstAsync(Token);
        using var response = await PostAsync(owner, "/api/appointments/", new AppointmentEndpoints.CreateRequest(Guid.NewGuid(), service.Id, service.Version, memberId, start, "Sentetik müşteri", "05000000001"), await GetCsrfAsync(owner));
        return await Body(response, HttpStatusCode.Created);
    }

    [Fact]
    public async Task PreviewAndMoveKeepOriginalSnapshotsAndReplayReturnsCurrentState()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10; await db.SaveChangesAsync(Token);
        var original = await Book(owner, db); var creation = await db.Appointments.AsNoTracking().SingleAsync(Token);
        service.DurationMinutes = 120; service.Price = 999; service.BufferBeforeMinutes = 30; service.BufferAfterMinutes = 60; service.Version = Guid.NewGuid(); await db.SaveChangesAsync(Token);
        using var preview = await owner.GetAsync(Preview(original.Id, original.StaffMemberId), Token);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var slots = Assert.IsType<AppointmentMoveEndpoints.AvailabilityResponse>(await preview.Content.ReadFromJsonAsync<AppointmentMoveEndpoints.AvailabilityResponse>(Token));
        Assert.Equal(original.Version, slots.Version); Assert.Equal("Europe/Istanbul", slots.TimeZone); Assert.Contains(original.StartsAt, slots.StartsAt);
        Assert.Contains(new DateTime(2026, 10, 16, 15, 30, 0, DateTimeKind.Utc), slots.StartsAt);
        var target = await db.StaffMembers.SingleAsync(item => item.Id != original.StaffMemberId, Token); target.Name = "Yeni personel"; await db.SaveChangesAsync(Token);
        var request = new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), target.Id, "2026-10-16T18:30:00+03:00");
        var writes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(owner, Path(original.Id), request, csrf)));
        AppointmentEndpoints.AppointmentResponse? saved = null;
        try { foreach (var write in writes) { var result = await Body(write); saved ??= result; Assert.Equal(saved, result); } }
        finally { foreach (var write in writes) write.Dispose(); }
        Assert.NotNull(saved); Assert.NotEqual(original.Version, saved.Version);
        Assert.Equal(original with { Version = saved.Version, StaffMemberId = target.Id, StaffName = target.Name, StartsAt = saved.StartsAt, EndsAt = saved.EndsAt, OccupiedStartsAt = saved.OccupiedStartsAt, OccupiedEndsAt = saved.OccupiedEndsAt }, saved);
        Assert.Equal(TimeSpan.FromMinutes(20), saved.EndsAt - saved.StartsAt); Assert.Equal(saved.StartsAt.AddMinutes(-5), saved.OccupiedStartsAt); Assert.Equal(saved.EndsAt.AddMinutes(10), saved.OccupiedEndsAt);
        Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token)); Assert.Single(await db.AppointmentMoveCommands.ToArrayAsync(Token));
        foreach (var changed in new[] { request with { Version = saved.Version }, request with { StaffMemberId = original.StaffMemberId }, request with { StartsAt = "2026-10-16T17:00:00+03:00" } })
        { using var rejected = await PostAsync(owner, Path(original.Id), changed, csrf); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode); }
        var noOp = new AppointmentMoveEndpoints.Request(saved.Version, Guid.NewGuid(), saved.StaffMemberId, request.StartsAt);
        using var unchanged = await PostAsync(owner, Path(original.Id), noOp, csrf); Assert.Equal(saved, await Body(unchanged));
        Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token)); Assert.Equal(2, await db.AppointmentMoveCommands.CountAsync(Token));
        using var reusedNoOp = await PostAsync(owner, Path(original.Id), noOp with { StartsAt = "2026-10-16T17:00:00+03:00" }, csrf); Assert.Equal(HttpStatusCode.Conflict, reusedNoOp.StatusCode);
        using var cancel = await PostAsync(owner, $"/api/appointments/{original.Id}/cancel", new AppointmentStatusEndpoints.Request(saved.Version, Guid.NewGuid()), csrf); var cancelled = await Body(cancel);
        using var replay = await PostAsync(owner, Path(original.Id), request, csrf); Assert.Equal(cancelled, await Body(replay));
        var after = await db.Appointments.AsNoTracking().SingleAsync(Token); Assert.Equal(creation.RequestKey, after.RequestKey); Assert.Equal(creation.RequestHash, after.RequestHash); Assert.Equal(creation.CreatedAt, after.CreatedAt);
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task OccupiedTargetRollsBackEverythingAndAdjacentBufferBoundarySucceeds()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.DurationMinutes = 30; service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10; await db.SaveChangesAsync(Token);
        var original = await Book(owner, db); var target = await db.StaffMembers.Where(item => item.Id != original.StaffMemberId).Select(item => item.Id).SingleAsync(Token);
        await Book(owner, db, target, "2026-10-16T11:00:00+03:00");
        var request = new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), target, "2026-10-16T11:30:00+03:00");
        using var conflict = await PostAsync(owner, Path(original.Id), request, csrf); Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var retained = await owner.GetAsync($"/api/appointments/{original.Id}", Token); Assert.Equal(original, await Body(retained));
        Assert.Empty(await db.AppointmentMoveCommands.ToArrayAsync(Token)); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
        using var preview = await owner.GetAsync(Preview(original.Id, target), Token); var slots = Assert.IsType<AppointmentMoveEndpoints.AvailabilityResponse>(await preview.Content.ReadFromJsonAsync<AppointmentMoveEndpoints.AvailabilityResponse>(Token));
        Assert.DoesNotContain(new DateTime(2026, 10, 16, 8, 30, 0, DateTimeKind.Utc), slots.StartsAt);
        // Arka tamponun 10:55+03 bitişi, komşunun ön tampon başlangıcına tam değer.
        using var moved = await PostAsync(owner, Path(original.Id), request with { StartsAt = "2026-10-16T10:15:00+03:00" }, csrf); var saved = await Body(moved);
        Assert.Equal(new DateTime(2026, 10, 16, 7, 55, 0, DateTimeKind.Utc), saved.OccupiedEndsAt);
        using var reuseOther = await PostAsync(owner, Path(Guid.NewGuid()), request, csrf); Assert.Equal(HttpStatusCode.Conflict, reuseOther.StatusCode);
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task AuthorityValidationAuditFailureAndRevocationCannotPartiallyMove()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        var request = new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), original.StaffMemberId, "2026-10-16T11:00:00+03:00");
        using var anonymous = app.CreateClient(); using var unauthorized = await PostAsync(anonymous, Path(original.Id), request, await GetCsrfAsync(anonymous)); Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var anonymousPreview = await anonymous.GetAsync(Preview(original.Id, original.StaffMemberId), Token); Assert.Equal(HttpStatusCode.Unauthorized, anonymousPreview.StatusCode);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        using var denied = await PostAsync(staff, Path(original.Id), request, await GetCsrfAsync(staff)); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedPreview = await staff.GetAsync(Preview(original.Id, original.StaffMemberId), Token); Assert.Equal(HttpStatusCode.Forbidden, deniedPreview.StatusCode);
        using var noCsrf = await PostAsync(owner, Path(original.Id), request, null); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        foreach (var invalid in new[] { request with { Version = Guid.Empty }, request with { RequestKey = Guid.Empty }, request with { StaffMemberId = Guid.Empty }, request with { StartsAt = "2026-10-16T11:00:00" }, request with { StartsAt = "2026-10-16T11:00:01+03:00" } })
        { using var bad = await PostAsync(owner, Path(original.Id), invalid, csrf); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); }
        using var missing = await PostAsync(owner, Path(Guid.NewGuid()), request, csrf); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var stale = await PostAsync(owner, Path(original.Id), request with { Version = Guid.NewGuid() }, csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_move_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic audit failure'; END $$; CREATE TRIGGER reject_move BEFORE INSERT ON \"AppointmentAudits\" FOR EACH ROW EXECUTE FUNCTION reject_move_audit()", Token);
        using var failed = await PostAsync(owner, Path(original.Id), request, csrf); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        using var retained = await owner.GetAsync($"/api/appointments/{original.Id}", Token); Assert.Equal(original, await Body(retained));
        Assert.Empty(await db.AppointmentMoveCommands.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_move ON \"AppointmentAudits\"", Token);
        var user = await db.Users.SingleAsync(item => item.Id == seed.OwnerId, Token); user.SecurityStamp = Guid.NewGuid().ToString(); await db.SaveChangesAsync(Token);
        using var revoked = await PostAsync(owner, Path(original.Id), request, csrf); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Theory]
    [InlineData("lead")]
    [InlineData("horizon")]
    [InlineData("step")]
    [InlineData("started")]
    [InlineData("cancelled")]
    [InlineData("inactive")]
    [InlineData("skill")]
    [InlineData("service")]
    [InlineData("hours")]
    [InlineData("leave")]
    public async Task CurrentEligibilityIsRecheckedBeforeMoving(string condition)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db, start: condition == "started" ? "2026-10-15T10:00:00+03:00" : "2026-10-16T10:00:00+03:00");
        var member = await db.StaffMembers.SingleAsync(item => item.Id != original.StaffMemberId, Token);
        var request = new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), member.Id, "2026-10-16T11:00:00+03:00");
        switch (condition)
        {
            case "lead": request = request with { StartsAt = "2026-10-15T09:45:00+03:00" }; break;
            case "horizon": request = request with { StartsAt = "2026-11-15T11:00:00+03:00" }; break;
            case "step": request = request with { StartsAt = "2026-10-16T11:01:00+03:00" }; break;
            case "started": clock.Now = original.StartsAt; break;
            case "cancelled": using (var cancel = await PostAsync(owner, $"/api/appointments/{original.Id}/cancel", new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()), csrf)) original = await Body(cancel); request = request with { Version = original.Version }; break;
            case "inactive": member.IsActive = false; break;
            case "skill": db.StaffServiceAssignments.Remove(await db.StaffServiceAssignments.SingleAsync(item => item.StaffMemberId == member.Id, Token)); break;
            case "service": (await db.ServiceDefinitions.SingleAsync(Token)).IsActive = false; break;
            case "hours": var hours = await db.StaffWorkingDays.SingleAsync(item => item.StaffMemberId == member.Id && item.Day == 4, Token); hours.IsClosed = true; hours.OpensAtMinute = null; hours.ClosesAtMinute = null; break;
            case "leave": db.StaffLeaves.Add(new() { Id = Guid.NewGuid(), StaffMemberId = member.Id, StartsAt = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc), EndsAt = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc), RequestHash = new string('A', 64) }); break;
        }
        await db.SaveChangesAsync(Token);
        using var rejected = await PostAsync(owner, Path(original.Id), request, csrf); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var retained = await owner.GetAsync($"/api/appointments/{original.Id}", Token); Assert.Equal(original, await Body(retained)); Assert.Empty(await db.AppointmentMoveCommands.ToArrayAsync(Token));
    }

    [Fact]
    public async Task OppositeStaffMovesAndCompetingVersionsUseConsistentLocks()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); using var other = await AppointmentTests.OtherOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); var otherCsrf = await GetCsrfAsync(other);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        var target = await db.StaffMembers.Where(item => item.Id != original.StaffMemberId).Select(item => item.Id).SingleAsync(Token);
        var second = await Book(owner, db, target);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"StaffMembers\" ORDER BY \"Id\" FOR UPDATE", connection, transaction)) await command.ExecuteNonQueryAsync(Token);
        var firstMove = PostAsync(owner, Path(original.Id), new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), target, "2026-10-16T11:00:00+03:00"), csrf);
        var secondMove = PostAsync(other, Path(second.Id), new AppointmentMoveEndpoints.Request(second.Version, Guid.NewGuid(), original.StaffMemberId, "2026-10-16T11:00:00+03:00"), otherCsrf);
        await AwaitInviteLocksAsync(app); await transaction.CommitAsync(Token);
        using var firstResult = await firstMove; using var secondResult = await secondMove; var saved = await Body(firstResult); await Body(secondResult);
        var competing = await Task.WhenAll(PostAsync(owner, Path(saved.Id), new AppointmentMoveEndpoints.Request(saved.Version, Guid.NewGuid(), saved.StaffMemberId, "2026-10-16T12:00:00+03:00"), csrf), PostAsync(other, Path(saved.Id), new AppointmentMoveEndpoints.Request(saved.Version, Guid.NewGuid(), original.StaffMemberId, "2026-10-16T12:00:00+03:00"), otherCsrf));
        try { Assert.Single(competing, result => result.StatusCode == HttpStatusCode.OK); Assert.Single(competing, result => result.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var result in competing) result.Dispose(); }
        Assert.Equal(3, await db.AppointmentMoveCommands.CountAsync(Token)); Assert.Equal(5, await db.AppointmentAudits.CountAsync(Token));
    }

    [Fact]
    public async Task AdditiveMigrationPreservesStatusHistoryAndRefusesDownEvenAfterNoOp()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        var other = await Book(owner, db, start: "2026-10-16T12:00:00+03:00");
        using var cancel = await PostAsync(owner, $"/api/appointments/{other.Id}/cancel", new AppointmentStatusEndpoints.Request(other.Version, Guid.NewGuid()), csrf); await Body(cancel);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261014230241_AppointmentStatus", Token); await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        using var read = await owner.GetAsync($"/api/appointments/{original.Id}", Token); Assert.Equal(original, await Body(read)); Assert.Single(await db.AppointmentStatusCommands.ToArrayAsync(Token));
        Assert.Equal(3, await db.AppointmentAudits.CountAsync(Token)); Assert.False(db.Database.HasPendingModelChanges());
        using var noOp = await PostAsync(owner, Path(original.Id), new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), original.StaffMemberId, "2026-10-16T10:00:00+03:00"), csrf); Assert.Equal(original, await Body(noOp));
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261014230241_AppointmentStatus", Token)); Assert.Equal("P0001", refusal.SqlState);
        foreach (var sql in new[] { "UPDATE \"AppointmentMoveCommands\" SET \"ExpectedVersion\"='00000000-0000-0000-0000-000000000000'", "UPDATE \"AppointmentMoveCommands\" SET \"StartsAt\"='infinity'", "UPDATE \"AppointmentMoveCommands\" SET \"StartsAt\"=\"StartsAt\"+interval '1 second'" })
        { var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("23514", invalid.SqlState); }
        Assert.Single(await db.AppointmentMoveCommands.ToArrayAsync(Token)); Assert.Equal(3, await db.AppointmentAudits.CountAsync(Token)); await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task TimeoutAndRateLimitPreserveOriginalAndRetryRemainsSafe()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        var request = new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), original.StaffMemberId, "2026-10-16T11:00:00+03:00");
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"StaffMembers\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction)) { command.Parameters.AddWithValue("id", original.StaffMemberId); await command.ExecuteScalarAsync(Token); }
        using var timeout = await PostAsync(owner, Path(original.Id), request, csrf).WaitAsync(TimeSpan.FromSeconds(25), Token); Assert.Equal(HttpStatusCode.InternalServerError, timeout.StatusCode);
        await transaction.CommitAsync(Token); Assert.Empty(await db.AppointmentMoveCommands.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token)); Assert.Equal(original.Version, (await db.Appointments.AsNoTracking().SingleAsync(Token)).Version);
        using var retry = await PostAsync(owner, Path(original.Id), request, csrf); var saved = await Body(retry);
        for (var attempt = 0; attempt < 57; attempt++) { using var invalid = await PostAsync(owner, Path(original.Id), request with { Version = Guid.Empty }, csrf); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using var limited = await PostAsync(owner, Path(original.Id), request, csrf); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(saved.Version, (await db.Appointments.AsNoTracking().SingleAsync(Token)).Version); Assert.Single(await db.AppointmentMoveCommands.ToArrayAsync(Token)); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
    }

    [Fact]
    public async Task TwentyIndependentDatabaseMovesHaveOneWinnerAndRetainNineteenOldRanges()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var originals = new List<AppointmentEndpoints.AppointmentResponse>();
        for (var day = 16; day <= 35; day++) originals.Add(await Book(owner, db, start: new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(3)).AddDays(day - 16).ToString("O")));
        var target = await db.StaffMembers.Where(item => item.Id != originals[0].StaffMemberId).Select(item => item.Id).SingleAsync(Token);
        async Task<bool> Move(AppointmentEndpoints.AppointmentResponse original)
        {
            await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
            await using var command = new NpgsqlCommand("UPDATE \"AppointmentTimeBlocks\" SET \"StaffMemberId\"=@member, \"StartsAt\"='2026-10-16T09:00Z', \"EndsAt\"='2026-10-16T09:20Z', \"OccupiedStartsAt\"='2026-10-16T09:00Z', \"OccupiedEndsAt\"='2026-10-16T09:20Z' WHERE \"AppointmentId\"=@id", connection, transaction);
            command.Parameters.AddWithValue("member", target); command.Parameters.AddWithValue("id", original.Id);
            try { await command.ExecuteNonQueryAsync(Token); await transaction.CommitAsync(Token); return true; }
            catch (PostgresException error) when (error.SqlState == "23P01" && error.ConstraintName == "EX_AppointmentTimeBlocks_StaffTime") { return false; }
        }
        var results = await Task.WhenAll(originals.Select(Move)); Assert.Single(results, result => result);
        foreach (var original in originals.Where((_, index) => !results[index])) { var block = await db.AppointmentTimeBlocks.AsNoTracking().SingleAsync(item => item.AppointmentId == original.Id, Token); Assert.Equal(original.StartsAt, block.StartsAt); Assert.Equal(original.StaffMemberId, block.StaffMemberId); }
        Assert.Equal(20, await db.AppointmentAudits.CountAsync(Token)); Assert.Empty(await db.AppointmentMoveCommands.ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MoveAndTargetLeaveSerializeInBothOrders(bool moveFirst)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); using var other = await AppointmentTests.OtherOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); var otherCsrf = await GetCsrfAsync(other);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db); var target = await db.StaffMembers.SingleAsync(item => item.Id != original.StaffMemberId, Token);
        Task<HttpResponseMessage> Move() => PostAsync(owner, Path(original.Id), new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), target.Id, "2026-10-16T11:00:00+03:00"), csrf);
        Task<HttpResponseMessage> Leave() => PostAsync(other, $"/api/staff-members/{target.Id}/leave", new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), target.Version, "2026-10-16T11:00:00+03:00", "2026-10-16T12:00:00+03:00"), otherCsrf);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"StaffMembers\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction)) { command.Parameters.AddWithValue("id", target.Id); await command.ExecuteScalarAsync(Token); }
        var first = moveFirst ? Move() : Leave(); using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) < 1) await Task.Delay(20, wait.Token);
        var second = moveFirst ? Leave() : Move(); await AwaitInviteLocksAsync(app); await transaction.CommitAsync(Token);
        using var firstResult = await first; using var secondResult = await second;
        Assert.Equal(HttpStatusCode.OK, firstResult.StatusCode); Assert.Equal(HttpStatusCode.Conflict, secondResult.StatusCode);
        Assert.Equal(moveFirst ? 1 : 0, await db.AppointmentMoveCommands.CountAsync(Token)); Assert.Equal(moveFirst ? 0 : 1, await db.StaffLeaves.CountAsync(Token));
        if (!moveFirst) { using var retained = await owner.GetAsync($"/api/appointments/{original.Id}", Token); Assert.Equal(original, await Body(retained)); }
    }

    [Fact]
    public async Task TwoApplicationInstancesCannotMoveDifferentAppointmentsIntoOneTarget()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        await using var otherApp = app.WithWebHostBuilder(_ => { }); using var other = await InviteOwnerAsync(seed with { App = otherApp }); var otherCsrf = await GetCsrfAsync(other);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db); var second = await Book(owner, db, start: "2026-10-16T12:00:00+03:00");
        var target = await db.StaffMembers.Where(item => item.Id != original.StaffMemberId).Select(item => item.Id).SingleAsync(Token);
        var source = await db.StaffMembers.SingleAsync(item => item.Id == original.StaffMemberId, Token); source.IsActive = false; source.IsDeleted = true; await db.SaveChangesAsync(Token);
        var request = new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), target, "2026-10-16T11:00:00+03:00");
        var writes = await Task.WhenAll(PostAsync(owner, Path(original.Id), request, csrf), PostAsync(other, Path(second.Id), request with { Version = second.Version, RequestKey = Guid.NewGuid() }, otherCsrf));
        try { Assert.Single(writes, result => result.StatusCode == HttpStatusCode.OK); Assert.Single(writes, result => result.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var write in writes) write.Dispose(); }
        Assert.Single(await db.AppointmentMoveCommands.ToArrayAsync(Token)); Assert.Equal(3, await db.AppointmentAudits.CountAsync(Token));
    }
}
