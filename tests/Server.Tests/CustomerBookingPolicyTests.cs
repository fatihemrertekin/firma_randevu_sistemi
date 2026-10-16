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

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class CustomerBookingPolicyTests
{
    private const string Path = "/api/booking-policy/";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static async Task<BookingPolicyEndpoints.Response> Read(HttpClient client)
    {
        using var response = await client.GetAsync(Path, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<BookingPolicyEndpoints.Response>(await response.Content.ReadFromJsonAsync<BookingPolicyEndpoints.Response>(Token));
    }

    [Fact]
    public async Task CustomerFieldsPersistFalseAndBoundariesWithVersionAuditAndPublicProjection()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var anonymous = app.CreateClient();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await Read(owner); Assert.Equal(120, before.CustomerCancellationLeadMinutes); Assert.True(before.RequiresBusinessApproval);
        var csrf = await GetCsrfAsync(owner);
        foreach (var (minutes, approval) in new[] { (0, false), (43200, true) })
        {
            var request = new BookingPolicyEndpoints.UpdateRequest(before.Version, before.LeadMinutes, before.HorizonDays, before.SlotMinutes, minutes, approval);
            using var saved = await PostAsync(owner, Path, request, csrf); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var after = await Read(owner); Assert.NotEqual(before.Version, after.Version);
            Assert.Equal((minutes, approval), (after.CustomerCancellationLeadMinutes, after.RequiresBusinessApproval));
            var audit = await db.BookingPolicyAudits.AsNoTracking().SingleAsync(item => item.PolicyVersion == after.Version, Token);
            Assert.Equal((minutes, approval), (audit.CustomerCancellationLeadMinutes, audit.RequiresBusinessApproval));
            Assert.Equal(before.Version, audit.PreviousVersion); Assert.Equal(seed.OwnerId, audit.ActorId);
            using var catalog = await anonymous.GetAsync("/api/public-booking/catalog", Token);
            Assert.True(catalog.Headers.CacheControl?.NoStore);
            var body = Assert.IsType<PublicBookingEndpoints.CatalogResponse>(await catalog.Content.ReadFromJsonAsync<PublicBookingEndpoints.CatalogResponse>(Token));
            Assert.Equal((after.Version, minutes, approval), (body.Policy.Version, body.Policy.CustomerCancellationLeadMinutes, body.Policy.RequiresBusinessApproval));
            using var stale = await PostAsync(owner, Path, request, csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var count = await db.BookingPolicyAudits.CountAsync(Token);
            using var noOp = await PostAsync(owner, Path, request with { Version = after.Version }, csrf); Assert.Equal(HttpStatusCode.OK, noOp.StatusCode);
            Assert.Equal(after, await Read(owner)); Assert.Equal(count, await db.BookingPolicyAudits.CountAsync(Token));
            before = after;
        }
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"BookingPolicies\" SET \"CustomerCancellationLeadMinutes\"=-1", Token));
        Assert.Equal("23514", invalid.SqlState);
        await AssertOriginalStateAsync(app, seed);
    }

    [Theory]
    [InlineData(null, true, "customerCancellationLeadMinutes")]
    [InlineData(-1, true, "customerCancellationLeadMinutes")]
    [InlineData(43201, true, "customerCancellationLeadMinutes")]
    [InlineData(120, null, "requiresBusinessApproval")]
    public async Task MissingOrInvalidCustomerValuesNeverDefaultOrWrite(int? minutes, bool? approval, string field)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var before = await Read(owner);
        using var response = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(before.Version, 60, 30, 15, minutes, approval), await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains(field, await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(before, await Read(owner)); using var scope = app.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().BookingPolicyAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task MigrationPreservesExistingPolicyAndAuditAndGuardsHistoryOnDown()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261015185734_BookingPolicyEditing", Token);
        var oldVersion = Guid.NewGuid(); var previous = Guid.NewGuid(); var auditId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BookingPolicies\" SET \"LeadMinutes\"=90, \"HorizonDays\"=45, \"SlotMinutes\"=7, \"Version\"={oldVersion}", Token);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"BookingPolicyAudits\" (\"Id\", \"ActorId\", \"PreviousVersion\", \"PolicyVersion\", \"OccurredAt\", \"LeadMinutes\", \"HorizonDays\", \"SlotMinutes\") VALUES ({auditId}, {seed.OwnerId}, {previous}, {oldVersion}, {DateTimeOffset.UtcNow}, 90, 45, 7)", Token);
        await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        var before = await Read(owner); Assert.Equal((oldVersion, 90, 45, 7, 120, true), (before.Version, before.LeadMinutes, before.HorizonDays, before.SlotMinutes, before.CustomerCancellationLeadMinutes, before.RequiresBusinessApproval));
        var historical = await db.BookingPolicyAudits.AsNoTracking().SingleAsync(Token);
        Assert.Equal(auditId, historical.Id); Assert.Null(historical.CustomerCancellationLeadMinutes); Assert.Null(historical.RequiresBusinessApproval);
        using var saved = await PostAsync(owner, Path, new BookingPolicyEndpoints.UpdateRequest(before.Version, 90, 45, 7, 0, false), await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var after = await Read(owner);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync("20261015185734_BookingPolicyEditing", Token));
        Assert.Equal("P0001", denied.SqlState); Assert.Equal(after, await Read(owner)); Assert.Equal(2, await db.BookingPolicyAudits.CountAsync(Token));
        Assert.False(db.Database.HasPendingModelChanges()); await AssertOriginalStateAsync(app, seed);
    }
}
