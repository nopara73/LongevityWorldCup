using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LeaderboardInteractionAccessibilityTests
{
    [Fact]
    public void AthleteNames_AreNamedKeyboardControlsWithVisibleFocus()
    {
        var html = File.ReadAllText(GetLeaderboardPartialPath());

        Assert.Contains(".athlete-name:focus-visible", html);
        Assert.Contains("athleteNameElement.setAttribute('role', 'button');", html);
        Assert.Contains("athleteNameElement.setAttribute('tabindex', '0');", html);
        Assert.Contains("athleteNameElement.setAttribute('aria-label'", html);
        Assert.Contains("athleteNameElement.addEventListener('keydown', handleAthleteNameKeydown);", html);
        Assert.Contains("if (event.key !== 'Enter' && event.key !== ' ') return;", html);
        Assert.Contains("event.stopPropagation();", html);
    }

    private static string GetLeaderboardPartialPath([CallerFilePath] string sourceFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Could not locate the tests directory.");
        return Path.GetFullPath(Path.Combine(
            testsDirectory,
            "..",
            "LongevityWorldCup.Website",
            "wwwroot",
            "partials",
            "leaderboard-content.html"));
    }
}
