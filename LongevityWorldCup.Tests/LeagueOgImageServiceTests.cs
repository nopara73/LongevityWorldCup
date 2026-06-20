using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LeagueOgImageServiceTests
{
    [Theory]
    [InlineData("Ultimate", "ultimate")]
    [InlineData("Ultimate League", "ultimate")]
    [InlineData("women's", "womens")]
    [InlineData("womens league", "womens")]
    [InlineData("silent generation", "silent-generation")]
    [InlineData("baby boomers league", "baby-boomers")]
    [InlineData("gen x", "gen-x")]
    [InlineData("gen-z", "gen-z")]
    [InlineData("Gen Alpha League", "gen-alpha")]
    public void TryNormalizeLeagueSlug_AcceptsRouteAndDisplayAliases(string raw, string expectedSlug)
    {
        var normalized = LeagueOgImageService.TryNormalizeLeagueSlug(raw, out var slug);

        Assert.True(normalized);
        Assert.Equal(expectedSlug, slug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("crowd")]
    [InlineData("pheno")]
    public void TryNormalizeLeagueSlug_RejectsUnsupportedShareCardLeagues(string? raw)
    {
        var normalized = LeagueOgImageService.TryNormalizeLeagueSlug(raw, out var slug);

        Assert.False(normalized);
        Assert.Equal("", slug);
    }

    [Theory]
    [InlineData("mens", "Men's League")]
    [InlineData("baby boomers", "Baby Boomers League")]
    [InlineData("prosperan", "Prosperan League")]
    public void TryGetLeagueDisplayName_ReturnsCanonicalDisplayName(string raw, string expectedDisplayName)
    {
        var found = LeagueOgImageService.TryGetLeagueDisplayName(raw, out var displayName);

        Assert.True(found);
        Assert.Equal(expectedDisplayName, displayName);
    }
}
