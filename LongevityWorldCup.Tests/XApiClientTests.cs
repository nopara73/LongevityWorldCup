using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    [Theory]
    [InlineData(null)]
    [InlineData("expired-access")]
    public async Task OAuth1PostsWithoutDependingOnAnOAuth2Token(string? oauth2Token)
    {
        var config = OAuth1Config();
        config.XAccessToken = oauth2Token;
        var requestCount = 0;
        var handler = new StubHandler(async request =>
        {
            requestCount++;
            Assert.Equal("/2/tweets", request.RequestUri?.AbsolutePath);
            AssertOAuth1Signature(request);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("Keep the original caption", body.RootElement.GetProperty("text").GetString());
            Assert.Equal("media-1", body.RootElement.GetProperty("media").GetProperty("media_ids")[0].GetString());
            Assert.Equal("parent-1", body.RootElement.GetProperty("reply").GetProperty("in_reply_to_tweet_id").GetString());
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"data":{"id":"tweet-1"}}""")
            };
        });
        var client = CreateClient(config, handler);

        Assert.True(client.IsConfigured);
        Assert.Equal("tweet-1", await client.SendTweetAsync("Keep the original caption", ["media-1"], "parent-1"));
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task OAuth1MediaUploadDoesNotRequireAnOAuth2Token()
    {
        var handler = new StubHandler(request =>
        {
            Assert.Equal("/1.1/media/upload.json", request.RequestUri?.AbsolutePath);
            AssertOAuth1Signature(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"media_id_string":"media-1"}""")
            });
        });
        var client = CreateClient(OAuth1Config(), handler);
        using var content = new MemoryStream([1, 2, 3]);

        Assert.Equal("media-1", await client.UploadMediaAsync(content, "image/png"));
    }

    [Fact]
    public async Task RejectedOAuth1PostDoesNotRefreshTheSeparateOAuth2Account()
    {
        var config = OAuth1Config();
        config.XAccessToken = "old-access";
        config.XRefreshToken = "old-refresh";
        config.XApiKey = "client-id";
        config.XApiSecret = "client-secret";
        var requestCount = 0;
        var handler = new StubHandler(request =>
        {
            requestCount++;
            Assert.Equal("/2/tweets", request.RequestUri?.AbsolutePath);
            AssertOAuth1Signature(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("""{"title":"Unauthorized","status":401}""")
            });
        });

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateClient(config, handler).SendTweetAsync("Original post"));
        Assert.Equal(1, requestCount);
    }

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

    private static Config OAuth1Config() => new()
    {
        XConsumerKey = "consumer-key",
        XConsumerSecret = "consumer-secret",
        XUserAccessToken = "user-token",
        XUserAccessTokenSecret = "user-secret"
    };

    private static XApiClient CreateClient(Config config, HttpMessageHandler handler) => new(
        new HttpClient(handler),
        config,
        new ProductionEnvironment(Path.GetTempPath()),
        NullLogger<XApiClient>.Instance,
        CreatePreviewService());

    private static void AssertOAuth1Signature(HttpRequestMessage request)
    {
        Assert.Equal("OAuth", request.Headers.Authorization?.Scheme);
        var parameters = request.Headers.Authorization!.Parameter!.Split(", ")
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1].Trim('"')));
        Assert.Equal("consumer-key", parameters["oauth_consumer_key"]);
        Assert.Equal("user-token", parameters["oauth_token"]);
        Assert.Equal("HMAC-SHA1", parameters["oauth_signature_method"]);
        Assert.False(string.IsNullOrWhiteSpace(parameters["oauth_nonce"]));
        Assert.True(long.TryParse(parameters["oauth_timestamp"], out _));

        var signedParameters = string.Join("&", parameters.Where(pair => pair.Key != "oauth_signature")
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var signatureBase = $"POST&{Uri.EscapeDataString(request.RequestUri!.AbsoluteUri)}&{Uri.EscapeDataString(signedParameters)}";
        using var hmac = new HMACSHA1(Encoding.ASCII.GetBytes("consumer-secret&user-secret"));
        Assert.Equal(Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(signatureBase))), parameters["oauth_signature"]);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
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
