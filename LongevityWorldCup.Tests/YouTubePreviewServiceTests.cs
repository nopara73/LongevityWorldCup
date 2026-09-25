using System.Net;
using System.Text;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class YouTubePreviewServiceTests
{
    private const string Id = "usE4x1Gyss0";
    private const string Metadata = """{"title":"A real title & its findings","author_name":"The channel","html":"<script>untrusted</script>","thumbnail_url":"http://127.0.0.1/private"}""";

    [Theory]
    [InlineData("https://youtu.be/usE4x1Gyss0?t=90")]
    [InlineData("https://www.youtube.com/watch?v=usE4x1Gyss0&list=anything")]
    [InlineData("https://m.youtube.com/shorts/usE4x1Gyss0")]
    [InlineData("https://music.youtube.com/watch?v=usE4x1Gyss0")]
    [InlineData("https://youtube.com/embed/usE4x1Gyss0/")]
    [InlineData("https://youtube.com/live/usE4x1Gyss0")]
    public void RecognizesOnlyVideoUrls(string url) => Assert.Equal(Id, YouTubePreviewService.TryGetVideoId(url));

    [Theory]
    [InlineData("https://youtube.com.evil.test/watch?v=usE4x1Gyss0")]
    [InlineData("https://youtube.com:8443/watch?v=usE4x1Gyss0")]
    [InlineData("https://name:password@youtube.com/watch?v=usE4x1Gyss0")]
    [InlineData("https://youtu.be/usE4x1Gyss0/extra")]
    [InlineData("https://youtube.com/@channel?v=usE4x1Gyss0")]
    [InlineData("https://youtube.com/playlist?list=abc")]
    [InlineData("file:///watch?v=usE4x1Gyss0")]
    public void LeavesOtherUrlsAlone(string url) => Assert.Null(YouTubePreviewService.TryGetVideoId(url));

    [Fact]
    public async Task Fetch_CoalescesConcurrentReadsAndCachesOnlySafeMetadata()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new Handler(async (request, ct) =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal("https://www.youtube.com/oembed?url=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3DusE4x1Gyss0&format=json", request.RequestUri!.AbsoluteUri);
            called.TrySetResult();
            await release.Task.WaitAsync(ct);
            return Json(Metadata);
        });
        using var service = Create(handler);
        var requests = Enumerable.Range(0, 12).Select(_ => service.FetchAsync(Id)).ToArray();
        await called.Task;
        release.SetResult();
        var results = await Task.WhenAll(requests);
        var preview = Assert.IsType<YouTubePreview>(results[0]);
        Assert.All(results, result => Assert.Same(preview, result));
        Assert.Same(preview, await service.FetchAsync(Id));
        Assert.Equal(1, calls);
        Assert.Equal("A real title & its findings", preview.Title);
        Assert.Equal("The channel", preview.AuthorName);
        Assert.Equal($"https://i.ytimg.com/vi/{Id}/hqdefault.jpg", preview.ThumbnailUrl);
    }

    [Theory]
    [InlineData("{bad json")]
    [InlineData("{}")]
    [InlineData("{\"title\":\"   \"}")]
    [InlineData("{\"title\":42}")]
    public async Task InvalidMetadataHasABoundedNegativeCache(string json)
    {
        var calls = 0;
        using var handler = new Handler((_, _) => { calls++; return Task.FromResult(Json(json)); });
        using var service = Create(handler);
        Assert.Null(await service.FetchAsync(Id));
        Assert.Null(await service.FetchAsync(Id));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OversizedResponsesAndUpstreamFailuresDoNotEscapeToThePage()
    {
        foreach (var response in new[] { Json(new string('x', 70_000)), new HttpResponseMessage(HttpStatusCode.TooManyRequests), Json("{\"title\":\"" + new string('x', 1001) + "\"}") })
        {
            using var handler = new Handler((_, _) => Task.FromResult(response));
            using var service = Create(handler);
            Assert.Null(await service.FetchAsync(Id));
        }
    }

    [Fact]
    public async Task CallerCancellationDoesNotPoisonTheCache()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        using var handler = new Handler((_, ct) =>
        {
            if (++calls == 1) { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
            return Task.FromResult(Json(Metadata));
        });
        using var service = Create(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FetchAsync(Id, cancellation.Token));
        Assert.NotNull(await service.FetchAsync(Id));
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("usE4x1Gyss!")]
    [InlineData("../private")]
    public async Task InvalidIdsNeverMakeAnOutboundRequest(string id)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Unexpected network request"));
        using var service = Create(handler);
        Assert.Null(await service.FetchAsync(id));
    }

    [Fact]
    public async Task PublicEndpointValidatesCachesAndRateLimitsMetadataReads()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        using var invalid = await client.GetAsync("/api/previews/youtube/invalid");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.True(invalid.Headers.CacheControl!.NoStore);
        for (var i = 0; i < 59; i++)
        {
            using var response = await client.GetAsync($"/api/previews/youtube/{Id}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.Public);
            Assert.Equal(TimeSpan.FromHours(1), response.Headers.CacheControl.MaxAge);
            Assert.Contains("Perfect Hair Health", await response.Content.ReadAsStringAsync());
        }
        using var limited = await client.GetAsync($"/api/previews/youtube/{Id}");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter!.Delta > TimeSpan.Zero);
        var requests = factory.Services.GetRequiredService<DeterministicExternalHttpClientFactory>().Requests;
        Assert.Single(requests, uri => uri.Host == "www.youtube.com");
    }

    private static YouTubePreviewService Create(HttpMessageHandler handler) => new(new Factory(handler), NullLogger<YouTubePreviewService>.Instance);
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
    }
}
