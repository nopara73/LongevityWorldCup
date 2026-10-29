using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// On phones and tablets the folded contents stay under the site header as a bar that
// names the section being read, fills with reading progress and opens over the document.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DocumentationContentsBarBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/about", 390, 844, "light")]
    [InlineData("/history", 390, 844, "dark")]
    [InlineData("/ruleset", 768, 1024, "dark")]
    [InlineData("/privacy", 320, 640, "light")]
    public async Task ContentsBar_StaysUnderTheHeaderNamingTheSectionBeingRead(string path, int width, int height, string theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        var scenario = $"{path} at {width}px {theme}";

        // Read from the middle of the document.
        await page.EvaluateAsync(
            """
            () => {
                const article = document.querySelector('.documentation-document').getBoundingClientRect();
                window.scrollTo(0, window.scrollY + article.top + article.height / 2);
            }
            """);
        await page.WaitForFunctionAsync("() => document.querySelector('.documentation-nav-current')?.textContent !== 'On this page'");

        var reading = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const nav = document.querySelector('.documentation-nav'), bar = nav.querySelector('.documentation-nav-toggle').getBoundingClientRect();
                const header = document.getElementById('site-sticky-header')?.getBoundingClientRect();
                if (bar.bottom <= 0 || bar.top >= innerHeight) issues.push('the contents bar scrolled away');
                if (header && (bar.top < header.bottom - 0.5 || bar.top > header.bottom + 16)) issues.push(`the bar is not under the header (${bar.top} vs ${header.bottom})`);
                const active = nav.querySelector('a.is-active');
                const label = nav.querySelector('.documentation-nav-current');
                if (!active || label.textContent.trim() !== active.textContent.replace(/\s+/g, ' ').trim())
                    issues.push(`the bar names "${label.textContent}" while reading "${active?.textContent}"`);
                if (label.scrollWidth > label.clientWidth + 1 && getComputedStyle(label.firstElementChild).textOverflow !== 'ellipsis') issues.push('a long section name spills');
                const fill = getComputedStyle(nav.querySelector('.documentation-nav-toggle'), '::after');
                const scale = new DOMMatrixReadOnly(fill.transform).a;
                if (fill.content === 'none' || !(scale > 0.05 && scale < 0.98)) issues.push(`reading progress is ${scale}`);
                if (getComputedStyle(nav).boxShadow === 'none') issues.push('the stuck bar does not lift off the document');
                return issues;
            }
            """);
        Assert.True(reading.Length == 0, $"{scenario}: {string.Join("; ", reading)}");

        // Opening the list overlays the document instead of pushing it down.
        var before = await page.EvaluateAsync<double>("() => document.querySelector('.documentation-document').getBoundingClientRect().top");
        await page.Locator(".documentation-nav-toggle").ClickAsync();
        var open = await page.EvaluateAsync<double[]>(
            """
            () => {
                const list = document.querySelector('.documentation-nav-links').getBoundingClientRect();
                const active = document.querySelector('.documentation-nav a.is-active').getBoundingClientRect();
                return [document.querySelector('.documentation-document').getBoundingClientRect().top, list.height,
                    active.top >= list.top - 1 && active.bottom <= list.bottom + 1 ? 1 : 0];
            }
            """);
        Assert.True(Math.Abs(open[0] - before) < 1, $"{scenario}: opening the contents moved the document by {open[0] - before:0.0}px");
        Assert.True(open[1] > 100, $"{scenario}: the open list is {open[1]:0}px tall");
        Assert.True(open[2] == 1, $"{scenario}: the open list does not show the section being read");

        await page.Keyboard.PressAsync("Escape");
        Assert.Equal("false", await page.Locator(".documentation-nav-toggle").GetAttributeAsync("aria-expanded"));
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/ruleset", "#prizes-and-payouts")]
    [InlineData("/privacy", "#storage-and-retention")]
    public async Task ContentsBar_JumpsLandHeadingsBelowTheBar(string path, string href)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await page.EvaluateAsync("() => document.querySelector('.documentation-nav-toggle').click()");
        await page.Locator($".documentation-nav a[href='{href}']").ClickAsync();
        await page.WaitForFunctionAsync("href => document.querySelector('.documentation-nav a.is-active')?.getAttribute('href') === href", href);

        var layout = await page.EvaluateAsync<double[]>(
            """
            href => [
                document.getElementById(href.slice(1)).getBoundingClientRect().top,
                document.querySelector('.documentation-nav').getBoundingClientRect().bottom,
                document.querySelector('.documentation-nav').classList.contains('is-open') ? 1 : 0
            ]
            """, href);
        Assert.True(layout[0] >= layout[1], $"{path}: the heading lands under the bar ({layout[0]:0.0} < {layout[1]:0.0})");
        Assert.True(layout[0] - layout[1] < 48, $"{path}: the heading lands {layout[0] - layout[1]:0}px below the bar");
        Assert.Equal(0, layout[2]);
        var label = await page.Locator(".documentation-nav-current").TextContentAsync();
        var linkText = await page.Locator($".documentation-nav a[href='{href}']").TextContentAsync();
        Assert.Equal(linkText?.Trim(), label?.Trim());
    }

    [Theory]
    [InlineData("/privacy", "#storage-and-retention")]
    [InlineData("/ruleset", "#prizes-and-payouts")]
    public async Task ContentsBar_UpdatesTheLabelInTheSameClickAsTheActiveLink(string path, string href)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        var synchronized = await page.EvaluateAsync<bool>("""
            href => {
                const link = document.querySelector(`.documentation-nav a[href='${href}']`);
                document.querySelector('.documentation-nav-toggle').click();
                link.click();
                return link.getAttribute('aria-current') === 'location' &&
                    document.querySelector('.documentation-nav-current').textContent.trim() === link.textContent.trim();
            }
            """, href);
        Assert.True(synchronized, "The selected link and visible bar label must agree before a scroll frame runs.");
    }

    [Theory]
    [InlineData("/privacy", "#storage-and-retention")]
    [InlineData("/about", "#how-it-works")]
    [InlineData("/ruleset", "#prizes-and-payouts")]
    public async Task ContentsBar_RefitsToAResizedHeaderAndKeepsKeyboardDismissal(string path, string href)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 640 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path + href);
        await page.WaitForFunctionAsync("href => document.querySelector('.documentation-nav a.is-active')?.hash === href", href);
        await page.EvaluateAsync("document.getElementById('site-sticky-header').style.minHeight = '140px'");
        await page.WaitForFunctionAsync("""
            href => document.getElementById(href.slice(1)).getBoundingClientRect().top >=
                document.querySelector('.documentation-nav-toggle').getBoundingClientRect().bottom
            """, href);
        await page.Locator(".documentation-nav-toggle").ClickAsync();
        var listFits = await page.Locator(".documentation-nav-links").EvaluateAsync<bool>("e => e.getBoundingClientRect().bottom <= innerHeight");
        Assert.True(listFits, "The open contents must fit below the resized header and bar.");
        await page.Locator($".documentation-nav a[href='{href}']").FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal("false", await page.Locator(".documentation-nav-toggle").GetAttributeAsync("aria-expanded"));
        Assert.True(await page.Locator(".documentation-nav-toggle").EvaluateAsync<bool>("e => e === document.activeElement"));
        await page.Keyboard.PressAsync("Enter");
        await page.Locator(".documentation-nav a[href='#contact'], .documentation-source-link").Last.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.Equal("false", await page.Locator(".documentation-nav-toggle").GetAttributeAsync("aria-expanded"));
    }

    [Theory]
    [InlineData(844, 390)]
    [InlineData(320, 422)]
    public async Task ShortScreens_KeepTheStaticContentsAboveTheDocument(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/privacy");
        var layout = await page.EvaluateAsync<double[]>("""
            () => [document.querySelector('.documentation-nav-toggle').getBoundingClientRect().bottom,
                document.querySelector('.documentation-document').getBoundingClientRect().top]
            """);
        Assert.True(layout[0] <= layout[1], $"The static contents overlap the document ({layout[0]} > {layout[1]}).");
        await page.Locator(".documentation-nav-toggle").ClickAsync();
        Assert.Equal(layout[1], await page.Locator(".documentation-document").EvaluateAsync<double>("e => e.getBoundingClientRect().top"));
        Assert.True(await page.Locator(".documentation-nav-links").EvaluateAsync<bool>("e => e.getBoundingClientRect().bottom <= innerHeight"));
    }
}
