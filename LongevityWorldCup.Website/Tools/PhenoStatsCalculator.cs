using System.Globalization;
using System.Text.Json.Nodes;

namespace LongevityWorldCup.Website.Tools;

public static class PhenoStatsCalculator
{
    public sealed class Result
    {
        public required string Slug { get; init; }
        public required string Name { get; init; }
        public DateTime? DobUtc { get; init; }
        public double? ChronoAge { get; init; }
        public double? LowestPhenoAge { get; init; }
        public double? LowestBortzAge { get; init; }
        public double? AgeReduction { get; init; }
        public double? BortzAgeReduction { get; init; }
        public int SubmissionCount { get; init; }
        public int BortzSubmissionCount { get; init; }
        public string? Division { get; init; }
        public string? Generation { get; init; }
        public string? Exclusive { get; init; }
        public double[]? BestMarkerValues { get; init; }
        public double? PhenoAgeDiffFromBaseline { get; init; }
        public double? BortzAgeDiffFromBaseline { get; init; }
        public double? CrowdAge { get; init; }
        public int CrowdCount { get; init; }
        public double[]? BestBortzValues { get; init; }
    }

    public static Dictionary<string, Result> BuildAll(JsonArray athletes, DateTime asOf)
    {
        var dict = new Dictionary<string, Result>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in athletes.OfType<JsonObject>())
        {
            var slug = node["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;
            dict[slug] = Compute(node, asOf);
        }
        return dict;
    }

    public static Result Compute(JsonObject o, DateTime asOf)
    {
        var slug = o["AthleteSlug"]?.GetValue<string>() ?? "";
        var name = o["Name"]?.GetValue<string>() ?? slug;

        DateTime? dob = null;
        if (o["DateOfBirth"] is JsonObject dobNode)
        {
            try
            {
                int y = dobNode["Year"]!.GetValue<int>();
                int m = dobNode["Month"]!.GetValue<int>();
                int d = dobNode["Day"]!.GetValue<int>();
                dob = new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc);
            }
            catch { }
        }

        double? chrono = null;
        if (dob.HasValue)
            chrono = (asOf.Date - dob.Value.Date).TotalDays / 365.2425;

        int submissionCount = 0;
        int bortzSubmissionCount = 0;
        double lowestPheno = double.PositiveInfinity;
        double chronoAtLowest = double.NaN;
        double lowestBortz = double.PositiveInfinity;
        double chronoAtLowestBortz = double.NaN;
        double? firstPheno = null;
        double? lastPheno = null;
        double? firstBortz = null;
        double? lastBortz = null;

        double? bestAlb = null, bestCreat = null, bestGlu = null, bestCrp = null, bestWbc = null, bestLym = null, bestMcv = null, bestRdw = null, bestAlp = null;
        double[]? bestBortzValues = null;

