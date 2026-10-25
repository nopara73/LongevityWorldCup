using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FooterCompositionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static readonly string[] Labels =
        ["Merch", "GitHub", "History", "Ruleset", "Contact", "Podcast", "Instagram", "YouTube", "X", "Nostr", "Reddit", "Threads", "Mastodon", "RSS", "Browser notifications"];

    private static readonly string[] Destinations =
    [
        "https://merch.longevityworldcup.com/", "https://github.com/nopara73/LongevityWorldCup/",
        "/history", "/ruleset", "mailto:hi@longevityworldcup.com",
        "https://www.youtube.com/playlist?list=PL4nqc85w185sO4i7eR3oUO_lMmlJ2K1cL",
        "https://www.instagram.com/LongevityWorldCup/", "https://www.youtube.com/@longevityworldcup",
        "https://x.com/LongevityWorldC", "https://primal.net/p/npub1dgxhzvhhca4hrpzmakc2n8hpvmawtvlacdy80r4wsaghf4h5486sg8tqtl",
        "https://www.reddit.com/r/LongevityWorldCup/", "https://www.threads.com/@longevityworldcup",
        "https://mastodon.social/@longevityworldcup", "/feeds/events.rss", "/events#eventBoardTitle"
    ];

    [Theory]
    [InlineData("/", 1440, false, false, 100)]
    [InlineData("/history", 1440, true, false, 200)]
    [InlineData("/ruleset", 320, false, true, 100)]
    [InlineData("/ruleset", 320, true, true, 200)]
    [InlineData("/ruleset", 376, false, false, 100)]
    [InlineData("/ruleset", 377, true, false, 100)]
    [InlineData("/ruleset", 378, false, false, 100)]
    [InlineData("/ruleset", 390, false, true, 100)]
    [InlineData("/ruleset", 390, true, true, 200)]
    [InlineData("/events", 600, true, false, 200)]
    [InlineData("/leaderboard", 768, false, true, 200)]
    [InlineData("/longevitymaxxing", 844, true, true, 100)]
    public async Task Footer_PreservesIconDesignTargetsAndOrderWithActualTextEnlargement(
        string path, int width, bool dark, bool touch, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce, IsMobile = touch, HasTouch = touch
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        var labels = page.Locator(".footer-link-label");
        var originalSizes = await labels.EvaluateAllAsync<double[]>("labels => labels.map(l => parseFloat(getComputedStyle(l).fontSize))");
        if (textPercent != 100)
        {
            await labels.EvaluateAllAsync<object>(
                "(labels, sizes) => labels.forEach((label, i) => label.style.fontSize = sizes[i] * 2 + 'px')", originalSizes);
        }
        await page.Locator(".footer").ScrollIntoViewIfNeededAsync();
        await SettleAsync(page);
        var sizes = await labels.EvaluateAllAsync<double[]>("labels => labels.map(l => parseFloat(getComputedStyle(l).fontSize))");
        for (var i = 0; i < sizes.Length; i++) Assert.InRange(sizes[i], originalSizes[i] * textPercent / 100 - 0.1, originalSizes[i] * textPercent / 100 + 0.1);
        var links = page.Locator(".footer .footer-link");
        Assert.Equal(Labels, await links.EvaluateAllAsync<string[]>("links => links.map(l => l.textContent.trim() || l.title)"));
        Assert.Equal(Destinations, await links.EvaluateAllAsync<string[]>("links => links.map(l => l.getAttribute('href'))"));
        var problems = await page.EvaluateAsync<string[]>(
            $$"""
            () => {
                const problems = [];
                const ink = {{BrowserIconAssertions.Ink}};
                const links = [...document.querySelectorAll('.footer .footer-link')];
                const contains = (outer, inner) => inner.left >= outer.left - 1 && inner.right <= outer.right + 1 && inner.top >= outer.top - 1 && inner.bottom <= outer.bottom + 1;
                const boxes = links.map(link => link.getBoundingClientRect());
                links.forEach((link, i) => {
                    const box = boxes[i], name = link.getAttribute('aria-label') || link.textContent.trim();
                    if (!link.getAttribute('aria-label')) problems.push(name + ': missing accessible name');
                    if (box.width < 44 || box.height < 44) problems.push(name + ': small target');
                    if (box.left < 0 || box.right > innerWidth + 1) problems.push(name + ': clipped target');
                    const icon = link.querySelector('i, svg');
                    const painted = ink(icon);
                    if (!painted || !contains(box, painted)) problems.push(name + ': missing or clipped icon');
                    const label = link.querySelector('.footer-link-label');
                    if (label && label.getBoundingClientRect().width > 1) {
                        const range = document.createRange(); range.selectNodeContents(label);
                        for (const rect of range.getClientRects()) if (!contains(box, rect)) problems.push(name + ': clipped text');
                    } else if (link.closest('.footer-column--explore')) problems.push(name + ': hidden name');
                    if (link.closest('.footer-column--follow') && label && getComputedStyle(label).clipPath !== 'inset(50%)') problems.push(name + ': unexpected visible caption');
                    if (link.target === '_blank' && !link.relList.contains('noopener')) problems.push(name + ': unsafe external target');
                    for (let j = i + 1; j < boxes.length; j++) {
                        const other = boxes[j];
                        if (box.left < other.right - 1 && other.left < box.right - 1 && box.top < other.bottom - 1 && other.top < box.bottom - 1) problems.push(name + ': overlapping target');
                    }
                    if (i && (box.top < boxes[i - 1].top - 1 || (Math.abs(box.top - boxes[i - 1].top) < 1 && box.left < boxes[i - 1].left))) problems.push(name + ': visual order differs from DOM order');
                });
                const pages = links.slice(0, 6), platforms = links.slice(6);
                if (Math.min(...platforms.map(l => l.getBoundingClientRect().top)) < Math.max(...pages.map(l => l.getBoundingClientRect().bottom))) problems.push('groups overlap');
                const featured = links.slice(6, 9).map(l => l.getBoundingClientRect());
                const remaining = links.slice(9).map(l => l.getBoundingClientRect());
                if (featured.some(b => Math.abs(b.top - featured[0].top) > 1)) problems.push('featured platforms split across rows');
                if (Math.min(...remaining.map(b => b.top)) < Math.max(...featured.map(b => b.bottom))) problems.push('other icons share the featured row');
                if (Math.min(...featured.map(b => b.width)) <= Math.max(...remaining.map(b => b.width))) problems.push('featured platforms are not larger');
                if (!document.fonts.check('400 16px Roboto') || !document.fonts.check('400 16px "Font Awesome 6 Brands"')) problems.push('fonts missing');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{path}, {width}px, {textPercent}% actual text: {string.Join("; ", problems)}");
        var current = page.Locator(".footer [aria-current=page]");
        if (path is "/ruleset" or "/history")
        {
            Assert.Equal(1, await current.CountAsync());
            Assert.Equal(path, await current.GetAttributeAsync("href"));
            var indicator = page.Locator(".footer .nav-glide.is-shown.is-current");
            await Assertions.Expect(indicator).ToBeVisibleAsync();
            Assert.NotEqual("none", await indicator.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow"));
            Assert.True(await current.EvaluateAsync<bool>("""
                link => {
                    const indicator = link.parentElement.querySelector('.nav-glide');
                    const target = link.getBoundingClientRect(), actual = indicator.getBoundingClientRect();
                    return ['left', 'top', 'width', 'height'].every(key => Math.abs(actual[key] - target[key]) <= 1);
                }
                """), "The current-page indicator must cover the current link.");
        }
        else Assert.Equal(0, await current.CountAsync());
        BrowserContrast.AssertMinimum("footer", await BrowserContrast.MeasureVisibleTextAsync(page, ".footer-link-label"));
    }

    [Theory]
    [InlineData(376)]
    [InlineData(377)]
    [InlineData(378)]
    public async Task Footer_IntermediatePhoneWidthsDoNotOrphanAPlatform(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 844 } });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        var rowSizes = await page.Locator(".footer-follow-links .footer-link").EvaluateAllAsync<int[]>(
            "links => Object.values(Object.groupBy(links, l => Math.round(l.getBoundingClientRect().top))).map(row => row.length)");
        Assert.DoesNotContain(1, rowSizes);
    }

    [Theory]
    [InlineData(390, false, 100)]
    [InlineData(390, true, 100)]
    [InlineData(320, false, 200)]
    [InlineData(390, true, 200)]
    public async Task Footer_KeyboardOrderFocusAndPlatformNamesRemainVisible(int width, bool dark, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = parseFloat(getComputedStyle(document.documentElement).fontSize) * percent / 100 + 'px'", textPercent);
        await SettleAsync(page);
        var links = page.Locator(".footer .footer-link");
        await links.First.FocusAsync();
        for (var i = 0; i < Labels.Length; i++)
        {
            var link = links.Nth(i);
            await Assertions.Expect(link).ToBeFocusedAsync();
            Assert.True(await link.EvaluateAsync<bool>(
                "l => l.matches(':focus-visible') && parseFloat(getComputedStyle(l).outlineWidth) >= 2"));
            if (i >= 6 && i < 13)
            {
                var label = link.Locator(".footer-link-label");
                await Assertions.Expect(label).ToBeVisibleAsync();
                Assert.True(await label.EvaluateAsync<bool>("l => { const b=l.getBoundingClientRect(); return b.width > 1 && b.left >= 0 && b.right <= innerWidth && getComputedStyle(l).clipPath === 'none'; }"),
                    $"{Labels[i]} label was hidden or clipped at {width}px / {textPercent}% text.");
                await page.Keyboard.PressAsync("Escape");
                Assert.Equal("inset(50%)", await label.EvaluateAsync<string>("l => getComputedStyle(l).clipPath"));
                await Assertions.Expect(link).ToBeFocusedAsync();
            }
            else if (i >= 13) Assert.Equal(Labels[i], await link.GetAttributeAsync("title"));
            await page.Keyboard.PressAsync("Tab");
        }
        await page.EvaluateAsync("() => document.activeElement?.blur()");
        var hoveredLink = links.Nth(7);
        var hoveredLabel = hoveredLink.Locator(".footer-link-label");
        await hoveredLink.HoverAsync();
        Assert.True(await hoveredLink.EvaluateAsync<bool>("l => l.matches(':hover') && document.activeElement === document.body"));
        Assert.True(await hoveredLabel.EvaluateAsync<bool>("l => l.getBoundingClientRect().width > 1 && getComputedStyle(l).clipPath === 'none'"));
        await page.Keyboard.PressAsync("Escape");
        Assert.Equal("inset(50%)", await hoveredLabel.EvaluateAsync<string>("l => getComputedStyle(l).clipPath"));
        Assert.True(await hoveredLink.EvaluateAsync<bool>("l => l.matches(':hover') && document.activeElement === document.body"));
        await page.Mouse.MoveAsync(0, 0);
        await hoveredLink.HoverAsync();
        Assert.Equal("none", await hoveredLabel.EvaluateAsync<string>("l => getComputedStyle(l).clipPath"));
    }

    [Fact]
    public async Task Footer_PageFeedContactAndExternalLinksKeepTheirDestinations()
    {
        await using var context = await NewContextAsync(Browser, App, new() { HasTouch = true, IsMobile = true, ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await page.Locator(".footer a[href='/history']").TapAsync();
        await Assertions.Expect(page).ToHaveURLAsync(App.BaseAddress + "history");
        await page.Locator(".footer a[href='/ruleset']").TapAsync();
        await Assertions.Expect(page).ToHaveURLAsync(App.BaseAddress + "ruleset");
        await page.Locator(".footer a[href^='mailto:']").EvaluateAsync("l => l.addEventListener('click', e => { e.preventDefault(); window.footerContact = l.href; }, {once:true})");
        await page.Locator(".footer a[href^='mailto:']").TapAsync();
        Assert.Equal(Destinations[4], await page.EvaluateAsync<string>("window.footerContact"));
        await context.RouteAsync(Destinations[6], r => r.FulfillAsync(new() { Status = 200, ContentType = "text/html", Body = "<title>External navigation fixture</title>" }));
        var popup = await page.RunAndWaitForPopupAsync(() => page.Locator(".footer-follow-links .footer-link").First.TapAsync());
        await popup.WaitForLoadStateAsync();
        Assert.Equal(Destinations[6], popup.Url);
        Assert.True(await popup.EvaluateAsync<bool>("window.opener === null"));
        await popup.CloseAsync();
        using var client = App.CreateClient();
        using var response = await client.GetAsync("/feeds/events.rss");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/rss+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("rss", System.Xml.Linq.XDocument.Parse(await response.Content.ReadAsStringAsync()).Root?.Name.LocalName);
        var nostr = page.GetByRole(AriaRole.Link, new() { Name = "Follow Longevity World Cup on Nostr", Exact = true });
        await Assertions.Expect(nostr.Locator("svg")).ToBeVisibleAsync();
        Assert.Contains("primal.net", await nostr.GetAttributeAsync("href"));
    }

    [Fact]
    public async Task Footer_NotificationShortcutDoesNotOptInAndCancelledPermissionRestoresKeyboardFocus()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        await context.RouteAsync("**/api/web-push/configuration", r => r.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = """{"publicKey":"test"}""" }));
        await context.AddInitScriptAsync("""
            window.footerPermissionRequests = 0;
            Object.defineProperty(Notification, 'permission', { get: () => 'default' });
            Notification.requestPermission = () => {
                window.footerPermissionRequests++;
                return new Promise(resolve => window.dismissFooterPermission = () => resolve('default'));
            };
            """);
        var writes = 0;
        await context.RouteAsync("**/api/web-push/subscribe", async r => { writes++; await r.AbortAsync(); });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await page.GetByRole(AriaRole.Link, new() { Name = "Get browser notifications on Highlights", Exact = true }).ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.Load);
        await SettleAsync(page);
        await Assertions.Expect(page.Locator("#eventBoardTitle")).ToBeInViewportAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.footerPermissionRequests"));
        var bell = page.Locator("#webPushToggle");
        await Assertions.Expect(bell).ToBeVisibleAsync();
        await bell.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(bell).ToBeDisabledAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("window.footerPermissionRequests"));
        await page.EvaluateAsync("window.dismissFooterPermission()");
        await Assertions.Expect(bell).ToBeEnabledAsync();
        await Assertions.Expect(bell).ToBeFocusedAsync();
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(page.Locator("#webPushStatus")).ToHaveTextAsync("Announcement notifications were not enabled.");
        Assert.Equal(0, writes);
    }

    private static async Task SettleAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }
}
