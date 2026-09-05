using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Controllers;
using LongevityWorldCup.Website.Tools;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class AlbuminCapBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task CalculatorAndRankPreview_CapAlbuminAfterUnitConversion(string clock)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-GB", TimezoneId = "UTC",
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var athletes = new JsonArray(AlbuminCapTestData.Athlete(54, "Ada"), AlbuminCapTestData.Athlete(66, "Zed"));
        await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ({athletes.ToJsonString()});");
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync($"/{clock}-age", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#dob-year").SelectOptionAsync("1980");
        await page.Locator("#dob-month").SelectOptionAsync("1");
        await page.Locator("#dob-day").SelectOptionAsync("1");
        await page.Locator("#blood-draw-date").FillAsync("2026-01-01");
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await Expect(page.Locator("#lwc-step-2")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lwc-step--visible"));

        var fields = FormFields(clock);
        await page.EvaluateAsync(
            """
            fields => {
                for (const [id, value] of Object.entries(fields)) {
                    const element = document.getElementById(id);
                    if (!element) throw new Error(`Missing calculator field: ${id}`);
                    element.value = value;
                    element.dispatchEvent(new Event('input', { bubbles: true }));
                    element.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }
            """, fields);
        await page.Locator("#calculateBioageButton").ClickAsync();

        using var client = App.CreateClient();
        var atCap = clock == "pheno"
            ? PhenoAgeHelper.CalculatePhenoAge(AlbuminCapTestData.PhenoValues(54))
            : BortzAgeHelper.CalculateBortzAgeFromRaw(AlbuminCapTestData.Age, AlbuminCapTestData.BortzValues(54));

        foreach (var (value, unit, rawAlbumin) in new[]
        {
            ("53", "1", 53d), ("54", "1", 54d), ("55", "1", 55d), ("66", "1", 66d),
            ("5.3", "0.1", 53d), ("5.4", "0.1", 54d), ("6.6", "0.1", 66d)
        })
        {
            await page.EvaluateAsync(
                """
                ({value, unit}) => {
                    document.getElementById('albumin').value = value;
                    document.getElementById('albuminUnit').value = unit;
                    calculateResult();
                }
                """, new { value, unit });

            var apiAge = await CalculateApiAsync(client, clock, rawAlbumin);
            var browserAge = await page.EvaluateAsync<double>(clock == "pheno"
                ? "() => lastCalculatedPhenoAge"
                : "() => lastCalculatedBortzAge");
            Assert.Equal(apiAge, browserAge, precision: 10);
            if (rawAlbumin >= 54)
                Assert.Equal(atCap, browserAge, precision: 10);
            else
                Assert.True(browserAge > atCap);

            // Bortz onboarding also computes pheno age from the same entered laboratory values.
            Assert.Equal(PhenoAgeHelper.CalculatePhenoAge(AlbuminCapTestData.PhenoValues(rawAlbumin)),
                await page.EvaluateAsync<double>("() => lastCalculatedPhenoAge"), precision: 10);
            await Expect(page.Locator("#animatedAge")).ToHaveTextAsync(apiAge.ToString("F1", CultureInfo.InvariantCulture));
            await Expect(page.Locator("#albumin")).ToHaveValueAsync(value);
            await Expect(page.Locator("#albuminUnit")).ToHaveValueAsync(unit);

            // Ada (54) and Zed (66) tie. With the same DOB, the established name tie-break places You between them.
            var preview = page.Locator($"#{clock}AgeRankPreview");
            await Expect(preview.Locator(".bioage-rank-number")).ToHaveTextAsync(rawAlbumin >= 54 ? "#2" : "#3");
            var names = await preview.Locator(".bioage-rank-row-name").AllTextContentsAsync();
            Assert.Equal(rawAlbumin >= 54 ? new[] { "Ada", "You", "Zed" } : new[] { "Ada", "Zed", "You" }, names);
        }

        await page.Locator("#continueButton").ClickAsync();
        await page.WaitForDomContentLoadedUrlAsync("**/apply");
        Assert.Equal(66, await page.EvaluateAsync<double>(
            "() => JSON.parse(sessionStorage.getItem('biomarkerData')).Biomarkers[0].AlbGL"), precision: 10);
        Assert.Empty(errors);
    }

    private static async Task<double> CalculateApiAsync(HttpClient client, string clock, double albumin)
    {
        var request = JsonSerializer.SerializeToNode(AlbuminCapTestData.Marker(albumin))!.AsObject();
        request["ChronologicalAge"] = AlbuminCapTestData.Age;
        using var response = await client.PostAsJsonAsync($"/api/data/{clock}-age", request);
        response.EnsureSuccessStatusCode();
        if (clock == "bortz")
            return (await response.Content.ReadFromJsonAsync<BortzAgeCalculationResult>())!.BiologicalAge;

        var result = (await response.Content.ReadFromJsonAsync<PhenoAgeCalculationResult>())!;
        Assert.Equal(PhenoAgeHelper.CalculateLiverPhenoAgeContributor(AlbuminCapTestData.PhenoValues(albumin)),
            result.DomainContributions.Liver, precision: 10);
        return result.BiologicalAge;
    }

    private static Dictionary<string, string> FormFields(string clock)
    {
        var fields = new Dictionary<string, string>
        {
            ["albumin"] = "53", ["wbc"] = "6.3", ["mcv"] = "96.7", ["creatinine"] = "88.4",
            ["glucose"] = "5.05", ["crp"] = "0.8",
            [clock == "pheno" ? "lymphocyte" : "lymphocyte_percentage"] = "25.5",
            [clock == "pheno" ? "rcdw" : "rdw"] = "12.5",
            [clock == "pheno" ? "ap" : "alp"] = "51"
        };
        if (clock == "bortz")
        {
            foreach (var (id, value) in new[]
            {
                ("urea", "5.4"), ("cholesterol", "4.8"), ("cystatin_c", "0.85"), ("hba1c", "34"),
                ("ggt", "22"), ("rbc", "4.7"), ("monocyte_percentage", "7.2"), ("neutrophil_percentage", "58.1"),
                ("alt", "24"), ("shbg", "45"), ("vitamin_d", "85"), ("mch", "31.5"), ("apoa1", "1.55")
            }) fields[id] = value;
        }
        foreach (var id in fields.Keys.ToArray()) fields[id + "Unit"] = "1";
        fields["crpUnit"] = clock == "pheno" ? "10" : "1";
        return fields;
    }
}
