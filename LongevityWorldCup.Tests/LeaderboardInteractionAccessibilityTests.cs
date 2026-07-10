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

    [Fact]
    public void ImageViewer_UsesKeyboardControlsAndRestoresFocus()
    {
        var html = File.ReadAllText(GetLeaderboardPartialPath());

        Assert.Contains("role=\"dialog\" aria-modal=\"true\" aria-label=\"Enlarged image\"", html);
        Assert.Contains("<button type=\"button\" class=\"close-btn\" aria-label=\"Close enlarged image\">", html);
        Assert.Contains("closeButton.focus();", html);
        Assert.Contains("returnFocusTo.focus();", html);
        Assert.Contains("img.setAttribute('role', 'button');", html);
        Assert.Contains("img.setAttribute('tabindex', '0');", html);
        Assert.Contains("img.addEventListener('keydown', event => {", html);
        Assert.Contains(".proof-item img:focus-visible", html);
        Assert.Contains(".enlarged-portrait .close-btn:focus-visible", html);
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
