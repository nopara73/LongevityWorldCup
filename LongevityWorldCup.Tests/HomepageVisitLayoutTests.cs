using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageVisitLayoutTests
{
    [Fact]
    public void HomepageVisitCounter_IsGuardedToHomepageRoutes()
    {
        var indexHtml = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "LongevityWorldCup.Website",
            "wwwroot",
            "index.html"));

        Assert.Contains("function isHomepageVisitCounterRoute()", indexHtml);
        Assert.Contains("path === '/' || path === '/index.html'", indexHtml);
        Assert.Contains("['athlete', 'filters', 'search', 'view'].some(param => params.has(param))", indexHtml);

        var layoutStart = indexHtml.IndexOf("function placeHomepageHighlightsForVisit()", StringComparison.Ordinal);
        Assert.True(layoutStart >= 0, "Could not find placeHomepageHighlightsForVisit.");

        var incrementStart = indexHtml.IndexOf("const visitCount = incrementHomepageVisitCount();", layoutStart, StringComparison.Ordinal);
        Assert.True(incrementStart >= 0, "Could not find homepage visit counter increment.");

        var guardedPrefix = indexHtml[layoutStart..incrementStart];
        Assert.Contains("if (!isHomepageVisitCounterRoute())", guardedPrefix);
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
