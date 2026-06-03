using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SharePreviewMetadataTests
{
    [Theory]
    [InlineData("/", "/og/page/home.png?v=")]
    [InlineData("/events", "/og/page/events.png?v=")]
    [InlineData("/media", "/og/page/media.png?v=")]
    [InlineData("/about", "/og/page/about.png?v=")]
    [InlineData("/history", "/og/page/history.png?v=")]
    [InlineData("/ruleset", "/og/page/ruleset.png?v=")]
    [InlineData("/league/bortz", "/og/page/view-bortz.png?v=")]
    [InlineData("/league/pheno", "/og/page/view-pheno.png?v=")]
    [InlineData("/league/crowd", "/og/page/view-crowd.png?v=")]
    public async Task PublicPages_UseGeneratedPageSharePreviewImages(string path, string expectedImagePath)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains($"property=\"og:image\" content=\"https://longevityworldcup.com{expectedImagePath}", html);
        Assert.Contains($"name=\"twitter:image\" content=\"https://longevityworldcup.com{expectedImagePath}", html);
    }

    [Fact]
    public async Task Leaderboard_StillUsesLeagueSharePreviewImage()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/leaderboard");

        Assert.Contains("property=\"og:image\" content=\"https://longevityworldcup.com/og/league/ultimate.png?v=", html);
        Assert.Contains("name=\"twitter:image\" content=\"https://longevityworldcup.com/og/league/ultimate.png?v=", html);
    }

    [Fact]
    public async Task AthleteProfile_StillUsesAthleteSharePreviewImage()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/athlete/ron-lugbill");

        Assert.Contains("property=\"og:image\" content=\"https://longevityworldcup.com/og/athlete/ron-lugbill.png?v=", html);
        Assert.Contains("name=\"twitter:image\" content=\"https://longevityworldcup.com/og/athlete/ron-lugbill.png?v=", html);
    }

    [Fact]
    public async Task GeneratedPageSharePreviewEndpoint_ReturnsPng()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/og/page/home.png");

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
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
