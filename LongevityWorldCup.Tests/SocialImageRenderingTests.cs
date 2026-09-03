using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
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
