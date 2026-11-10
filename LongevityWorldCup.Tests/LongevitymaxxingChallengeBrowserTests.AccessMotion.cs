using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320, false)]
    [InlineData(390, true)]
    public async Task AccessSignup_EnlargedTimezoneControlsKeepShortContentReadable(int width, bool dark)
    {
        await using var context = await SignInContextAsync(width, dark);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("[data-select-id='lmxSignupTimeZone']")).ToHaveAttributeAsync("data-wired", "true");
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.Locator("#lmxSignupTimeZoneButton").ClickAsync();
        var search = page.Locator("#lmxSignupTimeZoneSearch");
        await search.FillAsync("Tokyo");
        await Assertions.Expect(page.Locator("#lmxSignupTimeZoneList .lmx-timezone-option")).ToHaveCountAsync(1);
        Assert.True(await page.Locator("#lmxSignupTimeZoneButton [data-timezone-display]").EvaluateAsync<bool>(
            "e => e.scrollWidth <= e.clientWidth + 1 && e.scrollHeight <= e.clientHeight + 1"));
        Assert.InRange(await search.EvaluateAsync<double>("e => e.scrollLeft"), 0, 1);
        Assert.True(await page.Locator("#lmxSignupTimeZoneList").EvaluateAsync<bool>(
            "e => e.scrollHeight <= e.clientHeight + 1 && e.scrollWidth <= e.clientWidth + 1"));
        await search.PressAsync("ArrowDown");
        await search.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#lmxSignupTimeZone")).ToHaveValueAsync("Asia/Tokyo");
        await Assertions.Expect(page.Locator("#lmxSignupTimeZoneButton")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccessTabs_KeyboardUsesTheSameSettlingCardMotion(bool reduced)
    {
        await using var context = await SignInContextAsync();
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference });
        await OpenSignInPageAsync(page);
        await WaitForFiniteAnimationsAsync(page);
        await page.Locator("#lmxSigninTab").PressAsync("Home");
        await Assertions.Expect(page.Locator("#lmxSignupTab")).ToBeFocusedAsync();
        var heightEffects = await page.Locator(".lmx-action-card").EvaluateAsync<int>(
            "e => e.getAnimations().filter(a => a.effect.getKeyframes().some(k => 'height' in k)).length");
        Assert.Equal(reduced ? 0 : 1, heightEffects);
        await WaitForFiniteAnimationsAsync(page);
        await Assertions.Expect(page.Locator("#lmxSignupPanel")).ToBeVisibleAsync();
        Assert.Equal("visible", await page.Locator(".lmx-action-card").EvaluateAsync<string>("e => getComputedStyle(e).overflow"));
    }

    [Fact]
    public async Task AccessTabs_RapidReversalReplacesTheObsoleteHeightTarget()
    {
        await using var context = await SignInContextAsync();
        var page = await context.NewPageAsync();
        await OpenSignInPageAsync(page);
        await WaitForFiniteAnimationsAsync(page);
        var signInHeight = await page.Locator(".lmx-action-card").EvaluateAsync<double>("e => e.getBoundingClientRect().height");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        var targets = await page.EvaluateAsync<double[]>("""
            () => {
                document.getElementById('lmxSignupTab').click();
                const card = document.querySelector('.lmx-action-card');
                // Reverse halfway through the first transition without relying on wall-clock timing.
                for (const animation of card.getAnimations()) animation.currentTime = 140;
                document.getElementById('lmxSigninTab').click();
                return card.getAnimations().filter(a => a.effect.getKeyframes().some(k => 'height' in k))
                    .map(a => parseFloat(a.effect.getKeyframes().at(-1).height));
            }
            """);
        Assert.Single(targets);
        Assert.InRange(targets[0], signInHeight - 1, signInHeight + 1);
        await WaitForFiniteAnimationsAsync(page);
        Assert.InRange(await page.Locator(".lmx-action-card").EvaluateAsync<double>("e => e.getBoundingClientRect().height"), signInHeight - 1, signInHeight + 1);
        await Assertions.Expect(page.Locator("#lmxResendEmail")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(1440, false, false)]
    [InlineData(390, true, false)]
    [InlineData(320, false, true)]
    [InlineData(320, true, true)]
    public async Task AccessTabs_OneUnderlineFollowsTheSelectedTabAfterReflow(int width, bool dark, bool enlarged)
    {
        await using var context = await SignInContextAsync(width, dark);
        var page = await OpenSignInAsync(context);
        if (enlarged) await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await Assertions.Expect(page.Locator("#lmxAccessTabs > .lwc-segment-glide")).ToHaveAttributeAsync("data-ready", "");
        foreach (var tab in new[] { "#lmxSignupTab", "#lmxSigninTab" })
        {
            await page.Locator(tab).ClickAsync();
            await page.WaitForFunctionAsync("""
                () => {
                    const tabs = document.getElementById('lmxAccessTabs');
                    const active = tabs.querySelector('[aria-selected=true]');
                    const glide = tabs.querySelector('.lwc-segment-glide');
                    const a = active.getBoundingClientRect(), g = glide.getBoundingClientRect();
                    return Math.abs(a.left - g.left) <= 1 && Math.abs(a.bottom - g.bottom) <= 1 && Math.abs(a.width - g.width) <= 1;
                }
                """);
            Assert.True(await page.Locator("#lmxAccessTabs").EvaluateAsync<bool>("""
                tabs => {
                    const active = tabs.querySelector('[aria-selected=true]');
                    const glide = tabs.querySelector('.lwc-segment-glide');
                    const a = getComputedStyle(active), g = getComputedStyle(glide);
                    return a.borderBottomColor === 'rgba(0, 0, 0, 0)' && g.boxShadow !== 'none'
                        && ['none', 'normal'].includes(getComputedStyle(tabs, '::after').content)
                        && tabs.scrollWidth <= tabs.clientWidth + 1;
                }
                """));
        }
    }
}
