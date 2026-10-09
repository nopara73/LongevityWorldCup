using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialImageRenderingTests(TestWebApplicationFactory sharedFactory)
    : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task XAutoposterImages_RenderAsPngCanvases()
    {
        var factory = sharedFactory;
        var images = factory.Services.GetRequiredService<XImageService>();
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var top3Slugs = athletes.GetTop3SlugsForLeague("ultimate").Take(3).ToList();

        Assert.True(top3Slugs.Count >= 3);

        await AssertPngCanvasAsync(await images.BuildNewRankImageAsync(top3Slugs[0], top3Slugs[1]));
        await AssertPngCanvasAsync(await images.BuildSingleAthleteImageAsync(top3Slugs[0]));
        await AssertPngCanvasAsync(await images.BuildAthleteCountMilestoneImageAsync(100));
        await AssertPngCanvasAsync(await images.BuildTop3LeaderboardPodiumImageAsync(top3Slugs));
        await AssertPngCanvasAsync(await images.BuildNewcomersImageAsync(top3Slugs));
        await AssertPngCanvasAsync(await images.BuildNewcomersImageAsync(new[]
        {
            "andressa-lohana-de-almeida",
            "vishwamithra-shashishekara",
            "teodor-katrandjiev-okoto"
        }));
    }

    [Fact]
    public async Task AthleteCountMilestoneMemes_ResolveOnlyApprovedMemeNumbers()
    {
        var factory = sharedFactory;
        var memes = factory.Services.GetRequiredService<AthleteCountMilestoneMemeService>();

        foreach (var count in new[] { 404, 666, 777, 1337, 9001 })
        {
            Assert.True(memes.TryGetMeme(count, out var meme));
            Assert.Equal(count, meme.AthleteCount);
            Assert.StartsWith("https://longevityworldcup.com/assets/social/memes/", meme.PublicUrl);
            Assert.True(File.Exists(meme.FullPath));

            using var image = await Image.LoadAsync(meme.FullPath);
            Assert.True(image.Width > 0);
            Assert.True(image.Height > 0);
        }

        foreach (var count in new[] { 42, 69, 100, 123, 200, 222, 256, 300, 500, 1000, 2048, 8008, 8888, 9999, 10000 })
            Assert.False(memes.TryGetMeme(count, out _));
    }

    [Fact]
    public async Task CustomEventAutoposterImage_RenderAsPngCanvas()
    {
        var factory = sharedFactory;
        var images = factory.Services.GetRequiredService<CustomEventImageService>();

        await AssertPngCanvasAsync(await images.RenderToStreamAsync("Season update\nLongevity World Cup athletes keep pushing biological age sport forward."));
    }

    [Fact]
    public async Task InstagramImages_AreImmutableJpegsWithinThePublishingLimits()
    {
        var images = sharedFactory.Services.GetRequiredService<InstagramImageService>();
        var environment = sharedFactory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        const string announcement = "[strong](A sport for time) 🏆\n\nLongevity World Cup announcements now have an image and a caption.";
        var url = await images.RenderAsync(announcement, null, null);
        Assert.Equal(url, await images.RenderAsync(announcement, null, null));
        Assert.Matches(@"^https://longevityworldcup\.com/generated/instagram/[a-f0-9]{64}\.jpg$", url);
        var path = Path.Combine(environment.WebRootPath, "generated", "instagram", Path.GetFileName(new Uri(url).LocalPath));
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.InRange(bytes.Length, 1, 8_000_000);
        Assert.Equal("JPEG", Image.DetectFormat(bytes).Name);
        using var image = Image.Load(bytes);
        Assert.Equal(1080, image.Width);
        Assert.Equal(1350, image.Height);
        Assert.InRange(image.Width, 320, 1440);
        Assert.InRange((double)image.Width / image.Height, .8, 1.91);
        var captures = Environment.GetEnvironmentVariable("LWC_INSTAGRAM_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(captures))
        {
            Directory.CreateDirectory(captures);
            File.Copy(path, Path.Combine(captures, "announcement-card.jpg"), overwrite: true);
        }
    }

    [Theory]
    [InlineData("mastodon", "LWC is now on [Mastodon](https://mastodon.social/@longevityworldcup)")]
    [InlineData("bluesky", "LWC is now on [Bluesky](https://bsky.app/profile/longevityworldcup.bsky.social)")]
    [InlineData("instagram", "LWC announcements are now on [Instagram](https://www.instagram.com/longevityworldcup/)")]
    [InlineData("rss", "Follow LWC by [RSS](https://longevityworldcup.com/feeds/events.rss)")]
    [InlineData("browser", "LWC announcements, straight to your [browser](https://longevityworldcup.com/events) 🔔")]
    [InlineData("reddit", "LWC announcements are now on [Reddit](https://www.reddit.com/r/LongevityWorldCup/)")]
    public async Task InstagramLaunchImages_RenderTheirPlatformMark(string name, string announcement)
    {
        var images = sharedFactory.Services.GetRequiredService<InstagramImageService>();
        var url = await images.RenderAsync(announcement, null, null);
        var path = InstagramImagePath(url);
        using var image = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(path);
        var visibleMarkPixels = 0;
        for (var y = 255; y < 555; y++)
            for (var x = 390; x < 690; x++)
                if (image[x, y].R > 220 && image[x, y].G > 220 && image[x, y].B > 220) visibleMarkPixels++;
        Assert.True(visibleMarkPixels > 3000, "The platform mark must be present on the image itself.");
        CaptureInstagram(path, name);
    }

    [Fact]
    public async Task AnnouncementImages_ChangeWhenOnlyTheHeadlineChanges()
    {
        const string body = "\n\nThe same supporting detail.";
        var instagram = sharedFactory.Services.GetRequiredService<InstagramImageService>();
        var first = await instagram.RenderAsync("The season begins" + body, null, null);
        var second = await instagram.RenderAsync("The season ends" + body, null, null);
        Assert.NotEqual(first, second);
        CaptureInstagram(InstagramImagePath(first), "season");

        var cards = sharedFactory.Services.GetRequiredService<CustomEventImageService>();
        using var cardA = await cards.RenderToStreamAsync("The season begins" + body);
        using var cardB = await cards.RenderToStreamAsync("The season ends" + body);
        Assert.NotEqual(cardA!.ToArray(), cardB!.ToArray());
    }

    [Fact]
    public async Task InstagramAthleteImage_KeepsTheEventAndAthleteTogether()
    {
        var images = sharedFactory.Services.GetRequiredService<InstagramImageService>();
        var visual = LongevityWorldCup.Website.Tools.InstagramVisual.ForEvent(EventType.NewRank,
            "slug[ron_lugbill] rank[1] prev[alice_smith]", "Ron Lugbill is now 1st in the Ultimate League.");
        var url = await images.RenderAsync(visual, null);
        CaptureInstagram(InstagramImagePath(url), "athlete");
        using var image = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(InstagramImagePath(url));
        Assert.Equal(1350, image.Height);
        var portraitColors = new HashSet<SixLabors.ImageSharp.PixelFormats.Rgba32>();
        for (var y = 240; y < 580; y += 10)
            for (var x = 370; x < 710; x += 10) portraitColors.Add(image[x, y]);
        Assert.True(portraitColors.Count > 100, "The athlete's actual photo must be present.");
    }

    private string InstagramImagePath(string url) => Path.Combine(
        sharedFactory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().WebRootPath,
        "generated", "instagram", Path.GetFileName(new Uri(url).LocalPath));

    [Fact]
    public async Task InstagramPreview_UsesThePortraitRendererWithoutPublishingAnEvent()
    {
        using var client = sharedFactory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/custom-event-preview/image", new
        {
            title = "LWC is now on [Mastodon](https://mastodon.social/@longevityworldcup)", content = "", platform = "instagram"
        });
        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        using var image = Image.Load(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(1080, image.Width);
        Assert.Equal(1350, image.Height);
    }

    [Fact]
    public async Task InstagramMilestoneImage_RetainsTheMemeAndAddsItsAnnouncementContext()
    {
        var images = sharedFactory.Services.GetRequiredService<InstagramImageService>();
        var memes = sharedFactory.Services.GetRequiredService<AthleteCountMilestoneMemeService>();
        Assert.True(memes.TryGetMeme(777, out var meme));
        var first = await images.RenderAsync("777 athletes now compete in LWC", null, meme.FullPath);
        var second = await images.RenderAsync("888 athletes now compete in LWC", null, meme.FullPath);
        Assert.NotEqual(first, second); // A meme alone loses which milestone happened.
        using var image = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(InstagramImagePath(first));
        Assert.True(HeroColorCount(image) > 100);
        CaptureInstagram(InstagramImagePath(first), "milestone");
    }

    [Fact]
    public async Task InstagramPodcastImage_UsesEpisodeArtworkAndRetainsItsHeadlineWhenArtworkIsMissing()
    {
        var env = sharedFactory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        var athletes = sharedFactory.Services.GetRequiredService<AthleteDataService>();
        var artwork = File.ReadAllBytes(Path.Combine(env.WebRootPath, "athletes", "ron_lugbill", "ron_lugbill.jpeg"));
        var http = new ThumbnailFactory(artwork);
        var cards = new InstagramAnnouncementImageService(env, athletes, http, NullLogger<InstagramAnnouncementImageService>.Instance);
        var visual = InstagramVisual.ForCustom("Ron Lugbill on Immortal Combat\n\n[Watch](https://www.youtube.com/watch?v=kOWAsyQCtH4)");
        using var rendered = await cards.RenderAsync(visual);
        using var image = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(rendered);
        Assert.True(HeroColorCount(image) > 100);
        Assert.Equal("https://i.ytimg.com/vi/kOWAsyQCtH4/hqdefault.jpg", Assert.Single(http.Requests).AbsoluteUri);

        http.Artwork = null;
        using var fallback = await cards.RenderAsync(visual);
        using var withoutArtwork = Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(fallback);
        Assert.Equal(1350, withoutArtwork.Height);
        Assert.NotEqual(rendered.ToArray(), fallback.ToArray());
    }

    private static int HeroColorCount(Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image)
    {
        var colors = new HashSet<SixLabors.ImageSharp.PixelFormats.Rgba32>();
        for (var y = 240; y < 580; y += 10)
            for (var x = 370; x < 710; x += 10) colors.Add(image[x, y]);
        return colors.Count;
    }

    [Fact]
    public async Task InstagramImage_OmitsAnUnsupportedDecorativeEmojiInsteadOfDrawingAMissingGlyph()
    {
        var images = sharedFactory.Services.GetRequiredService<InstagramImageService>();
        var plain = await images.RenderAsync("A sport for time", null, null);
        var decorated = await images.RenderAsync("A sport for time 🏆", null, null);
        Assert.Equal(plain, decorated);
    }

    private sealed class ThumbnailFactory(byte[] artwork) : HttpMessageHandler, IHttpClientFactory
    {
        internal byte[]? Artwork { get; set; } = artwork;
        internal List<Uri> Requests { get; } = [];
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(Artwork is null ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(Artwork) });
        }
    }

    private static void CaptureInstagram(string path, string name)
    {
        var captures = Environment.GetEnvironmentVariable("LWC_INSTAGRAM_SCREENSHOT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(captures)) return;
        Directory.CreateDirectory(captures);
        File.Copy(path, Path.Combine(captures, name + ".jpg"), overwrite: true);
    }

    [Fact]
    public async Task AthleteLeagueAndPageSharePreviewImages_RenderAsPngCanvases()
    {
        var factory = sharedFactory;
        var athleteImages = factory.Services.GetRequiredService<AthleteOgImageService>();
        var leagueImages = factory.Services.GetRequiredService<LeagueOgImageService>();
        var pageImages = factory.Services.GetRequiredService<PageOgImageService>();

        Assert.True(athleteImages.TryGetCurrentPayload("ron-lugbill", out var athletePayload));
        Assert.True(leagueImages.TryGetCurrentPayload("ultimate", out var leaguePayload));

        await AssertPngFileCanvasAsync(await athleteImages.EnsureRenderedImageAsync(athletePayload), 1200, 630);
        await AssertPngFileCanvasAsync(await leagueImages.EnsureRenderedImageAsync(leaguePayload), 1200, 630);

        foreach (var pageSlug in new[]
                 {
                     "home",
                     "events",
                     "media",
                     "about",
                     "history",
                     "ruleset",
                     "view-bortz",
                     "view-pheno",
                     "view-improvement",
                     "view-bortz-improvement",
                     "view-crowd"
                 })
        {
            Assert.True(pageImages.TryGetCurrentPayload(pageSlug, out var pagePayload));
            await AssertPngFileCanvasAsync(await pageImages.EnsureRenderedImageAsync(pagePayload), 1200, 630);
        }
    }

    [Fact]
    public async Task AthleteCrowdAgeSharePreviewImage_UsesCrowdAgeMetrics()
    {
        var factory = sharedFactory;
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var athleteImages = factory.Services.GetRequiredService<AthleteOgImageService>();

        Assert.True(athletes.TryGetProfileImageId("ron_lugbill", out var profileImageId));
        for (var i = 0; i < 100; i++)
            Assert.True(athletes.TryAddAgeGuess("ron_lugbill", profileImageId, 68));

        Assert.True(athleteImages.TryGetCurrentPayload("ron-lugbill", "crowd", out var payload));
        Assert.Equal("crowd", payload.LeagueSlug);
        Assert.Equal(1, payload.Rank);
        Assert.Equal("Crowd Age leaderboard", payload.LeagueName);
        Assert.Equal("Crowd Age rank", payload.RankLabel);
        Assert.Equal("68", payload.MetricValue);
        Assert.Equal("Crowd Age", payload.MetricLabel);
        Assert.Contains("100 guesses", payload.Description);

        await AssertPngFileCanvasAsync(await athleteImages.EnsureRenderedImageAsync(payload), 1200, 630);
    }

    [Theory]
    [InlineData("pheno", "Pheno Age")]
    [InlineData("bortz", "Bortz Age")]
    public async Task AthleteBiologicalAgeSharePreviewImage_UsesClockSpecificMetrics(string context, string clockLabel)
    {
        var factory = sharedFactory;
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var athleteImages = factory.Services.GetRequiredService<AthleteOgImageService>();
        AthleteOgImageService.AthleteOgPayload? payload = null;

        foreach (var athlete in athletes.GetAthletesSnapshot().OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var slug = athlete["AthleteSlug"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(slug) &&
                athleteImages.TryGetCurrentPayload(slug, context, out var candidate) &&
                string.Equals(candidate.LeagueSlug, context, StringComparison.Ordinal))
            {
                payload = candidate;
                break;
            }
        }

        Assert.NotNull(payload);
        Assert.Equal(context, payload!.LeagueSlug);
        Assert.Equal($"{clockLabel} leaderboard", payload.LeagueName);
        Assert.Equal($"{clockLabel} rank", payload.RankLabel);
        Assert.Equal(clockLabel, payload.MetricLabel);
        Assert.Contains("age reduction", payload.Description, StringComparison.OrdinalIgnoreCase);

        await AssertPngFileCanvasAsync(await athleteImages.EnsureRenderedImageAsync(payload), 1200, 630);
    }

    [Theory]
    [InlineData("pheno", "pheno-baseline-improvement", "improvement")]
    [InlineData("bortz", "bortz-baseline-improvement", "bortz-improvement")]
    public async Task AthleteImprovementSharePreviews_DistinguishBaselineBadgesFromLeaderboardEvents(
        string clock,
        string baselineContext,
        string leaderboardContext)
    {
        var factory = sharedFactory;
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var athleteImages = factory.Services.GetRequiredService<AthleteOgImageService>();
        AgeImprovementLeaderboardEntry? baselineEntry = null;
        AthleteOgImageService.AthleteOgPayload? baselinePayload = null;
        AthleteOgImageService.AthleteOgPayload? leaderboardPayload = null;

        foreach (var athlete in athletes.GetAthletesSnapshot().OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var slug = athlete["AthleteSlug"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(slug) &&
                athletes.TryGetBaselineImprovementLeaderboardEntry(slug, clock, out var candidateEntry) &&
                athleteImages.TryGetCurrentPayload(slug, baselineContext, out var candidateBaseline) &&
                athleteImages.TryGetCurrentPayload(slug, leaderboardContext, out var candidateLeaderboard))
            {
                baselineEntry = candidateEntry;
                baselinePayload = candidateBaseline;
                leaderboardPayload = candidateLeaderboard;
                break;
            }
        }

        Assert.NotNull(baselineEntry);
        Assert.NotNull(baselinePayload);
        Assert.NotNull(leaderboardPayload);
        Assert.Equal(baselineContext, baselinePayload!.LeagueSlug);
        Assert.Equal(baselineEntry!.Rank, baselinePayload.Rank);
        Assert.Equal(baselineEntry.Improvement, baselinePayload.AgeReduction, 6);
        Assert.Equal("Baseline rank", baselinePayload.RankLabel);
        Assert.Equal("Baseline improvement", baselinePayload.MetricLabel);
        Assert.Contains("from first to latest eligible result", baselinePayload.Description);
        Assert.DoesNotContain("from worst", baselinePayload.Description);
        Assert.Equal(leaderboardContext, leaderboardPayload!.LeagueSlug);
        Assert.Contains("from worst to latest eligible result", leaderboardPayload.Description);

        await AssertPngFileCanvasAsync(await athleteImages.EnsureRenderedImageAsync(baselinePayload), 1200, 630);
        await AssertPngFileCanvasAsync(await athleteImages.EnsureRenderedImageAsync(leaderboardPayload), 1200, 630);
    }

    [Fact]
    public async Task AthleteDomainSharePreviewImage_UsesTheDomainWinnerContext()
    {
        var factory = sharedFactory;
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var badges = factory.Services.GetRequiredService<BadgeDataService>();
        var athleteImages = factory.Services.GetRequiredService<AthleteOgImageService>();
        var domainKeys = new[] { "liver", "kidney", "metabolic", "inflammation", "immune", "vitamin_d" };
        badges.ComputeAndPersistAwards();
        var domainKey = domainKeys.First(key => !string.IsNullOrWhiteSpace(athletes.GetBestDomainWinnerSlug(key)));
        var winnerSlug = athletes.GetBestDomainWinnerSlug(domainKey);
        var context = $"domain-{domainKey.Replace('_', '-')}";

        Assert.True(athleteImages.TryGetCurrentPayload(winnerSlug!, context, out var payload));
        Assert.Equal(context, payload.LeagueSlug);
        Assert.Equal(1, payload.Rank);
        Assert.Equal("Domain rank", payload.RankLabel);
        Assert.Equal("#1", payload.MetricValue);
        Assert.DoesNotContain("Ultimate League", payload.Description, StringComparison.OrdinalIgnoreCase);

        await AssertPngFileCanvasAsync(await athleteImages.EnsureRenderedImageAsync(payload), 1200, 630);
    }

    [Theory]
    [InlineData("chronological-oldest", "Oldest athlete")]
    [InlineData("chronological-youngest", "Youngest athlete")]
    public async Task AthleteChronologicalAgeSharePreviewImage_UsesTheRequestedAgeContext(string context, string leagueName)
    {
        var factory = sharedFactory;
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var athleteImages = factory.Services.GetRequiredService<AthleteOgImageService>();
        AthleteOgImageService.AthleteOgPayload? payload = null;

        foreach (var athlete in athletes.GetAthletesSnapshot().OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var slug = athlete["AthleteSlug"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(slug) &&
                athleteImages.TryGetCurrentPayload(slug, context, out var candidate))
            {
                payload = candidate;
                break;
            }
        }

        Assert.NotNull(payload);
        Assert.Equal(context, payload!.LeagueSlug);
        Assert.Equal(leagueName, payload.LeagueName);
        Assert.Equal("Age rank", payload.RankLabel);
        Assert.Equal("Chronological age", payload.MetricLabel);

        await AssertPngFileCanvasAsync(await athleteImages.EnsureRenderedImageAsync(payload), 1200, 630);
    }

    [Fact]
    public void AthleteSharePreview_DoesNotFallBackToUltimateForAnUnavailableKnownContext()
    {
        var factory = sharedFactory;
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var athleteImages = factory.Services.GetRequiredService<AthleteOgImageService>();
        string? phenoOnlySlug = null;

        foreach (var athlete in athletes.GetAthletesSnapshot().OfType<System.Text.Json.Nodes.JsonObject>())
        {
            var slug = athlete["AthleteSlug"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(slug) &&
                athletes.TryGetBiologicalAgeLeaderboardEntry(slug, "pheno", out _) &&
                !athletes.TryGetBiologicalAgeLeaderboardEntry(slug, "bortz", out _))
            {
                phenoOnlySlug = slug;
                break;
            }
        }

        Assert.False(string.IsNullOrWhiteSpace(phenoOnlySlug));
        Assert.False(athleteImages.TryGetCurrentPayload(phenoOnlySlug!, "bortz", out _));
    }

    private static async Task AssertPngCanvasAsync(Stream? stream)
    {
        Assert.NotNull(stream);

        await using var rendered = stream!;
        using var image = await Image.LoadAsync(rendered);

        Assert.Equal(1200, image.Width);
        Assert.Equal(675, image.Height);
    }

    private static async Task AssertPngFileCanvasAsync(string? path, int width, int height)
    {
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(File.Exists(path));

        using var image = await Image.LoadAsync(path);

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
    }

}
