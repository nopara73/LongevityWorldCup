using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class FlowProgressBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task ApplicationSubProgress_KeepsOneNumberPerStepThroughoutTheFlow()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        for (var stage = 2; stage <= 6; stage++)
        {
            await page.WaitForFunctionAsync("() => !document.getElementById('nextButton')?.disabled");
            await page.Locator("#nextButton").ClickAsync();
            await page.WaitForFunctionAsync(
                "stage => document.body.dataset.convergenceStage === String(stage)", stage);

            var markers = await page.Locator("#subProgressContainerItem").AriaSnapshotAsync();
            Assert.Equal("- text: 1 2 3 4 5", markers.Trim());
        }
    }
}