        if (o["Biomarkers"] is JsonArray biomArr)
        {
            foreach (var entry in biomArr.OfType<JsonObject>())
            {
                var entryDate = asOf;
                var ds = entry["Date"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(ds) && DateTime.TryParse(ds, null, DateTimeStyles.RoundtripKind, out var parsed))
                    entryDate = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);

                double AgeAt(DateTime d) => dob.HasValue ? (d.Date - dob.Value.Date).TotalDays / 365.2425 : double.NaN;

                var ageAtEntry = AgeAt(entryDate);

                if (TryBuildBortzInput(entry, ageAtEntry, out var bortzInput))
                {
                    var bortzAge = BortzAgeHelper.CalculateBortzAgeFromRaw(ageAtEntry, bortzInput);
                    if (!double.IsNaN(bortzAge) && !double.IsInfinity(bortzAge))
                    {
                        bortzSubmissionCount++;
                        if (!firstBortz.HasValue) firstBortz = bortzAge;
                        lastBortz = bortzAge;
                        if (bortzAge < lowestBortz)
                        {
                            lowestBortz = bortzAge;
                            chronoAtLowestBortz = ageAtEntry;
                            bestBortzValues = bortzInput;
                        }
                    }
                }

                if (TryGetDouble(entry, "AlbGL", out var alb) &&
                    TryGetDouble(entry, "CreatUmolL", out var creat) &&
                    TryGetDouble(entry, "GluMmolL", out var glu) &&
                    TryGetDouble(entry, "CrpMgL", out var crpMgL) &&
                    TryGetDouble(entry, "Wbc1000cellsuL", out var wbc) &&
                    TryGetDouble(entry, "LymPc", out var lym) &&
                    TryGetDouble(entry, "McvFL", out var mcv) &&
                    TryGetDouble(entry, "RdwPc", out var rdw) &&
                    TryGetDouble(entry, "AlpUL", out var alp) &&
                    crpMgL > 0 && !double.IsNaN(ageAtEntry))
                {
                    submissionCount++;

                    var ph = PhenoAgeHelper.CalculatePhenoAgeFromRaw(
                        ageAtEntry, alb, creat, glu, crpMgL, wbc, lym, mcv, rdw, alp);

                    if (!double.IsNaN(ph) && !double.IsInfinity(ph))
                    {
                        if (!firstPheno.HasValue) firstPheno = ph;
                        lastPheno = ph;
                        if (ph < lowestPheno)
                        {
                            lowestPheno = ph;
                            chronoAtLowest = ageAtEntry;
                        }

                        if (!bestAlb.HasValue || alb > bestAlb.Value) bestAlb = alb;
                        if (!bestLym.HasValue || lym > bestLym.Value) bestLym = lym;

                        if (!bestCreat.HasValue || creat < bestCreat.Value) bestCreat = creat;
                        if (!bestGlu.HasValue || glu < bestGlu.Value) bestGlu = glu;
                        if (!bestCrp.HasValue || crpMgL < bestCrp.Value) bestCrp = crpMgL;
                        if (!bestWbc.HasValue || wbc < bestWbc.Value) bestWbc = wbc;
                        if (!bestMcv.HasValue || mcv < bestMcv.Value) bestMcv = mcv;
                        if (!bestRdw.HasValue || rdw < bestRdw.Value) bestRdw = rdw;
                        if (!bestAlp.HasValue || alp < bestAlp.Value) bestAlp = alp;
                    }
                }
            }
        }

        if (double.IsNaN(lowestPheno) || double.IsInfinity(lowestPheno))
            lowestPheno = chrono ?? double.PositiveInfinity;

        if (double.IsNaN(chronoAtLowest))
            chronoAtLowest = chrono ?? double.NaN;

        if (double.IsNaN(lowestBortz) || double.IsInfinity(lowestBortz))
            lowestBortz = double.PositiveInfinity;

        if (double.IsNaN(chronoAtLowestBortz))
            chronoAtLowestBortz = chrono ?? double.NaN;

        double? ageReduction = null;
        if (!double.IsInfinity(lowestPheno) && !double.IsNaN(chronoAtLowest))
            ageReduction = lowestPheno - chronoAtLowest;
        double? bortzAgeReduction = null;
        if (!double.IsInfinity(lowestBortz) && !double.IsNaN(chronoAtLowestBortz))
            bortzAgeReduction = lowestBortz - chronoAtLowestBortz;

        double[]? bestMarkerValues = null;
        if (bestAlb.HasValue && bestCreat.HasValue && bestGlu.HasValue && bestCrp.HasValue &&
            bestWbc.HasValue && bestLym.HasValue && bestMcv.HasValue && bestRdw.HasValue && bestAlp.HasValue &&
            bestCrp.Value > 0 && chrono.HasValue)
        {
            var lnCrpOver10 = Math.Log(bestCrp.Value / 10.0);
            bestMarkerValues = new[]
            {
                chrono.Value,
                bestAlb.Value,
                bestCreat.Value,
                bestGlu.Value,
                lnCrpOver10,
                bestWbc.Value,
                bestLym.Value,
                bestMcv.Value,
                bestRdw.Value,
                bestAlp.Value
            };
        }

        double? phenoDiffFromBaseline = null;
        if (firstPheno.HasValue && lastPheno.HasValue)
            phenoDiffFromBaseline = lastPheno.Value - firstPheno.Value;
        double? bortzDiffFromBaseline = null;
        if (firstBortz.HasValue && lastBortz.HasValue)
            bortzDiffFromBaseline = lastBortz.Value - firstBortz.Value;

        double? crowdAge = null;
        int crowdCount = 0;
        try
        {
            if (o["CrowdAge"] is JsonValue jvAge && jvAge.TryGetValue<double>(out var ca)) crowdAge = ca;
            if (o["CrowdCount"] is JsonValue jvCnt && jvCnt.TryGetValue<int>(out var cc)) crowdCount = cc;
        }
        catch { }

        var division = TryGetString(o, "Division");
        var generation = TryGetString(o, "Generation");
        var exclusive = TryGetString(o, "ExclusiveLeague");
        if (string.IsNullOrWhiteSpace(generation) && dob.HasValue)
            generation = GetGenerationFromBirthYear(dob.Value.Year);

        return new Result
        {
            Slug = slug,
            Name = name,
            DobUtc = dob,
            ChronoAge = chrono,
            LowestPhenoAge = double.IsInfinity(lowestPheno) ? null : lowestPheno,
            LowestBortzAge = double.IsInfinity(lowestBortz) ? null : lowestBortz,
            AgeReduction = ageReduction,
            BortzAgeReduction = bortzAgeReduction,
            SubmissionCount = submissionCount,
            BortzSubmissionCount = bortzSubmissionCount,
            Division = division,
            Generation = generation,
            Exclusive = exclusive,
            BestMarkerValues = bestMarkerValues,
            PhenoAgeDiffFromBaseline = phenoDiffFromBaseline,
            BortzAgeDiffFromBaseline = bortzDiffFromBaseline,
            CrowdAge = crowdAge,
            CrowdCount = crowdCount,
            BestBortzValues = bestBortzValues
        };
    }

    private static bool TryBuildBortzInput(JsonObject entry, double ageAtEntry, out double[] values)
    {
        values = Array.Empty<double>();
        if (!double.IsFinite(ageAtEntry))
            return false;

        if (!TryGetDouble(entry, "AlbGL", out var alb) ||
            !TryGetDouble(entry, "AlpUL", out var alp) ||
            !TryGetDouble(entry, "UreaMmolL", out var urea) ||
            !TryGetDouble(entry, "CholesterolMmolL", out var cholesterol) ||
            !TryGetDouble(entry, "CreatUmolL", out var creat) ||
            !TryGetDouble(entry, "CystatinCMgL", out var cystatin) ||
            !TryGetDouble(entry, "Hba1cMmolMol", out var hba1c) ||
            !TryGetDouble(entry, "CrpMgL", out var crp) ||
            !TryGetDouble(entry, "GgtUL", out var ggt) ||
            !TryGetDouble(entry, "Rbc10e12L", out var rbc) ||
            !TryGetDouble(entry, "McvFL", out var mcv) ||
            !TryGetDouble(entry, "RdwPc", out var rdw) ||
            !TryGetDouble(entry, "Wbc1000cellsuL", out var wbc) ||
            !TryGetDouble(entry, "MonocytePc", out var monocytePc) ||
            !TryGetDouble(entry, "NeutrophilPc", out var neutrophilPc) ||
            !TryGetDouble(entry, "LymPc", out var lymPc) ||
            !TryGetDouble(entry, "AltUL", out var alt) ||
            !TryGetDouble(entry, "ShbgNmolL", out var shbg) ||
            !TryGetDouble(entry, "VitaminDNmolL", out var vitaminD) ||
            !TryGetDouble(entry, "GluMmolL", out var glucose) ||
            !TryGetDouble(entry, "MchPg", out var mch) ||
            !TryGetDouble(entry, "ApoA1GL", out var apoa1))
        {
            return false;
        }

        values = new[]
        {
            ageAtEntry,
            alb,
            alp,
            urea,
            cholesterol,
            creat,
            cystatin,
            hba1c,
            crp,
            ggt,
            rbc,
            mcv,
            rdw,
            BortzAgeHelper.DeriveMonocyteCountFromPc(wbc, monocytePc),
            BortzAgeHelper.DeriveNeutrophilCountFromPc(wbc, neutrophilPc),
            lymPc,
            alt,
            shbg,
            vitaminD,
            glucose,
            mch,
            apoa1
        };

        return values.All(v => double.IsFinite(v));
    }

    private static bool TryGetDouble(JsonObject o, string key, out double v)
    {
        v = 0;
        try
        {
            var n = o[key];
            if (n is null) return false;
            v = n.GetValue<double>();
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetString(JsonObject o, string key)
    {
        try
        {
            var n = o[key];
            if (n is null) return null;
            var s = n.GetValue<string?>();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetGenerationFromBirthYear(int birthYear)
    {
        if (birthYear >= 1928 && birthYear <= 1945) return "Silent Generation";
        if (birthYear >= 1946 && birthYear <= 1964) return "Baby Boomers";
        if (birthYear >= 1965 && birthYear <= 1980) return "Gen X";
        if (birthYear >= 1981 && birthYear <= 1996) return "Millennials";
        if (birthYear >= 1997 && birthYear <= 2012) return "Gen Z";
        if (birthYear >= 2013) return "Gen Alpha";
        return null;
    }
}
