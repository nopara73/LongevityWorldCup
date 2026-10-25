using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DepthSystemBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlayPanels_StayStillAndReadableWhileTheirControlsAreUsed(bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await page.WaitForFunctionAsync("() => window.playAthleteFlow && !document.documentElement.hasAttribute('data-initial-view')");
        await page.Locator("#newGameBtn").ClickAsync();
        await Assertions.Expect(page.Locator(".play-join-card").First).ToBeVisibleAsync();
        await WaitForFiniteAnimationsAsync(page);
        await page.Locator(".play-join-card").First.HoverAsync();
        await WaitForFiniteAnimationsAsync(page);
        var state = await page.Locator(".play-join-card").First.EvaluateAsync<string[]>("""
            card => {
                const s = getComputedStyle(card);
                return [s.translate, s.filter, s.opacity];
            }
            """);
        Assert.Equal("none", state[0]);
        Assert.Equal("none", state[1]);
        Assert.Equal("1", state[2]);
    }

    [Fact]
    public async Task StickyDepth_UsesTheScrollportInsideItsBorderAndReleasesOnReverseScroll()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/privacy");
        await page.EvaluateAsync("""
            () => {
                const scroller = document.createElement('div');
                scroller.id = 'depth-test-scrollport';
                scroller.style.cssText = 'position:fixed;left:20px;top:120px;width:240px;height:220px;overflow:auto;border:8px solid black;z-index:99999';
                scroller.innerHTML = '<div style="height:80px"></div><div data-lwc-sticky id="depth-test-bar" style="position:sticky;top:0;height:40px;background:white">Sticky heading</div><div style="height:800px"></div>';
                document.body.append(scroller);
            }
            """);
        var bar = page.Locator("#depth-test-bar");
        await Assertions.Expect(bar).Not.ToHaveAttributeAsync("data-lwc-stuck", "");
        await page.EvaluateAsync("document.getElementById('depth-test-scrollport').scrollTop = 180");
        await Assertions.Expect(bar).ToHaveAttributeAsync("data-lwc-stuck", "");
        Assert.Equal("none", await bar.EvaluateAsync<string>("e => getComputedStyle(e).filter"));
        await page.EvaluateAsync("document.getElementById('depth-test-scrollport').scrollTop = 0");
        await Assertions.Expect(bar).Not.ToHaveAttributeAsync("data-lwc-stuck", "");
    }

    [Fact]
    public async Task StickyDepth_DoesNotRescanTheDocumentOrWalkStylesOnScroll()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        // A bounded synthetic fixture isolates the new observer from the site's
        // other observers. The real shared stylesheet and runtime are used.
        await page.GotoAsync("/privacy");
        var script = await page.EvaluateAsync<string>("document.querySelector('script[src*=\"depth-system\"]').src");
        var source = await page.EvaluateAsync<string>("url => fetch(url).then(response => response.text())", script);
        await page.GotoAsync("about:blank");
        await page.SetContentAsync("<style>body{margin:0}.bar{position:sticky;top:0;height:30px}section{height:300px}</style><main></main>");
        await page.EvaluateAsync("""
            () => {
                document.querySelector('main').innerHTML = '<section><div class="bar" data-lwc-sticky>Heading</div></section>'.repeat(40);
            }
            """);
        await page.AddScriptTagAsync(new() { Content = source });
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        await page.EvaluateAsync("""
            () => {
                window.depthReads = 0; window.depthScans = 0;
                const styles = window.getComputedStyle;
                window.getComputedStyle = (...args) => { window.depthReads++; return styles(...args); };
                const query = document.querySelectorAll.bind(document);
                document.querySelectorAll = (...args) => { window.depthScans++; return query(...args); };
                const node = document.createElement('span'); node.style.position = 'fixed'; node.textContent = 'Unrelated update'; document.body.append(node);
                window.scrollTo(0, 170);
            }
            """);
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.depthScans"));
        Assert.Equal(0, await page.EvaluateAsync<int>("window.depthReads"));
    }
}
