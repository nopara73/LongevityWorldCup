using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LongevityWorldCup.Website.Business.IndexNow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class IndexNowTests : IDisposable
{
    private const string Origin = IndexNowContentSnapshot.SiteBaseUrl;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "IndexNowTests", Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
    private string StatePath => Path.Combine(_root, "indexnow-state.json");

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task KeyIsExactUtf8PlainTextAndPersistsAcrossRestart(string method)
    {
        var store = new IndexNowStateStore(StatePath);
        var key = store.Key;
        Assert.True(IndexNowStateStore.IsValidKey(key));
        store = new IndexNowStateStore(StatePath);
        Assert.Equal(key, store.Key);
        using var services = new ServiceCollection().AddSingleton(store).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = method;
        context.Request.Path = $"/{key}.txt";
        context.Response.Body = new MemoryStream();
        await new IndexNowKeyMiddleware(_ => throw new Exception("The key must short circuit the pipeline")).InvokeAsync(context);
        Assert.Equal("text/plain; charset=utf-8", context.Response.ContentType);
        Assert.Equal(key.Length, context.Response.ContentLength);
        Assert.Equal(method == "HEAD" ? [] : Encoding.UTF8.GetBytes(key), ((MemoryStream)context.Response.Body).ToArray());
        Assert.Equal("noindex", context.Response.Headers["X-Robots-Tag"]);
    }

    [Fact]
    public async Task BrokenLedgerDoesNotBreakOrdinaryRequestsOrRegenerateKey()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(StatePath, "broken");
        using var services = new ServiceCollection().AddSingleton(_ => new IndexNowStateStore(StatePath)).BuildServiceProvider();
        var nextCalled = false;
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = "/leaderboard";
        await new IndexNowKeyMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }).InvokeAsync(context);
        Assert.True(nextCalled);
        Assert.Throws<JsonException>(() => new IndexNowStateStore(StatePath));
        Assert.Equal("broken", File.ReadAllText(StatePath));
    }

    [Theory]
    [InlineData("Development", true, false)]
    [InlineData("Test", true, false)]
    [InlineData("Staging", true, false)]
    [InlineData("Production", false, false)]
    [InlineData("Production", true, true)]
    public void SubmissionsRequireExplicitProductionOptIn(string environment, bool enabled, bool expected) =>
        Assert.Equal(expected, IndexNowOptions.CanSubmit(new TestEnvironment(_root) { EnvironmentName = environment }, new() { Enabled = enabled }));

    [Theory]
    [InlineData("/")]
    [InlineData("/events")]
    [InlineData("/league/pheno")]
    [InlineData("/athlete/michael-lustgarten")]
    [InlineData("/flag/hungary")]
    [InlineData("/pheno-age")]
    public void CanonicalPublicPathsAreEligible(string path) => Assert.True(IndexNowContentSnapshot.IsCanonicalUrl(Origin + path));

    [Theory]
    [InlineData("https://example.com/athlete/test")]
    [InlineData("https://www.longevityworldcup.com/")]
    [InlineData("http://longevityworldcup.com/")]
    [InlineData("https://longevityworldcup.com:443/")]
    [InlineData("https://longevityworldcup.com/leaderboard?utm_source=test")]
    [InlineData("https://longevityworldcup.com/leaderboard#test")]
    [InlineData("https://longevityworldcup.com/athlete/test_person")]
    [InlineData("https://longevityworldcup.com/athlete/Test")]
    [InlineData("https://longevityworldcup.com/athlete/test/")]
    [InlineData("https://longevityworldcup.com/athlete/../events")]
    [InlineData("https://longevityworldcup.com/athlete/test%2Ftest")]
    [InlineData("https://longevityworldcup.com/league/ultimate")]
    [InlineData("https://longevityworldcup.com/league/pheno-improvement")]
    [InlineData("https://longevityworldcup.com/athletes/test/proof_1.webp")]
    [InlineData("https://longevityworldcup.com/admin")]
    [InlineData("https://longevityworldcup.com/apply")]
    [InlineData("https://longevityworldcup.com/review")]
    [InlineData("https://longevityworldcup.com/onboarding/pheno-age.html")]
    [InlineData("https://longevityworldcup.com/generated/profiles/test.webp")]
    [InlineData("https://longevityworldcup.com/api/application/application")]
    public void PrivateAliasedAndArbitraryUrlsAreExcluded(string url) => Assert.False(IndexNowContentSnapshot.IsCanonicalUrl(url));

    [Fact]
    public async Task BatchesDeduplicatePersistAndDoNotResubmitUnchangedRestart()
    {
        var store = new IndexNowStateStore(StatePath);
        var handler = new FakeTransport(HttpStatusCode.OK);
        var options = new IndexNowOptions { BatchSize = 2 };
        var submitter = Create(store, handler, options);
        var content = new Dictionary<string, string> { [Origin + "/"] = "one", [Origin + "/events"] = "two", [Origin + "/leaderboard"] = "three" };
        submitter.Reconcile(content, Now);
        submitter.Reconcile(content, Now);
        await submitter.SubmitNextBatchAsync(Now, default);
        Assert.Equal(2, handler.Requests[0].GetProperty("urlList").GetArrayLength());
        store = new IndexNowStateStore(StatePath);
        submitter = Create(store, handler, options);
        await submitter.SubmitNextBatchAsync(Now.AddMinutes(1), default);
        Assert.Single(handler.Requests[1].GetProperty("urlList").EnumerateArray());
        submitter.Reconcile(content, Now.AddDays(1));
        await submitter.SubmitNextBatchAsync(Now.AddDays(1), default);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(store.Read().Urls.Values, entry => Assert.False(entry.Pending));
        var payload = handler.Requests[0];
        Assert.Equal("longevityworldcup.com", payload.GetProperty("host").GetString());
        Assert.Equal(store.Key, payload.GetProperty("key").GetString());
        Assert.Equal($"{Origin}/{store.Key}.txt", payload.GetProperty("keyLocation").GetString());
    }

    [Fact]
    public async Task ChangesAreCoalescedWithMinimumIntervalAndRemovalsNotifyOnce()
    {
        var store = new IndexNowStateStore(StatePath);
        var handler = new FakeTransport(HttpStatusCode.OK);
        var submitter = Create(store, handler);
        var url = Origin + "/athlete/removed-athlete";
        submitter.Reconcile(new Dictionary<string, string> { [url] = "old" }, Now);
        await submitter.SubmitNextBatchAsync(Now, default);
        submitter.Reconcile(new Dictionary<string, string> { [url] = "new" }, Now.AddMinutes(1));
        await submitter.SubmitNextBatchAsync(Now.AddMinutes(1), default);
        Assert.Single(handler.Requests);
        submitter.Reconcile(new Dictionary<string, string> { [url] = "latest" }, Now.AddMinutes(2));
        await submitter.SubmitNextBatchAsync(Now.AddMinutes(5), default);
        Assert.Equal("latest", store.Read().Urls[url].SubmittedHash);
        submitter.Reconcile(new Dictionary<string, string>(), Now.AddMinutes(6));
        await submitter.SubmitNextBatchAsync(Now.AddMinutes(10), default);
        store = new IndexNowStateStore(StatePath);
        submitter = Create(store, handler);
        submitter.Reconcile(new Dictionary<string, string>(), Now.AddDays(1));
        await submitter.SubmitNextBatchAsync(Now.AddDays(1), default);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(IndexNowUrlState.Removed, store.Read().Urls[url].SubmittedHash);
    }

    [Fact]
    public async Task ContentUpdatedDuringSubmissionRemainsPending()
    {
        var store = new IndexNowStateStore(StatePath);
        var handler = new FakeTransport(HttpStatusCode.OK);
        var submitter = Create(store, handler);
        var url = Origin + "/events";
        submitter.Reconcile(new Dictionary<string, string> { [url] = "before" }, Now);
        handler.OnSend = () => submitter.Reconcile(new Dictionary<string, string> { [url] = "after" }, Now);
        await submitter.SubmitNextBatchAsync(Now, default);
        Assert.Equal("before", store.Read().Urls[url].SubmittedHash);
        Assert.Equal("after", store.Read().Urls[url].ContentHash);
        Assert.True(store.Read().Urls[url].Pending);
    }

    [Fact]
    public async Task DeletedStaticRouteCanStillBeNotifiedAfterLeavingCurrentCatalog()
    {
        var store = new IndexNowStateStore(StatePath);
        // Simulate a canonical document recorded by the previous deployed route catalog.
        var url = Origin + "/retired-public-document";
        store.Update(state =>
        {
            state.Urls[url] = new IndexNowUrlState { ContentHash = "old", SubmittedHash = "old", ChangedAtUtc = Now };
            return true;
        });
        var handler = new FakeTransport(HttpStatusCode.OK);
        var submitter = Create(store, handler);
        submitter.Reconcile(new Dictionary<string, string>(), Now);
        await submitter.SubmitNextBatchAsync(Now, default);
        Assert.Equal(url, Assert.Single(handler.Requests[0].GetProperty("urlList").EnumerateArray()).GetString());
        Assert.False(store.Read().Urls[url].Pending);
    }

    [Fact]
    public async Task PayloadCannotExceedProtocolLimit()
    {
        var store = new IndexNowStateStore(StatePath);
        var handler = new FakeTransport(HttpStatusCode.OK);
        var submitter = Create(store, handler, new IndexNowOptions { BatchSize = 50_000 });
        submitter.Reconcile(Enumerable.Range(0, 10_001).ToDictionary(i => Origin + "/athlete/member" + i, _ => "hash"), Now);
        await submitter.SubmitNextBatchAsync(Now, default);
        Assert.Equal(10_000, handler.Requests[0].GetProperty("urlList").GetArrayLength());
        Assert.Single(store.Read().Urls.Values, entry => entry.Pending);
    }

    [Theory]
    [InlineData(202)]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task RetryableResponsesPersistBackoffAcrossRestart(int code)
    {
        var store = new IndexNowStateStore(StatePath);
        var handler = new FakeTransport((HttpStatusCode)code);
        var submitter = Create(store, handler);
        submitter.Reconcile(new Dictionary<string, string> { [Origin + "/"] = "v1" }, Now);
        await submitter.SubmitNextBatchAsync(Now, default);
        var state = new IndexNowStateStore(StatePath).Read();
        Assert.True(state.Urls[Origin + "/"].Pending);
        Assert.Null(state.Urls[Origin + "/"].SubmittedAtUtc);
        Assert.Equal(code, state.LastStatusCode);
        Assert.True(state.NotBeforeUtc >= Now.AddMinutes(code == 202 ? 30 : 5));
        if (code == 202) Assert.Equal("Ownership validation pending", state.LastOutcome);
        store = new IndexNowStateStore(StatePath);
        submitter = Create(store, handler);
        // A newly changed URL must not bypass a host-wide throttle/backoff.
        submitter.Reconcile(new Dictionary<string, string> { [Origin + "/"] = "v1", [Origin + "/events"] = "new" }, Now.AddMinutes(1));
        await submitter.SubmitNextBatchAsync(Now.AddMinutes(1), default);
        Assert.Single(handler.Requests);
        handler.Code = HttpStatusCode.OK;
        await submitter.SubmitNextBatchAsync(state.NotBeforeUtc!.Value, default);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(store.Read().Urls.Values, entry => Assert.False(entry.Pending));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterHonorsBothHttpDateAndSeconds(bool date)
    {
        var store = new IndexNowStateStore(StatePath);
        var handler = new FakeTransport(HttpStatusCode.TooManyRequests)
        {
            RetryAfter = date ? new RetryConditionHeaderValue(Now.AddHours(3)) : new RetryConditionHeaderValue(TimeSpan.FromHours(3))
        };
        var submitter = Create(store, handler);
        submitter.Reconcile(new Dictionary<string, string> { [Origin + "/"] = "v1" }, Now);
        await submitter.SubmitNextBatchAsync(Now, default);
        Assert.Equal(Now.AddHours(3), store.Read().NotBeforeUtc);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(422)]
    [InlineData(301)]
    public async Task PermanentErrorsRemainPausedUntilExplicitRetryVersionChange(int code)
    {
        var store = new IndexNowStateStore(StatePath);
        var handler = new FakeTransport((HttpStatusCode)code);
        var options = new IndexNowOptions();
        var submitter = Create(store, handler, options);
        var content = new Dictionary<string, string> { [Origin + "/"] = "v1" };
        submitter.Reconcile(content, Now);
        await submitter.SubmitNextBatchAsync(Now, default);
        store = new IndexNowStateStore(StatePath);
        submitter = Create(store, handler, options);
        submitter.Reconcile(content, Now.AddDays(1));
        await submitter.SubmitNextBatchAsync(Now.AddDays(1), default);
        Assert.Single(handler.Requests);
        Assert.NotNull(store.Read().PausedReason);
        Assert.True(store.Read().Urls[Origin + "/"].Pending);
        options.RetryVersion = "fixed-1";
        handler.Code = HttpStatusCode.OK;
        submitter.Reconcile(content, Now.AddDays(1));
        await submitter.SubmitNextBatchAsync(Now.AddDays(1), default);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Null(store.Read().PausedReason);
    }

    [Fact]
    public async Task TransportFailureAndShutdownKeepDurableWork()
    {
        var store = new IndexNowStateStore(StatePath);
        var handler = new FakeTransport(HttpStatusCode.OK) { Failure = new HttpRequestException("offline") };
        var submitter = Create(store, handler);
        submitter.Reconcile(new Dictionary<string, string> { [Origin + "/"] = "v1" }, Now);
        await submitter.SubmitNextBatchAsync(Now, default);
        Assert.True(new IndexNowStateStore(StatePath).Read().Urls[Origin + "/"].Pending);
        using var cts = new CancellationTokenSource();
        handler.OnSend = cts.Cancel;
        handler.Failure = new OperationCanceledException(cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => submitter.SubmitNextBatchAsync(Now.AddDays(1), cts.Token));
        var state = new IndexNowStateStore(StatePath).Read();
        Assert.True(state.Urls[Origin + "/"].Pending);
        Assert.Equal(Now.AddDays(1), state.Urls[Origin + "/"].LastAttemptUtc);
    }

    private static IndexNowSubmitter Create(IndexNowStateStore store, FakeTransport handler, IndexNowOptions? options = null) =>
        new(store, new FakeFactory(handler), Options.Create(options ?? new()), NullLogger<IndexNowSubmitter>.Instance);

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeTransport(HttpStatusCode code) : HttpMessageHandler
    {
        public HttpStatusCode Code { get; set; } = code;
        public List<JsonElement> Requests { get; } = [];
        public RetryConditionHeaderValue? RetryAfter { get; init; }
        public Action? OnSend { get; set; }
        public Exception? Failure { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(IndexNowOptions.Endpoint, request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("application/json", request.Content!.Headers.ContentType?.MediaType);
            Requests.Add(JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
            OnSend?.Invoke();
            if (Failure is not null) throw Failure;
            return new HttpResponseMessage(Code) { Headers = { RetryAfter = RetryAfter } };
        }
    }

    internal sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "IndexNowTests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
