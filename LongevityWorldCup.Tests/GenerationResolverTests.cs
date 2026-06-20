using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class GenerationResolverTests
{
    [Theory]
    [InlineData(1927, null)]
    [InlineData(1928, "Silent Generation")]
    [InlineData(1945, "Silent Generation")]
    [InlineData(1946, "Baby Boomers")]
    [InlineData(1964, "Baby Boomers")]
    [InlineData(1965, "Gen X")]
    [InlineData(1980, "Gen X")]
    [InlineData(1981, "Millennials")]
    [InlineData(1996, "Millennials")]
    [InlineData(1997, "Gen Z")]
    [InlineData(2012, "Gen Z")]
    [InlineData(2013, "Gen Alpha")]
    public void Resolve_MapsBirthYearToGenerationBoundaries(int birthYear, string? expectedGeneration)
    {
        Assert.Equal(expectedGeneration, GenerationResolver.Resolve(generation: null, birthYear));
    }

    [Fact]
    public void Resolve_PrefersExplicitGeneration()
    {
        Assert.Equal("Custom Generation", GenerationResolver.Resolve("Custom Generation", birthYear: 1990));
    }

    [Fact]
    public void Resolve_ReturnsNullWhenGenerationAndBirthYearAreMissing()
    {
        Assert.Null(GenerationResolver.Resolve(generation: null, birthYear: null));
    }

    [Fact]
    public void ResolveFromAthleteJson_UsesExplicitGeneration()
    {
        var athlete = new JsonObject
        {
            ["Generation"] = "Legacy League",
            ["DateOfBirth"] = new JsonObject { ["Year"] = 1990 }
        };

        Assert.Equal("Legacy League", GenerationResolver.ResolveFromAthleteJson(athlete));
    }

    [Fact]
    public void ResolveFromAthleteJson_UsesBirthYearWhenGenerationIsMissing()
    {
        var athlete = new JsonObject
        {
            ["DateOfBirth"] = new JsonObject { ["Year"] = 1990 }
        };

        Assert.Equal("Millennials", GenerationResolver.ResolveFromAthleteJson(athlete));
    }

    [Theory]
    [MemberData(nameof(AthletesWithoutResolvableGeneration))]
    public void ResolveFromAthleteJson_ReturnsNullWhenBirthYearIsUnavailable(JsonObject athlete)
    {
        Assert.Null(GenerationResolver.ResolveFromAthleteJson(athlete));
    }

    public static IEnumerable<object[]> AthletesWithoutResolvableGeneration()
    {
        yield return [new JsonObject()];
        yield return [new JsonObject { ["DateOfBirth"] = new JsonObject() }];
        yield return [new JsonObject { ["DateOfBirth"] = new JsonObject { ["Year"] = "1990" } }];
    }
}
