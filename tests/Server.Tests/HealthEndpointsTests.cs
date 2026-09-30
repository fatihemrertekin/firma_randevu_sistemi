using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Server.Tests;

public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task LiveEndpointRespondsWithoutDatabase()
    {
        await using var app = CreateAppWithoutDatabase();
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadyEndpointFailsWhenDatabaseIsNotConfigured()
    {
        await using var app = CreateAppWithoutDatabase();
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task RootServesBuiltWebPage()
    {
        await using var app = CreateAppWithoutDatabase();
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"tr\">", body);
    }

    private static WebApplicationFactory<Program> CreateAppWithoutDatabase() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:AppDatabase"] = string.Empty
                    })));
}
