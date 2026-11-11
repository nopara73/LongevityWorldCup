using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Fact]
    public async Task AccessFeedback_ReducedMotionKeepsCompletionAndSentArtworkStatic()
    {
        await using var context = await SignInContextAsync();
        await context.RouteAsync("**/api/longevitymaxxing/signup", route => FulfillJsonAsync(route, "{\"message\":\"Check your email.\"}"));
        await context.RouteAsync("**/api/longevitymaxxing/resend", route => FulfillJsonAsync(route, "{\"message\":\"Link sent.\"}"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("[data-select-id='lmxSignupTimeZone']")).ToHaveAttributeAsync("data-wired", "true");
        await page.Locator("#lmxSignupEmail").FillAsync("reader@example.test");
        await page.Locator("#lmxSignupName").FillAsync("Browser Tester");
        var stampAnimations = await page.Locator("#lmxSignupForm").EvaluateAsync<string[]>("""
            form => [...form.children].filter(e => e.matches('.lmx-field, .lmx-identity-field, button[type=submit]'))
                .map(e => getComputedStyle(e, '::before').animationName)
            """);
        Assert.All(stampAnimations, name => Assert.Equal("none", name));
        await page.Locator("#lmxSignupForm button[type=submit]").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxSignupDonePanel")).ToBeVisibleAsync();
        Assert.True(await page.Locator("#lmxSignupDonePanel .lmx-card-head").EvaluateAsync<bool>("""
            head => ['::before', '::after'].every(pseudo => {
                const style = getComputedStyle(head, pseudo);
                return style.animationName === 'none' && style.animationDelay === '0s'
                    && style.opacity === '1' && style.transform === 'none';
            })
            """), "Reduced motion must show the sent artwork immediately, without a delayed tile or flight.");
        await page.Locator("#lmxSigninTab").ClickAsync();
        await page.Locator("#lmxResendEmail").FillAsync("reader@example.test");
        await page.Locator("#lmxResendButton").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxResendStatus.success")).ToBeVisibleAsync();
        Assert.Equal("none", await page.Locator("#lmxResendStatus").EvaluateAsync<string>(
            "e => getComputedStyle(e, '::before').animationName"));
    }

    [Theory]
    [InlineData(305, false, false)]
    [InlineData(320, false, false)]
    [InlineData(390, true, false)]
    [InlineData(320, false, true)]
    public async Task AccessSignup_EnlargedTimezoneControlsKeepShortContentReadable(int width, bool dark, bool nativeScrollbars)
    {
        await using var nativeBrowser = nativeScrollbars ? await Browser.BrowserType.LaunchAsync(new()
        {
            Headless = true,
            IgnoreDefaultArgs = ["--hide-scrollbars"]
        }) : null;
        await using var context = nativeBrowser is null ? await SignInContextAsync(width, dark)
            : await nativeBrowser.NewContextAsync(new()
            {
                BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 844 },
                TimezoneId = "Asia/Bangkok", ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
                ReducedMotion = ReducedMotion.Reduce
            });
        if (nativeBrowser is not null)
        {
            await BrowserTestApp.RouteExternalResourcesAsync(context);
            await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route,
                System.Text.Json.JsonSerializer.Serialize(BuildPublicState())));
        }
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
        await search.FillAsync("America");
        Assert.True(await page.Locator("#lmxSignupTimeZoneList").EvaluateAsync<bool>("""
            list => [...list.children].every(option => option.scrollHeight <= option.clientHeight + 1
                && option.scrollWidth <= option.clientWidth + 1)
            """), "Long result lists must not compress multi-line timezone options into overlapping rows.");
        await search.FillAsync("Tokyo");
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
