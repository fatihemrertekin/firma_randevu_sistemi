using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Server.Features.Identity;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.TestAccounts;

namespace Server.Tests.Support;

internal static class EmailTestSupport
{
    internal const string RecoveryEmailPath = "/api/auth/recovery-email/";

    internal static async Task<HttpResponseMessage> RequestEmailAsync(HttpClient client) =>
        await PostAsync(client, RecoveryEmailPath + "request", new { }, await GetCsrfAsync(client));

    internal sealed class EmailEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Server";
        public string WebRootPath { get; set; } = "/app/wwwroot";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "/app";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    internal const string SelfResetPath = "/api/auth/password-reset-request";

    internal static async Task VerifyForSelfResetAsync(WebApplicationFactory<Program> app, Guid ownerId)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await db.Users.SingleAsync(entry => entry.Id == ownerId, TestContext.Current.CancellationToken);
        db.OwnerRecoveryEmails.Add(new OwnerRecoveryEmail
        {
            OwnerId = ownerId,
            EmailHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(owner.NormalizedEmail ?? ""))),
            StampHash = "unused-verified-stamp",
            RequestedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
            VerifiedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> AskSelfResetAsync(HttpClient client, string email = Email) =>
        await PostAsync(client, SelfResetPath, new { email }, await GetCsrfAsync(client));

    internal static async Task ProcessSelfResetAsync(WebApplicationFactory<Program> app)
    {
        using var worker = ActivatorUtilities.CreateInstance<OwnerResetDeliveryWorker>(app.Services);
        await worker.ProcessOneAsync(TestContext.Current.CancellationToken);
    }
}
