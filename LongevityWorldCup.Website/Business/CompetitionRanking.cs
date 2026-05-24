namespace LongevityWorldCup.Website.Business;

public sealed record CompetitionRankCandidate(
    string Slug,
    string Name,
    bool HasBortz,
    double EffectiveReduction,
    DateTime DobUtc);

public sealed record HypotheticalRankResult(
    int Rank,
    int FieldSize,
    int CurrentFieldSize,
    string LeagueName,
    string Category,
    double AgeDifference,
    IReadOnlyList<HypotheticalRankNeighbor> Nearby);

public sealed record HypotheticalRankNeighbor(
    int Rank,
    string Name,
    string Category,
    double AgeDifference,
    bool IsHypothetical);

public static class CompetitionRanking
{
    public const string HypotheticalSlug = "__hypothetical__";
    public const string HypotheticalName = "Your result";

    public static IOrderedEnumerable<CompetitionRankCandidate> SortByCompetitionRules(IEnumerable<CompetitionRankCandidate> rows)
    {
        return rows
            .OrderByDescending(t => t.HasBortz)
            .ThenBy(t => t.EffectiveReduction)
            .ThenBy(t => t.DobUtc)
            .ThenBy(t => t.Name, StringComparer.Ordinal);
    }

    public static HypotheticalRankResult CalculateHypothetical(
        IEnumerable<CompetitionRankCandidate> currentField,
        double chronologicalAge,
        double biologicalAge,
        DateTime dobUtc,
        bool hasBortz)
    {
        if (!double.IsFinite(chronologicalAge))
            throw new ArgumentOutOfRangeException(nameof(chronologicalAge));
        if (!double.IsFinite(biologicalAge))
            throw new ArgumentOutOfRangeException(nameof(biologicalAge));

        var hypothetical = new CompetitionRankCandidate(
            HypotheticalSlug,
            HypotheticalName,
            hasBortz,
            biologicalAge - chronologicalAge,
            dobUtc.Date);

        var sorted = SortByCompetitionRules(currentField.Append(hypothetical)).ToList();
        var index = sorted.FindIndex(x => string.Equals(x.Slug, HypotheticalSlug, StringComparison.Ordinal));
        if (index < 0)
            throw new InvalidOperationException("Hypothetical result was not found in the ranked field.");

        var start = Math.Max(0, index - 2);
        var count = Math.Min(sorted.Count - start, 5);
        var nearby = sorted
            .Skip(start)
            .Take(count)
            .Select((x, i) => new HypotheticalRankNeighbor(
                start + i + 1,
                x.Name,
                x.HasBortz ? "Pro" : "Amateur",
                x.EffectiveReduction,
                string.Equals(x.Slug, HypotheticalSlug, StringComparison.Ordinal)))
            .ToList();

        return new HypotheticalRankResult(
            index + 1,
            sorted.Count,
            sorted.Count - 1,
            "Ultimate League",
            hasBortz ? "Pro" : "Amateur",
            hypothetical.EffectiveReduction,
            nearby);
    }
}
