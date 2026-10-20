using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Server.Features.Scheduling;
using Xunit;

namespace Server.Tests;

public sealed class NetgsmBookingSmsDeliveryTests
{
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
            ["Netgsm:Enabled"] = "true",
            ["Netgsm:Username"] = "8500000000",
            ["Netgsm:Password"] = "Synthetic!Api123",
            ["Netgsm:Header"] = "SENTETIK",
            ["Netgsm:MaximumUnitPriceTry"] = "0.40"
        };
        if (key is not null) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task SendsOnlyFixedHttpsOtpPostAndPreservesLongReferenceAsString()
    {
        var reference = new string('9', 80);
        var handler = new Handler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.netgsm.com.tr/sms/rest/v2/otp", request.RequestUri?.AbsoluteUri);
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Equal("8500000000:Synthetic!Api123", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization!.Parameter!)));
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(new[] { "msgheader", "msg", "no" }, json.RootElement.EnumerateObject().Select(x => x.Name));
            Assert.Equal("SENTETIK", json.RootElement.GetProperty("msgheader").GetString());
            Assert.Equal("5000000001", json.RootElement.GetProperty("no").GetString());
            var text = json.RootElement.GetProperty("msg").GetString()!;
            Assert.Contains("123456", text); Assert.True(text.All(char.IsAscii)); Assert.InRange(text.Length + 6, 1, 155);
            return Response("{\"code\":\"00\",\"jobid\":\"" + reference + "\"}");
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://untrusted.invalid") };
        var delivery = new NetgsmBookingSmsDelivery(client, Config());
        var result = await delivery.SendAsync("+905000000001", "123456", Guid.NewGuid(), TestContext.Current.CancellationToken);
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
    [InlineData("{\"code\":\"41\"}", BookingSmsOutcome.PermanentFailure)]
    [InlineData("{\"code\":\"50\"}", BookingSmsOutcome.PermanentFailure)]
    [InlineData("{\"code\":\"52\"}", BookingSmsOutcome.PermanentFailure)]
    [InlineData("{\"code\":\"60\"}", BookingSmsOutcome.PermanentFailure)]
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
        var result = await new NetgsmBookingSmsDelivery(client, Config()).SendAsync("+905000000001", "123456", Guid.NewGuid(), TestContext.Current.CancellationToken);
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
        Assert.Equal(expected, (await new NetgsmBookingSmsDelivery(client, Config()).SendAsync("+905000000001", "123456", Guid.NewGuid(), TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("large")]
    [InlineData("long-id")]
    public async Task UncertainTransportAndOversizedRepliesRemainUnknown(string kind)
    {
        var handler = new Handler((_, _) => kind switch
        {
            "timeout" => throw new OperationCanceledException(),
            "network" => throw new HttpRequestException("Synthetic failure"),
            "large" => Task.FromResult(Response(new string(' ', 4097))),
            _ => Task.FromResult(Response("{\"code\":\"00\",\"jobid\":\"" + new string('9', 81) + "\"}"))
        });
        using var client = new HttpClient(handler);
        Assert.Equal(BookingSmsOutcome.Unknown, (await new NetgsmBookingSmsDelivery(client, Config()).SendAsync("+905000000001", "123456", Guid.NewGuid(), TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("Netgsm:Username", "wrong\r\n")]
    [InlineData("Netgsm:Password", "")]
    [InlineData("Netgsm:Header", "İŞLETME")]
    [InlineData("Netgsm:Header", "AB")]
    [InlineData("Netgsm:Header", "   ")]
    [InlineData("Netgsm:MaximumUnitPriceTry", "0")]
    [InlineData("Netgsm:MaximumUnitPriceTry", "0.51")]
    [InlineData("Netgsm:MaximumUnitPriceTry", "0.401")]
    public void InvalidCredentialsHeaderOrPriceFailClosed(string key, string value)
    {
        using var client = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Network forbidden")));
        Assert.Throws<InvalidOperationException>(() => new NetgsmBookingSmsDelivery(client, Config(key, value)));
    }

    [Fact]
    public async Task DisabledAdapterAndInvalidInputNeverReachTransport()
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException("Network forbidden"));
        using var client = new HttpClient(handler);
        var disabled = new NetgsmBookingSmsDelivery(client, new ConfigurationBuilder().Build());
        Assert.False(disabled.Available);
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.SendAsync("+905000000001", "123456", Guid.NewGuid(), TestContext.Current.CancellationToken));
        var enabled = new NetgsmBookingSmsDelivery(client, Config());
        foreach (var pair in new[] { ("+905000000001", "１２３４５６"), ("+904000000001", "123456"), ("05000000001", "123456") })
            Assert.Equal(BookingSmsOutcome.PermanentFailure, (await enabled.SendAsync(pair.Item1, pair.Item2, Guid.NewGuid(), TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(0, handler.Calls);
    }
}
