using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageScoreboardBrowserTests
{
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    [InlineData(375, 667)]
    public async Task HomepageScoreboard_FitsFirstViewportWithoutRuntimeErrors(int viewportWidth, int viewportHeight)
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LWC_SCREENSHOT_DIR")))
        {
            await context.RouteAsync(
                "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/**",
                route => route.ContinueAsync());
        }

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var scoreboard = page.Locator(".homepage-scoreboard");
        await scoreboard.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForLoadStateAsync(LoadState.Load);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var hasHorizontalOverflow = await page.EvaluateAsync<bool>(
            """
            () => document.documentElement.scrollWidth > window.innerWidth + 1
                || document.body.scrollWidth > window.innerWidth + 1
            """);
        Assert.False(hasHorizontalOverflow, $"Homepage overflowed horizontally at {viewportWidth}x{viewportHeight}.");

        var scoreboardBox = await scoreboard.BoundingBoxAsync();
        Assert.NotNull(scoreboardBox);
        Assert.True(scoreboardBox.Y >= -1, $"Scoreboard starts above the viewport at {viewportWidth}x{viewportHeight}: {scoreboardBox.Y}px.");
        Assert.True(
            scoreboardBox.Y + scoreboardBox.Height <= viewportHeight + 1,
            $"Scoreboard ends below the first viewport at {viewportWidth}x{viewportHeight}: {scoreboardBox.Y + scoreboardBox.Height}px > {viewportHeight}px.");

        foreach (var (selector, description) in new[]
        {
            (".homepage-scoreboard-action", "Explore live standings action"),
            (".homepage-scoreboard-rules", "Ranking rules action"),
            (".homepage-scoreboard-leader", "current leader link")
        })
        {
            var link = page.Locator(selector);
            Assert.Equal(1, await link.CountAsync());
            Assert.True(await link.IsVisibleAsync(), $"The {description} is not visible at {viewportWidth}x{viewportHeight}.");
            await AssertMinimumActionHeightAsync(link, 44, description, viewportWidth, viewportHeight);
        }

        var playButton = page.GetByRole(AriaRole.Button, new() { Name = "Play the game", Exact = true }).First;
        await AssertMinimumActionHeightAsync(playButton, 44, "header Play the game button", viewportWidth, viewportHeight);

        await CaptureOptionalScreenshotAsync(page, viewportWidth, viewportHeight);
        Assert.Empty(errors);
    }

    private static List<string> CapturePageErrors(IPage page)
    {
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        return errors;
    }

    private static async Task AssertMinimumActionHeightAsync(
        ILocator locator,
        double minimumHeight,
        string description,
        int viewportWidth,
        int viewportHeight)
    {
        var box = await locator.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(
            box.Height >= minimumHeight - 0.5,
            $"The {description} is only {box.Height:0.##}px tall at {viewportWidth}x{viewportHeight}; expected at least {minimumHeight}px.");
    }

    private static async Task CaptureOptionalScreenshotAsync(IPage page, int viewportWidth, int viewportHeight)
    {
        var screenshotDirectory = Environment.GetEnvironmentVariable("LWC_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(screenshotDirectory))
            return;

        var fullDirectory = Path.GetFullPath(screenshotDirectory);
        Directory.CreateDirectory(fullDirectory);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(fullDirectory, $"homepage-scoreboard-{viewportWidth}x{viewportHeight}.png"),
            FullPage = false
        });

        var footer = page.Locator(".footer");
        await footer.ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await footer.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(fullDirectory, $"grouped-footer-{viewportWidth}x{viewportHeight}.png")
        });
    }
}
