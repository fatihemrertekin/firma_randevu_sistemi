using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Audit;
using Server.Features.Identity;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;
using static Server.Tests.Support.TestAccounts;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class StaffActivationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Task<HttpResponseMessage> Change(HttpClient client, Guid id, string version, string? csrf, bool active = true) =>
        PostAsync(client, $"/api/staff-accounts/{id}/{(active ? "activate" : "deactivate")}", new { version }, csrf);

    private static async Task<StaffAccountEndpoints.StaffAccount> Read(HttpClient owner)
    {
        using var response = await owner.GetAsync("/api/staff-accounts/", Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<StaffAccountEndpoints.StaffPage>(Token);
        return Assert.Single(Assert.IsType<StaffAccountEndpoints.StaffPage>(page).Items);
    }

    [Fact]
    public async Task ActivationRequiresFreshMfaOwnerCsrfVersionAndStaffOnlyTarget()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var anonymous = app.CreateClient(); using var pending = app.CreateClient(); await PasswordStepAsync(pending);
        using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        var row = await Read(owner);
        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        {
            using var denied = await Change(client, id, row.Version, await GetCsrfAsync(client));
            Assert.Equal(expected, denied.StatusCode);
        }
        using var noCsrf = await Change(owner, id, row.Version, null); Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var empty = await Change(owner, id, "", await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var dual = await users.FindByIdAsync(seed.OwnerId.ToString()); Assert.NotNull(dual);
        Assert.True((await users.AddToRoleAsync(dual, "Staff")).Succeeded);
        foreach (var target in new[] { seed.OwnerId, seed.OtherUserId, Guid.NewGuid() })
        {
            using var protectedTarget = await Change(owner, target, row.Version, await GetCsrfAsync(owner));
            Assert.Equal(HttpStatusCode.NotFound, protectedTarget.StatusCode);
        }
        using var disable = await Change(owner, id, row.Version, await GetCsrfAsync(owner), false); Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        using var stale = await Change(owner, id, row.Version, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.False((await Read(owner)).IsActive); Assert.Empty(await db.StaffActivationAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ReactivationPreservesPasswordAndOwnerWhileOldCookiesAndResetCodesStayRevoked()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app); using var owner = await InviteOwnerAsync(seed);
        using var oldSession = app.CreateClient(); await LoginPasswordStaffAsync(oldSession);
        using var issued = await PostAsync(owner, "/api/staff-password-resets/", new { email = StaffEmail, verifiedRecipient = true }, await GetCsrfAsync(owner));
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var code = Assert.IsType<StaffPasswordResetEndpoints.IssuedResponse>(await issued.Content.ReadFromJsonAsync<StaffPasswordResetEndpoints.IssuedResponse>(Token));
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.Users.AsNoTracking().SingleAsync(user => user.Id == id, Token);
        using var disable = await Change(owner, id, (await Read(owner)).Version, await GetCsrfAsync(owner), false); Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        var inactive = await db.Users.AsNoTracking().SingleAsync(user => user.Id == id, Token);
        using var enable = await Change(owner, id, (await Read(owner)).Version, await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);
        var after = await db.Users.AsNoTracking().SingleAsync(user => user.Id == id, Token);
        Assert.True(after.IsActive); Assert.Equal(before.PasswordHash, after.PasswordHash); Assert.Equal(before.LockoutEnd, after.LockoutEnd);
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp); Assert.NotEqual(inactive.SecurityStamp, after.SecurityStamp);
        using var revoked = await oldSession.GetAsync("/api/auth/me", Token); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var fresh = app.CreateClient(); await LoginPasswordStaffAsync(fresh);
        using var anonymousReset = app.CreateClient();
        using var reset = await PostAsync(anonymousReset, "/api/staff-password-resets/complete", new { token = code.Token, newPassword = NewPassword, confirmPassword = NewPassword }, await GetCsrfAsync(anonymousReset));
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
        Assert.Equal(before.PasswordHash, (await db.Users.AsNoTracking().SingleAsync(user => user.Id == id, Token)).PasswordHash);
        var unchangedOwner = await db.Users.AsNoTracking().SingleAsync(user => user.Id == seed.OwnerId, Token);
        Assert.True(unchangedOwner.IsActive); Assert.True(unchangedOwner.TwoFactorEnabled); Assert.Equal(seed.Stamp, unchangedOwner.SecurityStamp);
        var audit = Assert.Single(await db.StaffActivationAudits.AsNoTracking().ToArrayAsync(Token));
        Assert.Equal(id, audit.StaffId); Assert.Equal(seed.OwnerId, audit.ActorId);
        using var log = await owner.GetAsync("/api/audit-log/?category=security", Token); Assert.Equal(HttpStatusCode.OK, log.StatusCode);
        var page = Assert.IsType<AuditLogEndpoints.Page>(await log.Content.ReadFromJsonAsync<AuditLogEndpoints.Page>(Token));
        Assert.Contains(page.Items, item => item.Module == "Çalışan erişimi" && item.Action == "Hesap etkinleştirildi" && item.Target == StaffEmail);
    }

    [Fact]
    public async Task ConcurrentActivationCreatesOneAuditAndOldActivationCannotUndoLaterDeactivation()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app); using var owner = await InviteOwnerAsync(seed);
        var csrf = await GetCsrfAsync(owner);
        using var disable = await Change(owner, id, (await Read(owner)).Version, csrf, false); Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        var row = await Read(owner);
        var responses = await Task.WhenAll(Change(owner, id, row.Version, csrf), Change(owner, id, row.Version, csrf));
        foreach (var response in responses) { using (response) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); }
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.StaffActivationAudits.CountAsync(Token));
        using var disabledAgain = await Change(owner, id, (await Read(owner)).Version, csrf, false); Assert.Equal(HttpStatusCode.NoContent, disabledAgain.StatusCode);
        using var stale = await Change(owner, id, row.Version, csrf); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.False((await Read(owner)).IsActive); Assert.Equal(1, await db.StaffActivationAudits.CountAsync(Token));
    }

    [Fact]
    public async Task AuditInsertAndDeferredCommitFailureRollBackActivationAndStamps()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        var id = await CreatePasswordStaffAsync(app); using var owner = await InviteOwnerAsync(seed);
        var csrf = await GetCsrfAsync(owner);
        using var disable = await Change(owner, id, (await Read(owner)).Version, csrf, false); Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.Users.AsNoTracking().SingleAsync(user => user.Id == id, Token); var row = await Read(owner);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_activation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic activation failure'; END; $$;
            CREATE TRIGGER reject_activation BEFORE INSERT ON "StaffActivationAudits"
            FOR EACH ROW EXECUTE FUNCTION reject_activation();
            """, Token);
        using var insertFailure = await Change(owner, id, row.Version, csrf); Assert.Equal(HttpStatusCode.InternalServerError, insertFailure.StatusCode);
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER reject_activation ON "StaffActivationAudits";
            CREATE CONSTRAINT TRIGGER reject_activation AFTER INSERT ON "StaffActivationAudits"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_activation();
            """, Token);
        using var commitFailure = await Change(owner, id, row.Version, csrf); Assert.Equal(HttpStatusCode.InternalServerError, commitFailure.StatusCode);
        var after = await db.Users.AsNoTracking().SingleAsync(user => user.Id == id, Token);
        Assert.False(after.IsActive); Assert.Equal(before.SecurityStamp, after.SecurityStamp); Assert.Equal(before.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.Empty(await db.StaffActivationAudits.ToArrayAsync(Token));
    }
}
