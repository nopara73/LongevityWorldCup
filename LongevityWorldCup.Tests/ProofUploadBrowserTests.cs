using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ProofUploadBrowserTests
{
    [Fact]
    public async Task ResultUpload_WaitsForDelayedProofHelperBeforeBindingUploadControls()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(browser, app);
        await RoutePageDependenciesAsync(context, delayProofHelper: true);

        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                Biomarkers: []
            }));
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [
                    { Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }
                ]
            }));
            """);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/play/proof-upload.html", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'");

        Assert.Contains("Browser Test Athlete", await page.Locator("#character-title").InnerTextAsync());
        Assert.Contains("Upload", await page.Locator("#mainProofInstructions").InnerHTMLAsync());
        Assert.Contains("proofs", await page.Locator("#mainProofInstructions").InnerHTMLAsync());
        Assert.True(await page.Locator("#submitButton").IsDisabledAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task OnboardingProofStage_WaitsForDelayedProofHelperBeforeBindingUploadControls()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(browser, app);
        await RoutePageDependenciesAsync(context, delayProofHelper: true);

        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: [
                    { Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }
                ]
            }));
            """);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/onboarding/convergence.html?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await AdvanceOnboardingStageAsync(page, "2. Finding your why");
        await AdvanceOnboardingStageAsync(page, "3. The price of glory");
        await AdvanceOnboardingStageAsync(page, "4/a. Almost there");
        await AdvanceOnboardingStageAsync(page, "4/b. Don't trust, verify");

        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'");

        Assert.Contains("Upload", await page.Locator("#mainProofInstructions").InnerHTMLAsync());
        Assert.Contains("proofs", await page.Locator("#mainProofInstructions").InnerHTMLAsync());
        Assert.Contains("Albumin", await page.Locator("#biomarker-checklist").InnerTextAsync());
        Assert.Contains("Glucose", await page.Locator("#biomarker-checklist").InnerTextAsync());
        Assert.True(await page.Locator("#nextButton").IsEnabledAsync());
        Assert.Empty(errors);
    }

    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    private static async Task<IBrowserContext> NewContextAsync(IBrowser browser, BrowserTestApp app)
    {
        return await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US"
        });
    }

    private static async Task RoutePageDependenciesAsync(IBrowserContext context, bool delayProofHelper)
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context, async uri =>
        {
            if (delayProofHelper && uri.AbsolutePath.Equals("/js/proof-helpers.js", StringComparison.OrdinalIgnoreCase))
                await Task.Delay(1200);
        });
    }

    private static async Task AdvanceOnboardingStageAsync(IPage page, string expectedHeading)
    {
        await page.WaitForFunctionAsync("() => !document.getElementById('nextButton')?.disabled");
        await page.Locator("#nextButton").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = expectedHeading }).WaitForAsync();
    }
}
