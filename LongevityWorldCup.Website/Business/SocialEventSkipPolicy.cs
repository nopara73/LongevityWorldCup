using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public enum SocialEventSkipReason
{
    None,
    PlatformNotConfigured,
    UnsupportedEventType,
    UnsupportedEventPayload,
    UnsupportedBadgeAward,
    YoungestLookingBadge,
    PodcastBadgeHandledImmediately,
    NonWinningSingleWinnerBadge,
    TiedBestImprovementBadge,
    StalePrimaryEvent,
    EmptyMessage,
    FacebookSupportsCustomEventsOnly,
    DeliveryRetriesExhausted,
    PermanentDeliveryFailure,
    RoutineConversationUpdate,
    SupersededBadgeAward
}

public static class SocialEventSkipPolicy
{
    internal static SocialEventSkipReason? ConversationSkipReason(EventType type, string text, DateTime occurredAtUtc, DateTime freshCutoffUtc)
    {
        if (type == EventType.CustomEvent) return null;
        if (type == EventType.DonationReceived && !DonationReceivedPost.TryGetAmountSatoshis(text, out _))
            return SocialEventSkipReason.UnsupportedEventPayload;

        var newsworthy = type is EventType.AthleteCountMilestone or EventType.DonationReceived ||
            (type == EventType.NewRank && EventHelpers.TryExtractRank(text, out var rank) && rank is >= 1 and <= 3);
        if (!newsworthy) return SocialEventSkipReason.RoutineConversationUpdate;
        return occurredAtUtc < freshCutoffUtc ? SocialEventSkipReason.StalePrimaryEvent : null;
    }

    internal static bool IsSupersededBadgeAward(EventType type, string text, Func<string, bool> isCurrentBadgeAward) =>
        type == EventType.BadgeAward && !isCurrentBadgeAward(text);

    internal static bool TryGetBroadcastSkipReason(
        EventType type,
        string text,
        DateTime occurredAtUtc,
        SocialHeadline headline,
        DateTime freshCutoffUtc,
        Func<string, bool> hasSingleGlobalPlaceOneBadgeHolder,
        out SocialEventSkipReason reason,
        Func<string, bool>? isCurrentBadgeAward = null)
    {
        if (type == EventType.CustomEvent)
        {
            reason = default;
            return false;
        }

        if (type == EventType.DonationReceived)
        {
            reason = DonationReceivedPost.TryGetAmountSatoshis(text, out _)
                ? default
                : SocialEventSkipReason.UnsupportedEventPayload;
            return reason != default;
        }

        if (headline.Tier != SocialHeadlineTier.Other && occurredAtUtc < freshCutoffUtc)
        {
            reason = SocialEventSkipReason.StalePrimaryEvent;
            return true;
        }

        if (type == EventType.AthleteCountMilestone)
        {
            reason = default;
            return false;
        }

        if (type is EventType.BecamePro or EventType.BiologicalAgeImproved or EventType.TestResultAccepted)
        {
            reason = SocialEventSkipReason.UnsupportedEventType;
            return true;
        }

        if (type is EventType.NewRank or EventType.PhenoAgeTop10Change or EventType.CrowdAgeTop10Change or EventType.AgeImprovementTop10Change)
        {
            reason = headline.Tier == SocialHeadlineTier.Other ? SocialEventSkipReason.UnsupportedEventPayload : default;
            return reason != default;
        }

        if (type != EventType.BadgeAward)
        {
            reason = SocialEventSkipReason.UnsupportedEventType;
            return true;
        }

        if (!EventHelpers.TryExtractBadgeLabel(text, out var label))
        {
            reason = SocialEventSkipReason.UnsupportedEventPayload;
            return true;
        }

        var norm = EventHelpers.NormalizeBadgeLabel(label);
        if (string.Equals(norm, "Crowd Age – lowest", StringComparison.OrdinalIgnoreCase))
        {
            reason = SocialEventSkipReason.YoungestLookingBadge;
            return true;
        }

        if (string.Equals(norm, "Podcast", StringComparison.OrdinalIgnoreCase))
        {
            reason = SocialEventSkipReason.PodcastBadgeHandledImmediately;
            return true;
        }

        if (SocialHeadline.IsGlobalHeadlineBadge(norm) &&
            (!EventHelpers.TryExtractPlace(text, out var place) || place != 1))
        {
            reason = SocialEventSkipReason.NonWinningSingleWinnerBadge;
            return true;
        }

        if (IsBestImprovementBadge(norm) && !hasSingleGlobalPlaceOneBadgeHolder(label))
        {
            reason = SocialEventSkipReason.TiedBestImprovementBadge;
            return true;
        }

        if (headline.Tier == SocialHeadlineTier.Other)
        {
            reason = SocialEventSkipReason.UnsupportedBadgeAward;
            return true;
        }

        if (isCurrentBadgeAward is not null && !isCurrentBadgeAward(text))
        {
            reason = SocialEventSkipReason.SupersededBadgeAward;
            return true;
        }

        reason = default;
        return false;
    }

    private static bool IsBestImprovementBadge(string norm)
    {
        return
            string.Equals(norm, "Pheno Age best improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Bortz Age best improvement", StringComparison.OrdinalIgnoreCase);
    }
}
