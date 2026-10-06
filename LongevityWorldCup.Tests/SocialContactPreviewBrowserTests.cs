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
}
