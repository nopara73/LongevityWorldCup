using System.Net;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ThreadsApiClientTests
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendPostAsync_RetriesTransientContainerCreationFailure(bool imagePost)
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.TooManyRequests, """{"error":{"message":"Application request limit reached","type":"OAuthException","code":613}}"""),
            Json(HttpStatusCode.OK, """{"id":"creation-1"}"""),
            Json(HttpStatusCode.OK, """{"id":"creation-1","status":"FINISHED"}"""),
            Json(HttpStatusCode.OK, """{"id":"threads-post-1"}""")
        });
        var requests = new List<RecordedRequest>();
        var logger = new RecordingLogger<ThreadsApiClient>();
        var client = CreateClient(responses, requests, logger);

        var postId = imagePost
            ? await client.SendImagePostAsync("hello Threads", "https://longevityworldcup.com/post.png")
            : await client.SendPostAsync("hello Threads");

        Assert.Equal("threads-post-1", postId);
        Assert.Equal(
            [
                "/me/threads",
                "/me/threads",
                "/creation-1",
                "/me/threads_publish"
            ],
            requests.Select(request => request.RequestUri!.AbsolutePath));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("container creation transiently failed", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendPostAsync_DoesNotRetryPermanentContainerCreationFailure(bool imagePost)
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.BadRequest, """{"error":{"message":"Invalid parameter","type":"OAuthException","code":100}}""")
        });
        var requests = new List<RecordedRequest>();
        var logger = new RecordingLogger<ThreadsApiClient>();
        var client = CreateClient(responses, requests, logger);

        var postId = imagePost
            ? await client.SendImagePostAsync("hello Threads", "https://longevityworldcup.com/post.png")
            : await client.SendPostAsync("hello Threads");

        Assert.Null(postId);
        Assert.Single(requests);
        Assert.Equal("/me/threads", requests[0].RequestUri!.AbsolutePath);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendPostAsync_RecreatesContainer_WhenMetaLosesFinishedContainer(bool imagePost)
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.OK, """{"id":"creation-1"}"""),
            Json(HttpStatusCode.OK, """{"id":"creation-1","status":"FINISHED"}"""),
            Json(
                HttpStatusCode.BadRequest,
                """{"error":{"message":"The requested resource does not exist","type":"OAuthException","code":24,"error_subcode":4279009,"is_transient":false,"error_user_title":"Media Not Found","error_user_msg":"The media with id creation-1 cannot be found."}}"""),
            Json(HttpStatusCode.OK, """{"id":"creation-2"}"""),
            Json(HttpStatusCode.OK, """{"id":"creation-2","status":"FINISHED"}"""),
            Json(HttpStatusCode.OK, """{"id":"threads-post-1"}""")
        });
        var requests = new List<RecordedRequest>();
        var logger = new RecordingLogger<ThreadsApiClient>();
        var client = CreateClient(responses, requests, logger);

        var postId = imagePost
            ? await client.SendImagePostAsync("hello Threads", "https://longevityworldcup.com/post.png")
            : await client.SendPostAsync("hello Threads");

        Assert.Equal("threads-post-1", postId);
        Assert.Collection(
            requests,
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/me/threads", request.RequestUri!.AbsolutePath);
                Assert.Contains(imagePost ? "media_type=IMAGE" : "media_type=TEXT", request.Content);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/creation-1", request.RequestUri!.AbsolutePath);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("creation_id=creation-1", request.Content);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("/me/threads", request.RequestUri!.AbsolutePath);
                Assert.Contains(imagePost ? "media_type=IMAGE" : "media_type=TEXT", request.Content);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Equal("/creation-2", request.RequestUri!.AbsolutePath);
            },
            request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("creation_id=creation-2", request.Content);
            });
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("Creating a replacement container", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task SendPostAsync_RecreatesContainer_WhenMetaLosesContainerBeforeStatusCheck()
    {
        var responses = new Queue<HttpResponseMessage>(new[]
        {
            Json(HttpStatusCode.OK, """{"id":"creation-1"}"""),
            Json(
                HttpStatusCode.BadRequest,
                """{"error":{"message":"The requested resource does not exist","type":"OAuthException","code":24,"error_subcode":4279009,"is_transient":false,"error_user_title":"Media Not Found"}}"""),
            Json(HttpStatusCode.OK, """{"id":"creation-2"}"""),
            Json(HttpStatusCode.OK, """{"id":"creation-2","status":"FINISHED"}"""),
            Json(HttpStatusCode.OK, """{"id":"threads-post-1"}""")
        });
        var requests = new List<RecordedRequest>();
        var logger = new RecordingLogger<ThreadsApiClient>();
        var client = CreateClient(responses, requests, logger);

        var postId = await client.SendPostAsync("hello Threads");

        Assert.Equal("threads-post-1", postId);
        Assert.Equal(
            [
                "/me/threads",
                "/creation-1",
                "/me/threads",
                "/creation-2",
                "/me/threads_publish"
            ],
            requests.Select(request => request.RequestUri!.AbsolutePath));
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Error);
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

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"retry later","type":"OAuthException","is_transient":true,"code":2}}""")]
    [InlineData(HttpStatusCode.TooManyRequests, "{}")]
    [InlineData(HttpStatusCode.InternalServerError, "{}")]
    public void IsTransientThreadsError_ReturnsTrue_ForRetryableResponse(HttpStatusCode statusCode, string responseBody)
    {
        var isTransient = ThreadsApiClient.IsTransientThreadsError(
            statusCode,
            responseBody);

        Assert.True(isTransient);
    }

    [Fact]
    public void IsMissingThreadsContainerError_RecognizesMetaMediaNotFoundCode()
    {
        var isMissing = ThreadsApiClient.IsMissingThreadsContainerError(
            """{"error":{"code":24,"error_subcode":4279009,"is_transient":false,"error_user_title":"Media Not Found"}}""");

        Assert.True(isMissing);
    }

    [Fact]
    public void IsMissingThreadsContainerError_DoesNotTreatOtherBadRequestsAsMissingContainers()
    {
        var isMissing = ThreadsApiClient.IsMissingThreadsContainerError(
            """{"error":{"code":100,"error_subcode":4279009,"error_user_title":"Media Not Found"}}""");

        Assert.False(isMissing);
    }

    private static ThreadsApiClient CreateClient(
        Queue<HttpResponseMessage> responses,
        List<RecordedRequest> requests,
        ILogger<ThreadsApiClient>? logger = null)
    {
        return new ThreadsApiClient(
            new HttpClient(new QueueHttpHandler(responses, requests)),
            new Config { ThreadsAccessToken = "threads-token" },
            logger ?? NullLogger<ThreadsApiClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        };
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri? RequestUri, string? Content);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

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
