using LongevityWorldCup.Website.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PublicCalculationApiTests
{
    [Fact]
    public void PhenoAgeEndpoint_CalculatesBiologicalAgeAndDifference()
    {
        var controller = CreateController();

        var response = controller.CalculatePhenoAge(new PhenoAgeCalculationRequest
        {
            ChronologicalAge = 45.5,
            AlbGL = 46,
            CreatUmolL = 88.4,
            GluMmolL = 5.05,
            CrpMgL = 0.8,
            Wbc1000cellsuL = 6.3,
            LymPc = 25.5,
            McvFL = 96.7,
            RdwPc = 12.5,
            AlpUL = 51
        });

        var ok = Assert.IsType<OkObjectResult>(response);
        var result = Assert.IsType<PhenoAgeCalculationResult>(ok.Value);

        Assert.Equal(38.82088317469524, result.BiologicalAge, precision: 10);
        Assert.Equal(-6.67911682530476, result.BiologicalAgeDifference, precision: 10);
        Assert.True(result.DomainContributions.Immune > 0);
    }

    [Fact]
    public void BortzAgeEndpoint_CalculatesBiologicalAgeAccelerationAndDerivedCounts()
    {
        var controller = CreateController();

        var response = controller.CalculateBortzAge(new BortzAgeCalculationRequest
        {
            ChronologicalAge = 45.5,
            AlbGL = 46,
            AlpUL = 51,
            UreaMmolL = 5.4,
            CholesterolMmolL = 4.8,
            CreatUmolL = 88.4,
            CystatinCMgL = 0.85,
            Hba1cMmolMol = 34,
            CrpMgL = 0.8,
            GgtUL = 22,
            Rbc10e12L = 4.7,
            McvFL = 96.7,
            RdwPc = 12.5,
            Wbc1000cellsuL = 6.3,
            MonocytePc = 7.2,
            NeutrophilPc = 58.1,
            LymPc = 25.5,
            AltUL = 24,
            ShbgNmolL = 45,
            VitaminDNmolL = 85,
            GluMmolL = 5.05,
            MchPg = 31.5,
            ApoA1GL = 1.55
        });

        var ok = Assert.IsType<OkObjectResult>(response);
        var result = Assert.IsType<BortzAgeCalculationResult>(ok.Value);

        Assert.Equal(39.731263850686695, result.BiologicalAge, precision: 10);
        Assert.Equal(-5.768736149313305, result.BiologicalAgeAcceleration, precision: 10);
        Assert.Equal(0.4536, result.DerivedMonocyteCount10e9L, precision: 10);
        Assert.Equal(3.6603, result.DerivedNeutrophilCount10e9L, precision: 10);
    }

    [Fact]
    public void PhenoAgeEndpoint_ReturnsBadRequestForInvalidLogInput()
    {
        var controller = CreateController();

        var response = controller.CalculatePhenoAge(new PhenoAgeCalculationRequest
        {
            ChronologicalAge = 45.5,
            AlbGL = 46,
            CreatUmolL = 88.4,
            GluMmolL = 5.05,
            CrpMgL = 0,
            Wbc1000cellsuL = 6.3,
            LymPc = 25.5,
            McvFL = 96.7,
            RdwPc = 12.5,
            AlpUL = 51
        });

        Assert.IsType<BadRequestObjectResult>(response);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void PhenoAgeEndpoint_ReturnsBadRequestForNonFiniteInput(double invalidValue)
    {
        var controller = CreateController();

        var response = controller.CalculatePhenoAge(new PhenoAgeCalculationRequest
        {
            ChronologicalAge = 45.5,
            AlbGL = 46,
            CreatUmolL = invalidValue,
            GluMmolL = 5.05,
            CrpMgL = 0.8,
            Wbc1000cellsuL = 6.3,
            LymPc = 25.5,
            McvFL = 96.7,
            RdwPc = 12.5,
            AlpUL = 51
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal("All Pheno Age inputs must be finite numbers.", GetMessage(badRequest.Value));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void BortzAgeEndpoint_ReturnsBadRequestForNonFiniteInput(double invalidValue)
    {
        var controller = CreateController();

        var response = controller.CalculateBortzAge(new BortzAgeCalculationRequest
        {
            ChronologicalAge = 45.5,
            AlbGL = 46,
            AlpUL = 51,
            UreaMmolL = 5.4,
            CholesterolMmolL = 4.8,
            CreatUmolL = 88.4,
            CystatinCMgL = 0.85,
            Hba1cMmolMol = 34,
            CrpMgL = 0.8,
            GgtUL = 22,
            Rbc10e12L = 4.7,
            McvFL = 96.7,
            RdwPc = 12.5,
            Wbc1000cellsuL = 6.3,
            MonocytePc = 7.2,
            NeutrophilPc = 58.1,
            LymPc = 25.5,
            AltUL = 24,
            ShbgNmolL = invalidValue,
            VitaminDNmolL = 85,
            GluMmolL = 5.05,
            MchPg = 31.5,
            ApoA1GL = 1.55
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal("All Bortz Age inputs must be finite numbers.", GetMessage(badRequest.Value));
    }

    private static DataController CreateController()
    {
        return new DataController(null!);
    }

    private static string? GetMessage(object? value)
    {
        Assert.NotNull(value);
        return value!.GetType().GetProperty("message")?.GetValue(value) as string;
    }
}
