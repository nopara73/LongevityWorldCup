using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialEventSkipPolicyTests
{
    [Fact]
    public void Facebook_NonCustomEventsAreTerminalSkips()
    {
        var skipped = SocialEventSkipPolicy.TryGetFacebookTerminalSkipReason(
            EventType.NewRank,
            out var reason);

        Assert.True(skipped);
        Assert.Equal(SocialEventSkipReason.FacebookSupportsCustomEventsOnly, reason);
    }

    [Fact]
    public void Facebook_CustomEventsAreNotTerminalSkips()
    {
        var skipped = SocialEventSkipPolicy.TryGetFacebookTerminalSkipReason(
            EventType.CustomEvent,
            out var reason);

        Assert.False(skipped);
        Assert.Equal(SocialEventSkipReason.None, reason);
    }

    [Fact]
    public void XOrThreads_UnsupportedEventTypesAreTerminalSkips()
    {
        var skipped = SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(
            EventType.Joined,
            "slug[alice]",
            DateTime.UtcNow,
            priority: 99,
            freshCutoffUtc: DateTime.UtcNow.AddDays(-7),
            hasSingleGlobalPlaceOneBadgeHolder: _ => true,
            out var reason);

        Assert.True(skipped);
        Assert.Equal(SocialEventSkipReason.UnsupportedEventType, reason);
    }

    [Fact]
    public void XOrThreads_StalePrimaryEventsAreTerminalSkips()
    {
        var skipped = SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(
            EventType.NewRank,
            "slug[alice] rank[1]",
            DateTime.UtcNow.AddDays(-8),
            priority: 0,
            freshCutoffUtc: DateTime.UtcNow.AddDays(-7),
            hasSingleGlobalPlaceOneBadgeHolder: _ => true,
            out var reason);

        Assert.True(skipped);
        Assert.Equal(SocialEventSkipReason.StalePrimaryEvent, reason);
    }

    [Fact]
    public void XOrThreads_LowRankEventsAreTerminalSkips()
    {
        var skipped = SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(
            EventType.NewRank,
            "slug[alice] rank[4]",
            DateTime.UtcNow,
            priority: 99,
            freshCutoffUtc: DateTime.UtcNow.AddDays(-7),
            hasSingleGlobalPlaceOneBadgeHolder: _ => true,
            out var reason);

        Assert.True(skipped);
        Assert.Equal(SocialEventSkipReason.UnsupportedEventPayload, reason);
    }

    [Fact]
    public void XOrThreads_TiedBestImprovementBadgesAreTerminalSkips()
    {
        var skipped = SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(
            EventType.BadgeAward,
            "slug[alice] badge[Pheno Age best improvement] cat[Global] val[] place[1]",
            DateTime.UtcNow,
            priority: 3,
            freshCutoffUtc: DateTime.UtcNow.AddDays(-7),
            hasSingleGlobalPlaceOneBadgeHolder: _ => false,
            out var reason);

        Assert.True(skipped);
        Assert.Equal(SocialEventSkipReason.TiedBestImprovementBadge, reason);
    }
}
