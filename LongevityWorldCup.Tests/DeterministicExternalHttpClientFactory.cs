using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace LongevityWorldCup.Tests;

/// <summary>
/// Keeps integration hosts off the public network while preserving the real
/// first-party controllers, services, parsing, and caches. Known providers get
/// representative protocol responses; every other outbound host fails closed.
/// </summary>
internal sealed class DeterministicExternalHttpClientFactory : IHttpClientFactory
{
    private readonly ConcurrentQueue<Uri> _requests = new();

    public Uri[] Requests => _requests.ToArray();

    public HttpClient CreateClient(string name)
        => new(new DeterministicExternalHttpMessageHandler(_requests), disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

    private sealed class DeterministicExternalHttpMessageHandler(ConcurrentQueue<Uri> requests)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri
                ?? throw new InvalidOperationException("Outbound test requests must have an absolute URI.");
            requests.Enqueue(uri);

            var response = CreateResponse(uri);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private static HttpResponseMessage CreateResponse(Uri uri)
        {
            if (uri.Host.Equals("api.coingecko.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.Equals("/api/v3/simple/price", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"bitcoin":{"usd":65432.10}}""");
            }

            if (uri.Host.Equals("blockchain.info", StringComparison.OrdinalIgnoreCase))
            {
                if (uri.AbsolutePath.StartsWith("/q/addressbalance/", StringComparison.OrdinalIgnoreCase))
                    return TextResponse("123456789");
                if (uri.AbsolutePath.Equals("/ticker", StringComparison.OrdinalIgnoreCase))
                    return JsonResponse("""{"USD":{"last":65432.10}}""");
            }

            if (uri.Host.Equals("api.blockcypher.com", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.EndsWith("/balance", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{"balance":123456789}""");
            }

            if (uri.Host.Equals("gravatar.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".gravatar.com", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(
                    $"External HTTP is disabled in integration tests for {uri.Host}.",
                    Encoding.UTF8,
                    "text/plain")
            };
        }

        private static HttpResponseMessage JsonResponse(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

        private static HttpResponseMessage TextResponse(string text)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(text, Encoding.UTF8, "text/plain")
            };
    }
}
