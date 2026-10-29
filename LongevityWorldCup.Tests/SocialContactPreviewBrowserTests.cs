using LongevityWorldCup.Website.Tools;
using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class SocialContactPreviewBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task SocialPreviews_MatchTheServerMentionDestinations(int width)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var athletes = JsonNode.Parse("""
            [
                {"AthleteSlug":"preview-verified","DisplayName":"Preview Verified",
                 "MediaContact":"@legacy","PersonalLink":"https://instagram.com/personal",
                 "SocialProfiles":{"X":{"Url":"https://x.com/verified"},"Threads":{"Url":"thread.owner"},"Instagram":{"Url":"@photo.owner"}}},
                {"AthleteSlug":"preview-fallback","DisplayName":"Preview Fallback",
                 "MediaContact":"@legacy","PersonalLink":"https://instagram.com/personal",
                 "SocialProfiles":{"X":{"Url":"https://notx.com/imposter"}}},
                {"AthleteSlug":"preview-link","DisplayName":"Preview Link",
                 "MediaContact":"https://threads.net/@thread.owner","PersonalLink":"https://x.com/personal"},
                {"AthleteSlug":"preview-malformed","DisplayName":"Preview Malformed","MediaContact":"@legacy",
                 "SocialProfiles":{"X":{"Url":42},"Instagram":{"Url":"https://x.com/legacy"}}}
            ]
            """)!.AsArray();
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = athletes.ToJsonString()
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/custom-event-designer.html");
        await page.WaitForFunctionAsync("() => athleteMentionIndex.has('preview-verified')");
        var contacts = new[]
        {
            "https://netflix.com/alice", "https://notthreads.com/alice",
            "https://x.com.evil.example/alice", "https://threads.com.evil.example/alice",
            "HTTPS://X.COM/alice", "HTTPS://THREADS.COM/@alice",
            "https://mobile.twitter.com/alice", "www.threads.com/alice", "@alice",
            "alice", "@alice.smith", "@@alice", "@" + new string('a', 16),
            "https://threads.net/@alice", "https://instagram.com/alice.smith",
            "https://m.instagram.com/alice", "https://foo.x.com/alice",
            "https://user@x.com/alice", "https://x.com:8443/alice", "https://x.com:443/alice",
            "ftp://x.com/alice", "https://x.com/intent/post", "https://instagram.com/p/photo",
            "https://threads.com/accounts", "https://x.com/home", "https://x.com/@alice/status/123",
            "https://x.com/", "https://x.com/alice-smith", "https://instagram.com/.alice",
            "https://instagram.com/alice.", "https://instagram.com/alice..smith",
            "https://instagram.com/" + new string('a', 31)
        };
        var cases = (from contact in contacts
                     from platform in Enum.GetValues<SocialPlatform>()
                     from allowBareHandle in new[] { false, true }
                     select new
                     {
                         contact, platform = platform.ToString().ToLowerInvariant(), allowBareHandle,
                         expected = SocialContactParser.TryBuildMention(contact, platform, allowBareHandle) ?? ""
                     }).ToArray();
        var actual = await page.EvaluateAsync<string[]>(
            "cases => cases.map(args => extractMentionHandle(args.contact, args.platform, args.allowBareHandle))", cases);
        Assert.Equal(cases.Select(item => item.expected), actual);

        // The preview must also choose the same saved account and fallback as
        // the sender, without guessing that an X handle belongs to other sites.
        foreach (var athlete in athletes.OfType<JsonObject>())
        foreach (var platform in Enum.GetValues<SocialPlatform>())
        {
            var resolved = await page.EvaluateAsync<string>(
                "args => resolveMentionSlug(args.slug, args.platform)",
                new { slug = athlete["AthleteSlug"]!.GetValue<string>(), platform = platform.ToString().ToLowerInvariant() });
            Assert.Equal(AthleteSocialProfiles.ResolveMention(athlete, platform)
                ?? athlete["DisplayName"]!.GetValue<string>(), resolved);
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
    [InlineData(390)]
    public async Task InstagramPreview_AlwaysShowsAnImageAndRetainsAnIndependentDestination(int width)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/custom-event-designer.html");
        foreach (var id in new[] { "sendWebpage", "sendSlack", "sendX", "sendThreads", "sendFacebook", "sendMastodon", "sendNostr", "sendReddit", "sendBluesky", "sendWebPush" })
            await page.Locator("#" + id).UncheckAsync();
        await page.Locator("#sendInstagram").CheckAsync();
        await page.Locator("#titleInput").FillAsync("[strong](A sport for time) 🏆");
        await page.Locator("#contentInput").FillAsync("Follow [mention](benjamin_garden) in the Longevity World Cup.");
        Assert.True(await page.EvaluateAsync<bool>("() => hasSelectedDestination(buildEventPayload()) && buildEventPayload().sendToInstagram"));
        var preview = page.Locator(".instagram-post");
        await preview.WaitForAsync();
        // Image/OG completions replace the preview markup. Capture readiness and
        // both rectangles in one browser task so they belong to the same render.
        var layoutHandle = await page.WaitForFunctionAsync("""
            () => {
                const post = document.querySelector('.instagram-post');
                const image = post?.querySelector('.instagram-media img');
                const media = post?.querySelector('.instagram-media');
                const caption = post?.querySelector('.instagram-caption');
                if (!image?.complete || image.naturalWidth !== 1080 || image.naturalHeight !== 1350
                    || !media || !caption || !caption.textContent.includes('Follow')) return false;
                const picture = media.getBoundingClientRect();
                const text = caption.getBoundingClientRect();
                if (!picture.width || !picture.height || !text.width || !text.height) return false;
                return [picture.bottom, text.top, picture.width, picture.height];
            }
            """);
        var layout = await layoutHandle.JsonValueAsync<double[]>();
        await layoutHandle.DisposeAsync();
        Assert.Equal(4, layout.Length);
        Assert.True(layout[0] <= layout[1]);
        Assert.InRange(Math.Abs(layout[2] / layout[3] - 4d / 5), 0, .01);
        Assert.Equal("image", await page.EvaluateAsync<string>("() => buildPlan('A short update', '', LIMITS.instagram, 'instagram', false).mode"));
        var emojiTitle = string.Concat(Enumerable.Repeat("👩‍🔬", 500));
        var clipped = await page.EvaluateAsync<string>("title => buildPlan(title, '', LIMITS.instagram, 'instagram', false).postText", emojiTitle);
        Assert.Equal(InstagramPost.Truncate(emojiTitle, 2200), clipped);
        var oversizedLink = "[More](https://example.com/" + new string('a', 2300) + ")";
        Assert.Equal("Read the update", await page.EvaluateAsync<string>(
            "body => buildPlan('Read the update', body, LIMITS.instagram, 'instagram', false).postText", oversizedLink));
        Assert.DoesNotContain("[mention]", await preview.InnerTextAsync());
        Assert.DoesNotContain("@benjamin", await preview.InnerTextAsync());
        var captures = Environment.GetEnvironmentVariable("LWC_INSTAGRAM_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(captures))
        {
            Directory.CreateDirectory(captures);
            await preview.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = Path.Combine(captures, $"instagram-preview-{width}.png"), Animations = ScreenshotAnimations.Disabled
            });
        }
        await page.ReloadAsync();
        Assert.True(await page.Locator("#sendInstagram").IsCheckedAsync());
        Assert.False(await page.Locator("#sendThreads").IsCheckedAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(360)]
    public async Task FollowLinks_PreserveFeaturedOrderAndAccessibleIcons(int width)
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
        var labels = await page.Locator(".footer-follow-links .footer-link").AllTextContentsAsync();
        Assert.Equal(["Instagram", "YouTube", "X", "Nostr", "Reddit", "Threads", "Mastodon"], labels.Select(label => label.Trim()));
        Assert.Equal(0, await page.Locator(".footer a[href*='bsky.app']").CountAsync());
        Assert.Equal("true", await link.Locator("svg").GetAttributeAsync("aria-hidden"));
        var box = Assert.IsType<LocatorBoundingBoxResult>(await link.BoundingBoxAsync());
        Assert.True(box.Height >= 44);
        Assert.True(box.X >= 0 && box.X + box.Width <= width);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        Assert.Equal("/feeds/events.rss", await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Subscribe to Longevity World Cup via RSS", Exact = true }).GetAttributeAsync("href"));
        Assert.Equal("/events#eventBoardTitle", await page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Get browser notifications on Highlights", Exact = true }).GetAttributeAsync("href"));
        var captures = Environment.GetEnvironmentVariable("LWC_BLUESKY_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(captures))
        {
            Directory.CreateDirectory(captures);
            await page.Locator(".footer .footer-link").Last.ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(captures, $"footer-{width}.png"), Animations = ScreenshotAnimations.Disabled });
        }
    }
}
