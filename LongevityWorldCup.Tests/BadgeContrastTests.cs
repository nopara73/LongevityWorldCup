using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BadgeContrastTests
{
    [Fact]
    public void BadgeIcons_UseAConsistentWhiteForeground()
    {
        var badges = File.ReadAllText(GetWebRootFile("js", "badges.js"));
        var badgeCss = File.ReadAllText(GetWebRootFile("css", "badges.css"));
        var eventBoard = File.ReadAllText(GetWebRootFile("partials", "event-board-content.html"));

        Assert.DoesNotContain("--badge-fg", badges);
        Assert.Contains("color:#fff!important;", badgeCss);
        Assert.Contains("color:#fff!important;", eventBoard);
    }

    private static string GetWebRootFile(string folder, string fileName, [CallerFilePath] string sourceFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Could not locate the tests directory.");
        return Path.GetFullPath(Path.Combine(
            testsDirectory,
            "..",
            "LongevityWorldCup.Website",
            "wwwroot",
            folder,
            fileName));
    }
}
