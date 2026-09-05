using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

internal static class SocialPostPolicy
{
    internal static XPostSampleBasis? DetermineSampleBasisForEvent(EventType type, string rawText)
    {
        if (type == EventType.NewRank)
            return XPostSampleBasis.Combined;

        if (type == EventType.BecamePro)
            return XPostSampleBasis.Bortz;

        if (type == EventType.BiologicalAgeImproved)
        {
            if (!EventHelpers.TryExtractClock(rawText, out var clock))
                return null;

            return string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase)
                ? XPostSampleBasis.Bortz
                : XPostSampleBasis.PhenoAge;
        }

        if (type != EventType.BadgeAward)
            return null;

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label))
            return null;

        var norm = EventHelpers.NormalizeBadgeLabel(label);
        if (string.Equals(norm, "Pheno Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Pheno Age best improvement", StringComparison.OrdinalIgnoreCase))
            return XPostSampleBasis.PhenoAge;

        if (string.Equals(norm, "Bortz Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Bortz Age best improvement", StringComparison.OrdinalIgnoreCase))
            return XPostSampleBasis.Bortz;

        if (string.Equals(norm, "Age reduction", StringComparison.OrdinalIgnoreCase) &&
            EventHelpers.TryExtractPlace(rawText, out var place) && place == 1 &&
            EventHelpers.TryExtractCategory(rawText, out var category) &&
            !string.Equals(category, "Global", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(category, "Amateur", StringComparison.OrdinalIgnoreCase))
                return XPostSampleBasis.PhenoAge;

            return XPostSampleBasis.Combined;
        }

        return null;
    }

    internal static XPostSampleBasis? DetermineSampleBasisForFiller(FillerType fillerType, string payloadText)
    {
        if (fillerType == FillerType.DomainTop)
        {
            if (!EventHelpers.TryExtractDomain(payloadText, out var domain))
                return null;

            if (string.Equals(domain, "inflammation", StringComparison.OrdinalIgnoreCase))
                return XPostSampleBasis.Combined;

            return XPostSampleBasis.Bortz;
        }

        if (fillerType == FillerType.Top3Leaderboard)
        {
            if (!EventHelpers.TryExtractLeague(payloadText, out var league))
                return null;

            if (string.Equals(league, "amateur", StringComparison.OrdinalIgnoreCase))
                return XPostSampleBasis.PhenoAge;

            return XPostSampleBasis.Combined;
        }

        return null;
    }

    internal static string? DetermineLeagueScopeForEvent(EventType type, string rawText)
    {
        if (type == EventType.NewRank)
            return "ultimate";

        if (type != EventType.BadgeAward)
            return null;

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label))
            return null;

        var norm = EventHelpers.NormalizeBadgeLabel(label);
        if (string.Equals(norm, "Age reduction", StringComparison.OrdinalIgnoreCase) &&
            EventHelpers.TryExtractPlace(rawText, out var place) && place == 1 &&
            EventHelpers.TryExtractCategory(rawText, out var category))
        {
            EventHelpers.TryExtractValue(rawText, out var value);
            return SocialPostLinks.LeagueContextSlug(category, value);
        }

        return null;
    }

    internal static string? DetermineLeagueScopeForFiller(FillerType fillerType, string payloadText)
    {
        if (fillerType == FillerType.Top3Leaderboard &&
            EventHelpers.TryExtractLeague(payloadText, out var league) &&
            !string.IsNullOrWhiteSpace(league))
            return league.Trim();

        return null;
    }
}
