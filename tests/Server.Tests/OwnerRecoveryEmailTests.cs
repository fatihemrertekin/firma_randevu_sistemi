using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Server.Features.Identity;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.TestAccounts;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.PasswordTestSupport;
using static Server.Tests.Support.StaffTestSupport;
using static Server.Tests.Support.EmailTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class OwnerRecoveryEmailTests
{
    private const string VerifyEmailPath = "/api/auth/recovery-email/confirm";
    private sealed class EmailDelivery : IOwnerEmailVerificationDelivery
    {
        public List<string> Tokens { get; } = [];
        public bool Fails { get; set; }
        public bool CanDeliver(string email) => true;
        public Task DeliverAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            Tokens.Add(token);
            if (Fails) throw new IOException("Synthetic delivery failure");
            return Task.CompletedTask;
        }
    }

    private static WebApplicationFactory<Program> EmailApp(RecoverySeed seed, EmailDelivery delivery) =>
        seed.App.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IOwnerEmailVerificationDelivery>(delivery)));
    private static async Task<HttpResponseMessage> VerifyEmailAsync(HttpClient client, string token) =>
        await PostAsync(client, VerifyEmailPath, new { token }, await GetCsrfAsync(client));
    private static async Task<OwnerRecoveryEmailEndpoints.StatusResponse> EmailStatusAsync(HttpClient client)
    {
        using var response = await client.GetAsync(RecoveryEmailPath, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return Assert.IsType<OwnerRecoveryEmailEndpoints.StatusResponse>(await response.Content
            .ReadFromJsonAsync<OwnerRecoveryEmailEndpoints.StatusResponse>(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RecoveryEmailRequiresMfaOwnerCsrfAndDoesNotTrustLegacyEmailConfirmed()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seed.App;
        using var owner = await InviteOwnerAsync(seed);
        var status = await EmailStatusAsync(owner);
        Assert.Null(status.VerifiedAt);
        Assert.False(status.DeliveryAvailable);
        using var disabled = await RequestEmailAsync(owner);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabled.StatusCode);
        using var anonymous = app.CreateClient();
        using var setup = app.CreateClient();
        await PasswordStepAsync(setup);
        using var staff = app.CreateClient();
        var invite = await InviteAsync(owner);
        using var accepted = await AcceptInviteAsync(staff, invite.Token, await GetCsrfAsync(staff));
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        using var staffLogin = await PostAsync(staff, "/api/auth/login", new { email = StaffEmail, password = Password }, await GetCsrfAsync(staff));
        Assert.Equal(HttpStatusCode.NoContent, staffLogin.StatusCode);
        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized),
            (setup, HttpStatusCode.Unauthorized), (staff, HttpStatusCode.Forbidden) })
        {
            using var read = await client.GetAsync(RecoveryEmailPath, TestContext.Current.CancellationToken);
            using var write = await RequestEmailAsync(client);
            Assert.Equal(expected, read.StatusCode);
            Assert.Equal(expected, write.StatusCode);
        }
        using var noCsrf = await PostAsync(owner, RecoveryEmailPath + "request", new { }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var setupUser = await users.FindByIdAsync(seed.OtherUserId.ToString());
        Assert.NotNull(setupUser);
        Assert.True((await users.AddToRoleAsync(setupUser, "Owner")).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(setupUser, false)).Succeeded);
        using var setupOwner = app.CreateClient();
        using var setupLogin = await PostAsync(setupOwner, "/api/auth/login", new { email = "other@example.test", password = Password }, await GetCsrfAsync(setupOwner));
        Assert.Equal(HttpStatusCode.NoContent, setupLogin.StatusCode);
        using var setupDenied = await RequestEmailAsync(setupOwner);
        Assert.Equal(HttpStatusCode.Forbidden, setupDenied.StatusCode);
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().OwnerRecoveryEmails.AnyAsync(TestContext.Current.CancellationToken));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task VerificationStoresOnlyHashesNeedsExplicitPostIsSingleUseAndPreservesIdentityAcrossRestart()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var original = seed.App;
        var delivery = new EmailDelivery();
        await using var app = EmailApp(seed, delivery);
        using var owner = await InviteOwnerAsync(seed with { App = app });
        using var requested = await RequestEmailAsync(owner);
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var token = Assert.Single(delivery.Tokens);
        Assert.Equal(43, token.Length);
        Assert.DoesNotContain(token, await requested.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.OwnerRecoveryEmails.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(seed.OwnerId, pending.OwnerId);
        Assert.Equal(64, pending.TokenHash?.Length);
        Assert.DoesNotContain(token, pending.TokenHash ?? "");
        Assert.Null((await EmailStatusAsync(owner)).VerifiedAt);
        using var repeated = await RequestEmailAsync(owner);
        Assert.Equal(HttpStatusCode.TooManyRequests, repeated.StatusCode);
        using var anonymous = app.CreateClient();
        using var noCsrf = await PostAsync(anonymous, VerifyEmailPath, new { token }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var tampered = await VerifyEmailAsync(anonymous, new string('a', 43));
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        using var verified = await VerifyEmailAsync(anonymous, token);
        Assert.Equal(HttpStatusCode.NoContent, verified.StatusCode);
        Assert.NotNull((await EmailStatusAsync(owner)).VerifiedAt);
        using var replay = await VerifyEmailAsync(anonymous, token);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var already = await RequestEmailAsync(owner);
        Assert.Equal(HttpStatusCode.Conflict, already.StatusCode);
        await using var restart = app.WithWebHostBuilder(_ => { });
        using var restartedOwner = await InviteOwnerAsync(seed with { App = restart });
        Assert.NotNull((await EmailStatusAsync(restartedOwner)).VerifiedAt);
        Assert.Null((await db.OwnerRecoveryEmails.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).TokenHash);
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task VerificationRejectsWrongInstanceExpiryReplacedLinkAndChangedAccountBinding()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var clock = new ResetClock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock);
        await using var original = seed.App;
        var delivery = new EmailDelivery();
        await using var app = EmailApp(seed, delivery);
        using var owner = await InviteOwnerAsync(seed with { App = app });
        using var requested = await RequestEmailAsync(owner);
        Assert.Equal(HttpStatusCode.OK, requested.StatusCode);
        var first = Assert.Single(delivery.Tokens);
        await using var wrongInstance = app.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:InstanceId"] = "other-firma" })));
        using var wrongClient = wrongInstance.CreateClient();
        using var wrong = await VerifyEmailAsync(wrongClient, first);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var anonymous = app.CreateClient();
        clock.Advance(TimeSpan.FromMinutes(30));
        using var expired = await VerifyEmailAsync(anonymous, first);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        using var again = await RequestEmailAsync(owner);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var second = delivery.Tokens[1];
        using var old = await VerifyEmailAsync(anonymous, first);
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AspNetUsers\" SET \"NormalizedEmail\" = 'changed@example.test' WHERE \"Id\" = {seed.OwnerId}", TestContext.Current.CancellationToken);
        using var addressChanged = await VerifyEmailAsync(anonymous, second);
        Assert.Equal(HttpStatusCode.BadRequest, addressChanged.StatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AspNetUsers\" SET \"NormalizedEmail\" = {Email.ToUpperInvariant()}, \"SecurityStamp\" = 'changed-stamp' WHERE \"Id\" = {seed.OwnerId}", TestContext.Current.CancellationToken);
        using var stampChanged = await VerifyEmailAsync(anonymous, second);
        Assert.Equal(HttpStatusCode.BadRequest, stampChanged.StatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AspNetUsers\" SET \"SecurityStamp\" = {seed.Stamp} WHERE \"Id\" = {seed.OwnerId}", TestContext.Current.CancellationToken);
        Assert.Null((await EmailStatusAsync(owner)).VerifiedAt);
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task VerificationRollsBackOnCommitFailureAndConcurrentRequestsAndConfirmationsHaveOneWinner()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var original = seed.App;
        var delivery = new EmailDelivery();
        await using var app = EmailApp(seed, delivery);
        using var first = await InviteOwnerAsync(seed with { App = app });
        using var second = await InviteOwnerAsync(seed with { App = app });
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstCsrf = await GetCsrfAsync(first);
        var secondCsrf = await GetCsrfAsync(second);
        await using (var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {seed.OwnerId} FOR UPDATE", TestContext.Current.CancellationToken);
            var one = PostAsync(first, RecoveryEmailPath + "request", new { }, firstCsrf);
            var two = PostAsync(second, RecoveryEmailPath + "request", new { }, secondCsrf);
            await AwaitInviteLocksAsync(app);
            await blocker.CommitAsync(TestContext.Current.CancellationToken);
            var responses = await Task.WhenAll(one, two);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.TooManyRequests);
            foreach (var response in responses) response.Dispose();
        }
        var token = Assert.Single(delivery.Tokens);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_email_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic commit failure'; END; $$;
            CREATE CONSTRAINT TRIGGER reject_email_commit AFTER UPDATE ON "OwnerRecoveryEmails"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_email_commit();
            """, TestContext.Current.CancellationToken);
        using var failure = await VerifyEmailAsync(first, token);
        Assert.Equal(HttpStatusCode.InternalServerError, failure.StatusCode);
        Assert.Null((await EmailStatusAsync(first)).VerifiedAt);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_email_commit ON \"OwnerRecoveryEmails\"; DROP FUNCTION reject_email_commit();", TestContext.Current.CancellationToken);
        firstCsrf = await GetCsrfAsync(first);
        secondCsrf = await GetCsrfAsync(second);
        await using (var blocker = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {seed.OwnerId} FOR UPDATE", TestContext.Current.CancellationToken);
            var one = PostAsync(first, VerifyEmailPath, new { token }, firstCsrf);
            var two = PostAsync(second, VerifyEmailPath, new { token }, secondCsrf);
            await AwaitInviteLocksAsync(app);
            await blocker.CommitAsync(TestContext.Current.CancellationToken);
            var responses = await Task.WhenAll(one, two);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            foreach (var response in responses) response.Dispose();
        }
        Assert.NotNull((await EmailStatusAsync(first)).VerifiedAt);
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task FailedDeliveryNeverReturnsSuccessAndInvalidatesItsToken()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var original = seed.App;
        var delivery = new EmailDelivery { Fails = true };
        await using var app = EmailApp(seed, delivery);
        using var owner = await InviteOwnerAsync(seed with { App = app });
        using var failure = await RequestEmailAsync(owner);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        using var rejected = await VerifyEmailAsync(owner, Assert.Single(delivery.Tokens));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var scope = app.Services.CreateScope();
        var record = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OwnerRecoveryEmails.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(record.TokenHash);
        Assert.Null(record.VerifiedAt);
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public void LocalEmailDeliveryDefaultsOffAndCannotBeEnabledInProduction()
    {
        var empty = new ConfigurationBuilder().Build();
        Assert.False(new LocalOwnerEmailVerificationDelivery(empty, new EmailEnvironment()).CanDeliver(Email));
        var local = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RecoveryEmail:LocalPickupDirectory"] = "/app/private-mail",
            ["RecoveryEmail:PublicOrigin"] = "http://localhost:8081"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new LocalOwnerEmailVerificationDelivery(local, new EmailEnvironment()));
    }

    [Fact]
    public async Task LocalEmailDeliveryProtectsItsFilesAndRejectsRealAddressesAndUnsafeConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), "p02-synthetic-email-" + Guid.NewGuid().ToString("N"));
        var settings = new Dictionary<string, string?>
        {
            ["RecoveryEmail:LocalPickupDirectory"] = path,
            ["RecoveryEmail:PublicOrigin"] = "http://localhost:8081"
        };
        var environment = new EmailEnvironment { EnvironmentName = "Development" };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        if (!OperatingSystem.IsLinux())
        {
            Assert.Throws<InvalidOperationException>(() => new LocalOwnerEmailVerificationDelivery(configuration, environment));
            return;
        }
        try
        {
            var sender = new LocalOwnerEmailVerificationDelivery(configuration, environment);
            Assert.False(sender.CanDeliver("owner@example.com"));
            Assert.False(sender.CanDeliver("owner@example.test\r\nBcc: bad@example.test"));
            await sender.DeliverAsync(Email, "synthetic-proof", DateTimeOffset.UtcNow.AddMinutes(30), TestContext.Current.CancellationToken);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(path));
            var file = Assert.Single(Directory.GetFiles(path));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            var content = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
            Assert.Contains("Content-Transfer-Encoding: base64", content);
            var body = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(content.Split("\r\n\r\n", 2)[1]));
            Assert.Contains("http://localhost:8081/#verify-owner-email=synthetic-proof", body);
            settings["RecoveryEmail:PublicOrigin"] = "https://unexpected.example.test";
            Assert.Throws<InvalidOperationException>(() => new LocalOwnerEmailVerificationDelivery(
                new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), environment));
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
            await Assert.ThrowsAsync<IOException>(() => sender.DeliverAsync(Email, "another-synthetic-proof", DateTimeOffset.UtcNow.AddMinutes(30), TestContext.Current.CancellationToken));
            Assert.Single(Directory.GetFiles(path));
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    }
}
