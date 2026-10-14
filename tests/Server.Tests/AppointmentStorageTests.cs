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
        DateTime start, string status = "Confirmed", int duration = 20)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await using var command = new NpgsqlCommand("""
            INSERT INTO "Appointments" ("Id","ActorId","RequestKey","RequestHash","CustomerName","CustomerPhone","CreatedAt","Version")
            VALUES (@id,@owner,@key,@hash,'Sentetik müşteri','+905000000001',@start,@version);
            INSERT INTO "AppointmentTimeBlocks" ("AppointmentId","StaffMemberId","ServiceDefinitionId","StaffName","ServiceName","DurationMinutes","Price","Currency","StartsAt","EndsAt","Status")
            VALUES (@id,@member,@service,'Sentetik personel','Sentetik hizmet',@duration,123.45,'TRY',@start,@end,@status);
            INSERT INTO "AppointmentAudits" ("Id","AppointmentId","ActorId","AppointmentVersion","OccurredAt")
            VALUES (@audit,@id,@owner,@version,@start);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid()); command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("key", Guid.NewGuid()); command.Parameters.AddWithValue("hash", new string('A', 64));
        command.Parameters.AddWithValue("version", Guid.NewGuid()); command.Parameters.AddWithValue("audit", Guid.NewGuid());
        command.Parameters.AddWithValue("member", member); command.Parameters.AddWithValue("service", service);
        command.Parameters.AddWithValue("start", start); command.Parameters.AddWithValue("end", start.AddMinutes(duration));
        command.Parameters.AddWithValue("duration", duration); command.Parameters.AddWithValue("status", status);
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
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AppointmentTimeBlocks\" SET \"Status\" = {status} WHERE \"StaffMemberId\" = {members[0]} AND \"StartsAt\" = {Start}", Token);
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
        Assert.Equal(service.Price, (await db.ServiceDefinitions.SingleAsync(Token)).Price); await AssertOriginalStateAsync(app, seed);
        await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        Assert.Empty(await db.Appointments.ToArrayAsync(Token)); Assert.Empty(await db.AppointmentAudits.ToArrayAsync(Token));
        Assert.Equal(15, (await db.BookingPolicies.AsNoTracking().SingleAsync(Token)).SlotMinutes);
    }

    [Fact]
    public async Task RestrictedApplicationRoleCanBookButCannotAlterAuditOrPolicyValues()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), new BookingClock()); await using var app = seed.App;
        await AppointmentTests.ConfigureAsync(app);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE ROLE app_user LOGIN PASSWORD 'SyntheticApp!123'; GRANT USAGE ON SCHEMA public TO app_user", Token);
        var grantsScript = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/Apply-LocalMigration.ps1")));
        var grantStart = grantsScript.IndexOf("@'", StringComparison.Ordinal) + 2;
        var grantEnd = grantsScript.IndexOf("'@ |", grantStart, StringComparison.Ordinal);
        Assert.True(grantStart >= 2 && grantEnd > grantStart);
        await db.Database.ExecuteSqlRawAsync(grantsScript[grantStart..grantEnd], Token);
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
        using var response = await PostAsync(owner, "/api/appointments/", new
        {
            requestKey = Guid.NewGuid(),
            serviceId = service.Id,
            serviceVersion = service.Version,
            staffMemberId = member.Id,
            startsAt = "2026-10-15T10:00:00+03:00",
            customerName = "Sentetik müşteri",
            customerPhone = "05000000001"
        }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<AppointmentEndpoints.AppointmentResponse>(Token));
        await using var connection = new NpgsqlConnection(limitedConnection); await connection.OpenAsync(Token);
        foreach (var sql in new[] { "UPDATE \"AppointmentAudits\" SET \"ActorId\"=\"ActorId\"", "DELETE FROM \"Appointments\"", "UPDATE \"BookingPolicies\" SET \"LeadMinutes\"=0" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Token)); Assert.Equal("42501", failure.SqlState);
        }
        await AssertOriginalStateAsync(app, seed);
    }
}
