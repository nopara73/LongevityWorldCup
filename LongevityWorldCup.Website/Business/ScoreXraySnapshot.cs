using System.Globalization;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

/// <summary>A read-only explanation of a coherent public leaderboard snapshot.</summary>
public sealed class ScoreXraySnapshot
{
    private readonly PublicLeaderboardSnapshot _leaderboard;
    private readonly DateTime _asOf;
    public ScoreXraySnapshot(JsonArray athletes, DateTime asOf)
    {
        _asOf = asOf;
        _leaderboard = new(athletes, asOf);
    }

    public XrayDirectory Directory() => new(_asOf, _leaderboard.Athletes.Select(Summary).ToList());

    private static XrayAthlete Summary(PublicAthlete athlete) => new(
        athlete.Row.Slug,
        athlete.Row.DisplayName,
        athlete.Row.LeaderboardThumbnailUrl,
        athlete.Row.Rank, athlete.IsPro);

    public XrayDocument? Find(string slug)
    {
        var athlete = _leaderboard.Athletes.FirstOrDefault(a => a.Row.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
        if (athlete is null) return null;
        var panels = athlete.Source["Biomarkers"] is JsonArray entries ? entries.OfType<JsonObject>().ToList() : [];
        var history = panels.Select((panel, index) =>
        {
            var single = new JsonObject
            {
                ["Name"] = athlete.Stats.Name, ["AthleteSlug"] = athlete.Row.Slug,
                ["DateOfBirth"] = athlete.Source["DateOfBirth"]?.DeepClone(),
                ["Biomarkers"] = new JsonArray(panel.DeepClone())
            };
            var result = PhenoStatsCalculator.Compute(single, _asOf);
            return new XrayPanel(index + 1, TestDate(panel),
                result.LowestPhenoPanelIndex.HasValue ? result.LowestPhenoAge : null,
                result.LowestBortzPanelIndex.HasValue ? result.LowestBortzAge : null,
                index == athlete.Stats.LowestPhenoPanelIndex, index == athlete.Stats.LowestBortzPanelIndex);
        }).ToList();
        var proofs = athlete.Source["Proofs"] is JsonArray evidence
            ? evidence.Select(p => p?.GetValue<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>().ToList() : [];
        return new(_asOf, Summary(athlete), history, proofs,
            Clock(athlete, panels, history, false), Clock(athlete, panels, history, true));
    }

    private XrayClock? Clock(PublicAthlete athlete, List<JsonObject> panels, IReadOnlyList<XrayPanel> history, bool bortz)
    {
        var stats = athlete.Stats;
        var index = bortz ? stats.LowestBortzPanelIndex : stats.LowestPhenoPanelIndex;
        var values = bortz ? stats.BestBortzValues : stats.LowestPhenoValues;
        if (index is not int selected || selected >= panels.Count || values is null) return null;
        var panel = panels[selected];
        var age = (bortz ? stats.LowestBortzAge : stats.LowestPhenoAge)!.Value;
        var difference = (bortz ? stats.BortzAgeReduction : stats.AgeReduction)!.Value;
        var order = _leaderboard.Order(bortz ? "bortz" : "pheno");
        var rank = order.Select((a, i) => (a.Row.Slug, Rank: i + 1)).First(a => a.Slug == athlete.Row.Slug).Rank;
        var neighborStart = Math.Max(0, rank - 3);
        var terms = bortz ? BortzTerms(panel, values) : PhenoTerms(panel, values);
        var calculations = new List<XrayCalculation>();
        if (bortz)
        {
            var acceleration = BortzAgeHelper.CalculateBAA(values);
            calculations.Add(new("Age acceleration", "10 × Σ ((model value − reference mean) × coefficient)", Finite(acceleration), "years"));
            calculations.Add(new("Before the age floor", "age at test + age acceleration", Finite(values[0] + acceleration), "years"));
            calculations.Add(new("Bortz age", "max(0, age at test + age acceleration)", age, "years"));
        }
        else
        {
            var trace = PhenoAgeHelper.Trace(values);
            calculations.Add(new("Weighted sum", "Σ (model value × coefficient)", Finite(trace.WeightedSum), ""));
            calculations.Add(new("Linear predictor", "weighted sum − 19.9067", Finite(trace.LinearPredictor), ""));
            calculations.Add(new("Model mortality score", "1 − exp(−exp(predictor) × (exp(0.0076927 × 120) − 1) / 0.0076927)", Finite(trace.MortalityScore), ""));
            calculations.Add(new("Before the age floor", "141.50225 + ln(−0.00553 × ln(1 − mortality score)) / 0.090165", Finite(trace.UnflooredAge), "years"));
            calculations.Add(new("Pheno age", "max(0, model age)", age, "years"));
        }
        var keys = bortz ? BortzKeys.Skip(1).Concat(["Wbc1000cellsuL"]).Distinct() : PhenoKeys.Skip(1);
        var lab = keys.Select(key => new XrayLabValue(key, Label(key), Units(key), Number(panel, key))).ToList();
        return new(bortz ? "bortz" : "pheno", selected + 1, TestDate(panel), values[0], age, difference,
            rank, order.Count, history.Count(p => p.PhenoAge.HasValue), history.Count(p => p.BortzAge.HasValue),
            bortz || !athlete.IsPro, lab, terms, calculations,
            order.Skip(neighborStart).Take(5).Select((a, i) => new XrayNeighbor(
                a.Row.Slug, Summary(a).Name, neighborStart + i + 1,
                a.Metric(bortz ? "bortz" : "pheno")!.Value)).ToList());
    }

    private static readonly string[] PhenoKeys = ["age", "AlbGL", "CreatUmolL", "GluMmolL", "CrpMgL", "Wbc1000cellsuL", "LymPc", "McvFL", "RdwPc", "AlpUL"];
    private static readonly string[] BortzKeys = ["age", "AlbGL", "AlpUL", "UreaMmolL", "CholesterolMmolL", "CreatUmolL", "CystatinCMgL", "Hba1cMmolMol", "CrpMgL", "GgtUL", "Rbc10e12L", "McvFL", "RdwPc", "MonocytePc", "NeutrophilPc", "LymPc", "AltUL", "ShbgNmolL", "VitaminDNmolL", "GluMmolL", "MchPg", "ApoA1GL"];

    private static List<XrayTerm> PhenoTerms(JsonObject panel, double[] values) => PhenoAgeHelper.Biomarkers.Select((feature, i) =>
    {
        var key = PhenoKeys[i];
        var reported = i == 0 ? values[0] : Number(panel, key);
        var input = key == "CrpMgL" ? reported / 10 : reported;
        var scored = PhenoAgeHelper.ApplyCap(input, feature);
        var model = key == "CrpMgL" ? values[i] : scored;
        return new XrayTerm(key, Label(key), Units(key), key == "CrpMgL" ? "mg/dL" : Units(key),
            reported, input, scored, model, feature.Coeff, null, model * feature.Coeff,
            feature.Cap, feature.Mode.ToString().ToLowerInvariant(),
            key == "CrpMgL" ? "mg/L ÷ 10 → mg/dL" : null, key == "CrpMgL" ? "ln" : null);
    }).ToList();

    private static List<XrayTerm> BortzTerms(JsonObject panel, double[] values) => BortzAgeHelper.Features.Select((feature, i) =>
    {
        var key = BortzKeys[i];
        var reported = i == 0 ? values[0] : Number(panel, key);
        var scored = BortzAgeHelper.ApplyCap(values[i], feature);
        var count = key is "MonocytePc" or "NeutrophilPc";
        return new XrayTerm(key, Label(key), Units(key), count ? "10⁹/L" : Units(key), reported, values[i], scored,
            feature.IsLog ? Math.Log(scored) : scored, feature.BaaCoeff, feature.Mean,
            BortzAgeHelper.CalculateFeatureContribution(values[i], feature) * 10,
            feature.Cap, feature.CapMode.ToString().ToLowerInvariant(),
            count ? $"WBC {Number(panel, "Wbc1000cellsuL").ToString("G", CultureInfo.InvariantCulture)} × {reported.ToString("G", CultureInfo.InvariantCulture)}% ÷ 100 → 10⁹/L" : null,
            feature.IsLog ? "ln" : null);
    }).ToList();

    private static double? Finite(double value) => double.IsFinite(value) ? value : null;
    private static string? Text(JsonObject item, string key) => item[key]?.GetValue<string>();
    private static double Number(JsonObject item, string key) => item[key]!.GetValue<double>();
    private static string? TestDate(JsonObject panel) => DateTime.TryParse(Text(panel, "Date"), null, DateTimeStyles.RoundtripKind, out var date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    private static string Label(string key) => key switch
    {
        "age" => "Age at test", "AlbGL" => "Albumin", "CreatUmolL" => "Creatinine", "GluMmolL" => "Glucose",
        "CrpMgL" => "C-reactive protein", "Wbc1000cellsuL" => "White blood cells", "LymPc" => "Lymphocytes",
        "McvFL" => "Mean corpuscular volume", "RdwPc" => "Red cell distribution width", "AlpUL" => "Alkaline phosphatase",
        "UreaMmolL" => "Urea", "CholesterolMmolL" => "Total cholesterol", "CystatinCMgL" => "Cystatin C",
        "Hba1cMmolMol" => "HbA1c", "GgtUL" => "Gamma-glutamyl transferase", "Rbc10e12L" => "Red blood cells",
        "MonocytePc" => "Monocytes", "NeutrophilPc" => "Neutrophils", "AltUL" => "Alanine aminotransferase",
        "ShbgNmolL" => "Sex hormone-binding globulin", "VitaminDNmolL" => "Vitamin D", "MchPg" => "Mean corpuscular hemoglobin",
        "ApoA1GL" => "Apolipoprotein A1", _ => key
    };
    private static string Units(string key) => key switch
    {
        "age" => "years", "AlbGL" or "ApoA1GL" => "g/L", "CreatUmolL" => "µmol/L",
        "GluMmolL" or "UreaMmolL" or "CholesterolMmolL" => "mmol/L", "CrpMgL" or "CystatinCMgL" => "mg/L",
        "Wbc1000cellsuL" => "10⁹/L", "LymPc" or "RdwPc" or "MonocytePc" or "NeutrophilPc" => "%",
        "McvFL" => "fL", "AlpUL" or "GgtUL" or "AltUL" => "U/L", "Hba1cMmolMol" => "mmol/mol",
        "Rbc10e12L" => "10¹²/L", "ShbgNmolL" or "VitaminDNmolL" => "nmol/L", "MchPg" => "pg", _ => ""
    };
}

public sealed record XrayDirectory(DateTime AsOfUtc, IReadOnlyList<XrayAthlete> Athletes);
public sealed record XrayAthlete(string Slug, string Name, string? Portrait, int UltimateRank, bool IsPro);
public sealed record XrayDocument(DateTime AsOfUtc, XrayAthlete Athlete, IReadOnlyList<XrayPanel> Panels, IReadOnlyList<string> Proofs, XrayClock? Pheno, XrayClock? Bortz);
public sealed record XrayPanel(int Number, string? Date, double? PhenoAge, double? BortzAge, bool SelectedPheno, bool SelectedBortz);
public sealed record XrayLabValue(string Key, string Name, string Unit, double Value);
public sealed record XrayTerm(string Key, string Name, string Unit, string InputUnit, double Reported, double Input, double Scored, double Model,
    double Coefficient, double? Mean, double Contribution, double? Cap, string CapMode, string? Conversion, string? Transform);
public sealed record XrayCalculation(string Name, string Formula, double? Value, string Unit);
public sealed record XrayNeighbor(string Slug, string Name, int Rank, double AgeDifference);
public sealed record XrayClock(string Id, int PanelNumber, string? Date, double AgeAtTest, double Age, double AgeDifference,
    int Rank, int FieldSize, int PhenoPanelCount, int BortzPanelCount, bool SuppliesUltimateScore,
    IReadOnlyList<XrayLabValue> Lab, IReadOnlyList<XrayTerm> Terms, IReadOnlyList<XrayCalculation> Calculation, IReadOnlyList<XrayNeighbor> Neighbors);
