using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1280, ColorScheme.Light, true)]
    [InlineData(844, ColorScheme.Dark, false)]
    [InlineData(390, ColorScheme.Dark, true)]
    [InlineData(320, ColorScheme.Light, false)]
    public async Task DiscussionYouTube_CardsFitPostsAndRepliesAndRetainOriginalLinks(int width, ColorScheme theme, bool signedIn)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme
        });
        await StubDiscussionThumbnailAsync(context);
        const string postUrl = "https://www.youtube.com/watch?v=usE4x1Gyss0&t=93s&list=PLtest";
        const string replyUrl = "https://youtu.be/usE4x1Gyss0?t=42";
        var state = YouTubeDiscussionState($"@Ari Able check this out if you have time:\n{postUrl}", $"Try this part: {replyUrl}");
        var page = await OpenDiscussionPolishAsync(context, state, "#discussion/post/p7/5", signedIn);
        var thread = DiscussionThread(page, "p7", 5);
        var post = thread.Locator(":scope > p .lmx-discussion-video");
        var reply = thread.Locator("[data-discussion-reply-body] .lmx-discussion-video");
        await Assertions.Expect(post).ToHaveAttributeAsync("href", postUrl);
        await Assertions.Expect(reply).ToHaveAttributeAsync("href", replyUrl);
        await Assertions.Expect(thread.Locator(":scope > p .lmx-note-mention")).ToHaveTextAsync("@Ari Able");
        await Assertions.Expect(thread.Locator(":scope > p")).ToContainTextAsync("check this out if you have time:");
        await Assertions.Expect(thread.Locator("iframe")).ToHaveCountAsync(0);
        foreach (var card in new[] { post, reply })
        {
            await card.ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(card).ToBeVisibleAsync();
            await Assertions.Expect(card).ToHaveAttributeAsync("target", "_blank");
            await Assertions.Expect(card).ToHaveAttributeAsync("rel", "noopener noreferrer ugc");
            await card.FocusAsync();
            await Assertions.Expect(card).ToBeFocusedAsync();
            Assert.True(await card.EvaluateAsync<bool>("e => getComputedStyle(e).outlineStyle !== 'none'"));
            Assert.True(await card.EvaluateAsync<bool>("e => { const r = e.getBoundingClientRect(); const p = e.parentElement.getBoundingClientRect(); return r.width <= 360 && Math.abs(r.width / r.height - 16 / 9) < .02 && r.left >= p.left && r.right <= p.right + 1; }"));
            await Assertions.Expect(card.Locator("img")).ToHaveAttributeAsync("loading", "lazy");
        }
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        var captureDirectory = Environment.GetEnvironmentVariable("LWC_DISCUSSION_CAPTURE_DIR");
        if (!string.IsNullOrEmpty(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory);
            await thread.ScreenshotAsync(new() { Path = Path.Combine(captureDirectory, $"youtube-{width}-{theme}.png") });
        }
    }

    [Fact]
    public async Task DiscussionYouTube_RecognizesVideoUrlsWithoutTurningOtherLinksIntoPreviews()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await StubDiscussionThumbnailAsync(context);
        var videos = new[] { "https://youtu.be/usE4x1Gyss0", "www.youtube.com/watch?v=usE4x1Gyss0", "https://m.youtube.com/shorts/usE4x1Gyss0", "https://music.youtube.com/watch?v=usE4x1Gyss0", "https://www.youtube.com/live/usE4x1Gyss0", "https://www.youtube.com/embed/usE4x1Gyss0" };
        var ordinary = new[] { "https://youtube.com.evil.test/watch?v=usE4x1Gyss0", "https://example.com/watch?v=usE4x1Gyss0", "https://youtube.com/@channel", "https://youtube.com/playlist?list=PLtest", "https://youtube.com/watch?v=too-short", "https://youtu.be/usE4x1Gyss0/extra", "https://youtube.com:8443/watch?v=usE4x1Gyss0" };
        var state = YouTubeDiscussionState(string.Join("\n", videos.Select(url => $"({url}).")), string.Join("\n", ordinary) + " <script>alert(1)</script>");
        var page = await OpenDiscussionPolishAsync(context, state, "#discussion/post/p7/5", signedIn: false);
        var thread = DiscussionThread(page, "p7", 5);
        await Assertions.Expect(thread.Locator(":scope > p .lmx-discussion-video")).ToHaveCountAsync(videos.Length);
        await Assertions.Expect(thread.Locator("[data-discussion-reply-body] .lmx-discussion-video")).ToHaveCountAsync(0);
        await Assertions.Expect(thread.Locator("[data-discussion-reply-body] .lmx-discussion-text-link")).ToHaveCountAsync(ordinary.Length);
        await Assertions.Expect(thread.Locator("script")).ToHaveCountAsync(0);
        await Assertions.Expect(thread.Locator("[data-discussion-reply-body]")).ToContainTextAsync("<script>alert(1)</script>");
        foreach (var card in await thread.Locator(".lmx-discussion-video").AllAsync())
            await Assertions.Expect(card.Locator("img")).ToHaveAttributeAsync("src", "https://i.ytimg.com/vi/usE4x1Gyss0/hqdefault.jpg");
    }

    [Fact]
    public async Task DiscussionYouTube_FailedThumbnailKeepsTheCardAndLinkUsable()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.RouteAsync("https://i.ytimg.com/**", route => route.AbortAsync());
        const string url = "https://www.youtube.com/watch?v=usE4x1Gyss0";
        var page = await OpenDiscussionPolishAsync(context, YouTubeDiscussionState(url, "A reply."), "#discussion/post/p7/5", signedIn: false);
        var card = DiscussionThread(page, "p7", 5).Locator(".lmx-discussion-video");
        await card.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(card).ToHaveClassAsync("lmx-discussion-video has-error");
        await Assertions.Expect(card.Locator("img")).ToBeHiddenAsync();
        await Assertions.Expect(card.Locator(".lmx-discussion-video-play")).ToBeVisibleAsync();
        await Assertions.Expect(card).ToHaveAttributeAsync("href", url);
        await card.FocusAsync();
        await Assertions.Expect(card).ToBeFocusedAsync();
        // Exercise native keyboard navigation without depending on YouTube availability.
        await context.RouteAsync(url, route => route.FulfillAsync(new() { ContentType = "text/html", Body = "Video destination" }));
        var popup = await page.RunAndWaitForPopupAsync(() => page.Keyboard.PressAsync("Enter"));
        await popup.WaitForLoadStateAsync();
        Assert.Equal(url, popup.Url);
    }

    private static JsonObject YouTubeDiscussionState(string note, string reply)
    {
        var state = DiscussionWorkspaceState();
        foreach (var collection in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            var post = collection.AsArray()[0]!;
            post["note"] = note;
            post["replies"] = JsonSerializer.SerializeToNode(new[] { Reply("youtube-reply", "p1", "Browser Tester", reply, "2026-07-01T09:59:30Z") });
            post["replyCount"] = 1;
            post["lastActivityAtUtc"] = "2026-07-01T09:59:30Z";
        }
        return state;
    }

    private static Task StubDiscussionThumbnailAsync(IBrowserContext context) => context.RouteAsync("https://i.ytimg.com/**", route => route.FulfillAsync(new()
    {
        ContentType = "image/svg+xml",
        Body = "<svg xmlns='http://www.w3.org/2000/svg' width='480' height='360'><rect width='480' height='360' fill='#202d38'/><rect y='45' width='480' height='270' fill='#4d7184'/></svg>"
    }));
}
