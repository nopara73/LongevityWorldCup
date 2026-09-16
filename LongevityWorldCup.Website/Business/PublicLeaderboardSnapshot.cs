using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

/// <summary>
/// One coherent, full-precision snapshot for the initial HTML. The browser still
/// owns interactive filtering, badges, charts and live updates.
/// </summary>
public sealed class PublicLeaderboardSnapshot
{
    public static readonly string[] Views = ["ultimate", "bortz", "pheno", "improvement", "bortz-improvement", "crowd"];
    public static readonly IReadOnlyDictionary<string, string> LeagueFilters = new Dictionary<string, string>
    {
        ["amateur"] = "Amateur", ["professional"] = "Professional", ["womens"] = "Women's",
        ["mens"] = "Men's", ["open"] = "Open", ["silent-generation"] = "Silent Generation",
        ["baby-boomers"] = "Baby Boomers", ["gen-x"] = "Gen X", ["millennials"] = "Millennials",
        ["gen-z"] = "Gen Z", ["gen-alpha"] = "Gen Alpha", ["prosperan"] = "Prosperan"
    };

    public IReadOnlyList<PublicAthlete> Athletes { get; }

    public PublicLeaderboardSnapshot(JsonArray athletes, DateTime asOf)
    {
        var stats = PhenoStatsCalculator.BuildAll(athletes, asOf);
        var order = CompetitionRanking.SortByCompetitionRules(stats.Values
            .Where(s => s.DobUtc.HasValue)
            .Select(s => new CompetitionRankCandidate(s.Slug, s.Name, IsFinite(s.BortzAgeReduction),
                s.BortzAgeReduction ?? s.AgeReduction ?? 0, s.DobUtc!.Value))).ToList();
        // Reuse the public snapshot's asset URLs, name selection and contact privacy policy.
        var rankedJson = new JsonArray(order.Select(candidate => (JsonNode)new JsonObject
        {
            ["AthleteSlug"] = candidate.Slug,
            ["Name"] = candidate.Name,
            ["AgeDifference"] = candidate.EffectiveReduction,
            ["ChronologicalAge"] = stats[candidate.Slug].ChronoAge,
            ["LowestPhenoAge"] = stats[candidate.Slug].LowestPhenoAge,
            ["LowestBortzAge"] = stats[candidate.Slug].LowestBortzAge
        }).ToArray());
        var rows = LeaderboardSnapshotBuilder.Build(rankedJson, athletes).Rows;
        var source = athletes.OfType<JsonObject>().ToDictionary(a => a["AthleteSlug"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        Athletes = rows.Select(row => new PublicAthlete(row, stats[row.Slug], source[row.Slug])).ToList();
    }

    public PublicLeaderboardSelection Select(string path, IQueryCollection query)
    {
        var slug = path.StartsWith("/league/", StringComparison.OrdinalIgnoreCase) ? path[8..].Trim('/') : "";
        var rawView = query["view"].FirstOrDefault();
        if (string.IsNullOrEmpty(rawView)) rawView = slug;
        var view = rawView?.ToLowerInvariant() == "pheno-improvement" ? "improvement" : rawView?.ToLowerInvariant();
        if (!Views.Contains(view)) view = "ultimate";
        var filtersParam = query["filters"].FirstOrDefault();
        var filters = !string.IsNullOrEmpty(filtersParam)
            ? filtersParam.Split(',').Select(DecodeFilter).ToArray()
            : LeagueFilters.TryGetValue(slug, out var label) ? [label] : Array.Empty<string>();
        if (string.IsNullOrEmpty(filtersParam) && path.StartsWith("/flag/", StringComparison.OrdinalIgnoreCase)
            && FlagRouteCatalog.TryResolve(path[6..].Trim('/'), Athletes.Select(a => a.Row.Flag), out var flag))
            filters = [flag.Name];

        var divisions = MatchValues(filters, a => a.Row.Division);
        var generations = MatchValues(filters, a => a.Row.Generation);
        var exclusive = MatchValues(filters, a => a.Row.ExclusiveLeague);
        var flags = Athletes.Select(a => FlagKey(a.Row.Flag)).Where(key => key.Length > 0 && filters.Any(f => FlagKey(f) == key)).ToHashSet();
        var tracks = filters.Where(f => f.Equals("professional", StringComparison.OrdinalIgnoreCase) || f.Equals("amateur", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var cohort = Order(view!).Where(a =>
            (tracks.Length != 1 || a.IsPro == tracks[0].Equals("professional", StringComparison.OrdinalIgnoreCase)) &&
            (divisions.Count == 0 || divisions.Contains(a.Row.Division)) &&
            (generations.Count == 0 || generations.Contains(a.Row.Generation)) &&
            (exclusive.Count == 0 || exclusive.Contains(a.Row.ExclusiveLeague)) &&
            (flags.Count == 0 || flags.Contains(FlagKey(a.Row.Flag)))).ToList();
        var rows = cohort.Select((athlete, index) => athlete.Row with
        {
            Rank = index + 1,
            AnchorRank = athlete.Row.Rank,
            AthleteName = athlete.Stats.Name,
            EffectiveAgeReductionYears = athlete.Metric(view!),
            MetricDecimals = MetricDecimals(cohort, index, view!)
        }).ToList();
        var railTitle = view switch
        {
            "bortz" => "Bortz Age League", "pheno" => "Pheno Age League",
            "improvement" => "Pheno Improvement League", "bortz-improvement" => "Bortz Improvement League",
            "crowd" => "Crowd Age League", _ => "Ultimate League"
        };
        if (view == "ultimate")
        {
            if (flags.Count == 1) railTitle = FlagCanonicalizer.GetCanonicalName(Athletes.First(a => flags.Contains(FlagKey(a.Row.Flag))).Row.Flag);
            else if (flags.Count > 1) railTitle = $"{flags.Count} Flags";
            else if (exclusive.Count > 0) railTitle = exclusive.Count == 1 ? exclusive.First() + " League" : "Exclusive Leagues";
            else if (generations.Count > 0 || divisions.Count > 0)
                railTitle = string.Join(' ', new[] { GenerationLabel(generations), DivisionLabel(divisions) }.Where(s => s.Length > 0)) + " League";
            else if (tracks.Length == 1) railTitle = tracks[0].Equals("professional", StringComparison.OrdinalIgnoreCase) ? "Pro League" : "Amateur League";
        }
        var isDefault = view == "ultimate" && divisions.Count == 0 && generations.Count == 0 && exclusive.Count == 0 && flags.Count == 0 && tracks.Length != 1;
        return new PublicLeaderboardSelection(view!, new LeaderboardSnapshot(rows), railTitle, isDefault);
    }

    public IReadOnlyList<PublicAthlete> Order(string view)
    {
        var candidates = Athletes.Where(a => view switch
        {
            "bortz" => a.IsPro,
            "improvement" => IsFinite(a.Stats.PhenoAgeImprovementFromWorst),
            "bortz-improvement" => IsFinite(a.Stats.BortzAgeImprovementFromWorst),
            "crowd" => a.Stats.CrowdCount >= 100 && IsFinite(a.Metric("crowd")),
            _ => true
        }).ToList();
        IEnumerable<string> order = view switch
        {
            "crowd" => CompetitionRanking.SortByCrowdAgeRules(candidates.Select(a => new CrowdAgeRankCandidate(
                a.Row.Slug, a.Stats.Name, a.Stats.CrowdAge!.Value, a.Metric(view)!.Value, a.Stats.CrowdCount, a.Stats.DobUtc!.Value))).Select(a => a.Slug),
            "improvement" => CompetitionRanking.SortByPhenoAgeImprovementRules(candidates.Select(a => new PhenoAgeImprovementRankCandidate(
                a.Row.Slug, a.Stats.Name, a.Metric(view)!.Value, a.Stats.AgeReduction ?? 0, a.Stats.DobUtc!.Value))).Select(a => a.Slug),
            "bortz-improvement" => CompetitionRanking.SortByBortzAgeImprovementRules(candidates.Select(a => new BortzAgeImprovementRankCandidate(
                a.Row.Slug, a.Stats.Name, a.Metric(view)!.Value, a.Stats.BortzAgeReduction ?? 0, a.Stats.DobUtc!.Value))).Select(a => a.Slug),
            _ => CompetitionRanking.SortByCompetitionRules(candidates.Select(a => new CompetitionRankCandidate(
                a.Row.Slug, a.Stats.Name, view != "pheno" && a.IsPro, a.Metric(view) ?? 0, a.Stats.DobUtc!.Value))).Select(a => a.Slug)
        };
        var bySlug = candidates.ToDictionary(a => a.Row.Slug, StringComparer.OrdinalIgnoreCase);
        return order.Select(slug => bySlug[slug]).ToList();
    }

    private HashSet<string> MatchValues(string[] filters, Func<PublicAthlete, string> getValue) =>
        Athletes.Select(getValue).Where(value => !string.IsNullOrEmpty(value) && filters.Contains(value, StringComparer.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string FlagKey(string value) => FlagRouteCatalog.TryCreate(value, out var route) ? route.Slug : "";
    private static string SelectionKey(HashSet<string> values) => string.Join('|', values.Select(v => v.ToLowerInvariant()).Order(StringComparer.Ordinal));
    private static string GenerationLabel(HashSet<string> values) => values.Count switch
    {
        0 => "", 1 => values.First(), _ => SelectionKey(values) switch
        {
            "baby boomers|silent generation" => "Heritage", "baby boomers|gen x" => "Senior",
            "gen x|millennials" => "Prime", "gen z|millennials" => "Rising", "gen alpha|gen z" => "Next-gen", _ => "Multi-generation"
        }
    };
    private static string DivisionLabel(HashSet<string> values) => values.Count switch
    {
        0 => "", 1 => values.First(), >= 3 => "All Divisions", _ => SelectionKey(values) switch
        {
            "men's|women's" => "Mixed", "men's|open" => "Inclusive Men's", "open|women's" => "Inclusive Women's", _ => "Multi-division"
        }
    };
    private static bool IsFinite(double? value) => value.HasValue && double.IsFinite(value.Value);
    private static string DecodeFilter(string value)
    {
        for (var i = 0; i < 2; i++) value = Uri.UnescapeDataString(value);
        return value.Trim();
    }

    private static int MetricDecimals(IReadOnlyList<PublicAthlete> cohort, int index, string view)
    {
        var value = PublicHtmlFormat.Fixed(cohort[index].Metric(view), 1);
        return (index > 0 && value == PublicHtmlFormat.Fixed(cohort[index - 1].Metric(view), 1)) ||
               (index + 1 < cohort.Count && value == PublicHtmlFormat.Fixed(cohort[index + 1].Metric(view), 1)) ? 2 : 1;
    }
}

public sealed record PublicAthlete(LeaderboardSnapshotRow Row, PhenoStatsCalculator.Result Stats, JsonObject Source)
{
    public bool IsPro => Stats.BortzAgeReduction is double value && double.IsFinite(value);
    public double? Metric(string view) => view switch
    {
        "pheno" => Stats.AgeReduction,
        "improvement" => Stats.PhenoAgeImprovementFromWorst,
        "bortz-improvement" => Stats.BortzAgeImprovementFromWorst,
        "crowd" => Stats.CrowdAge - Stats.ChronoAge,
        _ => Stats.BortzAgeReduction ?? Stats.AgeReduction
    };
}

public sealed record PublicLeaderboardSelection(string View, LeaderboardSnapshot Snapshot, string RailTitle, bool IsDefault)
{
    public string MetricLabel => View switch { "improvement" => "Improvement", "bortz-improvement" => "Bortz improvement", _ => "Age reduction" };
}
