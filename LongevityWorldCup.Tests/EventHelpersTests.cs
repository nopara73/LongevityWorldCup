using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EventHelpersTests
{
    [Theory]
    [InlineData("slug[alice] clock[pheno] from[44.2] to[41.8]", "pheno", 44.2, 41.8)]
    [InlineData("slug[alice] clock[Pheno Age] from[44.2] to[41.8]", "pheno", 44.2, 41.8)]
    [InlineData("slug[alice] clock[bortz age] from[50.5] to[49.1]", "bortz", 50.5, 49.1)]
    public void TryExtractBiologicalAgeImprovement_NormalizesSupportedClockPayloads(
        string raw,
        string expectedClock,
        double expectedFromAge,
        double expectedToAge)
    {
        var parsed = EventHelpers.TryExtractBiologicalAgeImprovement(raw, out var clock, out var fromAge, out var toAge);

        Assert.True(parsed);
        Assert.Equal(expectedClock, clock);
        Assert.Equal(expectedFromAge, fromAge);
        Assert.Equal(expectedToAge, toAge);
    }

    [Theory]
    [InlineData("clock[pheno] from[41.8] to[44.2]")]
    [InlineData("clock[unknown] from[44.2] to[41.8]")]
    [InlineData("clock[pheno] from[NaN] to[41.8]")]
    [InlineData("clock[pheno] from[44.2] to[Infinity]")]
    public void TryExtractBiologicalAgeImprovement_RejectsInvalidPayloads(string raw)
    {
        Assert.False(EventHelpers.TryExtractBiologicalAgeImprovement(raw, out _, out _, out _));
    }

    [Theory]
    [InlineData("place[3] prevPlace[8] crowdAge[35.25] crowdCount[123]", 3, 8, 35.25, 123)]
    [InlineData("place[7] crowdAge[35.25] crowdCount[123]", 7, null, 35.25, 123)]
    public void TryExtractCrowdAgeTop10Change_ParsesValidTop10Payloads(
        string raw,
        int expectedPlace,
        int? expectedPreviousPlace,
        double expectedCrowdAge,
        int expectedCrowdCount)
    {
        var parsed = EventHelpers.TryExtractCrowdAgeTop10Change(
            raw,
            out var place,
            out var previousPlace,
            out var crowdAge,
            out var crowdCount);

        Assert.True(parsed);
        Assert.Equal(expectedPlace, place);
        Assert.Equal(expectedPreviousPlace, previousPlace);
        Assert.Equal(expectedCrowdAge, crowdAge);
        Assert.Equal(expectedCrowdCount, crowdCount);
    }

    [Theory]
    [InlineData("place[11] crowdAge[35.25] crowdCount[123]")]
    [InlineData("place[3] prevPlace[11] crowdAge[35.25] crowdCount[123]")]
    [InlineData("place[3] crowdAge[NaN] crowdCount[123]")]
    [InlineData("place[3] crowdAge[35.25] crowdCount[0]")]
    public void TryExtractCrowdAgeTop10Change_RejectsInvalidTop10Payloads(string raw)
    {
        Assert.False(EventHelpers.TryExtractCrowdAgeTop10Change(raw, out _, out _, out _, out _));
    }

    [Fact]
    public void TryExtractAgeImprovementTop10Change_ParsesValidPayload()
    {
        var parsed = EventHelpers.TryExtractAgeImprovementTop10Change(
            "clock[bortz age] place[4] prevPlace[9] improvement[-6.75] ageReduction[-20.4]",
            out var clock,
            out var place,
            out var previousPlace,
            out var improvement,
            out var ageReduction);

        Assert.True(parsed);
        Assert.Equal("bortz", clock);
        Assert.Equal(4, place);
        Assert.Equal(9, previousPlace);
        Assert.Equal(-6.75, improvement);
        Assert.Equal(-20.4, ageReduction);
    }

    [Theory]
    [InlineData("clock[pheno] place[0] improvement[-6.75] ageReduction[-20.4]")]
    [InlineData("clock[pheno] place[4] prevPlace[0] improvement[-6.75] ageReduction[-20.4]")]
    [InlineData("clock[clock] place[4] improvement[-6.75] ageReduction[-20.4]")]
    [InlineData("clock[pheno] place[4] improvement[Infinity] ageReduction[-20.4]")]
    [InlineData("clock[pheno] place[4] improvement[-6.75] ageReduction[NaN]")]
    public void TryExtractAgeImprovementTop10Change_RejectsInvalidPayloads(string raw)
    {
        Assert.False(EventHelpers.TryExtractAgeImprovementTop10Change(raw, out _, out _, out _, out _, out _));
    }

    [Theory]
    [InlineData("slug[alice] solo[] prevs[bob, carol]", true, new[] { "bob", "carol" })]
    [InlineData("slug[alice] solo[1] prevs[bob]", true, new[] { "bob" })]
    [InlineData("slug[alice] solo[false] prevs[]", false, new string[0])]
    public void FieldExtractors_ParseFlagsAndCsvLists(string raw, bool expectedSolo, string[] expectedPrevs)
    {
        Assert.True(EventHelpers.TryExtractSolo(raw, out var solo));
        Assert.Equal(expectedSolo, solo);

        var hasPrevs = EventHelpers.TryExtractPrevs(raw, out var prevs);

        Assert.Equal(expectedPrevs.Length > 0, hasPrevs);
        Assert.Equal(expectedPrevs, prevs);
    }

    [Theory]
    [InlineData("First line\nSecond line", "First line")]
    [InlineData("First line\r\nSecond line", "First line")]
    [InlineData("  First line  ", "First line")]
    public void TryExtractCustomEventTitle_ReturnsTrimmedFirstLine(string raw, string expectedTitle)
    {
        Assert.True(EventHelpers.TryExtractCustomEventTitle(raw, out var title));
        Assert.Equal(expectedTitle, title);
    }
}
