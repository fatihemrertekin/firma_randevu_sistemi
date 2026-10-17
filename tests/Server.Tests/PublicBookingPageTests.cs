using System.Net;
using Xunit;

namespace Server.Tests;

[Collection(AuthenticationTestCollection.Name)]
public sealed class PublicBookingPageTests
{
    [Theory]
    [InlineData("/randevu")]
    [InlineData("/randevu/sonuc/00000000-0000-0000-0000-000000000001")]
    [InlineData("/randevu/gecersiz")]
    public async Task GuestPagesKeepProofsOutOfCacheAndReferrers(string path)
    {
        await using var fixture = new BookingVerificationTests.Fixture { Enabled = false };
        await fixture.StartAsync();
        using var client = fixture.Client();
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
