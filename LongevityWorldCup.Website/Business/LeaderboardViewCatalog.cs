using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed record LeaderboardViewDefinition(string Slug, string Name, string HtmlPath, string Metric, string Rules)
{
    public string MarkdownPath => Slug == "ultimate" ? "/ai/leaderboard.md" : $"/ai/league/{Slug}.md";
}

/// <summary>Public view definitions shared by machine documents and content dependency tracking.</summary>
public static class LeaderboardViewCatalog
{
    private const string UltimateRules = "Pro athletes rank before Amateur athletes. Within each track, lower effective age reduction ranks first, then earlier date of birth and athlete name. Effective age reduction uses bortz age for Pro athletes and pheno age otherwise.";
    private const string FilterRules = "The selected field retains Ultimate League ordering; rank is the position within this field.";
    public static readonly IReadOnlyList<LeaderboardViewDefinition> Views =
    [
        new("ultimate", "Ultimate League", "/leaderboard", "Effective age reduction", UltimateRules),
        new("bortz", "Bortz Age League", "/league/bortz", "Bortz age reduction", "Requires an eligible bortz result. Rank by lower bortz age minus chronological age at that result, then earlier date of birth and athlete name."),
        new("pheno", "Pheno Age League", "/league/pheno", "Pheno age reduction", "Uses the public Ultimate field without Pro-first ordering. Rank by lower pheno age reduction, then earlier date of birth and athlete name. The existing competition fallback score is zero when no pheno result is available; missing biological-age values remain unavailable."),
        new("crowd", "Crowd Age League", "/league/crowd", "Crowd age reduction", "Requires at least 100 accepted realistic guesses for the current profile image. Rank by lower median crowd age minus current chronological age, then more guesses, earlier date of birth and athlete name. This is a perceived-age view."),
        new("improvement", "Pheno Improvement League", "/league/improvement", "Pheno improvement from worst", "Requires at least two eligible pheno results. Rank by latest eligible pheno age minus worst eligible pheno age, then pheno age reduction, earlier date of birth and athlete name. This differs from improvement from the first result."),
        new("bortz-improvement", "Bortz Improvement League", "/league/bortz-improvement", "Bortz improvement from worst", "Requires at least two eligible bortz results. Rank by latest eligible bortz age minus worst eligible bortz age, then bortz age reduction, earlier date of birth and athlete name. This differs from improvement from the first result."),
        new("amateur", "Amateur League", "/league/amateur", "Pheno age reduction", "Includes athletes without an eligible bortz result. " + FilterRules),
        new("mens", "Men's League", "/league/mens", "Effective age reduction", FilterRules),
        new("womens", "Women's League", "/league/womens", "Effective age reduction", FilterRules),
        new("open", "Open League", "/league/open", "Effective age reduction", FilterRules),
        new("silent-generation", "Silent Generation League", "/league/silent-generation", "Effective age reduction", FilterRules),
        new("baby-boomers", "Baby Boomers League", "/league/baby-boomers", "Effective age reduction", FilterRules),
        new("gen-x", "Gen X League", "/league/gen-x", "Effective age reduction", FilterRules),
        new("millennials", "Millennials League", "/league/millennials", "Effective age reduction", FilterRules),
        new("gen-z", "Gen Z League", "/league/gen-z", "Effective age reduction", FilterRules),
        new("gen-alpha", "Gen Alpha League", "/league/gen-alpha", "Effective age reduction", FilterRules),
        new("prosperan", "Prosperan League", "/league/prosperan", "Effective age reduction", FilterRules)
    ];

