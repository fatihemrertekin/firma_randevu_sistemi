using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
public sealed class AppointmentPendingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static AppointmentPendingWorker Worker(IServiceProvider services) => ActivatorUtilities.CreateInstance<AppointmentPendingWorker>(services);
    private static string Path(Guid id, string action = "confirm") => $"/api/appointments/{id}/{action}";
    private static async Task<AppointmentEndpoints.AppointmentResponse> Body(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
    }
    private static async Task<AppointmentEndpoints.CreateRequest> Request(AppDbContext db, string start = "2026-10-15T10:00:00+03:00")
    {
        var service = await db.ServiceDefinitions.SingleAsync(Token); var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token);
        return new(Guid.NewGuid(), service.Id, service.Version, member.Id, start, "Sentetik müşteri", "05000000001");
    }
    private static async Task<AppointmentEndpoints.AppointmentResponse> Book(HttpClient owner, AppDbContext db, string start = "2026-10-15T10:00:00+03:00")
    {
        using var response = await PostAsync(owner, "/api/appointments/pending", await Request(db, start), await GetCsrfAsync(owner));
        return await Body(response, HttpStatusCode.Created);
    }

    [Fact]
    public async Task TwentyPendingRequestsHaveOneWinnerAndConfirmationPreservesSnapshotAndReplay()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10; await db.SaveChangesAsync(Token);
        var template = await Request(db); var requests = Enumerable.Range(0, 20).Select(_ => template with { RequestKey = Guid.NewGuid() }).ToArray();
        var writes = await Task.WhenAll(requests.Select(request => PostAsync(owner, "/api/appointments/pending", request, csrf)));
        AppointmentEndpoints.AppointmentResponse original; AppointmentEndpoints.CreateRequest winner;
        try { var index = Array.FindIndex(writes, response => response.StatusCode == HttpStatusCode.Created); Assert.True(index >= 0); Assert.Equal(19, writes.Count(response => response.StatusCode == HttpStatusCode.Conflict)); original = await Body(writes[index], HttpStatusCode.Created); winner = requests[index]; }
        finally { foreach (var response in writes) response.Dispose(); }
        Assert.Equal("Pending", original.Status); Assert.Equal(clock.Now.UtcDateTime.AddMinutes(30), original.PendingExpiresAt); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
        using var wrongMode = await PostAsync(owner, "/api/appointments/", winner, csrf); Assert.Equal(HttpStatusCode.Conflict, wrongMode.StatusCode);
        using var unavailable = await owner.GetAsync($"/api/appointments/availability?serviceId={original.ServiceId}&staffMemberId={original.StaffMemberId}&date=2026-10-15", Token);
        Assert.DoesNotContain(original.StartsAt, Assert.IsType<AppointmentEndpoints.AvailabilityResponse>(await unavailable.Content.ReadFromJsonAsync<AppointmentEndpoints.AvailabilityResponse>(Token)).StartsAt);
        service.Name = "Yeni hizmet"; service.Price = 999; service.DurationMinutes = 90; service.IsActive = false;
        (await db.StaffMembers.SingleAsync(item => item.Id == original.StaffMemberId, Token)).IsActive = false; await db.SaveChangesAsync(Token);
        var command = new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid());
        var confirmations = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(owner, Path(original.Id), command, csrf)));
        AppointmentEndpoints.AppointmentResponse? saved = null;
        try { foreach (var response in confirmations) { var result = await Body(response); saved ??= result; Assert.Equal(saved, result); } }
        finally { foreach (var response in confirmations) response.Dispose(); }
        Assert.NotNull(saved); Assert.NotEqual(original.Version, saved.Version); Assert.Equal(original with { Version = saved.Version, Status = "Confirmed" }, saved);
        clock.Now = clock.Now.AddMinutes(31); using var worker = Worker(app.Services); Assert.Equal(0, await worker.ProcessBatchAsync(Token));
        using var creationReplay = await PostAsync(owner, "/api/appointments/pending", winner, csrf); Assert.Equal(saved, await Body(creationReplay));
        using var differentAction = await PostAsync(owner, Path(original.Id, "cancel"), command, csrf); Assert.Equal(HttpStatusCode.Conflict, differentAction.StatusCode);
        Assert.Single(await db.AppointmentStatusCommands.ToArrayAsync(Token)); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
        Assert.Equal(seed.OwnerId, (await db.AppointmentAudits.AsNoTracking().SingleAsync(item => item.Kind == "Confirmed", Token)).ActorId); await AssertOriginalStateAsync(app, seed);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ConfirmationUsesStrictDeadlineAndTwentyWorkersExpireAtMostOnce(int milliseconds)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db, "2026-10-15T11:00:00+03:00");
        clock.Now = new DateTimeOffset(Assert.IsType<DateTime>(original.PendingExpiresAt)).AddMilliseconds(milliseconds);
        using var confirmation = await PostAsync(owner, Path(original.Id), new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()), csrf);
        Assert.Equal(milliseconds < 0 ? HttpStatusCode.OK : HttpStatusCode.Conflict, confirmation.StatusCode);
        var workers = Enumerable.Range(0, 20).Select(_ => Worker(app.Services)).ToArray();
        try { Assert.Equal(milliseconds < 0 ? 0 : 1, (await Task.WhenAll(workers.Select(worker => worker.ProcessBatchAsync(Token)))).Sum()); }
        finally { foreach (var worker in workers) worker.Dispose(); }
        using var read = await owner.GetAsync($"/api/appointments/{original.Id}", Token); var saved = await Body(read);
        Assert.Equal(milliseconds < 0 ? "Confirmed" : "Cancelled", saved.Status); Assert.NotEqual(original.Version, saved.Version); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
        var audit = await db.AppointmentAudits.AsNoTracking().SingleAsync(item => item.Kind != "Created", Token);
        Assert.Equal(milliseconds < 0 ? "Confirmed" : "Expired", audit.Kind); Assert.Equal(milliseconds < 0 ? seed.OwnerId : (Guid?)null, audit.ActorId);
        using var late = await PostAsync(owner, Path(original.Id), new AppointmentStatusEndpoints.Request(saved.Version, Guid.NewGuid()), csrf); Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        if (milliseconds >= 0) { using var replacement = await PostAsync(owner, "/api/appointments/", await Request(db, "2026-10-15T11:00:00+03:00"), csrf); await Body(replacement, HttpStatusCode.Created); }
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task PendingCancellationReleasesTimeAndOtherTransitionsCannotBypassApproval()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db, "2026-10-15T11:00:00+03:00");
        foreach (var action in new[] { "complete", "no-show" }) { using var denied = await PostAsync(owner, Path(original.Id, action), new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()), csrf); Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode); }
        using var move = await PostAsync(owner, Path(original.Id, "move"), new AppointmentMoveEndpoints.Request(original.Version, Guid.NewGuid(), original.StaffMemberId, "2026-10-15T11:00:00+03:00"), csrf); Assert.Equal(HttpStatusCode.Conflict, move.StatusCode);
        var command = new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()); using var cancel = await PostAsync(owner, Path(original.Id, "cancel"), command, csrf); var saved = await Body(cancel); Assert.Equal("Cancelled", saved.Status);
        clock.Now = clock.Now.AddMinutes(30); using var worker = Worker(app.Services); Assert.Equal(0, await worker.ProcessBatchAsync(Token));
        using var replay = await PostAsync(owner, Path(original.Id, "cancel"), command, csrf); Assert.Equal(saved, await Body(replay));
        using var replacement = await PostAsync(owner, "/api/appointments/", await Request(db, "2026-10-15T11:00:00+03:00"), csrf); await Body(replacement, HttpStatusCode.Created);
        Assert.Equal(3, await db.AppointmentAudits.CountAsync(Token)); Assert.DoesNotContain(await db.AppointmentAudits.ToArrayAsync(Token), item => item.Kind == "Expired");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthorityAndAuditFailureProtectPendingState(bool expiry)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db); var request = await Request(db, "2026-10-15T12:00:00+03:00");
        var command = new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid());
        using var anonymous = app.CreateClient(); await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        foreach (var client in new[] { anonymous, staff })
        {
            using var creation = await PostAsync(client, "/api/appointments/pending", request, await GetCsrfAsync(client)); Assert.Equal(client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, creation.StatusCode);
            using var confirmation = await PostAsync(client, Path(original.Id), command, await GetCsrfAsync(client)); Assert.Equal(client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, confirmation.StatusCode);
        }
        using var noCsrf = await PostAsync(owner, Path(original.Id), command, null); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var noCreateCsrf = await PostAsync(owner, "/api/appointments/pending", request, null); Assert.Equal(HttpStatusCode.BadRequest, noCreateCsrf.StatusCode);
        using var stale = await PostAsync(owner, Path(original.Id), command with { Version = Guid.NewGuid() }, csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_pending_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic audit failure'; END $$; CREATE TRIGGER reject_pending BEFORE INSERT ON \"AppointmentAudits\" FOR EACH ROW EXECUTE FUNCTION reject_pending_audit()", Token);
        if (expiry) { clock.Now = clock.Now.AddMinutes(30); using var worker = Worker(app.Services); Assert.Equal(0, await worker.ProcessBatchAsync(Token)); }
        else { using var failed = await PostAsync(owner, Path(original.Id), command, csrf); Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); }
        using var retained = await owner.GetAsync($"/api/appointments/{original.Id}", Token); Assert.Equal(original, await Body(retained)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token)); Assert.Empty(await db.AppointmentStatusCommands.ToArrayAsync(Token));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_pending ON \"AppointmentAudits\"", Token);
        if (expiry) { using var restarted = Worker(app.Services); Assert.Equal(1, await restarted.ProcessBatchAsync(Token)); }
        else { using var retry = await PostAsync(owner, Path(original.Id), command, csrf); await Body(retry); }
        var user = await db.Users.SingleAsync(item => item.Id == seed.OwnerId, Token); user.SecurityStamp = Guid.NewGuid().ToString(); await db.SaveChangesAsync(Token);
        using var revoked = await PostAsync(owner, "/api/appointments/pending", request, csrf); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var revokedConfirm = await PostAsync(owner, Path(original.Id), command, csrf); Assert.Equal(HttpStatusCode.Unauthorized, revokedConfirm.StatusCode);
    }

    [Fact]
    public async Task WaitingConfirmationRechecksDeadlineAndWorkerSkipsLockedStaff()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"StaffMembers\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction)) { command.Parameters.AddWithValue("id", original.StaffMemberId); await command.ExecuteScalarAsync(Token); }
        var confirmation = PostAsync(owner, Path(original.Id), new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid()), csrf);
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) < 1) await Task.Delay(20, wait.Token);
        clock.Now = clock.Now.AddMinutes(30); using var worker = Worker(app.Services); Assert.Equal(0, await worker.ProcessBatchAsync(Token).WaitAsync(TimeSpan.FromSeconds(3), Token));
        await transaction.CommitAsync(Token); using var result = await confirmation; Assert.Equal(HttpStatusCode.Conflict, result.StatusCode); Assert.Equal(1, await worker.ProcessBatchAsync(Token)); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
    }

    [Fact]
    public async Task HostedWorkerStartsAfterRestartAndExpiresPersistedRequest()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db); clock.Now = clock.Now.AddMinutes(30);
        await using var restarted = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AppDatabase"] = database.GetConnectionString(), ["Auth:InstanceId"] = "pending-restart-test" }));
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(clock));
        }); using var client = restarted.CreateClient();
        Assert.Single(restarted.Services.GetServices<IHostedService>(), service => service is AppointmentPendingWorker);
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.AppointmentTimeBlocks.AsNoTracking().Where(item => item.AppointmentId == original.Id).Select(item => item.Status).SingleAsync(wait.Token) == "Pending") await Task.Delay(40, wait.Token);
        var audit = await db.AppointmentAudits.AsNoTracking().SingleAsync(item => item.Kind == "Expired", Token); Assert.Null(audit.ActorId); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token)); await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task HostedWorkerSurvivesDatabaseOutageAndResumesExpiry()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var connectionString = new NpgsqlConnectionStringBuilder(database.GetConnectionString()) { Timeout = 1, CommandTimeout = 1 }.ConnectionString;
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db); clock.Now = clock.Now.AddMinutes(30);
        await database.PauseAsync(Token);
        await using var restarted = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AppDatabase"] = connectionString, ["Auth:InstanceId"] = "pending-outage-test", ["OwnerPasswordReset:LocalEnabled"] = "true" }));
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(clock));
        }); using var client = restarted.CreateClient();
        try
        {
            Assert.True(restarted.Services.GetRequiredService<Server.Features.Identity.OwnerSelfServiceResetFlow>().Enabled);
            using var unavailable = await client.GetAsync("/health/ready", Token); Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            await Task.Delay(TimeSpan.FromSeconds(5), Token);
            using var live = await client.GetAsync("/health/live", Token); Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            Assert.False(restarted.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        }
        finally { await database.UnpauseAsync(Token); }
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (await db.AppointmentTimeBlocks.AsNoTracking().Where(item => item.AppointmentId == original.Id).Select(item => item.Status).SingleAsync(wait.Token) == "Pending") await Task.Delay(100, wait.Token);
        var audit = await db.AppointmentAudits.AsNoTracking().SingleAsync(item => item.Kind == "Expired", Token); Assert.Null(audit.ActorId); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
        using var ready = await client.GetAsync("/health/ready", Token); Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task MigrationPreservesOldRowsRefusesUnreviewedPendingAndProtectsNewHistory()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var request = await Request(db);
        using var creation = await PostAsync(owner, "/api/appointments/", request, await GetCsrfAsync(owner)); var original = await Body(creation, HttpStatusCode.Created);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261015144847_AppointmentMove", Token);
        await db.Database.ExecuteSqlRawAsync("UPDATE \"AppointmentTimeBlocks\" SET \"Status\"='Pending'", Token);
        var guard = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(cancellationToken: Token)); Assert.Equal("P0001", guard.SqlState);
        await db.Database.ExecuteSqlRawAsync("UPDATE \"AppointmentTimeBlocks\" SET \"Status\"='Confirmed'", Token); await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        using var read = await owner.GetAsync($"/api/appointments/{original.Id}", Token); Assert.Equal(original, await Body(read)); Assert.False(db.Database.HasPendingModelChanges());
        var pending = await Book(owner, db, "2026-10-15T12:00:00+03:00");
        foreach (var sql in new[] { "UPDATE \"AppointmentTimeBlocks\" SET \"PendingExpiresAt\"=NULL WHERE \"Status\"='Pending'", "UPDATE \"AppointmentTimeBlocks\" SET \"PendingExpiresAt\"='infinity'", "UPDATE \"AppointmentTimeBlocks\" SET \"PendingExpiresAt\"=\"StartsAt\"+interval '1 second' WHERE \"Status\"='Pending'", "UPDATE \"AppointmentAudits\" SET \"ActorId\"=NULL" })
        { var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("23514", invalid.SqlState); }
        var down = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261015144847_AppointmentMove", Token)); Assert.Equal("P0001", down.SqlState);
        using var retained = await owner.GetAsync($"/api/appointments/{pending.Id}", Token); Assert.Equal(pending, await Body(retained)); await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task RestrictedRoleCanConfirmAndExpireButCannotRewriteDeadlineOrSystemAudit()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App; await AppointmentTests.ConfigureAsync(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE ROLE app_user LOGIN PASSWORD 'SyntheticApp!123'; GRANT USAGE ON SCHEMA public TO app_user", Token);
        var grants = File.ReadAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/Apply-LocalMigration.ps1"))); var start = grants.IndexOf("@'", StringComparison.Ordinal) + 2; var end = grants.IndexOf("'@ |", start, StringComparison.Ordinal); await db.Database.ExecuteSqlRawAsync(grants[start..end], Token);
        var limitedConnection = new NpgsqlConnectionStringBuilder(database.GetConnectionString()) { Username = "app_user", Password = "SyntheticApp!123" }.ConnectionString;
        await using var limited = app.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:AppDatabase"] = limitedConnection })));
        using var owner = await InviteOwnerAsync(seed with { App = limited }); var csrf = await GetCsrfAsync(owner); var confirmed = await Book(owner, db); var expired = await Book(owner, db, "2026-10-15T12:00:00+03:00");
        using var response = await PostAsync(owner, Path(confirmed.Id), new AppointmentStatusEndpoints.Request(confirmed.Version, Guid.NewGuid()), csrf); await Body(response);
        clock.Now = clock.Now.AddMinutes(30); using var worker = Worker(limited.Services); Assert.Equal(1, await worker.ProcessBatchAsync(Token));
        using var limitedScope = limited.Services.CreateScope(); var limitedDb = limitedScope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var sql in new[] { "UPDATE \"AppointmentTimeBlocks\" SET \"PendingExpiresAt\"=\"PendingExpiresAt\"", "UPDATE \"AppointmentAudits\" SET \"ActorId\"=NULL", "DELETE FROM \"AppointmentAudits\"", "TRUNCATE \"AppointmentStatusCommands\"" })
        { var failure = await Assert.ThrowsAsync<PostgresException>(() => limitedDb.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("42501", failure.SqlState); }
        Assert.Equal("Cancelled", (await db.AppointmentTimeBlocks.AsNoTracking().SingleAsync(item => item.AppointmentId == expired.Id, Token)).Status); await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task BatchIsBoundedAndOneAuditFailureDoesNotPreventOtherExpirations()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var day = 16; day <= 36; day++) await Book(owner, db, new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.FromHours(3)).AddDays(day - 16).ToString("O"));
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_one_expiry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Kind\"='Expired' AND NEW.\"AppointmentId\"=(SELECT \"Id\" FROM \"Appointments\" ORDER BY \"Id\" LIMIT 1) THEN RAISE EXCEPTION 'synthetic single-row failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER reject_one_expiry BEFORE INSERT ON \"AppointmentAudits\" FOR EACH ROW EXECUTE FUNCTION reject_one_expiry()", Token);
        clock.Now = clock.Now.AddMinutes(30); using var worker = Worker(app.Services);
        Assert.Equal(19, await worker.ProcessBatchAsync(Token)); Assert.Equal(2, await db.AppointmentTimeBlocks.CountAsync(item => item.Status == "Pending", Token));
        Assert.Equal(1, await worker.ProcessBatchAsync(Token)); Assert.Single(await db.AppointmentTimeBlocks.Where(item => item.Status == "Pending").ToArrayAsync(Token));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_one_expiry ON \"AppointmentAudits\"", Token);
        using var restarted = Worker(app.Services); Assert.Equal(1, await restarted.ProcessBatchAsync(Token)); Assert.Equal(21, await db.AppointmentAudits.CountAsync(item => item.Kind == "Expired" && item.ActorId == null, Token));
    }

    [Fact]
    public async Task DeadlineUsesDatabasePrecisionAndNeverExtendsPastAppointmentStart()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock(); clock.Now = clock.Now.AddTicks(1234567);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app); using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var request = await Request(db, "2026-10-15T11:00:00+03:00");
        using var created = await PostAsync(owner, "/api/appointments/pending", request, csrf); var saved = await Body(created, HttpStatusCode.Created);
        using var replay = await PostAsync(owner, "/api/appointments/pending", request, csrf); Assert.Equal(saved, await Body(replay));
        var stored = await db.Appointments.AsNoTracking().SingleAsync(Token); Assert.Equal(stored.CreatedAt.AddMinutes(30), saved.PendingExpiresAt);
        (await db.BookingPolicies.SingleAsync(Token)).LeadMinutes = 0; await db.SaveChangesAsync(Token); clock.Now = new(2026, 10, 15, 6, 0, 0, TimeSpan.Zero);
        var shortWait = await Book(owner, db, "2026-10-15T09:15:00+03:00"); Assert.Equal(shortWait.StartsAt, shortWait.PendingExpiresAt);
        using var alreadyStarted = await PostAsync(owner, "/api/appointments/pending", await Request(db, "2026-10-15T09:00:00+03:00"), csrf); Assert.Equal(HttpStatusCode.Conflict, alreadyStarted.StatusCode);
    }

    [Fact]
    public async Task PendingBuffersProtectCalendarUntilExplicitExpiryReleasesThem()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App; await AppointmentTests.ConfigureAsync(app);
        using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = await db.ServiceDefinitions.SingleAsync(Token); service.BufferBeforeMinutes = 5; service.BufferAfterMinutes = 10; await db.SaveChangesAsync(Token); var original = await Book(owner, db, "2026-10-15T11:00:00+03:00");
        var member = await db.StaffMembers.AsNoTracking().SingleAsync(item => item.Id == original.StaffMemberId, Token); var leave = new StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), member.Version, "2026-10-15T11:20:00+03:00", "2026-10-15T11:30:00+03:00");
        clock.Now = clock.Now.AddMinutes(30);
        using var blocked = await PostAsync(owner, $"/api/staff-members/{member.Id}/leave", leave, csrf); Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var worker = Worker(app.Services); Assert.Equal(1, await worker.ProcessBatchAsync(Token));
        using var slots = await owner.GetAsync($"/api/appointments/availability?serviceId={service.Id}&staffMemberId={member.Id}&date=2026-10-15", Token); Assert.Contains(original.StartsAt, Assert.IsType<AppointmentEndpoints.AvailabilityResponse>(await slots.Content.ReadFromJsonAsync<AppointmentEndpoints.AvailabilityResponse>(Token)).StartsAt);
        using var released = await PostAsync(owner, $"/api/staff-members/{member.Id}/leave", leave, csrf); Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmationAndExpirySerializeAtTheirLockedDecision(bool confirmFirst)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token); var clock = new Clock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock); await using var app = seed.App; await AppointmentTests.ConfigureAsync(app);
        using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION pause_pending_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Kind\" IN ('Confirmed','Expired') THEN PERFORM pg_advisory_xact_lock(174043); END IF; RETURN NEW; END $$; CREATE TRIGGER pause_pending BEFORE INSERT ON \"AppointmentAudits\" FOR EACH ROW EXECUTE FUNCTION pause_pending_audit()", Token);
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(174043)", connection, transaction)) await command.ExecuteNonQueryAsync(Token);
        var expiry = Assert.IsType<DateTime>(original.PendingExpiresAt); clock.Now = new DateTimeOffset(expiry).AddMilliseconds(confirmFirst ? -1 : 0);
        using var worker = Worker(app.Services); var request = new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid());
        Task<HttpResponseMessage>? confirmation = confirmFirst ? PostAsync(owner, Path(original.Id), request, csrf) : null;
        Task<int>? expiration = confirmFirst ? null : worker.ProcessBatchAsync(Token);
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock'").SingleAsync(wait.Token) < 1) await Task.Delay(20, wait.Token);
        clock.Now = expiry;
        if (confirmFirst) { Assert.Equal(0, await worker.ProcessBatchAsync(Token)); }
        else { confirmation = PostAsync(owner, Path(original.Id), request, csrf); await AwaitInviteLocksAsync(app); }
        await transaction.CommitAsync(Token);
        Assert.NotNull(confirmation); using var result = await confirmation; Assert.Equal(confirmFirst ? HttpStatusCode.OK : HttpStatusCode.Conflict, result.StatusCode);
        if (expiration is not null) Assert.Equal(1, await expiration);
        Assert.Equal(0, await worker.ProcessBatchAsync(Token)); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
        Assert.Equal(confirmFirst ? "Confirmed" : "Cancelled", (await db.AppointmentTimeBlocks.AsNoTracking().SingleAsync(Token)).Status);
    }

    [Fact]
    public async Task ConfirmationTimeoutRetryAndRateLimitCannotLeavePartialState()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new Clock()); await using var app = seed.App; await AppointmentTests.ConfigureAsync(app);
        using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var original = await Book(owner, db);
        var request = new AppointmentStatusEndpoints.Request(original.Version, Guid.NewGuid());
        await using var connection = new NpgsqlConnection(database.GetConnectionString()); await connection.OpenAsync(Token); await using var transaction = await connection.BeginTransactionAsync(Token);
        await using (var command = new NpgsqlCommand("SELECT 1 FROM \"StaffMembers\" WHERE \"Id\"=@id FOR UPDATE", connection, transaction)) { command.Parameters.AddWithValue("id", original.StaffMemberId); await command.ExecuteScalarAsync(Token); }
        using var timeout = await PostAsync(owner, Path(original.Id), request, csrf).WaitAsync(TimeSpan.FromSeconds(25), Token); Assert.Equal(HttpStatusCode.InternalServerError, timeout.StatusCode);
        await transaction.CommitAsync(Token); Assert.Empty(await db.AppointmentStatusCommands.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token)); Assert.Equal(original.Version, (await db.Appointments.AsNoTracking().SingleAsync(Token)).Version);
        using var retry = await PostAsync(owner, Path(original.Id), request, csrf); var saved = await Body(retry);
        for (var attempt = 0; attempt < 57; attempt++) { using var invalid = await PostAsync(owner, Path(original.Id), request with { Version = Guid.Empty }, csrf); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using var limited = await PostAsync(owner, Path(original.Id), request, csrf); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(saved.Version, (await db.Appointments.AsNoTracking().SingleAsync(Token)).Version); Assert.Single(await db.AppointmentStatusCommands.ToArrayAsync(Token)); Assert.Equal(2, await db.AppointmentAudits.CountAsync(Token));
    }
}
