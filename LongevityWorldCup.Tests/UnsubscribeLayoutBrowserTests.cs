using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class UnsubscribeLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, 100)]
    [InlineData(1440, 900, 200)]
    [InlineData(820, 1180, 100)]
    [InlineData(390, 844, 100)]
    [InlineData(390, 844, 200)]
    [InlineData(320, 640, 100)]
    [InlineData(320, 640, 200)]
    [InlineData(560, 800, 200)]
    public async Task Unsubscribe_FillsTheScreenAndKeepsTheCardWhole(int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/unsubscribe");
        await Assertions.Expect(page.Locator("#unsubscribe-button")).ToBeVisibleAsync();
        var originalTitleSize = await page.Locator(".unsubscribe-panel h1").EvaluateAsync<double>("title => parseFloat(getComputedStyle(title).fontSize)");
        await page.EvaluateAsync("async percent => { document.documentElement.style.fontSize = `${percent}%`; await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }", textPercent);

        // Enlarging the root starts a reflow; two animation frames need not expose its final computed type.
        try
        {
            await page.WaitForFunctionAsync(
                "minimum => parseFloat(getComputedStyle(document.querySelector('.unsubscribe-panel h1')).fontSize) >= minimum",
                originalTitleSize * textPercent / 100 - 0.1, new() { Timeout = 5_000 });
        }
        catch (System.TimeoutException)
        {
            var diagnostics = await page.EvaluateAsync<string>(
                "() => JSON.stringify({rootFont: getComputedStyle(document.documentElement).fontSize, inlineRoot: document.documentElement.style.fontSize, titleFont: getComputedStyle(document.querySelector('.unsubscribe-panel h1')).fontSize, viewport: [innerWidth, innerHeight]})");
            throw new Xunit.Sdk.XunitException($"{width}x{height} / {textPercent}%: enlarged type did not settle: {diagnostics}");
        }

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const doc = document.documentElement;
                const footer = document.querySelector('footer').getBoundingClientRect();
                const panel = document.querySelector('.unsubscribe-panel').getBoundingClientRect();
                const title = document.querySelector('.unsubscribe-panel h1');
                const titleRange = document.createRange();
                titleRange.selectNodeContents(title);
                const titleLines = new Set(Array.from(titleRange.getClientRects()).map(rect => Math.round(rect.top))).size;
                const input = document.getElementById('email').getBoundingClientRect();
                const button = document.getElementById('unsubscribe-button').getBoundingClientRect();
                return [Math.max(innerHeight, doc.scrollHeight) - (footer.bottom + scrollY), panel.left, panel.right,
                    title.getBoundingClientRect().right, title.scrollWidth - title.clientWidth, titleLines,
                    input.top, button.top, button.left, input.right, parseFloat(getComputedStyle(title).fontSize),
                    Math.min(...Array.from(titleRange.getClientRects()).map(rect => rect.left)),
                    Math.max(...Array.from(titleRange.getClientRects()).map(rect => rect.right))];
            }
            """);
        var scenario = $"{width}x{height} / {textPercent}%";
        Assert.True(Math.Abs(layout[0]) <= 1, $"{scenario}: the footer stops {layout[0]}px above the bottom of the page.");
        Assert.True(layout[3] <= layout[2] + 1 && layout[4] <= 1, $"{scenario}: the title spills out of the card.");
        Assert.True(layout[10] >= originalTitleSize * textPercent / 100 - 0.1,
            $"{scenario}: the title should respect enlarged text ({originalTitleSize}px became {layout[10]}px).");
        Assert.True(layout[11] >= layout[1] && layout[12] <= layout[2], $"{scenario}: heading text is clipped outside the card.");
        if (width >= 560 && textPercent == 100)
            Assert.True(Math.Abs(layout[6] - layout[7]) <= 1 && layout[8] >= layout[9], $"{scenario}: the email field and button should share one row.");
        Assert.True(layout[1] >= 0 && layout[2] <= width, $"{scenario}: the card escapes the viewport ({layout[1]}-{layout[2]}px).");
        // The shared footer's own columns overflow at 200% text; this page only owns the document width.
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"), $"{scenario}: the page scrolls sideways.");
    }
}
