using System.Net;
using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SlackWebhookClientTests
{
    [Fact]
    public async Task SendAsync_PostsJsonPayloadToConfiguredWebhook()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new SlackWebhookClient(
            new HttpClient(handler),
            new Config { SlackWebhookUrl = "https://hooks.slack.example.test/services/T000/B000/secret" },
            NullLogger<SlackWebhookClient>.Instance);

        await client.SendAsync("hello from tests");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://hooks.slack.example.test/services/T000/B000/secret", request.RequestUri?.AbsoluteUri);
        Assert.Equal("application/json", request.ContentType);
        using var payload = JsonDocument.Parse(request.Body);
        Assert.Equal("hello from tests", payload.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendAsync_SkipsHttpRequestWhenWebhookIsMissing()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var logger = new RecordingLogger<SlackWebhookClient>();
        var client = new SlackWebhookClient(
            new HttpClient(handler),
            new Config { SlackWebhookUrl = " " },
            logger);

        await client.SendAsync("do not send");

        Assert.Empty(handler.Requests);
        var error = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Contains("Slack webhook URL is not configured", error.Message);
        Assert.Contains("do not send", error.Message);
    }

    [Fact]
    public async Task SendAsync_ThrowsWhenSlackReturnsFailure()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var client = new SlackWebhookClient(
            new HttpClient(handler),
            new Config { SlackWebhookUrl = "https://hooks.slack.example.test/services/T000/B000/secret" },
            NullLogger<SlackWebhookClient>.Instance);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync("will fail"));
        Assert.Single(handler.Requests);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri,
                request.Content?.Headers.ContentType?.MediaType,
                body));
            return responseFactory(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        string? ContentType,
        string Body);

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
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);
}
