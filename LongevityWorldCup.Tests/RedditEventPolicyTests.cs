using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class RedditEventPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OnlyOverallLeadsAndExplicitCustomEventsAreEligible()
    {
        foreach (var type in Enum.GetValues<EventType>().Append((EventType)999))
        {
            var reason = RedditEventPolicy.SkipReason(type, "slug[alice] rank[1]", Now.UtcDateTime, Now);
            if (type is EventType.CustomEvent or EventType.NewRank) Assert.Null(reason);
            else Assert.Equal(RedditEventPolicy.RoutineUpdate, reason);
        }
        Assert.Null(RedditEventPolicy.SkipReason(EventType.CustomEvent, "250 athletes\n\nDeliberately selected copy", Now.UtcDateTime.AddMonths(-1), Now));
    }

    [Theory]
    [InlineData("slug[alice] rank[2]", "RedditRoutineUpdate")]
    [InlineData("slug[alice] rank[3]", "RedditRoutineUpdate")]
    [InlineData("slug[alice] rank[0]", "UnsupportedEventPayload")]
    [InlineData("rank[1]", "UnsupportedEventPayload")]
    [InlineData("slug[ ] rank[1]", "UnsupportedEventPayload")]
    public void RankingPayloadMustIdentifyAnOverallLeader(string text, string reason) =>
        Assert.Equal(reason, RedditEventPolicy.SkipReason(EventType.NewRank, text, Now.UtcDateTime, Now));

    [Fact]
    public void SevenDayFreshnessDoesNotTurnIntoHistoricalReplay()
    {
        Assert.Null(RedditEventPolicy.SkipReason(EventType.NewRank, "slug[alice] rank[1]", Now.UtcDateTime.AddDays(-7), Now));
        Assert.Equal("StalePrimaryEvent", RedditEventPolicy.SkipReason(EventType.NewRank, "slug[alice] rank[1]", Now.UtcDateTime.AddDays(-7).AddTicks(-1), Now));
    }

    [Theory]
    [InlineData(true, "bortz age is 25.4 years (Pro track)")]
    [InlineData(false, "pheno age is 22.3 years (Amateur track)")]
    public void LeaderTextHasStandaloneContextWithoutInventingAHistoricalWinningMargin(bool pro, string expected)
    {
        var leader = new AthleteForX("alice", "Alice", 1, 22.34, pro ? 25.36 : null, 45, null, null, null, null, null);
        var runnerUp = new AthleteForX("bob", "Bob", 2, 20, null, 50, null, null, null, null, null);
        var item = new EventItem("new-leader", EventType.NewRank, "slug[alice] rank[1] prev[unknown]", Now.UtcDateTime, 10, true);
        var text = Assert.IsType<string>(RedditPost.BuildLeaderText(item, [leader, runnerUp]));
        Assert.Contains("8 November 2026", text);
        Assert.Contains(expected, text);
        Assert.Contains("Bob is currently in second place.", text);
        Assert.Contains("Pro athletes ahead of Amateur athletes", text);
        Assert.Contains("biological age minus chronological age", text);
        Assert.DoesNotContain("unknown", text);
        Assert.Contains("https://longevityworldcup.com/leaderboard", text);
        var post = Assert.IsType<RedditPostRequest>(RedditPost.Build(item, text, slug => slug));
        Assert.Equal("Alice takes the Ultimate League lead", post.Title);
        Assert.DoesNotContain(post.Title, post.Text);
        Assert.Contains(expected, post.Text);
        Assert.Null(RedditPost.BuildLeaderText(item, [leader with { CurrentRank = 2 }, runnerUp with { CurrentRank = 1 }]));
        Assert.Null(RedditPost.BuildLeaderText(item, [leader with { LowestPhenoAge = null, LowestBortzAge = null }]));
    }
}
