using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LeaderboardOrderingTests
{
    [Fact]
    public void BiomarkerCatalog_CoversEveryPublicBloodMarkerExactlyOnce()
    {
        var storedMarkers = typeof(BiomarkerData).GetProperties()
            .Where(property => property.PropertyType == typeof(double?))
            .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        var fields = LeaderboardOrdering.Fields.Where(field => field.Group == "Biomarkers").ToArray();
        Assert.Equal(storedMarkers, fields.Select(field => field.SourceKey).Order(StringComparer.Ordinal));
        Assert.Equal(LeaderboardOrdering.Fields.Count, LeaderboardOrdering.Fields.Select(field => field.Key).Distinct().Count());
        Assert.All(fields, field => Assert.False(string.IsNullOrWhiteSpace(field.Unit)));
    }

    [Fact]
    public void EveryBloodMarker_UsesItsLatestDatedRawValueWithoutMixingPanels()
    {
        var source = Athlete("all-markers", 1980);
        var older = new JsonObject { ["Date"] = "2024-01-01" };
        var newer = new JsonObject { ["Date"] = "2025-01-01" };
        var undated = new JsonObject();
        var markers = typeof(BiomarkerData).GetProperties().Where(property => property.PropertyType == typeof(double?)).ToArray();
        for (var index = 0; index < markers.Length; index++)
        {
            older[markers[index].Name] = 10d + index;
            newer[markers[index].Name] = 100d + index;
            undated[markers[index].Name] = 1000d + index;
        }
        source["Biomarkers"] = new JsonArray(newer, older, undated, new JsonObject { ["Date"] = "2026-01-01" });
        var values = new PublicLeaderboardSnapshot(new JsonArray(source), new DateTime(2026, 2, 1)).Athletes.Single().Row.OrderValues;
        foreach (var field in LeaderboardOrdering.Fields.Where(field => field.SourceKey is not null))
            Assert.Equal(new(newer[field.SourceKey!]!.GetValue<double>(), TestDate: "2025-01-01"), values[field.Key]);
        // Differential counts must remain stored percentages, not WBC-derived counts.
        Assert.Equal(newer["MonocytePc"]!.GetValue<double>(), values["monocytes"].Number);
        Assert.Equal(newer["NeutrophilPc"]!.GetValue<double>(), values["neutrophils"].Number);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteValues_AreMissingInBothDirections(double value)
    {
        Assert.True(LeaderboardOrdering.Compare(new(value), new(0), "asc") > 0);
        Assert.True(LeaderboardOrdering.Compare(new(value), new(0), "desc") > 0);
    }

    [Theory]
    [InlineData("asc", "asc", "beta,alpha,zero,missing")]
    [InlineData("asc", "desc", "alpha,beta,zero,missing")]
    [InlineData("desc", "asc", "zero,beta,alpha,missing")]
    [InlineData("desc", "desc", "zero,alpha,beta,missing")]
    public void TwoLevels_UseFullPrecisionKeepMissingLastAndPreserveRanks(string primary, string secondary, string expected)
    {
        var rows = new[] { Row(7, "alpha", -10.001, 40), Row(2, "beta", -10.001, 30), Row(5, "zero", 0, 10), Row(1, "missing", null, 0) };
        var result = LeaderboardOrdering.Apply(rows, new("pheno", primary, "age", secondary));
        Assert.Equal(expected.Split(','), result.Select(row => row.Slug));
        Assert.All(result, row => Assert.Equal(rows.Single(original => original.Slug == row.Slug).Rank, row.Rank));
        Assert.Equal([7, 2, 5, 1], rows.Select(row => row.Rank));
        Assert.Equal(["beta", "alpha"], LeaderboardOrdering.Apply(rows.Take(2), new("pheno")).Select(row => row.Slug));
        Assert.True(LeaderboardOrdering.Compare(new(-10.002), new(-10.001), "asc") < 0);
    }

    [Theory]
    [InlineData("?sort=unknown&then=age", "rank", "asc", null, "asc")]
    [InlineData("?sort=rank&dir=desc&then=age", "rank", "asc", null, "asc")]
    [InlineData("?sort=pheno&dir=bad&then=pheno", "pheno", "asc", null, "asc")]
    [InlineData("?sort=lastTest&dir=bad&then=bad", "lastTest", "desc", null, "asc")]
    [InlineData("?sort=pheno&sort=age&dir=desc&then=age&thenDir=desc", "pheno", "desc", "age", "desc")]
    public void Query_ValidatesBothLevelsAndUsesTheFirstOccurrence(string query, string sort, string direction, string? then, string thenDirection) =>
        Assert.Equal(new(sort, direction, then, thenDirection), LeaderboardOrderState.Parse(new QueryString(query).ToQuery()));

    [Fact]
    public void PublicValues_UseLatestExplicitlyDatedRawMarkersAndDoNotInventMeasurements()
    {
        var source = Athlete("example", 1980);
        source["Biomarkers"] = JsonNode.Parse("""
            [{"Date":"2024-02-29","AlbGL":60,"GluMmolL":0},
             {"Date":"2025-03-01","AlbGL":65,"CrpMgL":0},
             {"Date":"2025-03-01","AlbGL":66},
             {"Date":"2026-01-01","CreatUmolL":70},
             {"Date":"2026-02-30","AlbGL":100},
             {"AlbGL":101}]
            """);
        source["Badges"] = JsonNode.Parse("""[{"name":"award"},{"name":"another"}]""");
        var athlete = new PublicLeaderboardSnapshot(new JsonArray(source), DateTime.UtcNow.Date).Athletes.Single();
        var values = athlete.Row.OrderValues;
        Assert.Equal(new(66, TestDate: "2025-03-01"), values["albumin"]);
        Assert.Equal(new(0, TestDate: "2024-02-29"), values["glucose"]);
        Assert.Equal("2024-02-29", values["firstTest"].Text);
        Assert.Equal("2026-01-01", values["lastTest"].Text);
        Assert.Equal(6, values["tests"].Number);
        Assert.Equal(2, values["badges"].Number);
        Assert.True(values["pheno"].IsMissing);
        Assert.True(values["bortz"].IsMissing);
        Assert.True(values["phenoReduction"].IsMissing);
        Assert.True(values["phenoPercent"].IsMissing);
        Assert.True(values["phenoPace"].IsMissing);
        Assert.True(values["bortzPercent"].IsMissing);
        Assert.True(values["pace"].IsMissing);
        Assert.True(values["crowd"].IsMissing);
        Assert.Equal(0, values["crowdCount"].Number);
    }

    [Fact]
    public void Selection_AssignsCohortRanksBeforeSortingAndKeepsByFlagOrderDormant()
    {
        var snapshot = new PublicLeaderboardSnapshot(new JsonArray(Athlete("older", 1970), Athlete("younger", 1990), Athlete("middle", 1980)), DateTime.UtcNow.Date);
        var official = snapshot.Select("/league/open", QueryCollection.Empty);
        var sorted = snapshot.Select("/league/open", new QueryString("?sort=age").ToQuery());
        Assert.Equal(["younger", "middle", "older"], sorted.Snapshot.Rows.Select(row => row.Slug));
        Assert.All(sorted.Snapshot.Rows, row => Assert.Equal(official.Snapshot.Rows.Single(other => other.Slug == row.Slug).Rank, row.Rank));
        var flags = snapshot.Select("/leaderboard", new QueryString("?group=flags&sort=age&then=pheno").ToQuery());
        Assert.Equal("age", flags.Ordering.Sort);
        Assert.False(flags.DisplayOrdering.IsCustom);
        Assert.Equal(snapshot.Select("/leaderboard", QueryCollection.Empty).Snapshot.Rows.Select(row => row.Slug), flags.Snapshot.Rows.Select(row => row.Slug));
    }

    [Fact]
    public void Headers_DeduplicateExistingColumnsAndExposeOnlyPrimaryAriaSort()
    {
        var state = new LeaderboardOrderState("pheno", "asc", "age", "desc");
        Assert.Equal(2, state.Columns.Count());
        Assert.Empty(new LeaderboardOrderState("name", "asc", "metric").Columns);
        Assert.Contains("aria-sort=\"ascending\"", LeaderboardOrderingHtmlRenderer.Header("pheno", "Pheno Age", state));
        var secondary = LeaderboardOrderingHtmlRenderer.Header("age", "Chronological age", state);
        Assert.DoesNotContain("aria-sort", secondary);
        Assert.Contains("then by", secondary);
        Assert.Contains("descending", secondary);
    }

    private static JsonObject Athlete(string name, int year) => new()
    {
        ["Name"] = name, ["AthleteSlug"] = name, ["Division"] = "Open", ["Flag"] = "Hungary",
        ["DateOfBirth"] = new JsonObject { ["Year"] = year, ["Month"] = 1, ["Day"] = 1 }
    };

    private static LeaderboardSnapshotRow Row(int rank, string slug, double? pheno, double? age)
    {
        var source = new JsonArray(new JsonObject { ["AthleteSlug"] = slug, ["Name"] = slug });
        return LeaderboardSnapshotBuilder.Build(source, source).Rows.Single() with
        {
            Rank = rank,
            OrderValues = new Dictionary<string, LeaderboardOrderValue> { ["pheno"] = new(pheno), ["age"] = new(age) }
        };
    }
}
