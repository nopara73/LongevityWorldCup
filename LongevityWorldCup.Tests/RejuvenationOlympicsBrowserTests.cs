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
    [InlineData(1440)]
    [InlineData(768)]
    [InlineData(390)]
    [InlineData(320)]
    public async Task GuideAndMarkerDisclosures_WorkWithoutJavaScriptAndFitTheViewport(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            JavaScriptEnabled = false,
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new() { Width = width, Height = 900 }
        });
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync("/rejuvenation-olympics");
        Assert.True(response!.Ok);
        await Assertions.Expect(page.Locator("h1")).ToContainTextAsync("competing.");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Join the World Cup" })).ToBeInViewportAsync();
        Assert.True(await page.EvaluateAsync<bool>(
            "Array.from(document.querySelectorAll('.ro-guide .ro-button, .ro-track-action span, .ro-guide summary > span'))" +
            ".every(el => getComputedStyle(el).transitionProperty === 'none')"),
            "Reduced motion must disable the button, track-arrow and disclosure transitions.");
        Assert.True(await page.EvaluateAsync<bool>(
            "document.querySelector('main').innerText.trim().split(/\\s+/).length < 300"),
            "The landing page should stay concise before the reference details are opened.");
        Assert.Equal("https://longevityworldcup.com/rejuvenation-olympics",
            await page.Locator("link[rel=canonical]").GetAttributeAsync("href"));
        Assert.Contains("index, follow", await page.Locator("meta[name=robots]").GetAttributeAsync("content"));
        Assert.Contains("?v=", await page.Locator("link[href*='ro-guide.css']").GetAttributeAsync("href"));
        Assert.Contains("?v=", await page.Locator(".ro-phoenix").GetAttributeAsync("src"));
        Assert.True(await page.Locator(".ro-phoenix").EvaluateAsync<bool>(
            "image => image.complete && image.naturalWidth > 0"));
        await page.GetByRole(AriaRole.Link, new() { Name = "Can I use my bloodwork?" }).ClickAsync();
        Assert.Equal("#bloodwork-check", new Uri(page.Url).Fragment);
        var email = await page.GetByRole(AriaRole.Link, new() { Name = "Check my bloodwork" }).GetAttributeAsync("href");
        Assert.StartsWith("mailto:hi@longevityworldcup.com?", email);
        Assert.Contains("RO bloodwork check", Uri.UnescapeDataString(email!));

        await Assertions.Expect(page.Locator(".ro-markers table").First).ToBeHiddenAsync();
        await page.Locator("#markers > summary").ClickAsync();
        foreach (var disclosure in await page.Locator(".ro-markers").AllAsync())
        {
            await disclosure.Locator("summary").ClickAsync();
            Assert.True(await disclosure.Locator("table").IsVisibleAsync());
        }
        await Assertions.Expect(page.Locator(".ro-markers tbody tr")).ToHaveCountAsync(22);
        Assert.True(await page.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth <= window.innerWidth"));
        foreach (var anchor in await page.Locator("main a[href^='#']").AllAsync())
            Assert.Equal(1, await page.Locator((await anchor.GetAttributeAsync("href"))!).CountAsync());

        Assert.Equal("https://www.rapamycin.news/t/why-is-the-rejuvenation-olympics-closing/26779/1",
            await page.GetByRole(AriaRole.Link, new() { Name = "Announcement" }).GetAttributeAsync("href"));
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
        await page.GetByRole(AriaRole.Link, new() { Name = "Join the World Cup" }).ClickAsync();
        await page.WaitForURLAsync("**/join");
        await Assertions.Expect(page.Locator("#joinTrackPanel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#playStartPanel")).ToBeHiddenAsync();
    }
}
