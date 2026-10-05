using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Audit;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class AuditLogTests
{
    private const string Path = "/api/audit-log/";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static async Task<AuditLogEndpoints.Page> Read(HttpClient client, string query = "")
    {
        using var result = await client.GetAsync(Path + query, Token);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode); Assert.True(result.Headers.CacheControl?.NoStore);
        return Assert.IsType<AuditLogEndpoints.Page>(await result.Content.ReadFromJsonAsync<AuditLogEndpoints.Page>(Token));
    }

    [Fact]
    public async Task OnlyMfaOwnerCanReadAndRevokedSessionsCannotContinue()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var anonymous = app.CreateClient(); using var pending = app.CreateClient(); await PasswordStepAsync(pending);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        { using var result = await client.GetAsync(Path, Token); Assert.Equal(expected, result.StatusCode); }
        using var owner = await InviteOwnerAsync(seed); Assert.Empty((await Read(owner)).Items);
        using var scope = app.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByIdAsync(seed.OwnerId.ToString()); Assert.NotNull(user);
        Assert.True((await users.UpdateSecurityStampAsync(user)).Succeeded);
        using var revoked = await owner.GetAsync(Path, Token); Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task ExistingSourcesExposeOnlyDisplayFieldsAndReadsPreserveIdentityAndData()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedAsync(db, seed.OwnerId, seed.OtherUserId);
        var before = await FingerprintAsync(db);
        using var response = await owner.GetAsync(Path, Token); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Token);
        Assert.Equal(new[] { "category", "cursor", "items", "nextCursor", "timeZone" }, json.EnumerateObject().Select(property => property.Name).Order().ToArray());
        foreach (var entry in json.GetProperty("items").EnumerateArray())
            Assert.Equal(new[] { "action", "actor", "id", "module", "occurredAt", "target" }, entry.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain("SECRET", json.GetRawText());
        var page = await Read(owner); Assert.Equal(13, page.Items.Length); Assert.Null(page.NextCursor); Assert.Equal("Europe/Istanbul", page.TimeZone);
        Assert.Equal(12, page.Items.Select(item => item.Module).Distinct().Count());
        Assert.Equal(2, page.Items.Count(item => item.Actor == "Yerel bakım"));
        Assert.Contains(page.Items, item => item.Target == "Güncel personel · Güncel hizmet");
        Assert.Contains(page.Items, item => item.Target == "invite@example.test");
        Assert.Equal(7, (await Read(owner, "?category=definitions")).Items.Length);
        Assert.Equal(6, (await Read(owner, "?category=security")).Items.Length);
        Assert.Equal(before, await FingerprintAsync(db));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>(); var user = await users.FindByIdAsync(seed.OwnerId.ToString()); Assert.NotNull(user);
        Assert.Equal(seed.Stamp, user.SecurityStamp); Assert.Equal(seed.Key, await users.GetAuthenticatorKeyAsync(user));
        Assert.True(user.TwoFactorEnabled); Assert.Equal(8, await users.CountRecoveryCodesAsync(user));
    }

    [Fact]
    public async Task EqualTimeAndIdsAcrossSourcesPaginateWithoutDuplicatesWhileNewerWritesStayOutsideCursor()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedAsync(db, seed.OwnerId, seed.OtherUserId);
        var first = await Read(owner, "?pageSize=3"); var entries = first.Items.ToList(); var next = first.NextCursor;
        db.BusinessProfileAudits.Add(new() { Id = Guid.NewGuid(), ActorId = seed.OwnerId, ProfileVersion = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(Token);
        while (next is not null)
        { var page = await Read(owner, "?pageSize=3&cursor=" + next); entries.AddRange(page.Items); next = page.NextCursor; }
        Assert.Equal(13, entries.Count); Assert.Equal(13, entries.Select(item => item.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 13).Reverse(), entries.Select(item => int.Parse(item.Id.Split(':')[0])));
        Assert.Equal(first.Items, (await Read(owner, "?pageSize=3&cursor=" + first.Cursor)).Items);
        Assert.Equal(14, (await Read(owner)).Items.Length);
    }

    [Fact]
    public async Task InvalidAndForeignCategoryCursorsAreRejectedBeforeQuery()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed);
        var current = await Read(owner);
        var partial = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { Category = "all", AsOf = DateTimeOffset.UtcNow, BeforeAt = DateTimeOffset.UtcNow, Source = 14, Id = Guid.NewGuid() }));
        foreach (var query in new[] { "?category=other", "?pageSize=0", "?pageSize=51", "?cursor=invalid", "?cursor=" + new string('a', 513), "?cursor=" + partial, "?category=security&cursor=" + current.Cursor })
        { using var result = await owner.GetAsync(Path + query, Token); Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); }
        Assert.Empty((await Read(owner)).Items);
    }

    [Fact]
    public async Task IndexMigrationDownUpAndRepeatedUpPreserveAllRows()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedAsync(db, seed.OwnerId, seed.OtherUserId); var before = await FingerprintAsync(db);
        var migrator = db.GetService<IMigrator>();
        // Yalnız indeks migration'ını geri al; sonradan eklenen audit tablolarını düşürme.
        var down = migrator.GenerateScript("20261003200924_AuditLogIndexes", "20261003184120_BusinessLogo");
        await db.Database.ExecuteSqlRawAsync(down, Token);
        Assert.Equal(before, await FingerprintAsync(db)); Assert.Equal(13, (await Read(owner)).Items.Length);
        await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        Assert.Equal(before, await FingerprintAsync(db));
        Assert.Equal(13, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_indexes WHERE schemaname='public' AND indexname LIKE '%Audits_OccurredAt_Id'").SingleAsync(Token));
    }

    [Fact]
    public async Task AnonymousResetRequestDoesNotAttributeTheTargetOwnerAsAuthenticatedActor()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var at = DateTimeOffset.UtcNow.AddMinutes(-1);
        foreach (var kind in new[] { "SelfIssued", "Issued", "Completed" })
            db.OwnerPasswordResetAudits.Add(new() { Id = Guid.NewGuid(), OwnerId = seed.OwnerId, GrantId = Guid.NewGuid(), InstanceId = "synthetic", OperatorReference = "SECRET-operator", RequestReference = "SECRET-request", Kind = kind, OccurredAt = at, ExpiresAt = at.AddMinutes(30) });
        await db.SaveChangesAsync(Token); var before = await FingerprintAsync(db);
        var page = await Read(owner, "?category=security"); Assert.Equal(3, page.Items.Length);
        Assert.Contains(page.Items, item => item.Action == "Sıfırlama bağlantısı istendi" && item.Actor == "Oturum açılmadan");
        Assert.Contains(page.Items, item => item.Action == "Sıfırlama oluşturuldu" && item.Actor == "Yerel bakım");
        Assert.Contains(page.Items, item => item.Action == "Parola sıfırlandı" && item.Actor == Server.Tests.Support.TestAccounts.Email);
        Assert.All(page.Items, item => Assert.Equal(Server.Tests.Support.TestAccounts.Email, item.Target));
        Assert.Equal(before, await FingerprintAsync(db));
    }

    private static Task<string> FingerprintAsync(AppDbContext db)
    {
        var tables = db.Model.GetEntityTypes().Select(type => type.GetTableName()).Where(name => name is not null).Distinct().Order();
        var parts = tables.Select(table => "SELECT '" + table + "' AS name, coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text, '') AS data FROM \"" + table + "\" t");
        var sql = "SELECT md5(string_agg(data, ',' ORDER BY name)) AS \"Value\" FROM (" + string.Join(" UNION ALL ", parts) + ") all_data";
        return db.Database.SqlQueryRaw<string>(sql).SingleAsync(Token);
    }
    internal static async Task SeedAsync(AppDbContext db, Guid owner, Guid other)
    {
        var id = Guid.Parse("f1111111-1111-4111-8111-111111111111"); var at = DateTimeOffset.UtcNow.AddHours(-1); var version = Guid.NewGuid();
        var member = new StaffMember { Id = Guid.NewGuid(), Name = "Güncel personel", Version = version };
        var service = new ServiceDefinition { Id = Guid.NewGuid(), Name = "Güncel hizmet", DurationMinutes = 30, Price = 100, Currency = "TRY", Version = version };
        var invitation = new StaffInvitation { Id = Guid.NewGuid(), IssuedById = owner, InstanceId = "SECRET-instance", Email = "invite@example.test", NormalizedEmail = "INVITE@EXAMPLE.TEST", TokenHash = new string('A', 64), ExpiresAt = at.AddDays(1) };
        db.AddRange(member, service, invitation); await db.SaveChangesAsync(Token);
        db.AddRange(
            new BusinessProfileAudit { Id = id, ActorId = owner, ProfileVersion = version, OccurredAt = at },
            new BusinessLogoAudit { Id = id, ActorId = owner, LogoVersion = version, OccurredAt = at },
            new BusinessHoursAudit { Id = id, ActorId = owner, ScheduleVersion = version, OccurredAt = at },
            new StaffMemberAudit { Id = id, ActorId = owner, StaffMemberId = member.Id, Kind = "Created", MemberVersion = version, OccurredAt = at },
            new ServiceDefinitionAudit { Id = id, ActorId = owner, ServiceDefinitionId = service.Id, Kind = "Updated", ServiceVersion = version, OccurredAt = at },
            new StaffServiceAssignmentAudit { Id = id, ActorId = owner, StaffMemberId = member.Id, ServiceDefinitionId = service.Id, Kind = "Assigned", MemberVersion = version, OccurredAt = at },
            new StaffHoursAudit { Id = id, ActorId = owner, StaffMemberId = member.Id, MemberVersion = version, OccurredAt = at },
            new StaffInvitationAudit { Id = id, ActorId = owner, InvitationId = invitation.Id, Kind = "Issued", OccurredAt = at },
            new StaffPasswordResetAudit { Id = id, ActorId = owner, StaffId = other, GrantId = Guid.NewGuid(), InstanceId = "SECRET-instance", Kind = "Issued", OccurredAt = at, ExpiresAt = at.AddHours(1) },
            new StaffDeactivationAudit { Id = id, ActorId = owner, StaffId = other, OccurredAt = at },
            new StaffActivationAudit { Id = id, ActorId = owner, StaffId = other, OccurredAt = at },
            new OwnerPasswordResetAudit { Id = id, OwnerId = owner, GrantId = Guid.NewGuid(), InstanceId = "SECRET-instance", OperatorReference = "SECRET-operator", RequestReference = "SECRET-request", Kind = "Issued", OccurredAt = at, ExpiresAt = at.AddHours(1) },
            new OwnerMfaRecoveryAudit { Id = id, OwnerId = owner, InstanceId = "SECRET-instance", OperatorReference = "SECRET-operator", RequestReference = "SECRET-request", OccurredAt = at });
        await db.SaveChangesAsync(Token);
    }
}
