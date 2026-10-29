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
    [InlineData("/pheno-age", 160, 16)]
    [InlineData("/pheno-age", 195, 16)]
    [InlineData("/pheno-age", 320, 32)]
    [InlineData("/bortz-age", 160, 16)]
    [InlineData("/bortz-age", 195, 16)]
    [InlineData("/bortz-age", 320, 32)]
    [InlineData("/apply?fake=1", 160, 16)]
    [InlineData("/apply?fake=1", 195, 16)]
    [InlineData("/apply?fake=1", 320, 32)]
    public async Task JourneyLabels_KeepWholeWordsAtPhoneZoomAndEnlargedText(string path, int width, int rootSize)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = width, Height = 640 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync($"document.addEventListener('DOMContentLoaded', () => document.documentElement.style.fontSize = '{rootSize}px', {{ once: true }});");
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("() => document.fonts.ready");
        var issues = await page.Locator("#mainProgressBar .stage-label").EvaluateAllAsync<string[]>("""
            labels => labels.flatMap(label => {
                const range = document.createRange();
                range.selectNodeContents(label);
                const lines = [...range.getClientRects()];
                const box = label.getBoundingClientRect();
                const text = label.textContent.trim();
                const failures = [];
                if (new Set(lines.map(rect => Math.round(rect.top))).size !== 1) failures.push(text + ' splits across lines');
                if (lines.some(rect => rect.left < box.left - 1 || rect.right > box.right + 1)) failures.push(text + ' overflows its stage');
                if (parseFloat(getComputedStyle(label).fontSize) < parseFloat(getComputedStyle(document.documentElement).fontSize) * .75) failures.push(text + ' shrinks below the existing label size');
                return failures;
            })
            """);
        Assert.Equal(3, await page.Locator("#mainProgressBar .stage-label").CountAsync());
        Assert.Empty(issues);
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1"));
    }

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
