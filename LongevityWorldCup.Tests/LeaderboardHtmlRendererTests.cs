using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public class LeaderboardHtmlRendererTests
{
    [Fact]
    public void RenderRows_RendersAllRowsWithoutCutoff()
    {
        var rows = Enumerable.Range(1, 35)
            .Select(i => Row(i, $"athlete_{i}", i <= 30 ? "Pro" : "Amateur"))
            .ToList();

        var html = LeaderboardHtmlRenderer.RenderRows(new LeaderboardSnapshot(rows));

        Assert.Equal(35, CountOccurrences(html, "server-rendered-leaderboard-row"));
        Assert.Contains("id=\"rank-35\"", html);
        Assert.Contains("tier-amateur", html);
    }

    [Fact]
    public void RenderRows_EscapesContentAndAttributes()
    {
        var snapshot = new LeaderboardSnapshot([
            Row(
                rank: 1,
                slug: "evil",
                track: "Pro",
                displayName: "<script>alert(1)</script> \"Name\"",
                athletePath: "/athlete/evil",
                thumbnail: "/generated/thumbs/athletes/evil.webp?v=1&x=<bad>",
                mediaContact: "https://example.com/?q=<script>")
        ]);

        var html = LeaderboardHtmlRenderer.RenderRows(snapshot);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &quot;Name&quot;", html);
        Assert.Contains("evil.webp?v=1&amp;x=&lt;bad&gt;", html);
        Assert.Contains("https://example.com/?q=&lt;script&gt;", html);
    }

    [Fact]
    public void RenderRows_OnlyRendersGeneratedThumbnails()
    {
        var snapshot = new LeaderboardSnapshot([
            Row(1, "generated", "Pro", thumbnail: "/generated/thumbs/athletes/generated.webp?v=1"),
            Row(2, "athlete", "Pro", thumbnail: "/athletes/athlete/athlete.webp?v=1")
        ]);

        var html = LeaderboardHtmlRenderer.RenderRows(snapshot);

        Assert.Contains("/generated/thumbs/athletes/generated.webp?v=1", html);
        Assert.DoesNotContain("/athletes/athlete/athlete.webp?v=1", html);
    }

    private static LeaderboardSnapshotRow Row(
        int rank,
        string slug,
        string track,
        string displayName = "",
        string athletePath = "",
        string? thumbnail = null,
        string mediaContact = "")
    {
        var name = string.IsNullOrWhiteSpace(displayName) ? $"Athlete {rank}" : displayName;
        var path = string.IsNullOrWhiteSpace(athletePath) ? $"/athlete/{slug.Replace('_', '-')}" : athletePath;
        return new LeaderboardSnapshotRow(
            Rank: rank,
            Slug: slug,
            RouteSlug: slug.Replace('_', '-'),
            DisplayName: name,
            AthletePath: path,
            AthleteUrl: $"https://longevityworldcup.com{path}",
            Track: track,
            Tier: track.ToLowerInvariant(),
            EffectiveAgeReductionYears: -rank,
            LowestBortzAge: track == "Pro" ? 40 : null,
            LowestPhenoAge: 45,
            ChronologicalAge: 50,
            Division: "Open",
            Generation: "Millennials",
            Flag: "",
            ExclusiveLeague: "",
            MediaContact: mediaContact,
            LeaderboardThumbnailUrl: thumbnail);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
