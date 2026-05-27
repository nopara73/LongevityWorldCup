using System.Text.Json.Nodes;
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

    [Fact]
    public void BuildAndRender_PreservesProBeforeAmateurRowsFromSharedSnapshot()
    {
        var ranked = new JsonArray
        {
            Ranked("first_pro", "First Pro", -1, lowestBortzAge: 40),
            Ranked("second_pro", "Second Pro", 1, lowestBortzAge: 55),
            Ranked("first_amateur", "First Amateur", -100),
            Ranked("second_amateur", "Second Amateur", -99)
        };
        var athletes = new JsonArray
        {
            Athlete("first_amateur", "First Amateur", mediaContact: "first.amateur@example.com"),
            Athlete("second_amateur", "Second Amateur"),
            Athlete("first_pro", "First Pro"),
            Athlete("second_pro", "Second Pro")
        };

        var snapshot = LeaderboardSnapshotBuilder.Build(ranked, athletes);
        var html = LeaderboardHtmlRenderer.RenderRows(snapshot);

        Assert.Equal(["pro", "pro", "amateur", "amateur"], snapshot.Rows.Select(row => row.Tier).ToArray());
        Assert.True(html.IndexOf("id=\"rank-2\"", StringComparison.Ordinal) < html.IndexOf("id=\"rank-3\"", StringComparison.Ordinal));
        Assert.DoesNotContain("tier-pro", html[html.IndexOf("tier-amateur", StringComparison.Ordinal)..]);
        Assert.DoesNotContain("first.amateur@example.com", html);
        Assert.DoesNotContain("mailto:", html);
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

    private static JsonObject Ranked(string slug, string name, double ageDifference, double? lowestBortzAge = null)
    {
        var row = new JsonObject
        {
            ["AthleteSlug"] = slug,
            ["Name"] = name,
            ["ChronologicalAge"] = 50.0,
            ["LowestPhenoAge"] = 45.0,
            ["AgeDifference"] = ageDifference
        };
        if (lowestBortzAge.HasValue)
        {
            row["LowestBortzAge"] = lowestBortzAge.Value;
        }

        return row;
    }

    private static JsonObject Athlete(string slug, string displayName, string mediaContact = "")
    {
        return new JsonObject
        {
            ["AthleteSlug"] = slug,
            ["DisplayName"] = displayName,
            ["Division"] = "Open",
            ["MediaContact"] = mediaContact,
            ["DateOfBirth"] = new JsonObject
            {
                ["Year"] = 1985,
                ["Month"] = 1,
                ["Day"] = 1
            }
        };
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
