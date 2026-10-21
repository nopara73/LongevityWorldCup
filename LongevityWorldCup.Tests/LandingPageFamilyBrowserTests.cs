using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// The Rejuvenation Olympics guide, the Reddit app policy pages and Unsubscribe are one landing-page family:
// one sheet with a muted cover band (accent bar, eyebrow, bold title), a body and an accent-soft closing band.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LandingPageFamilyBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static readonly string[] FamilyPages = ["/rejuvenation-olympics", "/reddit-privacy.html", "/reddit-terms.html", "/unsubscribe"];

    [Theory]
    [InlineData(1440, 200)]
    [InlineData(1024, 200)]
    [InlineData(768, 200)]
    [InlineData(390, 200)]
    [InlineData(320, 200)]
    [InlineData(160, 100)]
    [InlineData(160, 200)]
    public async Task NarrowAndEnlargedText_KeepsTextInsideItsReadingColumn(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.RouteAsync("**/api/home/unsubscribe", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        foreach (var path in FamilyPages)
        {
            await NavigateAndSettleAsync(page, path);
            await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
            if (path == "/unsubscribe")
                await page.Locator("#unsubscribe-button").ClickAsync();
            var overflowing = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const sheet = document.querySelector('main .landing-sheet').getBoundingClientRect();
                    const issues = [];
                    for (const element of document.querySelectorAll('.landing-sheet :is(h1,h2,h3,p,li,time,button,label)')) {
                        if (!element.getClientRects().length) continue;
                        const column = element.getBoundingClientRect();
                        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                        while (walker.nextNode()) {
                            const node = walker.currentNode;
                            if (!node.textContent.trim()) continue;
                            const range = document.createRange();
                            range.selectNodeContents(node);
                            if ([...range.getClientRects()].some(rect => rect.width > 0 &&
                                (rect.left < Math.max(sheet.left, column.left) - 1 || rect.right > Math.min(sheet.right, column.right) + 1)))
                                issues.push(`${element.tagName}: ${node.textContent.trim()}`);
                        }
                    }
                    return [...new Set(issues)];
                }
                """);
            Assert.True(overflowing.Length == 0, $"{path} {width}px {textPercent}%: {string.Join("; ", overflowing)}");
            await ExpectNoHorizontalOverflowAsync(page);
        }
    }

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(1024, 768, "dark")]
    [InlineData(768, 1024, "light")]
    [InlineData(390, 844, "dark")]
    [InlineData(320, 640, "light")]
    public async Task EveryFamilyPage_SharesTheSheetCoverAndBoldTitle(int width, int height, string theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        foreach (var path in FamilyPages)
        {
            await NavigateAndSettleAsync(page, path);
            var scenario = $"{path} at {width}px {theme}";
            var problems = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const issues = [];
                    const sheet = document.querySelector('main .landing-sheet');
                    const cover = sheet?.querySelector(':scope > .landing-cover');
                    const title = cover?.querySelector('h1');
                    if (!sheet || !cover || !title) return ['no landing sheet with a cover and title'];
                    const probe = document.createElement('div');
                    probe.style.cssText = 'background: var(--lwc-surface-muted); color: var(--lwc-accent)';
                    document.body.append(probe);
                    const muted = getComputedStyle(probe).backgroundColor, accent = getComputedStyle(probe).color;
                    probe.remove();
                    const bar = getComputedStyle(cover, '::before');
                    if (bar.width !== '48px' || bar.height !== '4px' || bar.backgroundColor !== accent) issues.push(`accent bar ${bar.width}x${bar.height} ${bar.backgroundColor}`);
                    if (getComputedStyle(cover).backgroundColor !== muted) issues.push('cover band is not the muted surface');
                    const sheetBox = sheet.getBoundingClientRect(), coverBox = cover.getBoundingClientRect();
                    if (Math.abs(coverBox.top - sheetBox.top) > 1.5 || Math.abs(coverBox.width - sheetBox.width) > 2.5) issues.push('cover band does not span the top of the sheet');
                    if (getComputedStyle(title).fontWeight !== '700') issues.push(`title weight ${getComputedStyle(title).fontWeight}`);
                    const fonts = [...document.fonts].filter(font => font.family.replaceAll('"', '') === 'Roboto' && font.status === 'loaded');
                    if (!fonts.some(font => font.weight === '700')) issues.push('Roboto Bold is not loaded, so bold headings fall back');
                    const eyebrow = cover.querySelector('.landing-eyebrow');
                    if (eyebrow && getComputedStyle(eyebrow).textTransform !== 'uppercase') issues.push('eyebrow is not an uppercase section label');
                    if (sheetBox.left < -0.5 || sheetBox.right > innerWidth + 0.5) issues.push('sheet escapes the viewport');
                    return issues;
                }
                """);
            Assert.True(problems.Length == 0, $"{scenario}: {string.Join("; ", problems)}");
            await ExpectNoHorizontalOverflowAsync(page);
        }
    }

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(1024, 768, "dark")]
    [InlineData(768, 1024, "light")]
    [InlineData(390, 844, "dark")]
    [InlineData(320, 640, "light")]
    public async Task RejuvenationOlympics_IsOneSheetOverThePosterWithTimelineAndActionBands(int width, int height, string theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/rejuvenation-olympics");
        var layout = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const box = selector => document.querySelector(selector)?.getBoundingClientRect();
                const sheet = box('.ro-essay.landing-sheet'), poster = box('.ro-artwork');
                if (!sheet) return ['the essay is not one sheet'];
                if (!(sheet.top < poster.bottom - 16)) issues.push('sheet does not ride over the poster');
                for (const selector of ['.ro-intro', '.ro-body', '.ro-timeline', '.ro-actions']) {
                    const part = box(`.ro-essay > ${selector}`);
                    if (!part || Math.abs(part.width - sheet.width) > 2.5) issues.push(`${selector} is not a full-width band of the sheet`);
                }
                const actions = box('.ro-essay > .ro-actions');
                if (actions && Math.abs(actions.bottom - sheet.bottom) > 1.5) issues.push('actions do not close the sheet');
                const buttons = [...document.querySelectorAll('.ro-actions a')].map(a => a.getBoundingClientRect());
                const left = Math.min(...buttons.map(b => b.left)) - actions.left, right = actions.right - Math.max(...buttons.map(b => b.right));
                if (innerWidth > 700 && Math.abs(left - right) > 2) issues.push(`actions are not centred (${left.toFixed(1)} vs ${right.toFixed(1)})`);
                for (const time of document.querySelectorAll('.ro-milestones time')) {
                    if (!getComputedStyle(time).fontFamily.includes('Orbitron')) issues.push('milestone dates are not competition marks');
                    if (time.scrollWidth > time.clientWidth + 1 || time.getBoundingClientRect().right > sheet.right) issues.push(`${time.textContent} overflows`);
                }
                if (getComputedStyle(document.querySelector('.ro-timeline h2')).textTransform !== 'uppercase') issues.push('timeline title is not a section label');
                return issues;
            }
            """);
        Assert.True(layout.Length == 0, $"{width}px {theme}: {string.Join("; ", layout)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(390, 844, "dark")]
    public async Task Unsubscribe_StatusCalloutsCarryDistinctGlyphsAndTheFieldMatchesTheButton(int width, int height, string theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/unsubscribe");
        var sizes = await page.EvaluateAsync<double[]>(
            "() => ['#email', '#unsubscribe-button'].map(selector => document.querySelector(selector).getBoundingClientRect().height)");
        Assert.True(sizes[0] >= 56 && Math.Abs(sizes[0] - sizes[1]) <= 1, $"field {sizes[0]}px, button {sizes[1]}px");

        await page.Locator("#email").FillAsync("not-an-email");
        await page.Locator("#unsubscribe-button").ClickAsync();
        await Assertions.Expect(page.Locator("#message")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-error"));
        var error = await GlyphAsync(page);

        await page.RouteAsync("**/api/home/unsubscribe", route => route.FulfillAsync(new() { Status = 200, Body = "" }));
        await page.Locator("#email").FillAsync("athlete@example.com");
        await page.Locator("#unsubscribe-button").ClickAsync();
        await Assertions.Expect(page.Locator("#message")).ToHaveTextAsync("Removed athlete@example.com from the yearly newsletter.");
        var success = await GlyphAsync(page);

        Assert.DoesNotContain("✓", await page.Locator("#message").AriaSnapshotAsync());

        Assert.Equal("✕", error[0]);
        Assert.Equal("✓", success[0]);
        Assert.NotEqual(error[1], success[1]);
        Assert.NotEqual(error[2], success[2]);
    }

    private static Task<string[]> GlyphAsync(IPage page) => page.EvaluateAsync<string[]>(
        """
        () => {
            const message = document.getElementById('message');
            const glyph = getComputedStyle(message, '::before');
            return [glyph.content.split(' / ')[0].replaceAll('"', ''), glyph.backgroundColor, getComputedStyle(message).backgroundColor];
        }
        """);
}
