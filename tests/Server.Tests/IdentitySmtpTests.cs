using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MimeKit;
using Server.Features.Identity;
using Server.Infrastructure;
using Xunit;
using static Server.Tests.Support.TestAccounts;
using static Server.Tests.Support.AuthenticationTestSupport;
using static Server.Tests.Support.IdentityTestEnvironment;
using static Server.Tests.Support.EmailTestSupport;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class IdentitySmtpTests
{
    private static Dictionary<string, string?> SmtpSettings() => new()
    {
        ["IdentityEmail:Mode"] = "Smtp",
        ["IdentityEmail:Smtp:Host"] = "localhost",
        ["IdentityEmail:Smtp:Port"] = "587",
        ["IdentityEmail:Smtp:Username"] = "sender@example.test",
        ["IdentityEmail:Smtp:Password"] = "synthetic-smtp-password",
        ["IdentityEmail:Smtp:FromAddress"] = "sender@example.test",
        ["IdentityEmail:Smtp:DailyLimit"] = "3",
        ["IdentityEmail:PublicOrigin"] = "http://localhost:8081",
        ["IdentityEmail:Smtp:AllowedRecipients:0"] = Email,
        ["OwnerPasswordReset:Enabled"] = "true",
        ["Auth:InstanceId"] = RecoveryInstance
    };

    private sealed class SmtpCapture : IIdentityEmailTransport
    {
        public List<(string Recipient, string Body, string MessageId)> Messages { get; } = [];
        public bool? RetryableFailure { get; set; }
        public bool Timeout { get; set; }
        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            lock (Messages) Messages.Add((message.To.Mailboxes.Single().Address,
                Assert.IsType<string>(message.TextBody), Assert.IsType<string>(message.MessageId)));
            if (Timeout) throw new OperationCanceledException("synthetic timeout");
            if (RetryableFailure is bool retryable) throw new IdentityEmailDeliveryException(retryable);
            return Task.CompletedTask;
        }
    }

    private static WebApplicationFactory<Program> SmtpApp(RecoverySeed seed, SmtpCapture transport,
        Dictionary<string, string?>? settings = null) => seed.App.WithWebHostBuilder(builder =>
    {
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings ?? SmtpSettings()));
        builder.ConfigureServices(services =>
        {
            foreach (var descriptor in services.Where(entry => entry.ServiceType == typeof(IHostedService) &&
                entry.ImplementationType == typeof(OwnerResetDeliveryWorker)).ToArray()) services.Remove(descriptor);
            services.AddSingleton<IIdentityEmailTransport>(transport);
        });
    });

    [Theory]
    [InlineData("IdentityEmail:Smtp:Host", "bad\r\nhost")]
    [InlineData("IdentityEmail:Smtp:Port", "0")]
    [InlineData("IdentityEmail:Smtp:Port", "65536")]
    [InlineData("IdentityEmail:Smtp:Password", "")]
    [InlineData("IdentityEmail:Smtp:FromAddress", "sender@example.test\r\nBcc: other@example.test")]
    [InlineData("IdentityEmail:Smtp:DailyLimit", "0")]
    [InlineData("IdentityEmail:Smtp:DailyLimit", "501")]
    [InlineData("IdentityEmail:Smtp:AllowedRecipients:0", "bad address")]
    [InlineData("IdentityEmail:PublicOrigin", "http://remote.example.test")]
    [InlineData("IdentityEmail:PublicOrigin", "https://example.test/?token=bad")]
    [InlineData("IdentityEmail:PublicOrigin", "https://user:pass@example.test/")]
    [InlineData("RecoveryEmail:LocalPickupDirectory", "/private-mail")]
    [InlineData("Auth:InstanceId", "")]
    public void SmtpRejectsUnsafeConfigurationWithoutExposingSecrets(string key, string value)
    {
        var settings = SmtpSettings(); settings[key] = value;
        var error = Assert.Throws<InvalidOperationException>(() => new IdentityEmailOptions(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new EmailEnvironment { EnvironmentName = "Development" }));
        Assert.DoesNotContain("synthetic-smtp-password", error.ToString());
    }

    [Fact]
    public void SmtpRequiresExplicitDevelopmentRecipientsAndHttpsInProduction()
    {
        var settings = SmtpSettings();
        settings.Remove("IdentityEmail:Smtp:AllowedRecipients:0");
        Assert.Throws<InvalidOperationException>(() => new IdentityEmailOptions(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new EmailEnvironment { EnvironmentName = "Development" }));
        Assert.Throws<InvalidOperationException>(() => new IdentityEmailOptions(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new EmailEnvironment()));
        settings["IdentityEmail:PublicOrigin"] = "https://salon.example.test";
        var options = new IdentityEmailOptions(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), new EmailEnvironment());
        Assert.True(options.CanDeliver("owner@gmail.com"));
        Assert.True(options.CanDeliver("owner@hotmail.com"));
        Assert.True(options.CanDeliver("info@firmaadi.com"));
        Assert.False(options.CanDeliver("owner@example.test\r\nBcc: other@example.test"));
        Assert.False(options.CanDeliver("Someone <owner@example.test>"));
        Assert.False(options.CanDeliver("invalid"));
    }

    [Fact]
    public async Task SmtpQuotaIsAtomicPersistsAcrossRestartAndChangesDayWithoutSavingSecrets()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var original = seed.App;
        var transport = new SmtpCapture();
        var settings = SmtpSettings();
        await using var app = SmtpApp(seed, transport, settings);
        var sender = app.Services.GetRequiredService<IOwnerPasswordResetDelivery>();
        var grant = Guid.NewGuid();
        async Task<bool> SendAsync()
        {
            try { await sender.DeliverAsync(Email, "synthetic-proof", DateTimeOffset.UtcNow.AddMinutes(30), grant, TestContext.Current.CancellationToken); return true; }
            catch (IdentityEmailDeliveryException failure) { Assert.False(failure.Retryable); return false; }
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => SendAsync()));
        Assert.Equal(3, results.Count(result => result));
        Assert.Equal(3, transport.Messages.Count);
        Assert.Single(transport.Messages.Select(message => message.MessageId).Distinct());
        Assert.All(transport.Messages, message =>
        {
            Assert.Equal(Email, message.Recipient);
            Assert.Contains("http://localhost:8081/#reset-owner-password=synthetic-proof", message.Body);
            Assert.Contains("İki aşamalı girişiniz korunur", message.Body);
            Assert.DoesNotContain("synthetic-smtp-password", message.Body);
        });
        await using var restarted = SmtpApp(seed, transport, settings);
        var cap = await Assert.ThrowsAsync<IdentityEmailDeliveryException>(() => restarted.Services.GetRequiredService<IOwnerEmailVerificationDelivery>()
            .DeliverAsync(Email, "synthetic-email-proof", DateTimeOffset.UtcNow.AddMinutes(30), TestContext.Current.CancellationToken));
        Assert.False(cap.Retryable);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var quota = await db.IdentityEmailQuotas.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, quota.Attempts);
        // Yesterday's bucket cannot consume today's quota. No test-only clock bypass in the sender.
        await db.IdentityEmailQuotas.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        db.IdentityEmailQuotas.Add(new IdentityEmailQuota { Day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), Attempts = 500 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        await restarted.Services.GetRequiredService<IOwnerEmailVerificationDelivery>().DeliverAsync(Email, "synthetic-email-proof",
            DateTimeOffset.UtcNow.AddMinutes(30), TestContext.Current.CancellationToken);
        Assert.Contains("verify-owner-email=synthetic-email-proof", transport.Messages[^1].Body);
        Assert.Equal(2, await db.IdentityEmailQuotas.CountAsync(TestContext.Current.CancellationToken));
        await AssertOriginalStateAsync(app, seed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SmtpWorkerRetriesTransientErrorsButTerminatesPermanentErrors(bool retryable)
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var original = seed.App;
        var transport = new SmtpCapture { RetryableFailure = retryable };
        await using var app = SmtpApp(seed, transport);
        await VerifyForSelfResetAsync(app, seed.OwnerId);
        using var client = app.CreateClient();
        using var options = await client.GetAsync("/api/auth/password-reset-options", TestContext.Current.CancellationToken);
        Assert.Contains("true", await options.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var requested = await AskSelfResetAsync(client);
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        await ProcessSelfResetAsync(app);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.OwnerSelfServiceResets.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(retryable ? "Pending" : "Failed", job.Status);
        Assert.Equal(retryable, job.ProtectedPayload is not null);
        Assert.Equal(1, (await db.IdentityEmailQuotas.SingleAsync(TestContext.Current.CancellationToken)).Attempts);
        await ProcessSelfResetAsync(app);
        Assert.Single(transport.Messages);
        if (retryable)
        {
            await db.OwnerSelfServiceResets.ExecuteUpdateAsync(setters => setters.SetProperty(entry => entry.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)),
                TestContext.Current.CancellationToken);
            transport.RetryableFailure = null;
            await ProcessSelfResetAsync(app);
            db.ChangeTracker.Clear();
            job = await db.OwnerSelfServiceResets.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal("Delivered", job.Status);
            Assert.Null(job.ProtectedPayload);
            Assert.Equal(transport.Messages[0].MessageId, transport.Messages[1].MessageId);
        }
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task SmtpVerificationTimeoutRevokesProofAndNeverReturnsSuccess()
    {
        await using var database = RecoveryDatabase();
        await database.StartAsync(TestContext.Current.CancellationToken);
        var seed = await CreateRecoveryAppAsync(database.GetConnectionString());
        await using var original = seed.App;
        var transport = new SmtpCapture { Timeout = true };
        await using var app = SmtpApp(seed, transport);
        using var owner = await InviteOwnerAsync(seed with { App = app });
        using var response = await RequestEmailAsync(owner);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain("synthetic-smtp-password", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var scope = app.Services.CreateScope();
        var record = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OwnerRecoveryEmails.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(record.TokenHash);
        Assert.Null(record.VerifiedAt);
        await AssertOriginalStateAsync(app, seed);
    }

    [Fact]
    public async Task MailKitRefusesServerWithoutStartTlsBeforeCredentialsOrMessageAreSent()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var settings = SmtpSettings();
        settings["IdentityEmail:Smtp:Port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString();
        var commands = new List<string>();
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 localhost synthetic SMTP");
            var hello = await reader.ReadLineAsync(timeout.Token);
            if (hello is not null) commands.Add(hello);
            await writer.WriteLineAsync("250-localhost\r\n250 AUTH PLAIN");
            var next = await reader.ReadLineAsync(timeout.Token);
            if (next is not null) commands.Add(next);
        }, timeout.Token);
        var sender = new SmtpIdentityEmailTransport(new IdentityEmailOptions(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new EmailEnvironment { EnvironmentName = "Development" }));
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse("sender@example.test")); message.To.Add(MailboxAddress.Parse(Email));
        message.Body = new TextPart("plain") { Text = "synthetic-only" };
        var failure = await Assert.ThrowsAsync<IdentityEmailDeliveryException>(() => sender.SendAsync(message, timeout.Token));
        Assert.False(failure.Retryable);
        await server;
        Assert.DoesNotContain(commands, command => command.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commands, command => command.StartsWith("MAIL", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("synthetic-smtp-password", failure.ToString());
    }

    [Fact]
    public async Task MailKitRejectsUntrustedTlsCertificateBeforeAuthentication()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        var settings = SmtpSettings();
        settings["IdentityEmail:Smtp:Port"] = ((IPEndPoint)listener.LocalEndpoint).Port.ToString();
        var commands = new List<string>();
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 localhost synthetic SMTP");
            commands.Add((await reader.ReadLineAsync(timeout.Token)) ?? "");
            await writer.WriteLineAsync("250-localhost\r\n250 STARTTLS");
            commands.Add((await reader.ReadLineAsync(timeout.Token)) ?? "");
            await writer.WriteLineAsync("220 Begin TLS");
            await using var tls = new SslStream(stream, leaveInnerStreamOpen: true);
            try
            {
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, timeout.Token);
                using var secureReader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
                var next = await secureReader.ReadLineAsync(timeout.Token);
                if (next is not null) commands.Add(next);
            }
            catch (Exception failure) when (failure is IOException or System.Security.Authentication.AuthenticationException) { }
        }, timeout.Token);
        var sender = new SmtpIdentityEmailTransport(new IdentityEmailOptions(new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            new EmailEnvironment { EnvironmentName = "Development" }));
        using var message = new MimeMessage();
        var failure = await Assert.ThrowsAsync<IdentityEmailDeliveryException>(() => sender.SendAsync(message, timeout.Token));
        Assert.False(failure.Retryable);
        await server;
        Assert.Contains("STARTTLS", commands);
        Assert.DoesNotContain(commands, command => command.StartsWith("AUTH", StringComparison.OrdinalIgnoreCase));
    }
}
