using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public class CrowdAgeGuessRateLimiterTests
{
    private const string ImageA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ImageB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void TryAccept_BlocksSameClientAthleteAndImageWithinWindow()
    {
        var limiter = new CrowdAgeGuessRateLimiter(TimeSpan.FromMinutes(15));
        var now = DateTimeOffset.Parse("2026-05-31T10:00:00Z");

        Assert.True(limiter.TryAccept("203.0.113.4", "athlete_one", ImageA, now, out var firstRetryAfter));
        Assert.Equal(TimeSpan.Zero, firstRetryAfter);

        Assert.False(limiter.TryAccept("203.0.113.4", "athlete_one", ImageA, now.AddMinutes(1), out var retryAfter));
        Assert.Equal(TimeSpan.FromMinutes(14), retryAfter);
    }

    [Fact]
    public void TryAccept_AllowsDifferentAthletesForSameClient()
    {
        var limiter = new CrowdAgeGuessRateLimiter(TimeSpan.FromMinutes(15));
        var now = DateTimeOffset.Parse("2026-05-31T10:00:00Z");

        Assert.True(limiter.TryAccept("203.0.113.4", "athlete_one", ImageA, now, out _));
        Assert.True(limiter.TryAccept("203.0.113.4", "athlete_two", ImageA, now.AddMinutes(1), out _));
    }

    [Fact]
    public void TryAccept_AllowsSameClientAndAthleteForNewProfileImage()
    {
        var limiter = new CrowdAgeGuessRateLimiter(TimeSpan.FromMinutes(15));
        var now = DateTimeOffset.Parse("2026-05-31T10:00:00Z");

        Assert.True(limiter.TryAccept("203.0.113.4", "athlete_one", ImageA, now, out _));
        Assert.True(limiter.TryAccept("203.0.113.4", "athlete_one", ImageB, now.AddMinutes(1), out var retryAfter));
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    [Fact]
    public void TryAccept_AllowsSameClientAndAthleteAfterWindow()
    {
        var limiter = new CrowdAgeGuessRateLimiter(TimeSpan.FromMinutes(15));
        var now = DateTimeOffset.Parse("2026-05-31T10:00:00Z");

        Assert.True(limiter.TryAccept("203.0.113.4", "athlete_one", ImageA, now, out _));
        Assert.True(limiter.TryAccept("203.0.113.4", "athlete_one", ImageA, now.AddMinutes(15), out var retryAfter));
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }

    [Fact]
    public void TryAccept_RejectsMissingProfileImageId()
    {
        var limiter = new CrowdAgeGuessRateLimiter(TimeSpan.FromMinutes(15));

        Assert.False(limiter.TryAccept("203.0.113.4", "athlete_one", "", DateTimeOffset.UtcNow, out var retryAfter));
        Assert.Equal(TimeSpan.Zero, retryAfter);
    }
}
