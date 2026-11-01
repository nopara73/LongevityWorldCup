using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ScrollChoreographyBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    // Isolate lifecycle cases from other page observers, using the actual served assets.
    private async Task<IPage> Fixture(IBrowserContext context, string markup, string? before = null)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/privacy");
        var source = await page.EvaluateAsync<string>("() => fetch(document.querySelector('script[src*=\"scroll-choreography\"]').src).then(r => r.text())");
        var css = await page.EvaluateAsync<string>("() => fetch(document.querySelector('link[href*=\"scroll-choreography\"]').href).then(r => r.text())");
        await page.GotoAsync("about:blank");
        await page.SetContentAsync("<style>body{margin:0}section{height:1000px}h2{margin:0;height:40px}button{height:44px}</style>" + markup);
        await page.AddStyleTagAsync(new() { Content = css });
        if (before is not null) await page.EvaluateAsync(before);
        await page.AddScriptTagAsync(new() { Content = source });
        return page;
    }

    [Fact]
    public async Task MissingIntersectionObserver_NeverHidesContent()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await Fixture(context, "<section>First viewport</section><section data-lwc-enter id='next'>Next section</section>",
            "delete window.IntersectionObserver");
        Assert.Equal("1", await page.Locator("#next").EvaluateAsync<string>("e => getComputedStyle(e).opacity"));
        Assert.Equal(0, await page.Locator("[data-lwc-enter-state]").CountAsync());
    }

    [Fact]
    public async Task ReducedMotionAtStartup_CanChangeWithoutStrandingNewContent()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await Fixture(context, "<section>First viewport</section>");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.EvaluateAsync("document.body.insertAdjacentHTML('beforeend', '<section data-lwc-enter id=late>Late section</section>')");
        await page.Locator("#late").ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(page.Locator("#late")).ToHaveCSSAsync("opacity", "1");
        await Assertions.Expect(page.Locator("#late")).Not.ToHaveAttributeAsync("data-lwc-enter-state", "wait");
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("hash")]
    [InlineData("print")]
    public async Task ReaderNavigation_ReleasesBothWaitingAndArrivingContentImmediately(string action)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await Fixture(context, "<section>First viewport</section><section data-lwc-enter id='next'><button id='target'>Target</button></section><section data-lwc-enter>Last</section>");
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        await page.EvaluateAsync("window.scrollTo(0, 900)");
        await Assertions.Expect(page.Locator("#next")).ToHaveAttributeAsync("data-lwc-enter-state", "go");
        // Pin a real CSS entrance mid-flight, independent of runner/frame timing.
        var opacity = await page.Locator("#next").EvaluateAsync<double>("""
            e => {
                for (const animation of e.getAnimations()) {
                    if (animation instanceof CSSTransition) { animation.pause(); animation.currentTime = 100; }
                }
                return Number(getComputedStyle(e).opacity);
            }
            """);
        Assert.InRange(opacity, 0.001, 0.999);
        await page.EvaluateAsync("action => { if(action === 'focus') document.getElementById('target').focus(); else if(action === 'hash') { history.replaceState(null,'','#next'); dispatchEvent(new HashChangeEvent('hashchange')); } else dispatchEvent(new Event('beforeprint')); }", action);
        Assert.Equal(0, await page.Locator("[data-lwc-enter-state]").CountAsync());
        Assert.Equal("1", await page.Locator("#next").EvaluateAsync<string>("e => getComputedStyle(e).opacity"));
    }

    [Fact]
    public async Task ClickingAnArrivingLink_DoesNotMoveItBeforeClickDispatch()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await Fixture(context, "<section>First viewport</section><section data-lwc-enter id='next'><a id='target' href='#done'>Open destination</a></section><section id='done'>Destination</section>");
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        await page.EvaluateAsync("window.scrollTo(0, 900)");
        await Assertions.Expect(page.Locator("#next")).ToHaveAttributeAsync("data-lwc-enter-state", "go");
        await page.Locator("#next").EvaluateAsync("e => e.getAnimations().forEach(a => { a.pause(); a.currentTime = 10; })");
        Assert.InRange(await page.Locator("#next").EvaluateAsync<double>("e => Number(getComputedStyle(e).opacity)"), 0.001, 0.999);
        await page.Locator("#target").ClickAsync();
        Assert.Equal("#done", await page.EvaluateAsync<string>("location.hash"));
        Assert.Equal("1", await page.Locator("#next").EvaluateAsync<string>("e => getComputedStyle(e).opacity"));
    }

    [Fact]
    public async Task MovingARevealedElement_DoesNotReplayItsEntrance()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await Fixture(context, "<section data-lwc-enter id='first'>Already visible</section><section>Spacer</section><div id='end'></div>");
        await page.EvaluateAsync("document.getElementById('end').append(document.getElementById('first'))");
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        Assert.Equal(0, await page.Locator("#first[data-lwc-enter-state]").CountAsync());
        Assert.Equal("1", await page.Locator("#first").EvaluateAsync<string>("e => getComputedStyle(e).opacity"));
    }

    [Fact]
    public async Task AsynchronousHandoffs_UpdateOnScrollAndStayOpaqueBeforeSticking()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await Fixture(context, "<section>Spacer</section><main></main><section>Tail</section>");
        await page.EvaluateAsync("document.querySelector('main').innerHTML = '<section data-lwc-handoff id=group><h2>Heading</h2></section>'");
        await Assertions.Expect(page.Locator("#group h2")).ToHaveAttributeAsync("data-lwc-handoff-head", "");
        Assert.Equal("1", await page.Locator("#group h2").EvaluateAsync<string>("e => getComputedStyle(e).opacity"));
        await page.EvaluateAsync("window.scrollTo(0, 1940)");
        await page.WaitForFunctionAsync("parseFloat(document.querySelector('#group h2').style.getPropertyValue('--lwc-handoff')) > 0.3");
        await page.EvaluateAsync("window.scrollTo(0, 1100)");
        await page.WaitForFunctionAsync("document.querySelector('#group h2').style.getPropertyValue('--lwc-handoff') === '0.000'");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Homepage_NoScriptAndReducedMotionKeepEverySectionReadable(bool javaScript)
    {
        await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = javaScript, ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        Assert.Empty(await page.Locator(".section-container").EvaluateAllAsync<string[]>(
            "elements => elements.filter(e => getComputedStyle(e).opacity !== '1').map(e => e.id)"));
        Assert.Equal(0, await page.Locator("[data-lwc-enter-state]").CountAsync());
    }

    [Fact]
    public async Task RemovedContent_ReleasesObserversWithoutRescanningTheDocument()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await Fixture(context, "<section>Spacer</section><section data-lwc-enter id='removed'><strong data-lwc-count-in>123</strong></section>", """
            () => {
                window.choreoTargets = new Map();
                const Native = window.IntersectionObserver;
                window.IntersectionObserver = class extends Native {
                    observe(e) { if (!choreoTargets.has(this)) choreoTargets.set(this, new Set()); choreoTargets.get(this).add(e); super.observe(e); }
                    unobserve(e) { choreoTargets.get(this)?.delete(e); super.unobserve(e); }
                    disconnect() { choreoTargets.get(this)?.clear(); super.disconnect(); }
                };
            }
            """);
        Assert.True(await page.EvaluateAsync<bool>("[...choreoTargets.values()].some(s => s.size > 0)"));
        await page.EvaluateAsync("""
            () => {
                window.choreoDocumentScans = 0;
                for (const name of ['querySelector', 'querySelectorAll']) {
                    const original = document[name].bind(document);
                    document[name] = (...args) => { choreoDocumentScans++; return original(...args); };
                }
                document.getElementById('removed').remove();
                const marker = document.createElement('span'); marker.textContent = 'Unrelated update'; document.body.append(marker);
            }
            """);
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        Assert.True(await page.EvaluateAsync<bool>("[...choreoTargets.values()].every(s => s.size === 0)"));
        Assert.Equal(0, await page.EvaluateAsync<int>("choreoDocumentScans"));
    }
}
