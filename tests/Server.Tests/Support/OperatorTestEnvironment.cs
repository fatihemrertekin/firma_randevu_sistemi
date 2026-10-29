using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Server.Infrastructure;
using SubscriptionOperator;
using Testcontainers.PostgreSql;
using Xunit;

namespace Server.Tests.Support;

internal sealed class OperatorTestEnvironment : IAsyncDisposable
{
    internal const string Password = "Synthetic!Operator123";
    internal const string Email = "operator@example.test";
    internal const string Instance = "synthetic-firm-70703";
    internal PostgreSqlContainer Database { get; } = IdentityTestEnvironment.RecoveryDatabase();
    private WebApplicationFactory<OperatorProgram>? app;
    internal WebApplicationFactory<OperatorProgram> App => app ?? throw new InvalidOperationException("Fixture not started");
    internal string IdentityConnection { get; private set; } = "";
    internal string TargetConnection { get; private set; } = "";
    internal Guid Actor { get; private set; }
    private readonly string keys = Path.Combine(Path.GetTempPath(), "operator-tests-" + Guid.NewGuid());

    internal async Task StartAsync()
    {
        var token = TestContext.Current.CancellationToken;
        await Database.StartAsync(token);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Database.GetConnectionString()).Options)) await db.Database.MigrateAsync(token);
        Assert.Equal(0, await ProvisionAsync("target", "p02_recovery"));
        await using var admin = new NpgsqlConnection(Database.GetConnectionString()); await admin.OpenAsync(token);
        await using (var command = new NpgsqlCommand("CREATE DATABASE operator_identity;", admin)) await command.ExecuteNonQueryAsync(token);
        Assert.Equal(0, await ProvisionAsync("identity", "operator_identity"));
        var identityAdmin = new NpgsqlConnectionStringBuilder(Database.GetConnectionString()) { Database = "operator_identity" }.ConnectionString;
        await using (var db = new OperatorIdentityDb(new DbContextOptionsBuilder<OperatorIdentityDb>().UseNpgsql(identityAdmin).Options)) await db.Database.EnsureCreatedAsync(token);
        Assert.Equal(0, await ProvisionAsync("identity", "operator_identity"));
        IdentityConnection = new NpgsqlConnectionStringBuilder(identityAdmin) { Username = "operator_identity_user", Password = "Synthetic!Identity123" }.ConnectionString;
        TargetConnection = new NpgsqlConnectionStringBuilder(Database.GetConnectionString()) { Username = "subscription_operator", Password = "Synthetic!Target123" }.ConnectionString;
        app = Factory();
        Environment.SetEnvironmentVariable("OPERATOR_BOOTSTRAP_EMAIL", Email); Environment.SetEnvironmentVariable("OPERATOR_BOOTSTRAP_PASSWORD", Password);
        try { Assert.Equal(0, await OperatorProgram.InitializeAsync(App.Services)); Assert.Equal(1, await OperatorProgram.InitializeAsync(App.Services)); }
        finally { Environment.SetEnvironmentVariable("OPERATOR_BOOTSTRAP_EMAIL", null); Environment.SetEnvironmentVariable("OPERATOR_BOOTSTRAP_PASSWORD", null); }
        using var scope = App.Services.CreateScope(); Actor = await scope.ServiceProvider.GetRequiredService<OperatorIdentityDb>().Users.Select(u => u.Id).SingleAsync(token);
    }

    internal async Task<int> ProvisionAsync(string kind, string database, string? instance = null)
    {
        var token = TestContext.Current.CancellationToken;
        var sql = "\\set database_name '" + database + "'\n\\set instance_id '" + (instance ?? (kind == "target" ? Instance : "synthetic-operator")) +
            "'\n\\set target_password 'Synthetic!Target123'\n\\set identity_password 'Synthetic!Identity123'\n" +
            await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "deploy/subscription-operator-" + kind + ".sql"), token);
        using var process = new Process { StartInfo = new ProcessStartInfo("docker") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        foreach (var arg in new[] { "exec", "-i", Database.Id, "psql", "-U", "postgres", "-d", database, "-v", "ON_ERROR_STOP=1" }) process.StartInfo.ArgumentList.Add(arg);
        process.Start(); var output = process.StandardOutput.ReadToEndAsync(token); var error = process.StandardError.ReadToEndAsync(token);
        await process.StandardInput.WriteAsync(sql.AsMemory(), token); process.StandardInput.Close(); await process.WaitForExitAsync(token);
        await output; await error; return process.ExitCode;
    }

    internal WebApplicationFactory<OperatorProgram> Factory(string? targetId = null) => new WebApplicationFactory<OperatorProgram>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Operator:InstanceId"] = "synthetic-operator",
            ["Operator:KeysDirectory"] = keys,
            ["ConnectionStrings:OperatorIdentity"] = IdentityConnection,
            ["ConnectionStrings:SubscriptionTarget"] = TargetConnection,
            ["SubscriptionTarget:InstanceId"] = targetId ?? Instance,
            ["SubscriptionTarget:Name"] = "Sentetik Salon"
        }));
    });
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FirmaRandevu.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root missing");
    }
    internal static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body, string? csrf = null)
    {
        if (csrf is null)
        {
            using var response = await client.GetAsync("/api/auth/csrf", TestContext.Current.CancellationToken); response.EnsureSuccessStatusCode();
            using var value = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)); csrf = value.RootElement.GetProperty("token").GetString();
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf); return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
    internal async Task<HttpClient> LoginAsync()
    {
        var client = App.CreateClient();
        using var login = await Post(client, "/api/auth/login", new { Email, Password });
        if (login.StatusCode == HttpStatusCode.NoContent)
        {
            using var setup = await Post(client, "/api/auth/mfa/setup", new { Password }); setup.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await setup.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var key = document.RootElement.GetProperty("key").GetString() ?? throw new InvalidOperationException();
            using var enable = await Post(client, "/api/auth/mfa/enable", new { Password, value = AuthenticationTestSupport.GenerateAuthenticatorCode(key) }); enable.EnsureSuccessStatusCode();
            using var again = await Post(client, "/api/auth/login", new { Email, Password }); Assert.Equal(HttpStatusCode.Accepted, again.StatusCode);
        }
        else Assert.Equal(HttpStatusCode.Accepted, login.StatusCode);
        using var scope = App.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<OperatorUser>>();
        var user = await users.FindByEmailAsync(Email) ?? throw new InvalidOperationException();
        var activeKey = await users.GetAuthenticatorKeyAsync(user) ?? throw new InvalidOperationException();
        using var mfa = await Post(client, "/api/auth/mfa/login", new { value = AuthenticationTestSupport.GenerateAuthenticatorCode(activeKey) }); mfa.EnsureSuccessStatusCode();
        return client;
    }
    public async ValueTask DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync(); await Database.DisposeAsync();
    }
}
