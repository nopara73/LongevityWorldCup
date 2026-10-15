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
        await Expect(section.Locator(".biomarker-overview-table > thead th")).ToHaveTextAsync(["Result", "History", "Latest"]);
        Assert.Equal(0, await section.Locator("select").CountAsync());
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
        foreach (var (name, unit, value, clickTarget) in new[]
        {
            ("Albumin", "g/L", "66", ".biomarker-overview-current"),
            ("Creatinine", "µmol/L", "88.4", ".biomarker-overview-result"),
            ("Glucose", "mmol/L", "5.05", ".biomarker-overview-current"),
            ("CRP", "mg/L", "0.8", ".biomarker-overview-history"),
            ("RBC", "10¹²/L", "4.7", ".biomarker-overview-history svg"),
            ("Vitamin D", "nmol/L", "85", ".biomarker-overview-arrow"),
            ("HbA1c", "mmol/mol", "34", "th")
        })
        {
            var row = Row(page, $"{name} ({unit})");
            var button = row.GetByRole(AriaRole.Button);
            await row.Locator(clickTarget).ClickAsync(clickTarget == "th" ? new() { Position = new() { X = 2, Y = 2 } } : null);
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
        await Row(page, "HbA1c (mmol/mol)").GetByRole(AriaRole.Button).PressAsync("Escape");
        await Expect(Row(page, "HbA1c (mmol/mol)").GetByRole(AriaRole.Button)).ToBeFocusedAsync();
        await Expect(section.Locator(".result-detail-row:not([hidden])")).ToHaveCountAsync(0);
        var focusedButton = Row(page, "HbA1c (mmol/mol)").GetByRole(AriaRole.Button);
        await focusedButton.PressAsync("Space");
        await Expect(focusedButton).ToHaveAttributeAsync("aria-expanded", "true");
        await focusedButton.PressAsync("Enter");
        await Expect(focusedButton).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        Assert.True(await focusedButton.EvaluateAsync<bool>("el => getComputedStyle(el).outlineStyle === 'none' && getComputedStyle(el).boxShadow === 'none'"));
        Assert.True(await Row(page, "HbA1c (mmol/mol)").EvaluateAsync<bool>("el => getComputedStyle(el).backgroundColor !== 'rgba(0, 0, 0, 0)'"));
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
        await WaitForChartAsync(page);
        var canvas = page.Locator($".result-plot canvas[data-clock='{clock}']");
        await Expect(canvas).ToBeVisibleAsync();
        Assert.Null(await canvas.GetAttributeAsync("tabindex"));
        var chart = await page.EvaluateAsync<JsonElement>("""
            () => { const c = Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas'));
                return { count: c.data.datasets.slice(0,2).flatMap(d => d.data).length,
                    own: c.data.datasets[0].data, reference: c.data.datasets[2].data, axis: c.options.scales.x.title.text,
                    minY: c.scales.y.min }; }
            """);
        Assert.Equal(2, chart.GetProperty("count").GetInt32());
        Assert.True(chart.GetProperty("minY").GetDouble() >= 0);
        Assert.Equal(1, chart.GetProperty("own").GetArrayLength());
        var point = chart.GetProperty("own")[0];
        Assert.Equal(AlbuminCapTestData.Age, point.GetProperty("x").GetDouble(), 8);
        var expected = clock == "pheno" ? PhenoAgeHelper.CalculatePhenoAge(AlbuminCapTestData.PhenoValues(66))
            : BortzAgeHelper.CalculateBortzAgeFromRaw(AlbuminCapTestData.Age, AlbuminCapTestData.BortzValues(66));
        Assert.Equal(expected, point.GetProperty("y").GetDouble(), 6);
        await Expect(page.Locator(clock == "pheno" ? "#lowestPhenoAge" : "#lowestBortzAge"))
            .ToHaveTextAsync(expected.ToString("F1", CultureInfo.InvariantCulture));
        Assert.Equal("2026-01-01", point.GetProperty("date").GetString());
        Assert.Equal("Chronological age (years)", chart.GetProperty("axis").GetString());
        Assert.All(chart.GetProperty("reference").EnumerateArray(), p => Assert.Equal(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble()));
        await Expect(page.Locator(".result-field-count")).ToHaveTextAsync("2 results · 2 athletes");
        await HoverPointAsync(page, 1, 0);
        await page.WaitForFunctionAsync("""
            () => { const c = Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas'));
                return c.tooltip.opacity > 0 && c.tooltip.title.join(' ').includes('Valerie Orsoni') && c.tooltip.beforeBody.includes('1 Jan 2026'); }
            """);
        Assert.False(await canvas.EvaluateAsync<bool>("el => el === document.activeElement"));
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForFunctionAsync("""
            () => { const canvas = document.querySelector('.result-detail-row:not([hidden]) canvas');
                const pane = document.querySelector('#biomarkerResultList');
                const modal = document.querySelector('#detailsModal .modal-content');
                return Math.abs(Chart.getChart(canvas).width - canvas.parentElement.clientWidth) < 1
                    && pane.scrollWidth <= pane.clientWidth + 1 && modal.scrollWidth <= modal.clientWidth + 1; }
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
        await Expect(Row(page, "WBC (10^3 cells/µL)").Locator(".biomarker-overview-current")).ToHaveTextAsync("0");
        await Expect(Row(page, "Glucose (mmol/L)").Locator("time")).ToHaveAttributeAsync("datetime", "2026-01-01");
        Assert.Equal(0, await albumin.Locator("svg line").CountAsync());
        await albumin.GetByRole(AriaRole.Button).ClickAsync();
        await WaitForChartAsync(page);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => { const c = Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas'));
                return c.config.type === 'scatter' && c.data.datasets[0].data.map(p => p.y).join(',') === '66,63'
                    && c.data.datasets[0].data.every(p => Number.isFinite(p.x) && Number.isFinite(p.y)); }
            """));
        await Expect(page.Locator(".result-detail-row:not([hidden]) .biomarker-history-table tbody td").Nth(1)).ToHaveTextAsync("—");
    }

    [Theory]
    [InlineData(true, 1280)]
    [InlineData(true, 390)]
    [InlineData(false, 390)]
    public async Task IncompleteClockPanels_OmitUnavailableClocksAndKeepMeasuredResults(bool hasPheno, int width)
    {
        await using var context = await NewContextAsync(width, true);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 66, partial: false);
        athlete["Biomarkers"]![0]!.AsObject().Remove("CystatinCMgL");
        if (!hasPheno) athlete["Biomarkers"]![0]!.AsObject().Remove("GluMmolL");
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var section = page.Locator("#athlete-biomarkers");
        await Expect(Row(page, "bortz")).ToHaveCountAsync(0);
        await Expect(Row(page, "pheno")).ToHaveCountAsync(hasPheno ? 1 : 0);
        await Expect(section.Locator(".result-summary").First).ToHaveAttributeAsync("data-measure", hasPheno ? "pheno" : "Albumin (g/L)");
        await Expect(Row(page, "Albumin (g/L)").Locator(".biomarker-overview-current")).ToHaveTextAsync("66");
        await Expect(section.Locator(".result-first-biomarker")).ToHaveCountAsync(hasPheno ? 1 : 0);
        if (hasPheno)
            await Expect(section.Locator(".result-first-biomarker")).ToHaveAttributeAsync("data-measure", "Albumin (g/L)");
        await Row(page, hasPheno ? "pheno" : "Albumin (g/L)").GetByRole(AriaRole.Button).ClickAsync();
        await WaitForChartAsync(page);
        Assert.Equal(1, await page.EvaluateAsync<int>("Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas')).data.datasets[0].data.length"));
        if (hasPheno) await Expect(page.Locator(".result-field-count")).ToHaveTextAsync("1 result · 1 athlete");
    }

    [Fact]
    public async Task SingleMeasurement_PlotsOneDatedValue()
    {
        await using var context = await NewContextAsync(390, false);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 66, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var row = Row(page, "Albumin (g/L)");
        await Expect(row.Locator(".biomarker-overview-current")).ToHaveTextAsync("66");
        await row.GetByRole(AriaRole.Button).ClickAsync();
        await WaitForChartAsync(page);
        Assert.Equal(1, await page.EvaluateAsync<int>("Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas')).data.datasets[0].data.length"));
        Assert.True(await page.EvaluateAsync<bool>("""
            () => { const point = Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas')).getDatasetMeta(0).data[0];
                return Number.isFinite(point.x) && Number.isFinite(point.y); }
            """));
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

    [Theory]
    [InlineData(1280, false)]
    [InlineData(390, false)]
    [InlineData(320, true)]
    public async Task RawComparison_HoverAndTouchTooltipsShowExactValuesAndStayInsideChart(int width, bool touch)
    {
        await using var context = await NewContextAsync(width, true, touch: touch);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 44, partial: false);
        var marker = athlete["Biomarkers"]![0]!.DeepClone();
        var dates = new[] { "2005-12-05", "2020-05-13", "2023-08-27", "2024-03-13", "2026-07-06" };
        var values = new[] { 20, 60, 46, 60, 26 };
        athlete["Biomarkers"] = new JsonArray(dates.Select((date, index) =>
        {
            var entry = marker!.DeepClone().AsObject();
            entry["Date"] = date;
            entry["AltUL"] = values[index];
            return (JsonNode)entry;
        }).ToArray());
        athlete["Biomarkers"]!.AsArray().Insert(0, new JsonObject { ["Date"] = "1997-10-31", ["CrpMgL"] = 0.2 });
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Row(page, "ALT (U/L)").Locator(".biomarker-overview-current").ClickAsync();
        await WaitForChartAsync(page);
        Assert.Equal("scatter", await page.EvaluateAsync<string>("Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas')).config.type"));
        Assert.Equal(5, await page.EvaluateAsync<int>("Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas')).data.datasets[0].data.length"));
        Assert.Equal("Chronological age (years)", await page.EvaluateAsync<string>("Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas')).options.scales.x.title.text"));
        foreach (var index in new[] { 0, 4 })
        {
            await HoverPointAsync(page, 0, index, touch);
            await page.WaitForFunctionAsync("""
                expected => { const c = Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas'));
                    return c.tooltip.opacity > 0 && c.tooltip.dataPoints[0]?.raw.date === expected; }
                """, dates[index]);
            var tooltip = await page.EvaluateAsync<JsonElement>("""
                () => { const c = Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas')); const t = c.tooltip;
                    return { value: t.body.flatMap(b => b.lines).join(' '), fits: t.x >= 0 && t.y >= 0
                        && t.x + t.width <= c.width + 1 && t.y + t.height <= c.height + 1,
                        focused: document.activeElement === c.canvas }; }
                """);
            Assert.Contains($"ALT: {values[index]} U/L", tooltip.GetProperty("value").GetString());
            Assert.True(tooltip.GetProperty("fits").GetBoolean());
            Assert.False(tooltip.GetProperty("focused").GetBoolean());
        }
        await SaveReviewScreenshotAsync(page.Locator(".result-detail-row:not([hidden]) .result-detail"), $"alt-scatter-{width}.png", Row(page, "ALT (U/L)"));
        if (!touch)
        {
            await page.Mouse.MoveAsync(0, 0);
            await page.WaitForFunctionAsync("Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas')).tooltip.opacity === 0");
        }
        Assert.True(await page.Locator("#biomarkerResultList").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
    }

    [Fact]
    public async Task EveryResult_UsesMeasuredCohortValuesAndHighlightsOnlyTheViewedAthlete()
    {
        await using var context = await NewContextAsync(390, true);
        var first = await HistoryAthleteAsync("Benjamin Garden", 66, partial: true);
        var second = await HistoryAthleteAsync("Valerie Orsoni", 43, partial: false);
        second["Biomarkers"]![0]!.AsObject().Remove("CystatinCMgL");
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ({new JsonArray(first, second).ToJsonString()});");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Row(page, "Albumin (g/L)")).ToBeVisibleAsync();
        var keys = await page.Locator("#athlete-biomarkers .result-summary").EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.measure)");
        Assert.Equal(24, keys.Length);
        foreach (var key in keys)
        {
            await Row(page, key).GetByRole(AriaRole.Button).ClickAsync();
            await WaitForChartAsync(page);
            var chart = await page.EvaluateAsync<JsonElement>("""
                () => { const c = Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas'));
                    return { type: c.config.type, own: c.data.datasets[0].data, others: c.data.datasets[1].data,
                        axis: c.options.scales.x.title.text, datasets: c.data.datasets.length }; }
                """);
            Assert.Equal("scatter", chart.GetProperty("type").GetString());
            Assert.Equal("Chronological age (years)", chart.GetProperty("axis").GetString());
            Assert.Equal(key == "CRP (mg/L)" ? 2 : 1, chart.GetProperty("own").GetArrayLength());
            Assert.Equal(key is "bortz" or "Cystatin C (mg/L)" ? 0 : 1, chart.GetProperty("others").GetArrayLength());
            Assert.Equal(key is "bortz" or "pheno" ? 3 : 2, chart.GetProperty("datasets").GetInt32());
            Assert.All(chart.GetProperty("own").EnumerateArray(), point =>
            {
                Assert.Equal("Benjamin Garden", point.GetProperty("name").GetString());
                Assert.True(point.GetProperty("isAthlete").GetBoolean());
                Assert.True(double.IsFinite(point.GetProperty("x").GetDouble()));
            });
            Assert.All(chart.GetProperty("others").EnumerateArray(), point =>
            {
                Assert.Equal("Valerie Orsoni", point.GetProperty("name").GetString());
                Assert.False(point.GetProperty("isAthlete").GetBoolean());
                Assert.Equal(AlbuminCapTestData.Age, point.GetProperty("x").GetDouble(), 8);
            });
            if (key == "Albumin (g/L)")
            {
                Assert.Equal(66, chart.GetProperty("own")[0].GetProperty("y").GetDouble());
                Assert.Equal(43, chart.GetProperty("others")[0].GetProperty("y").GetDouble());
            }
            if (key == "CRP (mg/L)")
                Assert.Equal(0, chart.GetProperty("own")[1].GetProperty("y").GetDouble());
        }
    }

    [Theory]
    [InlineData(1280, 2, true)]
    [InlineData(390, 2, true)]
    [InlineData(320, 2, true)]
    [InlineData(1280, 16, true)]
    [InlineData(390, 16, true)]
    [InlineData(320, 16, true)]
    [InlineData(390, 16, false)]
    public async Task ClockHistory_ShowsEveryRecordedAgeWithoutWideningTheTable(int width, int count, bool direct)
    {
        await using var context = await NewContextAsync(width, true);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 43, partial: false);
        var marker = athlete["Biomarkers"]![0]!.DeepClone();
        athlete["Biomarkers"] = new JsonArray(Enumerable.Range(0, count).Select(index =>
        {
            var entry = marker.DeepClone().AsObject();
            entry["Date"] = $"{2010 + index}-01-01";
            entry["AlbGL"] = 43 + index / 10.0;
            return (JsonNode)entry;
        }).ToArray());
        athlete["Biomarkers"]!.AsArray().Add(new JsonObject { ["Date"] = "2026-02-01", ["CrpMgL"] = 0 });
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync(direct ? "/athlete/benjamin-garden" : "/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (!direct) await page.GetByRole(AriaRole.Link, new() { Name = "View stats of Benjamin Garden", Exact = true }).First.ClickAsync();
        foreach (var clock in new[] { "bortz", "pheno" })
        {
            var row = Row(page, clock);
            var readings = row.Locator(".clock-history-reading");
            await Expect(readings).ToHaveCountAsync(count);
            await Expect(readings.First).ToHaveAttributeAsync("data-date", "2010-01-01");
            await Expect(readings.Last).ToHaveAttributeAsync("data-date", $"{2009 + count}-01-01");
            await Expect(row.Locator(".clock-history-reading.is-latest")).ToHaveTextAsync(await row.Locator(".biomarker-overview-current").InnerTextAsync());
            Assert.True(await row.Locator(".clock-history-scroll").EvaluateAsync<bool>("""
                el => { const viewport = el.getBoundingClientRect(); const last = el.querySelector('.is-latest').getBoundingClientRect();
                    return last.left >= viewport.left - 1 && last.right <= viewport.right + 1; }
                """));
            if (count == 2)
                Assert.True(await row.Locator(".clock-history-scroll").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
        }
        Assert.True(await page.Locator("#biomarkerResultList").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
        await SaveReviewScreenshotAsync(page.Locator("#athlete-biomarkers"), $"clock-history-{width}-{count}-{direct}.png");
    }

    private static async Task SaveReviewScreenshotAsync(ILocator section, string name, ILocator? summaryRow = null)
    {
        var directory = Environment.GetEnvironmentVariable("LWC_UI_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        if (summaryRow is null) await section.GetByRole(AriaRole.Heading).First.ScrollIntoViewIfNeededAsync();
        else await summaryRow.GetByRole(AriaRole.Button).ScrollIntoViewIfNeededAsync();
        var bounds = (await (summaryRow ?? section).BoundingBoxAsync())!;
        var viewport = section.Page.ViewportSize!;
        var left = Math.Max(0, bounds.X);
        var top = Math.Max(0, bounds.Y);
        await section.Page.ScreenshotAsync(new()
        {
            Path = Path.Combine(directory, name),
            Clip = new() { X = left, Y = top, Width = Math.Min(bounds.Width, viewport.Width - left), Height = Math.Min(500, viewport.Height - top) }
        });
    }

    [Fact]
    public async Task UnavailableChartLibrary_KeepsDatedReadingsAndOffersWorkingRetry()
    {
        await using var context = await NewContextAsync(390, true);
        var allowChart = false;
        await context.RouteAsync("https://cdn.jsdelivr.net/npm/chart.js@4.5.1", route => allowChart
            ? route.FulfillAsync(new() { ContentType = "application/javascript", Path = ChartScriptPath() })
            : route.AbortAsync());
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 44, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Row(page, "Albumin (g/L)").GetByRole(AriaRole.Button).ClickAsync();
        var retry = page.GetByRole(AriaRole.Button, new() { Name = "Retry chart", Exact = true });
        await Expect(retry).ToBeVisibleAsync();
        await Expect(page.Locator(".result-detail-row:not([hidden]) .biomarker-history-table tbody td")).ToHaveTextAsync("44");
        allowChart = true;
        await retry.ClickAsync();
        await WaitForChartAsync(page);
        await Expect(retry).ToHaveCountAsync(0);
    }

    private static string ChartScriptPath() => Path.Combine(AppContext.BaseDirectory, "Fixtures", "chart.umd.js");

    private static async Task WaitForChartAsync(IPage page) => await page.WaitForFunctionAsync("""
        () => { const canvas = document.querySelector('.result-detail-row:not([hidden]) canvas'); return canvas && window.Chart?.getChart(canvas); }
        """);

    private static async Task HoverPointAsync(IPage page, int dataset, int point, bool touch = false)
    {
        var canvas = page.Locator(".result-detail-row:not([hidden]) canvas");
        await canvas.ScrollIntoViewIfNeededAsync();
        var position = await canvas.EvaluateAsync<double[]>("""
            (canvas, indexes) => { const c = Chart.getChart(canvas); const point = c.getDatasetMeta(indexes[0]).data[indexes[1]];
                const r = canvas.getBoundingClientRect(); return [r.left + point.x + 3, r.top + point.y + 2]; }
            """, new[] { dataset, point });
        if (touch) await page.Touchscreen.TapAsync((float)position[0], (float)position[1]);
        else await page.Mouse.MoveAsync((float)position[0], (float)position[1]);
    }

    private static ILocator Row(IPage page, string key) => page.Locator($"#athlete-biomarkers tr[data-measure='{key}']");

    private async Task<IBrowserContext> NewContextAsync(int width, bool dark, string timezone = "UTC", bool touch = false)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, Locale = "en-GB", TimezoneId = timezone, ReducedMotion = ReducedMotion.Reduce,
            HasTouch = touch, IsMobile = touch
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("https://cdn.jsdelivr.net/npm/chart.js@4.5.1", route => route.FulfillAsync(new()
        {
            ContentType = "application/javascript", Path = ChartScriptPath()
        }));
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
