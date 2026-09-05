using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialPostRetryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulSend_StopsAfterOneAttempt(bool retryMissingPostId)
    {
        var attempts = 0;
        var logger = new RecordingLogger();

        var sent = await SocialPostRetry.TrySendAsync(
            () => Task.FromResult<string?>($"post-{++attempts}"),
            logger, "Test send", "caption", retryMissingPostId);

        Assert.True(sent);
        Assert.Equal(1, attempts);
        Assert.Empty(logger.Entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task MissingPostId_DoesNotRetryWhenTheClientOwnsRetries(string? postId)
    {
        var attempts = 0;
        var logger = new RecordingLogger();

        var sent = await SocialPostRetry.TrySendAsync(
            () =>
            {
                attempts++;
                return Task.FromResult(postId);
            },
            logger, "Threads image send", "caption", retryMissingPostId: false);

        Assert.False(sent);
        Assert.Equal(1, attempts);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal("Threads image send", entry.Properties["Operation"]);
        Assert.Equal("caption", entry.Properties["Text"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThrownSend_RetriesAndCanSucceedRegardlessOfMissingIdPolicy(bool retryMissingPostId)
    {
        var failure = new HttpRequestException("Connection interrupted");
        var attempts = 0;
        var logger = new RecordingLogger();

        var sent = await SocialPostRetry.TrySendAsync(
            () => ++attempts == 1 ? throw failure : Task.FromResult<string?>("post-2"),
            logger, "Test send", "caption", retryMissingPostId);

        Assert.True(sent);
        Assert.Equal(2, attempts);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Same(failure, entry.Exception);
        Assert.Equal(1, entry.Properties["Attempt"]);
        Assert.Equal(2, entry.Properties["MaxAttempts"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FaultedSends_StopAfterTwoAttemptsAndLogTheFinalException(bool retryMissingPostId)
    {
        var failures = new[] { new HttpRequestException("First failure"), new HttpRequestException("Final failure") };
        var attempts = 0;
        var logger = new RecordingLogger();

        var sent = await SocialPostRetry.TrySendAsync(
            () => Task.FromException<string?>(failures[attempts++]),
            logger, "Test send", "caption", retryMissingPostId);

        Assert.False(sent);
        Assert.Equal(2, attempts);
        Assert.Equal([LogLevel.Warning, LogLevel.Error], logger.Entries.Select(entry => entry.Level));
        Assert.Same(failures[0], logger.Entries[0].Exception);
        Assert.Same(failures[1], logger.Entries[1].Exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingIdAndException_ShareOneRetryBudget(bool exceptionFirst)
    {
        var failure = new HttpRequestException("Send failed");
        var attempts = 0;
        var logger = new RecordingLogger();

        var sent = await SocialPostRetry.TrySendAsync(
            () => (++attempts == 1) == exceptionFirst
                ? Task.FromException<string?>(failure)
                : Task.FromResult<string?>(null),
            logger, "Test send", "caption", retryMissingPostId: true);

        Assert.False(sent);
        Assert.Equal(2, attempts);
        Assert.Equal(LogLevel.Warning, logger.Entries[0].Level);
        Assert.Equal(exceptionFirst ? LogLevel.Warning : LogLevel.Error, logger.Entries[1].Level);
        Assert.Same(failure, logger.Entries[exceptionFirst ? 0 : 1].Exception);
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, Dictionary<string, object?> Properties);

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state).ToDictionary();
            Entries.Add(new LogEntry(logLevel, exception, properties));
        }
    }
}
