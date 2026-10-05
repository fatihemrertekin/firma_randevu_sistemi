using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Services;
using Server.Features.Staff;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class StaffServiceAssignmentTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Path(Guid id) => $"/api/staff-members/{id}/services";
    private static async Task<StaffMemberEndpoints.MemberResponse> CreateMemberAsync(HttpClient owner, string name = "Deneme Personel")
    {
        using var response = await PostAsync(owner, "/api/staff-members/", new { id = Guid.NewGuid(), name }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<StaffMemberEndpoints.MemberResponse>(await response.Content.ReadFromJsonAsync<StaffMemberEndpoints.MemberResponse>(Token));
    }
    private static async Task<ServiceDefinitionEndpoints.ServiceResponse> CreateServiceAsync(HttpClient owner, string name = "A Hizmet")
    {
        using var response = await PostAsync(owner, "/api/services/", new { id = Guid.NewGuid(), name, durationMinutes = 30, price = "0.29" }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<ServiceDefinitionEndpoints.ServiceResponse>(await response.Content.ReadFromJsonAsync<ServiceDefinitionEndpoints.ServiceResponse>(Token));
    }
    private static StaffServiceEndpoints.ServiceReference Ref(ServiceDefinitionEndpoints.ServiceResponse service) => new(service.Id, service.Version);
    private static Task<HttpResponseMessage> SaveAsync(HttpClient owner, StaffMemberEndpoints.MemberResponse member, string? csrf, params StaffServiceEndpoints.ServiceReference[] services) =>
        PostAsync(owner, Path(member.Id), new { member.Version, services }, csrf);
    private static async Task<StaffServiceEndpoints.SelectionResponse> SelectionAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<StaffServiceEndpoints.SelectionResponse>(await response.Content.ReadFromJsonAsync<StaffServiceEndpoints.SelectionResponse>(Token));
    }
    private static async Task<StaffServiceEndpoints.SelectionPage> ReadAsync(HttpClient owner, Guid id, string query = "")
    {
        using var response = await owner.GetAsync(Path(id) + query, Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<StaffServiceEndpoints.SelectionPage>(await response.Content.ReadFromJsonAsync<StaffServiceEndpoints.SelectionPage>(Token));
    }
    [Fact]
    public async Task BothRoutesRequireMfaOwnerAndMutationRequiresCsrf()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var member = await CreateMemberAsync(owner);
        using var anonymous = app.CreateClient(); using var pending = app.CreateClient();
        await PasswordStepAsync(pending);
        var staffId = await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        {
            using var read = await client.GetAsync(Path(member.Id), Token); Assert.Equal(expected, read.StatusCode);
            using var write = await SaveAsync(client, member, await GetCsrfAsync(client)); Assert.Equal(expected, write.StatusCode);
        }
        using var missing = await SaveAsync(owner, member, null); Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var invalid = await SaveAsync(owner, member, "invalid-csrf"); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var staffSession = await staff.GetAsync("/api/auth/me", Token); Assert.Equal(HttpStatusCode.OK, staffSession.StatusCode);
        Assert.NotEqual(seed.OwnerId, staffId); Assert.Empty((await ReadAsync(owner, member.Id)).Selected);
    }
    [Fact]
    public async Task InvalidSetsAndMissingObjectsNeverWrite()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var member = await CreateMemberAsync(owner); var service = await CreateServiceAsync(owner); var csrf = await GetCsrfAsync(owner);
        var invalidSets = new object[] {
            new { member.Version, services = (StaffServiceEndpoints.ServiceReference[]?)null },
            new { version = Guid.Empty, services = Array.Empty<StaffServiceEndpoints.ServiceReference>() },
            new { member.Version, services = new[] { Ref(service), Ref(service) } },
            new { member.Version, services = new[] { new StaffServiceEndpoints.ServiceReference(Guid.Empty, service.Version) } },
            new { member.Version, services = new[] { new StaffServiceEndpoints.ServiceReference(service.Id, Guid.Empty) } },
            new { member.Version, services = Enumerable.Range(0, 501).Select(_ => new StaffServiceEndpoints.ServiceReference(Guid.NewGuid(), Guid.NewGuid())).ToArray() },
            new { member.Version, services = new object?[] { null } }
        };
        foreach (var request in invalidSets)
        {
            using var response = await PostAsync(owner, Path(member.Id), request, csrf); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(Token); Assert.Contains("\"services\"", body);
        }
        using var missingMember = await PostAsync(owner, Path(Guid.NewGuid()), new { member.Version, services = new[] { Ref(service) } }, csrf);
        using var missingService = await SaveAsync(owner, member, csrf, new StaffServiceEndpoints.ServiceReference(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, missingMember.StatusCode); Assert.Equal(HttpStatusCode.NotFound, missingService.StatusCode);
        foreach (var query in new[] { "?page=0", "?pageSize=0", "?pageSize=51" })
        { using var response = await owner.GetAsync(Path(member.Id) + query, Token); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.StaffServiceAssignments.ToArrayAsync(Token)); Assert.Empty(await db.StaffServiceAssignmentAudits.ToArrayAsync(Token));
        Assert.Equal(member.Version, (await db.StaffMembers.AsNoTracking().SingleAsync(Token)).Version);
    }
    [Fact]
    public async Task ReplacementAndNoOpAreAtomicAuditedAndDoNotChangeOtherData()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var member = await CreateMemberAsync(owner); var other = await CreateMemberAsync(owner, "Diğer Personel");
        var first = await CreateServiceAsync(owner); var second = await CreateServiceAsync(owner, "B Hizmet"); var csrf = await GetCsrfAsync(owner);
        using var initial = await SaveAsync(owner, member, csrf, Ref(second), Ref(first)); var saved = await SelectionAsync(initial);
        Assert.NotEqual(member.Version, saved.Member.Version); Assert.Equal(member.Name, saved.Member.Name); Assert.True(saved.Member.IsActive);
        var page1 = await ReadAsync(owner, member.Id, "?pageSize=1"); var page2 = await ReadAsync(owner, member.Id, "?pageSize=1&page=2");
        Assert.True(page1.HasMore); Assert.False(page2.HasMore); Assert.Equal(2, page1.Selected.Length); Assert.Equal(2, page2.Selected.Length);
        Assert.NotEqual(Assert.Single(page1.Items).Id, Assert.Single(page2.Items).Id); Assert.All(page1.Items, item => Assert.Equal("0.29", item.Price));
        using var noOp = await SaveAsync(owner, saved.Member, csrf, Ref(first), Ref(second)); Assert.Equal(saved.Member, (await SelectionAsync(noOp)).Member);
        using var remove = await SaveAsync(owner, saved.Member, csrf, Ref(second)); var current = await SelectionAsync(remove); Assert.Equal(second.Id, Assert.Single(current.Selected).Id);
        using var staleRename = await PostAsync(owner, $"/api/staff-members/{member.Id}", new { name = "Eski ad", member.Version }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, staleRename.StatusCode); Assert.Empty((await ReadAsync(owner, other.Id)).Selected);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audits = await db.StaffServiceAssignmentAudits.AsNoTracking().OrderBy(item => item.OccurredAt).ToArrayAsync(Token);
        Assert.Equal(3, audits.Length); Assert.Equal(2, audits.Count(item => item.Kind == "Assigned")); Assert.Single(audits, item => item.Kind == "Unassigned" && item.ServiceDefinitionId == first.Id);
        Assert.All(audits, item => { Assert.Equal(member.Id, item.StaffMemberId); Assert.Equal(seed.OwnerId, item.ActorId); Assert.Equal(TimeSpan.Zero, item.OccurredAt.Offset); });
        Assert.Equal(2, audits.Select(item => item.MemberVersion).Distinct().Count());
        Assert.Equal(2, await db.ServiceDefinitionAudits.CountAsync(Token)); Assert.Equal(2, await db.StaffMemberAudits.CountAsync(Token));
        var definitions = await db.ServiceDefinitions.AsNoTracking().ToArrayAsync(Token); Assert.All(definitions, item => { Assert.Equal(0.29m, item.Price); Assert.Equal(30, item.DurationMinutes); Assert.True(item.IsActive); });
        await AssertOriginalStateAsync(app, seed);
    }
    [Fact]
    public async Task InactiveRecordsKeepExistingLinksButRejectNewLinksAndAllowRemoval()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var member = await CreateMemberAsync(owner); var service = await CreateServiceAsync(owner); var fresh = await CreateServiceAsync(owner, "B Hizmet"); var csrf = await GetCsrfAsync(owner);
        using var initial = await SaveAsync(owner, member, csrf, Ref(service)); var saved = await SelectionAsync(initial);
        using var offService = await PostAsync(owner, $"/api/services/{service.Id}/status", new { isActive = false, service.Version }, csrf); Assert.Equal(HttpStatusCode.OK, offService.StatusCode);
        using var offMember = await PostAsync(owner, $"/api/staff-members/{member.Id}/status", new { isActive = false, version = saved.Member.Version }, csrf); Assert.Equal(HttpStatusCode.OK, offMember.StatusCode);
        var page = await ReadAsync(owner, member.Id); Assert.False(page.Member.IsActive); Assert.False(page.Items.Single(item => item.Id == service.Id).IsActive);
        var kept = Assert.Single(page.Selected);
        using var unchanged = await SaveAsync(owner, page.Member, csrf, kept); Assert.Equal(page.Member, (await SelectionAsync(unchanged)).Member);
        using var newForInactiveMember = await SaveAsync(owner, page.Member, csrf, kept, Ref(fresh)); Assert.Equal(HttpStatusCode.Conflict, newForInactiveMember.StatusCode);
        var other = await CreateMemberAsync(owner, "Aktif Personel"); using var newForInactiveService = await SaveAsync(owner, other, csrf, kept); Assert.Equal(HttpStatusCode.Conflict, newForInactiveService.StatusCode);
        using var removed = await SaveAsync(owner, page.Member, csrf); var result = await SelectionAsync(removed); Assert.Empty(result.Selected); Assert.False(result.Member.IsActive);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.StaffServiceAssignments.ToArrayAsync(Token)); Assert.Equal(2, await db.StaffServiceAssignmentAudits.CountAsync(Token));
    }
    [Fact]
    public async Task ParallelSetsAndCatalogChangesCannotOverwriteStaleSelections()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var member = await CreateMemberAsync(owner); var first = await CreateServiceAsync(owner); var second = await CreateServiceAsync(owner, "B Hizmet"); var csrf = await GetCsrfAsync(owner);
        var writes = await Task.WhenAll(SaveAsync(owner, member, csrf, Ref(first)), SaveAsync(owner, member, csrf, Ref(second)));
        Assert.Single(writes, item => item.StatusCode == HttpStatusCode.OK); Assert.Single(writes, item => item.StatusCode == HttpStatusCode.Conflict); foreach (var response in writes) response.Dispose();
        var current = await ReadAsync(owner, member.Id); Assert.Single(current.Selected);
        using var edit = await PostAsync(owner, $"/api/services/{second.Id}", new { name = "Güncel Hizmet", durationMinutes = 45, price = "1234.50", second.Version }, csrf);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var staleCatalog = await SaveAsync(owner, current.Member, csrf, Ref(second)); Assert.Equal(HttpStatusCode.Conflict, staleCatalog.StatusCode);
        var page = await ReadAsync(owner, member.Id); var definition = page.Items.Single(item => item.Id == second.Id);
        using var cleared = await SaveAsync(owner, page.Member, csrf); var empty = await SelectionAsync(cleared);
        var race = await Task.WhenAll(SaveAsync(owner, empty.Member, csrf, new StaffServiceEndpoints.ServiceReference(definition.Id, definition.Version)),
            PostAsync(owner, $"/api/services/{definition.Id}/status", new { isActive = false, definition.Version }, csrf));
        Assert.Equal(HttpStatusCode.OK, race[1].StatusCode); Assert.True(race[0].StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
        var after = await ReadAsync(owner, member.Id); Assert.False(after.Items.Single(item => item.Id == definition.Id).IsActive);
        Assert.Equal(race[0].StatusCode == HttpStatusCode.OK ? 1 : 0, after.Selected.Length); foreach (var response in race) response.Dispose();
        await AssertOriginalStateAsync(app, seed);
    }
    [Fact]
    public async Task ImmediateAndCommitAuditFailuresRollBackEntireReplacementAndVersion()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var member = await CreateMemberAsync(owner); var first = await CreateServiceAsync(owner); var second = await CreateServiceAsync(owner, "B Hizmet"); var csrf = await GetCsrfAsync(owner);
        using var initial = await SaveAsync(owner, member, csrf, Ref(first)); var saved = await SelectionAsync(initial);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_assignment_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic assignment audit failure'; END; $$;
            CREATE TRIGGER reject_assignment_audit BEFORE INSERT ON "StaffServiceAssignmentAudits"
            FOR EACH ROW EXECUTE FUNCTION reject_assignment_audit();
            """, Token);
        foreach (var deferred in new[] { false, true })
        {
            if (deferred) await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_assignment_audit ON "StaffServiceAssignmentAudits";
                CREATE CONSTRAINT TRIGGER reject_assignment_audit AFTER INSERT ON "StaffServiceAssignmentAudits"
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_assignment_audit();
                """, Token);
            using var response = await SaveAsync(owner, saved.Member, csrf, Ref(second)); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(first.Id, (await db.StaffServiceAssignments.AsNoTracking().SingleAsync(Token)).ServiceDefinitionId);
            Assert.Equal(saved.Member.Version, (await db.StaffMembers.AsNoTracking().SingleAsync(Token)).Version);
            Assert.Single(await db.StaffServiceAssignmentAudits.ToArrayAsync(Token));
        }
    }
    [Fact]
    public async Task WaitingWriteRechecksRevokedOwnerBeforeChangingAnyLink()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var member = await CreateMemberAsync(owner); var service = await CreateServiceAsync(owner); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var locked = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {seed.OwnerId} FOR UPDATE").SingleAsync(Token);
        var waiting = SaveAsync(owner, member, csrf, Ref(service));
        await using var observer = new NpgsqlConnection(database.GetConnectionString()); await observer.OpenAsync(Token);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10); var blocked = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var query = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%AspNetUsers%FOR UPDATE%')", observer);
            blocked = (bool)(await query.ExecuteScalarAsync(Token) ?? false); if (blocked) break; await Task.Delay(25, Token);
        }
        Assert.True(blocked); Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().UpdateSecurityStampAsync(locked)).Succeeded);
        await transaction.CommitAsync(Token); using var result = await waiting; Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Empty(await db.StaffServiceAssignments.ToArrayAsync(Token)); Assert.Empty(await db.StaffServiceAssignmentAudits.ToArrayAsync(Token));
        Assert.Equal(member.Version, (await db.StaffMembers.AsNoTracking().SingleAsync(Token)).Version);
    }
    [Fact]
    public async Task MigrationAndForeignKeysPreserveDefinitionsAndPreventDuplicateOrOrphanLinks()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var member = await CreateMemberAsync(owner); var service = await CreateServiceAsync(owner);
        using var response = await SaveAsync(owner, member, await GetCsrfAsync(owner), Ref(service)); var saved = await SelectionAsync(response);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"StaffServiceAssignments\" VALUES ({member.Id}, {service.Id})", Token)); Assert.Equal("23505", duplicate.SqlState);
        var orphan = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"StaffServiceAssignments\" VALUES ({member.Id}, {Guid.NewGuid()})", Token)); Assert.Equal("23503", orphan.SqlState);
        var deletion = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"ServiceDefinitions\" WHERE \"Id\" = {service.Id}", Token)); Assert.Equal("23001", deletion.SqlState);
        var migrator = db.GetService<IMigrator>();
        var down = migrator.GenerateScript("20261003004248_StaffServiceAssignments", "20261002234539_ServiceDefinitions");
        await db.Database.ExecuteSqlRawAsync(down, Token);
        Assert.Equal(saved.Member.Version, (await db.StaffMembers.AsNoTracking().SingleAsync(Token)).Version);
        var retained = await db.ServiceDefinitions.AsNoTracking().SingleAsync(Token); Assert.Equal(service.Version, retained.Version); Assert.Equal(0.29m, retained.Price);
        await AssertOriginalStateAsync(app, seed); await migrator.MigrateAsync(cancellationToken: Token);
        Assert.Empty(await db.StaffServiceAssignments.ToArrayAsync(Token)); Assert.Empty(await db.StaffServiceAssignmentAudits.ToArrayAsync(Token));
    }
}
