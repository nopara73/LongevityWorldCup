using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public class PhenoStatsCalculatorTests
{
    [Fact]
    public void Compute_PhenoAgeImprovementFromWorst_UsesLatestMinusWorstEligiblePhenoAge()
    {
        var athlete = new JsonObject
        {
            ["AthleteSlug"] = "improver",
            ["Name"] = "Improver",
            ["DateOfBirth"] = new JsonObject
            {
                ["Year"] = 1980,
                ["Month"] = 1,
                ["Day"] = 1
            },
            ["Biomarkers"] = new JsonArray
            {
                Biomarkers("2024-01-01", alb: 45, creat: 80, glu: 5.0, crp: 0.8, wbc: 5.0, lym: 32, mcv: 90, rdw: 12.5, alp: 55),
                Biomarkers("2025-01-01", alb: 38, creat: 120, glu: 7.0, crp: 5.0, wbc: 8.0, lym: 18, mcv: 98, rdw: 15.0, alp: 95),
                Biomarkers("2026-01-01", alb: 44, creat: 85, glu: 5.2, crp: 1.0, wbc: 5.5, lym: 30, mcv: 91, rdw: 12.8, alp: 60)
            }
        };

        var result = PhenoStatsCalculator.Compute(athlete, new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc));

        var phenoAges = athlete["Biomarkers"]!.AsArray()
            .OfType<JsonObject>()
            .Select(entry => CalculatePhenoAge(athlete, entry))
            .ToArray();
        var expected = phenoAges[2] - phenoAges.Max();

        Assert.True(result.PhenoAgeImprovementFromWorst < 0);
        Assert.Equal(expected, result.PhenoAgeImprovementFromWorst!.Value, precision: 10);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.LowestPhenoAgeDateUtc);
    }

    [Fact]
    public void Compute_BortzAgeImprovementFromWorst_UsesLatestMinusWorstEligibleBortzAge()
    {
        var athlete = new JsonObject
        {
            ["AthleteSlug"] = "bortz-improver",
            ["Name"] = "Bortz Improver",
            ["DateOfBirth"] = new JsonObject
            {
                ["Year"] = 1980,
                ["Month"] = 1,
                ["Day"] = 1
            },
            ["Biomarkers"] = new JsonArray
            {
                BortzBiomarkers(
                    "2024-01-01",
                    alb: 45, alp: 55, urea: 5.0, cholesterol: 5.0, creat: 80, cystatin: 0.85, hba1c: 34, crp: 0.8, ggt: 25,
                    rbc: 4.8, mcv: 90, rdw: 12.5, wbc: 5.0, monocytePc: 6, neutrophilPc: 55, lym: 32, alt: 22, shbg: 45, vitaminD: 90, glu: 5.0, mch: 31, apoA1: 1.6),
                BortzBiomarkers(
                    "2025-01-01",
                    alb: 34, alp: 140, urea: 11.0, cholesterol: 8.5, creat: 135, cystatin: 1.6, hba1c: 68, crp: 12, ggt: 120,
                    rbc: 3.7, mcv: 104, rdw: 18, wbc: 10, monocytePc: 12, neutrophilPc: 78, lym: 13, alt: 70, shbg: 95, vitaminD: 20, glu: 9.0, mch: 35, apoA1: 1.0),
                BortzBiomarkers(
                    "2026-01-01",
                    alb: 47, alp: 50, urea: 4.8, cholesterol: 4.7, creat: 78, cystatin: 0.75, hba1c: 32, crp: 0.6, ggt: 20,
                    rbc: 5.0, mcv: 88, rdw: 12.1, wbc: 4.8, monocytePc: 5, neutrophilPc: 52, lym: 35, alt: 19, shbg: 38, vitaminD: 100, glu: 4.8, mch: 30, apoA1: 1.7)
            }
        };

        var result = PhenoStatsCalculator.Compute(athlete, new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc));

        var bortzAges = athlete["Biomarkers"]!.AsArray()
            .OfType<JsonObject>()
            .Select(entry => CalculateBortzAge(athlete, entry))
            .ToArray();
        var expected = bortzAges[2] - bortzAges.Max();

        Assert.True(result.BortzAgeImprovementFromWorst < 0);
        Assert.Equal(expected, result.BortzAgeImprovementFromWorst!.Value, precision: 10);
        Assert.Equal(
            athlete["Biomarkers"]!.AsArray()
                .OfType<JsonObject>()
                .Select((entry, index) => (Date: DateTime.Parse(entry["Date"]!.GetValue<string>()).Date, Age: bortzAges[index]))
                .MinBy(item => item.Age).Date,
            result.LowestBortzAgeDateUtc!.Value.Date);
    }

    [Fact]
    public void ShouldEmitBiologicalAgeImprovement_RejectsBackfilledPersonalBest()
    {
        var emit = AthleteDataService.ShouldEmitBiologicalAgeImprovement(
            canEmit: true,
            previousAge: 26.69,
            previousBestDateUtc: new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            currentAge: 22.28,
            currentBestDateUtc: new DateTime(2023, 8, 27, 0, 0, 0, DateTimeKind.Utc));

        Assert.False(emit);
    }

    [Fact]
    public void ShouldEmitBiologicalAgeImprovement_AllowsChronologicallyNewPersonalBest()
    {
        var emit = AthleteDataService.ShouldEmitBiologicalAgeImprovement(
            canEmit: true,
            previousAge: 41.8,
            previousBestDateUtc: new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            currentAge: 39.2,
            currentBestDateUtc: new DateTime(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(emit);
    }

    private static JsonObject Biomarkers(
        string date,
        double alb,
        double creat,
        double glu,
        double crp,
        double wbc,
        double lym,
        double mcv,
        double rdw,
        double alp)
    {
        return new JsonObject
        {
            ["Date"] = date,
            ["AlbGL"] = alb,
            ["CreatUmolL"] = creat,
            ["GluMmolL"] = glu,
            ["CrpMgL"] = crp,
            ["Wbc1000cellsuL"] = wbc,
            ["LymPc"] = lym,
            ["McvFL"] = mcv,
            ["RdwPc"] = rdw,
            ["AlpUL"] = alp
        };
    }

    private static JsonObject BortzBiomarkers(
        string date,
        double alb,
        double alp,
        double urea,
        double cholesterol,
        double creat,
        double cystatin,
        double hba1c,
        double crp,
        double ggt,
        double rbc,
        double mcv,
        double rdw,
        double wbc,
        double monocytePc,
        double neutrophilPc,
        double lym,
        double alt,
        double shbg,
        double vitaminD,
        double glu,
        double mch,
        double apoA1)
    {
        return new JsonObject
        {
            ["Date"] = date,
            ["AlbGL"] = alb,
            ["AlpUL"] = alp,
            ["UreaMmolL"] = urea,
            ["CholesterolMmolL"] = cholesterol,
            ["CreatUmolL"] = creat,
            ["CystatinCMgL"] = cystatin,
            ["Hba1cMmolMol"] = hba1c,
            ["CrpMgL"] = crp,
            ["GgtUL"] = ggt,
            ["Rbc10e12L"] = rbc,
            ["McvFL"] = mcv,
            ["RdwPc"] = rdw,
            ["Wbc1000cellsuL"] = wbc,
            ["MonocytePc"] = monocytePc,
            ["NeutrophilPc"] = neutrophilPc,
            ["LymPc"] = lym,
            ["AltUL"] = alt,
            ["ShbgNmolL"] = shbg,
            ["VitaminDNmolL"] = vitaminD,
            ["GluMmolL"] = glu,
            ["MchPg"] = mch,
            ["ApoA1GL"] = apoA1
        };
    }

    private static double CalculatePhenoAge(JsonObject athlete, JsonObject entry)
    {
        var dob = athlete["DateOfBirth"]!.AsObject();
        var birthDate = new DateTime(
            dob["Year"]!.GetValue<int>(),
            dob["Month"]!.GetValue<int>(),
            dob["Day"]!.GetValue<int>(),
            0,
            0,
            0,
            DateTimeKind.Utc);
        var entryDate = DateTime.Parse(entry["Date"]!.GetValue<string>()).Date;
        var ageAtEntry = (entryDate - birthDate.Date).TotalDays / 365.2425;

        return PhenoAgeHelper.CalculatePhenoAgeFromRaw(
            ageAtEntry,
            entry["AlbGL"]!.GetValue<double>(),
            entry["CreatUmolL"]!.GetValue<double>(),
            entry["GluMmolL"]!.GetValue<double>(),
            entry["CrpMgL"]!.GetValue<double>(),
            entry["Wbc1000cellsuL"]!.GetValue<double>(),
            entry["LymPc"]!.GetValue<double>(),
            entry["McvFL"]!.GetValue<double>(),
            entry["RdwPc"]!.GetValue<double>(),
            entry["AlpUL"]!.GetValue<double>());
    }

    private static double CalculateBortzAge(JsonObject athlete, JsonObject entry)
    {
        var dob = athlete["DateOfBirth"]!.AsObject();
        var birthDate = new DateTime(
            dob["Year"]!.GetValue<int>(),
            dob["Month"]!.GetValue<int>(),
            dob["Day"]!.GetValue<int>(),
            0,
            0,
            0,
            DateTimeKind.Utc);
        var entryDate = DateTime.Parse(entry["Date"]!.GetValue<string>()).Date;
        var ageAtEntry = (entryDate - birthDate.Date).TotalDays / 365.2425;
        var wbc = entry["Wbc1000cellsuL"]!.GetValue<double>();
        var values = new[]
        {
            ageAtEntry,
            entry["AlbGL"]!.GetValue<double>(),
            entry["AlpUL"]!.GetValue<double>(),
            entry["UreaMmolL"]!.GetValue<double>(),
            entry["CholesterolMmolL"]!.GetValue<double>(),
            entry["CreatUmolL"]!.GetValue<double>(),
            entry["CystatinCMgL"]!.GetValue<double>(),
            entry["Hba1cMmolMol"]!.GetValue<double>(),
            entry["CrpMgL"]!.GetValue<double>(),
            entry["GgtUL"]!.GetValue<double>(),
            entry["Rbc10e12L"]!.GetValue<double>(),
            entry["McvFL"]!.GetValue<double>(),
            entry["RdwPc"]!.GetValue<double>(),
            BortzAgeHelper.DeriveMonocyteCountFromPc(wbc, entry["MonocytePc"]!.GetValue<double>()),
            BortzAgeHelper.DeriveNeutrophilCountFromPc(wbc, entry["NeutrophilPc"]!.GetValue<double>()),
            entry["LymPc"]!.GetValue<double>(),
            entry["AltUL"]!.GetValue<double>(),
            entry["ShbgNmolL"]!.GetValue<double>(),
            entry["VitaminDNmolL"]!.GetValue<double>(),
            entry["GluMmolL"]!.GetValue<double>(),
            entry["MchPg"]!.GetValue<double>(),
            entry["ApoA1GL"]!.GetValue<double>()
        };

        return BortzAgeHelper.CalculateBortzAgeFromRaw(ageAtEntry, values);
    }
}
