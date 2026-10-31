using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Audit;
using Server.Features.Scheduling;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class BookingPolicyTests
{
    private const string Path = "/api/booking-policy/";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
    }
    private static async Task<BookingPolicyEndpoints.Response> Body(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<BookingPolicyEndpoints.Response>(await response.Content.ReadFromJsonAsync<BookingPolicyEndpoints.Response>(Token));
    }
    private static async Task<BookingPolicyEndpoints.Response> Read(HttpClient client)
    {
        using var response = await client.GetAsync(Path, Token); return await Body(response);
    }
    private static async Task<AppointmentEndpoints.CreateRequest> Request(AppDbContext db, string start)
    {
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        return new(Guid.NewGuid(), service.Id, service.Version, member.Id, start, "Sentetik müşteri", "05000000001");
    }

    [Fact]
    public async Task DefaultsUpdateNoOpStaleAndAuditAreConsistent()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App; using var owner = await InviteOwnerAsync(seed);
        var before = await Read(owner); Assert.Equal((60, 30, 15, "Europe/Istanbul"), (before.LeadMinutes, before.HorizonDays, before.SlotMinutes, before.TimeZone));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var csrf = await GetCsrfAsync(owner);
        using var noOp = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(before.Version, 60, 30, 15, 120, true), csrf); Assert.Equal(before, await Body(noOp)); Assert.Empty(await db.BookingPolicyAudits.ToArrayAsync(Token));
        using var response = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(before.Version, 120, 7, 20, 120, true), csrf); var after = await Body(response); Assert.NotEqual(before.Version, after.Version); Assert.Equal((120, 7, 20), (after.LeadMinutes, after.HorizonDays, after.SlotMinutes));
        using var stale = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(before.Version, 120, 7, 20, 120, true), csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var noOpAfter = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(after.Version, 120, 7, 20, 120, true), csrf); Assert.Equal(after, await Body(noOpAfter));
        var audit = Assert.Single(await db.BookingPolicyAudits.AsNoTracking().ToArrayAsync(Token)); Assert.Equal(seed.OwnerId, audit.ActorId); Assert.Equal(before.Version, audit.PreviousVersion); Assert.Equal(after.Version, audit.PolicyVersion); Assert.Equal((120, 7, 20), (audit.LeadMinutes, audit.HorizonDays, audit.SlotMinutes)); Assert.Equal(new Clock().GetUtcNow(), audit.OccurredAt);
        using var definitions = await owner.GetAsync("/api/audit-log/?category=definitions&pageSize=1", Token); var page = Assert.IsType<AuditLogEndpoints.Page>(await definitions.Content.ReadFromJsonAsync<AuditLogEndpoints.Page>(Token)); var entry = Assert.Single(page.Items); Assert.StartsWith("14:", entry.Id); Assert.Equal("Rezervasyon ayarları", entry.Module); Assert.Equal("Güncellendi", entry.Action); Assert.Equal("Rezervasyon ayarları", entry.Target);
        using var replay = await owner.GetAsync("/api/audit-log/?category=definitions&pageSize=1&cursor=" + page.Cursor, Token); Assert.Equal(page.Items, Assert.IsType<AuditLogEndpoints.Page>(await replay.Content.ReadFromJsonAsync<AuditLogEndpoints.Page>(Token)).Items);
        using var security = await owner.GetAsync("/api/audit-log/?category=security", Token); Assert.Empty(Assert.IsType<AuditLogEndpoints.Page>(await security.Content.ReadFromJsonAsync<AuditLogEndpoints.Page>(Token)).Items);
        Assert.Equal(after, await Read(owner)); await AssertOriginalStateAsync(app, seed);
    }

    [Theory]
    [InlineData(null, 30, 15, "leadMinutes")]
    [InlineData(60, null, 15, "horizonDays")]
    [InlineData(60, 30, null, "slotMinutes")]
    [InlineData(-1, 30, 15, "leadMinutes")]
    [InlineData(43201, 365, 15, "leadMinutes")]
    [InlineData(60, 0, 15, "horizonDays")]
    [InlineData(60, 366, 15, "horizonDays")]
    [InlineData(60, int.MaxValue, 15, "horizonDays")]
    [InlineData(60, 30, 0, "slotMinutes")]
    [InlineData(60, 30, 61, "slotMinutes")]
    [InlineData(1440, 1, 15, "leadMinutes")]
    public async Task InvalidAndMissingValuesNeverWrite(int? lead, int? horizon, int? slot, string field)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App; using var owner = await InviteOwnerAsync(seed); var before = await Read(owner);
        using var response = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(before.Version, lead, horizon, slot, 120, true), await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains(field, await response.Content.ReadAsStringAsync(Token)); Assert.Equal(before, await Read(owner));
        using var scope = app.Services.CreateScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().BookingPolicyAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task PolicyCursorPaginatesEqualTimeSourceFourteenAndKeepsSecuritySeparate()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App; using var owner = await InviteOwnerAsync(seed); var current = await Read(owner); var csrf = await GetCsrfAsync(owner);
        foreach (var lead in new[] { 120, 180 }) { using var update = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(current.Version, lead, 30, 15, 120, true), csrf); current = await Body(update); }
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.StaffActivationAudits.Add(new() { Id = Guid.NewGuid(), ActorId = seed.OwnerId, StaffId = seed.OtherUserId, OccurredAt = new Clock().GetUtcNow() }); await db.SaveChangesAsync(Token);
        var ids = new List<string>(); string? cursor = null;
        do
        {
            using var response = await owner.GetAsync("/api/audit-log/?pageSize=1" + (cursor is null ? "" : "&cursor=" + cursor), Token); Assert.Equal(HttpStatusCode.OK, response.StatusCode); var page = Assert.IsType<AuditLogEndpoints.Page>(await response.Content.ReadFromJsonAsync<AuditLogEndpoints.Page>(Token)); ids.Add(Assert.Single(page.Items).Id); cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(3, ids.Distinct().Count()); Assert.Equal(new[] { "14", "14", "13" }, ids.Select(id => id.Split(':')[0]));
        using var security = await owner.GetAsync("/api/audit-log/?category=security", Token); Assert.StartsWith("13:", Assert.Single(Assert.IsType<AuditLogEndpoints.Page>(await security.Content.ReadFromJsonAsync<AuditLogEndpoints.Page>(Token)).Items).Id);
    }

    [Fact]
    public async Task MfaCsrfVersionAndRevokedSessionAreRequired()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App; using var owner = await InviteOwnerAsync(seed); var before = await Read(owner); var command = new BookingPolicyEndpoints.UpdateRequest(before.Version, 0, 1, 1, 120, true);
        using var anonymous = app.CreateClient(); using var pending = app.CreateClient(); await PasswordStepAsync(pending); await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        foreach (var client in new[] { anonymous, pending, staff })
        {
            using var read = await client.GetAsync(Path, Token); Assert.Equal(client == staff ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, read.StatusCode);
            using var post = await PostAsync(client, Path, command, await GetCsrfAsync(client)); Assert.Equal(read.StatusCode, post.StatusCode);
        }
        using var noCsrf = await PostAsync(owner, Path, command, null); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var noVersion = await PostAsync(owner, Path, command with { Version = Guid.Empty }, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Users.SingleAsync(item => item.Id == seed.OwnerId, Token)).SecurityStamp = Guid.NewGuid().ToString(); await db.SaveChangesAsync(Token);
        using var revoked = await PostAsync(owner, Path, command, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode); var saved = await db.BookingPolicies.AsNoTracking().SingleAsync(Token); Assert.Equal(before.Version, saved.Version); Assert.Empty(await db.BookingPolicyAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task TwentyConcurrentOwnersHaveOnePolicyWinnerAndAuditFailureRollsBack()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App; using var owner = await InviteOwnerAsync(seed); using var other = await AppointmentTests.OtherOwnerAsync(seed); var before = await Read(owner); var csrf = await GetCsrfAsync(owner); var otherCsrf = await GetCsrfAsync(other);
        var command = new BookingPolicyEndpoints.UpdateRequest(before.Version, 120, 7, 20, 120, true);
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(index => PostAsync(index % 2 == 0 ? owner : other, Path, command with { SlotMinutes = index + 1 }, index % 2 == 0 ? csrf : otherCsrf)));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK); Assert.Equal(19, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict)); foreach (var response in responses) response.Dispose(); var current = await Read(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Single(await db.BookingPolicyAudits.ToArrayAsync(Token));
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_policy_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic audit failure'; END $$; CREATE TRIGGER reject_policy BEFORE INSERT ON \"BookingPolicyAudits\" FOR EACH ROW EXECUTE FUNCTION reject_policy_audit()", Token);
        using var failed = await PostAsync(owner, Path, command with { Version = current.Version, LeadMinutes = 180 }, csrf); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); Assert.Equal(current, await Read(owner)); Assert.Single(await db.BookingPolicyAudits.ToArrayAsync(Token));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_policy ON \"BookingPolicyAudits\"", Token); using var retry = await PostAsync(owner, Path, command with { Version = current.Version, LeadMinutes = 180 }, csrf); var after = await Body(retry); Assert.Equal(180, after.LeadMinutes); Assert.Equal(2, await db.BookingPolicyAudits.CountAsync(Token));
    }

    [Fact]
    public async Task NewRulesApplyToAvailabilityCreationAndMoveWithoutChangingExistingAppointments()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App; await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var request = await Request(db, "2026-10-15T10:00:00+03:00");
        using var created = await PostAsync(owner, "/api/appointments/", request, csrf); Assert.Equal(HttpStatusCode.Created, created.StatusCode); var original = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await created.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        using var pending = await PostAsync(owner, "/api/appointments/pending", await Request(db, "2026-10-17T12:00:00+03:00"), csrf); Assert.Equal(HttpStatusCode.Created, pending.StatusCode); var held = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await pending.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        var policy = await Read(owner); using var update = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(policy.Version, 120, 1, 20, 120, true), csrf); await Body(update);
        foreach (var saved in new[] { original, held }) { using var read = await owner.GetAsync($"/api/appointments/{saved.Id}", Token); Assert.Equal(saved, await read.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token)); }
        using var replay = await PostAsync(owner, "/api/appointments/", request, csrf); Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.Equal(original, await replay.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        var query = $"?serviceId={request.ServiceId}&staffMemberId={request.StaffMemberId}&date=";
        using var availability = await owner.GetAsync("/api/appointments/availability" + query + "2026-10-15", Token); var slots = Assert.IsType<AppointmentEndpoints.AvailabilityResponse>(await availability.Content.ReadFromJsonAsync<AppointmentEndpoints.AvailabilityResponse>(Token)); Assert.Contains(new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc), slots.StartsAt); Assert.All(slots.StartsAt, start => { Assert.True(start >= new Clock().GetUtcNow().UtcDateTime.AddHours(2)); Assert.Equal(0, (start.Hour * 60 + start.Minute) % 20); });
        foreach (var start in new[] { "2026-10-15T10:40:00+03:00", "2026-10-15T11:10:00+03:00", "2026-10-17T14:00:00+03:00" })
        {
            foreach (var endpoint in new[] { "/api/appointments/", "/api/appointments/pending" }) { using var invalid = await PostAsync(owner, endpoint, await Request(db, start), csrf); Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode); }
            using var move = await PostAsync(owner, $"/api/appointments/{original.Id}/move", new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), original.StaffMemberId, start), csrf); Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
        }
        using var tooFar = await owner.GetAsync("/api/appointments/availability" + query + "2026-10-17", Token); Assert.Equal(HttpStatusCode.BadRequest, tooFar.StatusCode);
        using var confirmed = await PostAsync(owner, $"/api/appointments/{held.Id}/confirm", new AppointmentStatusEndpoints.Request(held.Version, Guid.NewGuid()), csrf); Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        using var valid = await PostAsync(owner, "/api/appointments/", await Request(db, "2026-10-15T11:20:00+03:00"), csrf); Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        using var moved = await PostAsync(owner, $"/api/appointments/{original.Id}/move", new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), original.StaffMemberId, "2026-10-15T11:00:00+03:00"), csrf); Assert.Equal(HttpStatusCode.OK, moved.StatusCode); Assert.Equal(original.Price, Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await moved.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token)).Price); await AssertOriginalStateAsync(app, seed);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(43200, 365, 60)]
    [InlineData(1439, 1, 7)]
    public async Task ValidBoundaryValuesAreStoredWithoutRounding(int lead, int horizon, int step)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App; using var owner = await InviteOwnerAsync(seed); var before = await Read(owner);
        using var response = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(before.Version, lead, horizon, step, 120, true), await GetCsrfAsync(owner)); var after = await Body(response); Assert.Equal((lead, horizon, step), (after.LeadMinutes, after.HorizonDays, after.SlotMinutes)); Assert.Equal(after, await Read(owner));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task PolicyAndBookingOrMoveUseTheCommittedOrder(bool policyFirst, bool move)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App; await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); using var other = await AppointmentTests.OtherOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); var otherCsrf = await GetCsrfAsync(other);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var policy = await Read(other); var request = await Request(db, "2026-10-15T10:00:00+03:00"); AppointmentEndpoints.AppointmentResponse? original = null;
        if (move) { using var initial = await PostAsync(owner, "/api/appointments/", request with { StartsAt = "2026-10-15T12:00:00+03:00", RequestKey = Guid.NewGuid() }, csrf); Assert.Equal(HttpStatusCode.Created, initial.StatusCode); original = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await initial.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token)); }
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"BookingPolicies\" WHERE \"Id\"=1 FOR UPDATE", connection, transaction)) await command.ExecuteScalarAsync(Token);
        Task<HttpResponseMessage> Change() => PostAsync(other, Path, new BookingPolicyEndpoints.UpdateRequest(policy.Version, 180, 30, 15, 120, true), otherCsrf);
        Task<HttpResponseMessage> BookOrMove() => original is null ? PostAsync(owner, "/api/appointments/", request, csrf) : PostAsync(owner, $"/api/appointments/{original.Id}/move", new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), original.StaffMemberId, request.StartsAt), csrf);
        async Task AwaitLocks(int count)
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%BookingPolicies%'").SingleAsync(wait.Token) < count) await Task.Delay(20, wait.Token);
        }
        var first = policyFirst ? Change() : BookOrMove(); await AwaitLocks(1); var second = policyFirst ? BookOrMove() : Change(); await AwaitLocks(2); await transaction.CommitAsync(Token);
        using var firstResult = await first; using var secondResult = await second; var change = policyFirst ? firstResult : secondResult; var booking = policyFirst ? secondResult : firstResult; Assert.Equal(HttpStatusCode.OK, change.StatusCode); Assert.Equal(policyFirst ? HttpStatusCode.Conflict : move ? HttpStatusCode.OK : HttpStatusCode.Created, booking.StatusCode);
        Assert.Equal(180, (await Read(other)).LeadMinutes); Assert.Single(await db.BookingPolicyAudits.ToArrayAsync(Token)); Assert.Equal(move || !policyFirst ? 1 : 0, await db.Appointments.CountAsync(Token));
        if (move && policyFirst) { using var retained = await owner.GetAsync($"/api/appointments/{original?.Id}", Token); Assert.Equal(original, await retained.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token)); }
    }

    [Fact]
    public async Task MigrationPreservesOldDataRejectsInvalidWindowAndProtectsPolicyHistory()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App; await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var before = await Read(owner); var request = await Request(db, "2026-10-15T10:00:00+03:00"); using var created = await PostAsync(owner, "/api/appointments/", request, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.Created, created.StatusCode); var original = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await created.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261015164213_AppointmentPending", Token);
        await db.Database.ExecuteSqlRawAsync("UPDATE \"BookingPolicies\" SET \"LeadMinutes\"=1440, \"HorizonDays\"=1", Token);
        var guard = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(cancellationToken: Token)); Assert.Equal("23514", guard.SqlState);
        await db.Database.ExecuteSqlRawAsync("UPDATE \"BookingPolicies\" SET \"LeadMinutes\"=60, \"HorizonDays\"=30", Token); await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token); Assert.Equal(before, await Read(owner));
        using var retained = await owner.GetAsync($"/api/appointments/{original.Id}", Token); Assert.Equal(original, await retained.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token)); Assert.False(db.Database.HasPendingModelChanges());
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"BookingPolicies\" SET \"LeadMinutes\"=1440, \"HorizonDays\"=1", Token)); Assert.Equal("23514", invalid.SqlState);
        using var change = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(before.Version, 120, 7, 20, 120, true), await GetCsrfAsync(owner)); var after = await Body(change);
        var down = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261015164213_AppointmentPending", Token)); Assert.Equal("P0001", down.SqlState); Assert.Equal(after, await Read(owner)); Assert.Single(await db.BookingPolicyAudits.ToArrayAsync(Token)); await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task RestrictedRoleCanUpdateOnlyPolicyValuesAndAppendHistory()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.Database.ExecuteSqlRawAsync("CREATE ROLE app_user LOGIN PASSWORD 'SyntheticApp!123'; GRANT USAGE ON SCHEMA public TO app_user", Token);
        var grants = File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/app-user-grants.sql"))); await db.Database.ExecuteSqlRawAsync(grants, Token);
        var limitedConnection = new NpgsqlConnectionStringBuilder(database.GetConnectionString()) { Username = "app_user", Password = "SyntheticApp!123" }.ConnectionString;
        await using var limited = app.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AppDatabase"] = limitedConnection })));
        using var owner = limited.CreateClient(); await PasswordStepAsync(owner); await CompleteMfaAsync(owner, seed.Key); var before = await Read(owner); using var update = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(before.Version, 120, 7, 20, 120, true), await GetCsrfAsync(owner)); var after = await Body(update);
        await using var connection = new NpgsqlConnection(limitedConnection); await connection.OpenAsync(Token);
        foreach (var sql in new[] { "DELETE FROM \"BookingPolicies\"", "TRUNCATE \"BookingPolicies\"", "INSERT INTO \"BookingPolicies\" SELECT * FROM \"BookingPolicies\"", "UPDATE \"BookingPolicyAudits\" SET \"LeadMinutes\"=0", "DELETE FROM \"BookingPolicyAudits\"", "TRUNCATE \"BookingPolicyAudits\"" })
        { await using var command = new NpgsqlCommand(sql, connection); var denied = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token)); Assert.Equal("42501", denied.SqlState); }
        await using (var invalid = new NpgsqlCommand("UPDATE \"BookingPolicies\" SET \"Id\"=2", connection)) Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync(Token))).SqlState);
        await db.Database.ExecuteSqlRawAsync(grants, Token); Assert.Equal(after, await Read(owner)); Assert.Single(await db.BookingPolicyAudits.ToArrayAsync(Token)); await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task LockedPolicyTimesOutWithoutPartialUpdateAndRateLimitIsFinite()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App; using var owner = await InviteOwnerAsync(seed); var before = await Read(owner); var csrf = await GetCsrfAsync(owner); var request = new BookingPolicyEndpoints.UpdateRequest(before.Version, 120, 7, 20, 120, true);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"BookingPolicies\" WHERE \"Id\"=1 FOR UPDATE", connection, transaction)) await command.ExecuteScalarAsync(Token);
        using var failed = await PostAsync(owner, Path, request, csrf); Assert.False(failed.IsSuccessStatusCode); await transaction.CommitAsync(Token);
        Assert.Equal(before, await Read(owner)); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Empty(await db.BookingPolicyAudits.ToArrayAsync(Token));
        using var retry = await PostAsync(owner, Path, request, csrf); var after = await Body(retry); Assert.Single(await db.BookingPolicyAudits.ToArrayAsync(Token));
        var statuses = new List<HttpStatusCode>(); for (var index = 0; index < 31; index++) { using var response = await owner.GetAsync(Path, Token); statuses.Add(response.StatusCode); }
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses); Assert.Equal(after.Version, (await db.BookingPolicies.AsNoTracking().SingleAsync(Token)).Version); Assert.Single(await db.BookingPolicyAudits.ToArrayAsync(Token));
    }
}
