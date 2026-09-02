using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SharePreviewMetadataTests(TestWebApplicationFactory sharedFactory) : IClassFixture<TestWebApplicationFactory>
{
    [Theory]
    [InlineData("/", "/og/page/home.png?v=")]
    [InlineData("/events", "/og/page/events.png?v=")]
    [InlineData("/media", "/og/page/media.png?v=")]
    [InlineData("/about", "/og/page/about.png?v=")]
    [InlineData("/history", "/og/page/history.png?v=")]
    [InlineData("/ruleset", "/og/page/ruleset.png?v=")]
    [InlineData("/longevitymaxxing", "/og/page/longevitymaxxing.png?v=")]
    [InlineData("/league/bortz", "/og/page/view-bortz.png?v=")]
    [InlineData("/league/pheno", "/og/page/view-pheno.png?v=")]
    [InlineData("/league/improvement", "/og/page/view-improvement.png?v=")]
    [InlineData("/league/bortz-improvement", "/og/page/view-bortz-improvement.png?v=")]
    [InlineData("/league/crowd", "/og/page/view-crowd.png?v=")]
    public async Task PublicPages_UseGeneratedPageSharePreviewImages(string path, string expectedImagePath)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains($"property=\"og:image\" content=\"https://longevityworldcup.com{expectedImagePath}", html);
        Assert.Contains($"name=\"twitter:image\" content=\"https://longevityworldcup.com{expectedImagePath}", html);
    }

    [Fact]
    public async Task Leaderboard_StillUsesLeagueSharePreviewImage()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/leaderboard");

        Assert.Contains("property=\"og:image\" content=\"https://longevityworldcup.com/og/league/ultimate.png?v=", html);
        Assert.Contains("name=\"twitter:image\" content=\"https://longevityworldcup.com/og/league/ultimate.png?v=", html);
    }

    [Fact]
    public async Task AthleteProfile_StillUsesAthleteSharePreviewImage()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/athlete/ron-lugbill");

        Assert.Contains("property=\"og:image\" content=\"https://longevityworldcup.com/og/athlete/ron-lugbill.png?v=", html);
        Assert.Contains("name=\"twitter:image\" content=\"https://longevityworldcup.com/og/athlete/ron-lugbill.png?v=", html);
    }

    [Fact]
    public async Task AthleteProfile_CrowdContextUsesCrowdAgeSharePreviewMetadata()
    {
        using var factory = new TestWebApplicationFactory();
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var database = factory.Services.GetRequiredService<DatabaseManager>();
        Assert.True(athletes.TryGetProfileImageId("ron_lugbill", out var profileImageId));
        CrowdAgeTestData.SeedAcceptedGuesses(
            database,
            athletes,
            "ron_lugbill",
            profileImageId,
            age: 68,
            count: 100);

        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/athlete/ron-lugbill?ctx=crowd");

        Assert.Contains("Crowd Age 68 years from 100 guesses.", html);
        Assert.Contains("property=\"og:image\" content=\"https://longevityworldcup.com/og/athlete/ron-lugbill.png?v=", html);
        Assert.Contains("&amp;ctx=crowd", html);
    }

    [Theory]
    [InlineData("pheno", "Pheno Age leaderboard")]
    [InlineData("bortz", "Bortz Age leaderboard")]
    public async Task AthleteProfile_BiologicalAgeContextUsesClockSpecificSharePreviewMetadata(
        string context,
        string expectedLeagueName)
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
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync($"/athlete/{payload!.RouteSlug}?ctx={context}");

        Assert.Contains(expectedLeagueName, html);
        Assert.Contains($"{expectedLeagueName} rank #", html);
        Assert.Contains($"&amp;ctx={context}", html);
    }

    [Theory]
    [InlineData("pheno-baseline-improvement", "Pheno Age best improvement")]
    [InlineData("bortz-baseline-improvement", "Bortz Age best improvement")]
    public async Task AthleteProfile_BaselineImprovementContextUsesBadgeSpecificSharePreviewMetadata(
        string context,
        string expectedTitle)
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
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync($"/athlete/{payload!.RouteSlug}?ctx={context}");

        Assert.Contains(expectedTitle, html);
        Assert.Contains("from first to latest eligible result", html);
        Assert.DoesNotContain("from worst to latest eligible result", html);
        Assert.Contains($"&amp;ctx={context}", html);
    }

    [Fact]
    public async Task GeneratedPageSharePreviewEndpoint_ReturnsPng()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/og/page/home.png");

        response.EnsureSuccessStatusCode();
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/athlete/ron-lugbill", "Ron Lugbill | Longevity World Cup")]
    [InlineData("/league/gen-alpha", "Gen Alpha League | Longevity World Cup")]
    [InlineData("/league/prosperan", "Prosperan League | Longevity World Cup")]
    [InlineData("/league/improvement", "Pheno Improvement Leaderboard | Longevity World Cup")]
    [InlineData("/flag/hungary", "Leaderboard: Hungary | Longevity World Cup")]
    [InlineData("/about", "About | Longevity World Cup")]
    public async Task SharePreviewTitles_AddBrandSuffixWhenMissing(string path, string expectedTitle)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains($"property=\"og:title\" content=\"{expectedTitle}\"", html);
        Assert.Contains($"name=\"twitter:title\" content=\"{expectedTitle}\"", html);
    }

    [Fact]
    public async Task FlagRoute_UsesCanonicalFlagMetadata()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/flag/hungary");

        Assert.Contains("rel=\"canonical\" href=\"https://longevityworldcup.com/flag/hungary\"", html);
        Assert.Contains("Current Longevity World Cup athletes representing Hungary.", html);
        Assert.Contains("property=\"og:title\" content=\"Leaderboard: Hungary | Longevity World Cup\"", html);
    }

    [Fact]
    public async Task FlagRouteAlias_UsesCanonicalFlagMetadata()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/flag/magyarorszag");

        Assert.Contains("rel=\"canonical\" href=\"https://longevityworldcup.com/flag/hungary\"", html);
        Assert.Contains("Current Longevity World Cup athletes representing Hungary.", html);
        Assert.Contains("property=\"og:title\" content=\"Leaderboard: Hungary | Longevity World Cup\"", html);
    }

    [Fact]
    public async Task CustomFlagRoute_UsesNeutralRepresentationMetadata()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/flag/live-long-enough-to-live-forever");

        Assert.Contains("Current Longevity World Cup athletes representing Live long enough to live forever.", html);
        Assert.Contains("property=\"og:title\" content=\"Leaderboard: Live long enough to live forever | Longevity World Cup\"", html);
        Assert.DoesNotContain("athletes from Live long enough", html);
    }

    [Fact]
    public async Task SharePreviewTitles_DoNotDuplicateBrandSuffix()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("property=\"og:title\" content=\"Longevity World Cup\"", html);
        Assert.DoesNotContain("Longevity World Cup | Longevity World Cup", html);
    }

}
