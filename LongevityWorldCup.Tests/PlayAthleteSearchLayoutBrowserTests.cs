using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayAthleteSearchLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1280, 720)]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task AthleteSearch_ShowsEveryMatchWithItsPortraitAboveTheActions(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(input).ToBeEditableAsync();
        await page.WaitForFunctionAsync("() => !document.body.classList.contains('play-route-hydrating')");
        await input.FillAsync("a");
        await Assertions.Expect(page.Locator(".autocomplete-items [role=option]").First).ToBeVisibleAsync();
        await page.EvaluateAsync("async () => { window.LwcFlowActionDock?.refreshNow(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const list = document.querySelector('.autocomplete-items');
                const options = Array.from(list.querySelectorAll('[role=option]'));
                const actions = document.querySelector('.play-athlete-actions').getBoundingClientRect();
                const listBox = list.getBoundingClientRect();
                if (options.length < 3) issues.push(`only ${options.length} matches`);
                for (const option of options) {
                    const box = option.getBoundingClientRect();
                    const name = option.textContent.trim();
                    const thumb = option.querySelector('img');
                    if (!thumb) issues.push(`${name}: no portrait`);
                    else if (thumb.getAttribute('alt') !== '') issues.push(`${name}: portrait should be decorative`);
                    if (box.bottom > listBox.bottom + 1) continue; // scrolled inside the list
                    if (box.bottom > innerHeight + 1) issues.push(`${name}: below the screen`);
                    if (box.bottom > actions.top + 1 && box.top < actions.bottom - 1) issues.push(`${name}: hidden behind the actions`);
                }
                const visible = options.filter(option => option.getBoundingClientRect().bottom <= Math.min(listBox.bottom, innerHeight, actions.top) + 1);
                if (visible.length < Math.min(3, options.length)) issues.push(`only ${visible.length} matches visible`);
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }
}