    public static LeaderboardViewDefinition? Find(string slug) =>
        Views.FirstOrDefault(view => view.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<LeaderboardSnapshotRow> SelectRows(string path, IReadOnlyList<LeaderboardSnapshotRow> rows,
        IReadOnlyDictionary<string, PhenoStatsCalculator.Result> stats)
    {
        if (path.StartsWith("/flag/", StringComparison.Ordinal))
            return rows.Where(row => FlagRouteCatalog.TryCreate(row.Flag, out var flag) && flag.Path == path).ToArray();
        var selected = path switch
        {
            "/league/amateur" => rows.Where(row => row.Track == "Amateur"),
            "/league/mens" => rows.Where(row => string.Equals(row.Division, "Men's", StringComparison.OrdinalIgnoreCase)),
            "/league/womens" => rows.Where(row => string.Equals(row.Division, "Women's", StringComparison.OrdinalIgnoreCase)),
            "/league/open" => rows.Where(row => string.Equals(row.Division, "Open", StringComparison.OrdinalIgnoreCase)),
            "/league/silent-generation" => rows.Where(row => string.Equals(row.Generation, "Silent Generation", StringComparison.OrdinalIgnoreCase)),
            "/league/baby-boomers" => rows.Where(row => string.Equals(row.Generation, "Baby Boomers", StringComparison.OrdinalIgnoreCase)),
            "/league/gen-x" => rows.Where(row => string.Equals(row.Generation, "Gen X", StringComparison.OrdinalIgnoreCase)),
            "/league/millennials" => rows.Where(row => string.Equals(row.Generation, "Millennials", StringComparison.OrdinalIgnoreCase)),
            "/league/gen-z" => rows.Where(row => string.Equals(row.Generation, "Gen Z", StringComparison.OrdinalIgnoreCase)),
            "/league/gen-alpha" => rows.Where(row => string.Equals(row.Generation, "Gen Alpha", StringComparison.OrdinalIgnoreCase)),
            "/league/prosperan" => rows.Where(row => string.Equals(row.ExclusiveLeague, "Prosperan", StringComparison.OrdinalIgnoreCase)),
            "/league/bortz" => rows.Where(row => row.Track == "Pro"),
            "/league/improvement" => rows.Where(row => Finite(stats[row.Slug].PhenoAgeImprovementFromWorst)),
            "/league/bortz-improvement" => rows.Where(row => Finite(stats[row.Slug].BortzAgeImprovementFromWorst)),
            "/league/crowd" => rows.Where(row => stats[row.Slug].CrowdCount >= 100 && Finite(stats[row.Slug].CrowdAge)),
            _ => rows
        };
        var candidates = selected.Select(row => stats[row.Slug]).Where(s => s.DobUtc.HasValue).ToArray();
        IEnumerable<string>? ordered = path switch
        {
            "/league/bortz" or "/league/pheno" => CompetitionRanking.SortByCompetitionRules(candidates.Select(s => new CompetitionRankCandidate(
                s.Slug, s.Name, false, (path == "/league/pheno" ? s.AgeReduction : s.BortzAgeReduction) ?? 0, s.DobUtc!.Value))).Select(s => s.Slug),
            "/league/improvement" => CompetitionRanking.SortByPhenoAgeImprovementRules(candidates.Select(s => new PhenoAgeImprovementRankCandidate(
                s.Slug, s.Name, s.PhenoAgeImprovementFromWorst!.Value, s.AgeReduction ?? 0, s.DobUtc!.Value))).Select(s => s.Slug),
            "/league/bortz-improvement" => CompetitionRanking.SortByBortzAgeImprovementRules(candidates.Select(s => new BortzAgeImprovementRankCandidate(
                s.Slug, s.Name, s.BortzAgeImprovementFromWorst!.Value, s.BortzAgeReduction ?? 0, s.DobUtc!.Value))).Select(s => s.Slug),
            "/league/crowd" => CompetitionRanking.SortByCrowdAgeRules(candidates.Select(s => new CrowdAgeRankCandidate(
                s.Slug, s.Name, s.CrowdAge!.Value, s.CrowdAge.Value - (s.ChronoAge ?? 0), s.CrowdCount, s.DobUtc!.Value))).Select(s => s.Slug),
            _ => null
        };
        var bySlug = rows.ToDictionary(row => row.Slug, StringComparer.Ordinal);
        return ordered is null ? selected.ToArray() : ordered.Select(slug => bySlug[slug]).ToArray();
    }

    public static double? Metric(string path, LeaderboardSnapshotRow row, PhenoStatsCalculator.Result stats) => path switch
    {
        "/league/pheno" => stats.AgeReduction,
        "/league/improvement" => stats.PhenoAgeImprovementFromWorst,
        "/league/bortz-improvement" => stats.BortzAgeImprovementFromWorst,
        "/league/crowd" => stats.CrowdAge - stats.ChronoAge,
        "/league/bortz" => stats.BortzAgeReduction,
        _ => row.EffectiveAgeReductionYears
    };

    private static bool Finite(double? value) => value.HasValue && double.IsFinite(value.Value);
}
