using System.Globalization;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed record LeaderboardOrderField(string Key, string Label, string Group, string Unit = "", int Decimals = 1, string Direction = "asc", string Kind = "number", string? SourceKey = null);

public sealed record LeaderboardOrderState(string Sort = "rank", string Direction = "asc", string? Then = null, string ThenDirection = "asc")
{
    public bool IsCustom => Sort != "rank";

    public static LeaderboardOrderState Parse(IQueryCollection query)
    {
        var primary = LeaderboardOrdering.Find(query["sort"].FirstOrDefault());
        if (primary is null || primary.Key == "rank") return new();
        var secondary = LeaderboardOrdering.Find(query["then"].FirstOrDefault());
        if (secondary?.Key == "rank" || secondary?.Key == primary.Key) secondary = null;
        return new(primary.Key, DirectionOrDefault(query["dir"].FirstOrDefault(), primary), secondary?.Key,
            secondary is null ? "asc" : DirectionOrDefault(query["thenDir"].FirstOrDefault(), secondary));
    }

    private static string DirectionOrDefault(string? direction, LeaderboardOrderField field) =>
        direction is "asc" or "desc" ? direction : field.Direction;

    public IEnumerable<LeaderboardOrderField> Columns => (IsCustom ? new[] { Sort, Then } : [])
        .Where(key => key is not null && key is not "name" and not "metric" and not "rank")
        .Select(key => LeaderboardOrdering.Find(key)!).Distinct();
}

public readonly record struct LeaderboardOrderValue(double? Number = null, string? Text = null, string? TestDate = null)
{
    public bool IsMissing => Number.HasValue ? !double.IsFinite(Number.Value) : string.IsNullOrWhiteSpace(Text);
}

