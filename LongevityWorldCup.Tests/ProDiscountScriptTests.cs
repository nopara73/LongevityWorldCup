using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ProDiscountScriptTests
{
    [Fact]
    public void PerfectGuessDiscount_DoesNotRequireMarkerWriteAfterReadableExactGuess()
    {
        var script = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "LongevityWorldCup.Website",
            "Frontend",
            "pro-discounts.ts"));

        var markerStart = script.IndexOf("function hasPerfectGuessMarker()", StringComparison.Ordinal);
        var setterStart = script.IndexOf("function setPerfectGuessMarker()", markerStart, StringComparison.Ordinal);

        Assert.True(markerStart >= 0);
        Assert.True(setterStart > markerStart);

        var markerBody = script[markerStart..setterStart];

        Assert.Contains("const hasExact = Object.values(allGuesses).some(g => isRecord(g) && g.exact === true);", markerBody);
        Assert.Contains("if (hasExact) {", markerBody);
        Assert.Contains("setPerfectGuessMarker();", markerBody);
        Assert.Contains("return true;", markerBody);
        Assert.DoesNotContain("localStorage.setItem(PERFECT_GUESS_KEY, \"1\");", markerBody);
    }

    private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
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
