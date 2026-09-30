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

namespace Server.Tests;

public sealed partial class OwnerMfaTests
{
    private const string Email = "mfa-owner@example.test";
    private const string Password = "Synthetic!Owner123";

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

    private static string GenerateAuthenticatorCode(string secret)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in secret.ToUpperInvariant())
        {
            var value = alphabet.IndexOf(character);
            Assert.True(value >= 0);
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
        }

        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter.ToArray());
        var offset = hash[^1] & 0x0f;
        var valueCode = ((hash[offset] & 0x7f) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];
        return (valueCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CsrfResponse>(TestContext.Current.CancellationToken);
        return Assert.IsType<string>(body?.Token);
    }

    private static async Task PasswordStepAsync(HttpClient client)
    {
        var csrf = await GetCsrfAsync(client);
        using var response = await PostAsync(client, "/api/auth/login",
            new { email = Email, password = Password }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task LogoutAsync(HttpClient client)
    {
        var csrf = await GetCsrfAsync(client);
        using var response = await PostAsync(client, "/api/auth/logout", new { }, csrf);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, object body, string? csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        if (csrf is not null)
        {
            request.Headers.Add("X-CSRF-TOKEN", csrf);
        }
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record CsrfResponse(string Token);
    private sealed record SetupResponse(string Key, string Uri);
    private sealed record EnableResponse(string[] RecoveryCodes);
    private sealed record AccountResponse(string Email, bool MfaEnabled, bool OwnerAccess);
}
