using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialEventSkipPolicyTests
{
    public static IEnumerable<object[]> XOrThreadsGoldenSkipRules()
    {
        var now = new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
        var freshCutoff = now.AddDays(-7);

        yield return new object[] { EventType.CustomEvent, "Title\n\nBody", now, 99, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.Joined, "slug[alice]", now, 99, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.DonationReceived, "tx[abc] sats[1000]", now, 99, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.NewRank, "slug[alice] rank[1]", now, 0, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.NewRank, "slug[alice] rank[4]", now, 99, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.NewRank, "slug[alice] rank[1]", now.AddDays(-8), 0, freshCutoff, true, true, SocialEventSkipReason.StalePrimaryEvent };
        yield return new object[] { EventType.AthleteCountMilestone, "athletes[100]", now, 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Podcast] cat[Global] val[] place[1]", now, 99, freshCutoff, true, true, SocialEventSkipReason.PodcastBadgeHandledImmediately };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Pheno Age - lowest] cat[Global] val[] place[2]", now, 2, freshCutoff, true, true, SocialEventSkipReason.NonWinningSingleWinnerBadge };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Pheno Age best improvement] cat[Global] val[] place[1]", now, 3, freshCutoff, false, true, SocialEventSkipReason.TiedBestImprovementBadge };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Crowd Age - lowest] cat[Global] val[] place[1]", now, 99, freshCutoff, true, true, SocialEventSkipReason.UnsupportedBadgeAward };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Age reduction] cat[Division] val[Men's] place[1]", now, 1, freshCutoff, true, false, SocialEventSkipReason.None };
    }

    public static IEnumerable<object[]> FacebookGoldenSkipRules()
    {
        yield return new object[] { EventType.CustomEvent, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.Joined, true, SocialEventSkipReason.FacebookSupportsCustomEventsOnly };
        yield return new object[] { EventType.NewRank, true, SocialEventSkipReason.FacebookSupportsCustomEventsOnly };
        yield return new object[] { EventType.BadgeAward, true, SocialEventSkipReason.FacebookSupportsCustomEventsOnly };
        yield return new object[] { EventType.AthleteCountMilestone, true, SocialEventSkipReason.FacebookSupportsCustomEventsOnly };
        yield return new object[] { EventType.DonationReceived, true, SocialEventSkipReason.FacebookSupportsCustomEventsOnly };
    }

    [Theory]
    [MemberData(nameof(XOrThreadsGoldenSkipRules))]
    public void XOrThreads_TerminalSkipRulesMatchGoldenMatrix(
        EventType type,
        string text,
        DateTime occurredAtUtc,
        int priority,
        DateTime freshCutoffUtc,
        bool hasSingleGlobalPlaceOneBadgeHolder,
        bool expectedSkipped,
        SocialEventSkipReason expectedReason)
    {
        var skipped = SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(
            type,
            text,
            occurredAtUtc,
            priority,
            freshCutoffUtc,
            _ => hasSingleGlobalPlaceOneBadgeHolder,
            out var reason);

        Assert.Equal(expectedSkipped, skipped);
        Assert.Equal(expectedReason, reason);
    }

    [Theory]
    [MemberData(nameof(FacebookGoldenSkipRules))]
    public void Facebook_TerminalSkipRulesMatchGoldenMatrix(
        EventType type,
        bool expectedSkipped,
        SocialEventSkipReason expectedReason)
    {
        var skipped = SocialEventSkipPolicy.TryGetFacebookTerminalSkipReason(type, out var reason);

        Assert.Equal(expectedSkipped, skipped);
        Assert.Equal(expectedReason, reason);
    }

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
    public void Slack_OldAthleteCountMilestonesAreSkipped()
    {
        var freshCutoffUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(EventDataService.ShouldSkipSlackNotification(
            EventType.AthleteCountMilestone,
            freshCutoffUtc.AddSeconds(-1),
            freshCutoffUtc));
        Assert.False(EventDataService.ShouldSkipSlackNotification(
            EventType.AthleteCountMilestone,
            freshCutoffUtc,
            freshCutoffUtc));
        Assert.False(EventDataService.ShouldSkipSlackNotification(
            EventType.CustomEvent,
            freshCutoffUtc.AddYears(-1),
            freshCutoffUtc));
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