/// <summary>Presentation order only. Competition ranks and score precision are assigned first.</summary>
public static class LeaderboardOrdering
{
    public static readonly IReadOnlyList<LeaderboardOrderField> Fields = [
        new("rank", "Official rank", "Ranking", Kind: "text"),
        new("age", "Chronological age", "Ages", "years"),
        new("bortz", "Bortz Age", "Ages", "years"),
        new("pheno", "Pheno Age", "Ages", "years"),
        new("crowd", "Crowd Age", "Ages", "years"),
        new("metric", "Competition score", "Age reduction", "years"),
        new("bortzReduction", "Bortz age reduction", "Age reduction", "years"),
        new("phenoReduction", "Pheno age reduction", "Age reduction", "years"),
        new("crowdReduction", "Crowd age reduction", "Age reduction", "years"),
        new("phenoPercent", "Pheno age reduction", "Age reduction", "%", Direction: "desc"),
        new("bortzPercent", "Bortz age reduction", "Age reduction", "%", Direction: "desc"),
        new("pace", "Bortz pace of aging", "Progress", "×", 2),
        new("phenoPace", "Pheno pace of aging", "Progress", "×", 2),
        new("phenoImprovement", "Pheno improvement", "Progress", "years"),
        new("bortzImprovement", "Bortz improvement", "Progress", "years"),
        new("lastTest", "Latest test date", "Activity", Direction: "desc", Kind: "date"),
        new("firstTest", "First test date", "Activity", Direction: "desc", Kind: "date"),
        new("tests", "Tests submitted", "Activity", Decimals: 0, Direction: "desc"),
        new("crowdCount", "Crowd Count", "Activity", Decimals: 0, Direction: "desc"),
        new("name", "Athlete name", "Profile", Kind: "text"),
        new("flag", "Flag", "Profile", Kind: "text"),
        new("track", "Track", "Profile", Kind: "text"),
        new("division", "Division", "Profile", Kind: "text"),
        new("badges", "Badges earned", "Profile", Decimals: 0, Direction: "desc"),
        new("albumin", "Albumin", "Biomarkers", "g/L", SourceKey: nameof(BiomarkerData.AlbGL)),
        new("alp", "ALP", "Biomarkers", "U/L", SourceKey: nameof(BiomarkerData.AlpUL)),
        new("alt", "ALT", "Biomarkers", "U/L", SourceKey: nameof(BiomarkerData.AltUL)),
        new("apoa1", "ApoA1", "Biomarkers", "g/L", 2, SourceKey: nameof(BiomarkerData.ApoA1GL)),
        new("crp", "CRP", "Biomarkers", "mg/L", 2, SourceKey: nameof(BiomarkerData.CrpMgL)),
        new("creatinine", "Creatinine", "Biomarkers", "µmol/L", SourceKey: nameof(BiomarkerData.CreatUmolL)),
        new("cystatinC", "Cystatin C", "Biomarkers", "mg/L", 2, SourceKey: nameof(BiomarkerData.CystatinCMgL)),
        new("ggt", "GGT", "Biomarkers", "U/L", SourceKey: nameof(BiomarkerData.GgtUL)),
        new("glucose", "Glucose", "Biomarkers", "mmol/L", 2, SourceKey: nameof(BiomarkerData.GluMmolL)),
        new("hba1c", "HbA1c", "Biomarkers", "mmol/mol", SourceKey: nameof(BiomarkerData.Hba1cMmolMol)),
        new("lymphocytes", "Lymphocytes", "Biomarkers", "%", SourceKey: nameof(BiomarkerData.LymPc)),
        new("mch", "MCH", "Biomarkers", "pg", SourceKey: nameof(BiomarkerData.MchPg)),
        new("mcv", "MCV", "Biomarkers", "fL", SourceKey: nameof(BiomarkerData.McvFL)),
        new("monocytes", "Monocytes", "Biomarkers", "%", SourceKey: nameof(BiomarkerData.MonocytePc)),
        new("neutrophils", "Neutrophils", "Biomarkers", "%", SourceKey: nameof(BiomarkerData.NeutrophilPc)),
        new("rbc", "RBC", "Biomarkers", "10¹²/L", 2, SourceKey: nameof(BiomarkerData.Rbc10e12L)),
        new("rdw", "RDW", "Biomarkers", "%", SourceKey: nameof(BiomarkerData.RdwPc)),
        new("shbg", "SHBG", "Biomarkers", "nmol/L", SourceKey: nameof(BiomarkerData.ShbgNmolL)),
        new("cholesterol", "Total cholesterol", "Biomarkers", "mmol/L", 2, SourceKey: nameof(BiomarkerData.CholesterolMmolL)),
        new("urea", "Urea", "Biomarkers", "mmol/L", 2, SourceKey: nameof(BiomarkerData.UreaMmolL)),
        new("vitd", "Vitamin D", "Biomarkers", "nmol/L", SourceKey: nameof(BiomarkerData.VitaminDNmolL)),
        new("wbc", "WBC", "Biomarkers", "10³ cells/µL", 2, SourceKey: nameof(BiomarkerData.Wbc1000cellsuL))
    ];

    public static LeaderboardOrderField? Find(string? key) => Fields.FirstOrDefault(field => field.Key == key);

    public static Dictionary<string, string?> Query(LeaderboardOrderState state)
    {
        var query = new Dictionary<string, string?>();
        if (!state.IsCustom) return query;
        query["sort"] = state.Sort;
        if (state.Direction != Find(state.Sort)?.Direction) query["dir"] = state.Direction;
        if (state.Then is not null)
        {
            query["then"] = state.Then;
            if (state.ThenDirection != Find(state.Then)?.Direction) query["thenDir"] = state.ThenDirection;
        }
        return query;
    }

