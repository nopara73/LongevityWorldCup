using Microsoft.Playwright;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class NewAthleteOnboardingBrowserTests
{
    [Fact]
    public async Task AmateurOnboarding_CarriesPhenoHandoffFromJoinToApplicationProofStage()
    {
        var bloodDrawDate = DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd");

        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Button, new() { Name = "Start amateur" }).ClickAsync();
        await page.WaitForURLAsync("**/pheno-age");

        Assert.Equal("/pheno-age", new Uri(page.Url).AbsolutePath);
        await AssertPendingPaymentOfferAsync(page);

        await page.Locator("button[onclick=\"navigateBackFromBioage()\"]").ClickAsync();
        await page.WaitForURLAsync("**/join");

        Assert.Equal("/join", new Uri(page.Url).AbsolutePath);
        await AssertPendingPaymentOfferAsync(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Start amateur" }).ClickAsync();
        await page.WaitForURLAsync("**/pheno-age");
        await FillAndCalculatePhenoAgeAsync(page, bloodDrawDate);

        await page.Locator("#continueButton").ClickAsync();
        await page.WaitForURLAsync("**/apply");
        await page.GetByRole(AriaRole.Heading, new() { Name = "1. Enter the arena" }).WaitForAsync();

        Assert.Equal("/apply", new Uri(page.Url).AbsolutePath);
        await AssertPhenoHandoffAsync(page, bloodDrawDate);

        await page.Locator("#backButton").ClickAsync();
        await page.WaitForURLAsync("**/pheno-age");

        Assert.Equal("/pheno-age", new Uri(page.Url).AbsolutePath);

        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.GetByRole(AriaRole.Heading, new() { Name = "1. Enter the arena" }).WaitForAsync();
        await AdvanceOnboardingStageAsync(page, "2. Finding your why");
        await AdvanceOnboardingStageAsync(page, "3. The price of glory");
        await AdvanceOnboardingStageAsync(page, "4/a. Almost there");
        await AdvanceOnboardingStageAsync(page, "4/b. Don't trust, verify");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'");

        var proofChecklist = await page.Locator("#biomarker-checklist").InnerTextAsync();
        Assert.Contains("Collection date", proofChecklist);
        Assert.Contains("Lab/report source", proofChecklist);
        Assert.Contains("White blood cell count (WBC)", proofChecklist);
        Assert.Contains("Lymphocytes", proofChecklist);
        Assert.Contains("Albumin", proofChecklist);
        Assert.Contains("Glucose", proofChecklist);
        Assert.Contains("C-reactive protein (CRP)", proofChecklist);
        Assert.True(await page.Locator("#nextButton").IsEnabledAsync());
        Assert.Empty(errors);
    }

    private static async Task FillAndCalculatePhenoAgeAsync(IPage page, string bloodDrawDate)
    {
        await page.Locator("#dob-year").SelectOptionAsync("1980");
        await page.Locator("#dob-month").SelectOptionAsync("5");
        await page.WaitForFunctionAsync(
            "() => Array.from(document.querySelector('#dob-day')?.options || []).some(option => option.value === '20')");
        await page.Locator("#dob-day").SelectOptionAsync("20");
        await page.Locator("#blood-draw-date").FillAsync(bloodDrawDate);
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");

        await page.EvaluateAsync(
            """
            values => {
                for (const [id, value] of Object.entries(values)) {
                    const element = document.getElementById(id);
                    if (!element) throw new Error(`Missing pheno age field: ${id}`);
                    element.value = value;
                    element.dispatchEvent(new Event('input', { bubbles: true }));
                    element.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }
            """,
            new Dictionary<string, string>
            {
                ["wbc"] = "6.54",
                ["wbcUnit"] = "1",
                ["lymphocyte"] = "28.6",
                ["lymphocyteUnit"] = "1",
                ["mcv"] = "92",
                ["mcvUnit"] = "1",
                ["rcdw"] = "13.4",
                ["rcdwUnit"] = "1",
                ["albumin"] = "45",
                ["albuminUnit"] = "1",
                ["ap"] = "83",
                ["apUnit"] = "1",
                ["creatinine"] = "72",
                ["creatinineUnit"] = "1",
                ["glucose"] = "5",
                ["glucoseUnit"] = "1",
                ["crp"] = "1.35",
                ["crpUnit"] = "10"
            });

        await page.Locator("#phenoAgeForm button[type=\"submit\"]").ClickAsync();
        await page.WaitForSelectorAsync("#phenoAgeResult.show");
        await page.WaitForSelectorAsync("#continueButton.show");
    }

    private static async Task AssertPendingPaymentOfferAsync(IPage page)
    {
        var offer = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const offer = JSON.parse(sessionStorage.getItem('pendingPaymentOffer') || '{}');
                return {
                    source: offer.source,
                    offerType: offer.offerType,
                    currency: offer.currency,
                    amountUsd: offer.amountUsd
                };
            }
            """);

        Assert.Equal("join-game", offer.GetProperty("source").GetString());
        Assert.Equal("amateur", offer.GetProperty("offerType").GetString());
        Assert.Equal("USD", offer.GetProperty("currency").GetString());
        Assert.Equal(10, offer.GetProperty("amountUsd").GetDouble());
    }

    private static async Task AssertPhenoHandoffAsync(IPage page, string bloodDrawDate)
    {
        var handoff = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const biomarkerData = JSON.parse(sessionStorage.getItem('biomarkerData') || '{}');
                const latest = biomarkerData.Biomarkers?.[0] || {};
                const offer = JSON.parse(sessionStorage.getItem('pendingPaymentOffer') || '{}');
                const chronoPhenoDifference = sessionStorage.getItem('chronoPhenoDifference');

                return {
                    offerType: offer.offerType,
                    amountUsd: offer.amountUsd,
                    lwcStep: sessionStorage.getItem('lwcStep'),
                    hasChronoPhenoDifference: !!(chronoPhenoDifference && chronoPhenoDifference.trim()),
                    chronoPhenoDifference: chronoPhenoDifference === null ? null : Number(chronoPhenoDifference),
                    dobYear: biomarkerData.DateOfBirth?.Year,
                    dobMonth: biomarkerData.DateOfBirth?.Month,
                    dobDay: biomarkerData.DateOfBirth?.Day,
                    markerDate: latest.Date,
                    albGL: latest.AlbGL,
                    alpUL: latest.AlpUL,
                    creatUmolL: latest.CreatUmolL,
                    crpMgL: latest.CrpMgL,
                    gluMmolL: latest.GluMmolL,
                    lymPc: latest.LymPc,
                    mcvFL: latest.McvFL,
                    rdwPc: latest.RdwPc,
                    wbc1000cellsuL: latest.Wbc1000cellsuL,
                    markerKeys: Object.keys(latest).filter(key => key !== 'Date').sort()
                };
            }
            """);

        Assert.Equal("amateur", handoff.GetProperty("offerType").GetString());
        Assert.Equal(10, handoff.GetProperty("amountUsd").GetDouble());
        Assert.Equal("1", handoff.GetProperty("lwcStep").GetString());
        Assert.True(handoff.GetProperty("hasChronoPhenoDifference").GetBoolean());
        Assert.True(double.IsFinite(handoff.GetProperty("chronoPhenoDifference").GetDouble()));
        Assert.Equal(1980, handoff.GetProperty("dobYear").GetInt32());
        Assert.Equal(5, handoff.GetProperty("dobMonth").GetInt32());
        Assert.Equal(20, handoff.GetProperty("dobDay").GetInt32());
        Assert.Equal(bloodDrawDate, handoff.GetProperty("markerDate").GetString());
        Assert.Equal(45, handoff.GetProperty("albGL").GetDouble());
        Assert.Equal(83, handoff.GetProperty("alpUL").GetDouble());
        Assert.Equal(72, handoff.GetProperty("creatUmolL").GetDouble());
        Assert.Equal(1.35, handoff.GetProperty("crpMgL").GetDouble(), 2);
        Assert.Equal(5, handoff.GetProperty("gluMmolL").GetDouble());
        Assert.Equal(28.6, handoff.GetProperty("lymPc").GetDouble(), 1);
        Assert.Equal(92, handoff.GetProperty("mcvFL").GetDouble());
        Assert.Equal(13.4, handoff.GetProperty("rdwPc").GetDouble(), 1);
        Assert.Equal(6.54, handoff.GetProperty("wbc1000cellsuL").GetDouble(), 2);

        var markerKeys = handoff
            .GetProperty("markerKeys")
            .EnumerateArray()
            .Select(key => key.GetString() ?? throw new InvalidOperationException("Marker key was not a string."))
            .ToArray();
        Assert.Equal(
            [
                "AlbGL",
                "AlpUL",
                "CreatUmolL",
                "CrpMgL",
                "GluMmolL",
                "LymPc",
                "McvFL",
                "RdwPc",
                "Wbc1000cellsuL"
            ],
            markerKeys);
    }

    private static async Task AdvanceOnboardingStageAsync(IPage page, string expectedHeading)
    {
        await page.WaitForFunctionAsync("() => !document.getElementById('nextButton')?.disabled");
        await page.Locator("#nextButton").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = expectedHeading }).WaitForAsync();
    }
}
