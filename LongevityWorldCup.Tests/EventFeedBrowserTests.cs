using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class EventFeedBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitialHeadingScroll_DoesNotInterruptHoverOrFocus(bool focus)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        await context.AddInitScriptAsync("""
            window.headingScrolls = 0;
            const originalScrollIntoView = Element.prototype.scrollIntoView;
            Element.prototype.scrollIntoView = function (...args) {
                if (this.matches('.event-board-heading')) headingScrolls++;
                return originalScrollIntoView.apply(this, args);
            };
            """);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(844, 390);
        var start = new DateTime(2026, 10, 24, 4, 0, 0, DateTimeKind.Utc);
        await page.Clock.InstallAsync(new() { Time = start.ToString("O") });
        await page.Clock.PauseAtAsync(start.AddSeconds(1));
        await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("() => document.fonts.ready");
        var link = page.Locator(".event-feed-link");
        if (focus) await link.FocusAsync();
        else
        {
            var box = (await link.BoundingBoxAsync())!;
            await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        }
        await page.Clock.RunForAsync(600);
        Assert.Equal(0, await page.EvaluateAsync<int>("headingScrolls"));
    }

    [Theory]
    [InlineData(360)]
    [InlineData(1280)]
    public async Task InitialHeadingScroll_RespectsReducedMotion(int width)
    {
        // This is the no-input opening state. Keep its cancellable timer apart
        // from concurrent browser interactions and advance it after layout is ready.
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(browser, App, ReducedMotion.Reduce);
        await context.AddInitScriptAsync("""
            (() => {
                const scrollIntoView = Element.prototype.scrollIntoView;
                Element.prototype.scrollIntoView = function (options) {
                    const result = scrollIntoView.call(this, options);
                    if (this.matches('.event-board-heading')) {
                        window.__initialHeadingScroll = {
                            behavior: options?.behavior,
                            offset: Math.abs(this.getBoundingClientRect().top
                                - parseFloat(getComputedStyle(document.documentElement).scrollPaddingTop))
                        };
                    }
                    return result;
                };
            })();
            """);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 850);
        // Establish an idle pointer before the page installs its interaction
        // cancellation listeners, then control the opening delay explicitly.
        // Hover/focus cancellation is covered by the separate test above.
        await page.Mouse.MoveAsync(-1, -1);
        var start = new DateTime(2026, 10, 24, 4, 0, 0, DateTimeKind.Utc);
        await page.Clock.InstallAsync(new() { Time = start.ToString("O") });
        await page.Clock.PauseAtAsync(start.AddSeconds(1));
        await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.Clock.RunForAsync(600);

        // Observe the real callback rather than sampling exactly at its timer boundary.
        // Capture alignment synchronously so an unintended smooth scroll cannot settle first.
        Assert.True(await page.EvaluateAsync<bool>("window.__initialHeadingScroll !== undefined"),
            "The opening callback must run after its 500ms delay when there is no user input.");
        Assert.Equal("instant", await page.EvaluateAsync<string>("window.__initialHeadingScroll.behavior"));
        Assert.InRange(await page.EvaluateAsync<double>("window.__initialHeadingScroll.offset"), 0, 1);

        var offset = await page.Locator(".event-board-heading").EvaluateAsync<double>(
            "heading => Math.abs(heading.getBoundingClientRect().top - parseFloat(getComputedStyle(document.documentElement).scrollPaddingTop))");
        Assert.InRange(offset, 0, 1);
    }

    [Theory]
    [InlineData(320, 850, 100)]
    [InlineData(320, 850, 200)]
    [InlineData(390, 850, 200)]
    [InlineData(844, 390, 200)]
    [InlineData(1280, 900, 100)]
    [InlineData(1280, 900, 200)]
    public async Task HighlightSubscriptions_RemainUsableWhenNotificationsAreAvailable(int width, int height, int textPercent)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        await context.RouteAsync("**/api/web-push/configuration", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = "{\"publicKey\":\"test-public-key\"}"
        }));
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#webPushToggle").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        await page.Locator(".event-board-heading").EvaluateAsync("heading => heading.scrollIntoView({ behavior: 'instant', block: 'start' })");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var titleBox = Assert.IsType<LocatorBoundingBoxResult>(await page.Locator("#eventBoardTitle").BoundingBoxAsync());
        var textLeft = await page.Locator("#eventBoardTitle").EvaluateAsync<double>(
            "title => { const range = document.createRange(); range.selectNodeContents(title); return range.getClientRects()[0].left; }");
        Assert.True(Math.Abs(textLeft - titleBox.X) <= 1, "Highlights text must align with the board's leading edge.");
        Assert.True(titleBox.Y >= 0, "Highlights must remain inside the viewport.");
        if (await page.Locator("#site-sticky-header").BoundingBoxAsync() is { } headerBox)
        {
            Assert.True(titleBox.Y >= headerBox.Y + headerBox.Height - 1,
                $"Highlights starts at {titleBox.Y}px beneath a header ending at {headerBox.Y + headerBox.Height}px.");
        }

        foreach (var selector in new[] { ".event-feed-link", "#webPushToggle" })
        {
            var control = page.Locator(selector);
            var box = Assert.IsType<LocatorBoundingBoxResult>(await control.BoundingBoxAsync());
            Assert.True(box.Width >= 44 && box.Height >= 44, $"{selector} is smaller than a 44px target.");
            Assert.True(box.X >= 0 && box.X + box.Width <= width, $"{selector} is clipped.");
            if (await control.IsEnabledAsync())
            {
                await control.FocusAsync();
                Assert.True(await control.EvaluateAsync<bool>("element => document.activeElement === element"));
            }
        }
        Assert.True(await page.Locator("#eventBoardTitle").EvaluateAsync<bool>(
            """
            title => {
                const range = document.createRange();
                range.selectNodeContents(title);
                return [...range.getClientRects()].every(rect => rect.left >= 0 && rect.right <= innerWidth);
            }
            """));
        if (textPercent == 100)
        {
            Assert.True(await page.Locator("#eventBoardTitle").EvaluateAsync<bool>(
                "title => title.getBoundingClientRect().height <= parseFloat(getComputedStyle(title).lineHeight) + 1"),
                "Highlights should remain a whole word at normal text size.");
        }
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(360)]
    public async Task Highlights_ExposeFeedsWithAccessibleSubscribeLinkAndNoOverflow(int width)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = width, Height = 850 },
            Locale = "en-US",
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/events", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var rss = page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Subscribe to Highlights via RSS" });
        await rss.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.Locator("#events-root[aria-busy='false']").WaitForAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        Assert.Equal("/feeds/events.rss", await rss.GetAttributeAsync("href"));
        Assert.Equal(1, await rss.Locator(".fa-rss").CountAsync());
        await page.Locator(".event-board-heading").EvaluateAsync("heading => heading.scrollIntoView({ behavior: 'instant', block: 'start' })");
        await rss.FocusAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        Assert.True(await rss.EvaluateAsync<bool>("link => link === document.activeElement"));
        var box = Assert.IsType<LocatorBoundingBoxResult>(await rss.BoundingBoxAsync());
        Assert.True(box.Width >= 44 && box.Height >= 44);
        Assert.True(box.X >= 0 && box.X + box.Width <= width);
        var stickyHeader = page.Locator("#site-sticky-header");
        if (await stickyHeader.GetAttributeAsync("aria-hidden") == "false")
        {
            var headerBox = Assert.IsType<LocatorBoundingBoxResult>(await stickyHeader.BoundingBoxAsync());
            Assert.True(box.Y >= headerBox.Y + headerBox.Height - 1, "The sticky header must not cover the subscription link.");
        }
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        Assert.Equal("https://longevityworldcup.com/feeds/events.rss", await page.Locator("head link[rel=alternate][type='application/rss+xml']").GetAttributeAsync("href"));
        Assert.Equal("https://longevityworldcup.com/feeds/events.atom", await page.Locator("head link[rel=alternate][type='application/atom+xml']").GetAttributeAsync("href"));
        var feed = await context.APIRequest.GetAsync("/feeds/events.rss");
        Assert.True(feed.Ok);
        Assert.StartsWith("application/rss+xml", feed.Headers["content-type"]);
        await feed.DisposeAsync();

        var screenshotDirectory = Environment.GetEnvironmentVariable("LWC_FEED_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            Directory.CreateDirectory(screenshotDirectory);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(screenshotDirectory, $"events-{width}.png") });
        }
    }
}
