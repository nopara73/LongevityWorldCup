using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class EventFeedBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
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
        await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.Load });
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
        Assert.Equal("/feeds/events.rss", await rss.GetAttributeAsync("href"));
        Assert.Equal(1, await rss.Locator(".fa-rss").CountAsync());
        await page.Locator(".event-board-heading").EvaluateAsync("heading => heading.scrollIntoView({ behavior: 'instant', block: 'start' })");
        await rss.FocusAsync();
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
