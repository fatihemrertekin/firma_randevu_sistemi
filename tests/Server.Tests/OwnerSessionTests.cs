using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Server.Features.Identity;
using Server.Infrastructure;
using Testcontainers.PostgreSql;
using Xunit;

namespace Server.Tests;

public sealed class OwnerSessionTests
{
    [Fact]
    public async Task OwnerBootstrapAndSessionRequirePasswordAndCsrf()
    {
        await using var database = new PostgreSqlBuilder("postgres:18.6-trixie")
            .WithDatabase("p02_identity")
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

        var keyDirectory = Path.Combine(Path.GetTempPath(), "p02-keys-" + Guid.NewGuid());
        await using var app = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:AppDatabase"] = connectionString,
                        ["Auth:InstanceId"] = "p02-test-" + Guid.NewGuid(),
                        ["Auth:KeysDirectory"] = keyDirectory
                    }));
            });

        const string email = "owner@example.test";
        const string password = "Synthetic!Owner123";
        var oldEmail = Environment.GetEnvironmentVariable("APP_BOOTSTRAP_OWNER_EMAIL");
        var oldPassword = Environment.GetEnvironmentVariable("APP_BOOTSTRAP_OWNER_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("APP_BOOTSTRAP_OWNER_EMAIL", email);
            Environment.SetEnvironmentVariable("APP_BOOTSTRAP_OWNER_PASSWORD", password);
            Assert.Equal(0, await BootstrapOwner.RunAsync(app.Services, TestContext.Current.CancellationToken));
            Assert.Equal(1, await BootstrapOwner.RunAsync(app.Services, TestContext.Current.CancellationToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable("APP_BOOTSTRAP_OWNER_EMAIL", oldEmail);
            Environment.SetEnvironmentVariable("APP_BOOTSTRAP_OWNER_PASSWORD", oldPassword);
        }

        using var client = app.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var anonymous = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var noCsrf = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);

        var csrf = await GetCsrfAsync(client);
        using var wrongPassword = await PostWithCsrfAsync(client, "/api/auth/login",
            new { email, password = "wrong" }, csrf);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

        using var login = await PostWithCsrfAsync(client, "/api/auth/login",
            new { email, password }, csrf);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        using var me = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains(email, await me.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        using var logoutWithoutCsrf = await client.PostAsync("/api/auth/logout", null,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, logoutWithoutCsrf.StatusCode);

        var authenticatedCsrf = await GetCsrfAsync(client);
        using var logout = await PostWithCsrfAsync(client, "/api/auth/logout", null, authenticatedCsrf);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var afterLogout = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);

        var anonymousCsrf = await GetCsrfAsync(client);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await PostWithCsrfAsync(client, "/api/auth/login",
                new { email, password = "wrong" }, anonymousCsrf);
            Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }
        using var locked = await PostWithCsrfAsync(client, "/api/auth/login",
            new { email, password }, anonymousCsrf);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);

        HttpStatusCode lastStatus = default;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            using var limited = await client.PostAsJsonAsync("/api/auth/login",
                new { email, password }, TestContext.Current.CancellationToken);
            lastStatus = limited.StatusCode;
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<CsrfResponse>(TestContext.Current.CancellationToken);
        return Assert.IsType<string>(token?.Token);
    }

    private static async Task<HttpResponseMessage> PostWithCsrfAsync(
        HttpClient client, string path, object? body, string csrf)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed record CsrfResponse(string Token);
}
