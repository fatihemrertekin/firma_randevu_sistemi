using Microsoft.Extensions.Configuration;
using Server.Features.Scheduling;
using Xunit;

namespace Server.Tests;

public sealed class BookingHmacBindingTests
{
    [Theory]
    [InlineData("fr-synthetic-hmac-vector-a", "475BB41BF7B86D3C78F501E40E5B238D8B655D44328F6E0DCC5CDC6CE2668744")]
    [InlineData("fr-synthetic-hmac-vector-b", "4E0092FEE5B807C5AFA451BFE71B0BCF4C82C6CB249641FB934120D51C5F2FE0")]
    public void RecoveryPackageKeyBindingMatchesApplicationContract(string instance, string expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:InstanceId"] = instance,
            ["BookingVerification:Enabled"] = "true",
            ["BookingSms:DispatchEnabled"] = "false",
            ["BookingVerification:HmacKey"] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(x => (byte)x).ToArray())
        }).Build();
        var security = new BookingVerificationSecurity(config, new NoSend());
        Assert.Equal(expected, security.KeyId);
        Assert.False(security.DispatchEnabled);
    }

    private sealed class NoSend : IBookingSmsDelivery
    {
        public bool Available => true;
        public decimal MaximumUnitPriceTry => 0.10m;
        public Task<BookingSmsResult> SendAsync(string phone, string code, Guid deliveryId, CancellationToken token) =>
            throw new InvalidOperationException("Bu sözleşme testinde gönderim yapılamaz.");
    }
}
