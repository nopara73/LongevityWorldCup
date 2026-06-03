using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialImageRenderingTests
{
    [Fact]
    public async Task XAutoposterImages_RenderAsPngCanvases()
    {
        using var factory = CreateFactory();
        var images = factory.Services.GetRequiredService<XImageService>();
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var top3Slugs = athletes.GetTop3SlugsForLeague("ultimate").Take(3).ToList();

        Assert.True(top3Slugs.Count >= 3);

        await AssertPngCanvasAsync(await images.BuildNewRankImageAsync(top3Slugs[0], top3Slugs[1]));
        await AssertPngCanvasAsync(await images.BuildSingleAthleteImageAsync(top3Slugs[0]));
        await AssertPngCanvasAsync(await images.BuildAthleteCountMilestoneImageAsync(100));
        await AssertPngCanvasAsync(await images.BuildTop3LeaderboardPodiumImageAsync(top3Slugs));
        await AssertPngCanvasAsync(await images.BuildNewcomersImageAsync(top3Slugs));
    }

    [Fact]
    public async Task CustomEventAutoposterImage_RenderAsPngCanvas()
    {
        using var factory = CreateFactory();
        var images = factory.Services.GetRequiredService<CustomEventImageService>();

        await AssertPngCanvasAsync(await images.RenderToStreamAsync("Season update\nLongevity World Cup athletes keep pushing biological age sport forward."));
    }

    [Fact]
    public async Task AthleteAndLeagueSharePreviewImages_RenderAsPngCanvases()
    {
        using var factory = CreateFactory();
        var athleteImages = factory.Services.GetRequiredService<AthleteOgImageService>();
        var leagueImages = factory.Services.GetRequiredService<LeagueOgImageService>();

        Assert.True(athleteImages.TryGetCurrentPayload("ron-lugbill", out var athletePayload));
        Assert.True(leagueImages.TryGetCurrentPayload("ultimate", out var leaguePayload));

        await AssertPngFileCanvasAsync(await athleteImages.EnsureRenderedImageAsync(athletePayload), 1200, 630);
        await AssertPngFileCanvasAsync(await leagueImages.EnsureRenderedImageAsync(leaguePayload), 1200, 630);
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

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("EnableScheduledJobs", "false");
                builder.UseSetting("EnableStartupBadgeRefresh", "false");
            });
    }
}
