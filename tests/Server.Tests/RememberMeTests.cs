using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.StaffTestSupport;
using static Server.Tests.Support.TestAccounts;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class RememberMeTests
{
    [Theory]
    [InlineData("password", false)]
    [InlineData("password", true)]
    [InlineData("authenticator", false)]
    [InlineData("authenticator", true)]
    [InlineData("recovery", false)]
    [InlineData("recovery", true)]
    [InlineData("password", null)]
    [InlineData("authenticator", null)]
    [InlineData("recovery", null)]
    public async Task PersistenceIsOptInForEveryLoginAndExpiresAfterEightHours(string flow, bool? rememberMe)
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var client = app.CreateClient();
        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var clock = new LoginClock();
        options.TimeProvider = clock;
        Assert.Equal(TimeSpan.FromHours(8), options.ExpireTimeSpan);
        Assert.False(options.SlidingExpiration);

        string endpoint;
        object body;
        if (flow == "password")
        {
            await CreatePasswordStaffAsync(app);
            endpoint = "/api/auth/login";
            body = rememberMe.HasValue
                ? new { email = StaffEmail, password = Password, rememberMe = rememberMe.Value }
                : new { email = StaffEmail, password = Password };
        }
        else
        {
            using var password = await PostAsync(client, "/api/auth/login",
                new { email = Email, password = Password, rememberMe = rememberMe == true }, await GetCsrfAsync(client));
            Assert.Equal(HttpStatusCode.Accepted, password.StatusCode);
            Assert.DoesNotContain(password.Headers.GetValues("Set-Cookie"), value => value.StartsWith("FirmaRandevu.Dev=", StringComparison.Ordinal));
            var pending = SetCookieHeaderValue.ParseList(password.Headers.GetValues("Set-Cookie").ToList())
                .Single(cookie => cookie.Name == "FirmaRandevu.TwoFactor.Dev");
            Assert.Null(pending.Expires);
            var code = flow == "recovery" ? seeded.Codes[0] : GenerateAuthenticatorCode(seeded.Key);
            endpoint = flow == "recovery" ? "/api/auth/mfa/recovery-login" : "/api/auth/mfa/login";
            body = rememberMe.HasValue ? new { code, rememberMe = rememberMe.Value } : new { code };
            using var anonymous = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using var noCsrf = await PostAsync(client, endpoint, body, null);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        using var login = await PostAsync(client, endpoint, body, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookies = SetCookieHeaderValue.ParseList(login.Headers.GetValues("Set-Cookie").ToList());
        var authCookie = cookies.Single(cookie => cookie.Name == "FirmaRandevu.Dev");
        Assert.Equal(rememberMe == true, authCookie.Expires.HasValue);
        Assert.True(authCookie.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, authCookie.SameSite);
        Assert.DoesNotContain(cookies, cookie => cookie.Name.ToString().Contains("TwoFactorRememberMe", StringComparison.Ordinal));
        var ticket = options.TicketDataFormat.Unprotect(authCookie.Value.ToString());
        Assert.NotNull(ticket);
        Assert.Equal(rememberMe == true, ticket.Properties.IsPersistent);
        Assert.Equal(TimeSpan.FromHours(8), ticket.Properties.ExpiresUtc - ticket.Properties.IssuedUtc);
        using var account = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, account.StatusCode);
        var profile = await account.Content.ReadFromJsonAsync<AccountResponse>(TestContext.Current.CancellationToken);
        Assert.NotNull(profile);
        Assert.Equal(flow != "password", profile.OwnerAccess);

        clock.Advance(TimeSpan.FromHours(8) + TimeSpan.FromSeconds(1));
        using var expired = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task RecoveryFailureCannotIssuePersistentCookieAndLogoutStillClearsIt()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seeded = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var app = seeded.App;
        using var client = app.CreateClient();
        await PasswordStepAsync(client);
        using var invalid = await PostAsync(client, "/api/auth/mfa/recovery-login",
            new { code = "invalid", rememberMe = true }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.False(invalid.Headers.Contains("Set-Cookie"));
        using var login = await PostAsync(client, "/api/auth/mfa/recovery-login",
            new { code = seeded.Codes[0], rememberMe = true }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await LogoutAsync(client);
        using var loggedOut = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, loggedOut.StatusCode);
        await PasswordStepAsync(client);
        using var reused = await PostAsync(client, "/api/auth/mfa/recovery-login",
            new { code = seeded.Codes[0], rememberMe = true }, await GetCsrfAsync(client));
        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    private sealed class LoginClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
