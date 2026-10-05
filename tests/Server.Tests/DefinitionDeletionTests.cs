using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Audit;
using Server.Features.Services;
using Server.Features.Staff;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class DefinitionDeletionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static string Path(bool service) => service ? "/api/services/" : "/api/staff-members/";
    private static async Task<(Guid Id, Guid Version)> Create(HttpClient owner, bool service)
    {
        var id = Guid.NewGuid();
        using var response = await PostAsync(owner, Path(service), service
            ? (object)new { id, name = "Sentetik hizmet", durationMinutes = 30, price = "350.00" }
            : new { id, name = "Sentetik personel" }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        if (service)
        {
            var row = Assert.IsType<ServiceDefinitionEndpoints.ServiceResponse>(await response.Content.ReadFromJsonAsync<ServiceDefinitionEndpoints.ServiceResponse>(Token));
            return (row.Id, row.Version);
        }
        var member = Assert.IsType<StaffMemberEndpoints.MemberResponse>(await response.Content.ReadFromJsonAsync<StaffMemberEndpoints.MemberResponse>(Token));
        return (member.Id, member.Version);
    }
    private static Task<HttpResponseMessage> Delete(HttpClient client, bool service, Guid id, Guid version, string? csrf) =>
        PostAsync(client, Path(service) + id + "/delete", new { version }, csrf);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteRequiresOwnerCsrfVersionAndRejectsStaleWrites(bool service)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var anonymous = app.CreateClient();
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        var row = await Create(owner, service); var csrf = await GetCsrfAsync(owner);
        using var unauthenticated = await Delete(anonymous, service, row.Id, row.Version, await GetCsrfAsync(anonymous));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using var unauthorized = await Delete(staff, service, row.Id, row.Version, await GetCsrfAsync(staff));
        Assert.Equal(HttpStatusCode.Forbidden, unauthorized.StatusCode);
        using var noCsrf = await Delete(owner, service, row.Id, row.Version, null); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var noVersion = await Delete(owner, service, row.Id, Guid.Empty, csrf); Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
        using var stale = await Delete(owner, service, row.Id, Guid.NewGuid(), csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var missing = await Delete(owner, service, Guid.NewGuid(), row.Version, csrf); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var replies = await Task.WhenAll(Delete(owner, service, row.Id, row.Version, csrf), Delete(owner, service, row.Id, row.Version, csrf));
        foreach (var reply in replies) { using (reply) Assert.Equal(HttpStatusCode.NoContent, reply.StatusCode); }
        using var read = await owner.GetAsync(Path(service) + row.Id, Token); Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        using var revive = await PostAsync(owner, Path(service) + row.Id + "/status", new { isActive = true, row.Version }, csrf);
        Assert.Equal(HttpStatusCode.NotFound, revive.StatusCode);
        using var replay = await PostAsync(owner, Path(service), service
            ? (object)new { id = row.Id, name = "Sentetik hizmet", durationMinutes = 30, price = "350.00" }
            : new { id = row.Id, name = "Sentetik personel" }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (service) Assert.Single(await db.ServiceDefinitionAudits.Where(a => a.Kind == "Deleted").ToArrayAsync(Token));
        else Assert.Single(await db.StaffMemberAudits.Where(a => a.Kind == "Deleted").ToArrayAsync(Token));
        var invalidStateSql = service ? "UPDATE \"ServiceDefinitions\" SET \"IsActive\"=TRUE WHERE \"IsDeleted\"" : "UPDATE \"StaffMembers\" SET \"IsActive\"=TRUE WHERE \"IsDeleted\"";
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(invalidStateSql, Token));
        Assert.Equal("23514", invalid.SqlState);
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task DeletionHidesDefinitionsAndSelectionsButPreservesHoursLinksAccountsAndHistory()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var accountId = await CreatePasswordStaffAsync(app);
        using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        var member = await Create(owner, false); var other = await Create(owner, false); var service = await Create(owner, true);
        var csrf = await GetCsrfAsync(owner);
        using var assignment = await PostAsync(owner, $"/api/staff-members/{member.Id}/services", new
        { member.Version, services = new[] { new { service.Id, service.Version } } }, csrf);
        Assert.Equal(HttpStatusCode.OK, assignment.StatusCode);
        var saved = Assert.IsType<StaffServiceEndpoints.SelectionResponse>(await assignment.Content.ReadFromJsonAsync<StaffServiceEndpoints.SelectionResponse>(Token));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.StaffWorkingDays.Add(new StaffWorkingDay { StaffMemberId = member.Id, Day = 0, IsClosed = true }); await db.SaveChangesAsync(Token);
        var account = await db.Users.AsNoTracking().SingleAsync(u => u.Id == accountId, Token);
        using var deletedService = await Delete(owner, true, service.Id, service.Version, csrf); Assert.Equal(HttpStatusCode.NoContent, deletedService.StatusCode);
        using var selections = await owner.GetAsync($"/api/staff-members/{member.Id}/services", Token);
        var visible = Assert.IsType<StaffServiceEndpoints.SelectionPage>(await selections.Content.ReadFromJsonAsync<StaffServiceEndpoints.SelectionPage>(Token));
        Assert.Empty(visible.Items); Assert.Empty(visible.Selected);
        using var forbiddenSelection = await PostAsync(owner, $"/api/staff-members/{other.Id}/services", new
        { other.Version, services = new[] { new { service.Id, service.Version } } }, csrf);
        Assert.Equal(HttpStatusCode.NotFound, forbiddenSelection.StatusCode);
        using var noChange = await PostAsync(owner, $"/api/staff-members/{member.Id}/services", new { version = saved.Member.Version, services = Array.Empty<object>() }, csrf);
        Assert.Equal(HttpStatusCode.OK, noChange.StatusCode); Assert.Single(await db.StaffServiceAssignments.ToArrayAsync(Token));
        using var deletedMember = await Delete(owner, false, member.Id, saved.Member.Version, csrf); Assert.Equal(HttpStatusCode.NoContent, deletedMember.StatusCode);
        foreach (var suffix in new[] { "", "/hours", "/services" })
        { using var gone = await owner.GetAsync($"/api/staff-members/{member.Id}{suffix}", Token); Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode); }
        using var memberList = await owner.GetAsync("/api/staff-members/", Token);
        var list = Assert.IsType<StaffMemberEndpoints.MemberPage>(await memberList.Content.ReadFromJsonAsync<StaffMemberEndpoints.MemberPage>(Token));
        Assert.Equal(other.Id, Assert.Single(list.Items).Id);
        using var serviceList = await owner.GetAsync("/api/services/", Token);
        Assert.Empty(Assert.IsType<ServiceDefinitionEndpoints.ServicePage>(await serviceList.Content.ReadFromJsonAsync<ServiceDefinitionEndpoints.ServicePage>(Token)).Items);
        Assert.Single(await db.StaffWorkingDays.ToArrayAsync(Token)); Assert.Single(await db.StaffServiceAssignments.ToArrayAsync(Token));
        var retainedMember = await db.StaffMembers.AsNoTracking().SingleAsync(m => m.Id == member.Id, Token);
        var retainedService = await db.ServiceDefinitions.AsNoTracking().SingleAsync(s => s.Id == service.Id, Token);
        Assert.True(retainedMember.IsDeleted); Assert.False(retainedMember.IsActive); Assert.Equal("Sentetik personel", retainedMember.Name);
        Assert.True(retainedService.IsDeleted); Assert.False(retainedService.IsActive); Assert.Equal(350m, retainedService.Price);
        var unchanged = await db.Users.AsNoTracking().SingleAsync(u => u.Id == accountId, Token);
        Assert.True(unchanged.IsActive); Assert.Equal(account.SecurityStamp, unchanged.SecurityStamp); Assert.Equal(account.PasswordHash, unchanged.PasswordHash);
        using var session = await staff.GetAsync("/api/auth/me", Token); Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        using var history = await owner.GetAsync("/api/audit-log/", Token);
        var entries = Assert.IsType<AuditLogEndpoints.Page>(await history.Content.ReadFromJsonAsync<AuditLogEndpoints.Page>(Token)).Items;
        Assert.Contains(entries, e => e.Action == "Silindi" && e.Target == "Sentetik personel");
        Assert.Contains(entries, e => e.Action == "Silindi" && e.Target == "Sentetik hizmet");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuditInsertAndCommitFailuresRollBackDeletion(bool service)
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var row = await Create(owner, service); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = service ? "ServiceDefinitionAudits" : "StaffMemberAudits";
        // Tablo adı yalnız yukarıdaki iki sabit test tablosundan gelir.
        var insertionFailureSql = """
            CREATE FUNCTION reject_deletion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW."Kind" = 'Deleted' THEN RAISE EXCEPTION 'synthetic deletion failure'; END IF; RETURN NEW; END; $$;
            CREATE TRIGGER reject_deletion BEFORE INSERT ON "__AUDIT_TABLE__" FOR EACH ROW EXECUTE FUNCTION reject_deletion();
            """.Replace("__AUDIT_TABLE__", table, StringComparison.Ordinal);
        await db.Database.ExecuteSqlRawAsync(insertionFailureSql, Token);
        using var insertion = await Delete(owner, service, row.Id, row.Version, csrf); Assert.Equal(HttpStatusCode.InternalServerError, insertion.StatusCode);
        var commitFailureSql = """
            DROP TRIGGER reject_deletion ON "__AUDIT_TABLE__";
            CREATE CONSTRAINT TRIGGER reject_deletion AFTER INSERT ON "__AUDIT_TABLE__"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_deletion();
            """.Replace("__AUDIT_TABLE__", table, StringComparison.Ordinal);
        await db.Database.ExecuteSqlRawAsync(commitFailureSql, Token);
        using var commit = await Delete(owner, service, row.Id, row.Version, csrf); Assert.Equal(HttpStatusCode.InternalServerError, commit.StatusCode);
        using var read = await owner.GetAsync(Path(service) + row.Id, Token); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        if (service)
        {
            var item = await db.ServiceDefinitions.AsNoTracking().SingleAsync(Token); Assert.False(item.IsDeleted); Assert.True(item.IsActive); Assert.Equal(row.Version, item.Version);
            Assert.Empty(await db.ServiceDefinitionAudits.Where(a => a.Kind == "Deleted").ToArrayAsync(Token));
        }
        else
        {
            var item = await db.StaffMembers.AsNoTracking().SingleAsync(Token); Assert.False(item.IsDeleted); Assert.True(item.IsActive); Assert.Equal(row.Version, item.Version);
            Assert.Empty(await db.StaffMemberAudits.Where(a => a.Kind == "Deleted").ToArrayAsync(Token));
        }
    }
}
