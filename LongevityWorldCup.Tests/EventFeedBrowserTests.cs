using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class EventFeedBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
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
        var page = await context.NewPageAsync();
        await page.GotoAsync("/events", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var rss = page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Subscribe to Highlights via RSS" });
        await rss.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.Locator("#events-root[aria-busy='false']").WaitForAsync();
        Assert.Equal("/feeds/events.rss", await rss.GetAttributeAsync("href"));
        Assert.Equal(1, await rss.Locator(".fa-rss").CountAsync());
        await rss.FocusAsync();
        Assert.True(await rss.EvaluateAsync<bool>("link => link === document.activeElement"));
        var box = Assert.IsType<LocatorBoundingBoxResult>(await rss.BoundingBoxAsync());
        Assert.True(box.Width >= 44 && box.Height >= 44);
        Assert.True(box.X >= 0 && box.X + box.Width <= width);
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
