using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Business;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.TestAccounts;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class BusinessProfileTests
{
    private const string ProfilePath = "/api/business-profile/";
    private static async Task<BusinessProfileEndpoints.ProfileResponse> ReadProfileAsync(HttpClient client)
    {
        using var response = await client.GetAsync(ProfilePath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<BusinessProfileEndpoints.ProfileResponse>(await response.Content
            .ReadFromJsonAsync<BusinessProfileEndpoints.ProfileResponse>(TestContext.Current.CancellationToken));
    }

    private static BusinessProfileEndpoints.UpdateRequest ProfileUpdate(Guid version) =>
        new("  Örnek Kuaför  ", "0 (212) 000-00-00", " salon@example.test ", " İstanbul\nÖrnek sokak ", version);

    private static async Task<HttpResponseMessage> SaveProfileAsync(HttpClient client, BusinessProfileEndpoints.UpdateRequest request) =>
        await PostAsync(client, ProfilePath, request, await GetCsrfAsync(client));

    [Fact]
    public async Task ProfileRequiresMfaOwnerForReadingAndWritingAndCsrfForSaving()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var anonymous = app.CreateClient();
        using var owner = await InviteOwnerAsync(seeded);
        var initial = await ReadProfileAsync(owner);
        using var pending = app.CreateClient();
        await PasswordStepAsync(pending);
        await CreatePasswordStaffAsync(app);
        using var staff = app.CreateClient();
        await LoginPasswordStaffAsync(staff);
        using var setupOnly = app.CreateClient();
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var setupUser = await users.FindByIdAsync(seeded.OtherUserId.ToString());
            Assert.NotNull(setupUser);
            Assert.True((await users.AddToRoleAsync(setupUser, "Owner")).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(setupUser, false)).Succeeded);
        }
        using var setupLogin = await PostAsync(setupOnly, "/api/auth/login",
            new { email = "other@example.test", password = Password }, await GetCsrfAsync(setupOnly));
        Assert.Equal(HttpStatusCode.NoContent, setupLogin.StatusCode);
        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized),
            (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden), (setupOnly, HttpStatusCode.Forbidden) })
        {
            using var read = await client.GetAsync(ProfilePath, TestContext.Current.CancellationToken);
            using var write = await SaveProfileAsync(client, ProfileUpdate(initial.Version));
            Assert.Equal(expected, read.StatusCode);
            Assert.Equal(expected, write.StatusCode);
        }
        using var noCsrf = await PostAsync(owner, ProfilePath, ProfileUpdate(initial.Version), null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal(initial, await ReadProfileAsync(owner));
    }

    [Fact]
    public async Task ProfileValidatesAndNormalizesFieldsPersistsAcrossAppRestartAndAuditsWithoutContactValues()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var owner = await InviteOwnerAsync(seeded);
        var initial = await ReadProfileAsync(owner);
        Assert.Equal("", initial.Name);
        foreach (var invalid in new[]
        {
            ProfileUpdate(initial.Version) with { Name = " " },
            ProfileUpdate(initial.Version) with { Name = "a" },
            ProfileUpdate(initial.Version) with { Name = new string('a', 151) },
            ProfileUpdate(initial.Version) with { Name = "abc\0" },
            ProfileUpdate(initial.Version) with { Phone = "+1 212 000 00 00" },
            ProfileUpdate(initial.Version) with { Phone = "0212letters0000000" },
            ProfileUpdate(initial.Version) with { Email = "invalid" },
            ProfileUpdate(initial.Version) with { Email = new string('a', 250) + "@test.test" },
            ProfileUpdate(initial.Version) with { Address = new string('a', 501) },
            ProfileUpdate(initial.Version) with { Version = Guid.Empty }
        })
        {
            using var denied = await SaveProfileAsync(owner, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
        Assert.Equal(initial, await ReadProfileAsync(owner));
        using var saved = await SaveProfileAsync(owner, ProfileUpdate(initial.Version));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var current = await ReadProfileAsync(owner);
        Assert.Equal("Örnek Kuaför", current.Name);
        Assert.Equal("+902120000000", current.Phone);
        Assert.Equal("salon@example.test", current.Email);
        Assert.Equal("İstanbul\nÖrnek sokak", current.Address);
        Assert.NotEqual(initial.Version, current.Version);
        await using var restarted = app.WithWebHostBuilder(_ => { });
        using var restartClient = restarted.CreateClient();
        await PasswordStepAsync(restartClient);
        await CompleteMfaAsync(restartClient, seeded.Key);
        Assert.Equal(current, await ReadProfileAsync(restartClient));
        using var cleared = await SaveProfileAsync(owner, new("Salon", " ", "", null, current.Version));
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var emptyContacts = await ReadProfileAsync(owner);
        Assert.Null(emptyContacts.Phone);
        Assert.Null(emptyContacts.Email);
        Assert.Null(emptyContacts.Address);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.BusinessProfiles.CountAsync(TestContext.Current.CancellationToken));
        var audits = await db.BusinessProfileAudits.ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, audits.Length);
        Assert.All(audits, audit => Assert.Equal(seeded.OwnerId, audit.ActorId));
        Assert.Contains(audits, audit => audit.ProfileVersion == current.Version);
        var entity = db.Model.FindEntityType(typeof(BusinessProfileAudit));
        Assert.NotNull(entity);
        Assert.Equal(new[] { "ActorId", "Id", "OccurredAt", "ProfileVersion" }, entity.GetProperties().Select(property => property.Name).Order().ToArray());
        await AssertOriginalStateAsync(app, seeded);
    }

    [Fact]
    public async Task ConcurrentProfileSavesOnlyAcceptOneVersionAndNeverCreateASecondProfile()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var first = await InviteOwnerAsync(seeded);
        using var second = await InviteOwnerAsync(seeded);
        var initial = await ReadProfileAsync(first);
        var firstCsrf = await GetCsrfAsync(first);
        var secondCsrf = await GetCsrfAsync(second);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {seeded.OwnerId} FOR UPDATE", TestContext.Current.CancellationToken);
        var firstSave = PostAsync(first, ProfilePath, ProfileUpdate(initial.Version) with { Name = "First Salon" }, firstCsrf);
        var secondSave = PostAsync(second, ProfilePath, ProfileUpdate(initial.Version) with { Name = "Second Salon" }, secondCsrf);
        await AwaitInviteLocksAsync(app);
        await blocker.CommitAsync(TestContext.Current.CancellationToken);
        using var firstResult = await firstSave;
        using var secondResult = await secondSave;
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, new[] { firstResult.StatusCode, secondResult.StatusCode }.Order().ToArray());
        var current = await ReadProfileAsync(first);
        Assert.Equal(firstResult.StatusCode == HttpStatusCode.OK ? "First Salon" : "Second Salon", current.Name);
        using var stale = await SaveProfileAsync(first, ProfileUpdate(initial.Version));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(current, await ReadProfileAsync(first));
        Assert.Equal(1, await db.BusinessProfileAudits.CountAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            db.BusinessProfiles.Add(new BusinessProfile { Id = 2, Name = "Another", Version = Guid.NewGuid() });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task ProfileAuditOrCommitFailureRollsBackAllChanges()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var owner = await InviteOwnerAsync(seeded);
        var initial = await ReadProfileAsync(owner);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_profile_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic profile failure'; END; $$;
            CREATE TRIGGER reject_profile_audit BEFORE INSERT ON "BusinessProfileAudits"
            FOR EACH ROW EXECUTE FUNCTION reject_profile_audit();
            """, TestContext.Current.CancellationToken);
        using var auditFailure = await SaveProfileAsync(owner, ProfileUpdate(initial.Version));
        Assert.Equal(HttpStatusCode.InternalServerError, auditFailure.StatusCode);
        Assert.Equal(initial, await ReadProfileAsync(owner));
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER reject_profile_audit ON "BusinessProfileAudits";
            CREATE CONSTRAINT TRIGGER reject_profile_audit AFTER INSERT ON "BusinessProfileAudits"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_profile_audit();
            """, TestContext.Current.CancellationToken);
        using var commitFailure = await SaveProfileAsync(owner, ProfileUpdate(initial.Version));
        Assert.Equal(HttpStatusCode.InternalServerError, commitFailure.StatusCode);
        Assert.Equal(initial, await ReadProfileAsync(owner));
        Assert.False(await db.BusinessProfileAudits.AnyAsync(TestContext.Current.CancellationToken));
        await AssertOriginalStateAsync(app, seeded);
    }

    [Fact]
    public async Task RevokedOwnerWaitingForAccountLockCannotSaveProfile()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var first = await InviteOwnerAsync(seeded);
        using var second = await InviteOwnerAsync(seeded);
        var initial = await ReadProfileAsync(first);
        var firstCsrf = await GetCsrfAsync(first);
        var secondCsrf = await GetCsrfAsync(second);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        var newStamp = Guid.NewGuid().ToString();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"AspNetUsers\" SET \"SecurityStamp\" = {newStamp} WHERE \"Id\" = {seeded.OwnerId}", TestContext.Current.CancellationToken);
        var firstSave = PostAsync(first, ProfilePath, ProfileUpdate(initial.Version), firstCsrf);
        var secondSave = PostAsync(second, ProfilePath, ProfileUpdate(initial.Version), secondCsrf);
        await AwaitInviteLocksAsync(app);
        await blocker.CommitAsync(TestContext.Current.CancellationToken);
        using var firstResult = await firstSave;
        using var secondResult = await secondSave;
        Assert.Equal(HttpStatusCode.Unauthorized, firstResult.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondResult.StatusCode);
        Assert.False(await db.BusinessProfileAudits.AnyAsync(TestContext.Current.CancellationToken));
        Assert.Equal(initial.Version, (await db.BusinessProfiles.SingleAsync(TestContext.Current.CancellationToken)).Version);
    }
}
