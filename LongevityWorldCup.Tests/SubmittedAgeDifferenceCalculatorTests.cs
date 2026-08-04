using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SubmittedAgeDifferenceCalculatorTests
{
    [Fact]
    public void Calculate_PhenoDifferenceUsesExactAgeAndSubmittedBiomarkers()
    {
        var dateOfBirth = new DateOfBirthData { Year = 1979, Month = 9, Day = 30 };
        var biomarker = new BiomarkerData
        {
            Date = "2026-07-31",
            AlbGL = 46,
            CreatUmolL = 86.73,
            GluMmolL = 4.83,
            CrpMgL = 0.4,
            LymPc = 47,
            McvFL = 93.2,
            RdwPc = 11.4,
            AlpUL = 56,
            Wbc1000cellsuL = 4.4
        };

        var result = SubmittedAgeDifferenceCalculator.Calculate(
            dateOfBirth,
            [biomarker],
            includePheno: true,
            includeBortz: false);

        var birthDate = new DateTime(1979, 9, 30, 0, 0, 0, DateTimeKind.Utc);
        var resultDate = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);
        var exactAge = (resultDate - birthDate).TotalDays / 365.2425;
        var expected = PhenoAgeHelper.CalculatePhenoAgeFromRaw(
            exactAge,
            46,
            86.73,
            4.83,
            0.4,
            4.4,
            47,
            93.2,
            11.4,
            56) - exactAge;

        Assert.NotNull(result.PhenoDifference);
        Assert.Equal(expected, result.PhenoDifference.Value, precision: 12);
        Assert.Equal(-17.21, Math.Round(result.PhenoDifference.Value, 2));
        Assert.Null(result.BortzDifference);
    }

    [Fact]
    public void Calculate_BortzDifferenceUsesTheCanonicalBackendFeatureOrder()
    {
        var dateOfBirth = new DateOfBirthData { Year = 1966, Month = 2, Day = 2 };
        var biomarker = new BiomarkerData
        {
            Date = "2026-02-02",
            AlbGL = 45,
            AlpUL = 83,
            UreaMmolL = 5.4,
            CholesterolMmolL = 5.6,
            CreatUmolL = 72,
            CystatinCMgL = 0.9,
            Hba1cMmolMol = 35.5,
            CrpMgL = 1.35,
            GgtUL = 29,
            Rbc10e12L = 4.5,
            McvFL = 92,
            RdwPc = 13.4,
            Wbc1000cellsuL = 6.54,
            MonocytePc = 7.2,
            NeutrophilPc = 64.2,
            LymPc = 28.6,
            AltUL = 22,
            ShbgNmolL = 45.6,
            VitaminDNmolL = 50,
            GluMmolL = 5,
            MchPg = 31.8,
            ApoA1GL = 1.52
        };

        var result = SubmittedAgeDifferenceCalculator.Calculate(
            dateOfBirth,
            [biomarker],
            includePheno: true,
            includeBortz: true);

        Assert.NotNull(result.PhenoDifference);
        Assert.True(double.IsFinite(result.PhenoDifference.Value));
        Assert.NotNull(result.BortzDifference);
        Assert.True(double.IsFinite(result.BortzDifference.Value));
    }

    [Fact]
    public void FrontendChronologicalAgeCalculationKeepsFullPrecision()
    {
        var source = FrontendSourceTestHelper.ReadFrontendSource("misc.ts");

        Assert.Contains("return totalDays / 365.2425;", source);
        Assert.DoesNotContain("Math.round((totalDays / 365.2425) * 100) / 100", source);
    }
}
