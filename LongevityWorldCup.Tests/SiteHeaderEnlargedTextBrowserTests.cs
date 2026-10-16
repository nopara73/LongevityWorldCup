using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class SiteHeaderEnlargedTextBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/about", 390, 200)]
    [InlineData("/events", 390, 200)]
    [InlineData("/ruleset", 320, 200)]
    [InlineData("/", 390, 200)]
    [InlineData("/about", 390, 100)]
    public async Task Header_KeepsTheBrandAndPlayWordsWholeWithEnlargedText(string path, int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("header[role=banner] .bannertext")).ToBeVisibleAsync();
        await page.EvaluateAsync("async percent => { document.documentElement.style.fontSize = `${percent}%`; await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }", textPercent);

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const brand = document.querySelector('header[role=banner] .bannertext');
                if (brand.scrollWidth > brand.clientWidth + 1)
                    issues.push(`brand is cut off (${brand.scrollWidth}/${brand.clientWidth})`);
                const brandBox = brand.getBoundingClientRect();
                const header = document.querySelector('header[role=banner]').getBoundingClientRect();
                if (brandBox.bottom > header.bottom + 1) issues.push('brand spills out of the header');
                const play = document.querySelector('header[role=banner] .join-game:not(.scrolled-button)');
                const roots = [brand];
                if (play && play.getBoundingClientRect().width) roots.push(play);
                for (const root of roots) {
                    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
                    let node;
                    while (node = walker.nextNode()) {
                        for (const word of node.textContent.matchAll(/[^\s ]+/g)) {
                            const range = document.createRange();
                            range.setStart(node, word.index);
                            range.setEnd(node, word.index + word[0].length);
                            const rects = Array.from(range.getClientRects()).filter(rect => rect.width > 0);
                            if (new Set(rects.map(rect => Math.round(rect.top))).size > 1) issues.push(`split word: ${word[0]}`);
                        }
                    }
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{path} {width}px {textPercent}%: {string.Join("; ", problems)}");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }
}
