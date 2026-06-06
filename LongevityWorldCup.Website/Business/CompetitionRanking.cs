namespace LongevityWorldCup.Website.Business;

public sealed record CompetitionRankCandidate(
    string Slug,
    string Name,
    bool HasBortz,
    double EffectiveReduction,
    DateTime DobUtc);

public sealed record CrowdAgeRankCandidate(
    string Slug,
    string Name,
    double CrowdAge,
    double CrowdAgeReduction,
    int CrowdCount,
    DateTime DobUtc);

public sealed record PhenoAgeImprovementRankCandidate(
    string Slug,
    string Name,
    double PhenoAgeImprovement,
    double PhenoAgeReduction,
    DateTime DobUtc);

public sealed record BortzAgeImprovementRankCandidate(
    string Slug,
    string Name,
    double BortzAgeImprovement,
    double BortzAgeReduction,
    DateTime DobUtc);

/// <summary>
/// Result returned by a hypothetical Ultimate League rank preview.
/// </summary>
/// <param name="Rank">One-based rank the hypothetical result would receive in the selected field.</param>
/// <param name="FieldSize">Field size including the hypothetical result.</param>
/// <param name="CurrentFieldSize">Current field size excluding the hypothetical result.</param>
/// <param name="LeagueName">League used for the preview. Currently Ultimate League.</param>
/// <param name="Category">Track used for the preview, either Pro or Amateur.</param>
/// <param name="AgeDifference">Signed biological age difference, calculated as biological age minus chronological age. Lower and more negative values rank higher within the same track.</param>
/// <param name="Nearby">Nearby athletes around the hypothetical result after sorting.</param>
public sealed record HypotheticalRankResult(
    int Rank,
    int FieldSize,
    int CurrentFieldSize,
    string LeagueName,
    string Category,
    double AgeDifference,
    IReadOnlyList<HypotheticalRankNeighbor> Nearby);

/// <summary>
/// One neighboring row in a hypothetical rank preview.
/// </summary>
/// <param name="Rank">One-based rank in the preview field.</param>
/// <param name="Name">Athlete display name, or the hypothetical row label.</param>
/// <param name="Category">Track for the row, either Pro or Amateur.</param>
/// <param name="AgeDifference">Signed age difference used for sorting.</param>
/// <param name="IsHypothetical">True for the submitted hypothetical result row.</param>
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

    public static IOrderedEnumerable<CrowdAgeRankCandidate> SortByCrowdAgeRules(IEnumerable<CrowdAgeRankCandidate> rows)
    {
        return rows
            .OrderBy(t => t.CrowdAgeReduction)
            .ThenByDescending(t => t.CrowdCount)
            .ThenBy(t => t.DobUtc)
            .ThenBy(t => t.Name, StringComparer.Ordinal);
    }

    public static IOrderedEnumerable<PhenoAgeImprovementRankCandidate> SortByPhenoAgeImprovementRules(IEnumerable<PhenoAgeImprovementRankCandidate> rows)
    {
        return rows
            .OrderBy(t => t.PhenoAgeImprovement)
            .ThenBy(t => t.PhenoAgeReduction)
            .ThenBy(t => t.DobUtc)
            .ThenBy(t => t.Name, StringComparer.Ordinal);
    }

    public static IOrderedEnumerable<BortzAgeImprovementRankCandidate> SortByBortzAgeImprovementRules(IEnumerable<BortzAgeImprovementRankCandidate> rows)
    {
        return rows
            .OrderBy(t => t.BortzAgeImprovement)
            .ThenBy(t => t.BortzAgeReduction)
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
