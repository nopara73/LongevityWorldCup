using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class JoinComparisonBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static async Task OpenListsAsync(IPage page)
    {
        await NavigateAndSettleAsync(page, "/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinGoProButton').disabled");
        foreach (var summary in await page.Locator(".play-join-biomarkers summary").AllAsync())
        {
            await summary.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
        }
        await WaitForFiniteAnimationsAsync(page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedMarkersHighlightTheirPairWithoutWeakeningEitherTrack(bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        await OpenListsAsync(page);
        Assert.Equal(9, await page.Locator(".play-join-card:not(.play-join-card--pro) li[data-twin]").CountAsync());
        Assert.Equal(9, await page.Locator(".play-join-card--pro li[data-twin]").CountAsync());
        Assert.Equal(13, await page.Locator(".play-join-card--pro li:not([data-twin])").CountAsync());
        foreach (var item in await page.Locator(".play-join-card:not(.play-join-card--pro) li[data-twin]").AllAsync())
        {
            await item.HoverAsync();
            var key = await item.GetAttributeAsync("data-twin");
            await Assertions.Expect(page.Locator(".is-twin-lit")).ToHaveCountAsync(2);
            await Assertions.Expect(page.Locator($".is-twin-lit[data-twin='{key}']")).ToHaveCountAsync(2);
        }
        await WaitForFiniteAnimationsAsync(page);
        var iconContrast = await page.EvaluateAsync<double[]>("""
            () => {
                const luminance = color => {
                    const channels = color.match(/[\d.]+/g).slice(0, 3).map(Number).map(c => c / 255)
                        .map(c => c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4);
                    return channels[0] * .2126 + channels[1] * .7152 + channels[2] * .0722;
                };
                return [...document.querySelectorAll('.play-join-card--pro li:not([data-twin]), .play-join-card--pro li.is-twin-lit')].map(item => {
                    const style = getComputedStyle(item, '::before');
                    const values = [luminance(style.color), luminance(style.backgroundColor)].sort((a, b) => b - a);
                    return (values[0] + .05) / (values[1] + .05);
                });
            }
            """);
        Assert.All(iconContrast, ratio => Assert.True(ratio >= 3, $"Comparison symbol contrast is {ratio:0.00}:1."));
        await page.Locator(".play-join-card .track-name").First.HoverAsync();
        await WaitForFiniteAnimationsAsync(page);
        await Assertions.Expect(page.Locator(".is-twin-lit")).ToHaveCountAsync(0);
        Assert.True(await page.Locator(".play-join-card").EvaluateAllAsync<bool>("""
            cards => cards.every(card => getComputedStyle(card).opacity === '1' && getComputedStyle(card).filter === 'none')
            """));
        var otherTransform = await page.Locator(".play-join-card--pro").EvaluateAsync<string>("card => getComputedStyle(card).transform");
        Assert.Contains(otherTransform, new[] { "none", "matrix(1, 0, 0, 1, 0, 0)" });
    }

    [Fact]
    public async Task TouchComparisonSurvivesPointerReleaseAndClearsWhenItsListCloses()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, IsMobile = true, HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await OpenListsAsync(page);
        await page.Locator(".play-join-card--pro li[data-twin=alb]").TapAsync();
        await Assertions.Expect(page.Locator(".is-twin-lit[data-twin=alb]")).ToHaveCountAsync(2);
        await page.Locator(".play-join-card--pro summary").TapAsync();
        await Assertions.Expect(page.Locator(".is-twin-lit")).ToHaveCountAsync(0);
        await page.Locator("#joinTrackBackBtn").ClickAsync();
        await page.Locator("#newGameBtn").ClickAsync();
        await page.Locator(".play-join-card--pro summary").TapAsync();
        await page.Locator(".play-join-card--pro li[data-twin=crp]").TapAsync();
        await Assertions.Expect(page.Locator(".is-twin-lit[data-twin=crp]")).ToHaveCountAsync(2);
    }

    [Theory]
    [InlineData(320, 640, false, false)]
    [InlineData(390, 844, true, false)]
    [InlineData(600, 900, false, false)]
    [InlineData(768, 1024, false, false)]
    [InlineData(844, 390, true, false)]
    [InlineData(1440, 900, true, false)]
    [InlineData(390, 844, false, true)]
    [InlineData(1440, 900, true, true)]
    public async Task OpenComparisonFitsAndStaysStillWithReducedMotion(int width, int height, bool dark, bool enlargedText)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await OpenListsAsync(page);
        if (enlargedText) await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        await page.Locator(".play-join-card--pro li[data-twin=alb]").HoverAsync();
        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const issues = [];
                if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('page overflows');
                for (const list of document.querySelectorAll('.play-join-biomarkers ul')) {
                    if (list.scrollWidth > list.clientWidth + 1 || list.scrollHeight > list.clientHeight + 1) issues.push('list overflows');
                    const box = list.getBoundingClientRect();
                    for (const item of list.children) {
                        const range = document.createRange(); range.selectNodeContents(item);
                        for (const r of range.getClientRects()) {
                            if (r.left < box.left - 1 || r.right > box.right + 1 || r.bottom > box.bottom + 1) issues.push(item.textContent.trim() + ' clips');
                        }
                    }
                }
                for (const card of document.querySelectorAll('.play-join-card')) {
                    if (getComputedStyle(card).transform !== 'none') issues.push('card moves');
                }
                if (document.getAnimations().some(a => a.effect?.target?.closest?.('.play-join-biomarkers') && a.playState === 'running')) issues.push('comparison animates');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, string.Join("; ", problems));
    }
}
