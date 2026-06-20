using System.Net;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BitcoinDataServiceTests
{
    [Fact]
    public async Task GetBtcUsdAsync_UsesPrimaryPriceApiAndCachesResult()
    {
        var handler = new RecordingHandler(request => request.RequestUri?.Host switch
        {
            "api.coingecko.com" => JsonResponse("""{"bitcoin":{"usd":65432.10}}"""),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });
        var service = CreateService(handler);

        var first = await service.GetBtcUsdAsync();
        var second = await service.GetBtcUsdAsync();

        Assert.Equal(65432.10m, first);
        Assert.Equal(first, second);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd", request);
    }

    [Fact]
    public async Task GetBtcUsdAsync_FallsBackToBlockchainTickerWhenPrimaryFails()
    {
        var handler = new RecordingHandler(request => request.RequestUri?.Host switch
        {
            "api.coingecko.com" => new HttpResponseMessage(HttpStatusCode.BadGateway),
            "blockchain.info" => JsonResponse("""{"USD":{"last":54321.98}}"""),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });
        var service = CreateService(handler);

        var result = await service.GetBtcUsdAsync();

        Assert.Equal(54321.98m, result);
        Assert.Equal(
            [
                "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd",
                "https://blockchain.info/ticker"
            ],
            handler.Requests);
    }

    [Fact]
    public async Task GetBtcUsdAsync_ThrowsWhenPrimaryAndFallbackFail()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetBtcUsdAsync());

        Assert.Equal("Both primary and fallback APIs failed for BTC to USD rate.", ex.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetTotalReceivedSatoshisAsync_UsesPrimaryBalanceApiAndCachesResult()
    {
        var handler = new RecordingHandler(request => request.RequestUri?.Host switch
        {
            "blockchain.info" => TextResponse("123456789"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });
        var service = CreateService(handler);

        var first = await service.GetTotalReceivedSatoshisAsync();
        var second = await service.GetTotalReceivedSatoshisAsync();

        Assert.Equal(123456789L, first);
        Assert.Equal(first, second);
        var request = Assert.Single(handler.Requests);
        Assert.StartsWith("https://blockchain.info/q/addressbalance/", request, StringComparison.Ordinal);
        Assert.EndsWith("?confirmations=3", request, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTotalReceivedSatoshisAsync_FallsBackToBlockCypherBalance()
    {
        var handler = new RecordingHandler(request => request.RequestUri?.Host switch
        {
            "blockchain.info" => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            "api.blockcypher.com" => JsonResponse("""{"balance":987654321}"""),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}")
        });
        var service = CreateService(handler);

        var result = await service.GetTotalReceivedSatoshisAsync();

        Assert.Equal(987654321L, result);
        Assert.Equal(2, handler.Requests.Count);
        Assert.StartsWith(
            "https://api.blockcypher.com/v1/btc/main/addrs/",
            handler.Requests[1],
            StringComparison.Ordinal);
        Assert.EndsWith("/balance?confirmations=3", handler.Requests[1], StringComparison.Ordinal);
    }

    private static BitcoinDataService CreateService(RecordingHandler handler)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new BitcoinDataService(
            new StaticHttpClientFactory(new HttpClient(handler)),
            cache,
            events: null!,
            NullLogger<BitcoinDataService>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        };

    private static HttpResponseMessage TextResponse(string text)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(text)
        };

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri?.AbsoluteUri ?? string.Empty);
            return Task.FromResult(responseFactory(request));
        }
    }
}
