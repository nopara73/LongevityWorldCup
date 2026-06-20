using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PageOgImageServiceTests
{
    [Fact]
    public void TryGetCurrentPayload_NormalizesSlugAndBuildsVersionedUrl()
    {
        using var factory = CreateFactory();
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

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    [InlineData("view-unknown")]
    public void TryGetCurrentPayload_RejectsUnknownPageSlug(string rawSlug)
    {
        using var factory = CreateFactory();
        var pages = factory.Services.GetRequiredService<PageOgImageService>();

        var found = pages.TryGetCurrentPayload(rawSlug, out var payload);

        Assert.False(found);
        Assert.Null(payload);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new TestWebApplicationFactory();
    }
}
