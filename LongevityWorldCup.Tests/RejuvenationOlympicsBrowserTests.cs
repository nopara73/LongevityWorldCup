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
        await Assertions.Expect(page.Locator("h1")).ToHaveAccessibleNameAsync("Coming from the Rejuvenation Olympics?");
        Assert.Equal("Coming from the Rejuvenation Olympics?", (await page.Locator("h1").TextContentAsync())!.Replace("\u00ad", ""));
        await Assertions.Expect(page.Locator(".ro-lead")).ToHaveTextAsync(
            "Rejuvenation Olympics is closing. Its final leaderboard will freeze on 15 April 2027. " +
            "If you competed there, Longevity World Cup is your chance to keep competing.");
        Assert.Equal("Coming from the Rejuvenation Olympics? | Longevity World Cup", await page.TitleAsync());
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
    [InlineData(1440, 900, 100)]
    [InlineData(820, 1180, 100)]
    [InlineData(768, 900, 100)]
    [InlineData(701, 900, 100)]
    [InlineData(390, 844, 100)]
    [InlineData(320, 640, 100)]
    [InlineData(1440, 900, 200)]
    [InlineData(820, 1180, 200)]
    [InlineData(768, 900, 200)]
    [InlineData(701, 900, 200)]
    public async Task PosterTagline_StaysClearOfTheRacingLineAndIntroCard(int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/rejuvenation-olympics");
        await page.EvaluateAsync("document.fonts.ready");
        if (textPercent != 100)
        {
            await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
            await page.WaitForFunctionAsync(
                "() => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 32", null, new() { Timeout = 5000 });
        }
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var overlaps = await page.EvaluateAsync<string[]>(
            """
            () => {
                const box = selector => document.querySelector(selector)?.getBoundingClientRect();
                const hits = (a, b) => !!b && a.left < b.right && b.left < a.right && a.top < b.bottom && b.top < a.bottom;
                const tagline = box('.ro-sport-mark');
                const artwork = box('.ro-artwork');
                return [
                    artwork.left < 0 || artwork.right > innerWidth + 1 ? 'poster overflows viewport' : '',
                    hits(tagline, box('.ro-racing-line')) ? 'racing line' : '',
                    hits(tagline, box('.ro-intro')) ? 'intro card' : '',
                    hits(tagline, box('.ro-artwork-mark')) ? 'RO x LWC mark' : '',
                    tagline.left < artwork.left || tagline.right > artwork.right || tagline.bottom > artwork.bottom ? 'poster edge' : ''
                ].filter(Boolean);
            }
            """);
        Assert.Empty(overlaps);
    }

    [Theory]
    [InlineData(390)]
    [InlineData(320)]
    public async Task PhoneTextEnlargement_ScalesTheHeadlineAndKeepsEntryActionsWithinTheColumn(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/rejuvenation-olympics");
        await page.EvaluateAsync("() => document.fonts.ready");
        var originalSize = await page.Locator(".ro-guide h1").EvaluateAsync<double>(
            "heading => parseFloat(getComputedStyle(heading).fontSize)");
        await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        await page.WaitForFunctionAsync(
            "size => parseFloat(getComputedStyle(document.querySelector('.ro-guide h1')).fontSize) >= size * 1.99",
            originalSize, new() { Timeout = 5000 });
        var overflowing = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('.ro-intro, .ro-intro h1, .ro-actions, .ro-actions a')]
                .filter(element => {
                    const bounds = element.getBoundingClientRect();
                    return bounds.left < 0 || bounds.right > innerWidth + 1 || element.scrollWidth > element.clientWidth + 1;
                })
                .map(element => element.className || element.tagName)
            """);
        Assert.Empty(overflowing);
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
