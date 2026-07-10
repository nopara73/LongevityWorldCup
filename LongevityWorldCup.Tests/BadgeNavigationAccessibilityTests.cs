using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BadgeNavigationAccessibilityTests
{
    [Fact]
    public void ClickableBadges_UseNativeLinksInsteadOfPointerOnlySpans()
    {
        var badges = File.ReadAllText(GetWebRootFile("js", "badges.js"));
        var eventBoard = File.ReadAllText(GetWebRootFile("partials", "event-board-content.html"));

        Assert.Contains("<a class=\"${className} badge-clickable\" href=\"${escapeAttr(url)}\" aria-label=\"Open league\"", badges);
        Assert.Contains("aria-label=\"Open personal page\"", badges);
        Assert.DoesNotContain("onclick=\"window.location.href='${url}';\"", badges);

        Assert.Contains("<a class=\"badge-class ${familyClass} badge-clickable\" href=\"${esc(url)}\" aria-label=\"Open league\"", eventBoard);
        Assert.DoesNotContain("role=\"button\" tabindex=\"0\" onclick=\"window.location.href='${url}'\"", eventBoard);
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
