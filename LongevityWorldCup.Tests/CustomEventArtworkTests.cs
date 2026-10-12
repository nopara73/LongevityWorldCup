using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class CustomEventArtworkTests(TestWebApplicationFactory factory)
{
    private const string Url = "https://longevityworldcup.com/rejuvenation-olympics";
    private const string Body = "Rejuvenation Olympics is closing. Longevity World Cup is your chance to keep competing.";
    private const string Raw = "[Coming from the Rejuvenation Olympics?](" + Url + ")\n\n" + Body;
    private const string StoryUrl = "https://nopara73.medium.com/crossfit-vs-hyrox-4d151061c611";
    private const string StoryRaw = "CrossFit vs HYROX\n\nWhat can Longevity World Cup learn from their growth?\n\n[Read the story](" + StoryUrl + ")";

    [Theory]
    [InlineData(280)]
    [InlineData(300)]
    [InlineData(500)]
    public void StoryAnnouncement_AttachesHeaderAndKeepsTheApprovedCopyAndMediumDestination(int limit)
    {
        var plan = CustomEventSocialComposer.BuildPlan("story", StoryRaw, limit);
        Assert.Equal(CustomEventPostMode.Artwork, plan.Mode);
        Assert.Equal(StoryUrl, plan.EventUrl);
        Assert.Contains("CrossFit vs HYROX", plan.PostText);
        Assert.Contains("What can Longevity World Cup learn from their growth?", plan.PostText);
        Assert.Contains(StoryUrl, plan.PostText);
        var visual = InstagramVisual.ForCustom(StoryRaw);
        Assert.Equal("assets/social/crossfit-vs-hyrox.png", visual.Artwork?.AssetPath);
        Assert.Equal("nopara73.medium.com/crossfit-vs-hyrox-4d151061c611", visual.Address);
        Assert.DoesNotContain("rejuvenation", visual.Address!);
    }

    [Theory]
    [InlineData("https://nopara73.medium.com/crossfit-vs-hyrox-4d151061c611?postPublishedType=initial", true)]
    [InlineData("https://nopara73.medium.com/crossfit-vs-hyrox-other", false)]
    [InlineData("https://evil.example/crossfit-vs-hyrox-4d151061c611", false)]
    [InlineData("https://nopara73.medium.com.evil.example/crossfit-vs-hyrox-4d151061c611", false)]
    [InlineData("https://user@nopara73.medium.com/crossfit-vs-hyrox-4d151061c611", false)]
    [InlineData("http://nopara73.medium.com/crossfit-vs-hyrox-4d151061c611", false)]
    [InlineData("https://nopara73.medium.com:444/crossfit-vs-hyrox-4d151061c611", false)]
    public void StoryArtwork_RequiresTheExactHttpsArticle(string url, bool selected)
        => Assert.Equal(selected, CustomEventArtwork.ForUrl(url) is not null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoryFeed_ContainsHeaderAndArticleLink(bool atom)
    {
        var item = new EventItem("story", EventType.CustomEvent, StoryRaw, new DateTime(2026, 10, 12, 6, 0, 0, DateTimeKind.Utc), 15, true);
        var xml = System.Xml.Linq.XDocument.Parse(EventSyndication.Build([item], [], atom));
        var html = atom ? xml.Descendants(System.Xml.Linq.XName.Get("content", "http://www.w3.org/2005/Atom")).Single().Value
            : xml.Descendants("item").Single().Element("description")!.Value;
        Assert.Contains("src=\"https://longevityworldcup.com/assets/social/crossfit-vs-hyrox.png\"", html);
        Assert.Contains("href=\"" + StoryUrl + "\"", html);
        var artwork = item.ToJson()["Artwork"]!.AsObject();
        Assert.Equal("https://longevityworldcup.com/assets/social/crossfit-vs-hyrox.png", artwork["ImageUrl"]!.GetValue<string>());
        Assert.Equal(StoryUrl, artwork["LinkUrl"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(280)]
    [InlineData(300)]
    [InlineData(500)]
    [InlineData(63206)]
    public void ShortAnnouncement_AttachesArtworkAndKeepsTheCompleteCopyAndLandingLink(int limit)
    {
        var plan = CustomEventSocialComposer.BuildPlan("event", Raw, limit);
        Assert.Equal(CustomEventPostMode.Artwork, plan.Mode);
        Assert.Equal("Coming from the Rejuvenation Olympics?\n\n" + Body + "\n\n" + Url, plan.PostText);
        Assert.Equal(Url, plan.EventUrl);
        Assert.DoesNotContain("events?", plan.PostText);
        Assert.DoesNotContain("](", plan.PostText);
        Assert.InRange(plan.PostText.Length, 1, limit);
    }

    [Theory]
    [InlineData("https://longevityworldcup.com.evil.example/rejuvenation-olympics")]
    [InlineData("https://evil.example/rejuvenation-olympics")]
    [InlineData("https://longevityworldcup.com@evil.example/rejuvenation-olympics")]
    [InlineData("http://longevityworldcup.com/rejuvenation-olympics")]
    [InlineData("https://longevityworldcup.com:444/rejuvenation-olympics")]
    [InlineData("https://longevityworldcup.com/leaderboard")]
    public void Artwork_IsOnlySelectedForTheConfiguredLocalPage(string url)
        => Assert.Null(CustomEventArtwork.ForLinkedPage("[Title](" + url + ")"));

    [Fact]
    public void OverflowingCopy_KeepsTheExistingTextImageFallback()
        => Assert.Equal(CustomEventPostMode.Image, CustomEventSocialComposer.BuildPlan("event", Raw + new string('x', 600), 280).Mode);

    [Fact]
    public async Task NativeAttachmentAndPreview_UseTheOriginalArtworkPixels()
    {
        var environment = factory.Services.GetRequiredService<IWebHostEnvironment>();
        using var original = await Image.LoadAsync<Rgba32>(CustomEventArtwork.ForLinkedPage(Raw)!.FullPath(environment.WebRootPath));
        var renderer = factory.Services.GetRequiredService<CustomEventImageService>();
        using var stream = await renderer.RenderToStreamAsync(Raw, useLinkedArtwork: true);
        Assert.NotNull(stream);
        using var attachment = await Image.LoadAsync<Rgba32>(stream!);
        Assert.Equal(new Size(1200, 675), attachment.Size);
        foreach (var (x, y) in new[] { (20, 20), (150, 200), (800, 250), (1000, 300) })
            Assert.Contains(original[x, y], new[] { attachment[x, y + 97], attachment[x, y + 98] });

        var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/custom-event-preview/image",
            System.Net.Http.Json.JsonContent.Create(new { Title = "[Coming from the Rejuvenation Olympics?](" + Url + ")", Content = Body, Platform = "x" }));
        response.EnsureSuccessStatusCode();
        using var preview = await Image.LoadAsync<Rgba32>(await response.Content.ReadAsStreamAsync());
        Assert.Equal(attachment.Size, preview.Size);
        Assert.Contains(original[800, 250], new[] { preview[800, 347], preview[800, 348] });
        using var og = await Image.LoadAsync<Rgba32>(await client.GetStreamAsync("/og/page/rejuvenation-olympics.png"));
        Assert.Equal(new Size(1200, 630), og.Size);
        Assert.Equal(original[800, 250], og[800, 325]);
    }

    [Fact]
    public async Task InstagramCard_IncludesArtworkAndTheVisibleDestination()
    {
        var visual = InstagramVisual.ForCustom(Raw);
        Assert.NotNull(visual.Artwork);
        Assert.Equal("longevityworldcup.com/rejuvenation-olympics", visual.Address);
        Assert.Equal(Body, visual.Details);
        var caption = InstagramPost.BuildPlan("event", Raw, null, true).PostText;
        Assert.Contains(Url, caption);
        Assert.Contains(Body, caption);
        var renderer = factory.Services.GetRequiredService<InstagramImageService>();
        var url = await renderer.RenderAsync(Raw, null, null);
        var environment = factory.Services.GetRequiredService<IWebHostEnvironment>();
        var path = Path.Combine(environment.WebRootPath, "generated", "instagram", Path.GetFileName(new Uri(url).LocalPath));
        using var image = await Image.LoadAsync<Rgba32>(path);
        Assert.Equal(1080, image.Width);
        Assert.Equal(1350, image.Height);
        var heroPixels = 0;
        for (var y = 250; y < 520; y += 2)
            for (var x = 400; x < 950; x += 2)
                if (image[x, y].R > 200 && image[x, y].G > 200 && image[x, y].B > 200) heroPixels++;
        Assert.True(heroPixels > 1000, "The original phoenix and artwork lettering must be present.");
        if (Environment.GetEnvironmentVariable("LWC_RO_PREVIEW_DIR") is { Length: > 0 } capture)
        {
            Directory.CreateDirectory(capture);
            File.Copy(path, Path.Combine(capture, "instagram.jpg"), true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FeedAnnouncement_ContainsTheArtworkAndTheLandingLink(bool atom)
    {
        var item = new EventItem("campaign", EventType.CustomEvent, Raw, new DateTime(2026, 10, 23, 15, 0, 0, DateTimeKind.Utc), 15, true);
        var xml = System.Xml.Linq.XDocument.Parse(EventSyndication.Build([item], [], atom));
        var html = atom ? xml.Descendants(System.Xml.Linq.XName.Get("content", "http://www.w3.org/2005/Atom")).Single().Value
            : xml.Descendants("item").Single().Element("description")!.Value;
        Assert.Contains("src=\"https://longevityworldcup.com/assets/social/rejuvenation-olympics.png\"", html);
        Assert.Contains("href=\"" + Url + "\"", html);
        Assert.Contains(Body, html);
    }
}
