using LongevityWorldCup.Website.Middleware;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class RouteCanonicalizationTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("leaderboard/", "/leaderboard")]
    [InlineData("/about?ref=footer", "/about")]
    [InlineData("rules#details", "/rules")]
    [InlineData("/longevitymaxxing/", "/longevitymaxxing")]
    public void NormalizePath_StripsQueryFragmentsAndTrailingSlashes(string? rawPath, string expected)
    {
        var normalized = RouteCanonicalization.NormalizePath(rawPath);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("/RULES/?ref=docs", "/ruleset")]
    [InlineData("misc-pages/media.html", "/media")]
    [InlineData("/onboarding/BORTZ-AGE.html#form", "/bortz-age")]
    [InlineData("/Some/New/Page/", "/some/new/page")]
    public void GetCanonicalPath_UsesAliasesAndLowercasesUnknownPaths(string rawPath, string expected)
    {
        var canonical = RouteCanonicalization.GetCanonicalPath(rawPath);

        Assert.Equal(expected, canonical);
    }
}
