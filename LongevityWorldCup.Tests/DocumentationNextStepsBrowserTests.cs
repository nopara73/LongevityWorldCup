using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DocumentationNextStepsBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/about", 1440, false)]
    [InlineData("/history", 1440, true)]
    [InlineData("/ruleset", 1440, false)]
    [InlineData("/history", 390, false)]
    [InlineData("/about", 390, true)]
    public async Task WhereToGoNext_IsTheSameFullRowsOfTilesOnEveryDocument(string path, int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.Locator("#where-to-go-next").ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync("() => document.fonts.ready");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const list = document.querySelector('#where-to-go-next + ul');
                const box = list.getBoundingClientRect();
                const tiles = [...list.children];
                if (tiles.length < 3) problems.push(`only ${tiles.length} destinations`);
                if (!['flex', 'grid'].includes(getComputedStyle(list).display)) problems.push('destinations are a bulleted list, not tiles');
                for (const tile of tiles) {
                    const style = getComputedStyle(tile);
                    if (parseFloat(style.borderTopWidth) === 0) problems.push(`${tile.textContent.trim()}: not a bordered tile`);
                }
                // Every row of tiles reaches the list's right edge: no row ends in an empty slot.
                const rows = new Map();
                for (const tile of tiles) {
                    const rect = tile.getBoundingClientRect();
                    const top = Math.round(rect.top);
                    rows.set(top, Math.max(rows.get(top) ?? 0, rect.right));
                }
                for (const [top, right] of rows)
                    if (box.right - right > 2) problems.push(`the row at ${top}px stops ${Math.round(box.right - right)}px short of the edge`);
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page scrolls sideways');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{path} {width}px: {string.Join("; ", problems)}");
    }
}
