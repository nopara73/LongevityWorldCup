using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed record AgeImprovementLeaderboardEntry(
    string Slug,
    string Name,
    string Clock,
    int Rank,
    double Improvement,
    double AgeReduction);

/// <summary>
/// Shared eligibility, scores and ordering for improvement leaderboards and Redemption Arc.
/// Scores come from the clock calculator's latest-by-test-date minus worst eligible result.
/// </summary>
public static class AgeImprovementRanking
{
    public static double? Metric(PhenoStatsCalculator.Result stats, string clock) => clock.ToLowerInvariant() switch
    {
        "pheno" => stats.PhenoAgeImprovementFromWorst,
        "bortz" => stats.BortzAgeImprovementFromWorst,
        _ => throw new ArgumentOutOfRangeException(nameof(clock), clock, "Unknown improvement clock.")
    };

    public static IReadOnlyList<AgeImprovementLeaderboardEntry> Rank(
        IEnumerable<PhenoStatsCalculator.Result> stats, string clock)
    {
        clock = clock.ToLowerInvariant();
        var useBortz = clock switch
        {
            "pheno" => false,
            "bortz" => true,
            _ => throw new ArgumentOutOfRangeException(nameof(clock), clock, "Unknown improvement clock.")
        };
        return stats.Select(s => new
            {
                Stats = s,
                Improvement = Metric(s, clock),
                AgeReduction = useBortz ? s.BortzAgeReduction : s.AgeReduction,
                SubmissionCount = useBortz ? s.BortzSubmissionCount : s.SubmissionCount
            })
            .Where(row => row.Stats.DobUtc.HasValue && row.SubmissionCount >= 2 &&
                row.Improvement is { } improvement && double.IsFinite(improvement) &&
                row.AgeReduction is { } reduction && double.IsFinite(reduction))
            .OrderBy(row => row.Improvement!.Value)
            .ThenBy(row => row.AgeReduction!.Value)
            .ThenBy(row => row.Stats.DobUtc!.Value)
            .ThenBy(row => row.Stats.Name, StringComparer.Ordinal)
            .Select((row, index) => new AgeImprovementLeaderboardEntry(
                row.Stats.Slug, row.Stats.Name, clock, index + 1,
                row.Improvement!.Value, row.AgeReduction!.Value))
            .ToList();
    }

    public static IReadOnlyList<AgeImprovementLeaderboardEntry> RedemptionArcHolders(
        IEnumerable<PhenoStatsCalculator.Result> stats, string clock)
    {
        var ranked = Rank(stats, clock);
        if (ranked.Count == 0 || ranked[0].Improvement >= 0)
            return [];

        // The badge shares the best score; leaderboard tie-breakers only order those holders.
        return ranked.TakeWhile(row => row.Improvement == ranked[0].Improvement).ToList();
    }
}
