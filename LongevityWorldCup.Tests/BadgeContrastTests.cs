using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BadgeContrastTests
{
    [Fact]
    public void LightBadgePalettes_UseAContrastingForeground()
    {
        var badges = File.ReadAllText(GetWebRootFile("js", "badges.js"));
        var badgeCss = File.ReadAllText(GetWebRootFile("css", "badges.css"));
        var eventBoard = File.ReadAllText(GetWebRootFile("partials", "event-board-content.html"));

        Assert.Contains("linear-gradient(135deg, #c0c0c0, #696969); border: 2px solid #6e6e6e; --badge-fg: #0b1220;", badges);
        Assert.Contains("linear-gradient(135deg, #ffd700, #8b8000); border: 2px solid #8a6f00; --badge-fg: #0b1220;", badges);
        Assert.Contains("color:var(--badge-fg,#fff)!important;", badgeCss);
        Assert.Contains("color:var(--badge-fg,#fff)!important;", eventBoard);
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
