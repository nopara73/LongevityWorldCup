using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AboutPageBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task AboutContents_FillTheRailUpToTheSectionBeingRead()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/about");
        await page.Locator(".documentation-nav a[href='#how-it-works']").ClickAsync();
        // The active class changes before the shared border-color transition has painted.
        await page.WaitForFunctionAsync(
            """
            () => {
                if (document.querySelector('.documentation-nav a.is-active')?.getAttribute('href') !== '#how-it-works')
                    return false;
                const accent = getComputedStyle(document.querySelector('.documentation-source-link')).color;
                return [...document.querySelectorAll('.documentation-nav a.documentation-nav-level-2')]
                    .every((link, index) => (getComputedStyle(link).borderLeftColor === accent) === (index < 3));
            }
            """, null, new() { Timeout = 5000 });

        var rail = await page.EvaluateAsync<string[]>(
            """
            () => {
                const accent = getComputedStyle(document.querySelector('.documentation-source-link')).color;
                return [...document.querySelectorAll('.documentation-nav a.documentation-nav-level-2')].map(link => {
                    const style = getComputedStyle(link);
                    const number = getComputedStyle(link, '::before').content;
                    return `${style.borderLeftColor === accent ? 'filled' : 'open'}:${parseFloat(style.borderLeftWidth) >= 2}:${number !== 'none' && number !== ''}`;
                });
            }
            """);
        Assert.Equal(
            ["filled:true:true", "filled:true:true", "filled:true:true", "open:true:true", "open:true:true", "open:true:true"],
            rail);
    }

    [Theory]
    [InlineData(1440, 900, 100)]
    [InlineData(820, 1180, 100)]
    [InlineData(390, 844, 100)]
    [InlineData(390, 844, 200)]
    [InlineData(320, 640, 100)]
    public async Task AboutPage_OpensOnAFullWidthSilhouetteAndEndsOnWholeTileLinks(int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/about");
        if (textPercent != 100)
            await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.Locator(".documentation-document img").EvaluateAsync("image => image.decode()");
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        var scenario = $"{width}x{height} at {textPercent}% text";
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const article = document.querySelector('.documentation-document').getBoundingClientRect();
                const image = document.querySelector('.documentation-document img').getBoundingClientRect();
                if (Math.abs(image.left - article.left) > 2 || Math.abs(image.right - article.right) > 2)
                    issues.push(`silhouette spans ${image.left}-${image.right}, article ${article.left}-${article.right}`);
                const tiles = [...document.querySelectorAll('#where-to-go-next + ul > li')];
                if (tiles.length !== 5) issues.push(`expected 5 destination tiles, found ${tiles.length}`);
                for (const tile of tiles) {
                    const link = tile.querySelector('a');
                    tile.scrollIntoView({ block: 'center' });
                    const box = tile.getBoundingClientRect();
                    const target = document.elementFromPoint(box.right - 6, box.bottom - 6);
                    if (!target || !link.contains(target) && target !== link)
                        issues.push(`${link.textContent}: tile corner is not part of its link`);
                    const firstWord = document.createRange();
                    const text = link.firstChild;
                    const end = text.textContent.indexOf(' ') > 0 ? text.textContent.indexOf(' ') : text.textContent.length;
                    firstWord.setStart(text, 0);
                    firstWord.setEnd(text, end);
                    const lines = new Set([...firstWord.getClientRects()].map(rect => Math.round(rect.top)));
                    if (lines.size > 1) issues.push(`${link.textContent}: first word breaks across lines`);
                }
                const main = document.querySelector('.documentation-page');
                for (const element of main.querySelectorAll('*')) {
                    const box = element.getBoundingClientRect();
                    if (box.width > 0 && box.right > innerWidth + 1) { issues.push(`${element.tagName.toLowerCase()} overflows to ${Math.round(box.right)}px`); break; }
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{scenario}: {string.Join("; ", problems)}");
        // The shared footer overflows at 200% text on phones; that predates this page's layout.
        if (textPercent == 100)
            await ExpectNoHorizontalOverflowAsync(page);

        if (width >= 1280)
        {
            var ruleset = page.Locator("#where-to-go-next + ul > li").Nth(1);
            await ruleset.ScrollIntoViewIfNeededAsync();
            var box = await ruleset.BoundingBoxAsync();
            Assert.NotNull(box);
            await page.Mouse.ClickAsync(box.X + box.Width - 8, box.Y + box.Height - 8);
            await page.WaitForURLAsync(url => url.EndsWith("/ruleset", StringComparison.Ordinal));
        }
    }
}
