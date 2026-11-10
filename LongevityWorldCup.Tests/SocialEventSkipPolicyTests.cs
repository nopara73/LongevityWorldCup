using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialEventSkipPolicyTests
{
    [Theory]
    [InlineData(EventType.CustomEvent, "An authored announcement", false)]
    [InlineData(EventType.AthleteCountMilestone, "athletes[500]", false)]
    [InlineData(EventType.NewRank, "slug[alice] rank[1]", false)]
    [InlineData(EventType.NewRank, "slug[alice] rank[2]", false)]
    [InlineData(EventType.NewRank, "slug[alice] rank[3]", false)]
    [InlineData(EventType.NewRank, "slug[alice] rank[4]", true)]
    [InlineData(EventType.NewRank, "slug[alice] rank[0]", true)]
    [InlineData(EventType.Joined, "slug[alice]", true)]
    [InlineData(EventType.DonationReceived, "tx[abc] sats[1000]", true)]
    [InlineData(EventType.BadgeAward, "slug[alice] badge[Pheno Age best improvement] cat[Global] val[] place[1]", true)]
    [InlineData(EventType.CrowdAgeTop10Change, "slug[alice] place[1] prevPlace[2] crowdAge[25] crowdCount[100]", true)]
    [InlineData(EventType.AgeImprovementTop10Change, "slug[alice] clock[pheno] place[1] improvement[-9.2] ageReduction[-12]", true)]
    public void ConversationChannelsKeepAnnouncementsMilestonesAndOverallPodiums(EventType type, string text, bool skip)
    {
        var now = DateTime.UtcNow;
        Assert.Equal(skip, EventDataService.ShouldSkipSlackNotification(type, now, now.AddDays(-7), text));
        Assert.Equal(skip, SocialEventSkipPolicy.ConversationSkipReason(type, text, now, now.AddDays(-7)) is not null);
    }

    public static IEnumerable<object[]> XOrThreadsGoldenSkipRules()
    {
        var now = new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
        var freshCutoff = now.AddDays(-7);

        yield return new object[] { EventType.PhenoAgeTop10Change, "slug[alice] place[6] prevPlace[8] ageReduction[-20.4]", now, 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.PhenoAgeTop10Change, "slug[alice] place[10] ageReduction[-10.4]", now, 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.PhenoAgeTop10Change, "slug[alice] place[11] ageReduction[-10.4]", now, 8, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.PhenoAgeTop10Change, "slug[alice] place[6]", now, 8, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.CustomEvent, "Title\n\nBody", now, 99, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.Joined, "slug[alice]", now, 99, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.DonationReceived, "tx[abc] sats[1000]", now, 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.DonationReceived, "tx[abc] sats[1000]", now.AddYears(-1), 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.DonationReceived, "tx[abc] sats[0]", now, 8, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.DonationReceived, "sats[1000]", now, 8, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.NewRank, "slug[alice] rank[1]", now, 0, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.NewRank, "slug[alice] rank[4]", now, 99, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.NewRank, "slug[alice] rank[1]", now.AddDays(-8), 0, freshCutoff, true, true, SocialEventSkipReason.StalePrimaryEvent };
        yield return new object[] { EventType.AthleteCountMilestone, "athletes[100]", now, 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.BecamePro, "slug[alice]", now, 8, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.TestResultAccepted, "slug[alice] date[2026-06-06]", now, 99, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.BecamePro, "not-a-slug", now, 8, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.BiologicalAgeImproved, "slug[alice] clock[pheno] from[44.2] to[41.8]", now, 8, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.BiologicalAgeImproved, "slug[alice] clock[pheno] from[41.8] to[44.2]", now, 8, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.CrowdAgeTop10Change, "slug[alice] place[3] prevPlace[8] crowdAge[35.2] crowdCount[123]", now, 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.CrowdAgeTop10Change, "slug[alice] place[6] prevPlace[8] crowdAge[40.5] crowdCount[102]", now, 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.CrowdAgeTop10Change, "slug[alice] place[8] crowdAge[41] crowdCount[100]", now, 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.CrowdAgeTop10Change, "slug[alice] place[2] prevPlace[3] crowdAge[35.2] crowdCount[123]", now, 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.CrowdAgeTop10Change, "slug[alice] place[11] crowdAge[35.2] crowdCount[123]", now, 8, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.AgeImprovementTop10Change, "slug[alice] clock[pheno] place[3] prevPlace[8] improvement[-6.8] ageReduction[-20.4]", now, 8, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.AgeImprovementTop10Change, "slug[alice] clock[pheno] place[11] improvement[-6.8] ageReduction[-20.4]", now, 8, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Podcast] cat[Global] val[] place[1]", now, 99, freshCutoff, true, true, SocialEventSkipReason.PodcastBadgeHandledImmediately };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Pheno Age - lowest] cat[Global] val[] place[2]", now, 2, freshCutoff, true, true, SocialEventSkipReason.NonWinningSingleWinnerBadge };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Pheno Age best improvement] cat[Global] val[] place[1]", now, 3, freshCutoff, false, true, SocialEventSkipReason.TiedBestImprovementBadge };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Crowd Age - lowest] cat[Global] val[] place[1]", now, 0, freshCutoff, true, true, SocialEventSkipReason.YoungestLookingBadge };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Age reduction] cat[Division] val[Men's] place[1]", now, 1, freshCutoff, true, false, SocialEventSkipReason.None };
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
        Assert.True(EventDataService.ShouldSkipSlackNotification(
            EventType.BecamePro,
            freshCutoffUtc,
            freshCutoffUtc));
        Assert.True(EventDataService.ShouldSkipSlackNotification(
            EventType.BiologicalAgeImproved,
            freshCutoffUtc,
            freshCutoffUtc));
        Assert.True(EventDataService.ShouldSkipSlackNotification(
            EventType.TestResultAccepted,
            freshCutoffUtc,
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
