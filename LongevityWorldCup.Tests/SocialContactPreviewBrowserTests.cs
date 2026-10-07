using LongevityWorldCup.Website.Tools;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class SocialContactPreviewBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task SocialPreviews_MatchTheServerMentionDestinations()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString()
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/custom-event-designer.html");
        var contacts = new[]
        {
            "https://netflix.com/alice", "https://notthreads.com/alice",
            "https://x.com.evil.example/alice", "https://threads.com.evil.example/alice",
            "HTTPS://X.COM/alice", "HTTPS://THREADS.COM/@alice",
            "https://mobile.twitter.com/alice", "www.threads.com/alice", "@alice"
        };
        foreach (var contact in contacts)
        foreach (var platform in new[] { SocialPlatform.X, SocialPlatform.Threads, SocialPlatform.Mastodon, SocialPlatform.Nostr })
        {
            var actual = await page.EvaluateAsync<string>(
                "args => extractMentionHandle(args.contact, args.platform)",
                new { contact, platform = platform.ToString().ToLowerInvariant() });
            Assert.Equal(SocialContactParser.TryBuildMention(contact, platform) ?? "", actual);
        }
    }

    [Fact]
    public async Task MastodonPreview_UsesEmojiAndLinkCapacityAndRetainsItsDestinationOnMobile()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/custom-event-designer.html");
        await page.Locator("#sendMastodon").CheckAsync();
        await page.Locator("#titleInput").FillAsync(new string('x', 500));
        await page.Locator("#contentInput").FillAsync(new string('x', 550));
        var caption = await page.EvaluateAsync<string>("() => buildPlan(titleInput.value, contentInput.value, LIMITS.mastodon, 'mastodon', false).postText");
        Assert.Equal(500, MastodonPost.Count(caption));
        Assert.Equal("text", await page.EvaluateAsync<string>("() => buildPlan('👩‍🔬'.repeat(500), '', LIMITS.mastodon, 'mastodon', false).mode"));
        Assert.Equal(26, await page.EvaluateAsync<int>("() => postLength('🏆 https://longevityworldcup.com/' + 'x'.repeat(500) + '.', 'mastodon')"));
        Assert.True(await page.EvaluateAsync<bool>("() => buildEventPayload().sendToMastodon"));
        await page.ReloadAsync();
        Assert.True(await page.Locator("#sendMastodon").IsCheckedAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }

    [Fact]
    public async Task NostrPreview_PreservesLongNotesAndItsSelectedDestinationOnMobile()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/custom-event-designer.html");
        foreach (var id in new[] { "sendWebpage", "sendSlack", "sendX", "sendThreads", "sendFacebook", "sendMastodon" })
            await page.Locator("#" + id).UncheckAsync();
        await page.Locator("#sendNostr").CheckAsync();
        await page.Locator("#titleInput").FillAsync("A sport for time 🏆");
        await page.Locator("#contentInput").FillAsync("[mention](benjamin_garden) " + new string('x', 600));
        var post = await page.EvaluateAsync<string>("() => buildPlan(titleInput.value, contentInput.value, LIMITS.nostr, 'nostr', false).postText");
        Assert.Contains(new string('x', 600), post);
        Assert.DoesNotContain("[mention]", post);
        Assert.DoesNotContain("@", post);
        Assert.Equal("text", await page.EvaluateAsync<string>("() => buildPlan(titleInput.value, contentInput.value, LIMITS.nostr, 'nostr', false).mode"));
        Assert.True(await page.EvaluateAsync<bool>("() => hasSelectedDestination(buildEventPayload()) && buildEventPayload().sendToNostr"));
        await page.ReloadAsync();
        Assert.True(await page.Locator("#sendNostr").IsCheckedAsync());
        Assert.False(await page.Locator("#sendMastodon").IsCheckedAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }
    [Fact]
    public async Task BlueskyPreview_UsesGraphemeCapacityAndRetainsItsSelectedDestination()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/custom-event-designer.html");
        await page.Locator("#sendBluesky").CheckAsync();
        await page.Locator("#titleInput").FillAsync(new string('x', 500));
        await page.Locator("#contentInput").FillAsync("");
        var longTitle = await page.EvaluateAsync<string>("() => buildPlan(titleInput.value, '', LIMITS.bluesky, 'bluesky', false).postText");
        Assert.True(longTitle.Length <= 300);
        var mode = await page.EvaluateAsync<string>("() => buildPlan('🦋'.repeat(300), '', LIMITS.bluesky, 'bluesky', false).mode");
        Assert.Equal("text", mode);
        var combined = "a" + new string('\u0301', 1600);
        var byteLimited = await page.EvaluateAsync<string>("title => buildPlan(title, '', LIMITS.bluesky, 'bluesky', false).postText", combined);
        Assert.True(BlueskyPost.Fits(byteLimited));
        Assert.Equal("image", await page.EvaluateAsync<string>("title => buildPlan(title, '', LIMITS.bluesky, 'bluesky', false).mode", combined));
        Assert.True(await page.EvaluateAsync<bool>("() => buildEventPayload().sendToBluesky"));
        await page.ReloadAsync();
        Assert.True(await page.Locator("#sendBluesky").IsCheckedAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(360)]
    public async Task FollowLinks_KeepIncreasingLabelLengthsAndAccessibleIcons(int width)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 850 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/events");
        var link = page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Follow Longevity World Cup on Nostr", Exact = true });
        await link.ScrollIntoViewIfNeededAsync();
        var labels = await link.Locator("..").Locator("a").AllTextContentsAsync();
        var lengths = labels.Select(label => label.Trim().Length).ToArray();
        Assert.Equal(lengths.Order().ToArray(), lengths);
        Assert.Equal(["X", "Nostr", "Reddit", "Threads", "YouTube", "Instagram"], labels.Select(label => label.Trim()));
        Assert.Equal(0, await page.Locator(".footer a[href*='bsky.app']").CountAsync());
        Assert.Equal("true", await link.Locator("svg").GetAttributeAsync("aria-hidden"));
        var box = Assert.IsType<LocatorBoundingBoxResult>(await link.BoundingBoxAsync());
        Assert.True(box.Height >= 44);
        Assert.True(box.X >= 0 && box.X + box.Width <= width);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        var captures = Environment.GetEnvironmentVariable("LWC_BLUESKY_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(captures))
        {
            Directory.CreateDirectory(captures);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(captures, $"footer-{width}.png") });
        }
    }
}
