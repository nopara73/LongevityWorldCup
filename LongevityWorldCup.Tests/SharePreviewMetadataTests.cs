using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Mvc.Testing;
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

    [Theory]
    [InlineData("/athlete/ron-lugbill", "Ron Lugbill | Longevity World Cup")]
    [InlineData("/league/gen-alpha", "Gen Alpha League | Longevity World Cup")]
    [InlineData("/league/prosperan", "Prosperan League | Longevity World Cup")]
    [InlineData("/about", "About | Longevity World Cup")]
    public async Task SharePreviewTitles_AddBrandSuffixWhenMissing(string path, string expectedTitle)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains($"property=\"og:title\" content=\"{expectedTitle}\"", html);
        Assert.Contains($"name=\"twitter:title\" content=\"{expectedTitle}\"", html);
    }

    [Fact]
    public async Task SharePreviewTitles_DoNotDuplicateBrandSuffix()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("property=\"og:title\" content=\"Longevity World Cup\"", html);
        Assert.DoesNotContain("Longevity World Cup | Longevity World Cup", html);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new TestWebApplicationFactory();
    }
}
