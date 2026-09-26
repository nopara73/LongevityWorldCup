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
    [InlineData(390)]
    [InlineData(320)]
    public async Task GuideAndMarkerDisclosures_WorkWithoutJavaScriptAndFitTheViewport(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            JavaScriptEnabled = false,
            ViewportSize = new() { Width = width, Height = 900 }
        });
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync("/rejuvenation-olympics");
        Assert.True(response!.Ok);
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Coming from Rejuvenation Olympics?");
        Assert.Equal("https://longevityworldcup.com/rejuvenation-olympics",
            await page.Locator("link[rel=canonical]").GetAttributeAsync("href"));
        Assert.Contains("index, follow", await page.Locator("meta[name=robots]").GetAttributeAsync("content"));
        Assert.Contains("?v=", await page.Locator("link[href*='ro-guide.css']").GetAttributeAsync("href"));
        await page.GetByRole(AriaRole.Link, new() { Name = "Check my bloodwork" }).ClickAsync();
        Assert.Equal("#bloodwork-check", new Uri(page.Url).Fragment);
        var email = await page.GetByRole(AriaRole.Link, new() { Name = "Email my bloodwork" }).GetAttributeAsync("href");
        Assert.StartsWith("mailto:hi@longevityworldcup.com?", email);
        Assert.Contains("RO bloodwork check", Uri.UnescapeDataString(email!));

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
            await page.GetByRole(AriaRole.Link, new() { Name = "RO’s announcement, shared on Rapamycin News" }).GetAttributeAsync("href"));
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
        await page.GetByRole(AriaRole.Link, new() { Name = "Choose my track" }).First.ClickAsync();
        await page.WaitForURLAsync("**/join");
        await Assertions.Expect(page.Locator("#joinTrackPanel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#playStartPanel")).ToBeHiddenAsync();
    }
}
