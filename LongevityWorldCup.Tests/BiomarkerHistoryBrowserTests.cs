using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BiomarkerHistoryBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1280, false, 100)]
    [InlineData(390, true, 100)]
    [InlineData(320, true, 100)]
    [InlineData(320, true, 200)]
    public async Task UnifiedResults_PutBothClocksFirstAndPreserveRawDatedMeasurements(int width, bool dark, int textPercent)
    {
        await using var context = await NewContextAsync(width, dark);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 66, partial: true);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Row(page, "bortz").GetByRole(AriaRole.Button)).ToBeEnabledAsync();
        var section = page.Locator("#athlete-biomarkers");
        await Expect(section.GetByRole(AriaRole.Heading)).ToHaveTextAsync("Results over time");
        Assert.Equal(0, await section.GetByRole(AriaRole.Tab).CountAsync());
        Assert.Equal(0, await section.Locator("select, canvas").CountAsync());
        var rows = section.Locator(".result-summary");
        await Expect(rows.Nth(0)).ToHaveAttributeAsync("data-measure", "bortz");
        await Expect(rows.Nth(1)).ToHaveAttributeAsync("data-measure", "pheno");
        var pheno = PhenoAgeHelper.CalculatePhenoAge(AlbuminCapTestData.PhenoValues(66));
        var bortz = BortzAgeHelper.CalculateBortzAgeFromRaw(AlbuminCapTestData.Age, AlbuminCapTestData.BortzValues(66));
        await Expect(Row(page, "pheno").Locator(".biomarker-overview-current")).ToHaveTextAsync(pheno.ToString("F1", CultureInfo.InvariantCulture));
        await Expect(Row(page, "bortz").Locator(".biomarker-overview-current")).ToHaveTextAsync(bortz.ToString("F1", CultureInfo.InvariantCulture));
        await Expect(Row(page, "bortz").Locator("time")).ToHaveAttributeAsync("datetime", "2026-01-01");
        if (textPercent != 100)
            await page.AddStyleTagAsync(new() { Content = $"html {{ font-size:{textPercent}% !important; }}" });
        if (textPercent == 100)
            Assert.True(await page.Locator("#biomarkerResultList").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
        foreach (var (name, unit, value) in new[]
        {
            ("Albumin", "g/L", "66"), ("Creatinine", "µmol/L", "88.4"), ("Glucose", "mmol/L", "5.05"),
            ("CRP", "mg/L", "0.8"), ("RBC", "10¹²/L", "4.7"), ("Vitamin D", "nmol/L", "85"), ("HbA1c", "mmol/mol", "34")
        })
        {
            var button = Row(page, $"{name} ({unit})").GetByRole(AriaRole.Button);
            await button.ClickAsync();
            await Expect(button).ToHaveAttributeAsync("aria-expanded", "true");
            await Expect(section.Locator(".result-detail-row:not([hidden])")).ToHaveCountAsync(1);
            var table = section.Locator(".result-detail-row:not([hidden]) .biomarker-history-table");
            await Expect(table.GetByRole(AriaRole.Columnheader).Last).ToHaveTextAsync($"{name} ({unit})");
            await Expect(table.Locator("tbody tr")).ToHaveCountAsync(2);
            await Expect(table.Locator("time").First).ToHaveAttributeAsync("datetime", "2026-01-01");
            await Expect(table.Locator("tbody td").First).ToHaveTextAsync(value);
            await Expect(table.Locator("tbody td").Last).ToHaveTextAsync(name == "CRP" ? "0" : "—");
            Assert.True(await table.Locator("tbody td").First.EvaluateAsync<bool>("""
                el => { const range = document.createRange(); range.selectNodeContents(el); return range.getClientRects().length === 1; }
                """));
        }
        if (textPercent == 100)
            Assert.True(await page.Locator("#biomarkerResultList").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
        await page.Locator(".result-detail-row:not([hidden]) .result-plot svg").PressAsync("Escape");
        await Expect(Row(page, "HbA1c (mmol/mol)").GetByRole(AriaRole.Button)).ToBeFocusedAsync();
        await Expect(section.Locator(".result-detail-row:not([hidden])")).ToHaveCountAsync(0);
        await Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("pheno", "UTC")]
    [InlineData("pheno", "America/Los_Angeles")]
    [InlineData("pheno", "Asia/Singapore")]
    [InlineData("bortz", "UTC")]
    [InlineData("bortz", "America/Los_Angeles")]
    [InlineData("bortz", "Asia/Singapore")]
    public async Task ClockComparison_UsesCompleteSameClockPanelsAndHighlightsThisAthlete(string clock, string timezone)
    {
        await using var context = await NewContextAsync(1280, true, timezone);
        var first = await HistoryAthleteAsync("Benjamin Garden", 66, partial: true);
        var second = await HistoryAthleteAsync("Valerie Orsoni", 43, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ({new JsonArray(first, second).ToJsonString()});");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Row(page, clock).GetByRole(AriaRole.Button).ClickAsync();
        var svg = page.Locator($".result-plot svg[data-clock='{clock}']");
        await Expect(svg).ToBeVisibleAsync();
        await Expect(svg.Locator(".result-plot-point")).ToHaveCountAsync(2);
        await Expect(svg.Locator(".result-plot-point.is-athlete")).ToHaveCountAsync(1);
        await Expect(svg.Locator(".result-plot-equal")).ToHaveCountAsync(1);
        var point = svg.Locator(".result-plot-point.is-athlete");
        Assert.Equal(AlbuminCapTestData.Age, double.Parse((await point.GetAttributeAsync("data-age"))!, CultureInfo.InvariantCulture), 8);
        var expected = clock == "pheno" ? PhenoAgeHelper.CalculatePhenoAge(AlbuminCapTestData.PhenoValues(66))
            : BortzAgeHelper.CalculateBortzAgeFromRaw(AlbuminCapTestData.Age, AlbuminCapTestData.BortzValues(66));
        Assert.Equal(expected, double.Parse((await point.GetAttributeAsync("data-value"))!, CultureInfo.InvariantCulture), 6);
        await Expect(page.Locator(clock == "pheno" ? "#lowestPhenoAge" : "#lowestBortzAge"))
            .ToHaveTextAsync(expected.ToString("F1", CultureInfo.InvariantCulture));
        await Expect(point).ToHaveAttributeAsync("data-date", "2026-01-01");
        await Expect(svg.Locator("text")).ToContainTextAsync(["Chronological age (years)"]);
        await Expect(page.Locator(".result-field-count")).ToHaveTextAsync("2 results · 2 athletes");
        await svg.Locator(".result-plot-point:not(.is-athlete)").ClickAsync();
        await Expect(page.Locator(".result-plot-readout")).ToContainTextAsync("Valerie Orsoni");
        await svg.PressAsync("Home");
        await Expect(page.Locator(".result-plot-readout")).ToContainTextAsync("Benjamin Garden");
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForFunctionAsync("""
            () => { const svg = document.querySelector('.result-plot svg'); return svg && Math.abs(svg.viewBox.baseVal.width - svg.parentElement.clientWidth) < 1; }
            """);
        Assert.True(await page.Locator("#biomarkerResultList").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
        var table = page.Locator(".result-detail-row:not([hidden]) .biomarker-history-table");
        await Expect(table.Locator("tbody td").Last).ToHaveAttributeAsync("aria-label", "Not available");
    }

    [Fact]
    public async Task LatestRecordedValues_KeepTheirDatesAndLeaveMissingMeasurementsAsGaps()
    {
        await using var context = await NewContextAsync(390, true);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 66, partial: true);
        athlete["Biomarkers"]!.AsArray().Add(new JsonObject
        {
            ["Date"] = "2026-03-01", ["AlbGL"] = 63, ["CreatUmolL"] = 94.66, ["CrpMgL"] = 0.2, ["Wbc1000cellsuL"] = 0
        });
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var albumin = Row(page, "Albumin (g/L)");
        await Expect(albumin.Locator(".biomarker-overview-current")).ToHaveTextAsync("63");
        await Expect(albumin.Locator(".biomarker-overview-result .biomarker-history-unit")).ToHaveTextAsync("66");
        await Expect(albumin.Locator(".biomarker-overview-change span").First).ToHaveTextAsync("-3");
        await Expect(Row(page, "Creatinine (µmol/L)").Locator(".biomarker-overview-change span").First).ToHaveTextAsync("+6.26");
        await Expect(Row(page, "CRP (mg/L)").Locator(".biomarker-overview-change span").First).ToHaveTextAsync("-0.6");
        await Expect(Row(page, "WBC (10^3 cells/µL)").Locator(".biomarker-overview-current")).ToHaveTextAsync("0");
        await Expect(Row(page, "Glucose (mmol/L)").Locator("time")).ToHaveAttributeAsync("datetime", "2026-01-01");
        Assert.Equal(0, await albumin.Locator("svg line").CountAsync());
        await albumin.GetByRole(AriaRole.Button).ClickAsync();
        await Expect(page.Locator(".result-plot svg .result-plot-point")).ToHaveCountAsync(2);
        Assert.Equal(0, await page.Locator(".result-plot-trend").CountAsync());
        await Expect(page.Locator(".result-detail-row:not([hidden]) .biomarker-history-table tbody td").Nth(1)).ToHaveTextAsync("—");
    }

    [Fact]
    public async Task IncompleteClockPanel_RemainsUnavailableInsteadOfGeneratingAResult()
    {
        await using var context = await NewContextAsync(390, true);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 66, partial: false);
        athlete["Biomarkers"]![0]!.AsObject().Remove("CystatinCMgL");
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Row(page, "bortz").GetByRole(AriaRole.Button)).ToBeDisabledAsync();
        await Expect(Row(page, "bortz").Locator(".biomarker-overview-current")).ToHaveTextAsync("—");
        await Row(page, "pheno").GetByRole(AriaRole.Button).ClickAsync();
        await Expect(page.Locator(".result-plot-point")).ToHaveCountAsync(1);
        await Expect(page.Locator(".result-field-count")).ToHaveTextAsync("1 result · 1 athlete");
    }

    [Fact]
    public async Task ClockChange_EqualsTheDifferenceBetweenDisplayedReadouts()
    {
        await using var context = await NewContextAsync(390, true);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 66, partial: false);
        var next = athlete["Biomarkers"]![0]!.DeepClone().AsObject();
        next["Date"] = "2026-08-03";
        next["AlbGL"] = 42;
        athlete["Biomarkers"]!.AsArray().Add(next);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        foreach (var clock in new[] { "bortz", "pheno" })
        {
            var row = Row(page, clock);
            await Expect(row.GetByRole(AriaRole.Button)).ToBeEnabledAsync();
            var current = decimal.Parse(await row.Locator(".biomarker-overview-current").InnerTextAsync(), CultureInfo.InvariantCulture);
            var baseline = decimal.Parse(await row.Locator(".biomarker-overview-result .biomarker-history-unit").InnerTextAsync(), CultureInfo.InvariantCulture);
            var change = decimal.Parse(await row.Locator(".biomarker-overview-change span").First.InnerTextAsync(), CultureInfo.InvariantCulture);
            Assert.Equal(current - baseline, change);
        }
    }

    [Fact]
    public async Task SingleMeasurement_DoesNotInventABaselineOrChange()
    {
        await using var context = await NewContextAsync(390, false);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 66, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var row = Row(page, "Albumin (g/L)");
        await Expect(row.Locator(".biomarker-overview-current")).ToHaveTextAsync("66");
        await Expect(row.Locator(".biomarker-overview-result .biomarker-history-unit")).ToHaveTextAsync("—");
        await Expect(row.Locator(".biomarker-overview-change span").First).ToHaveTextAsync("—");
        await row.GetByRole(AriaRole.Button).ClickAsync();
        await Expect(page.Locator(".result-plot-point")).ToHaveCountAsync(1);
        Assert.True(await page.Locator(".result-plot-point").EvaluateAsync<bool>("el => Number.isFinite(Number(el.getAttribute('cx'))) && Number.isFinite(Number(el.getAttribute('cy')))"));
    }

    [Fact]
    public async Task SwitchingAthletes_ClearsExpandedHistoryAndReplacesValues()
    {
        await using var context = await NewContextAsync(390, true);
        var first = await HistoryAthleteAsync("Benjamin Garden", 66, partial: true);
        var second = await HistoryAthleteAsync("Valerie Orsoni", 43, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ({new JsonArray(first, second).ToJsonString()});");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Link, new() { Name = "View stats of Benjamin Garden", Exact = true }).First.ClickAsync();
        await Row(page, "Albumin (g/L)").GetByRole(AriaRole.Button).ClickAsync();
        await Expect(page.Locator(".result-detail-row:not([hidden]) .biomarker-history-table tbody tr")).ToHaveCountAsync(2);
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "View stats of Valerie Orsoni", Exact = true }).First.ClickAsync();
        await Expect(page.Locator(".biomarker-overview-table caption")).ToContainTextAsync("Valerie Orsoni");
        await Expect(page.Locator("#athlete-biomarkers [aria-expanded=true]")).ToHaveCountAsync(0);
        await Row(page, "Albumin (g/L)").GetByRole(AriaRole.Button).ClickAsync();
        var table = page.Locator(".result-detail-row:not([hidden]) .biomarker-history-table");
        await Expect(table.Locator("caption")).ToContainTextAsync("Valerie Orsoni");
        await Expect(table.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(table.Locator("tbody td")).ToHaveTextAsync("43");
    }

    private static ILocator Row(IPage page, string key) => page.Locator($"#athlete-biomarkers tr[data-measure='{key}']");

    private async Task<IBrowserContext> NewContextAsync(int width, bool dark, string timezone = "UTC")
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, Locale = "en-GB", TimezoneId = timezone, ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        return context;
    }

    private async Task<JsonObject> HistoryAthleteAsync(string name, double albumin, bool partial)
    {
        using var client = App.CreateClient();
        var athletes = (await client.GetFromJsonAsync<JsonArray>("/api/data/athletes"))!;
        var athlete = athletes.Single(item => item?["Name"]?.GetValue<string>() == name)!.DeepClone().AsObject();
        athlete["DateOfBirth"] = JsonSerializer.SerializeToNode(AlbuminCapTestData.DateOfBirth);
        athlete["Biomarkers"] = new JsonArray(JsonSerializer.SerializeToNode(AlbuminCapTestData.Marker(albumin)));
        if (partial) athlete["Biomarkers"]!.AsArray().Add(new JsonObject { ["Date"] = "2026-02-01", ["CrpMgL"] = 0 });
        return athlete;
    }
}
