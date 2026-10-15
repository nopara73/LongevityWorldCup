using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ReviewPageLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, ColorScheme.Light)]
    [InlineData(1280, 720, ColorScheme.Dark)]
    [InlineData(820, 1180, ColorScheme.Light)]
    [InlineData(390, 844, ColorScheme.Dark)]
    [InlineData(320, 640, ColorScheme.Light)]
    public async Task ReviewPage_SetsTheArtworkBesideTheMessageOrAsTheCardBanner(int width, int height, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/review?from=edit-profile");
        await Assertions.Expect(page.Locator("#appReviewText")).ToHaveTextAsync("Edit review");
        await page.WaitForFunctionAsync(
            "() => { const image = document.querySelector('.application-review-visual .illustration'); return image?.complete && image.naturalWidth > 0; }");
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        var problems = await page.EvaluateAsync<string[]>(
            """
            wide => {
                const issues = [];
                const rect = selector => document.querySelector(selector).getBoundingClientRect();
                const panel = rect('.application-review-panel');
                const art = rect('.application-review-visual .illustration');
                const title = rect('.application-review-title');
                const message = rect('.application-review-copy.primary');
                const words = document.getElementById('appReviewText').getClientRects();
                const lastLine = words[words.length - 1];
                const rocket = rect('.application-review-title i');
                if (wide) {
                    if (art.right > title.left) issues.push(`artwork (${art.right}) should sit beside the title (${title.left})`);
                    if (art.top > title.bottom) issues.push('artwork should start level with the title');
                    if (art.right > message.left) issues.push('artwork should sit beside the message');
                } else {
                    if (Math.abs(art.left - panel.left) > 1.5 || Math.abs(art.right - panel.right) > 1.5) issues.push('artwork should span the card');
                    if (Math.abs(art.top - panel.top) > 1.5) issues.push('artwork should be the card banner');
                    if (title.top < art.bottom) issues.push('title should follow the artwork');
                }
                if (Math.abs(words[0].left - message.left) > 2) issues.push('title should align with the message');
                const rocketMiddle = (rocket.top + rocket.bottom) / 2;
                if (rocketMiddle < lastLine.top || rocketMiddle > lastLine.bottom) issues.push('the rocket should stay on the title line');
                return issues;
            }
            """, width >= 720);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }
}
