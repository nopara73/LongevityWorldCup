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
        await Expect(focusedButton).ToBeFocusedAsync();
        await Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        Assert.True(await focusedButton.EvaluateAsync<bool>("el => getComputedStyle(el).outlineStyle === 'none' && getComputedStyle(el).boxShadow === 'none'"));
        await Expect(Row(page, "HbA1c (mmol/mol)")).Not.ToHaveCSSAsync("background-color", "rgba(0, 0, 0, 0)");
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
        Assert.Equal(0, await albumin.Locator("svg line:not(.sparkline-track):not(.sparkline-tick)").CountAsync());
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
    [InlineData(195, false)]
    [InlineData(160, true)]
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
        Assert.Equal("Chronological age (years)", await page.EvaluateAsync<string>("() => { const text = Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas')).options.scales.x.title.text; return Array.isArray(text) ? text.join(' ') : text; }"));
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
            await page.WaitForFunctionAsync("() => Chart.getChart(document.querySelector('.result-detail-row:not([hidden]) canvas')).tooltip.opacity === 0");
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
            Assert.True(await row.Locator(".clock-history-scroll").EvaluateAsync<bool>("el => { const bounds = el.getBoundingClientRect(); return bounds.height >= 44 && bounds.width >= 44; }"));
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

    [Theory]
    [InlineData(1440, false, false, 100)]
    [InlineData(768, true, false, 100)]
    [InlineData(390, true, true, 100)]
    [InlineData(320, false, false, 100)]
    [InlineData(1440, true, false, 200)]
    [InlineData(768, false, false, 200)]
    [InlineData(390, false, true, 200)]
    [InlineData(320, true, true, 200)]
    public async Task RadarCard_OpensWithTheSameHeaderBandAsItsSiblingCards(int width, bool dark, bool touch, int textPercent)
    {
        await using var context = await NewContextAsync(width, dark, touch: touch);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 44, partial: false);
        var peer = await HistoryAthleteAsync("Valerie Orsoni", 44, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ({new JsonArray(athlete, peer).ToJsonString()});");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.Chart?.getChart(document.getElementById('ageRadarChart')) && document.querySelector('#detailsModal .biomarkers-heading')");
        await page.EvaluateAsync("() => document.fonts.ready");
        var normalTabFontSize = await page.Locator("#ageTabBortz").EvaluateAsync<double>("el => parseFloat(getComputedStyle(el).fontSize)");
        if (textPercent != 100)
            await page.AddStyleTagAsync(new() { Content = $"html {{ font-size:{textPercent}% !important; }}" });

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.getElementById('ageVisualization').getBoundingClientRect();
                const band = document.getElementById('ageClockSwitch');
                const bandBox = band.getBoundingClientRect();
                const sibling = document.querySelector('#detailsModal .biomarkers-heading');
                const siblingStyle = getComputedStyle(sibling);
                const bandStyle = getComputedStyle(band);
                const tabs = [...band.querySelectorAll('[role="tab"]:not([hidden])')];
                const tablist = getComputedStyle(band.querySelector('[role="tablist"]'));
                const title = getComputedStyle(sibling.querySelector('h3'));
                return [card.left, card.right, card.top, bandBox.left, bandBox.right, bandBox.top, bandBox.height,
                    sibling.getBoundingClientRect().height,
                    bandStyle.borderBottomWidth === siblingStyle.borderBottomWidth && bandStyle.backgroundColor === siblingStyle.backgroundColor ? 1 : 0,
                    tablist.borderTopWidth === '0px' && tablist.backgroundColor === 'rgba(0, 0, 0, 0)' ? 1 : 0,
                    tabs.length, tabs.every(tab => getComputedStyle(tab).textTransform === title.textTransform
                        && getComputedStyle(tab).fontWeight === title.fontWeight) ? 1 : 0];
            }
            """);
        Assert.True(Math.Abs(layout[3] - layout[0]) <= 2 && Math.Abs(layout[4] - layout[1]) <= 2 && Math.Abs(layout[5] - layout[2]) <= 2,
            $"{width}px: the clock tabs should be the card's full-width header band, not a switch inset in it.");
        if (textPercent == 100)
            Assert.True(Math.Abs(layout[6] - layout[7]) <= 2, $"{width}px: the radar band ({layout[6]:F0}px) should match the sibling cards' bands ({layout[7]:F0}px).");
        Assert.True(layout[8] == 1, $"{width}px: the radar band should share the sibling bands' surface and hairline.");
        Assert.True(layout[9] == 1, $"{width}px: the clock tabs should not sit in their own bordered box.");
        Assert.True(layout[10] == 2 && layout[11] == 1, $"{width}px: both clock tabs should read as card titles.");
        var tabFontSize = await page.Locator("#ageTabBortz").EvaluateAsync<double>("el => parseFloat(getComputedStyle(el).fontSize)");
        Assert.True(tabFontSize >= normalTabFontSize * textPercent / 100 - .1, "The rendered tab text must actually enlarge.");
        var problems = await page.Locator("#ageClockSwitch [role='tab']:not([hidden])").EvaluateAllAsync<string[]>(
            """
            tabs => tabs.flatMap(tab => {
                const bounds = tab.getBoundingClientRect();
                const band = tab.closest('.age-clock-switch').getBoundingClientRect();
                const range = document.createRange(); range.selectNodeContents(tab);
                const problems = [];
                if (bounds.width < 44 || bounds.height < 44) problems.push('target below 44px');
                if (bounds.left < band.left || bounds.right > band.right || bounds.top < band.top || bounds.bottom > band.bottom)
                    problems.push('target escapes header band');
                if ([...range.getClientRects()].some(rect => rect.width > 0 &&
                    (rect.left < bounds.left - 1 || rect.right > bounds.right + 1 || rect.top < bounds.top - 1 || rect.bottom > bounds.bottom + 1)))
                    problems.push('label escapes target');
                return problems.map(problem => `${tab.textContent}: ${problem}`);
            })
            """);
        Assert.True(problems.Length == 0, $"{width}px at {textPercent}%: {string.Join("; ", problems)}");
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
        await page.Locator("#ageTabBortz").PressAsync("ArrowRight");
        await Expect(page.Locator("#ageTabPheno")).ToBeFocusedAsync();
        await Expect(page.Locator("#ageTabPheno")).ToHaveAttributeAsync("aria-selected", "true");
        Assert.True(await page.Locator("#ageTabPheno").EvaluateAsync<bool>("el => getComputedStyle(el).outlineStyle !== 'none' && parseFloat(getComputedStyle(el).outlineWidth) >= 2"));
    }

    [Theory]
    [InlineData(1280, true, 100, "UTC")]
    [InlineData(390, false, 100, "America/Los_Angeles")]
    [InlineData(320, true, 200, "America/Los_Angeles")]
    public async Task RadarHistory_SelectsOneCompleteBloodDrawAndRemembersEachClock(int width, bool direct, int textPercent, string timezone)
    {
        await using var context = await NewContextAsync(width, true, timezone);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 44, partial: false);
        (string Date, double Albumin)[] panels = [("2025-01-01", 50), ("2025-06-01", 38), ("2026-01-01", 44), ("2026-02-01", 45)];
        var markers = new JsonArray(panels.Select(panel =>
        {
            var marker = JsonSerializer.SerializeToNode(AlbuminCapTestData.Marker(panel.Albumin))!.AsObject();
            marker["Date"] = panel.Date;
            return (JsonNode)marker;
        }).ToArray());
        // This panel supports Pheno only; the later partial panel supports neither clock.
        markers[3]!.AsObject().Remove("CystatinCMgL");
        markers.Add(new JsonObject { ["Date"] = "2026-03-01", ["CreatUmolL"] = 80 });
        athlete["Biomarkers"] = markers;
        var peer = await HistoryAthleteAsync("Valerie Orsoni", 44, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ({new JsonArray(athlete, peer).ToJsonString()});");
        var page = await context.NewPageAsync();
        await page.GotoAsync(direct ? "/athlete/benjamin-garden" : "/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (!direct) await page.GetByRole(AriaRole.Link, new() { Name = "View stats of Benjamin Garden", Exact = true }).First.ClickAsync();
        await page.WaitForFunctionAsync("() => window.Chart?.getChart(document.getElementById('ageRadarChart'))");
        if (textPercent != 100) await page.AddStyleTagAsync(new() { Content = $"html {{ font-size:{textPercent}% !important; }}" });
        var select = page.Locator("#ageResultSelect");
        var chartId = await page.EvaluateAsync<int>("() => Chart.getChart(document.getElementById('ageRadarChart')).id");
        foreach (var clock in new[] { "bortz", "pheno" })
        {
            await page.Locator(clock == "bortz" ? "#ageTabBortz" : "#ageTabPheno").ClickAsync();
            var count = clock == "bortz" ? 3 : 4;
            await Expect(select.Locator("option")).ToHaveCountAsync(count);
            var expected = panels.Take(count).Select(panel =>
            {
                var date = DateTime.ParseExact(panel.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                var age = (date - new DateTime(1980, 1, 1)).TotalDays / 365.2425;
                var values = clock == "bortz" ? AlbuminCapTestData.BortzValues(panel.Albumin) : AlbuminCapTestData.PhenoValues(panel.Albumin);
                values[0] = age;
                return clock == "bortz" ? BortzAgeHelper.CalculateBortzAgeFromRaw(age, values) : PhenoAgeHelper.CalculatePhenoAge(values);
            }).ToArray();
            await Expect(select).ToHaveValueAsync(Array.IndexOf(expected, expected.Min()).ToString(CultureInfo.InvariantCulture));
            string? firstShape = null;
            for (var i = 0; i < count; i++)
            {
                await select.SelectOptionAsync(i.ToString(CultureInfo.InvariantCulture));
                await AssertRadarDrawingBoundsAsync(page);
                await Expect(page.Locator("#ageRadarChart")).ToHaveAttributeAsync("data-result-date", panels[i].Date);
                await Expect(page.Locator("#ageResultDate")).ToHaveAttributeAsync("datetime", panels[i].Date);
                await Expect(page.Locator("#ageRadarCenterBio")).ToHaveTextAsync($"biological age: {expected[i].ToString("F1", CultureInfo.InvariantCulture)}");
                var dateLabel = DateTime.ParseExact(panels[i].Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("d MMM yyyy", CultureInfo.InvariantCulture);
                await Expect(page.Locator("#ageResultCurrent")).ToHaveTextAsync(dateLabel);
                await Expect(select.Locator("option").Nth(i)).ToHaveTextAsync($"{dateLabel} · {expected[i].ToString("F1", CultureInfo.InvariantCulture)} years");
                var chart = await page.EvaluateAsync<JsonElement>("""
                    () => { const chart = Chart.getChart(document.getElementById('ageRadarChart'));
                        return { id: chart.id, shape: chart.data.datasets[0].data, contributors: chart._radarTooltipContributors.flat() }; }
                    """);
                Assert.Equal(chartId, chart.GetProperty("id").GetInt32());
                Assert.Contains($"Albumin: {panels[i].Albumin} g/L", chart.GetProperty("contributors").EnumerateArray().Select(item => item.GetString()));
                if (i == 0) firstShape = chart.GetProperty("shape").GetRawText();
                if (i == 1) Assert.NotEqual(firstShape, chart.GetProperty("shape").GetRawText());
            }
            await select.SelectOptionAsync("1");
        }
        await page.Locator("#ageTabPheno").PressAsync("ArrowLeft");
        await Expect(page.Locator("#ageTabBortz")).ToBeFocusedAsync();
        await Expect(select).ToHaveValueAsync("1");
        if (width > 480)
        {
            await page.Locator("#ageResultEarlier").ClickAsync();
            await Expect(select).ToHaveValueAsync("0");
            await Expect(page.Locator("#ageResultEarlier")).ToBeDisabledAsync();
            await page.Locator("#ageResultLater").ClickAsync();
            await Expect(select).ToHaveValueAsync("1");
        }
        await page.Locator("#ageTabBortz").PressAsync("ArrowRight");
        await Expect(page.Locator("#ageTabPheno")).ToBeFocusedAsync();
        await Expect(select).ToHaveValueAsync("1");
        Assert.True(await page.Locator("#ageResultCurrent").EvaluateAsync<bool>("""
            el => { const bounds = el.getBoundingClientRect(); const range = document.createRange(); range.selectNodeContents(el);
                return el.scrollWidth <= el.clientWidth + 1 && [...range.getClientRects()].every(rect => rect.left >= bounds.left - 1 && rect.right <= bounds.right + 1); }
            """));
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
        Assert.True(await page.Locator("#ageClockSwitch [role='tab']").EvaluateAllAsync<bool>("""
            tabs => tabs.every(tab => { const range = document.createRange(); range.selectNodeContents(tab);
                return [...range.getClientRects()].length <= 2; })
            """));
        var directory = Environment.GetEnvironmentVariable("LWC_UI_ARTIFACT_DIR");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            await page.Locator("#ageTabPheno").EvaluateAsync("el => el.blur()");
            var radar = page.Locator("#ageVisualization");
            await radar.EvaluateAsync("""
                el => { const scroller = el.closest('.modal-content'); scroller.scrollTo({
                    top: scroller.scrollTop + el.getBoundingClientRect().top - scroller.getBoundingClientRect().top - 80,
                    behavior: 'instant' }); }
                """);
            await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
            var bounds = (await radar.BoundingBoxAsync())!;
            var top = Math.Max(0, bounds.Y);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"radar-history-{width}-{textPercent}.png"),
                Clip = new() { X = Math.Max(0, bounds.X), Y = top, Width = bounds.Width, Height = Math.Min(bounds.Height, page.ViewportSize!.Height - top) } });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RadarHistory_DoesNotOfferMissingClocksOrSingleResultControls(bool noClock)
    {
        await using var context = await NewContextAsync(390, true);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 44, partial: false);
        var marker = athlete["Biomarkers"]![0]!.AsObject();
        marker.Remove("CystatinCMgL");
        if (noClock) marker.Remove("GluMmolL");
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Row(page, "Albumin (g/L)").GetByRole(AriaRole.Button)).ToBeEnabledAsync();
        if (noClock) await Expect(page.Locator("#ageVisualization")).ToBeHiddenAsync();
        else
        {
            await page.WaitForFunctionAsync("() => window.Chart?.getChart(document.getElementById('ageRadarChart'))");
            await Expect(page.Locator("#ageRadarChart")).ToHaveAttributeAsync("data-clock", "pheno");
            await Expect(page.Locator("#ageResultDate")).ToHaveTextAsync("1 Jan 2026");
            await Expect(page.Locator("#ageResultSelect")).ToBeHiddenAsync();
            await Expect(page.Locator("#ageResultEarlier")).ToBeHiddenAsync();
            await Expect(page.Locator("#ageResultLater")).ToBeHiddenAsync();
        }
        if (noClock) await Expect(page.Locator("#ageClockSwitch")).ToBeHiddenAsync();
        else
        {
            await Expect(page.Locator("#ageTabBortz")).ToBeHiddenAsync();
            await Expect(page.Locator("#ageTabPheno")).ToBeVisibleAsync();
            Assert.Equal("none", await page.Locator("#ageTabPheno").EvaluateAsync<string>("el => getComputedStyle(el).boxShadow"));
        }
        Assert.True(await page.Locator("#ageClockSwitch [role='tab']").EvaluateAllAsync<bool>(
            """
            tabs => tabs.filter(tab => !tab.checkVisibility()).every(tab => {
                tab.focus();
                return document.activeElement !== tab;
            })
            """));
    }

    [Fact]
    public async Task RadarHistory_DateSelectionStillWorksWithoutTheChartLibrary()
    {
        await using var context = await NewContextAsync(390, true);
        await context.RouteAsync("https://cdn.jsdelivr.net/npm/chart.js@4.6.1", route => route.FulfillAsync(new() { ContentType = "application/javascript", Body = "" }));
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 44, partial: false);
        var earlier = JsonSerializer.SerializeToNode(AlbuminCapTestData.Marker(50))!.AsObject();
        earlier["Date"] = "2025-01-01";
        athlete["Biomarkers"]!.AsArray().Insert(0, earlier);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(page.Locator("#ageRadarFallback")).ToBeVisibleAsync();
        await Expect(page.Locator("#ageRadarChart")).ToBeHiddenAsync();
        await page.Locator("#ageResultSelect").SelectOptionAsync("1");
        await Expect(page.Locator("#ageResultDate")).ToHaveTextAsync("1 Jan 2026");
        await Expect(page.Locator("#ageRadarFallbackBio")).ToHaveTextAsync($"biological age: {BortzAgeHelper.CalculateBortzAgeFromRaw(AlbuminCapTestData.Age, AlbuminCapTestData.BortzValues(44)).ToString("F1", CultureInfo.InvariantCulture)}");
        await page.Locator("#ageTabPheno").ClickAsync();
        await page.Locator("#ageResultSelect").SelectOptionAsync("1");
        await Expect(page.Locator("#ageRadarFallbackBio")).ToHaveTextAsync($"biological age: {PhenoAgeHelper.CalculatePhenoAge(AlbuminCapTestData.PhenoValues(44)).ToString("F1", CultureInfo.InvariantCulture)}");
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
        var chartScript = ChartScriptPath();
        await context.RouteAsync("https://cdn.jsdelivr.net/npm/chart.js@4.6.1", route => allowChart
            ? route.FulfillAsync(new() { ContentType = "application/javascript", Path = chartScript })
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

    private static string ChartScriptPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "chart.umd.js");
        if (!File.Exists(path))
            throw new FileNotFoundException("Build the browser test assets after restoring frontend dependencies.", path);
        return path;
    }

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

    [Theory]
    [InlineData(1440, false)]
    [InlineData(768, false)]
    [InlineData(390, true)]
    [InlineData(320, true)]
    public async Task ResultsOverTime_KeepsShortBiomarkersCompactAndLetsLongLabelsWrap(int width, bool touch)
    {
        await using var context = await NewContextAsync(width, dark: true, touch: touch);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 66, partial: true);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(Row(page, "bortz").GetByRole(AriaRole.Button)).ToBeEnabledAsync();
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.Locator("#athlete-biomarkers .result-summary:not(.result-clock)").First.ScrollIntoViewIfNeededAsync();

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const biomarkers = [...document.querySelectorAll('#athlete-biomarkers .result-summary:not(.result-clock)')];
                const clock = document.querySelector('#athlete-biomarkers .result-summary.result-clock');
                if (!biomarkers.length) return ['no biomarker rows'];
                for (const row of biomarkers) {
                    const button = row.querySelector('button');
                    const range = document.createRange();
                    range.selectNodeContents(button.firstChild);
                    const name = range.getBoundingClientRect();
                    const unit = row.querySelector('.biomarker-history-unit').getBoundingClientRect();
                    const label = button.firstChild.textContent.trim();
                    const bounds = button.getBoundingClientRect();
                    if (bounds.height < 44 || bounds.width < 44) problems.push(`${label}: target below 44px`);
                    range.selectNodeContents(button);
                    const cell = button.closest('th').getBoundingClientRect();
                    if ([...range.getClientRects()].some(rect => rect.width > 0 && (rect.left < cell.left - 1 || rect.right > cell.right + 1 || rect.top < bounds.top - 1 || rect.bottom > bounds.bottom + 1)))
                        problems.push(`${label}: label escapes its target or cell`);
                    // These short labels fit even at 320px. Character count alone cannot predict the width of other labels.
                    if (['ALP', 'ALT', 'CRP'].includes(label)) {
                        if (row.getBoundingClientRect().height > 56) problems.push(`${label}: short row lost its compact layout`);
                        if (Math.abs(unit.bottom - name.bottom) > 4 || unit.left < name.right) problems.push(`${label}: unit is not beside its name`);
                    }
                }
                const size = row => parseFloat(getComputedStyle(row.querySelector('.biomarker-overview-current')).fontSize);
                if (size(clock) <= size(biomarkers[0])) problems.push('clocks no longer lead the biomarkers');
                if (clock.getBoundingClientRect().height <= biomarkers[0].getBoundingClientRect().height) problems.push('clock rows lost their room');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px: {string.Join("; ", problems)}");
    }

    [Theory]
    [InlineData(1440, true, true)]
    [InlineData(768, false, false)]
    [InlineData(390, true, false)]
    [InlineData(390, false, true)]
    [InlineData(320, true, true)]
    [InlineData(320, false, false)]
    public async Task ResultsOverTime_EnlargesRealTextAndKeepsEachRowsContentAssociated(int width, bool direct, bool dark)
    {
        await using var context = await NewContextAsync(width, dark, touch: width <= 390);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 66, partial: true);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync(direct ? "/athlete/benjamin-garden" : "/about", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (!direct)
        {
            await page.WaitForFunctionAsync("() => typeof window.openAthleteModalBySlug === 'function'");
            await page.EvaluateAsync("() => window.openAthleteModalBySlug('benjamin-garden')");
        }
        await Expect(Row(page, "bortz").GetByRole(AriaRole.Button)).ToBeEnabledAsync();
        await page.EvaluateAsync("() => document.fonts.ready");

        var normal = await ResultTextMetricsAsync(page);
        await page.AddStyleTagAsync(new() { Content = "html { font-size:200% !important; }" });
        // Wait for actual layout and font growth, not merely a matching declaration.
        await page.WaitForFunctionAsync("() => parseFloat(getComputedStyle(document.querySelector('#athlete-biomarkers .biomarker-overview-current')).fontSize) > 30");
        var enlarged = await ResultTextMetricsAsync(page);
        Assert.Equal(normal.Length, enlarged.Length);
        for (var index = 0; index < normal.Length; index++)
        {
            Assert.Equal(normal[index].Key, enlarged[index].Key);
            Assert.Equal(normal[index].Content, enlarged[index].Content);
            Assert.True(enlarged[index].NameSize >= normal[index].NameSize * 1.9, enlarged[index].Key);
            Assert.True(enlarged[index].UnitSize >= normal[index].UnitSize * 1.9, enlarged[index].Key);
            Assert.True(enlarged[index].ValueSize >= normal[index].ValueSize * 1.9, enlarged[index].Key);
        }
        Assert.Contains(enlarged, row => row.Height > normal.Single(previous => previous.Key == row.Key).Height + 20);

        var problems = await page.Locator("#athlete-biomarkers .result-summary").EvaluateAllAsync<string[]>("""
            rows => rows.flatMap(row => {
                const problems = [];
                const button = row.querySelector('button');
                const bounds = button.getBoundingClientRect();
                if (bounds.width < 44 || bounds.height < 44) problems.push('target below 44px');
                for (const element of [button, row.querySelector('.biomarker-overview-current'), row.querySelector('.biomarker-overview-history svg')].filter(Boolean)) {
                    const cell = element.closest('th,td').getBoundingClientRect();
                    const range = document.createRange();
                    range.selectNodeContents(element);
                    const rects = element.tagName.toLowerCase() === 'svg' ? [element.getBoundingClientRect()] : [...range.getClientRects()];
                    if (rects.some(rect => rect.width > 0 && (rect.left < cell.left - 1 || rect.right > cell.right + 1 || rect.top < cell.top - 1 || rect.bottom > cell.bottom + 1)))
                        problems.push('content escapes its own cell');
                    if (element === button && rects.some(rect => rect.width > 0 && (rect.top < bounds.top - 1 || rect.bottom > bounds.bottom + 1)))
                        problems.push('label clipped by its button');
                }
                return problems.map(problem => `${row.dataset.measure}: ${problem}`);
            })
            """);
        Assert.True(problems.Length == 0, $"{width}px: {string.Join("; ", problems)}");
        // The narrow enlarged table may pan horizontally, while the dialog itself stays within its viewport.
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth + 1"));
        var albumin = Row(page, "Albumin (g/L)").GetByRole(AriaRole.Button);
        await albumin.PressAsync("Enter");
        await Expect(albumin).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(page.Locator(".result-detail-row:not([hidden]) .biomarker-history-table tbody td")).ToHaveTextAsync(["66", "—"]);
        await albumin.PressAsync("Escape");
        await Expect(albumin).ToBeFocusedAsync();
        await Expect(albumin).ToHaveAttributeAsync("aria-expanded", "false");
    }

    private sealed class ResultTextMetrics
    {
        public string Key { get; set; } = "";
        public string Content { get; set; } = "";
        public double NameSize { get; set; }
        public double UnitSize { get; set; }
        public double ValueSize { get; set; }
        public double Height { get; set; }
    }

    private static Task<ResultTextMetrics[]> ResultTextMetricsAsync(IPage page) =>
        page.Locator("#athlete-biomarkers .result-summary").EvaluateAllAsync<ResultTextMetrics[]>("""
            rows => rows.map(row => ({
                Key: row.dataset.measure,
                Content: row.textContent,
                NameSize: parseFloat(getComputedStyle(row.querySelector('button')).fontSize),
                UnitSize: parseFloat(getComputedStyle(row.querySelector('.biomarker-history-unit')).fontSize),
                ValueSize: parseFloat(getComputedStyle(row.querySelector('.biomarker-overview-current')).fontSize),
                Height: row.getBoundingClientRect().height
            }))
            """);

    private static ILocator Row(IPage page, string key) => page.Locator($"#athlete-biomarkers tr[data-measure='{key}']");

    [Theory]
    [InlineData(1440, false, 140)]
    [InlineData(1024, true, 140)]
    [InlineData(390, true, 88)]
    public async Task ProfileRadar_FillsItsCardInsteadOfFloatingInASquare(int width, bool dark, double minimumRadius)
    {
        await using var context = await NewContextAsync(width, dark, touch: width < 600);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 44, partial: false);
        var peer = await HistoryAthleteAsync("Valerie Orsoni", 44, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ({new JsonArray(athlete, peer).ToJsonString()});");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.Chart?.getChart(document.getElementById('ageRadarChart'))?.scales?.r?.drawingArea > 0");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const chart = Chart.getChart(document.getElementById('ageRadarChart'));
                const wrapper = document.getElementById('ageRadarWrapper').getBoundingClientRect();
                const card = document.getElementById('ageVisualization').getBoundingClientRect();
                return [chart.scales.r.drawingArea, wrapper.width, wrapper.height, wrapper.left, wrapper.right, card.left, card.right,
                    document.documentElement.scrollWidth, innerWidth];
            }
            """);
        Assert.True(layout[0] >= minimumRadius, $"{width}px: the radar web is only {layout[0]:F0}px in radius; it should be at least {minimumRadius}px.");
        if (width >= 769)
            Assert.True(layout[2] <= layout[1] * 0.75, $"{width}px: the radar well should be landscape, not a {layout[1]:F0}x{layout[2]:F0} square.");
        Assert.True(layout[3] >= layout[5] && layout[4] <= layout[6], $"{width}px: the radar should stay inside its card.");
        Assert.True(layout[7] <= layout[8], $"{width}px: the page scrolls sideways ({layout[7]} > {layout[8]}).");
        await AssertRadarDrawingBoundsAsync(page);
    }

    [Theory]
    [InlineData(700, 400, false)]
    [InlineData(760, 480, true)]
    [InlineData(844, 390, false)]
    public async Task ProfileRadar_ShortLandscapeKeepsTheChartWithinOneScreen(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(width, dark, touch: true);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 44, partial: false);
        var peer = await HistoryAthleteAsync("Valerie Orsoni", 44, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ({new JsonArray(athlete, peer).ToJsonString()});");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.Chart?.getChart(document.getElementById('ageRadarChart'))?.scales?.r?.drawingArea > 0");
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const wrapper = document.getElementById('ageRadarWrapper').getBoundingClientRect();
                const card = document.getElementById('ageVisualization').getBoundingClientRect();
                const chart = Chart.getChart(document.getElementById('ageRadarChart'));
                return [wrapper.height, wrapper.left, wrapper.right, card.left, card.right,
                    chart.scales.r.drawingArea, document.documentElement.scrollWidth, innerWidth, innerHeight];
            }
            """);
        Assert.True(layout[0] <= Math.Min(280, layout[8] * 0.7) + 1,
            $"{width}x{height}: the radar is {layout[0]}px tall and takes over the short dialog.");
        Assert.True(layout[1] >= layout[3] && layout[2] <= layout[4], "The radar must remain inside its card.");
        Assert.True(layout[5] >= 50, "The bounded chart must still render a readable radar.");
        Assert.True(layout[6] <= layout[7], "The profile must not scroll sideways.");
        await AssertRadarDrawingBoundsAsync(page);
    }

    [Fact]
    public async Task ProfileRadar_HiddenOpenResizeClockSwitchAndReopenKeepFreshGeometry()
    {
        await using var context = await NewContextAsync(390, dark: true, touch: true);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 44, partial: false);
        var peer = await HistoryAthleteAsync("Valerie Orsoni", 44, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ({new JsonArray(athlete, peer).ToJsonString()});");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        var opener = page.GetByRole(AriaRole.Link, new() { Name = "View stats of Benjamin Garden", Exact = true }).First;
        await opener.ClickAsync();
        foreach (var (width, height) in new[] { (390, 844), (700, 400), (1440, 600), (320, 720) })
        {
            await page.SetViewportSizeAsync(width, height);
            foreach (var clock in new[] { "#ageTabPheno", "#ageTabBortz" })
            {
                await page.Locator(clock).ClickAsync();
                await AssertRadarDrawingBoundsAsync(page);
            }
        }
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await opener.ClickAsync();
        await AssertRadarDrawingBoundsAsync(page);
    }

    private static async Task AssertRadarDrawingBoundsAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.WaitForFunctionAsync("""
            () => {
                const chart = window.Chart?.getChart(document.getElementById('ageRadarChart'));
                if (!chart?.scales?.r?.drawingArea) return false;
                const rect = chart.canvas.getBoundingClientRect();
                return Math.abs(chart.width - rect.width) <= 1 && Math.abs(chart.height - rect.height) <= 1;
            }
            """);
        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const chart = Chart.getChart(document.getElementById('ageRadarChart'));
                const scale = chart.scales.r;
                const problems = [];
                const inside = (left, top, right, bottom) =>
                    left >= -1 && top >= -1 && right <= chart.width + 1 && bottom <= chart.height + 1;
                chart.data.labels.forEach((label, index) => {
                    const box = scale.getPointLabelPosition(index);
                    if (!inside(box.left, box.top, box.right, box.bottom)) problems.push(`${label}: label box clipped`);
                    // Measure the current font's painted glyphs, not just the chart's fitting rectangle.
                    const item = scale._pointLabelItems[index];
                    const font = Chart.helpers.toFont(scale.options.pointLabels.setContext(scale.getPointLabelContext(index)).font);
                    const ctx = chart.ctx;
                    ctx.save();
                    ctx.font = font.string;
                    ctx.textAlign = item.textAlign;
                    ctx.textBaseline = 'middle';
                    const glyph = ctx.measureText(label);
                    const baseline = item.y + font.lineHeight / 2;
                    if (!inside(item.x - glyph.actualBoundingBoxLeft, baseline - glyph.actualBoundingBoxAscent,
                        item.x + glyph.actualBoundingBoxRight, baseline + glyph.actualBoundingBoxDescent))
                        problems.push(`${label}: painted glyphs clipped`);
                    ctx.restore();
                });
                const ratio = chart.currentDevicePixelRatio;
                if (Math.abs(chart.canvas.width - chart.width * ratio) > 1 ||
                    Math.abs(chart.canvas.height - chart.height * ratio) > 1) problems.push('canvas backing resolution is stale');
                if (scale.min !== 0 || scale.max !== 100 || scale.options.ticks.stepSize !== 20)
                    problems.push('percentile scale changed');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, string.Join("; ", problems));
    }

    private async Task<IBrowserContext> NewContextAsync(int width, bool dark, string timezone = "UTC", bool touch = false)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, Locale = "en-GB", TimezoneId = timezone, ReducedMotion = ReducedMotion.Reduce,
            HasTouch = touch, IsMobile = touch
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var chartScript = ChartScriptPath();
        await context.RouteAsync("https://cdn.jsdelivr.net/npm/chart.js@4.6.1", route => route.FulfillAsync(new()
        {
            ContentType = "application/javascript", Path = chartScript
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
