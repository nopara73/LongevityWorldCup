using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class FooterStructureTests
{
    [GeneratedRegex(@"<a\b[^>]*\bhref=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex FooterLinkRegex();

    [Fact]
    public void Footer_GroupsEveryDestinationInPriorityOrder()
    {
        var footer = ReadFooter();

        Assert.Contains(
            "<h2 id=\"footer-competition-heading\" class=\"footer-heading\">Competition</h2>",
            footer,
            StringComparison.Ordinal);
        Assert.Contains(
            "<h2 id=\"footer-community-heading\" class=\"footer-heading\">Community</h2>",
            footer,
            StringComparison.Ordinal);

        var expectedLinks = new[]
        {
            "/about",
            "/ruleset",
            "/history",
            "/media",
            "mailto:hi@longevityworldcup.com",
            "https://github.com/nopara73/LongevityWorldCup/",
            "https://merch.longevityworldcup.com/",
            "https://www.youtube.com/playlist?list=PL4nqc85w185sO4i7eR3oUO_lMmlJ2K1cL",
            "https://x.com/LongevityWorldC",
            "https://www.reddit.com/r/LongevityWorldCup/",
            "https://www.tiktok.com/@nopara73",
            "https://www.threads.com/@longevityworldcup",
            "https://www.youtube.com/@longevityworldcup",
            "https://www.instagram.com/LongevityWorldCup/"
        };

        var actualLinks = FooterLinkRegex()
            .Matches(footer)
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(expectedLinks, actualLinks);
        Assert.Equal(
            actualLinks.Length,
            Regex.Matches(footer, "class=\"footer-link\"", RegexOptions.IgnoreCase).Count);
    }

    [Fact]
    public void Footer_KeepsCompactTouchTargetsStableAtNarrowWidths()
    {
        var footer = ReadFooter();

        Assert.Contains("grid-template-columns: repeat(2, minmax(0, 1fr));", footer, StringComparison.Ordinal);
        Assert.Contains("min-height: 2.75rem;", footer, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap: anywhere;", footer, StringComparison.Ordinal);
        Assert.Contains(".footer-link:focus-visible", footer, StringComparison.Ordinal);
        Assert.Contains(
            "outline: 3px solid var(--footer-focus-color, var(--footer-accent-color, #43ee83));",
            footer,
            StringComparison.Ordinal);
        Assert.DoesNotContain("translate", footer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?<!text-)transform\s*:", footer);
        Assert.DoesNotContain("transition: transform", footer, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadFooter()
    {
        var repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(
            repoRoot,
            "LongevityWorldCup.Website",
            "wwwroot",
            "partials",
            "footer.html"));
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
