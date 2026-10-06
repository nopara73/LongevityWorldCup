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
}
