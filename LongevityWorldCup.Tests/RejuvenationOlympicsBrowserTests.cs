using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class RejuvenationOlympicsBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(768, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(320, ColorScheme.Light)]
    [InlineData(1440, ColorScheme.Dark)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task EssayTimelineAndEntryLinks_WorkWithoutJavaScriptAndFitTheViewport(int width, ColorScheme colorScheme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            JavaScriptEnabled = false,
            ColorScheme = colorScheme,
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new() { Width = width, Height = 900 }
        });
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync("/rejuvenation-olympics");
        Assert.True(response!.Ok);
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Rejuvenation Olympics is closing.");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("Longevity World Cup is your chance to keep competing.");
        await Assertions.Expect(page.Locator("main")).ToContainTextAsync("DunedinPACE alone won’t qualify.");
        await Assertions.Expect(page.Locator(".ro-lead time")).ToHaveAttributeAsync("datetime", "2027-04-15");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new()
            { Name = "Rejuvenation Olympics closing timeline", Level = 2, Exact = true }))
            .ToHaveTextAsync("Rejuvenation Olympics closing timeline");
        Assert.Contains("?v=", await page.Locator(".ro-phoenix").GetAttributeAsync("src"));
        Assert.True(await page.Locator(".ro-phoenix").EvaluateAsync<bool>(
            "image => image.complete && image.naturalWidth === 512 && image.naturalHeight === 512"));
        var milestones = page.Locator(".ro-milestones li");
        await Assertions.Expect(milestones).ToHaveCountAsync(3);
        var deadlines = new[] { "2026-10-30", "2027-03-30", "2027-04-15" };
        for (var i = 0; i < deadlines.Length; i++)
        {
            await Assertions.Expect(milestones.Nth(i).Locator("time"))
                .ToHaveAttributeAsync("datetime", deadlines[i]);
            await milestones.Nth(i).ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(milestones.Nth(i)).ToBeInViewportAsync();
        }
        await Assertions.Expect(milestones.Nth(1)).ToContainTextAsync("must reach the RO lab");
        await Assertions.Expect(page.Locator(".ro-timeline-note"))
            .ToContainTextAsync("three qualifying tests within two years");
        var joinLink = page.GetByRole(AriaRole.Link, new() { Name = "Join Longevity World Cup" });
        await joinLink.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(joinLink).ToBeInViewportAsync();
        await Assertions.Expect(joinLink).ToHaveAttributeAsync("href", "/join");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "View leaderboard" }))
            .ToHaveAttributeAsync("href", "/leaderboard");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "entry rules" }))
            .ToHaveAttributeAsync("href", "/ruleset");
        Assert.True(await page.EvaluateAsync<bool>(
            "Array.from(document.querySelectorAll('.ro-guide .ro-button'))" +
            ".every(el => getComputedStyle(el).transitionProperty === 'none')"),
            "Reduced motion must disable the button transition.");
        Assert.True(await page.EvaluateAsync<bool>(
            "document.querySelector('main').innerText.trim().split(/\\s+/).length < 250"),
            "The essay, timeline, and entry links should stay concise.");
        Assert.Equal("https://longevityworldcup.com/rejuvenation-olympics",
            await page.Locator("link[rel=canonical]").GetAttributeAsync("href"));
        Assert.Contains("index, follow", await page.Locator("meta[name=robots]").GetAttributeAsync("content"));
        Assert.Contains("?v=", await page.Locator("link[href*='ro-guide.css']").GetAttributeAsync("href"));
        Assert.Contains("Rejuvenation Olympics is closing.",
            await page.Locator("meta[name=description]").GetAttributeAsync("content"));
        Assert.True(await page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth <= window.innerWidth"));

        Assert.Equal("https://www.rapamycin.news/t/why-is-the-rejuvenation-olympics-closing/26779/1",
            await page.GetByRole(AriaRole.Link, new() { Name = "final leaderboard will freeze" }).GetAttributeAsync("href"));
        using var client = App.CreateClient();
        Assert.Contains("https://longevityworldcup.com/rejuvenation-olympics", await client.GetStringAsync("/sitemap.xml"));
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task HomepageDiscoveryAndJoinCta_ReachTheNewAthleteTrackChoice(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await page.GetByRole(AriaRole.Link, new() { Name = "Coming from Rejuvenation Olympics?" }).ClickAsync();
        await page.WaitForURLAsync("**/rejuvenation-olympics");
        await page.GetByRole(AriaRole.Link, new() { Name = "Join Longevity World Cup" }).ClickAsync();
        await page.WaitForURLAsync("**/join");
        await Assertions.Expect(page.Locator("#joinTrackPanel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#playStartPanel")).ToBeHiddenAsync();
    }
}
