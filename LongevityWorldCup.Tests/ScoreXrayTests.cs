using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ScoreXrayTests
{
    private static readonly DateTime AsOf = new(2026, 10, 6, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SeparateClocks_TraceTheirActualSelectedPanelRatherThanBestMarkerAggregate()
    {
        var first = Panel("2024-01-02");
        first["CreatUmolL"] = 40;
        var second = Panel("2025-01-02");
        second["AlbGL"] = 55;
        second["CreatUmolL"] = 95;
        second["GluMmolL"] = 3;
        second["CrpMgL"] = .01;
        second["Wbc1000cellsuL"] = 3;
        second["LymPc"] = 60;
        second["McvFL"] = 80;
        second["RdwPc"] = 10;
        second["AlpUL"] = 30;
        second["CystatinCMgL"] = 3;
        second["Hba1cMmolMol"] = 90;
        second["GgtUL"] = 300;
        second["ShbgNmolL"] = 300;
        second["VitaminDNmolL"] = 10;
        var athlete = Athlete("separate_clocks", first, second);
        var stats = PhenoStatsCalculator.Compute(athlete, AsOf);
        var document = new ScoreXraySnapshot(new JsonArray(athlete), AsOf).Find("separate_clocks")!;

        Assert.Equal(1, stats.LowestPhenoPanelIndex);
        Assert.Equal(0, stats.LowestBortzPanelIndex);
        Assert.Equal(2, document.Pheno!.PanelNumber);
        Assert.Equal(1, document.Bortz!.PanelNumber);
        Assert.Equal("2025-01-02", document.Pheno.Date);
        Assert.Equal(95, document.Pheno.Lab.Single(l => l.Key == "CreatUmolL").Value);
        Assert.Equal(40, stats.BestMarkerValues![2]);
        var chronologicalAge = (new DateTime(2025, 1, 2) - new DateTime(1975, 1, 2)).TotalDays / 365.2425;
        Assert.Equal(chronologicalAge, document.Pheno.AgeAtTest, 12);
        Assert.Equal(stats.AgeReduction, document.Pheno.AgeDifference);
        Assert.Equal(stats.BortzAgeReduction, document.Bortz.AgeDifference);
    }

    [Fact]
    public void CapsConversionsAndLogarithms_ReconstructTheCanonicalClock()
    {
        var panel = Panel("2025-01-02");
        panel["AlbGL"] = 60;
        panel["AltUL"] = 90;
        panel["VitaminDNmolL"] = 200;
        panel["Wbc1000cellsuL"] = 5;
        panel["MonocytePc"] = 4;
        panel["NeutrophilPc"] = 35;
        var result = new ScoreXraySnapshot(new JsonArray(Athlete("caps", panel)), AsOf).Find("caps")!;
        var pheno = result.Pheno!;
        var bortz = result.Bortz!;
        var albumin = pheno.Terms.Single(t => t.Key == "AlbGL");
        Assert.Equal(60, albumin.Reported);
        Assert.Equal(54, albumin.Scored);
        var crp = pheno.Terms.Single(t => t.Key == "CrpMgL");
        Assert.Equal(.08, crp.Input, 12);
        Assert.Equal(Math.Log(.08), crp.Model, 12);
        var alt = bortz.Terms.Single(t => t.Key == "AltUL");
        Assert.Equal(29, alt.Scored);
        Assert.Equal(Math.Log(29), alt.Model, 12);
        var monocyte = bortz.Terms.Single(t => t.Key == "MonocytePc");
        Assert.Equal(.2, monocyte.Input, 12);
        Assert.Equal(.3, monocyte.Scored, 12);
        var neutrophil = bortz.Terms.Single(t => t.Key == "NeutrophilPc");
        Assert.Equal(1.75, neutrophil.Input, 12);
        Assert.Equal(2, neutrophil.Scored, 12);
        var stats = PhenoStatsCalculator.Compute(Athlete("caps", panel.DeepClone().AsObject()), AsOf);
        Assert.Equal(stats.LowestPhenoAge, pheno.Age);
        Assert.Equal(stats.LowestBortzAge, bortz.Age);
        Assert.Equal(pheno.Calculation[0].Value!.Value, pheno.Terms.Sum(t => t.Contribution), 12);
        Assert.Equal(bortz.Calculation[0].Value!.Value, bortz.Terms.Sum(t => t.Contribution), 12);
        Assert.Equal(22, bortz.Lab.Count);
        Assert.DoesNotContain(bortz.Terms, t => t.Key == "Wbc1000cellsuL");
    }

    [Fact]
    public void EqualPanelsKeepFirstPublishedPanel_AndIneligiblePanelsDoNotProduceAnInventedTrace()
    {
        var panel = Panel("2025-01-02");
        var athlete = Athlete("tie", panel, panel.DeepClone().AsObject(), new JsonObject { ["Date"] = "2026-01-02", ["AlbGL"] = 40 });
        var document = new ScoreXraySnapshot(new JsonArray(athlete), AsOf).Find("tie")!;
        Assert.Equal(1, document.Pheno!.PanelNumber);
        Assert.Equal(1, document.Bortz!.PanelNumber);
        Assert.Null(document.Panels[2].PhenoAge);
        Assert.Null(document.Panels[2].BortzAge);
        var missing = new ScoreXraySnapshot(new JsonArray(Athlete("empty", new JsonObject())), AsOf).Find("empty")!;
        Assert.Null(missing.Pheno);
        Assert.Null(missing.Bortz);
    }

    [Fact]
    public void ZeroAgeFloor_HasASerializableTraceWithoutFabricatingAnIntermediate()
    {
        var panel = Panel("2025-01-02");
        panel["CrpMgL"] = 1e-200;
        var document = new ScoreXraySnapshot(new JsonArray(Athlete("floor", panel)), AsOf).Find("floor")!;
        Assert.Equal(0, document.Pheno!.Age);
        Assert.Null(document.Pheno.Calculation.Single(c => c.Name == "Before the age floor").Value);
        Assert.NotEmpty(JsonSerializer.Serialize(document));
    }

    [Fact]
    public void RankingUsesFullPrecisionAndCanonicalClockField_WithoutRenumberingNeighbors()
    {
        var one = Athlete("one", Panel("2025-01-02"));
        var two = Athlete("two", Panel("2025-01-02"));
        two["DateOfBirth"] = new JsonObject { ["Year"] = 1974, ["Month"] = 1, ["Day"] = 2 };
        var amateur = Athlete("amateur", Panel("2025-01-02"));
        amateur["Biomarkers"]![0]!.AsObject().Remove("ApoA1GL");
        var athletes = new JsonArray(one, two, amateur);
        var canonical = new PublicLeaderboardSnapshot(athletes, AsOf);
        var xray = new ScoreXraySnapshot(athletes, AsOf);
        foreach (var row in canonical.Athletes)
        {
            var document = xray.Find(row.Row.Slug)!;
            Assert.Equal(row.Row.Rank, document.Athlete.UltimateRank);
            Assert.Equal(canonical.Order("pheno").Select(a => a.Row.Slug).ToList().IndexOf(row.Row.Slug) + 1, document.Pheno!.Rank);
            Assert.Equal(3, document.Pheno.FieldSize);
            if (row.IsPro)
            {
                Assert.Equal(canonical.Order("bortz").Select(a => a.Row.Slug).ToList().IndexOf(row.Row.Slug) + 1, document.Bortz!.Rank);
                Assert.Equal(2, document.Bortz.FieldSize);
                Assert.False(document.Pheno.SuppliesUltimateScore);
                Assert.True(document.Bortz.SuppliesUltimateScore);
            }
            else { Assert.Null(document.Bortz); Assert.True(document.Pheno.SuppliesUltimateScore); }
            Assert.Equal(document.Pheno.Rank, document.Pheno.Neighbors.Single(n => n.Slug == row.Row.Slug).Rank);
        }
    }

    [Fact]
    public async Task ReviewPageAndApiAreNoindexNoStoreVersionedAndAbsentFromDiscovery()
    {
        await using var app = new TestWebApplicationFactory();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/score-xray");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains("noindex", string.Join(" ", response.Headers.GetValues("X-Robots-Tag")));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("/js/score-xray.js?v=", html);
        Assert.Contains("/css/score-xray.css?v=", html);
        Assert.DoesNotContain("{{ASSET_", html);
        using var alias = await client.GetAsync("/score-xray/index.html?athlete=michael-lustgarten&clock=pheno&step=4");
        Assert.Equal(HttpStatusCode.MovedPermanently, alias.StatusCode);
        Assert.EndsWith("/score-xray?athlete=michael-lustgarten&clock=pheno&step=4", alias.Headers.Location!.ToString());
        using var api = await client.GetAsync("/api/score-xray/michael-lustgarten");
        Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        Assert.True(api.Headers.CacheControl!.NoStore);
        var json = await api.Content.ReadAsStringAsync();
        Assert.Contains("\"panelNumber\"", json);
        Assert.DoesNotContain("DateOfBirth", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MediaContact", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/score-xray/not-a-real-athlete")).StatusCode);
        foreach (var path in new[] { "/sitemap.xml", "/llms.txt", "/swagger/v1/swagger.json", "/leaderboard" })
            Assert.DoesNotContain("score-xray", await client.GetStringAsync(path));
    }

    internal static JsonObject Athlete(string slug, params JsonObject[] panels) => JsonNode.Parse(new JsonObject
    {
        ["Name"] = slug, ["AthleteSlug"] = slug, ["DisplayName"] = slug,
        ["DateOfBirth"] = new JsonObject { ["Year"] = 1975, ["Month"] = 1, ["Day"] = 2 },
        ["Biomarkers"] = new JsonArray(panels.Select(p => (JsonNode)p).ToArray()),
        ["Proofs"] = new JsonArray("/athletes/test/proof.jpg")
    }.ToJsonString())!.AsObject();

    internal static JsonObject Panel(string date) => new()
    {
        ["Date"] = date, ["AlbGL"] = 50, ["CreatUmolL"] = 70, ["GluMmolL"] = 4.8, ["CrpMgL"] = .8,
        ["Wbc1000cellsuL"] = 5, ["LymPc"] = 35, ["McvFL"] = 89, ["RdwPc"] = 12, ["AlpUL"] = 55,
        ["UreaMmolL"] = 5, ["CholesterolMmolL"] = 4.5, ["CystatinCMgL"] = .8, ["Hba1cMmolMol"] = 34,
        ["GgtUL"] = 20, ["Rbc10e12L"] = 5, ["MonocytePc"] = 6, ["NeutrophilPc"] = 50,
        ["AltUL"] = 20, ["ShbgNmolL"] = 40, ["VitaminDNmolL"] = 100, ["MchPg"] = 30, ["ApoA1GL"] = 1.6
    };
}
