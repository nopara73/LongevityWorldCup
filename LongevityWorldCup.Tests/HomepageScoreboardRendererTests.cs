using System.Net;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageScoreboardRendererTests
{
    [Fact]
    public void Render_UsesAuthoritativeSnapshotOrderAndShowsFieldCounts()
    {
        var snapshot = new LeaderboardSnapshot([
            Row(1, "ordered_pro", "Ordered Pro", "Pro", -4.2),
            Row(2, "lower_amateur", "Numerically Lower Amateur", "Amateur", -30.0),
            Row(3, "second_pro", "Second Pro", "Pro", 1.5)
        ]);

        var html = HomepageScoreboardRenderer.Render(snapshot);
        var text = VisibleText(html);

        Assert.Contains("class=\"homepage-scoreboard\"", html);
        Assert.Contains("class=\"homepage-scoreboard-title\"", html);
        Assert.Contains("homepage-scoreboard-leader", html);
        Assert.Contains("homepage-scoreboard-score", html);
        Assert.Contains("homepage-scoreboard-metrics", html);
        Assert.Contains("data-state=\"live\"", html);
        Assert.Contains("href=\"/athlete/ordered-pro\"", html);
        Assert.Contains("Ordered Pro", text);
        Assert.DoesNotContain("Numerically Lower Amateur", text);
        Assert.Contains("3 ranked athletes", text);
        Assert.Contains("2 Pro", text);
        Assert.Contains("1 Amateur", text);
    }

    [Fact]
    public void Render_EncodesDynamicLeaderContentAndAttributes()
    {
        var snapshot = new LeaderboardSnapshot([
            Row(
                1,
                "unsafe",
                "<script>alert(\"leader\")</script>",
                "Pro",
                -1.0,
                athletePath: "/athlete/unsafe\" onclick=\"alert(1)")
        ]);

        var html = HomepageScoreboardRenderer.Render(snapshot);

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("onclick=\"alert(1)\"", html);
        Assert.Contains("&lt;script&gt;alert(&quot;leader&quot;)&lt;/script&gt;", html);
        Assert.Contains("/athlete/unsafe&quot; onclick=&quot;alert(1)", html);
    }

    [Theory]
    [InlineData(-4.2, "4.2 years younger")]
    [InlineData(2.1, "2.1 years older")]
    [InlineData(null, "pending")]
    public void Render_DescribesAgeDifferenceWithoutExposingTheRawSign(double? ageDifference, string expected)
    {
        var html = HomepageScoreboardRenderer.Render(new LeaderboardSnapshot([
            Row(1, "leader", "Leaderboard Leader", "Pro", ageDifference)
        ]));
        var text = VisibleText(html);

        Assert.Contains(expected, text, StringComparison.OrdinalIgnoreCase);
        if (ageDifference < 0)
        {
            Assert.DoesNotContain(ageDifference.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), text);
        }
    }

    [Fact]
    public void Render_EmptySnapshotUsesUnavailableStateWithoutFabricatedMetrics()
    {
        AssertUnavailable(HomepageScoreboardRenderer.Render(new LeaderboardSnapshot([])));
    }

    [Fact]
    public void RenderUnavailable_UsesUnavailableStateWithoutFabricatedMetrics()
    {
        AssertUnavailable(HomepageScoreboardRenderer.RenderUnavailable());
    }

    private static void AssertUnavailable(string html)
    {
        var text = VisibleText(html);

        Assert.Contains("class=\"homepage-scoreboard\"", html);
        Assert.Contains("data-state=\"unavailable\"", html);
        Assert.DoesNotContain("data-state=\"live\"", html);
        Assert.DoesNotContain("homepage-scoreboard-metrics", html);
        Assert.DoesNotContain("Ultimate League #1", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"\b0\s+(?:ranked athletes|Pro|Amateur)\b", text);
        Assert.DoesNotContain("years younger", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("years older", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("· Live", text, StringComparison.Ordinal);
        Assert.DoesNotContain("reconnect", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string VisibleText(string html)
    {
        var withoutTags = Regex.Replace(html, "<[^>]+>", " ", RegexOptions.CultureInvariant);
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static LeaderboardSnapshotRow Row(
        int rank,
        string slug,
        string displayName,
        string track,
        double? ageDifference,
        string? athletePath = null)
    {
        var path = athletePath ?? $"/athlete/{slug.Replace('_', '-')}";
        return new LeaderboardSnapshotRow(
            Rank: rank,
            Slug: slug,
            RouteSlug: slug.Replace('_', '-'),
            DisplayName: displayName,
            AthletePath: path,
            AthleteUrl: $"https://longevityworldcup.com{path}",
            Track: track,
            Tier: track.ToLowerInvariant(),
            EffectiveAgeReductionYears: ageDifference,
            LowestBortzAge: string.Equals(track, "Pro", StringComparison.OrdinalIgnoreCase) ? 40 : null,
            LowestPhenoAge: 45,
            ChronologicalAge: 50,
            Division: "Open",
            Generation: "Millennials",
            Flag: "",
            ExclusiveLeague: "",
            MediaContact: "",
            LeaderboardThumbnailUrl: null);
    }
}
