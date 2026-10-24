using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class BadgeFamilyBrowserTests
{
    [Fact]
    public async Task Tooltip_ContentFitsWithoutPhantomScrolling_AndLongContentRemainsReachable()
    {
        // Playwright normally launches headless Chromium with --hide-scrollbars.
        // Exercise native gutters as well as geometry: an invisible scrollbar
        // must not make a short explanation look like a passing visual check.
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"]
        });
        foreach (var (width, height, dark, fontSize) in new[]
        {
            (1440, 900, false, 16), (1440, 900, true, 16),
            (720, 450, false, 16), (390, 844, true, 16),
            (320, 480, false, 24), (320, 480, true, 32)
        })
        {
            await using var context = await NewContextAsync(browser, App, new()
            {
                ViewportSize = new() { Width = width, Height = height },
                ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
                ReducedMotion = ReducedMotion.Reduce,
                IsMobile = width < 600, HasTouch = width < 600
            });
            var page = await context.NewPageAsync();
            await NavigateAndSettleAsync(page, "/events");
            await page.WaitForFunctionAsync("() => typeof window.setBadges === 'function'");
            await page.EvaluateAsync("""
                size => {
                    document.documentElement.style.fontSize = size + 'px';
                    const host = document.createElement('div');
                    host.id = 'fit-badges'; host.className = 'badge-section';
                    host.style.cssText = 'position:fixed;left:8px;top:65%;z-index:10000';
                    document.body.appendChild(host);
                    window.setBadges({Badges:[{BadgeLabel:'≥2 submissions'}]}, host);
                }
                """, fontSize);
            await page.EvaluateAsync("() => document.fonts.ready");
            var badge = page.Locator("#fit-badges .badge-class");
            var tip = page.Locator(".badge-tip");
            // Above and below, at both horizontal edges. Reusing the popover
            // also exercises measurements after the previous side/width.
            foreach (var below in new[] { false, true })
            {
                await badge.EvaluateAsync("""
                    (el, below) => {
                        el.parentElement.style.top = below ? '8px' : '65%';
                        el.parentElement.style.left = below ? 'auto' : '8px';
                        el.parentElement.style.right = below ? '8px' : 'auto';
                    }
                    """, below);
                // Focus tests the anchored peek on every viewport. Tapping
                // opens a dialog/sheet, covered separately below.
                await badge.FocusAsync();
                await Assertions.Expect(tip).ToBeVisibleAsync();
                await Assertions.Expect(tip.Locator(".badge-tip-title")).ToHaveTextAsync("The Regular");
                await Assertions.Expect(tip.Locator(".badge-tip-detail")).ToHaveTextAsync("two or more tests submitted");
                await Assertions.Expect(tip).ToHaveAttributeAsync("data-side", below ? "below" : "above");
                await AssertExplanationFitsAsync(tip);
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(tip).ToBeHiddenAsync();
                await Assertions.Expect(badge).ToBeFocusedAsync();
                await badge.EvaluateAsync("el => el.blur()");
            }

            // A multiline +N card still scrolls when its real text exceeds the
            // available space. No hidden overflow, clipped text or tiny fonts.
            await page.EvaluateAsync("""
                () => window.setBadges({Badges:Array.from({length:35}, (_, i) => ({
                    BadgeLabel: i ? 'Best domain – inflammation' : '≥2 submissions', Place:1
                }))}, document.querySelector('#fit-badges'))
                """);
            var chip = page.Locator("#fit-badges .badge-overflow-count");
            await chip.FocusAsync();
            await Assertions.Expect(tip).ToBeVisibleAsync();
            await Assertions.Expect(tip.Locator(".badge-tip-row")).ToHaveCountAsync(32);
            var content = tip.Locator(".badge-tip-content");
            await Assertions.Expect(content).ToHaveCSSAsync("overflow-y", "auto");
            Assert.True(await content.EvaluateAsync<bool>("el => el.scrollHeight > el.clientHeight"));
            Assert.True(await content.EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth"));
            Assert.True(await tip.EvaluateAsync<bool>("""
                el => { const r=el.getBoundingClientRect(); return r.left>=7 && r.right<=innerWidth-7 && r.top>=7 && r.bottom<=innerHeight-7; }
                """));
            await content.EvaluateAsync("el => el.scrollTop = el.scrollHeight");
            await Assertions.Expect(tip).ToBeVisibleAsync();
            Assert.True(await content.EvaluateAsync<bool>("""
                el => {
                    const last = el.lastElementChild.getBoundingClientRect(), r = el.getBoundingClientRect();
                    return el.scrollTop > 0 && last.bottom <= r.bottom && last.bottom > r.top;
                }
                """));
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(chip).ToBeFocusedAsync();
            await Assertions.Expect(tip).ToBeHiddenAsync();
        }
    }

    private static async Task AssertExplanationFitsAsync(ILocator tip)
    {
        var fit = await tip.EvaluateAsync<ExplanationFit>("""
            tip => {
                // Falling back to the original scroll owner makes this fail
                // on the reported two-line defect, before the content wrapper.
                const content = tip.querySelector('.badge-tip-content') || tip;
                const r = content.getBoundingClientRect(), s = getComputedStyle(content);
                const outer = tip.getBoundingClientRect();
                const textFits = [...content.querySelectorAll('.badge-tip-title, .badge-tip-detail')].every(el => {
                    const range = document.createRange(); range.selectNodeContents(el);
                    return [...range.getClientRects()].every(t => t.left >= r.left && t.right <= r.right
                        && t.top >= r.top && t.bottom <= r.bottom);
                });
                return {
                    ScrollHeight: content.scrollHeight, ClientHeight: content.clientHeight,
                    ScrollWidth: content.scrollWidth, ClientWidth: content.clientWidth,
                    Gutter: content.offsetWidth - content.clientWidth - parseFloat(s.borderLeftWidth) - parseFloat(s.borderRightWidth),
                    TextFits: textFits, OverflowY: s.overflowY,
                    InViewport: outer.left>=7 && outer.right<=innerWidth-7 && outer.top>=7 && outer.bottom<=innerHeight-7
                };
            }
            """);
        Assert.Equal(fit.ClientHeight, fit.ScrollHeight);
        Assert.Equal(fit.ClientWidth, fit.ScrollWidth);
        Assert.Equal(0, fit.Gutter);
        Assert.Equal("auto", fit.OverflowY);
        Assert.True(fit.TextFits, "Every rendered text line must fit inside the visible content area.");
        Assert.True(fit.InViewport, "The explanation must fit within the viewport edges.");
    }

    private sealed class ExplanationFit
    {
        public int ScrollHeight { get; set; }
        public int ClientHeight { get; set; }
        public int ScrollWidth { get; set; }
        public int ClientWidth { get; set; }
        public double Gutter { get; set; }
        public bool TextFits { get; set; }
        public bool InViewport { get; set; }
        public string OverflowY { get; set; } = "";
    }
}
