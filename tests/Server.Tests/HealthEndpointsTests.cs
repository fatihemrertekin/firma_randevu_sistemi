using System.Net;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Features.Scheduling;
using Xunit;
using static Server.Tests.Support.IdentityTestEnvironment;

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
    public async Task ReadyEndpointChecksWarmPoolDuringDatabaseOutageAndRecovers()
    {
        var token = TestContext.Current.CancellationToken;
        await using var database = RecoveryDatabase();
        await database.StartAsync(token);
        var connectionString = new NpgsqlConnectionStringBuilder(database.GetConnectionString())
        {
            MaxPoolSize = 1,
            Timeout = 1,
            CommandTimeout = 30
        }.ConnectionString;
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(token);
            await using var command = new NpgsqlCommand("CREATE TABLE health_sentinel (value integer NOT NULL); INSERT INTO health_sentinel VALUES (42);", connection);
            await command.ExecuteNonQueryAsync(token);
        }
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:AppDatabase"] = connectionString }));
            builder.ConfigureServices(services =>
            {
                // Bu test şema kurmaz; havuzu yalnız sağlık ucunun kullanması gerekir.
                foreach (var service in services.Where(item => item.ImplementationType == typeof(AppointmentPendingWorker)).ToArray()) services.Remove(service);
            });
        });
        using var client = app.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        using (var warm = await client.GetAsync("/health/ready", token)) Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        await database.PauseAsync(token);
        try
        {
            var elapsed = Stopwatch.StartNew();
            using var unavailable = await client.GetAsync("/health/ready", token);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10));
            Assert.Equal(string.Empty, await unavailable.Content.ReadAsStringAsync(token));
            using var live = await client.GetAsync("/health/live", token);
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }
        finally { await database.UnpauseAsync(token); }
        using (var recovered = await client.GetAsync("/health/ready", token)) Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        await using var restoredConnection = new NpgsqlConnection(connectionString);
        await restoredConnection.OpenAsync(token);
        await using var sentinel = new NpgsqlCommand("SELECT value FROM health_sentinel", restoredConnection);
        Assert.Equal(42, await sentinel.ExecuteScalarAsync(token));
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
