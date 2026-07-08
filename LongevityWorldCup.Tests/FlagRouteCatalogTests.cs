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

    [Theory]
    [InlineData("Magyarország", "Hungary", "hungary", "/flag/hungary")]
    [InlineData("magyarorszag", "Hungary", "hungary", "/flag/hungary")]
    [InlineData("Brasil", "Brazil", "brazil", "/flag/brazil")]
    [InlineData("USA", "United States", "united-states", "/flag/united-states")]
    public void TryCreate_CanonicalizesKnownFlagAliases(string flag, string expectedName, string expectedSlug, string expectedPath)
    {
        var created = FlagRouteCatalog.TryCreate(flag, out var route);

        Assert.True(created);
        Assert.Equal(expectedName, route.Name);
        Assert.Equal(expectedSlug, route.Slug);
        Assert.Equal(expectedPath, route.Path);
    }

    [Fact]
    public void BuildRoutes_DeduplicatesCanonicalFlagAliases()
    {
        var routes = FlagRouteCatalog.BuildRoutes(["Hungary", "Magyarország", "magyarorszag"]);

        var route = Assert.Single(routes);
        Assert.Equal("Hungary", route.Name);
        Assert.Equal("hungary", route.Slug);
        Assert.Equal("/flag/hungary", route.Path);
    }

    [Fact]
    public void TryResolve_MatchesRouteSlugAgainstAvailableFlags()
    {
        var resolved = FlagRouteCatalog.TryResolve("new-zealand", ["Hungary", "New Zealand"], out var route);

        Assert.True(resolved);
        Assert.Equal("New Zealand", route.Name);
        Assert.Equal("/flag/new-zealand", route.Path);
    }

    [Theory]
    [InlineData("magyarorszag")]
    [InlineData("Magyarország")]
    [InlineData("Magyarorsz%C3%A1g")]
    public void TryResolve_MatchesAliasSlugAgainstCanonicalFlag(string slug)
    {
        var resolved = FlagRouteCatalog.TryResolve(slug, ["Hungary", "New Zealand"], out var route);

        Assert.True(resolved);
        Assert.Equal("Hungary", route.Name);
        Assert.Equal("/flag/hungary", route.Path);
    }
}
