using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class NotFoundPageBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/no-such-page", 1440, 900)]
    [InlineData("/athletes/nobody-here/at-all", 1280, 720)]
    [InlineData("/athletes/nobody-here/at-all", 390, 844)]
    [InlineData("/no-such-page", 320, 640)]
    [InlineData("/no-such-page", 560, 320)]
    [InlineData("/athletes/nobody-here/at-all", 640, 360)]
    [InlineData("/athletes/nobody-here/at-all", 667, 375)]
    [InlineData("/no-such-page", 719, 375)]
    [InlineData("/no-such-page", 720, 375)]
    [InlineData("/no-such-page", 844, 390)]
    public async Task MissingPages_ShowHeroldAndHomeAsOneCardInTheFirstScreen(string path, int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(path);
        Assert.Equal(404, response?.Status);
        await page.Locator(".not-found-visual img").EvaluateAsync("image => image.decode()");
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const image = document.querySelector('.not-found-visual img');
                const rect = element => document.querySelector(element).getBoundingClientRect();
                const card = rect('.not-found-card'), visual = image.getBoundingClientRect(), title = rect('.not-found-title'), home = rect('.not-found-actions .option-button');
                return [image.naturalWidth, card.left, card.right, card.top, card.bottom, visual.left, visual.right, visual.top, visual.bottom,
                    title.left, title.top, home.left, home.right, home.top, home.bottom, innerHeight];
            }
            """);
        var scenario = $"{path} at {width}x{height}";
        Assert.True(layout[0] > 0, $"{scenario}: Herold's photo did not load.");
        foreach (var start in new[] { 5, 11 })
            Assert.True(layout[start] >= layout[1] - 1 && layout[start + 1] <= layout[2] + 1 && layout[start + 2] >= layout[3] - 1 && layout[start + 3] <= layout[4] + 1,
                $"{scenario}: the photo and Home action should share the one card.");
        if (width >= 720 || width >= 560 && height <= 480)
            Assert.True(layout[6] <= layout[9] && layout[10] >= layout[7], $"{scenario}: the photo should sit beside the title.");
        else
            Assert.True(layout[8] <= layout[10], $"{scenario}: the photo should sit above the title.");
        Assert.True(layout[14] <= layout[15], $"{scenario}: Home should be in the first screen ({layout[14]}px of {layout[15]}px).");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320, 740, false)]
    [InlineData(320, 740, true)]
    [InlineData(640, 360, false)]
    [InlineData(1440, 900, true)]
    public async Task Scoreboard_KeepsItsAccessibleTitleAndWholeDigitGlyphs(int width, int height, bool reduced)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/no-such-page");
        await Assertions.Expect(page.Locator("h1")).ToHaveAccessibleNameAsync("404 not found");
        await page.EvaluateAsync("() => document.fonts.ready");
        // Inspect the final animation frame, including the moving reel's real glyph.
        await page.Locator(".nf-score").EvaluateAsync("e => e.getAnimations({subtree:true}).forEach(a => a.finish())");
        Assert.True(await page.Locator(".nf-score").EvaluateAsync<bool>("""
            score => [...score.querySelectorAll('.nf-reel')].every(reel => {
                const range = document.createRange(); range.selectNodeContents(reel);
                const tile = reel.parentElement.getBoundingClientRect();
                return [...range.getClientRects()].every(r => r.left >= tile.left - 1 && r.right <= tile.right + 1
                    && r.top >= tile.top - 1 && r.bottom <= tile.bottom + 1);
            })
            """));
        if (reduced)
            Assert.Equal("none", await page.Locator(".nf-reel").First.EvaluateAsync<string>("e => getComputedStyle(e, '::before').display"));
    }
}
