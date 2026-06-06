using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public enum SocialEventSkipReason
{
    None,
    PlatformNotConfigured,
    UnsupportedEventType,
    UnsupportedEventPayload,
    UnsupportedBadgeAward,
    PodcastBadgeHandledImmediately,
    NonWinningSingleWinnerBadge,
    TiedBestImprovementBadge,
    StalePrimaryEvent,
    EmptyMessage,
    FacebookSupportsCustomEventsOnly
}

public static class SocialEventSkipPolicy
{
    public static bool TryGetXOrThreadsTerminalSkipReason(
        EventType type,
        string text,
        DateTime occurredAtUtc,
        int priority,
        DateTime freshCutoffUtc,
        Func<string, bool> hasSingleGlobalPlaceOneBadgeHolder,
        out SocialEventSkipReason reason)
    {
        if (type == EventType.CustomEvent)
        {
            reason = default;
            return false;
        }

        if (priority <= EventDataService.XPriorityPrimaryMax && occurredAtUtc < freshCutoffUtc)
        {
            reason = SocialEventSkipReason.StalePrimaryEvent;
            return true;
        }

        if (type == EventType.AthleteCountMilestone)
        {
            reason = default;
            return false;
        }

        if (type == EventType.NewRank)
        {
            reason = EventHelpers.TryExtractRank(text, out var rank) && rank is >= 1 and <= 3
                ? default
                : SocialEventSkipReason.UnsupportedEventPayload;
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
        if (string.Equals(norm, "Podcast", StringComparison.OrdinalIgnoreCase))
        {
            reason = SocialEventSkipReason.PodcastBadgeHandledImmediately;
            return true;
        }

        if (IsSingleWinnerBadge(norm) &&
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

        if (priority > EventDataService.XPriorityPrimaryMax)
        {
            reason = SocialEventSkipReason.UnsupportedBadgeAward;
            return true;
        }

        reason = default;
        return false;
    }

    public static bool TryGetFacebookTerminalSkipReason(EventType type, out SocialEventSkipReason reason)
    {
        if (type == EventType.CustomEvent)
        {
            reason = default;
            return false;
        }

        reason = SocialEventSkipReason.FacebookSupportsCustomEventsOnly;
        return true;
    }

    private static bool IsSingleWinnerBadge(string norm)
    {
        return
            string.Equals(norm, "Pheno Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Pheno Age best improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Bortz Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Bortz Age best improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Chronological age – oldest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Chronological age – youngest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBestImprovementBadge(string norm)
    {
        return
            string.Equals(norm, "Pheno Age best improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Bortz Age best improvement", StringComparison.OrdinalIgnoreCase);
    }
}
