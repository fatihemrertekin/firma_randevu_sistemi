using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
using static Server.Tests.Support.IdentityTestEnvironment;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class AppointmentStorageTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTime Start = new(2026, 10, 15, 7, 0, 0, DateTimeKind.Utc);
    private sealed class BookingClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 14, 6, 0, 0, TimeSpan.Zero);
    }

    private static async Task<string> InsertAsync(string connectionString, Guid owner, Guid member, Guid service,
        DateTime start, string status = "Confirmed", int duration = 20, int before = 0, int after = 0, string zone = "UTC")
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await using var command = new NpgsqlCommand("""
            SELECT set_config('TimeZone', @zone, true);
            INSERT INTO "Appointments" ("Id","ActorId","RequestKey","RequestHash","CustomerName","CustomerPhone","CreatedAt","Version")
            VALUES (@id,@owner,@key,@hash,'Sentetik müşteri','+905000000001',@start,@version);
            INSERT INTO "AppointmentTimeBlocks" ("AppointmentId","StaffMemberId","ServiceDefinitionId","StaffName","ServiceName","DurationMinutes","Price","Currency","StartsAt","EndsAt","BufferBeforeMinutes","BufferAfterMinutes","OccupiedStartsAt","OccupiedEndsAt","Status")
            VALUES (@id,@member,@service,'Sentetik personel','Sentetik hizmet',@duration,123.45,'TRY',@start,@end,@before,@after,@occupiedStart,@occupiedEnd,@status);
            INSERT INTO "AppointmentAudits" ("Id","AppointmentId","ActorId","AppointmentVersion","OccurredAt")
            VALUES (@audit,@id,@owner,@version,@start);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("key", Guid.NewGuid()); command.Parameters.AddWithValue("hash", new string('A', 64));
        command.Parameters.AddWithValue("version", Guid.NewGuid()); command.Parameters.AddWithValue("audit", Guid.NewGuid());
        command.Parameters.AddWithValue("member", member); command.Parameters.AddWithValue("service", service);
        command.Parameters.AddWithValue("start", start); command.Parameters.AddWithValue("end", start.AddMinutes(duration));
        command.Parameters.AddWithValue("duration", duration); command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("before", before); command.Parameters.AddWithValue("after", after);
        command.Parameters.AddWithValue("zone", zone);
        command.Parameters.AddWithValue("occupiedStart", start.AddMinutes(-before)); command.Parameters.AddWithValue("occupiedEnd", start.AddMinutes(duration + after));
        try
        {
            await command.ExecuteNonQueryAsync(Token); await transaction.CommitAsync(Token); return "created";
        }
        catch (PostgresException exception) when (exception.SqlState == "23P01" && exception.ConstraintName == "EX_AppointmentTimeBlocks_StaffTime")
        {
            await transaction.RollbackAsync(Token); return exception.SqlState;
        }
    }

    [Fact]
    public async Task BufferMigrationBackfillsExistingBlocksAndProtectsExpandedIntervalsAndDown()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        Assert.Equal("created", await InsertAsync(database.GetConnectionString(), seed.OwnerId, member.Id, service.Id, Start));
        var original = await db.Appointments.AsNoTracking().SingleAsync(Token);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261014154014_AppointmentEngine", Token);
        await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        var retained = await db.AppointmentTimeBlocks.AsNoTracking().SingleAsync(Token);
        Assert.Equal(original.Version, (await db.Appointments.AsNoTracking().SingleAsync(Token)).Version);
        Assert.Equal(Start, retained.OccupiedStartsAt); Assert.Equal(Start.AddMinutes(20), retained.OccupiedEndsAt); Assert.Equal(0, retained.BufferBeforeMinutes);
        Assert.Equal(service.Price, await db.ServiceDefinitions.Select(item => item.Price).SingleAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
        Assert.False(db.Database.HasPendingModelChanges()); await AssertOriginalStateAsync(app, seed);
        Assert.Equal("created", await InsertAsync(database.GetConnectionString(), seed.OwnerId, member.Id, service.Id, Start.AddHours(2), before: 5, after: 10));
        Assert.Equal("23P01", await InsertAsync(database.GetConnectionString(), seed.OwnerId, member.Id, service.Id, Start.AddHours(2).AddMinutes(30), before: 5, after: 10));
        Assert.Equal("created", await InsertAsync(database.GetConnectionString(), seed.OwnerId, member.Id, service.Id, Start.AddHours(2).AddMinutes(35), before: 5, after: 10));
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => InsertAsync(database.GetConnectionString(), seed.OwnerId, member.Id, service.Id, Start.AddHours(4), before: 7, after: 11)));
        Assert.Single(results, item => item == "created"); Assert.Equal(19, results.Count(item => item == "23P01"));
        foreach (var sql in new[] { "UPDATE \"ServiceDefinitions\" SET \"BufferBeforeMinutes\"=-1", "UPDATE \"AppointmentTimeBlocks\" SET \"OccupiedEndsAt\"=\"EndsAt\" WHERE \"BufferAfterMinutes\">0", "UPDATE \"AppointmentTimeBlocks\" SET \"BufferAfterMinutes\"=121", "UPDATE \"AppointmentTimeBlocks\" SET \"OccupiedStartsAt\"='-infinity'" })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("23514", failure.SqlState);
        }
        Assert.Equal("created", await InsertAsync(database.GetConnectionString(), seed.OwnerId, member.Id, service.Id, Start.AddHours(6), before: 5, after: 10, zone: "America/New_York"));
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261014154014_AppointmentEngine", Token)); Assert.Equal("P0001", refusal.SqlState);
        Assert.Equal(5, await db.Appointments.CountAsync(Token)); Assert.Equal(5, await db.AppointmentAudits.CountAsync(Token));
    }

    [Fact]
    public async Task DatabaseConstraintStopsTwentyIndependentTransactionsAndRetainsHistoricalStates()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var members = await db.StaffMembers.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync(Token);
        var service = await db.ServiceDefinitions.SingleAsync(Token);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => InsertAsync(database.GetConnectionString(), seed.OwnerId, members[0], service.Id, Start)));
        Assert.Single(results, result => result == "created"); Assert.Equal(19, results.Count(result => result == "23P01"));
        Assert.Single(await db.Appointments.ToArrayAsync(Token)); Assert.Single(await db.AppointmentAudits.ToArrayAsync(Token));
        Assert.Equal("created", await InsertAsync(database.GetConnectionString(), seed.OwnerId, members[0], service.Id, Start.AddMinutes(20)));
        Assert.Equal("created", await InsertAsync(database.GetConnectionString(), seed.OwnerId, members[1], service.Id, Start));
        foreach (var status in new[] { "Pending", "Completed", "NoShow" })
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AppointmentTimeBlocks\" SET \"Status\" = {status}, \"PendingExpiresAt\" = {Start} WHERE \"StaffMemberId\" = {members[0]} AND \"StartsAt\" = {Start}", Token);
            Assert.Equal("23P01", await InsertAsync(database.GetConnectionString(), seed.OwnerId, members[0], service.Id, Start));
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AppointmentTimeBlocks\" SET \"Status\" = 'Cancelled' WHERE \"StaffMemberId\" = {members[0]} AND \"StartsAt\" = {Start}", Token);
        Assert.Equal("created", await InsertAsync(database.GetConnectionString(), seed.OwnerId, members[0], service.Id, Start));
        Assert.Equal(4, await db.Appointments.CountAsync(Token)); Assert.Equal(4, await db.AppointmentAudits.CountAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task DatabaseChecksRejectInvalidBlocksAndMigrationsPreserveExistingP02Data()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        Assert.Equal("created", await InsertAsync(database.GetConnectionString(), seed.OwnerId, member.Id, service.Id, Start));
        foreach (var sql in new[] {
            "UPDATE \"AppointmentTimeBlocks\" SET \"Status\"='Unknown'",
            "UPDATE \"AppointmentTimeBlocks\" SET \"EndsAt\"=\"StartsAt\"",
            "UPDATE \"AppointmentTimeBlocks\" SET \"DurationMinutes\"=25",
            "UPDATE \"AppointmentTimeBlocks\" SET \"Currency\"='USD'",
            "UPDATE \"AppointmentTimeBlocks\" SET \"StartsAt\"='-infinity'",
            "UPDATE \"Appointments\" SET \"RequestHash\"='invalid'",
            "UPDATE \"Appointments\" SET \"CustomerPhone\"='invalid'",
            "UPDATE \"BookingPolicies\" SET \"SlotMinutes\"=0"
        })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("23514", failure.SqlState);
        }
        var policy = await db.BookingPolicies.AsNoTracking().SingleAsync(Token); Assert.Equal(60, policy.LeadMinutes); Assert.Equal(30, policy.HorizonDays); Assert.Equal(15, policy.SlotMinutes);
        Assert.False(db.Database.HasPendingModelChanges());
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261005005021_DefinitionDeletion", Token);
        Assert.Equal(2, await db.StaffMembers.CountAsync(Token)); Assert.Equal(14, await db.StaffWorkingDays.CountAsync(Token));
        Assert.Equal(service.Price, await db.ServiceDefinitions.Select(item => item.Price).SingleAsync(Token)); await AssertOriginalStateAsync(app, seed);
        await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.AppointmentAudits.ToArrayAsync(Token));
        Assert.Equal(15, (await db.BookingPolicies.AsNoTracking().SingleAsync(Token)).SlotMinutes);
    }

    [Fact]
    public async Task RestrictedApplicationRoleCanBookButCannotRewriteHistoryOrDeletePolicy()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE ROLE app_user LOGIN PASSWORD 'SyntheticApp!123'; GRANT USAGE ON SCHEMA public TO app_user", Token);
        var grantsScript = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/app-user-grants.sql")));
        await db.Database.ExecuteSqlRawAsync(grantsScript, Token);
        var limitedConnection = new NpgsqlConnectionStringBuilder(database.GetConnectionString()) { Username = "app_user", Password = "SyntheticApp!123" }.ConnectionString;
        await using var limited = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services => services.AddSingleton<TimeProvider>(new BookingClock()));
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:AppDatabase"] = limitedConnection, ["Auth:InstanceId"] = "p02-recovery-test" }));
        });
        using var owner = limited.CreateClient(); await PasswordStepAsync(owner); await CompleteMfaAsync(owner, seed.Key);
        var member = await db.StaffMembers.OrderBy(item => item.Id).FirstAsync(Token); var service = await db.ServiceDefinitions.SingleAsync(Token);
        foreach (var endpoint in new[] { "/api/business-hours/breaks", $"/api/staff-members/{member.Id}/breaks" })
        {
            using var readBreaks = await owner.GetAsync(endpoint, Token);
            Assert.Equal(HttpStatusCode.OK, readBreaks.StatusCode);
            var week = Assert.IsType<Server.Features.Business.WeeklyBreaks.Response>(await readBreaks.Content.ReadFromJsonAsync<Server.Features.Business.WeeklyBreaks.Response>(Token));
            using var saveBreaks = await PostAsync(owner, endpoint, new
            {
                version = week.Version,
                days = Enumerable.Range(0, 7).Select(day => new { day, breaks = day == 3 ? new[] { new { startsAt = "12:00", endsAt = "13:00" } } : [] }).ToArray()
            }, await GetCsrfAsync(owner));
            Assert.Equal(HttpStatusCode.OK, saveBreaks.StatusCode);
        }
        foreach (var source in new[] { "/api/business-hours", $"/api/staff-members/{member.Id}" })
        {
            using var readWeek = await owner.GetAsync(source + "/breaks", Token);
            var parent = Assert.IsType<Server.Features.Business.WeeklyBreaks.Response>(await readWeek.Content.ReadFromJsonAsync<Server.Features.Business.WeeklyBreaks.Response>(Token));
            using var dated = await PostAsync(owner, source + "/exceptions/2026-10-15", new Server.Features.Business.DateExceptionEndpoints.UpdateRequest(parent.Version, false, "09:00", "19:00", []), await GetCsrfAsync(owner));
            Assert.Equal(HttpStatusCode.OK, dated.StatusCode);
            var day = Assert.IsType<Server.Features.Business.DateExceptionEndpoints.Response>(await dated.Content.ReadFromJsonAsync<Server.Features.Business.DateExceptionEndpoints.Response>(Token));
            using var removeDay = await PostAsync(owner, source + "/exceptions/2026-10-15/remove", new { version = day.Version }, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.OK, removeDay.StatusCode);
        }
        using var readMember = await owner.GetAsync($"/api/staff-members/{member.Id}/breaks", Token);
        var memberWeek = Assert.IsType<Server.Features.Business.WeeklyBreaks.Response>(await readMember.Content.ReadFromJsonAsync<Server.Features.Business.WeeklyBreaks.Response>(Token));
        using var createLeave = await PostAsync(owner, $"/api/staff-members/{member.Id}/leave", new Server.Features.Staff.StaffLeaveEndpoints.CreateRequest(Guid.NewGuid(), memberWeek.Version, "2026-10-15T12:00:00+03:00", "2026-10-15T13:00:00+03:00"), await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.OK, createLeave.StatusCode);
        var leave = Assert.IsType<Server.Features.Staff.StaffLeaveEndpoints.Response>(await createLeave.Content.ReadFromJsonAsync<Server.Features.Staff.StaffLeaveEndpoints.Response>(Token));
        using var removeLeave = await PostAsync(owner, $"/api/staff-members/{member.Id}/leave/{leave.Leave.Id}/remove", new { version = leave.Version }, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.OK, removeLeave.StatusCode);
        using var buffersWrite = await PostAsync(owner, $"/api/services/{service.Id}/buffers", new { version = service.Version, bufferBeforeMinutes = 5, bufferAfterMinutes = 10 }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, buffersWrite.StatusCode);
        var buffers = Assert.IsType<Server.Features.Services.ServiceDefinitionEndpoints.BuffersResponse>(await buffersWrite.Content.ReadFromJsonAsync<Server.Features.Services.ServiceDefinitionEndpoints.BuffersResponse>(Token));
        using var response = await PostAsync(owner, "/api/appointments/", new
        {
            requestKey = Guid.NewGuid(),
            serviceId = service.Id,
            serviceVersion = buffers.Version,
            staffMemberId = member.Id,
            startsAt = "2026-10-15T10:00:00+03:00",
            customerName = "Sentetik müşteri",
            customerPhone = "05000000001"
        }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var booked = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        var saved = await db.AppointmentTimeBlocks.AsNoTracking().SingleAsync(Token); Assert.Equal(5, saved.BufferBeforeMinutes); Assert.Equal(10, saved.BufferAfterMinutes);
        using var moved = await PostAsync(owner, $"/api/appointments/{booked.Id}/move", new AppointmentMoveEndpoints.Request(booked.Version, Guid.NewGuid(), booked.StaffMemberId, "2026-10-15T11:00:00+03:00"), await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        booked = Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await moved.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        using var cancelled = await PostAsync(owner, $"/api/appointments/{booked.Id}/cancel", new AppointmentStatusEndpoints.Request(booked.Version, Guid.NewGuid()), await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", Assert.IsType<AppointmentEndpoints.AppointmentResponse>(await cancelled.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token)).Status);
        await using var connection = new NpgsqlConnection(limitedConnection); await connection.OpenAsync(Token);
        foreach (var sql in new[] { "UPDATE \"Appointments\" SET \"CustomerName\"=\"CustomerName\"", "UPDATE \"AppointmentTimeBlocks\" SET \"Price\"=\"Price\"", "UPDATE \"AppointmentTimeBlocks\" SET \"DurationMinutes\"=\"DurationMinutes\"", "UPDATE \"AppointmentTimeBlocks\" SET \"BufferBeforeMinutes\"=\"BufferBeforeMinutes\"", "UPDATE \"AppointmentMoveCommands\" SET \"StartsAt\"=\"StartsAt\"", "DELETE FROM \"AppointmentMoveCommands\"", "TRUNCATE \"AppointmentMoveCommands\"", "UPDATE \"AppointmentStatusCommands\" SET \"Status\"=\"Status\"", "DELETE FROM \"AppointmentStatusCommands\"", "TRUNCATE \"AppointmentStatusCommands\"" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token)); Assert.Equal("42501", failure.SqlState);
        }
        foreach (var sql in new[] { "UPDATE \"AppointmentAudits\" SET \"ActorId\"=\"ActorId\"", "DELETE FROM \"Appointments\"", "DELETE FROM \"BookingPolicies\"", "TRUNCATE \"BusinessWeeklyBreaks\"", "TRUNCATE \"StaffWeeklyBreaks\"", "UPDATE \"BusinessHoursAudits\" SET \"Kind\"='Hours'", "DELETE FROM \"StaffHoursAudits\"", "DELETE FROM \"StaffLeaves\"", "TRUNCATE \"StaffLeaves\"", "TRUNCATE \"BusinessDateExceptions\"", "TRUNCATE \"StaffDateBreaks\"" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token)); Assert.Equal("42501", failure.SqlState);
        }
        await AssertOriginalStateAsync(app, seed);
    }
}
