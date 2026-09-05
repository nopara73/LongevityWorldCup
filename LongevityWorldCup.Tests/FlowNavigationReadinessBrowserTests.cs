using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FlowNavigationReadinessBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/apply", "#backButton", "/pheno-age")]
    [InlineData("/pheno-age", "#bioageStepOneBackButton", "/join")]
    [InlineData("/bortz-age", "#bioageStepOneBackButton", "/join")]
    [InlineData("/edit-profile", ".flow-action-stack .back-button", "/dashboard")]
    [InlineData("/proofs", ".flow-action-stack .back-button", "/dashboard")]
    public async Task BackNavigation_WorksWhilePageModulesAreStillLoading(
        string path,
        string backSelector,
        string destination)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new() { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                Biomarkers: []
            }));
            sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [{ Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }]
            }));
            """);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var moduleRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseModule = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/js/misc.js*", async route =>
        {
            moduleRequested.TrySetResult();
            await releaseModule.Task;
            await route.ContinueAsync();
        });

        try
        {
            await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await moduleRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (path == "/apply")
                await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();

            await page.Locator(backSelector).ClickAsync();
            await page.WaitForDomContentLoadedUrlAsync($"**{destination}");

            Assert.Equal(destination, new Uri(page.Url).AbsolutePath);
            Assert.Empty(errors);
        }
        finally
        {
            releaseModule.TrySetResult();
        }
    }
}
