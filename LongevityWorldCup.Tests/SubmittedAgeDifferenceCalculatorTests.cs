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

}
