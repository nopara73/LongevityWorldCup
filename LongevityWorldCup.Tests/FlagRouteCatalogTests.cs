using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class FlagRouteCatalogTests
{
    [Theory]
    [InlineData("Hungary", "hungary", "/flag/hungary")]
    [InlineData("New Zealand", "new-zealand", "/flag/new-zealand")]
    [InlineData("U.S. Virgin Islands", "u-s-virgin-islands", "/flag/u-s-virgin-islands")]
    public void TryCreate_BuildsStableFlagRoute(string flag, string expectedSlug, string expectedPath)
    {
        var created = FlagRouteCatalog.TryCreate(flag, out var route);

        Assert.True(created);
        Assert.Equal(flag, route.Name);
        Assert.Equal(expectedSlug, route.Slug);
        Assert.Equal(expectedPath, route.Path);
    }

    [Fact]
    public void TryResolve_MatchesRouteSlugAgainstAvailableFlags()
    {
        var resolved = FlagRouteCatalog.TryResolve("new-zealand", ["Hungary", "New Zealand"], out var route);

        Assert.True(resolved);
        Assert.Equal("New Zealand", route.Name);
        Assert.Equal("/flag/new-zealand", route.Path);
    }
}
