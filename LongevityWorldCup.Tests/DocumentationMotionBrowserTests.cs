using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DocumentationMotionBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task Rules_InitializeOnceReverseFoldsAndKeepTheirAccessibleState()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await page.EvaluateAsync("document.dispatchEvent(new Event('DOMContentLoaded'))");
        Assert.Equal(4, await page.Locator(".ruleset-rule-toggle").CountAsync());
        var rule = page.Locator(".ruleset-rule").First;
        var toggle = rule.Locator(".ruleset-rule-toggle");
        var heading = rule.Locator("h2");
        Assert.Equal(await heading.GetAttributeAsync("id"), await toggle.GetAttributeAsync("aria-labelledby"));
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(r))");
        await page.Keyboard.PressAsync("Space");
        await WaitForFoldsAsync(page);
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.Null(await rule.Locator(".ruleset-rule-body").GetAttributeAsync("hidden"));
        Assert.Equal("1", await rule.Locator(".ruleset-rule-body").EvaluateAsync<string>("e => getComputedStyle(e).opacity"));
        Assert.True(await toggle.EvaluateAsync<bool>("e => e === document.activeElement"));
    }

    [Fact]
    public async Task MotionPreferenceChange_SettlesFoldsAndRevealsPermanently()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await page.Locator(".ruleset-rule-toggle").First.ClickAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => !document.querySelector('.documentation-reveal:not(.is-revealed)')");
        Assert.Equal("until-found", await page.Locator(".ruleset-rule-body").First.GetAttributeAsync("hidden"));
        Assert.Equal(0, await page.EvaluateAsync<int>("document.getAnimations().filter(a => a.id === 'documentation-fold').length"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        Assert.Equal(0, await page.Locator(".documentation-reveal:not(.is-revealed)").CountAsync());
    }

    [Fact]
    public async Task Print_ExposesEveryFoldWithoutChangingScreenState()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await page.Locator(".ruleset-rule-toggle").First.ClickAsync();
        await page.EmulateMediaAsync(new() { Media = Media.Print });
        var hidden = await page.Locator(".ruleset-rule-body,.ruleset-faq-answer").EvaluateAllAsync<string[]>(
            "elements => elements.filter(e => getComputedStyle(e).contentVisibility === 'hidden' || getComputedStyle(e).display === 'none').map(e => e.id)");
        Assert.Empty(hidden);
        await page.EmulateMediaAsync(new() { Media = Media.Screen });
        Assert.Equal("until-found", await page.Locator(".ruleset-rule-body").First.GetAttributeAsync("hidden"));
    }

    [Fact]
    public async Task NativeFragment_ReopensFoldedRuleAndKeepsToggleSynchronized()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        var toggle = page.Locator(".ruleset-rule-toggle").First;
        await toggle.ClickAsync();
        await page.EvaluateAsync("() => { window.matchCount = 0; document.addEventListener('beforematch', () => window.matchCount++); location.hash = document.querySelector('.ruleset-rule-body').id; }");
        await page.WaitForFunctionAsync("() => window.matchCount > 0");
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.Null(await page.Locator(".ruleset-rule-body").First.GetAttributeAsync("hidden"));
    }

    [Fact]
    public async Task SmoothJump_YieldsToReaderAndPreservesHistoryState()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        await page.EvaluateAsync("history.replaceState({returnTo:'reader'}, '', location.href)");
        await page.Locator(".documentation-nav a[href^='#']").Last.EvaluateAsync("e => e.click()");
        await page.WaitForFunctionAsync("() => scrollY > 30");
        var stopped = await page.EvaluateAsync<double>("() => { window.dispatchEvent(new Event('touchstart')); return scrollY; }");
        await page.WaitForTimeoutAsync(500);
        Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("scrollY") - stopped), 0, 1);
        Assert.Equal("reader", await page.EvaluateAsync<string>("history.state.returnTo"));
        Assert.Equal(0, await page.Locator(".is-arriving").CountAsync());
    }

    [Fact]
    public async Task RestoredScroll_RevealsTheReadingPositionWithoutAnEntrance()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        var hidden = await page.EvaluateAsync<int>("""
            () => {
                scrollTo({ top: document.body.scrollHeight / 2, behavior: 'instant' });
                dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
                return [...document.querySelectorAll('.documentation-reveal:not(.is-revealed)')]
                    .filter(e => e.getBoundingClientRect().top < innerHeight).length;
            }
            """);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public async Task ClosingContents_ImmediatelyLeavesKeyboardOrder()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/privacy");
        var toggle = page.Locator(".documentation-nav-toggle");
        await toggle.ClickAsync();
        await page.Locator(".documentation-nav a").First.FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.True(await toggle.EvaluateAsync<bool>("e => document.activeElement === e"));
        Assert.True(await page.Locator(".documentation-nav-links").EvaluateAsync<bool>("e => e.inert"));
        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForFunctionAsync("() => !document.querySelector('.documentation-nav-links').inert");
    }

    [Fact]
    public async Task MissingBeforematchSupport_LeavesAllRulesAndAnswersReadable()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.AddInitScriptAsync("delete HTMLElement.prototype.onbeforematch");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        Assert.Equal(0, await page.Locator(".ruleset-rule-toggle,.ruleset-faq-toggle,.ruleset-rule-body[hidden],.ruleset-faq-answer[hidden]").CountAsync());
    }

    [Theory]
    [InlineData("/ruleset")]
    [InlineData("/privacy")]
    [InlineData("/about")]
    public async Task ShortContentsSheet_FitsWithoutAnUnnecessaryScrollbar(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await page.Locator(".documentation-nav-toggle").ClickAsync();
        Assert.True(await page.Locator(".documentation-nav-links").EvaluateAsync<bool>(
            "e => e.scrollHeight <= e.clientHeight + 1 && e.scrollWidth <= e.clientWidth + 1 && e.getBoundingClientRect().bottom < innerHeight"),
            $"{path}: the short contents list should fit its available viewport without a scrollbar.");
    }

    private static Task WaitForFoldsAsync(IPage page) => page.WaitForFunctionAsync(
        "() => document.getAnimations().every(a => a.id !== 'documentation-fold')");
}
