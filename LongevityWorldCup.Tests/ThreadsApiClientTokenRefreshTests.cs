using LongevityWorldCup.Website.Business;
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
}
