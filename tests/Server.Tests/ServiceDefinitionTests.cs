using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Services;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class ServiceDefinitionTests
{
    private const string Path = "/api/services/";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static async Task<ServiceDefinitionEndpoints.ServiceResponse> ServiceAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<ServiceDefinitionEndpoints.ServiceResponse>(await response.Content.ReadFromJsonAsync<ServiceDefinitionEndpoints.ServiceResponse>(Token));
    }
    private static async Task<ServiceDefinitionEndpoints.ServiceResponse> CreateAsync(HttpClient owner, string name = "Deneme Hizmet", Guid? id = null)
    {
        using var response = await PostAsync(owner, Path, new { id = id ?? Guid.NewGuid(), name, durationMinutes = 30, price = "350.00" }, await GetCsrfAsync(owner));
        var member = await ServiceAsync(response, HttpStatusCode.Created);
        Assert.Equal(Path + member.Id, response.Headers.Location?.OriginalString);
        return member;
    }
    private static Task<HttpResponseMessage> UpdateAsync(HttpClient owner, ServiceDefinitionEndpoints.ServiceResponse member, string name, string? csrf) =>
        PostAsync(owner, Path + member.Id, new { name, member.DurationMinutes, member.Price, member.Version }, csrf);
    private static Task<HttpResponseMessage> StatusAsync(HttpClient owner, ServiceDefinitionEndpoints.ServiceResponse member, bool isActive, string? csrf) =>
        PostAsync(owner, Path + member.Id + "/status", new { isActive, member.Version }, csrf);
    private static async Task<ServiceDefinitionEndpoints.ServicePage> ListAsync(HttpClient owner, string query = "")
    {
        using var response = await owner.GetAsync(Path + query, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<ServiceDefinitionEndpoints.ServicePage>(await response.Content.ReadFromJsonAsync<ServiceDefinitionEndpoints.ServicePage>(Token));
    }

    [Fact]
    public async Task PriceAndDurationValidationNeverRoundsAndExactDecimalEditsAreAtomic()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        foreach (var price in new[] { "-1", "1.005", "1e2", "NaN", "1000000", "1,50", "1,250.00", "", "+1", "1." })
        {
            using var invalid = await PostAsync(owner, Path, new { id = Guid.NewGuid(), name = "Deneme", durationMinutes = 30, price }, csrf);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var problem = await invalid.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Token);
            Assert.True(problem.GetProperty("errors").TryGetProperty("price", out _));
        }
        foreach (var durationMinutes in new[] { -1, 0, 1441 })
        {
            using var invalid = await PostAsync(owner, Path, new { id = Guid.NewGuid(), name = "Deneme", durationMinutes, price = "350.00" }, csrf);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var numericPrice = await PostAsync(owner, Path, new { id = Guid.NewGuid(), name = "Deneme", durationMinutes = 30, price = 0.29m }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, numericPrice.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.ServiceDefinitions.ToArrayAsync(Token)); Assert.Empty(await db.ServiceDefinitionAudits.ToArrayAsync(Token));
        foreach (var price in new[] { "0", "0.29", "1234.50", "999999.99" })
        {
            using var created = await PostAsync(owner, Path, new { id = Guid.NewGuid(), name = "Deneme", durationMinutes = 1440, price, currency = "USD" }, csrf);
            var item = await ServiceAsync(created, HttpStatusCode.Created); Assert.Equal("TRY", item.Currency);
            Assert.Equal(price == "0" ? "0.00" : price, item.Price);
            Assert.Equal(decimal.Parse(item.Price, System.Globalization.CultureInfo.InvariantCulture),
                (await db.ServiceDefinitions.AsNoTracking().SingleAsync(service => service.Id == item.Id, Token)).Price);
        }
        var before = (await ListAsync(owner)).Items[0];
        using var updated = await PostAsync(owner, Path + before.Id, new { name = "Yeni Hizmet", durationMinutes = 45, price = "499.99", before.Version }, csrf);
        var after = await ServiceAsync(updated); Assert.Equal("Yeni Hizmet", after.Name); Assert.Equal(45, after.DurationMinutes); Assert.Equal("499.99", after.Price);
        Assert.NotEqual(before.Version, after.Version);
        using var invalidUpdate = await PostAsync(owner, Path + before.Id, new { name = "Değişmemeli", durationMinutes = 45, price = "499.999", after.Version }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdate.StatusCode);
        using var persisted = await owner.GetAsync(Path + before.Id, Token); Assert.Equal(after, await ServiceAsync(persisted));
    }

    [Fact]
    public async Task AllRoutesRequireMfaOwnerAndWritesRequireCsrf()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        await CreatePasswordStaffAsync(app);
        using var anonymous = app.CreateClient(); using var pending = app.CreateClient(); await PasswordStepAsync(pending);
        using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        using var owner = await InviteOwnerAsync(seed);
        var member = await CreateAsync(owner);
        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        {
            foreach (var path in new[] { Path, Path + member.Id })
            { using var read = await client.GetAsync(path, Token); Assert.Equal(expected, read.StatusCode); }
            var csrf = await GetCsrfAsync(client);
            using var create = await PostAsync(client, Path, new { id = Guid.NewGuid(), name = "Yetkisiz", durationMinutes = 30, price = "350.00" }, csrf);
            using var rename = await UpdateAsync(client, member, "Yetkisiz", csrf);
            using var status = await StatusAsync(client, member, false, csrf);
            Assert.Equal(expected, create.StatusCode); Assert.Equal(expected, rename.StatusCode); Assert.Equal(expected, status.StatusCode);
        }
        using var noCreate = await PostAsync(owner, Path, new { id = Guid.NewGuid(), name = "CSRF yok", durationMinutes = 30, price = "350.00" }, null);
        using var noRename = await UpdateAsync(owner, member, "CSRF yok", null);
        using var noStatus = await StatusAsync(owner, member, false, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCreate.StatusCode); Assert.Equal(HttpStatusCode.BadRequest, noRename.StatusCode); Assert.Equal(HttpStatusCode.BadRequest, noStatus.StatusCode);
        Assert.Single((await ListAsync(owner)).Items);
    }

    [Fact]
    public async Task ValidationMissingStatusAndUnknownIdsNeverWrite()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        foreach (var name in new[] { "  ", new string('a', 101), "Bir\nKişi" })
        { using var invalid = await PostAsync(owner, Path, new { id = Guid.NewGuid(), name, durationMinutes = 30, price = "350.00" }, csrf); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); }
        using var invalidId = await PostAsync(owner, Path, new { id = Guid.Empty, name = "Deneme", durationMinutes = 30, price = "350.00" }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, invalidId.StatusCode);
        var member = await CreateAsync(owner);
        using var missingStatus = await PostAsync(owner, Path + member.Id + "/status", new { member.Version }, csrf);
        using var invalidVersion = await PostAsync(owner, Path + member.Id, new { name = "Deneme", durationMinutes = 30, price = "350.00", version = Guid.Empty }, csrf);
        using var missing = await PostAsync(owner, Path + Guid.NewGuid(), new { name = "Deneme", member.DurationMinutes, member.Price, member.Version }, csrf);
        using var badPage = await owner.GetAsync(Path + "?page=0&pageSize=51", Token);
        Assert.Equal(HttpStatusCode.BadRequest, missingStatus.StatusCode); Assert.Equal(HttpStatusCode.BadRequest, invalidVersion.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode); Assert.Equal(HttpStatusCode.BadRequest, badPage.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.ServiceDefinitions.AsNoTracking().SingleAsync(Token)).IsActive);
        Assert.Single(await db.ServiceDefinitionAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task DefinitionsAndStatusesAreAuditedWithoutChangingLoginAccountsOrOwner()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        var staffId = await CreatePasswordStaffAsync(app);
        using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        var member = await CreateAsync(owner, "  Aynı İsim  "); var duplicate = await CreateAsync(owner, "Aynı İsim");
        Assert.Equal("Aynı İsim", member.Name); Assert.NotEqual(member.Id, duplicate.Id);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stamp = (await db.Users.AsNoTracking().SingleAsync(user => user.Id == staffId, Token)).SecurityStamp;
        using var renamed = await UpdateAsync(owner, member, "Yeni İsim", csrf); var current = await ServiceAsync(renamed);
        Assert.NotEqual(member.Version, current.Version);
        using var same = await UpdateAsync(owner, current, current.Name, csrf); Assert.Equal(current, await ServiceAsync(same));
        using var inactive = await StatusAsync(owner, current, false, csrf); current = await ServiceAsync(inactive); Assert.False(current.IsActive);
        using var sameStatus = await StatusAsync(owner, current, false, csrf); Assert.Equal(current, await ServiceAsync(sameStatus));
        using var signedIn = await staff.GetAsync("/api/auth/me", Token); Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        using var active = await StatusAsync(owner, current, true, csrf); current = await ServiceAsync(active); Assert.True(current.IsActive);
        var audits = await db.ServiceDefinitionAudits.AsNoTracking().Where(audit => audit.ServiceDefinitionId == member.Id).OrderBy(audit => audit.OccurredAt).ToArrayAsync(Token);
        Assert.Equal(new[] { "Created", "Updated", "Deactivated", "Activated" }, audits.Select(audit => audit.Kind));
        Assert.All(audits, audit => { Assert.Equal(seed.OwnerId, audit.ActorId); Assert.Equal(TimeSpan.Zero, audit.OccurredAt.Offset); });
        var staffAfter = await db.Users.AsNoTracking().SingleAsync(user => user.Id == staffId, Token);
        Assert.True(staffAfter.IsActive); Assert.Equal(stamp, staffAfter.SecurityStamp);
        Assert.Empty(await db.StaffDeactivationAudits.ToArrayAsync(Token));
        await AssertOriginalStateAsync(app, seed);
        Assert.Equal(2, (await ListAsync(owner)).Items.Length);
    }

    [Fact]
    public async Task ParallelCreateIsIdempotentAndReplayCannotReactivateOrOverwrite()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner); var id = Guid.NewGuid();
        var writes = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostAsync(owner, Path, new { id, name = "Deneme", durationMinutes = 30, price = "350.00" }, csrf)));
        Assert.Single(writes, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(writes, response => Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        var member = await ServiceAsync(writes[0], writes[0].StatusCode); foreach (var response in writes) response.Dispose();
        using var inactive = await StatusAsync(owner, member, false, csrf); var current = await ServiceAsync(inactive);
        using var replay = await PostAsync(owner, Path, new { id, name = "Deneme", durationMinutes = 30, price = "350.00" }, csrf); Assert.Equal(current, await ServiceAsync(replay));
        using var different = await PostAsync(owner, Path, new { id, name = "Farklı", durationMinutes = 30, price = "350.00" }, csrf); Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        using var stale = await UpdateAsync(owner, member, "Eski sürüm", csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Single(await db.ServiceDefinitions.ToArrayAsync(Token)); Assert.Equal(2, await db.ServiceDefinitionAudits.CountAsync(Token));
    }

    [Fact]
    public async Task ParallelEditAndStatusAllowOnlyOneVersionAndPaginationKeepsInactiveMembers()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        var member = await CreateAsync(owner, "A Kişi"); await CreateAsync(owner, "B Kişi");
        var writes = await Task.WhenAll(UpdateAsync(owner, member, "A Yeni", csrf), StatusAsync(owner, member, false, csrf));
        Assert.Single(writes, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(writes, response => response.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in writes) response.Dispose();
        var page1 = await ListAsync(owner, "?pageSize=1"); var page2 = await ListAsync(owner, "?pageSize=1&page=2");
        Assert.True(page1.HasMore); Assert.False(page2.HasMore); Assert.NotEqual(Assert.Single(page1.Items).Id, Assert.Single(page2.Items).Id);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await db.ServiceDefinitionAudits.CountAsync(Token));
        var persisted = await db.ServiceDefinitions.AsNoTracking().SingleAsync(item => item.Id == member.Id, Token);
        Assert.True(persisted.Name == "A Yeni" ? persisted.IsActive : !persisted.IsActive);
        using var fresh = await owner.GetAsync(Path + member.Id, Token); Assert.Equal(persisted.Version, (await ServiceAsync(fresh)).Version);
    }

    [Fact]
    public async Task AuditAndDeferredCommitFailuresRollBackCreateEditAndStatus()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var member = await CreateAsync(owner); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_member_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic member audit failure'; END; $$;
            CREATE TRIGGER reject_member_audit BEFORE INSERT ON "ServiceDefinitionAudits"
            FOR EACH ROW EXECUTE FUNCTION reject_member_audit();
            """, Token);
        foreach (var deferred in new[] { false, true })
        {
            if (deferred) await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_member_audit ON "ServiceDefinitionAudits";
                CREATE CONSTRAINT TRIGGER reject_member_audit AFTER INSERT ON "ServiceDefinitionAudits"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_member_audit();
                """, Token);
            using var create = await PostAsync(owner, Path, new { id = Guid.NewGuid(), name = "Başarısız", durationMinutes = 30, price = "350.00" }, csrf);
            using var rename = await UpdateAsync(owner, member, "Başarısız", csrf);
            using var status = await StatusAsync(owner, member, false, csrf);
            Assert.Equal(HttpStatusCode.InternalServerError, create.StatusCode); Assert.Equal(HttpStatusCode.InternalServerError, rename.StatusCode); Assert.Equal(HttpStatusCode.InternalServerError, status.StatusCode);
            var after = await db.ServiceDefinitions.AsNoTracking().SingleAsync(Token);
            Assert.Equal(member.Name, after.Name); Assert.Equal(member.Version, after.Version); Assert.True(after.IsActive);
            Assert.Single(await db.ServiceDefinitionAudits.ToArrayAsync(Token));
        }
    }

    [Fact]
    public async Task WaitingWriteRejectsOwnerRevokedAfterAuthentication()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var locked = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {seed.OwnerId} FOR UPDATE").SingleAsync(Token);
        var waiting = PostAsync(owner, Path, new { id = Guid.NewGuid(), name = "İptal edilmiş Owner", durationMinutes = 30, price = "350.00" }, csrf);
        await using var observer = new NpgsqlConnection(database.GetConnectionString()); await observer.OpenAsync(Token);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10); var blocked = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var query = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%AspNetUsers%FOR UPDATE%')", observer);
            blocked = (bool)(await query.ExecuteScalarAsync(Token) ?? false); if (blocked) break;
            await Task.Delay(25, Token);
        }
        Assert.True(blocked);
        Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().UpdateSecurityStampAsync(locked)).Succeeded);
        await transaction.CommitAsync(Token);
        using var result = await waiting; Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Empty(await db.ServiceDefinitions.ToArrayAsync(Token)); Assert.Empty(await db.ServiceDefinitionAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task AdditiveMigrationPreservesAccountsAndSyntheticDownUpPreservesPreviousSchema()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(database.GetConnectionString()).Options);
        var migrations = db.Database.GetMigrations().ToArray();
        var index = Array.FindIndex(migrations, migration => migration.EndsWith("ServiceDefinitions", StringComparison.Ordinal)); Assert.True(index > 0);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(migrations[index - 1], Token);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "AspNetUsers" ("Id", "UserName", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "IsActive", "SecurityStamp")
            VALUES ('8fab3fce-6c1b-490d-99ac-01d39a2b12d8', 'synthetic-existing', true, false, true, true, 0, true, 'synthetic-stamp');
            INSERT INTO "StaffMembers" ("Id", "Name", "IsActive", "Version")
            VALUES ('ee963352-c826-4b86-b6c1-8be60aa44814', 'Existing Person', true, 'bfc5d327-d127-4a7d-bc24-a2d24c66f8d0');
            """, Token);
        await migrator.MigrateAsync(migrations[index], Token);
        var user = await db.Users.AsNoTracking().SingleAsync(Token); Assert.True(user.IsActive); Assert.True(user.TwoFactorEnabled); Assert.Equal("synthetic-stamp", user.SecurityStamp);
        Assert.Empty(await db.ServiceDefinitions.ToArrayAsync(Token)); Assert.Empty(await db.ServiceDefinitionAudits.ToArrayAsync(Token));
        await migrator.MigrateAsync(migrations[index - 1], Token); await migrator.MigrateAsync(migrations[index], Token);
        Assert.Equal(user.SecurityStamp, (await db.Users.AsNoTracking().SingleAsync(Token)).SecurityStamp);
        Assert.Single(await db.BusinessProfiles.ToArrayAsync(Token));
        Assert.Equal("Existing Person", (await db.StaffMembers.AsNoTracking().SingleAsync(Token)).Name);
    }
}
