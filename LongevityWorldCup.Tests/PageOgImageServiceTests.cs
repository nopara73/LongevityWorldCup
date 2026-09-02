using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PageOgImageServiceTests(TestWebApplicationFactory sharedFactory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public void TryGetCurrentPayload_NormalizesSlugAndBuildsVersionedUrl()
    {
        var factory = sharedFactory;
        var pages = factory.Services.GetRequiredService<PageOgImageService>();

        var found = pages.TryGetCurrentPayload(" VIEW-CROWD ", out var payload);
        var url = pages.BuildVersionedImageUrl("https://longevityworldcup.com", payload);

        Assert.True(found);
        Assert.Equal("view-crowd", payload.Slug);
        Assert.Equal("Community view", payload.Kicker);
        Assert.Equal("Crowd Age leaderboard", payload.Title);
        Assert.Contains("Crowd Age", payload.Stats);
        Assert.Matches("^[0-9a-f]{12}$", payload.Signature);
        Assert.Equal($"https://longevityworldcup.com/og/page/view-crowd.png?v={payload.Signature}", url);
    }

    [Fact]
    public void TryGetCurrentPayload_IncludesLongevitymaxxingPage()
    {
        var factory = sharedFactory;
        var pages = factory.Services.GetRequiredService<PageOgImageService>();

        var found = pages.TryGetCurrentPayload("longevitymaxxing", out var payload);
        var url = pages.BuildVersionedImageUrl("https://longevityworldcup.com", payload);

        Assert.True(found);
        Assert.Equal("longevitymaxxing", payload.Slug);
        Assert.Equal("Longevitymaxxing Challenge", payload.Kicker);
        Assert.Equal("The first muscle to train is your mind", payload.Title);
        Assert.Equal("Start longevitymaxxing today", payload.Description);
        Assert.Contains("Join anytime", payload.Stats);
        Assert.Matches("^[0-9a-f]{12}$", payload.Signature);
        Assert.Equal($"https://longevityworldcup.com/og/page/longevitymaxxing.png?v={payload.Signature}", url);
    }

    [Fact]
    public void TryGetCurrentPayload_NamesPhenoImprovementExplicitly()
    {
        var factory = sharedFactory;
        var pages = factory.Services.GetRequiredService<PageOgImageService>();

        var found = pages.TryGetCurrentPayload("view-improvement", out var payload);

        Assert.True(found);
        Assert.Equal("Pheno Improvement leaderboard", payload.Title);
        Assert.Contains("Pheno Improvement", payload.Stats);
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    [InlineData("view-unknown")]
    public void TryGetCurrentPayload_RejectsUnknownPageSlug(string rawSlug)
    {
        var factory = sharedFactory;
        var pages = factory.Services.GetRequiredService<PageOgImageService>();

        var found = pages.TryGetCurrentPayload(rawSlug, out var payload);

        Assert.False(found);
        Assert.Null(payload);
    }

}
