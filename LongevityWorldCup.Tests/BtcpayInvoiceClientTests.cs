using System.Net;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BtcpayInvoiceClientTests
{
    [Fact]
    public async Task BtcpayFailuresDoNotExposeRawResponseBodies()
    {
        var client = new BtcpayInvoiceClient(new StaticHttpClientFactory(
            HttpStatusCode.BadRequest,
            "{\"error\":\"secret-store-detail\",\"buyerEmail\":\"user@example.test\"}"));
        var config = new Config
        {
            BTCPayBaseUrl = "https://btcpay.example.test",
            BTCPayStoreId = "store",
            BTCPayGreenfieldApiKey = "secret"
        };

        var create = await client.CreateInvoiceAsync(
            config,
            new BtcpayInvoiceCreateRequest(25m, "USD", "order-1", "user@example.test", "User", new Dictionary<string, object?>()));
        var lookup = await client.GetInvoiceAsync(config, "invoice-1");

        Assert.False(create.Success);
        Assert.False(lookup.Success);
        Assert.Equal("BTCPay API returned HTTP 400.", create.Error);
        Assert.Equal("BTCPay API returned HTTP 400.", lookup.Error);
        Assert.DoesNotContain("secret-store-detail", create.Error);
        Assert.DoesNotContain("user@example.test", lookup.Error);
    }

    private sealed class StaticHttpClientFactory(HttpStatusCode statusCode, string body) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(new StaticHandler(statusCode, body));
    }

    private sealed class StaticHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            });
    }
}
