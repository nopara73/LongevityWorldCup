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
    [InlineData("/", ".leaderboard", 1440)]
    [InlineData("/", ".leaderboard", 390)]
    [InlineData("/leaderboard", ".leaderboard", 1440)]
    [InlineData("/leaderboard", ".leaderboard", 390)]
    [InlineData("/events", ".events-board", 1440)]
    [InlineData("/events", ".events-board", 390)]
    public async Task ThemeChanges_OnlyChangeTheRimOfEveryBadgeShape(string path, string surface, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await page.WaitForFunctionAsync("() => typeof window.setBadges === 'function'");
        // Exercise the shared renderer's actual gradients, icons and tooltips in each
        // surface's cascade rather than manufacturing a styled badge span.
        await page.EvaluateAsync("""
            surface => {
                const container = document.querySelector(surface);
                const table = container.matches('table') ? container : container.querySelector('table');
                // The page hydrates its first tbody asynchronously. Keep the fixture
                // in the same table cascade without letting hydration remove it.
                const row = table.createTBody().insertRow();
                const host = document.createElement('div');
                host.id = 'rim-test-badges';
                host.className = 'badge-section';
                row.insertCell().appendChild(host);
                window.setBadges({ Name: 'Rim Test', Badges: [
                    { BadgeLabel: '≥2 submissions' },
                    { BadgeLabel: 'Age reduction', Category: 'global', Place: 1 },
                    { BadgeLabel: 'Best domain – liver', Place: 1 }
                ] }, host);
            }
            """, surface);
        var badges = page.Locator("#rim-test-badges .badge-class");
        var light = await MeasureAsync(badges);
        // The hexagon draws its rim with its backing colour, so it follows the theme too.
        Assert.All(light, b => Assert.Equal("rgba(0, 0, 0, 0.48)", b.Rim));
        Assert.Equal("0px", Assert.Single(light, b => b.Domain).BorderWidth);

        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        var dark = await MeasureAsync(badges);
        Assert.All(dark, b => Assert.Equal("rgba(255, 255, 255, 0.26)", b.Rim));
        Assert.Equal("0px", Assert.Single(dark, b => b.Domain).BorderWidth);
        Assert.Equal(light.Select(b => b.Appearance), dark.Select(b => b.Appearance));

        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        var restored = await MeasureAsync(badges);
        Assert.Equal(light.Select(b => b.Rim), restored.Select(b => b.Rim));
        Assert.Equal(light.Select(b => b.Appearance), restored.Select(b => b.Appearance));
    }

    private static async Task<BadgeAppearance[]> MeasureAsync(ILocator badges)
    {
        await Assertions.Expect(badges).ToHaveCountAsync(3);
        return await badges.EvaluateAllAsync<BadgeAppearance[]>("""
            badges => badges.map(badge => {
                const style = getComputedStyle(badge);
                const icon = badge.querySelector('i');
                return {
                    Domain: badge.classList.contains('badge-family-domain'),
                    Rim: badge.classList.contains('badge-family-domain') ? style.backgroundColor : style.borderTopColor,
                    BorderWidth: style.borderTopWidth,
                    Appearance: JSON.stringify([
                        style.backgroundImage,
                        badge.classList.contains('badge-family-domain') ? '' : style.backgroundColor, style.color,
                        style.width, style.height, style.borderRadius, style.clipPath,
                        style.borderTopWidth, style.borderTopStyle, icon?.className,
                        getComputedStyle(badge, '::before').backgroundImage,
                        badge.title, badge.getAttribute('aria-label'), badge.getAttribute('href')
                    ])
                };
            })
            """);
    }

    private sealed class BadgeAppearance
    {
        public bool Domain { get; set; }
        public string Rim { get; set; } = "";
        public string BorderWidth { get; set; } = "";
        public string Appearance { get; set; } = "";
    }
}
