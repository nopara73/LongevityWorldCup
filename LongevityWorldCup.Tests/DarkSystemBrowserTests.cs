using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DarkSystemBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task ThemeChanges_PreserveReadableContentMediaAndGeometry(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");
        await page.EvaluateAsync("""
            () => {
                window.themeVeils = 0;
                new MutationObserver(records => {
                    for (const record of records) for (const node of record.addedNodes)
                        if (node.id === 'lwc-theme-veil') window.themeVeils++;
                }).observe(document.body, { childList: true });
                const probe = document.createElement('div');
                probe.innerHTML = '<p style="color:var(--lwc-success)"><strong id="semanticProbe">Saved</strong></p><img id="photoProbe" width="40" height="40" alt="Portrait fixture">';
                probe.querySelector('img').src = document.querySelector('meta[name="lwc-athlete-placeholder"]').content;
                document.querySelector('main').appendChild(probe);
            }
            """);
        var field = page.Locator("#name");
        var before = await field.BoundingBoxAsync();
        foreach (var scheme in new[] { ColorScheme.Dark, ColorScheme.Light, ColorScheme.Dark, ColorScheme.Light, ColorScheme.Dark })
        {
            await page.EmulateMediaAsync(new() { ColorScheme = scheme });
            await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
            Assert.Equal(0, await page.EvaluateAsync<int>("() => window.themeVeils"));
            Assert.Equal("none", await page.Locator("#photoProbe").EvaluateAsync<string>("e => getComputedStyle(e).filter"));
            Assert.True(await page.Locator("#semanticProbe").EvaluateAsync<bool>("e => getComputedStyle(e).color === getComputedStyle(e.parentElement).color"));
            var after = await field.BoundingBoxAsync();
            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.Equal(before.X, after.X);
            Assert.Equal(before.Y, after.Y);
            Assert.Equal(before.Width, after.Width);
            Assert.Equal(before.Height, after.Height);
        }
        await field.EvaluateAsync("e => e.setAttribute('aria-invalid', 'true')");
        await field.FocusAsync();
        await Assertions.Expect(field).ToHaveCSSAsync("border-top-color", "rgb(255, 139, 160)");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce, ForcedColors = ForcedColors.Active });
        await Assertions.Expect(field).ToHaveCSSAsync("outline-style", "solid");
        Assert.Equal("", await page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).getPropertyValue('--lwc-dark-edge').trim()"));
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task BadgeExit_PreservesTheOpenSurfaceAndShortTextFit(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = ColorScheme.Dark
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.WaitForFunctionAsync("() => typeof window.setBadges === 'function'");
        await page.EvaluateAsync("""
            () => {
                const host = document.createElement('div');
                host.id = 'exitProbe'; host.className = 'badge-section';
                document.querySelector('main').prepend(host);
                setBadges({ Name:'Exit fixture', Badges:[{BadgeLabel:'≥2 submissions'}]}, host);
            }
            """);
        var badge = page.Locator("#exitProbe .badge-class").First;
        await badge.FocusAsync();
        await badge.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".badge-tip.is-card")).ToBeVisibleAsync();
        await page.Locator(".badge-tip").EvaluateAsync("e => Promise.all(e.getAnimations({subtree:true}).filter(a => a.playState === 'running' && a.timeline === document.timeline && a.effect?.getTiming().iterations !== Infinity).map(a => a.finished.catch(() => {})))");
        var result = await page.EvaluateAsync<JsonElement>("""
            () => {
                const tip = document.querySelector('.badge-tip');
                const paint = e => { const s = getComputedStyle(e); return [s.backgroundColor,s.backgroundImage,s.borderTopColor,s.boxShadow]; };
                const before = paint(tip);
                const content = tip.querySelector('.badge-tip-content');
                const fit = content.scrollHeight <= content.clientHeight && content.scrollWidth <= content.clientWidth;
                document.activeElement.dispatchEvent(new KeyboardEvent('keydown', {key:'Escape',bubbles:true}));
                const exit = document.querySelector('.badge-tip-exit');
                return { before, after:exit ? paint(exit) : null, fit };
            }
            """);
        Assert.True(result.GetProperty("fit").GetBoolean(), result.ToString());
        Assert.Equal(result.GetProperty("before").ToString(), result.GetProperty("after").ToString());
    }
}
