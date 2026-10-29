using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class ByFlagArtworkTests(TestWebApplicationFactory factory)
{
    private const string Url = "https://longevityworldcup.com/leaderboard?group=flags";
    private const string Title = "New: [By Flag leaderboards](" + Url + "). Flags rank by their top three athletes' total age reduction. Expand a flag to see its team.";

    [Theory]
    [InlineData(280)]
    [InlineData(300)]
    [InlineData(500)]
    [InlineData(63206)]
    public void Announcement_AttachesScreenshotAndRetainsCopyAndExactDestination(int limit)
    {
        var plan = CustomEventSocialComposer.BuildPlan("flags", Title, limit);
        Assert.Equal(CustomEventPostMode.Artwork, plan.Mode);
        Assert.Equal(Url, plan.EventUrl);
        Assert.Contains("Flags rank by their top three athletes' total age reduction. Expand a flag to see its team.", plan.PostText);
        Assert.Contains(Url, plan.PostText);
        Assert.DoesNotContain("](", plan.PostText);
        var visual = InstagramVisual.ForCustom(Title);
        Assert.Equal("longevityworldcup.com/leaderboard?group=flags", visual.Address);
        Assert.Equal("assets/social/by-flag-20261017-instagram.png", visual.Artwork?.InstagramAssetPath);
        Assert.Empty(visual.Details);
    }

    [Theory]
    [InlineData("https://longevityworldcup.com/leaderboard")]
    [InlineData("https://longevityworldcup.com/leaderboard?group=athletes")]
    [InlineData("https://longevityworldcup.com/leaderboard?group=flags&view=pheno")]
    [InlineData("https://longevityworldcup.com.evil.example/leaderboard?group=flags")]
    public void OtherCompetitions_DoNotGetTheReleaseScreenshot(string url)
        => Assert.Null(CustomEventArtwork.ForUrl(url));

    [Theory]
    [InlineData("x", "by-flag-20261017.png", 1200, 675)]
    [InlineData("instagram", "by-flag-20261017-instagram.png", 1080, 1350)]
    public async Task NativePreview_ContainsTheActualScreenshotPixels(string platform, string asset, int width, int height)
    {
        var env = factory.Services.GetRequiredService<IWebHostEnvironment>();
        using var original = await Image.LoadAsync<Rgba32>(Path.Combine(env.WebRootPath, "assets", "social", asset));
        using var response = await factory.CreateClient().PostAsync("/api/custom-event-preview/image",
            System.Net.Http.Json.JsonContent.Create(new { Title, Content = "", Platform = platform }));
        response.EnsureSuccessStatusCode();
        using var preview = Image.Load<Rgba32>(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(new Size(width, height), preview.Size);
        foreach (var (x, y) in new[] { (100, 200), (400, 300), (800, 500) }) Assert.Equal(original[x, y], preview[x, y]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Feed_ContainsScreenshotAndExactFlagLink(bool atom)
    {
        var item = new EventItem("flags", EventType.CustomEvent, Title, DateTime.UtcNow, 15, true);
        var xml = System.Xml.Linq.XDocument.Parse(EventSyndication.Build([item], [], atom));
        var html = atom ? xml.Descendants(System.Xml.Linq.XName.Get("content", "http://www.w3.org/2005/Atom")).Single().Value
            : xml.Descendants("item").Single().Element("description")!.Value;
        Assert.Contains("https://longevityworldcup.com/assets/social/by-flag-20261017.png", html);
        Assert.Contains(Url, html);
        Assert.Equal(Url, item.ToJson()["Artwork"]!["LinkUrl"]!.GetValue<string>());
    }

    [Fact]
    public void BrowserPush_PreservesTheFlagGroupingInItsClickDestination()
    {
        var env = factory.Services.GetRequiredService<IWebHostEnvironment>();
        using var payload = System.Text.Json.JsonDocument.Parse(WebPushAnnouncementService.BuildPayload(
            new("flags", Title, true, "", null), new(env)));
        Assert.Equal("/leaderboard?group=flags", payload.RootElement.GetProperty("url").GetString());
        Assert.StartsWith("/assets/social/by-flag-20261017.png?v=", payload.RootElement.GetProperty("image").GetString());
    }
}
