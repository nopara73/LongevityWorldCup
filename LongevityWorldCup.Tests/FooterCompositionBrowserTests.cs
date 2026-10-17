using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FooterCompositionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, 1, 1)]
    [InlineData(1280, 720, 1, 1)]
    [InlineData(768, 1024, 2, 1)]
    [InlineData(390, 844, 3, 1)]
    [InlineData(320, 640, 3, 2)]
    public async Task Footer_SetsPagesAboveARowOfRoundPlatformMarks(int width, int height, int pageRows, int platformRows)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await page.Locator(".footer").ScrollIntoViewIfNeededAsync();

        var layout = await page.EvaluateAsync<string[]>(
            """
            () => {
                const rows = links => new Set(links.map(link => Math.round(link.getBoundingClientRect().top))).size;
                const pages = [...document.querySelectorAll('.footer-column--explore .footer-link')];
                const platforms = [...document.querySelectorAll('.footer-follow-links .footer-link')];
                const subscriptions = [...document.querySelectorAll('.footer-subscriptions .footer-link')];
                const problems = [];
                const lowestPage = Math.max(...pages.map(link => link.getBoundingClientRect().bottom));
                for (const link of [...platforms, ...subscriptions]) {
                    const box = link.getBoundingClientRect();
                    const style = getComputedStyle(link);
                    if (Math.abs(box.width - box.height) > 1 || parseFloat(style.borderTopLeftRadius) < box.width / 2 - 1)
                        problems.push(`${link.title}: not a round mark`);
                    if (box.width < 44) problems.push(`${link.title}: small target`);
                    if (box.top < lowestPage) problems.push(`${link.title}: not beneath the page links`);
                    const label = link.querySelector('.footer-link-label');
                    if (label && label.getBoundingClientRect().width > 1) problems.push(`${link.title}: visible name inside the mark`);
                }
                for (const link of pages) {
                    if (!link.querySelector('.footer-link-label').getBoundingClientRect().width) problems.push(`${link.textContent.trim()}: page name hidden`);
                }
                const center = rect => (rect.left + rect.right) / 2;
                const footer = document.querySelector('.footer-container').getBoundingClientRect();
                const firstRowTop = Math.round(platforms[0].getBoundingClientRect().top);
                const platformRow = [...platforms, ...subscriptions].map(link => link.getBoundingClientRect())
                    .filter(rect => Math.round(rect.top) === firstRowTop);
                const spread = (Math.min(...platformRow.map(r => r.left)) + Math.max(...platformRow.map(r => r.right))) / 2;
                if (Math.abs(spread - center(footer)) > 24) problems.push('platform marks are not centred');
                return [String(rows(pages)), String(rows(platforms)), ...problems];
            }
            """);
        Assert.True(layout.Length == 2, $"{width}x{height}: {string.Join("; ", layout.Skip(2))}");
        Assert.Equal(pageRows.ToString(), layout[0]);
        Assert.Equal(platformRows.ToString(), layout[1]);
    }
}
