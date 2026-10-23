using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Server.Features.Scheduling;
using Xunit;

namespace Server.Tests;

public sealed class NetgsmAppointmentReminderDeliveryTests
{
    private static readonly BookingVerificationTests.Clock Clock = new();
    private static readonly DateTimeOffset StartsAt = new(2026, 10, 17, 12, 0, 37, TimeSpan.FromHours(3));
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return reply(request, token); }
    }
    private static IConfiguration Config(string? key = null, string? value = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Netgsm:RemindersEnabled"] = "true",
            ["Netgsm:Username"] = "8500000000",
            ["Netgsm:Password"] = "Synthetic!Api123",
            ["Netgsm:Header"] = "SENTETIK",
            ["Netgsm:ReminderMaximumUnitPriceTry"] = "0.40"
        };
        if (key is not null) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SendsOnlyFixedHttpsReminderPostWithIstanbulExpiryAndTrackingReference()
    {
        var reference = new string('9', 80);
        var deliveryId = Guid.NewGuid();
        var handler = new Handler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.netgsm.com.tr/sms/rest/v2/send", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Equal("8500000000:Synthetic!Api123", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!)));
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(new[] { "msgheader", "messages", "encoding", "iysfilter", "stopdate", "referansID" }, json.RootElement.EnumerateObject().Select(x => x.Name));
            Assert.Equal("SENTETIK", json.RootElement.GetProperty("msgheader").GetString());
            var recipient = Assert.Single(json.RootElement.GetProperty("messages").EnumerateArray());
            Assert.Equal(new[] { "msg", "no" }, recipient.EnumerateObject().Select(x => x.Name));
            Assert.Equal("5000000001", recipient.GetProperty("no").GetString());
            var text = recipient.GetProperty("msg").GetString()!;
            Assert.Equal("Randevu hatirlatma: 17.10.2026 12:00 (Turkiye saati).", text);
            Assert.True(text.All(char.IsAscii)); Assert.InRange(text.Length + 6, 1, 120);
            Assert.Equal("UTF-8", json.RootElement.GetProperty("encoding").GetString());
            Assert.Equal("0", json.RootElement.GetProperty("iysfilter").GetString());
            Assert.Equal("171020261200", json.RootElement.GetProperty("stopdate").GetString());
            Assert.Equal(deliveryId.ToString("N"), json.RootElement.GetProperty("referansID").GetString());
            return Response("{\"code\":\"00\",\"jobid\":\"" + reference + "\"}");
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://untrusted.invalid") };
        var delivery = new NetgsmAppointmentReminderDelivery(client, Config(), Clock);
        var result = await delivery.SendAsync("+905000000001", StartsAt, deliveryId, TestContext.Current.CancellationToken);
        Assert.Equal(new BookingSmsResult(BookingSmsOutcome.Accepted, reference), result); Assert.Equal(1, handler.Calls);
        Assert.Equal(0.40m, delivery.MaximumUnitPriceTry);
    }

    [Theory]
    [InlineData("{\"code\":\"00\"}", BookingSmsOutcome.Unknown)]
    [InlineData("{\"code\":\"00\",\"jobid\":42}", BookingSmsOutcome.Unknown)]
    [InlineData("{\"code\":\"00\",\"jobid\":\"\"}", BookingSmsOutcome.Unknown)]
    [InlineData("{\"code\":\"00\",\"jobid\":\"abc\"}", BookingSmsOutcome.Unknown)]
    [InlineData("{\"code\":\"20\"}", BookingSmsOutcome.PermanentFailure)]
    [InlineData("{\"code\":\"30\",\"jobid\":null}", BookingSmsOutcome.PermanentFailure)]
    [InlineData("{\"code\":\"40\"}", BookingSmsOutcome.PermanentFailure)]
    [InlineData("{\"code\":\"51\"}", BookingSmsOutcome.PermanentFailure)]
    [InlineData("{\"code\":\"50\"}", BookingSmsOutcome.PermanentFailure)]
    [InlineData("{\"code\":\"85\"}", BookingSmsOutcome.Unknown)]
    [InlineData("{\"code\":\"80\"}", BookingSmsOutcome.Unknown)]
    [InlineData("{\"code\":\"70\"}", BookingSmsOutcome.PermanentFailure)]
    [InlineData("{\"code\":\"100\"}", BookingSmsOutcome.Unknown)]
    [InlineData("{\"code\":\"20\",\"jobid\":\"123\"}", BookingSmsOutcome.Unknown)]
    [InlineData("{\"code\":\"20\",\"jobid\":123}", BookingSmsOutcome.Unknown)]
    [InlineData("{\"code\":\"00\",\"code\":\"20\",\"jobid\":\"123\"}", BookingSmsOutcome.Unknown)]
    [InlineData("[]", BookingSmsOutcome.Unknown)]
    [InlineData("<html>error</html>", BookingSmsOutcome.Unknown)]
    public async Task DoesNotMistakeHttpSuccessOrAmbiguousResponseForAcceptance(string body, BookingSmsOutcome expected)
    {
        var handler = new Handler((_, _) => Task.FromResult(Response(body)));
        using var client = new HttpClient(handler);
        var result = await new NetgsmAppointmentReminderDelivery(client, Config(), Clock).SendAsync("+905000000001", StartsAt, Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Outcome); Assert.Null(result.ProviderReference); Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(401, BookingSmsOutcome.PermanentFailure)]
    [InlineData(403, BookingSmsOutcome.PermanentFailure)]
    [InlineData(429, BookingSmsOutcome.Unknown)]
    [InlineData(500, BookingSmsOutcome.Unknown)]
    [InlineData(302, BookingSmsOutcome.Unknown)]
    public async Task DoesNotRetryRateLimitServerErrorOrRedirect(int status, BookingSmsOutcome expected)
    {
        var handler = new Handler((_, _) => Task.FromResult(Response("{}", (HttpStatusCode)status)));
        using var client = new HttpClient(handler);
        Assert.Equal(expected, (await new NetgsmAppointmentReminderDelivery(client, Config(), Clock).SendAsync("+905000000001", StartsAt, Guid.NewGuid(), TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("large")]
    [InlineData("long-id")]
    [InlineData("read")]
    [InlineData("depth")]
    public async Task UncertainTransportAndOversizedRepliesRemainUnknown(string kind)
    {
        var handler = new Handler((_, _) => kind switch
        {
            "timeout" => throw new OperationCanceledException(),
            "network" => throw new HttpRequestException("Synthetic failure"),
            "read" => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) }),
            "depth" => Task.FromResult(Response("{\"code\":\"00\",\"jobid\":\"123\",\"extra\":" + new string('[', 10) + "0" + new string(']', 10) + "}")),
            "large" => Task.FromResult(Response(new string(' ', 4097))),
            _ => Task.FromResult(Response("{\"code\":\"00\",\"jobid\":\"" + new string('9', 81) + "\"}"))
        });
        using var client = new HttpClient(handler);
        Assert.Equal(BookingSmsOutcome.Unknown, (await new NetgsmAppointmentReminderDelivery(client, Config(), Clock).SendAsync("+905000000001", StartsAt, Guid.NewGuid(), TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("Synthetic read failure"));
    }

    [Theory]
    [InlineData(59, 59, BookingSmsOutcome.PermanentFailure)]
    [InlineData(60, 0, BookingSmsOutcome.Accepted)]
    [InlineData(1260, 59, BookingSmsOutcome.Accepted)]
    [InlineData(1261, 0, BookingSmsOutcome.PermanentFailure)]
    [InlineData(-1, 0, BookingSmsOutcome.PermanentFailure)]
    public async Task ProviderValidityNeverRoundsPastAppointmentAndRejectsOutsideWindow(int minutes, int seconds, BookingSmsOutcome expected)
    {
        var handler = new Handler((_, _) => Task.FromResult(Response("{\"code\":\"00\",\"jobid\":\"123\"}")));
        using var client = new HttpClient(handler);
        var result = await new NetgsmAppointmentReminderDelivery(client, Config(), Clock).SendAsync("+905000000001",
            Clock.Now.AddMinutes(minutes).AddSeconds(seconds), Guid.NewGuid(), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(expected == BookingSmsOutcome.Accepted ? 1 : 0, handler.Calls);
    }

    [Theory]
    [InlineData("Netgsm:Username", "wrong\r\n")]
    [InlineData("Netgsm:Password", "")]
    [InlineData("Netgsm:Header", "İŞLETME")]
    [InlineData("Netgsm:Header", "AB")]
    [InlineData("Netgsm:Header", "   ")]
    [InlineData("Netgsm:ReminderMaximumUnitPriceTry", "0")]
    [InlineData("Netgsm:ReminderMaximumUnitPriceTry", "0.51")]
    [InlineData("Netgsm:ReminderMaximumUnitPriceTry", "0.401")]
    public void InvalidCredentialsHeaderOrPriceFailClosed(string key, string value)
    {
        using var client = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Network forbidden")));
        Assert.Throws<InvalidOperationException>(() => new NetgsmAppointmentReminderDelivery(client, Config(key, value), Clock));
    }

    [Fact]
    public async Task DisabledAdapterAndInvalidInputNeverReachTransport()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("Network forbidden"));
        using var client = new HttpClient(handler);
        var disabled = new NetgsmAppointmentReminderDelivery(client, new ConfigurationBuilder().Build(), Clock);
        Assert.False(disabled.Available);
        Assert.Equal(0, disabled.MaximumUnitPriceTry);
        Assert.False(new NetgsmBookingSmsDelivery(client, Config()).Available);
        Assert.False(new NetgsmAppointmentReminderDelivery(client, Config("Netgsm:RemindersEnabled", "false"), Clock).Available);
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.SendAsync("+905000000001", StartsAt, Guid.NewGuid(), TestContext.Current.CancellationToken));
        var enabled = new NetgsmAppointmentReminderDelivery(client, Config(), Clock);
        foreach (var pair in new[] { ("+905000000001", StartsAt.AddDays(-1)), ("+904000000001", StartsAt), ("05000000001", StartsAt) })
            Assert.Equal(BookingSmsOutcome.PermanentFailure, (await enabled.SendAsync(pair.Item1, pair.Item2, Guid.NewGuid(), TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(BookingSmsOutcome.PermanentFailure, (await enabled.SendAsync("+905000000001", StartsAt, Guid.Empty, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(BookingSmsOutcome.PermanentFailure, (await enabled.SendAsync("+90500000000１", StartsAt, Guid.NewGuid(), TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(0, handler.Calls);
    }
}
