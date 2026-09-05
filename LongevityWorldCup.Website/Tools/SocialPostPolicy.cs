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

    internal static XPostPhase? GetPhase(
        Func<XPostSampleBasis, XPostSampleSize>? sampleForBasis,
        XPostSampleBasis? basis,
        Func<string, int?>? getFieldSizeForLeague,
        string? leagueScope)
    {
        XPostPhase? basisPhase = null;
        if (basis.HasValue && sampleForBasis is not null)
        {
            var sample = sampleForBasis(basis.Value);
            basisPhase = XPostPhaseDecider.Determine(sample);
        }

        XPostPhase? scopePhase = null;
        if (!string.IsNullOrWhiteSpace(leagueScope) && getFieldSizeForLeague is not null)
        {
            var fieldSize = getFieldSizeForLeague(leagueScope);
            if (fieldSize.HasValue)
            {
                var scopedSample = new XPostSampleSize(
                    Basis: basis ?? XPostSampleBasis.Combined,
                    N: fieldSize.Value,
                    PhenoCount: 0,
                    BortzCount: 0,
                    CombinedCount: fieldSize.Value);
                scopePhase = XPostPhaseDecider.Determine(scopedSample);
            }
        }

        if (basisPhase.HasValue && scopePhase.HasValue)
            return XPostPhaseDecider.Min(basisPhase.Value, scopePhase.Value);

        return basisPhase ?? scopePhase;
    }

    internal static XPostPhase? GetTop3LeaderboardPhase(
        string payloadText,
        Func<XPostSampleBasis, XPostSampleSize>? sampleForBasis,
        Func<string, int?>? getFieldSizeForLeague,
        Func<string, int?>? getBortzFieldSizeForLeague)
    {
        if (!EventHelpers.TryExtractLeague(payloadText, out var leagueSlug) || string.IsNullOrWhiteSpace(leagueSlug))
            return null;

        var normalizedLeague = leagueSlug.Trim();
        if (string.Equals(normalizedLeague, "amateur", StringComparison.OrdinalIgnoreCase))
            return GetPhase(sampleForBasis, XPostSampleBasis.PhenoAge, getFieldSizeForLeague, normalizedLeague);

        var totalPhase = GetPhase(null, null, getFieldSizeForLeague, normalizedLeague);
        var bortzPhase = GetPhase(null, null, getBortzFieldSizeForLeague, normalizedLeague);

        if (totalPhase.HasValue && bortzPhase.HasValue)
            return XPostPhaseDecider.Min(totalPhase.Value, bortzPhase.Value);

        return bortzPhase ?? totalPhase;
    }

    internal static bool ShouldSuppressEvent(EventType type, string rawText, XPostPhase? phase)
    {
        if (!phase.HasValue || phase == XPostPhase.Mature)
            return false;

        if (type == EventType.NewRank)
        {
            return !EventHelpers.TryExtractRank(rawText, out var rank) || rank != 1;
        }

        if (type != EventType.BadgeAward)
            return false;

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label))
            return false;

        var norm = EventHelpers.NormalizeBadgeLabel(label);
        if (string.Equals(norm, "Podcast", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(norm, "Pheno Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Bortz Age – lowest", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(norm, "Age reduction", StringComparison.OrdinalIgnoreCase))
        {
            return !(
                EventHelpers.TryExtractPlace(rawText, out var place) &&
                place == 1 &&
                EventHelpers.TryExtractCategory(rawText, out var category) &&
                !string.Equals(category, "Global", StringComparison.OrdinalIgnoreCase));
        }

        if (phase == XPostPhase.Early &&
            (string.Equals(norm, "Pheno Age best improvement", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(norm, "Bortz Age best improvement", StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    internal static bool ShouldSuppressFiller(FillerType fillerType, XPostPhase? phase)
    {
        if (!phase.HasValue)
            return false;

        if (phase == XPostPhase.Mature)
            return false;

        return fillerType switch
        {
            FillerType.Top3Leaderboard => phase == XPostPhase.Tiny,
            FillerType.DomainTop => true,
            FillerType.Newcomers => false,
            _ => false
        };
    }
}
