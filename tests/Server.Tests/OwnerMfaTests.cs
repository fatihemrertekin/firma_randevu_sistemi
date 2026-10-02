using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using static Server.Tests.Support.AuthenticationTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class OwnerMfaTests
{

    [Fact]
    public async Task OwnerNeedsAuthenticatorOrUnusedRecoveryCodeAfterMfaIsEnabled()
    {
        await using var database = new PostgreSqlBuilder("postgres:18.6-trixie")
            .WithDatabase("p02_mfa")
            .WithUsername("postgres")
            .WithPassword("Synthetic!Postgres123")
            .Build();
        await database.StartAsync(TestContext.Current.CancellationToken);

        var connectionString = database.GetConnectionString();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString).Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:AppDatabase"] = connectionString
                    }));
            });
        using (var scope = app.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            Assert.True((await roles.CreateAsync(new IdentityRole<Guid>("Owner"))).Succeeded);
            var user = new AppUser { UserName = Email, Email = Email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Owner")).Succeeded);
        }

        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var unauthorizedSetup = await PostAsync(client, "/api/auth/mfa/setup",
            new { password = Password }, null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedSetup.StatusCode);

        var csrf = await GetCsrfAsync(client);
        using var firstLogin = await PostAsync(client, "/api/auth/login",
            new { email = Email, password = Password }, csrf);
        Assert.Equal(HttpStatusCode.NoContent, firstLogin.StatusCode);
        using var beforeMfa = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        var beforeMfaAccount = await beforeMfa.Content.ReadFromJsonAsync<AccountResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(beforeMfaAccount);
        Assert.False(beforeMfaAccount.OwnerAccess);

        using var previousSession = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var previousCsrf = await GetCsrfAsync(previousSession);
        using var previousLogin = await PostAsync(previousSession, "/api/auth/login",
            new { email = Email, password = Password }, previousCsrf);
        Assert.Equal(HttpStatusCode.NoContent, previousLogin.StatusCode);

        using var noCsrf = await PostAsync(client, "/api/auth/mfa/setup",
            new { password = Password }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        csrf = await GetCsrfAsync(client);
        using var wrongPassword = await PostAsync(client, "/api/auth/mfa/setup",
            new { password = "wrong" }, csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        using var setup = await PostAsync(client, "/api/auth/mfa/setup",
            new { password = Password }, csrf);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var setupInfo = await setup.Content.ReadFromJsonAsync<SetupResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(setupInfo);
        Assert.Contains(setupInfo.Key, setupInfo.Uri);
        Assert.False(string.IsNullOrWhiteSpace(setupInfo.Key));

        csrf = await GetCsrfAsync(client);
        using var badCode = await PostAsync(client, "/api/auth/mfa/enable",
            new { password = Password, code = "not-a-code" }, csrf);
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);
        var code = GenerateAuthenticatorCode(setupInfo.Key);
        using var enable = await PostAsync(client, "/api/auth/mfa/enable",
            new { password = Password, code }, csrf);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var enabled = await enable.Content.ReadFromJsonAsync<EnableResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(enabled);
        Assert.Equal(8, enabled.RecoveryCodes.Length);
        var recoveryCode = enabled.RecoveryCodes[0];

        using var signedOut = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
        using var revokedSession = await previousSession.GetAsync("/api/auth/me",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedSession.StatusCode);

        csrf = await GetCsrfAsync(client);
        using var passwordOnly = await PostAsync(client, "/api/auth/login",
            new { email = Email, password = Password }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, passwordOnly.StatusCode);
        Assert.Contains(passwordOnly.Headers.GetValues("Set-Cookie"), value =>
            value.Contains("FirmaRandevu.TwoFactor.Dev", StringComparison.Ordinal) &&
            value.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));
        using var stillAnonymous = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, stillAnonymous.StatusCode);

        csrf = await GetCsrfAsync(client);
        using var noCodeCsrf = await PostAsync(client, "/api/auth/mfa/login", new { code }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCodeCsrf.StatusCode);
        using var invalidCode = await PostAsync(client, "/api/auth/mfa/login",
            new { code = "not-a-code" }, csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidCode.StatusCode);
        code = GenerateAuthenticatorCode(setupInfo.Key);
        using var codeLogin = await PostAsync(client, "/api/auth/mfa/login", new { code }, csrf);
        Assert.Equal(HttpStatusCode.NoContent, codeLogin.StatusCode);
        using var account = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        var mfaAccount = await account.Content.ReadFromJsonAsync<AccountResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(mfaAccount);
        Assert.True(mfaAccount.MfaEnabled);
        Assert.True(mfaAccount.OwnerAccess);

        await LogoutAsync(client);
        await PasswordStepAsync(client);
        csrf = await GetCsrfAsync(client);
        using var recoveryLogin = await PostAsync(client, "/api/auth/mfa/recovery-login",
            new { code = recoveryCode }, csrf);
        Assert.Equal(HttpStatusCode.NoContent, recoveryLogin.StatusCode);
        using var recoveredAccount = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        var recoveryAccount = await recoveredAccount.Content.ReadFromJsonAsync<AccountResponse>(
            TestContext.Current.CancellationToken);
        Assert.NotNull(recoveryAccount);
        Assert.True(recoveryAccount.OwnerAccess);
        await LogoutAsync(client);
        await PasswordStepAsync(client);
        csrf = await GetCsrfAsync(client);
        using var reused = await PostAsync(client, "/api/auth/mfa/recovery-login",
            new { code = recoveryCode }, csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }
}
