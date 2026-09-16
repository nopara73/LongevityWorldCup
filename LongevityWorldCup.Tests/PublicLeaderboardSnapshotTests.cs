using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PublicLeaderboardSnapshotTests
{
    private static readonly DateTime Today = new(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CrowdView_UsesEligibilityTieBreakersPrecisionAndCanonicalAnchors()
    {
        var snapshot = new PublicLeaderboardSnapshot(new JsonArray(
            Athlete("older", 1970, -10, 120),
            Athlete("younger", 1980, -10.049, 100),
            Athlete("not_eligible", 1960, -30, 99),
            Athlete("more_guesses", 1990, -10, 140)), Today);
        var selection = snapshot.Select("/league/crowd", QueryCollection.Empty);
        Assert.Equal(["younger", "more_guesses", "older"], selection.Snapshot.Rows.Select(r => r.Slug));
        Assert.Equal([1, 2, 3], selection.Snapshot.Rows.Select(r => r.Rank));
        Assert.All(selection.Snapshot.Rows, row => Assert.Equal(2, row.MetricDecimals));
        Assert.Equal([3, 4, 2], selection.Snapshot.Rows.Select(r => r.AnchorRank));
    }

    [Fact]
    public void FilteredFlag_RecognizesAliases_AndQueryOverridesTheRoute()
    {
        var first = Athlete("first", 1970);
        first["Flag"] = "USA";
        var second = Athlete("second", 1980);
        second["Flag"] = "United States";
        var third = Athlete("third", 1990);
        third["Flag"] = "Hungary";
        var snapshot = new PublicLeaderboardSnapshot(new JsonArray(first, second, third), Today);
        Assert.Equal(2, snapshot.Select("/flag/united-states", QueryCollection.Empty).Snapshot.Rows.Count);
        var selected = snapshot.Select("/flag/united-states", QueryString.Create("filters", "hungary").ToQuery());
        Assert.Equal("third", Assert.Single(selected.Snapshot.Rows).Slug);
        Assert.Equal(3, selected.Snapshot.Rows[0].AnchorRank);
        Assert.Equal(1, selected.Snapshot.Rows[0].Rank);
    }

    [Theory]
    [InlineData(35.25, 1, "35.3")]
    [InlineData(-35.25, 1, "-35.3")]
    [InlineData(1.15, 1, "1.1")]
    [InlineData(1.005, 2, "1.00")]
    [InlineData(-0.01, 1, "-0.0")]
    [InlineData(0, 2, "0.00")]
    public void Metrics_MatchJavascriptToFixed(double value, int decimals, string expected) =>
        Assert.Equal(expected, PublicHtmlFormat.Fixed(value, decimals));

    [Fact]
    public void Profile_EncodesAthleteText_WithoutPublishingSourceData()
    {
        var source = Athlete("example", 1980);
        source["DisplayName"] = "<script>alert('name')</script>";
        source["Why"] = "<img src=x onerror=alert('bio')>";
        source["MediaContact"] = "private@example.com";
        var snapshot = new PublicLeaderboardSnapshot(new JsonArray(source), Today);
        const string template = "<div id=\"detailsModal\" class=\"modal\"><div class=\"modal-content\"><h2 id=\"athleteName\"></h2><p id=\"athleteBio\"></p></div></div>";
        var html = PublicProfileHtmlRenderer.Render(template, snapshot, snapshot.Athletes[0], Today);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img", html);
        Assert.Contains("</h2>", html);
        Assert.Contains("</p>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("private@example.com", html);
        Assert.DoesNotContain("DateOfBirth", html);
    }

    private static JsonObject Athlete(string slug, int year, double crowdReduction = -10, int count = 100) => new()
    {
        ["AthleteSlug"] = slug,
        ["Name"] = slug,
        ["DateOfBirth"] = new JsonObject { ["Year"] = year, ["Month"] = 1, ["Day"] = 1 },
        ["Division"] = "Open",
        ["CrowdAge"] = (Today - new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays / 365.2425 + crowdReduction,
        ["CrowdCount"] = count
    };
}

internal static class TestQueryStringExtensions
{
    internal static IQueryCollection ToQuery(this QueryString query) => new QueryCollection(
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query.Value));
}
