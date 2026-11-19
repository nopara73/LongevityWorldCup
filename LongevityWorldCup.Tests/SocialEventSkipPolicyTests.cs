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
    [InlineData(EventType.DonationReceived, "tx[abc] sats[1000]", false)]
    [InlineData(EventType.DonationReceived, "tx[abc] sats[0]", true)]
    [InlineData(EventType.DonationReceived, "sats[1000]", true)]
    [InlineData(EventType.BadgeAward, "slug[alice] badge[Pheno Age best improvement] cat[Global] val[] place[1]", true)]
    [InlineData(EventType.CrowdAgeTop10Change, "slug[alice] place[1] prevPlace[2] crowdAge[25] crowdCount[100]", true)]
    [InlineData(EventType.AgeImprovementTop10Change, "slug[alice] clock[pheno] place[1] improvement[-9.2] ageReduction[-12]", true)]
    public void ConversationChannelsUseTheAnnouncementAllowlist(EventType type, string text, bool skip)
    {
        var now = DateTime.UtcNow;
        Assert.Equal(skip, EventDataService.ShouldSkipSlackNotification(type, now, now.AddDays(-7), text));
        Assert.Equal(skip, SocialEventSkipPolicy.ConversationSkipReason(type, text, now, now.AddDays(-7)) is not null);
    }

    public static IEnumerable<object[]> XOrThreadsGoldenSkipRules()
    {
        var now = new DateTime(2026, 6, 6, 12, 0, 0, DateTimeKind.Utc);
        var freshCutoff = now.AddDays(-7);

        yield return new object[] { EventType.PhenoAgeTop10Change, "slug[alice] place[6] prevPlace[8] ageReduction[-20.4]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.PhenoAgeTop10Change, "slug[alice] place[10] ageReduction[-10.4]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.PhenoAgeTop10Change, "slug[alice] place[11] ageReduction[-10.4]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.PhenoAgeTop10Change, "slug[alice] place[6]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.CustomEvent, "Title\n\nBody", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.Joined, "slug[alice]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.DonationReceived, "tx[abc] sats[1000]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.DonationReceived, "tx[abc] sats[1000]", now.AddYears(-1), freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.DonationReceived, "tx[abc] sats[0]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.DonationReceived, "sats[1000]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.NewRank, "slug[alice] rank[1]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.NewRank, "slug[alice] rank[10]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.NewRank, "slug[alice] rank[1]", now.AddDays(-8), freshCutoff, true, true, SocialEventSkipReason.StalePrimaryEvent };
        yield return new object[] { EventType.AthleteCountMilestone, "athletes[100]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.BecamePro, "slug[alice]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.TestResultAccepted, "slug[alice] date[2026-06-06]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.BecamePro, "not-a-slug", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.BiologicalAgeImproved, "slug[alice] clock[pheno] from[44.2] to[41.8]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.BiologicalAgeImproved, "slug[alice] clock[pheno] from[41.8] to[44.2]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventType };
        yield return new object[] { EventType.CrowdAgeTop10Change, "slug[alice] place[3] prevPlace[8] crowdAge[35.2] crowdCount[123]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.CrowdAgeTop10Change, "slug[alice] place[6] prevPlace[8] crowdAge[40.5] crowdCount[102]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.CrowdAgeTop10Change, "slug[alice] place[8] crowdAge[41] crowdCount[100]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.CrowdAgeTop10Change, "slug[alice] place[2] prevPlace[3] crowdAge[35.2] crowdCount[123]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.CrowdAgeTop10Change, "slug[alice] place[11] crowdAge[35.2] crowdCount[123]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.AgeImprovementTop10Change, "slug[alice] clock[pheno] place[3] prevPlace[8] improvement[-6.8] ageReduction[-20.4]", now, freshCutoff, true, false, SocialEventSkipReason.None };
        yield return new object[] { EventType.AgeImprovementTop10Change, "slug[alice] clock[pheno] place[11] improvement[-6.8] ageReduction[-20.4]", now, freshCutoff, true, true, SocialEventSkipReason.UnsupportedEventPayload };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Podcast] cat[Global] val[] place[1]", now, freshCutoff, true, true, SocialEventSkipReason.PodcastBadgeHandledImmediately };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Pheno Age - lowest] cat[Global] val[] place[2]", now, freshCutoff, true, true, SocialEventSkipReason.NonWinningSingleWinnerBadge };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Pheno Age best improvement] cat[Global] val[] place[1]", now, freshCutoff, false, true, SocialEventSkipReason.TiedBestImprovementBadge };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Crowd Age - lowest] cat[Global] val[] place[1]", now, freshCutoff, true, true, SocialEventSkipReason.YoungestLookingBadge };
        yield return new object[] { EventType.BadgeAward, "slug[alice] badge[Age reduction] cat[Division] val[Men's] place[1]", now, freshCutoff, true, false, SocialEventSkipReason.None };
    }

    [Theory]
    [MemberData(nameof(XOrThreadsGoldenSkipRules))]
    public void XOrThreads_TerminalSkipRulesMatchGoldenMatrix(
        EventType type,
        string text,
        DateTime occurredAtUtc,
        DateTime freshCutoffUtc,
        bool hasSingleGlobalPlaceOneBadgeHolder,
        bool expectedSkipped,
        SocialEventSkipReason expectedReason)
    {
        var skipped = SocialEventSkipPolicy.TryGetBroadcastSkipReason(
            type,
            text,
            occurredAtUtc,
            SocialHeadline.For(type, text),
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
        var skipped = SocialEventSkipPolicy.TryGetBroadcastSkipReason(
            EventType.Joined,
            "slug[alice]",
            DateTime.UtcNow,
            SocialHeadline.For(EventType.Joined, "slug[alice]"),
            freshCutoffUtc: DateTime.UtcNow.AddDays(-7),
            hasSingleGlobalPlaceOneBadgeHolder: _ => true,
            out var reason);

        Assert.True(skipped);
        Assert.Equal(SocialEventSkipReason.UnsupportedEventType, reason);
    }

    [Fact]
    public void XOrThreads_StalePrimaryEventsAreTerminalSkips()
    {
        var skipped = SocialEventSkipPolicy.TryGetBroadcastSkipReason(
            EventType.NewRank,
            "slug[alice] rank[1]",
            DateTime.UtcNow.AddDays(-8),
            SocialHeadline.For(EventType.NewRank, "slug[alice] rank[1]"),
            freshCutoffUtc: DateTime.UtcNow.AddDays(-7),
            hasSingleGlobalPlaceOneBadgeHolder: _ => true,
            out var reason);

        Assert.True(skipped);
        Assert.Equal(SocialEventSkipReason.StalePrimaryEvent, reason);
    }

    [Fact]
    public void XOrThreads_LowRankEventsAreTerminalSkips()
    {
        var skipped = SocialEventSkipPolicy.TryGetBroadcastSkipReason(
            EventType.NewRank,
            "slug[alice] rank[11]",
            DateTime.UtcNow,
            SocialHeadline.For(EventType.NewRank, "slug[alice] rank[11]"),
            freshCutoffUtc: DateTime.UtcNow.AddDays(-7),
            hasSingleGlobalPlaceOneBadgeHolder: _ => true,
            out var reason);

        Assert.True(skipped);
        Assert.Equal(SocialEventSkipReason.UnsupportedEventPayload, reason);
    }

    [Fact]
    public void XOrThreads_TiedBestImprovementBadgesAreTerminalSkips()
    {
        var skipped = SocialEventSkipPolicy.TryGetBroadcastSkipReason(
            EventType.BadgeAward,
            "slug[alice] badge[Pheno Age best improvement] cat[Global] val[] place[1]",
            DateTime.UtcNow,
            SocialHeadline.For(EventType.BadgeAward, "slug[alice] badge[Pheno Age best improvement] cat[Global] val[] place[1]"),
            freshCutoffUtc: DateTime.UtcNow.AddDays(-7),
            hasSingleGlobalPlaceOneBadgeHolder: _ => false,
            out var reason);

        Assert.True(skipped);
        Assert.Equal(SocialEventSkipReason.TiedBestImprovementBadge, reason);
    }
}
