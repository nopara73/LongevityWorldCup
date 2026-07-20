using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public class BiomarkerChartScriptTests
{
    [Fact]
    public void PartialBiomarkerRecords_AreChartedWithoutProducingBiologicalAges()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("const chartData = biomarkerData.filter(entry => entry && entry.Date", html);
        Assert.Contains("chartData.forEach(entry =>", html);
        Assert.Contains("const chronoAgeAtEntry = getProfileAgeAtBiomarker(fullAthleteData, entry);", html);
        Assert.Contains("if (Number.isFinite(chronoAgeAtEntry) && isCompleteBiomarkerSet(entry))", html);
        Assert.Contains("phenoAges.push(phenoAge);", html);
        Assert.Contains("isCompleteBortzBiomarkerSet(entry)", html);
        Assert.DoesNotContain("const completeData = biomarkerData.filter(isCompleteBiomarkerSet);", html);
    }

    private static string ReadLeaderboardPartial()
    {
        var repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(
            repoRoot,
            "LongevityWorldCup.Website",
            "wwwroot",
            "partials",
            "leaderboard-content.html"));
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
