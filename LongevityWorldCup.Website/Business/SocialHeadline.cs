using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

// Compare the competition first, then the place reached within it. A podium on a
// secondary board cannot displace the athlete's headline on the main leaderboard.
internal enum SocialHeadlineTier
{
    Ultimate,
    PhenoAge,
    LeagueTitle,
    GlobalBadge,
    Community,
    SecondaryLeaderboard,
    Other
}

internal readonly record struct SocialHeadline(SocialHeadlineTier Tier, int Place = 0, string? Subject = null)
{
    internal (SocialHeadlineTier Tier, int Place) Priority => (Tier, Place);

    internal static SocialHeadline For(EventType type, string text)
    {
        if (type is EventType.AthleteCountMilestone or EventType.DonationReceived)
            return new(SocialHeadlineTier.Community);

        if (!EventHelpers.TryExtractSlug(text, out var slug) || string.IsNullOrWhiteSpace(slug))
            return new(SocialHeadlineTier.Other);

        var subject = slug.Trim();
        return type switch
        {
            EventType.NewRank when EventHelpers.TryExtractTop10Rank(text, out var rank)
                => new(SocialHeadlineTier.Ultimate, rank, subject),
            EventType.PhenoAgeTop10Change when EventHelpers.TryExtractPhenoAgeTop10Change(text, out var place, out _, out _)
                => new(SocialHeadlineTier.PhenoAge, place, subject),
            EventType.CrowdAgeTop10Change when EventHelpers.TryExtractCrowdAgeTop10Change(text, out var place, out _, out _, out _)
                => new(SocialHeadlineTier.SecondaryLeaderboard, place, subject),
            EventType.AgeImprovementTop10Change when EventHelpers.TryExtractAgeImprovementTop10Change(text, out _, out var place, out _, out _, out _)
                => new(SocialHeadlineTier.SecondaryLeaderboard, place, subject),
            EventType.BadgeAward => new(BadgeTier(text), Subject: subject),
            _ => new(SocialHeadlineTier.Other)
        };
    }

    private static SocialHeadlineTier BadgeTier(string text)
    {
        if (!EventHelpers.TryExtractBadgeLabel(text, out var label) ||
            !EventHelpers.TryExtractPlace(text, out var place) || place != 1)
            return SocialHeadlineTier.Other;

        var badge = EventHelpers.NormalizeBadgeLabel(label);
        if (badge.Equals("Age reduction", StringComparison.OrdinalIgnoreCase) && EventHelpers.TryExtractCategory(text, out var category) &&
            !string.IsNullOrWhiteSpace(category) && !category.Equals("Global", StringComparison.OrdinalIgnoreCase))
            return SocialHeadlineTier.LeagueTitle;

        return IsGlobalHeadlineBadge(badge)
            ? SocialHeadlineTier.GlobalBadge
            : SocialHeadlineTier.Other;
    }

    internal static bool IsGlobalHeadlineBadge(string norm)
    {
        return
            string.Equals(norm, "Pheno Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Pheno Age best improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Bortz Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Bortz Age best improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Chronological age – oldest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Chronological age – youngest", StringComparison.OrdinalIgnoreCase);
    }
}
