using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageScoreboardPageTests
{
    [Fact]
    public async Task Homepage_InjectsLiveScoreboardWithOneVisiblePrimaryHeading()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");
        var scoreboard = ExtractScoreboard(html);

        Assert.Contains("data-state=\"live\"", scoreboard);
        Assert.DoesNotContain("<!--HOMEPAGE-SCOREBOARD-->", html);

        var h1Matches = Regex.Matches(html, @"<h1\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var h1 = Assert.Single(h1Matches.Cast<Match>()).Value;
        Assert.Contains("class=\"homepage-scoreboard-title\"", h1);
        Assert.DoesNotContain("visually-hidden", h1, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aria-hidden=\"true\"", h1, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" hidden", h1, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Homepage_UsesCanonicalClockCopyAndNamedLiveStandingsAnchor()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");
        var scoreboard = ExtractScoreboard(html);
        var scoreboardText = VisibleText(scoreboard);

        Assert.Contains("pheno age", scoreboardText, StringComparison.Ordinal);
        Assert.Contains("bortz age", scoreboardText, StringComparison.Ordinal);
        Assert.DoesNotContain("Pheno Age", scoreboardText, StringComparison.Ordinal);
        Assert.DoesNotContain("Bortz Age", scoreboardText, StringComparison.Ordinal);

        var standingsLink = Regex.Match(
            scoreboard,
            """<a\b(?=[^>]*\bhref\s*=\s*["']#live-standings["'])[^>]*>[\s\S]*?</a>""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.True(standingsLink.Success, "The homepage scoreboard must link to the in-page live standings anchor.");
        Assert.Equal("Explore live standings", VisibleText(standingsLink.Value));
        Assert.Contains("id=\"live-standings\"", html);
    }

    [Fact]
    public async Task Homepage_DoesNotReintroducePseudoPodiumMarkup()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var scoreboard = ExtractScoreboard(await client.GetStringAsync("/"));

        Assert.DoesNotContain("data-homepage-rank-card", scoreboard);
        Assert.DoesNotContain("homepage-rank-card", scoreboard);
        Assert.DoesNotContain("homepage-live-panel", scoreboard);
        Assert.DoesNotContain("Top Amateur", scoreboard, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scoreboard_IsInjectedOnHomepageOnly()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[] { "/leaderboard", "/about", "/play", "/longevitymaxxing" })
        {
            var html = await client.GetStringAsync(path);

            Assert.DoesNotContain("class=\"homepage-scoreboard\"", html);
            Assert.DoesNotContain("<!--HOMEPAGE-SCOREBOARD-->", html);
        }
    }

    private static string ExtractScoreboard(string html)
    {
        var match = Regex.Match(
            html,
            """<section\b(?=[^>]*\bclass\s*=\s*["'][^"']*\bhomepage-scoreboard\b[^"']*["'])[^>]*>[\s\S]*?</section>""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        Assert.True(match.Success, "The homepage response did not contain the injected scoreboard section.");
        return match.Value;
    }

    private static TestWebApplicationFactory CreateFactory()
    {
        return new TestWebApplicationFactory(builder =>
            builder.ConfigureLogging(logging => logging.ClearProviders()));
    }

    private static string VisibleText(string html)
    {
        var withoutHiddenContent = Regex.Replace(
            html,
            """<(?<tag>[a-z][a-z0-9]*)\b[^>]*\baria-hidden\s*=\s*["']true["'][^>]*>[\s\S]*?</\k<tag>>""",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var withoutTags = Regex.Replace(withoutHiddenContent, "<[^>]+>", " ", RegexOptions.CultureInvariant);
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }
}
