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

        Assert.Contains("aria-label=\"${escapeAttr(tooltip)}. Open league\"", badges);
        Assert.Contains("aria-label=\"${escapeAttr(tooltip)}. Open podcast\"", badges);
        Assert.Contains("badge-explained\" tabindex=\"0\" aria-label=\"${escapeAttr(tooltip)}\"", badges);
        Assert.Contains("aria-label=\"Open personal page\"", badges);
        Assert.DoesNotContain("onclick=\"window.location.href='${url}';\"", badges);

        Assert.Contains("const isPodcast=String(label||'').trim().toLowerCase()==='podcast';", eventBoard);
        Assert.Contains("const targetAttrs=isPodcast?' target=\"_blank\" rel=\"noopener\"':'';", eventBoard);
        Assert.Contains("const accessibleLabel=isPodcast?`${tip}. Open podcast`:`${tip}. Open league`;", eventBoard);
        Assert.Contains("href=\"${esc(url)}\"${targetAttrs} aria-label=\"${esc(accessibleLabel)}\"", eventBoard);
        Assert.Contains("badge-explained\" tabindex=\"0\" aria-label=\"${esc(tip)}\"", eventBoard);
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
