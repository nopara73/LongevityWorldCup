using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageFirstViewportRendererTests
{
    [Fact]
    public void Render_ShowsLiveCompetitionSignalsFromSharedSnapshotOrder()
    {
        var snapshot = new LeaderboardSnapshot([
            Row(rank: 1, slug: "first_pro", track: "Pro", displayName: "First Pro", ageReduction: -4.2),
            Row(rank: 2, slug: "second_pro", track: "Pro", displayName: "Second Pro", ageReduction: 1.1),
            Row(rank: 3, slug: "first_amateur", track: "Amateur", displayName: "First Amateur", ageReduction: -99.0)
        ]);

        var html = HomepageFirstViewportRenderer.Render(snapshot);

        Assert.Contains("Ultimate League", html);
        Assert.Contains("Pro before Amateur", html);
        Assert.Contains("Ultimate #1", html);
        Assert.Contains("Ultimate #1: First Pro", html);
        Assert.Contains("<strong>3</strong> athletes</span>", html);
        Assert.Contains("<strong>2</strong> Pro</span>", html);
        Assert.Contains("<strong>1</strong> Amateur</span>", html);
        Assert.Contains("href=\"/athlete/first-pro\"", html);
        Assert.DoesNotContain("href=\"/athlete/first-amateur\"", html);
        Assert.DoesNotContain("Apply as athlete", html);
        Assert.DoesNotContain("data-homepage-rank-card", html);
    }

    [Fact]
    public void Render_EscapesLeaderName()
    {
        var snapshot = new LeaderboardSnapshot([
            Row(
                rank: 1,
                slug: "bad",
                track: "Pro",
                displayName: "<script>alert(1)</script>",
                ageReduction: -1.0,
                thumbnail: "/generated/thumbs/athletes/safe.webp?v=2"),
            Row(
                rank: 2,
                slug: "safe",
                track: "Amateur",
                displayName: "Safe Athlete",
                ageReduction: -2.0,
                thumbnail: "/athletes/bad/bad.webp?v=1")
        ]);

        var html = HomepageFirstViewportRenderer.Render(snapshot);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("Ultimate #1: &lt;script&gt;alert(1)&lt;/script&gt;", html);
    }

    private static LeaderboardSnapshotRow Row(
        int rank,
        string slug,
        string track,
        string displayName,
        double ageReduction,
        string? thumbnail = null)
    {
        var path = $"/athlete/{slug.Replace('_', '-')}";
        return new LeaderboardSnapshotRow(
            Rank: rank,
            Slug: slug,
            RouteSlug: slug.Replace('_', '-'),
            DisplayName: displayName,
            AthletePath: path,
            AthleteUrl: $"https://longevityworldcup.com{path}",
            Track: track,
            Tier: track.ToLowerInvariant(),
            EffectiveAgeReductionYears: ageReduction,
            LowestBortzAge: string.Equals(track, "Pro", StringComparison.OrdinalIgnoreCase) ? 42 : null,
            LowestPhenoAge: 45,
            ChronologicalAge: 50,
            Division: "Open",
            Generation: "Millennials",
            Flag: "",
            ExclusiveLeague: "",
            MediaContact: "",
            LeaderboardThumbnailUrl: thumbnail);
    }
}
