using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class FlowProgressBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task ApplicationSubProgress_TracksEachOfTheSevenStepsInBothDirections(int width, int height)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await page.WaitForFunctionAsync("() => !document.getElementById('nextButton')?.disabled");
        await AssertProgressAsync(1);

        for (var stage = 2; stage <= 7; stage++)
        {
            await page.WaitForFunctionAsync("() => !document.getElementById('nextButton')?.disabled");
            await page.Locator("#nextButton").ClickAsync();
            await page.WaitForFunctionAsync(
                "stage => document.body.dataset.convergenceStage === String(stage)", stage);

            await AssertProgressAsync(stage);
        }

        for (var stage = 6; stage >= 1; stage--)
        {
            await page.Locator("#backButton").ClickAsync();
            await page.WaitForFunctionAsync(
                "stage => document.body.dataset.convergenceStage === String(stage)", stage);
            await AssertProgressAsync(stage);
        }

        async Task AssertProgressAsync(int stage)
        {
            Assert.Equal(7, await page.Locator("#subProgressContainerItem .stage").CountAsync());
            Assert.Equal($"subStage{stage}", await page.Locator("#subProgressContainerItem [aria-current=step]").GetAttributeAsync("id"));
            Assert.Equal(stage - 1, await page.Locator("#subProgressContainerItem .completed").CountAsync());
            var markers = await page.Locator("#subProgressContainerItem").AriaSnapshotAsync();
            Assert.Equal("- text: 1 2 3 4 5 6 7", markers.Trim());
        }
    }
}
