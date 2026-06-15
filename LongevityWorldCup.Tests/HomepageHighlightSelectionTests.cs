using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageHighlightSelectionTests
{
    [Fact]
    public void HomepageAthleteHighlightSelection_PrefersFreshEventsBeforeLifetimeImportance()
    {
        var repoRoot = FindRepoRoot();
        var eventBoardHtml = File.ReadAllText(Path.Combine(
            repoRoot,
            "LongevityWorldCup.Website",
            "wwwroot",
            "partials",
            "event-board-content.html"));

        Assert.Contains("const HOMEPAGE_ATHLETE_FRESHNESS_WINDOW_MS", eventBoardHtml);

        var compareStart = eventBoardHtml.IndexOf("function compareHomepageHighlightPreference(a,b)", StringComparison.Ordinal);
        Assert.True(compareStart >= 0, "Could not find homepage highlight preference comparator.");

        var compareEnd = eventBoardHtml.IndexOf("function selectHomepageHighlightRows", compareStart, StringComparison.Ordinal);
        Assert.True(compareEnd > compareStart, "Could not find end of homepage highlight preference comparator.");

        var compareBody = eventBoardHtml[compareStart..compareEnd];
        var freshnessCheck = compareBody.IndexOf("HOMEPAGE_ATHLETE_FRESHNESS_WINDOW_MS", StringComparison.Ordinal);
        var importanceCheck = compareBody.IndexOf("homepageHighlightImportance(a)", StringComparison.Ordinal);

        Assert.True(freshnessCheck >= 0, "Comparator must include the freshness guard.");
        Assert.True(importanceCheck >= 0, "Comparator must still include importance scoring.");
        Assert.True(
            freshnessCheck < importanceCheck,
            "Fresh homepage Events must beat stale historical Events from the same athlete before lifetime importance is compared.");
    }

    [Fact]
    public void HomepagePodcastEpisodeLinks_OpenInNewTab()
    {
        var repoRoot = FindRepoRoot();
        var eventBoardHtml = File.ReadAllText(Path.Combine(
            repoRoot,
            "LongevityWorldCup.Website",
            "wwwroot",
            "partials",
            "event-board-content.html"));

        var podcastStart = eventBoardHtml.IndexOf("badgeKey === \"podcast\"", StringComparison.Ordinal);
        Assert.True(podcastStart >= 0, "Could not find podcast highlight rendering.");

        var podcastEnd = eventBoardHtml.IndexOf("msgHtml = `<span class=\"event-inline-text\"", podcastStart, StringComparison.Ordinal);
        Assert.True(podcastEnd > podcastStart, "Could not find end of podcast episode link rendering.");

        var podcastRendering = eventBoardHtml[podcastStart..podcastEnd];
        Assert.Contains("target=\"_blank\" rel=\"noopener\">episode</a>", podcastRendering);
        Assert.DoesNotContain("target=\"_top\" rel=\"noopener\">episode</a>", podcastRendering);
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
