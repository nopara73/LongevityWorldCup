using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public class BiomarkerChartScriptTests
{
    [Fact]
    public void PartialBiomarkerRecords_AreChartedWithoutProducingBiologicalAges()
    {
        var html = ReadLeaderboardScript();

        // Dated partial records remain chartable; complete panels are required only for each age calculation.
        var chartDataStart = html.IndexOf("const chartData =", StringComparison.Ordinal);
        var chartDataEnd = html.IndexOf("const labels =", chartDataStart, StringComparison.Ordinal);
        var chartData = html[chartDataStart..chartDataEnd];
        Assert.Contains(".filter(entry => entry && Number.isFinite(biomarkerHistoryTimestamp(entry.Date))", chartData);
        Assert.Contains("key !== 'Date' && isBiomarkerHistoryValue(value)", chartData);
        Assert.DoesNotContain("isCompleteBiomarkerSet", chartData);
        Assert.DoesNotContain("isCompleteBortzBiomarkerSet", chartData);
        Assert.Contains("chartData.forEach(entry =>", html);
        Assert.Contains("if (isCompleteBiomarkerSet(entry))", html);
        Assert.Contains("phenoAges.push(phenoAge);", html);
        Assert.Contains("isCompleteBortzBiomarkerSet(entry)", html);
        Assert.DoesNotContain("const completeData = biomarkerData.filter(isCompleteBiomarkerSet);", html);
    }

    private static string ReadLeaderboardScript()
    {
        var repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(
            repoRoot,
            "LongevityWorldCup.Website",
            "Frontend",
            "leaderboard-page.js"));
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var startDirectory = Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory;
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LongevityWorldCup.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from {startDirectory}.");
    }
}
