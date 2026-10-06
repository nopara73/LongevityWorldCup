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
        foreach (var platform in new[] { SocialPlatform.X, SocialPlatform.Threads })
        {
            var actual = await page.EvaluateAsync<string>(
                "args => extractMentionHandle(args.contact, args.platform)",
                new { contact, platform = platform.ToString().ToLowerInvariant() });
            Assert.Equal(SocialContactParser.TryBuildMention(contact, platform) ?? "", actual);
        }
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
        Assert.True(await page.EvaluateAsync<bool>("() => buildEventPayload().sendToBluesky"));
        await page.ReloadAsync();
        Assert.True(await page.Locator("#sendBluesky").IsCheckedAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }
}
