using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Xunit;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.TestAccounts;

namespace Server.Tests.Support;

internal static class AuthenticationTestSupport
{

    internal static string GenerateAuthenticatorCode(string secret)
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

    internal static async Task<string> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CsrfResponse>(TestContext.Current.CancellationToken);
        return Assert.IsType<string>(body?.Token);
    }

    internal static async Task PasswordStepAsync(HttpClient client)
    {
        var csrf = await GetCsrfAsync(client);
        using var response = await PostAsync(client, "/api/auth/login",
            new { email = Email, password = Password }, csrf);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    internal static async Task LogoutAsync(HttpClient client)
    {
        var csrf = await GetCsrfAsync(client);
        using var response = await PostAsync(client, "/api/auth/logout", new { }, csrf);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    internal static async Task<HttpResponseMessage> PostAsync(
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

    internal static async Task CompleteMfaAsync(HttpClient client, string key)
    {
        using var response = await PostAsync(client, "/api/auth/mfa/login",
            new { code = GenerateAuthenticatorCode(key) }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    internal static async Task<HttpClient> InviteOwnerAsync(RecoverySeed seeded)
    {
        var client = seeded.App.CreateClient();
        await PasswordStepAsync(client);
        await CompleteMfaAsync(client, seeded.Key);
        return client;
    }

    internal sealed record CsrfResponse(string Token);

    internal sealed record SetupResponse(string Key, string Uri);

    internal sealed record EnableResponse(string[] RecoveryCodes);

    internal sealed record AccountResponse(string Email, bool MfaEnabled, bool OwnerAccess);
}
