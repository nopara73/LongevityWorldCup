using System.Net;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ThreadsApiClientTokenRefreshTests
{
    [Fact]
    public void ShouldRefreshProactively_ReturnsTrue_WhenKnownExpiryIsInsideRefreshWindow()
    {
        var now = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = now.AddDays(13).ToString("O");

        var shouldRefresh = ThreadsApiClient.ShouldRefreshProactively(now, expiresAt, lastRefreshAttemptAtUtc: null);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshProactively_ReturnsFalse_WhenKnownExpiryIsOutsideRefreshWindow()
    {
        var now = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = now.AddDays(15).ToString("O");

        var shouldRefresh = ThreadsApiClient.ShouldRefreshProactively(now, expiresAt, lastRefreshAttemptAtUtc: null);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshProactively_ReturnsTrue_WhenExpiryIsUnknownAndNoAttemptWasRecorded()
    {
        var now = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);

        var shouldRefresh = ThreadsApiClient.ShouldRefreshProactively(now, expiresAtUtc: null, lastRefreshAttemptAtUtc: null);

        Assert.True(shouldRefresh);
    }

    [Fact]
    public void ShouldRefreshProactively_ReturnsFalse_WhenExpiryIsUnknownAndRefreshWasRecentlyAttempted()
    {
        var now = new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);
        var lastAttempt = now.AddHours(-19).ToString("O");

        var shouldRefresh = ThreadsApiClient.ShouldRefreshProactively(now, expiresAtUtc: null, lastAttempt);

        Assert.False(shouldRefresh);
    }

    [Fact]
    public async Task SendPostAsync_RetriesTransientPublishFailureWithSameCreationId()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.OK, """{"id":"creation-1"}"""),
            Json(HttpStatusCode.OK, """{"id":"creation-1","status":"FINISHED"}"""),
            Json(HttpStatusCode.InternalServerError, """{"error":{"message":"An unexpected error has occurred. Please retry your request later.","type":"OAuthException","is_transient":true,"code":2}}"""),
            Json(HttpStatusCode.OK, """{"id":"threads-post-1"}""")
        });
        var requests = new List<RecordedRequest>();
        var client = CreateClient(responses, requests);

        var postId = await client.SendPostAsync("hello Threads");

        Assert.Equal("threads-post-1", postId);
        Assert.Collection(
            requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/me/threads", request.RequestUri!.AbsolutePath);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/creation-1", request.RequestUri!.AbsolutePath);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/me/threads_publish", request.RequestUri!.AbsolutePath);
                Assert.Equal("creation_id=creation-1", request.Content);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/me/threads_publish", request.RequestUri!.AbsolutePath);
                Assert.Equal("creation_id=creation-1", request.Content);
            });
    }

    [Fact]
    public async Task SendPostAsync_DoesNotRetryPermanentPublishFailure()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.OK, """{"id":"creation-1"}"""),
            Json(HttpStatusCode.OK, """{"id":"creation-1","status":"FINISHED"}"""),
            Json(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid container.","type":"OAuthException","code":100}}""")
        });
        var requests = new List<RecordedRequest>();
        var client = CreateClient(responses, requests);

        var postId = await client.SendPostAsync("hello Threads");

        Assert.Null(postId);
        Assert.Equal(3, requests.Count);
        Assert.Equal("/me/threads_publish", requests[2].RequestUri!.AbsolutePath);
    }

    [Fact]
    public void IsTransientThreadsError_ReturnsTrue_ForGraphTransientBody()
    {
        var isTransient = ThreadsApiClient.IsTransientThreadsError(
            HttpStatusCode.BadRequest,
            """{"error":{"message":"retry later","type":"OAuthException","is_transient":true,"code":2}}""");

        Assert.True(isTransient);
    }

    private static ThreadsApiClient CreateClient(Queue<HttpResponseMessage> responses, List<RecordedRequest> requests)
    {
        return new ThreadsApiClient(
            new HttpClient(new QueueHttpHandler(responses, requests)),
            new Config { ThreadsAccessToken = "threads-token" },
            NullLogger<ThreadsApiClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        };
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri? RequestUri, string? Content);

    private sealed class QueueHttpHandler(Queue<HttpResponseMessage> responses, List<RecordedRequest> requests) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            requests.Add(new RecordedRequest(request.Method, request.RequestUri, content));

            if (responses.Count == 0)
                throw new InvalidOperationException($"Unexpected HTTP request to {request.RequestUri}.");

            return responses.Dequeue();
        }
    }
}
