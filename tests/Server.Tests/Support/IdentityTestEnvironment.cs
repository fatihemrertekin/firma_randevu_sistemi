using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;
using static Server.Tests.Support.TestAccounts;

namespace Server.Tests.Support;

internal static class IdentityTestEnvironment
{

    internal static PostgreSqlContainer RecoveryDatabase() => new PostgreSqlBuilder("postgres:18.6-trixie")
        .WithDatabase("p02_recovery")
        .WithUsername("postgres")
        .WithPassword("Synthetic!Postgres123")
        .Build();

    internal static async Task<RecoverySeed> CreateRecoveryAppAsync(string connectionString, TimeProvider? clock = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            if (clock is not null) builder.ConfigureServices(services => services.AddSingleton(clock));
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:AppDatabase"] = connectionString,
                    ["Auth:InstanceId"] = RecoveryInstance
                }));
        });
        using var scope = app.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Owner"))).Succeeded);
        var owner = new AppUser { UserName = Email, Email = Email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(owner, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(owner, "Owner")).Succeeded);
        Assert.True((await users.ResetAuthenticatorKeyAsync(owner)).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(owner, true)).Succeeded);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(owner, 8))?.ToArray();
        Assert.NotNull(codes);
        var key = await users.GetAuthenticatorKeyAsync(owner);
        Assert.NotNull(key);
        var otherUser = new AppUser { UserName = "other@example.test", Email = "other@example.test", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(otherUser, Password)).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(otherUser, true)).Succeeded);
        return new RecoverySeed(app, owner.Id, otherUser.Id, key, codes, Assert.IsType<string>(owner.SecurityStamp));
    }

    internal static async Task AssertOriginalStateAsync(WebApplicationFactory<Program> app, RecoverySeed seeded)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.OwnerMfaRecoveryAudits.AnyAsync(TestContext.Current.CancellationToken));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = await users.FindByIdAsync(seeded.OwnerId.ToString());
        Assert.NotNull(owner);
        Assert.True(owner.TwoFactorEnabled);
        Assert.Equal(seeded.Stamp, owner.SecurityStamp);
        Assert.Equal(seeded.Key, await users.GetAuthenticatorKeyAsync(owner));
        Assert.Equal(8, await users.CountRecoveryCodesAsync(owner));
        Assert.True(await users.CheckPasswordAsync(owner, Password));
    }

    internal sealed record RecoverySeed(WebApplicationFactory<Program> App, Guid OwnerId, Guid OtherUserId,
        string Key, string[] Codes, string Stamp);

    internal static async Task AwaitInviteLocksAsync(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'")
            .SingleAsync(timeout.Token) < 2) await Task.Delay(20, timeout.Token);
    }
}
