using System.Net;
using System.Net.Http.Json;
using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PublicCalculationApiTests
{
    [Fact]
    public async Task PhenoAgeEndpoint_CalculatesBiologicalAgeAndDifference()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/data/pheno-age", new
        {
            chronologicalAge = 45.5,
            albGL = 46,
            creatUmolL = 88.4,
            gluMmolL = 5.05,
            crpMgL = 0.8,
            wbc1000cellsuL = 6.3,
            lymPc = 25.5,
            mcvFL = 96.7,
            rdwPc = 12.5,
            alpUL = 51
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PhenoAgeApiResult>();

        Assert.NotNull(result);
        Assert.Equal(38.82088317469524, result.BiologicalAge, precision: 10);
        Assert.Equal(-6.67911682530476, result.BiologicalAgeDifference, precision: 10);
        Assert.True(result.DomainContributions.Immune > 0);
    }

    [Fact]
    public async Task BortzAgeEndpoint_CalculatesBiologicalAgeAccelerationAndDerivedCounts()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/data/bortz-age", new
        {
            chronologicalAge = 45.5,
            albGL = 46,
            alpUL = 51,
            ureaMmolL = 5.4,
            cholesterolMmolL = 4.8,
            creatUmolL = 88.4,
            cystatinCMgL = 0.85,
            hba1cMmolMol = 34,
            crpMgL = 0.8,
            ggtUL = 22,
            rbc10e12L = 4.7,
            mcvFL = 96.7,
            rdwPc = 12.5,
            wbc1000cellsuL = 6.3,
            monocytePc = 7.2,
            neutrophilPc = 58.1,
            lymPc = 25.5,
            altUL = 24,
            shbgNmolL = 45,
            vitaminDNmolL = 85,
            gluMmolL = 5.05,
            mchPg = 31.5,
            apoA1GL = 1.55
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BortzAgeApiResult>();

        Assert.NotNull(result);
        Assert.Equal(39.731263850686695, result.BiologicalAge, precision: 10);
        Assert.Equal(-5.768736149313305, result.BiologicalAgeAcceleration, precision: 10);
        Assert.Equal(0.4536, result.DerivedMonocyteCount10e9L, precision: 10);
        Assert.Equal(3.6603, result.DerivedNeutrophilCount10e9L, precision: 10);
    }

    [Fact]
    public async Task PhenoAgeEndpoint_ReturnsBadRequestForInvalidLogInput()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/data/pheno-age", new
        {
            chronologicalAge = 45.5,
            albGL = 46,
            creatUmolL = 88.4,
            gluMmolL = 5.05,
            crpMgL = 0,
            wbc1000cellsuL = 6.3,
            lymPc = 25.5,
            mcvFL = 96.7,
            rdwPc = 12.5,
            alpUL = 51
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("EnableScheduledJobs", "false");
                builder.UseSetting("EnableStartupBadgeRefresh", "false");
            });
    }

    private sealed record PhenoAgeApiResult(
        double BiologicalAge,
        double BiologicalAgeDifference,
        double PaceOfAging,
        PhenoAgeDomainContributionsApiResult DomainContributions);

    private sealed record PhenoAgeDomainContributionsApiResult(
        double Liver,
        double Kidney,
        double Metabolic,
        double Inflammation,
        double Immune);

    private sealed record BortzAgeApiResult(
        double BiologicalAge,
        double BiologicalAgeDifference,
        double PaceOfAging,
        double BiologicalAgeAcceleration,
        double DerivedMonocyteCount10e9L,
        double DerivedNeutrophilCount10e9L);
}
