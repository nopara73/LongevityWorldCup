using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class XApiClientTests
{
    [Fact]
    public async Task ConcurrentClientsShareOneTokenRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "lwc-x-client-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var config = new Config
            {
                XAccessToken = "old-access",
                XRefreshToken = "old-refresh",
                XApiKey = "client-id",
                XApiSecret = "client-secret"
            }.UseFilePathsForTesting(
                Path.Combine(root, "config.json"),
                Path.Combine(root, "runtime-config.json"));
            var handler = new ConcurrentRefreshHandler();
            var preview = CreatePreviewService();
            var environment = new ProductionEnvironment(root);
            var first = new XApiClient(
                new HttpClient(handler, disposeHandler: false),
                config,
                environment,
                NullLogger<XApiClient>.Instance,
                preview);
            var second = new XApiClient(
                new HttpClient(handler, disposeHandler: false),
                config,
                environment,
                NullLogger<XApiClient>.Instance,
                preview);

            var tweetIds = await Task.WhenAll(
                first.SendTweetAsync("first"),
                second.SendTweetAsync("second"));

            Assert.All(tweetIds, id => Assert.NotNull(id));
            Assert.Equal(1, handler.RefreshRequestCount);
            Assert.Equal(2, handler.OldAccessTweetCount);
            Assert.Equal(2, handler.NewAccessTweetCount);
            Assert.Equal("new-access", config.XAccessToken);
            Assert.Equal("new-refresh", config.XRefreshToken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static XDevPreviewService CreatePreviewService()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        return new XDevPreviewService(
            NullLogger<XDevPreviewService>.Instance,
            new StaticHttpClientFactory(),
            configuration);
    }

    private sealed class ConcurrentRefreshHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _bothOldRequestsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _oldAccessTweetCount;
        private int _newAccessTweetCount;
        private int _refreshRequestCount;

        public int OldAccessTweetCount => Volatile.Read(ref _oldAccessTweetCount);
        public int NewAccessTweetCount => Volatile.Read(ref _newAccessTweetCount);
        public int RefreshRequestCount => Volatile.Read(ref _refreshRequestCount);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/2/tweets")
            {
                var token = request.Headers.Authorization?.Parameter;
                if (token == "old-access")
                {
                    if (Interlocked.Increment(ref _oldAccessTweetCount) == 2)
                        _bothOldRequestsStarted.TrySetResult();

                    await _bothOldRequestsStarted.Task.WaitAsync(cancellationToken);
                    return Json(HttpStatusCode.Unauthorized, """{"title":"Unauthorized","status":401}""");
                }

                Assert.Equal("new-access", token);
                var id = Interlocked.Increment(ref _newAccessTweetCount);
                return Json(HttpStatusCode.Created, "{\"data\":{\"id\":\"tweet-" + id + "\"}}");
            }

            Assert.Equal("/2/oauth2/token", request.RequestUri?.AbsolutePath);
            Interlocked.Increment(ref _refreshRequestCount);
            return Json(HttpStatusCode.OK, """{"access_token":"new-access","refresh_token":"new-refresh"}""");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body)
        {
            return new HttpResponseMessage(statusCode) { Content = new StringContent(body) };
        }
    }

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new HttpClientHandler());
    }

    private sealed class ProductionEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
