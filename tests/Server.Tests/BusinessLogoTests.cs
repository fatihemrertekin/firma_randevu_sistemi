using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Business;
using Server.Infrastructure;
using SkiaSharp;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class BusinessLogoTests
{
    private const string Path = "/api/business-logo/";
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static string Image(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 40, int height = 20, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(width, height); bitmap.Erase(color ?? SKColors.Teal);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(format, 100);
        return Convert.ToBase64String(data.ToArray());
    }
    private static async Task<BusinessLogoEndpoints.LogoResponse> Read(HttpClient client)
    {
        using var response = await client.GetAsync(Path, Token); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<BusinessLogoEndpoints.LogoResponse>(await response.Content.ReadFromJsonAsync<BusinessLogoEndpoints.LogoResponse>(Token));
    }
    private static Task<HttpResponseMessage> Save(HttpClient client, Guid version, string? image, string? csrf) => PostAsync(client, Path, new { version, image }, csrf);

    [Fact]
    public async Task PublicBrandingContainsOnlyLogoAndWritesRequireMfaOwnerAndCsrf()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); using var anonymous = app.CreateClient(); using var pending = app.CreateClient(); await PasswordStepAsync(pending);
        await CreatePasswordStaffAsync(app); using var staff = app.CreateClient(); await LoginPasswordStaffAsync(staff);
        var initial = await Read(anonymous); Assert.False(initial.HasLogo); Assert.Null(initial.ImageUrl); Assert.Null(initial.Width); Assert.Null(initial.Height);
        foreach (var (client, status) in new[] { (anonymous, HttpStatusCode.Unauthorized), (pending, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        { using var response = await Save(client, initial.Version, Image(), await GetCsrfAsync(client)); Assert.Equal(status, response.StatusCode); }
        foreach (var csrf in new string?[] { null, "invalid" })
        { using var response = await Save(owner, initial.Version, Image(), csrf); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
        using var empty = await anonymous.GetAsync($"{Path}image/{initial.Version}", Token); Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
        using var scope = app.Services.CreateScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessLogoAudits.ToArrayAsync(Token));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task InvalidFilesAndVersionsLeaveExistingLogoAndAuditUntouched()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner); var csrf = await GetCsrfAsync(owner);
        foreach (var invalid in new string?[] { null, "", "broken-base64", Convert.ToBase64String("<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray()),
            Convert.ToBase64String("<html>fake.png</html>"u8.ToArray()), Convert.ToBase64String(new byte[LogoImage.MaxInputBytes + 1]),
            Image(SKEncodedImageFormat.Webp), Image(width: 2049), Image(height: 2049), Convert.ToBase64String(Convert.FromBase64String(Image())[..30]) })
        {
            using var result = await Save(owner, initial.Version, invalid, csrf); Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
            var body = await result.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(Token); Assert.True(body.GetProperty("errors").TryGetProperty("image", out _));
        }
        using var version = await Save(owner, Guid.Empty, Image(), csrf); Assert.Equal(HttpStatusCode.BadRequest, version.StatusCode);
        Assert.Equal(initial, await Read(owner)); using var scope = app.Services.CreateScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessLogoAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task UploadReplaceAndNoOpExposeNormalizedPngAndPreserveOtherData()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.BusinessProfiles.AsNoTracking().SingleAsync(Token); var hours = await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(Token);
        using var first = await Save(owner, initial.Version, Image(SKEncodedImageFormat.Jpeg, 1024, 512), csrf); Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var saved = await Read(owner); Assert.True(saved.HasLogo); Assert.NotEqual(initial.Version, saved.Version); Assert.Equal(512, saved.Width); Assert.Equal(256, saved.Height);
        using var anonymous = app.CreateClient(); using var image = await anonymous.GetAsync(saved.ImageUrl, Token); Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType); Assert.Equal("nosniff", Assert.Single(image.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(image.Headers.CacheControl?.NoStore); var png = await image.Content.ReadAsByteArrayAsync(Token);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        using var repeated = await Save(owner, saved.Version, Image(SKEncodedImageFormat.Jpeg, 1024, 512), csrf); Assert.Equal(HttpStatusCode.OK, repeated.StatusCode); Assert.Equal(saved, await Read(owner));
        using var replace = await Save(owner, saved.Version, Image(color: SKColors.Purple), csrf); Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        var changed = await Read(owner); Assert.NotEqual(saved.Version, changed.Version);
        using var old = await anonymous.GetAsync(saved.ImageUrl, Token); Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.Equal(2, await db.BusinessLogoAudits.CountAsync(Token)); Assert.Equal(profile.Version, (await db.BusinessProfiles.AsNoTracking().SingleAsync(Token)).Version);
        Assert.Equal(hours.Version, (await db.BusinessHoursSchedules.AsNoTracking().SingleAsync(Token)).Version); await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task ParallelAndStaleWritesCannotLoseChangesOrDuplicateAudit()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var one = await InviteOwnerAsync(seed); using var two = await InviteOwnerAsync(seed); var initial = await Read(one);
        var csrfOne = await GetCsrfAsync(one); var csrfTwo = await GetCsrfAsync(two);
        var responses = await Task.WhenAll(Save(one, initial.Version, Image(), csrfOne), Save(two, initial.Version, Image(color: SKColors.Purple), csrfTwo));
        try { Assert.Single(responses, result => result.StatusCode == HttpStatusCode.OK); Assert.Single(responses, result => result.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var result in responses) result.Dispose(); }
        using var stale = await Save(one, initial.Version, Image(), csrfOne); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var scope = app.Services.CreateScope(); Assert.Single(await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessLogoAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task ImmediateAndDeferredAuditFailuresRollBackImageAndVersion()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_logo_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'synthetic logo audit failure'; END; $$;
            CREATE TRIGGER reject_logo_audit BEFORE INSERT ON "BusinessLogoAudits" FOR EACH ROW EXECUTE FUNCTION reject_logo_audit();
            """, Token);
        foreach (var deferred in new[] { false, true })
        {
            if (deferred) await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_logo_audit ON "BusinessLogoAudits";
                CREATE CONSTRAINT TRIGGER reject_logo_audit AFTER INSERT ON "BusinessLogoAudits" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_logo_audit();
                """, Token);
            using var result = await Save(owner, initial.Version, Image(), csrf); Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode); Assert.Equal(initial, await Read(owner));
            Assert.Empty(await db.BusinessLogoAudits.ToArrayAsync(Token));
        }
    }

    [Fact]
    public async Task WaitingWriteRechecksRevokedOwnerBeforeReplacingLogo()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner); var csrf = await GetCsrfAsync(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(Token);
        var locked = await db.Users.FromSqlInterpolated($"SELECT * FROM \"AspNetUsers\" WHERE \"Id\" = {seed.OwnerId} FOR UPDATE").SingleAsync(Token);
        var waiting = Save(owner, initial.Version, Image(), csrf);
        await using var observer = new NpgsqlConnection(database.GetConnectionString()); await observer.OpenAsync(Token);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10); var blocked = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await using var query = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND query LIKE '%AspNetUsers%FOR UPDATE%')", observer);
            blocked = (bool)(await query.ExecuteScalarAsync(Token) ?? false); if (blocked) break; await Task.Delay(25, Token);
        }
        Assert.True(blocked); Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().UpdateSecurityStampAsync(locked)).Succeeded);
        await transaction.CommitAsync(Token); using var response = await waiting; Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False((await db.BusinessLogos.AsNoTracking().SingleAsync(Token)).Png is not null); Assert.Empty(await db.BusinessLogoAudits.ToArrayAsync(Token));
    }

    [Fact]
    public async Task DatabaseConstraintsAndMigrationRoundTripProtectExistingIdentityAndDefinitions()
    {
        await using var database = RecoveryDatabase(); await database.StartAsync(Token);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString()); await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed); var initial = await Read(owner);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var sql in new[] {
            "UPDATE \"BusinessLogos\" SET \"Id\" = 2", "UPDATE \"BusinessLogos\" SET \"Width\" = 1",
            "UPDATE \"BusinessLogos\" SET \"Png\" = '\\x01'::bytea, \"Width\" = 513, \"Height\" = 1",
            "UPDATE \"BusinessLogos\" SET \"Png\" = '\\x'::bytea, \"Width\" = 1, \"Height\" = 1" })
        { var failure = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, Token)); Assert.Equal("23514", failure.SqlState); }
        using var saved = await Save(owner, initial.Version, Image(), await GetCsrfAsync(owner)); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261003172850_StaffWorkingHours", Token);
        await AssertOriginalStateAsync(app, seed); Assert.Single(await db.BusinessProfiles.AsNoTracking().ToArrayAsync(Token)); Assert.Single(await db.BusinessHoursSchedules.AsNoTracking().ToArrayAsync(Token));
        await migrator.MigrateAsync(cancellationToken: Token); await migrator.MigrateAsync(cancellationToken: Token);
        Assert.False((await Read(owner)).HasLogo); Assert.Empty(await db.BusinessLogoAudits.ToArrayAsync(Token));
    }
}
