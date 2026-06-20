using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BioageStoredBiomarkerTests
{
    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void BioagePages_ReadStoredBiomarkersDefensively(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("function readStoredBiomarkerData()", html);
        Assert.Contains("return JSON.parse(sessionStorage.getItem('biomarkerData'));", html);
        Assert.Contains("} catch (_) {", html);
        Assert.Contains("return null;", html);
        Assert.Contains("const stored = readStoredBiomarkerData();", html);
        Assert.DoesNotContain("const stored = JSON.parse(sessionStorage.getItem('biomarkerData'));", html);
    }

    [Fact]
    public void PhenoPage_GuardsStorageHandoffBeforeRedirecting()
    {
        var html = File.ReadAllText(GetPagePath("pheno-age.html"));

        Assert.Contains("function storePhenoResultForNextStep(biomarkerData, chronoPhenoDifference)", html);
        Assert.Contains("sessionStorage.setItem('chronoPhenoDifference', chronoPhenoDifference.toFixed(2));", html);
        Assert.Contains("sessionStorage.setItem('biomarkerData', JSON.stringify(biomarkerData));", html);
        Assert.Contains("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("if (!storePhenoResultForNextStep(biomarkerData, chronoPhenoDifference)) return;", html);
    }

    [Fact]
    public void BortzPage_GuardsStorageHandoffBeforeRedirecting()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html"));

        Assert.Contains("function storeBortzResultForNextStep(biomarkerData, chronoBortzDifference, chronoPhenoDifference)", html);
        Assert.Contains("sessionStorage.setItem('chronoBortzDifference', chronoBortzDifference.toFixed(2));", html);
        Assert.Contains("sessionStorage.setItem('chronoPhenoDifference', chronoPhenoDifference.toFixed(2));", html);
        Assert.Contains("sessionStorage.setItem('biomarkerData', JSON.stringify(biomarkerData));", html);
        Assert.Contains("sessionStorage.setItem(PENDING_PAYMENT_OFFER_KEY, JSON.stringify(", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("if (!storeBortzResultForNextStep(biomarkerData, chronoBortzDifference, chronoPhenoDifference)) return;", html);
    }

    private static string GetPagePath(string fileName)
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "onboarding", fileName);
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var startDirectory = Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory;
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LongevityWorldCup.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from {startDirectory}.");
    }
}
