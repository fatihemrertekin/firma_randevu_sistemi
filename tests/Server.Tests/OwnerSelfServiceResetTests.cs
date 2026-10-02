using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Server.Features.Identity;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.TestAccounts;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.PasswordTestSupport;
using static Server.Tests.Support.EmailTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class OwnerSelfServiceResetTests
{
    private sealed class ResetDelivery : IOwnerPasswordResetDelivery
    {
        public List<string> Tokens { get; } = [];
        public bool Fail { get; set; }
        public int Calls { get; private set; }
        public Func<CancellationToken, Task>? BeforeDelivery { get; set; }
        public bool CanDeliver(string email) => true;
        public async Task DeliverAsync(string email, string token, DateTimeOffset expiresAt, Guid deliveryId, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail) throw new IOException("Synthetic delivery failure");
            if (BeforeDelivery is not null) await BeforeDelivery(cancellationToken);
            Tokens.Add(token);
        }
    }
    private static WebApplicationFactory<Program> SelfResetApp(RecoverySeed seed, ResetDelivery delivery, bool enabled = true) =>
        seed.App.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["OwnerPasswordReset:LocalEnabled"] = enabled.ToString() }));
            builder.ConfigureServices(services =>
            {
                foreach (var descriptor in services.Where(entry => entry.ServiceType == typeof(IHostedService) &&
                    entry.ImplementationType == typeof(OwnerResetDeliveryWorker)).ToArray()) services.Remove(descriptor);
                services.AddSingleton<IOwnerPasswordResetDelivery>(delivery);
            });
        });

    [Fact]
    public async Task SelfResetUsesSameResponseForUnknownUnverifiedNonOwnerAndDisabledAndRequiresCsrf()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var original = seed.App;
        var delivery = new ResetDelivery();
        await using var app = SelfResetApp(seed, delivery);
        using var client = app.CreateClient();
        string? generic = null;
        foreach (var email in new[] { "unknown@example.test", Email, "other@example.test", "", new string('x', 255) })
        {
            using var response = await AskSelfResetAsync(client, email);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            generic ??= content;
            Assert.Equal(generic, content);
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
        using var noCsrf = await PostAsync(client, SelfResetPath, new { email = Email }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        await VerifyForSelfResetAsync(app, seed.OwnerId);
        await using var disabled = SelfResetApp(seed, delivery, false);
        using var disabledClient = disabled.CreateClient();
        using var refused = await AskSelfResetAsync(disabledClient);
        Assert.Equal(generic, await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().OwnerSelfServiceResets.AnyAsync(TestContext.Current.CancellationToken));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task SelfResetQueuesEncryptedProofSurvivesRestartRevokesSessionsAndPreservesMfa()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var original = seed.App;
        var delivery = new ResetDelivery();
        await using var app = SelfResetApp(seed, delivery);
        await VerifyForSelfResetAsync(app, seed.OwnerId);
        using var active = await InviteOwnerAsync(seed);
        using var client = app.CreateClient();
        using var requested = await AskSelfResetAsync(client);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.OwnerSelfServiceResets.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        var token = app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(OwnerSelfServiceResetFlow.OutboxPurpose)
            .Unprotect(Assert.IsType<string>(job.ProtectedPayload));
        Assert.DoesNotContain(token, job.ProtectedPayload);
        Assert.DoesNotContain(Email, job.ProtectedPayload);
        Assert.Empty(delivery.Tokens);
        await AssertOriginalStateAsync(app, seed);
        await using var restarted = SelfResetApp(seed, delivery);
        await ProcessSelfResetAsync(restarted);
        Assert.Equal(token, Assert.Single(delivery.Tokens));
        job = await db.OwnerSelfServiceResets.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Delivered", job.Status);
        Assert.Null(job.ProtectedPayload);
        using var reset = await ResetAsync(client, token);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        using var replay = await ResetAsync(client, token);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var oldSession = await active.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = await users.FindByIdAsync(seed.OwnerId.ToString());
        Assert.NotNull(owner);
        Assert.True(owner.TwoFactorEnabled);
        Assert.Equal(seed.Key, await users.GetAuthenticatorKeyAsync(owner));
        Assert.Equal(8, await users.CountRecoveryCodesAsync(owner));
        using var login = await PostAsync(client, "/api/auth/login", new { email = Email, password = NewPassword }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
        await CompleteMfaAsync(client, seed.Key);
        using var authenticated = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, authenticated.StatusCode);
    }

    [Fact]
    public async Task SelfResetRetriesWithBackoffExpiresJobsAndReclaimsDeadLeaseWithBoundedAttempts()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var clock = new ResetClock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock);
        await using var original = seed.App;
        var delivery = new ResetDelivery { Fail = true };
        await using var app = SelfResetApp(seed, delivery);
        await VerifyForSelfResetAsync(app, seed.OwnerId);
        using var client = app.CreateClient();
        using var request = await AskSelfResetAsync(client);
        await ProcessSelfResetAsync(app);
        await ProcessSelfResetAsync(app);
        Assert.Equal(1, delivery.Calls);
        clock.Advance(TimeSpan.FromSeconds(16));
        await ProcessSelfResetAsync(app);
        Assert.Equal(2, delivery.Calls);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.OwnerSelfServiceResets.ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.Status, "Processing")
            .SetProperty(entry => entry.LeaseUntil, clock.GetUtcNow().AddSeconds(-1)), TestContext.Current.CancellationToken);
        delivery.Fail = false;
        await ProcessSelfResetAsync(app);
        Assert.Single(delivery.Tokens);
        clock.Advance(TimeSpan.FromMinutes(1));
        using var renewed = await AskSelfResetAsync(client);
        clock.Advance(TimeSpan.FromMinutes(31));
        await ProcessSelfResetAsync(app);
        var job = await db.OwnerSelfServiceResets.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Expired", job.Status);
        Assert.Null(job.ProtectedPayload);
        using var expired = await ResetAsync(client, delivery.Tokens[0]);
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        using var again = await AskSelfResetAsync(client);
        delivery.Fail = true;
        for (var attempt = 0; attempt < 4; attempt++) { await ProcessSelfResetAsync(app); clock.Advance(TimeSpan.FromMinutes(3)); }
        job = await db.OwnerSelfServiceResets.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Failed", job.Status);
        Assert.Equal(4, job.Attempts);
        Assert.Null(job.ProtectedPayload);
    }

    [Fact]
    public async Task SelfResetIsAtomicAndConcurrentRequestsCreateOnlyOneCurrentGrant()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var original = seed.App;
        var delivery = new ResetDelivery();
        await using var app = SelfResetApp(seed, delivery);
        await VerifyForSelfResetAsync(app, seed.OwnerId);
        using var first = app.CreateClient(); using var second = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_self_reset_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic commit failure'; END; $$;
            CREATE CONSTRAINT TRIGGER reject_self_reset_commit AFTER INSERT ON "OwnerSelfServiceResets"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_self_reset_commit();
            """, TestContext.Current.CancellationToken);
        using var failed = await AskSelfResetAsync(first);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.False(await db.OwnerSelfServiceResets.AnyAsync(TestContext.Current.CancellationToken));
        Assert.False(await db.OwnerPasswordResetAudits.AnyAsync(TestContext.Current.CancellationToken));
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_self_reset_commit ON \"OwnerSelfServiceResets\"; DROP FUNCTION reject_self_reset_commit();", TestContext.Current.CancellationToken);
        var replies = await Task.WhenAll(AskSelfResetAsync(first), AskSelfResetAsync(second));
        try { Assert.All(replies, reply => Assert.Equal(HttpStatusCode.Accepted, reply.StatusCode)); }
        finally { foreach (var reply in replies) reply.Dispose(); }
        Assert.Equal(1, await db.OwnerSelfServiceResets.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, await db.OwnerPasswordResetAudits.CountAsync(TestContext.Current.CancellationToken));
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task SelfResetLateAcknowledgementCannotReplaceNewGrantAndOnlyOneResetSucceeds()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var clock = new ResetClock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock);
        await using var original = seed.App;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivery = new ResetDelivery { BeforeDelivery = async token => { started.SetResult(); await release.Task.WaitAsync(token); } };
        await using var app = SelfResetApp(seed, delivery);
        await VerifyForSelfResetAsync(app, seed.OwnerId);
        using var client = app.CreateClient();
        using var asked = await AskSelfResetAsync(client);
        var processing = ProcessSelfResetAsync(app);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(1));
        using var renewed = await AskSelfResetAsync(client);
        release.SetResult(); await processing;
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.OwnerSelfServiceResets.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Pending", job.Status);
        Assert.NotNull(job.ProtectedPayload);
        using var old = await ResetAsync(client, Assert.Single(delivery.Tokens));
        Assert.Equal(HttpStatusCode.BadRequest, old.StatusCode);
        delivery.BeforeDelivery = null;
        await Task.WhenAll(ProcessSelfResetAsync(app), ProcessSelfResetAsync(app));
        Assert.Equal(2, delivery.Tokens.Count);
        using var other = app.CreateClient();
        var resets = await Task.WhenAll(ResetAsync(client, delivery.Tokens[1]), ResetAsync(other, delivery.Tokens[1]));
        try
        {
            Assert.Single(resets, result => result.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(resets, result => result.StatusCode == HttpStatusCode.BadRequest);
        }
        finally { foreach (var reset in resets) reset.Dispose(); }
    }

    [Fact]
    public async Task SelfResetRejectsChangedEmailStampRoleAndDifferentInstanceAndCancelsStaleDelivery()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var clock = new ResetClock();
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString(), clock);
        await using var original = seed.App;
        var delivery = new ResetDelivery();
        await using var app = SelfResetApp(seed, delivery);
        await VerifyForSelfResetAsync(app, seed.OwnerId);
        using var client = app.CreateClient();
        using var request = await AskSelfResetAsync(client);
        await ProcessSelfResetAsync(app);
        var token = Assert.Single(delivery.Tokens);
        await using var wrongInstance = app.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:InstanceId"] = "wrong-self-service-instance" })));
        using var wrongClient = wrongInstance.CreateClient();
        using var wrong = await ResetAsync(wrongClient, token);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = await users.FindByIdAsync(seed.OwnerId.ToString());
        Assert.NotNull(owner);
        Assert.True((await users.SetEmailAsync(owner, "changed@example.test")).Succeeded);
        using var changedEmail = await ResetAsync(client, token);
        Assert.Equal(HttpStatusCode.BadRequest, changedEmail.StatusCode);
        Assert.True((await users.SetEmailAsync(owner, Email)).Succeeded);
        Assert.True((await users.UpdateSecurityStampAsync(owner)).Succeeded);
        using var changedStamp = await ResetAsync(client, token);
        Assert.Equal(HttpStatusCode.BadRequest, changedStamp.StatusCode);
        owner.EmailConfirmed = true;
        Assert.True((await users.UpdateAsync(owner)).Succeeded);
        clock.Advance(TimeSpan.FromMinutes(1));
        using var again = await AskSelfResetAsync(client);
        Assert.True((await users.RemoveFromRoleAsync(owner, "Owner")).Succeeded);
        await ProcessSelfResetAsync(app);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.OwnerSelfServiceResets.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Cancelled", job.Status);
        Assert.Null(job.ProtectedPayload);
        Assert.Single(delivery.Tokens);
    }
}
