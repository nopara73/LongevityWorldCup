using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class BioageUpdateDraftBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Athlete = """
        {
            "Name":"Draft Test Athlete", "AccountEmail":"draft@example.test", "DateOfBirth":{"Year":1980,"Month":5,"Day":20},
            "Biomarkers":[{
                "Date":"2026-06-01", "AlbGL":45, "AlpUL":83, "AltUL":22, "ApoA1GL":1.52,
                "CholesterolMmolL":5.6, "CreatUmolL":72, "CrpMgL":1.35, "CystatinCMgL":0.9,
                "GluMmolL":5.1, "GgtUL":29, "Hba1cMmolMol":35.5, "LymPc":28.6,
                "MchPg":31.8, "McvFL":92, "MonocytePc":7.2, "NeutrophilPc":64.2,
                "Rbc10e12L":4.5, "RdwPc":13.4, "ShbgNmolL":45.6, "UreaMmolL":5.4,
                "VitaminDNmolL":50, "Wbc1000cellsuL":6.54
            }]
        }
        """;

    [Theory]
    [InlineData("pheno", 390)]
    [InlineData("pheno", 1280)]
    [InlineData("bortz", 390)]
    [InlineData("bortz", 1280)]
    public async Task UpdateDraft_RestoresRawValuesAndCalculatesThemAfterReloadAndProofReturn(string clock, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await SelectAthleteAsync(page);
        await OpenCalculatorAsync(page, clock);
        await page.Locator("#blood-draw-date").FillAsync("2026-09-01");
        await FillMarkerAsync(page, "wbc", "5.2");
        await FillMarkerAsync(page, "glucose", "90.08", "18.016");
        await page.ReloadAsync();
        await WaitForEntryAsync(page);
        await AssertRawDraftAsync(page);
        await Assertions.Expect(page.Locator("#albumin")).ToHaveValueAsync("");

        await page.Locator("#calculateBioageButton").ClickAsync();
        await page.WaitForSelectorAsync($"#{clock}AgeResult.show");
        await page.Locator("#continueButton").ClickAsync();
        await page.WaitForURLAsync("**/proofs");
        var stored = await page.EvaluateAsync<JsonElement>("() => JSON.parse(sessionStorage.getItem('biomarkerData')).Biomarkers[0]");
        Assert.Equal("2026-09-01", stored.GetProperty("Date").GetString());
        Assert.Equal(5.2, stored.GetProperty("Wbc1000cellsuL").GetDouble(), 6);
        Assert.Equal(5, stored.GetProperty("GluMmolL").GetDouble(), 6);
        Assert.Equal(45, stored.GetProperty("AlbGL").GetDouble());
        Assert.Equal($"bioageDraft:{clock}:update:Draft%20Test%20Athlete:v1", await page.EvaluateAsync<string>("() => sessionStorage.getItem('biomarkerDraftKey')"));

        await page.GoBackAsync();
        await WaitForEntryAsync(page);
        await AssertRawDraftAsync(page);
        // Cached-page lifecycle must not refill converted handoff values into raw inputs.
        await page.EvaluateAsync("() => window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }))");
        await AssertRawDraftAsync(page);
        await FillMarkerAsync(page, "wbc", "");
        await page.ReloadAsync();
        await WaitForEntryAsync(page);
        await Assertions.Expect(page.Locator("#wbc")).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator("#glucose")).ToHaveValueAsync("90.08");
    }

    [Fact]
    public async Task UpdateDrafts_StaySeparateByAthleteClockAndNewApplication()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await SelectAthleteAsync(page);
        await OpenCalculatorAsync(page, "pheno");
        await FillMarkerAsync(page, "wbc", "5.2");
        await page.GotoAsync("/dashboard");
        await page.EvaluateAsync("() => {const a=JSON.parse(sessionStorage.getItem('selectedAthlete')); a.Name='Other Athlete'; a.DateOfBirth.Year=1990; sessionStorage.setItem('selectedAthlete',JSON.stringify(a));}");
        await OpenCalculatorAsync(page, "pheno");
        await Assertions.Expect(page.Locator("#wbc")).ToHaveValueAsync("");
        await FillMarkerAsync(page, "wbc", "7.3");
        await OpenCalculatorAsync(page, "bortz");
        await Assertions.Expect(page.Locator("#wbc")).ToHaveValueAsync("");
        await FillMarkerAsync(page, "wbc", "6.1");
        await page.GotoAsync("/dashboard");
        await SelectAthleteAsync(page);
        await OpenCalculatorAsync(page, "pheno");
        await Assertions.Expect(page.Locator("#wbc")).ToHaveValueAsync("5.2");
        await Assertions.Expect(page.Locator("#dob-year")).ToHaveValueAsync("1980");
        await page.GotoAsync("/pheno-age");
        await WaitForEntryAsync(page);
        await Assertions.Expect(page.Locator("#wbc")).ToHaveValueAsync("");
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task SuccessfulResultSubmission_ClearsOnlyItsOriginatingDraft(string clock)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 844 }, ReducedMotion = ReducedMotion.Reduce });
        var submissions = 0;
        await context.RouteAsync("**/api/application/application", async route =>
        {
            submissions++;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = """{"success":true,"paymentRequired":false}""" });
        });
        var page = await context.NewPageAsync();
        await SelectAthleteAsync(page);
        await OpenCalculatorAsync(page, clock);
        await page.Locator("#blood-draw-date").FillAsync("2026-09-01");
        await FillMarkerAsync(page, "wbc", "5.2");
        await page.Locator("#calculateBioageButton").ClickAsync();
        await page.WaitForSelectorAsync($"#{clock}AgeResult.show");
        await page.Locator("#continueButton").ClickAsync();
        await page.WaitForURLAsync("**/proofs");
        var draftKey = (await page.EvaluateAsync<string>("() => sessionStorage.getItem('biomarkerDraftKey')"))!;
        await page.WaitForFunctionAsync("key => sessionStorage.getItem(key) !== null", draftKey);
        var otherDraftKey = $"bioageDraft:{clock}:update:Other%20Athlete:v1";
        await page.EvaluateAsync("key => sessionStorage.setItem(key, 'unrelated draft')", otherDraftKey);
        await page.EvaluateAsync("() => sessionStorage.setItem('bioageDraft:pheno:v1', 'unfinished new application')");
        await page.WaitForFunctionAsync("() => document.querySelector('#uploadProofButton')?.dataset.listener === 'true'");
        var proof = await page.EvaluateAsync<string>("() => {const c=document.createElement('canvas'); c.width=600; c.height=400; const x=c.getContext('2d'); x.fillStyle='white'; x.fillRect(0,0,600,400); x.fillStyle='black'; x.font='24px sans-serif'; x.fillText('Synthetic test proof',20,50); return c.toDataURL('image/png').split(',')[1];}");
        await page.Locator("#proofPicInput").SetInputFilesAsync(new FilePayload { Name = "test-proof.png", MimeType = "image/png", Buffer = Convert.FromBase64String(proof) });
        await page.WaitForSelectorAsync("#proofImageContainer img");
        await page.EvaluateAsync("() => document.querySelectorAll('.biomarker-checkbox').forEach(box => {box.checked=true; box.dispatchEvent(new Event('change',{bubbles:true}));})");
        await page.Locator("#submitButton").ClickAsync();
        await page.WaitForFunctionAsync("key => sessionStorage.getItem(key) === null", draftKey);
        Assert.Equal(1, submissions);
        Assert.Null(await page.EvaluateAsync<string?>("() => sessionStorage.getItem('biomarkerDraftKey')"));
        Assert.Equal("unrelated draft", await page.EvaluateAsync<string>("key => sessionStorage.getItem(key)", otherDraftKey));
        Assert.Equal("unfinished new application", await page.EvaluateAsync<string>("() => sessionStorage.getItem('bioageDraft:pheno:v1')"));
    }

    [Theory]
    [InlineData("pheno", false)]
    [InlineData("bortz", false)]
    [InlineData("pheno", true)]
    [InlineData("bortz", true)]
    public async Task ExplicitUpdatePrefill_TakesPrecedenceOverAnUnfinishedDraft(string clock, bool calculated)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await SelectAthleteAsync(page);
        await OpenCalculatorAsync(page, clock);
        await FillMarkerAsync(page, "wbc", "5.2");
        if (calculated)
        {
            await page.Locator("#blood-draw-date").FillAsync("2026-09-01");
            await page.Locator("#calculateBioageButton").ClickAsync();
            await page.WaitForSelectorAsync($"#{clock}AgeResult.show");
            await page.Locator("#continueButton").ClickAsync();
            await page.WaitForURLAsync("**/proofs");
        }
        await page.GotoAsync($"/{clock}-age?update=1&Wbc1000cellsuL=7.3&Date=2026-09-02");
        await WaitForEntryAsync(page);
        await Assertions.Expect(page.Locator("#wbc")).ToHaveValueAsync("7.3");
        await Assertions.Expect(page.Locator("#blood-draw-date")).ToHaveValueAsync("2026-09-02");
    }

    private static async Task SelectAthleteAsync(IPage page)
    {
        if (page.Url == "about:blank") await page.GotoAsync("/dashboard");
        await page.EvaluateAsync("athlete => sessionStorage.setItem('selectedAthlete', athlete)", Athlete);
    }

    private static async Task OpenCalculatorAsync(IPage page, string clock)
    {
        await page.GotoAsync($"/{clock}-age?update=1");
        await WaitForEntryAsync(page);
        await Assertions.Expect(page.Locator("#lwc-step-2")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lwc-step--visible"));
    }

    private static Task WaitForEntryAsync(IPage page) => page.WaitForFunctionAsync("() => document.querySelector('.bioageform')?.classList.contains('bioage-biomarker-entry-ready')");

    private static async Task FillMarkerAsync(IPage page, string id, string value, string? unit = null)
    {
        var input = page.Locator($"#{id}");
        if (!await input.IsVisibleAsync()) await input.Locator("xpath=ancestor::*[contains(concat(' ',normalize-space(@class),' '),' biomarker-card ')]").Locator(".biomarker-card-header").ClickAsync();
        if (unit is not null) await page.Locator($"#{id}Unit").SelectOptionAsync(unit);
        await input.FillAsync(value);
    }

    private static async Task AssertRawDraftAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("#blood-draw-date")).ToHaveValueAsync("2026-09-01");
        await Assertions.Expect(page.Locator("#wbc")).ToHaveValueAsync("5.2");
        await Assertions.Expect(page.Locator("#glucose")).ToHaveValueAsync("90.08");
        await Assertions.Expect(page.Locator("#glucoseUnit")).ToHaveValueAsync("18.016");
        await Assertions.Expect(page.Locator("#glucose").Locator("xpath=ancestor::*[contains(concat(' ',normalize-space(@class),' '),' biomarker-card ')]"))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bactive\b"));
    }
}
