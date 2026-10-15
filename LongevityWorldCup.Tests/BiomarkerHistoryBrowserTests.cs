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
    [InlineData(320, false, 100)]
    [InlineData(320, true, 200)]
    public async Task Table_PreservesRawValuesUnitsAndClockResults_WithoutAChartDependency(int width, bool dark, int textPercent)
    {
        await using var context = await NewContextAsync(width, dark);
        var athlete = await HistoryAthleteAsync("Benjamin Garden", 66, partial: true);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Expect(page.Locator("#biomarkerTableTab")).ToBeEnabledAsync();
        await page.Locator("#biomarkerTableTab").ClickAsync();
        var table = page.Locator(".biomarker-history-table");
        await Expect(table).ToBeVisibleAsync();
        if (textPercent != 100)
            await page.AddStyleTagAsync(new() { Content = $"html {{ font-size:{textPercent}% !important; }}" });
        await Expect(table.Locator("tbody tr")).ToHaveCountAsync(2);
        await Expect(table.Locator("time").Nth(0)).ToHaveAttributeAsync("datetime", "2026-01-01");
        await Expect(table.Locator("time").Nth(1)).ToHaveAttributeAsync("datetime", "2026-02-01");

        var complete = table.Locator("tbody tr").First;
        var pheno = PhenoAgeHelper.CalculatePhenoAge(AlbuminCapTestData.PhenoValues(66));
        var bortz = BortzAgeHelper.CalculateBortzAgeFromRaw(AlbuminCapTestData.Age, AlbuminCapTestData.BortzValues(66));
        await Expect(complete.Locator("td").Nth(0)).ToHaveTextAsync(pheno.ToString("F1", CultureInfo.InvariantCulture));
        await Expect(complete.Locator("td").Nth(1)).ToHaveTextAsync(bortz.ToString("F1", CultureInfo.InvariantCulture));
        var partial = table.Locator("tbody tr").Nth(1);
        await Expect(partial.Locator("td").Nth(0)).ToHaveAttributeAsync("aria-label", "Not available");
        await Expect(partial.Locator("td").Nth(1)).ToHaveAttributeAsync("aria-label", "Not available");

        // Check the original lab result, including albumin above the scoring cap,
        // rather than deriving the displayed values back from normalized chart points.
        foreach (var (label, unit, value) in new[]
        {
            ("Albumin", "g/L", "66"), ("Creatinine", "µmol/L", "88.4"),
            ("Glucose", "mmol/L", "5.05"), ("CRP", "mg/L", "0.8"),
            ("Lymphocytes", "%", "25.5"), ("RBC", "10¹²/L", "4.7"),
            ("Vitamin D", "nmol/L", "85"), ("HbA1c", "mmol/mol", "34")
        })
        {
            await page.Locator("#biomarkerOverlaySelect").SelectOptionAsync($"{label} ({unit})");
            await Expect(table.Locator("thead th").Last).ToHaveTextAsync(label + unit);
            await Expect(complete.Locator("td").Last).ToHaveTextAsync(value);
            await Expect(partial.Locator("td").Last).ToHaveTextAsync(label == "CRP" ? "0" : "—");
        }

        await page.Locator("#biomarkerOverlaySelect").SelectOptionAsync("Creatinine (µmol/L)");
        await Expect(table.Locator("caption")).ToContainTextAsync("Benjamin Garden");
        Assert.Equal(2, await table.Locator("tbody th[scope='row']").CountAsync());
        Assert.Equal(4, await table.Locator("thead th[scope='col']").CountAsync());
        Assert.True(await complete.Locator("td").EvaluateAllAsync<bool>("""
            cells => cells.every(cell => {
                const range = document.createRange();
                range.selectNodeContents(cell);
                return range.getClientRects().length === 1;
            })
            """));
        Assert.True(await table.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"));
        await Expect(page.Locator("#biomarkerChartTab")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task SwitchingAthletes_ReplacesHistoryAndKeepsTheSelectedMarker()
    {
        await using var context = await NewContextAsync(390, true);
        var first = await HistoryAthleteAsync("Benjamin Garden", 66, partial: true);
        var second = await HistoryAthleteAsync("Valerie Orsoni", 43, partial: false);
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ({new JsonArray(first, second).ToJsonString()});");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Link, new() { Name = "View stats of Benjamin Garden", Exact = true }).First.ClickAsync();
        await Expect(page.Locator("#biomarkerTableTab")).ToBeEnabledAsync();
        await page.Locator("#biomarkerTableTab").ClickAsync();
        await page.Locator("#biomarkerOverlaySelect").SelectOptionAsync("Albumin (g/L)");
        await Expect(page.Locator(".biomarker-history-table tbody tr")).ToHaveCountAsync(2);
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "View stats of Valerie Orsoni", Exact = true }).First.ClickAsync();
        await Expect(page.Locator(".biomarker-history-table caption")).ToContainTextAsync("Valerie Orsoni");
        await Expect(page.Locator("#biomarkerTablePanel")).ToBeVisibleAsync();
        await Expect(page.Locator(".biomarker-history-table tbody tr")).ToHaveCountAsync(1);
        await Expect(page.Locator(".biomarker-history-table tbody td").Last).ToHaveTextAsync("43");
        await Expect(page.Locator("#biomarkerOverlaySelect")).ToHaveValueAsync("Albumin (g/L)");
    }

    private async Task<IBrowserContext> NewContextAsync(int width, bool dark)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            Locale = "en-GB", TimezoneId = "UTC", ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true'); localStorage.setItem('biomarkerVisibilityState', '{broken');");
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
