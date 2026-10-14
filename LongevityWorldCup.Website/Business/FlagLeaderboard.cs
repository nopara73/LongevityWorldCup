namespace LongevityWorldCup.Website.Business;

public sealed record FlagLeaderboardTeam(FlagRouteInfo Flag, IReadOnlyList<LeaderboardSnapshotRow> Athletes,
    int TotalAthletes, string View, double Score, int? Rank);

public static class FlagLeaderboard
{
    public static string Explanation(string view) => view == "ultimate"
        ? "Top-three total age reduction. Complete Bortz teams first, then complete Pheno teams."
        : "Top-three total years. Lower wins.";

    public static string Label(string view) => view switch
    {
        "bortz" => "Bortz Age", "pheno" => "Pheno Age", "crowd" => "Crowd Age",
        "improvement" => "Pheno Improvement", "bortz-improvement" => "Bortz Improvement", _ => "Ultimate"
    };

    public static IReadOnlyList<FlagLeaderboardTeam> Build(IEnumerable<PublicAthlete> athletes, string view)
    {
        var teams = athletes.Select(athlete => (Athlete: athlete, Flag: FlagRouteCatalog.TryCreate(athlete.Row.Flag, out var flag) ? flag : null))
            .Where(item => item.Flag is not null)
            .GroupBy(item => item.Flag!.Slug, StringComparer.Ordinal)
            .Select(group =>
            {
                var members = group.Select(item => item.Athlete).ToArray();
                var clocks = view == "ultimate" ? new[] { "bortz", "pheno" } : [view];
                return clocks.Select(clock =>
                {
                    var top = Order(members.Where(athlete => Eligible(athlete, clock)), clock)
                        .Take(3).Select(athlete => athlete.Row with { EffectiveAgeReductionYears = athlete.Metric(clock) }).ToArray();
                    return new FlagLeaderboardTeam(group.First().Flag!, top, members.Length, clock,
                        top.Sum(row => row.EffectiveAgeReductionYears!.Value), null);
                }).OrderByDescending(team => team.Athletes.Count).ThenBy(team => team.View == "bortz" ? 0 : 1).First();
            })
            .Where(team => team.Athletes.Count > 0)
            .OrderByDescending(team => team.Athletes.Count)
            .ThenBy(team => view == "ultimate" && team.View == "bortz" ? 0 : 1)
            .ThenBy(team => team.Score)
            .ThenBy(team => team.Flag.Slug, StringComparer.Ordinal)
            .ToArray();
        int? rank = null;
        for (var i = 0; i < teams.Length; i++)
        {
            if (teams[i].Athletes.Count < 3) continue;
            if (i == 0 || teams[i].View != teams[i - 1].View || teams[i].Score != teams[i - 1].Score) rank = i + 1;
            teams[i] = teams[i] with { Rank = rank };
        }
        return teams;
    }

    private static bool Eligible(PublicAthlete athlete, string view) => athlete.Metric(view) is double metric && double.IsFinite(metric) && view switch
    {
        "bortz" => athlete.IsPro,
        "pheno" => athlete.Stats.LowestPhenoAgeDateUtc.HasValue,
        "crowd" => athlete.Stats.CrowdCount >= 100,
        _ => true
    };

    private static IEnumerable<PublicAthlete> Order(IEnumerable<PublicAthlete> eligible, string view)
    {
        var candidates = eligible.ToArray();
        var bySlug = candidates.ToDictionary(athlete => athlete.Row.Slug, StringComparer.Ordinal);
        IEnumerable<string> order = view switch
        {
            "crowd" => CompetitionRanking.SortByCrowdAgeRules(candidates.Select(athlete => new CrowdAgeRankCandidate(
                athlete.Row.Slug, athlete.Stats.Name, athlete.Stats.CrowdAge!.Value, athlete.Metric(view)!.Value,
                athlete.Stats.CrowdCount, athlete.Stats.DobUtc!.Value))).Select(athlete => athlete.Slug),
            "improvement" => CompetitionRanking.SortByPhenoAgeImprovementRules(candidates.Select(athlete => new PhenoAgeImprovementRankCandidate(
                athlete.Row.Slug, athlete.Stats.Name, athlete.Metric(view)!.Value, athlete.Stats.AgeReduction ?? 0,
                athlete.Stats.DobUtc!.Value))).Select(athlete => athlete.Slug),
            "bortz-improvement" => CompetitionRanking.SortByBortzAgeImprovementRules(candidates.Select(athlete => new BortzAgeImprovementRankCandidate(
                athlete.Row.Slug, athlete.Stats.Name, athlete.Metric(view)!.Value, athlete.Stats.BortzAgeReduction ?? 0,
                athlete.Stats.DobUtc!.Value))).Select(athlete => athlete.Slug),
            _ => CompetitionRanking.SortByCompetitionRules(candidates.Select(athlete => new CompetitionRankCandidate(
                athlete.Row.Slug, athlete.Stats.Name, false, athlete.Metric(view)!.Value,
                athlete.Stats.DobUtc!.Value))).Select(athlete => athlete.Slug)
        };
        return order.Select(slug => bySlug[slug]);
    }
}