    public static IReadOnlyDictionary<string, LeaderboardOrderValue> Values(PublicAthlete athlete)
    {
        var stats = athlete.Stats;
        var hasPheno = stats.SubmissionCount > 0 && stats.LowestPhenoAgeDateUtc.HasValue;
        var hasCrowd = stats.CrowdCount > 0;
        var values = new Dictionary<string, LeaderboardOrderValue>
        {
            ["age"] = Number(stats.ChronoAge),
            ["bortz"] = Number(stats.LowestBortzAge),
            ["pheno"] = Number(hasPheno ? stats.LowestPhenoAge : null),
            ["crowd"] = Number(hasCrowd ? stats.CrowdAge : null),
            ["bortzReduction"] = Number(stats.BortzAgeReduction),
            ["phenoReduction"] = Number(hasPheno ? stats.AgeReduction : null),
            ["crowdReduction"] = Number(hasCrowd ? stats.CrowdAge - stats.ChronoAge : null),
            ["pace"] = Number(stats.BortzPaceOfAging),
            ["phenoPace"] = Number(hasPheno ? stats.PhenoPaceOfAging : null),
            ["phenoImprovement"] = Number(stats.PhenoAgeImprovementFromWorst),
            ["bortzImprovement"] = Number(stats.BortzAgeImprovementFromWorst),
            ["phenoPercent"] = Number(hasPheno ? (1 - stats.PhenoPaceOfAging) * 100 : null),
            ["bortzPercent"] = Number((1 - stats.BortzPaceOfAging) * 100),
            ["crowdCount"] = Number(stats.CrowdCount),
            ["name"] = new(Text: athlete.Row.DisplayName),
            ["flag"] = new(Text: FlagCanonicalizer.GetCanonicalName(athlete.Row.Flag)),
            ["track"] = new(Text: athlete.IsPro ? "Pro" : "Amateur"),
            ["division"] = new(Text: athlete.Row.Division),
            ["badges"] = Number((athlete.Source["Badges"] as JsonArray)?.Count ?? 0)
        };
        var panels = (athlete.Source["Biomarkers"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        values["tests"] = Number(panels.Count);
        var dated = panels.Select((panel, index) => (Panel: panel, Index: index, Date: ExplicitDate(panel["Date"])))
            .Where(item => item.Date is not null).OrderBy(item => item.Date, StringComparer.Ordinal).ThenBy(item => item.Index).ToList();
        values["firstTest"] = new(Text: dated.FirstOrDefault().Date);
        values["lastTest"] = new(Text: dated.LastOrDefault().Date);
        foreach (var field in Fields.Where(field => field.SourceKey is not null))
        {
            values[field.Key] = default;
            foreach (var item in dated)
                if (item.Panel[field.SourceKey!] is JsonValue number && number.TryGetValue<double>(out var value) && double.IsFinite(value))
                    values[field.Key] = new(value, TestDate: item.Date);
        }
        return values;
    }

    public static string? ExplicitDate(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text) || text.Length < 10) return null;
        if (text.Length > 10 && text[10] != 'T') return null;
        return DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && (text.Length == 10 || DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    public static List<LeaderboardSnapshotRow> Apply(IEnumerable<LeaderboardSnapshotRow> rows, LeaderboardOrderState state)
    {
        var ordered = rows.ToList();
        if (state.IsCustom) ordered.Sort((a, b) =>
        {
            var result = Compare(Value(a, state.Sort), Value(b, state.Sort), state.Direction);
            if (result == 0 && state.Then is not null) result = Compare(Value(a, state.Then), Value(b, state.Then), state.ThenDirection);
            if (result == 0) result = a.Rank.CompareTo(b.Rank);
            return result == 0 ? StringComparer.Ordinal.Compare(a.Slug, b.Slug) : result;
        });
        return ordered;
    }

    public static LeaderboardOrderValue Value(LeaderboardSnapshotRow row, string key) => key switch
    {
        "rank" => Number(row.Rank), "metric" => Number(row.EffectiveAgeReductionYears),
        _ => row.OrderValues.GetValueOrDefault(key)
    };

    public static int Compare(LeaderboardOrderValue a, LeaderboardOrderValue b, string direction)
    {
        if (a.IsMissing || b.IsMissing) return a.IsMissing.CompareTo(b.IsMissing);
        var result = a.Number.HasValue && b.Number.HasValue ? a.Number.Value.CompareTo(b.Number.Value)
            : StringComparer.Ordinal.Compare(a.Text?.Trim().ToLowerInvariant(), b.Text?.Trim().ToLowerInvariant());
        return direction == "desc" ? -result : result;
    }

    public static string Format(LeaderboardOrderValue value, LeaderboardOrderField field) => value.IsMissing ? "—"
        : value.Number is double number ? PublicHtmlFormat.Fixed(number, field.Decimals) : value.Text!;

    private static LeaderboardOrderValue Number(double? value) => value.HasValue && double.IsFinite(value.Value) ? new(value) : default;
}
