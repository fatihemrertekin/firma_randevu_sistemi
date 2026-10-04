using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class BusinessLogoRemovalTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RemovedLogoEndpointsReturnNotFoundForAnonymousAndOwner()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var anonymous = app.CreateClient(); using var owner = await InviteOwnerAsync(seed);
        foreach (var client in new[] { anonymous, owner })
        {
            using var metadata = await client.GetAsync("/api/business-logo/", Token);
            using var image = await client.GetAsync("/api/business-logo/image/" + Guid.NewGuid(), Token);
            using var upload = await client.PostAsJsonAsync("/api/business-logo/", new { version = Guid.NewGuid(), image = "AQ==" }, Token);
            Assert.Equal(HttpStatusCode.NotFound, metadata.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, image.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
        }
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task RemovalDropsImageStorageButPreservesAllOtherDataAndHistoricalAudit()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261003200924_AuditLogIndexes", Token);
        await AuditLogTests.SeedAsync(db, seed.OwnerId, seed.OtherUserId);
        await db.Database.ExecuteSqlRawAsync("UPDATE \"BusinessLogos\" SET \"Png\" = '\\x01'::bytea, \"Width\" = 1, \"Height\" = 1", Token);
        var before = await FingerprintAsync(db);
        await migrator.MigrateAsync(cancellationToken: Token);
        Assert.False(await LogoTableExistsAsync(db));
        Assert.Equal(before, await FingerprintAsync(db));
        using var response = await owner.GetAsync("/api/audit-log/", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(await db.BusinessLogoAudits.ToArrayAsync(Token));
        // Down restores an empty logo table; deleted image bytes require a pre-migration backup.
        await migrator.MigrateAsync("20261003200924_AuditLogIndexes", Token);
        Assert.True(await LogoTableExistsAsync(db));
        Assert.True(await db.Database.SqlQueryRaw<bool>("SELECT \"Png\" IS NULL AS \"Value\" FROM \"BusinessLogos\"").SingleAsync(Token));
        Assert.Equal(before, await FingerprintAsync(db));
        await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        Assert.False(await LogoTableExistsAsync(db));
        Assert.Equal(before, await FingerprintAsync(db));
    }

    private static Task<bool> LogoTableExistsAsync(AppDbContext db) =>
        db.Database.SqlQueryRaw<bool>("SELECT to_regclass('public.\"BusinessLogos\"') IS NOT NULL AS \"Value\"").SingleAsync(Token);

    private static Task<string> FingerprintAsync(AppDbContext db)
    {
        var tables = db.Model.GetEntityTypes().Select(type => type.GetTableName()).Where(name => name is not null).Distinct().Order();
        var parts = tables.Select(table => "SELECT '" + table + "' AS name, coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text, '') AS data FROM \"" + table + "\" t");
        var sql = "SELECT md5(string_agg(data, ',' ORDER BY name)) AS \"Value\" FROM (" + string.Join(" UNION ALL ", parts) + ") all_data";
        return db.Database.SqlQueryRaw<string>(sql).SingleAsync(Token);
    }
}
