using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Tests.Support;
using SubscriptionOperator;
using Xunit;
using static Server.Tests.Support.OperatorTestEnvironment;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class OperatorRecoveryTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task Quarantine(OperatorTestEnvironment env, string expected = "synthetic-operator")
    {
        var connection = new NpgsqlConnectionStringBuilder(env.Database.GetConnectionString()) { Database = "operator_identity" };
        await using var db = new NpgsqlConnection(connection.ConnectionString);
        await db.OpenAsync(Token);
        await using var scope = new NpgsqlCommand("SELECT set_config('recovery.expected_operator', @expected, false)", db);
        scope.Parameters.AddWithValue("expected", expected);
        await scope.ExecuteNonQueryAsync(Token);
        await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "deploy/Invalidate-RestoredOperatorAccess.sql"), Token), db);
        await command.ExecuteNonQueryAsync(Token);
    }

    [Fact]
    public async Task QuarantineRevokesCookiesPasswordPendingMfaAndRecoveryEvenAfterUnlock()
    {
        await using var env = new OperatorTestEnvironment(); await env.StartAsync();
        using var old = await env.LoginAsync();
        using var scope = env.App.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperatorUser>>();
        var user = await users.FindByEmailAsync(Email); Assert.NotNull(user);
        var stamp = user.SecurityStamp;
        var key = await users.GetAuthenticatorKeyAsync(user); Assert.NotNull(key);
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 8))?.ToArray(); Assert.NotNull(codes);
        using var pending = env.App.CreateClient();
        using (var login = await Post(pending, "/api/auth/login", new { Email, Password })) Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
        await Quarantine(env);
        scope.ServiceProvider.GetRequiredService<OperatorIdentityDb>().ChangeTracker.Clear();
        user = await users.FindByEmailAsync(Email); Assert.NotNull(user);
        Assert.NotEqual(stamp, user.SecurityStamp); Assert.Null(user.PasswordHash);
        Assert.False(user.TwoFactorEnabled); Assert.True(await users.IsLockedOutAsync(user));
        Assert.Null(await users.GetAuthenticatorKeyAsync(user)); Assert.Equal(0, await users.CountRecoveryCodesAsync(user));
        Assert.True(await users.IsInRoleAsync(user, "Operator"));
        using (var denied = await old.GetAsync("/api/auth/me", Token)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await Post(pending, "/api/auth/mfa/login", new { value = AuthenticationTestSupport.GenerateAuthenticatorCode(key) })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await Post(pending, "/api/auth/mfa/login", new { value = codes[0], recovery = true })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        // Yalnız testte kilidi kaldırmak eski kimlik bilgilerini yeniden geçerli yapmamalı.
        Assert.True((await users.SetLockoutEndDateAsync(user, null)).Succeeded);
        using (var denied = await old.GetAsync("/api/subscription", Token)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await Post(pending, "/api/auth/login", new { Email, Password })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await Post(pending, "/api/auth/mfa/login", new { value = AuthenticationTestSupport.GenerateAuthenticatorCode(key) })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var denied = await Post(pending, "/api/auth/mfa/login", new { value = codes[0], recovery = true })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongIdentityOrAdditionalAccountIsRejectedWithoutChangingCredentials(bool additionalAccount)
    {
        await using var env = new OperatorTestEnvironment(); await env.StartAsync();
        using var scope = env.App.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OperatorUser>>();
        if (additionalAccount) Assert.True((await users.CreateAsync(new OperatorUser { UserName = "other@example.test", Email = "other@example.test" }, Password)).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<OperatorIdentityDb>();
        var before = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.PasswordHash, u.SecurityStamp, u.ConcurrencyStamp, u.LockoutEnd, u.TwoFactorEnabled }).ToArrayAsync(Token);
        await Assert.ThrowsAsync<PostgresException>(() => Quarantine(env, additionalAccount ? "synthetic-operator" : "wrong-operator"));
        var after = await db.Users.AsNoTracking().OrderBy(u => u.Id).Select(u => new { u.Id, u.PasswordHash, u.SecurityStamp, u.ConcurrencyStamp, u.LockoutEnd, u.TwoFactorEnabled }).ToArrayAsync(Token);
        Assert.Equal(before, after);
    }
}
