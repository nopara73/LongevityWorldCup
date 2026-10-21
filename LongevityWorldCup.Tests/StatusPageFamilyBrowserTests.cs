using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// 404, Unsubscribe and the outage fallbacks are one status-page family: a centred sheet whose cover
// opens with a round seal, then the title on the same axis, then the page's one way out.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class StatusPageFamilyBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/no-such-page", 1440, 900, false)]
    [InlineData("/athletes/nobody-here/at-all", 390, 844, true)]
    [InlineData("/unsubscribe", 1280, 800, true)]
    [InlineData("/unsubscribe", 390, 844, false)]
    [InlineData("/error/503.html", 1440, 900, false)]
    [InlineData("/error/504.html", 390, 844, true)]
    [InlineData("/error/502.html", 640, 360, false)]
    [InlineData("/no-such-page", 640, 360, true)]
    public async Task StatusPages_ShareOneCentredSheetOpenedByARoundSeal(string path, int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.EvaluateAsync(
            "async () => { await document.fonts.ready; await Promise.all([...document.images].map(i => i.decode().catch(() => {}))); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const outage = location.pathname.startsWith('/error/');
                const sheet = document.querySelector(outage ? '.error-card' : 'main .status-sheet');
                const seal = document.querySelector(outage ? '.visual' : '.status-seal');
                const title = sheet?.querySelector('h1');
                const exit = sheet?.querySelector(outage ? '.actions button' : ':is(.landing-close .option-button, #unsubscribe-button)');
                if (!sheet || !seal || !title || !exit) return ['missing sheet, seal, title or way out'];
                const issues = [];
                const box = element => element.getBoundingClientRect();
                const s = box(sheet), r = box(seal), t = box(title), e = box(exit);
                const centre = b => b.left + b.width / 2;
                // Under the site header, short landscape screens have no height for a stack.
                const row = !outage && innerWidth >= 560 && innerHeight <= 480;
                if (Math.abs(r.width - r.height) > 1 || getComputedStyle(seal).borderTopLeftRadius !== '50%') issues.push(`seal is not a disc (${r.width}x${r.height})`);
                if (r.width < 72) issues.push(`seal collapsed to ${r.width}px`);
                if (row) {
                    if (!(r.right <= t.left && r.top < t.bottom && t.top < r.bottom)) issues.push('seal does not open the row beside the title');
                } else {
                    if (Math.abs(centre(r) - centre(s)) > 2 || Math.abs(centre(t) - centre(s)) > 2) issues.push('seal and title are not on the sheet axis');
                    if (getComputedStyle(title).textAlign !== 'center') issues.push('title is not centred');
                    if (!(r.bottom <= t.top)) issues.push('seal does not open the cover above the title');
                }
                if (!(t.bottom <= e.top)) issues.push('way out does not follow the title');
                if (s.width > 642) issues.push(`sheet widened to ${s.width}px`);
                if (s.left < -0.5 || s.right > innerWidth + 0.5) issues.push('sheet escapes the viewport');
                if (e.bottom > innerHeight + 0.5) issues.push(`way out sits below the first screen (${e.bottom} of ${innerHeight})`);
                const image = seal.querySelector('img');
                if (image && !(image.complete && image.naturalWidth > 0)) issues.push('seal photo did not load');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{path} at {width}x{height} {(dark ? "dark" : "light")}: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(390, 844, "dark")]
    public async Task Unsubscribe_SealAnswersTheOutcome(int width, int height, string theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/unsubscribe");
        var panel = page.Locator(".unsubscribe-panel");
        var icon = page.Locator(".status-seal i");
        var idle = await SealAsync(page);
        await Assertions.Expect(panel).ToHaveAttributeAsync("data-state", "idle");

        await page.RouteAsync("**/api/home/unsubscribe", route => route.FulfillAsync(new() { Status = 500, Body = "" }));
        await page.Locator("#email").FillAsync("athlete@example.com");
        await page.Locator("#unsubscribe-button").ClickAsync();
        await Assertions.Expect(panel).ToHaveAttributeAsync("data-state", "error");
        await Assertions.Expect(icon).ToHaveClassAsync("fas fa-envelope");
        var error = await SealAsync(page);

        await page.UnrouteAllAsync();
        await page.RouteAsync("**/api/home/unsubscribe", route => route.FulfillAsync(new() { Status = 200, Body = "" }));
        await page.Locator("#unsubscribe-button").ClickAsync();
        await Assertions.Expect(panel).ToHaveAttributeAsync("data-state", "done");
        await Assertions.Expect(icon).ToHaveClassAsync("fas fa-envelope-circle-check");
        var done = await SealAsync(page);

        await page.Locator("#email").FillAsync("x");
        await Assertions.Expect(panel).ToHaveAttributeAsync("data-state", "idle");

        Assert.Equal(idle[1], await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.status-seal')).color"));
        Assert.Equal(error[1], await page.EvaluateAsync<string>("() => { const p = document.createElement('i'); p.style.color = 'var(--lwc-danger)'; document.body.append(p); const c = getComputedStyle(p).color; p.remove(); return c; }"));
        Assert.Equal(done[1], await page.EvaluateAsync<string>("() => { const p = document.createElement('i'); p.style.color = 'var(--lwc-success)'; document.body.append(p); const c = getComputedStyle(p).color; p.remove(); return c; }"));
        Assert.NotEqual(idle[0], error[0]);
        Assert.NotEqual(error[0], done[0]);
    }

    private static Task<string[]> SealAsync(IPage page) => page.EvaluateAsync<string[]>(
        """
        () => {
            const seal = getComputedStyle(document.querySelector('.status-seal'));
            return [seal.color + ' ' + seal.boxShadow, seal.color];
        }
        """);
}
