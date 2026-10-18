using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BadgeDarkRimBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/leaderboard", ".leaderboard .badge-class", 1440)]
    [InlineData("/leaderboard", ".leaderboard .badge-class", 390)]
    [InlineData("/events", ".events-board .badge-class", 1440)]
    public async Task DarkMode_GivesBadgesALightRimInsteadOfBlackOnBlack(string path, string selector, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = ColorScheme.Dark,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("selector => document.querySelector(selector.split(' ')[0])", selector);
        // Render the black utility badge the leaderboard and Highlights use, inside each surface.
        await page.EvaluateAsync("selector => { const host = document.querySelector(selector.split(' ')[0]); const badge = document.createElement('span'); badge.className = 'badge-class badge-family-utility'; badge.style.background = 'linear-gradient(135deg,#2a2a2a,#1e1e1e)'; badge.innerHTML = '<i class=\"fa fa-calendar-check\"></i>'; host.appendChild(badge); }", selector);

        var measured = await page.EvaluateAsync<int>(
            "selector => [...document.querySelectorAll(selector)].filter(badge => badge.getBoundingClientRect().width > 0 && parseFloat(getComputedStyle(badge).borderTopWidth) > 0).length",
            selector);
        Assert.True(measured > 0, $"{path} {width}px: no rimmed badge was rendered to check.");
        var dark = await page.EvaluateAsync<string[]>(
            """
            selector => [...document.querySelectorAll(selector)]
                .filter(badge => badge.getBoundingClientRect().width > 0 && parseFloat(getComputedStyle(badge).borderTopWidth) > 0)
                .map(badge => getComputedStyle(badge).borderTopColor)
                .filter(color => {
                    const [r, g, b, a = 1] = color.match(/[\d.]+/g).map(Number);
                    return (r + g + b) / 3 < 128 || a < 0.15;
                })
            """, selector);
        Assert.True(dark.Length == 0, $"{path} {width}px: {dark.Length} badges keep a dark rim that disappears on the dark surface ({string.Join(", ", dark.Distinct())}).");
    }
}
