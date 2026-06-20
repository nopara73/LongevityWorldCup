using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CustomEventMarkupTests
{
    [Fact]
    public void SplitTitleAndContent_NormalizesNewlinesAndTrimsOnlyTitleEnd()
    {
        var (title, content) = CustomEventMarkup.SplitTitleAndContent("Title  \r\n\r\n Body line  \r\nNext ");

        Assert.Equal("Title", title);
        Assert.Equal(" Body line  \nNext", content);
    }

    [Fact]
    public void HyperlinkDetection_RecognizesSafeLinksInsideStyledMarkup()
    {
        const string text = "Read [bold]([guide](https://example.test/path(a))) now";

        Assert.True(CustomEventMarkup.ContainsHyperlink(text));
        Assert.Equal("https://example.test/path(a)", CustomEventMarkup.GetSingleHyperlink(text));
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](mailto:hello@example.test)")]
    [InlineData("[x](https://example.test")]
    [InlineData("[bold]([x](javascript:alert(1)))")]
    public void HyperlinkDetection_RejectsUnsafeOrMalformedLinks(string text)
    {
        Assert.False(CustomEventMarkup.ContainsHyperlink(text));
        Assert.Null(CustomEventMarkup.GetSingleHyperlink(text));
    }

    [Fact]
    public void GetSingleHyperlink_ReturnsNullWhenMultipleSafeLinksExist()
    {
        const string text = "[one](https://one.example.test) and [two](https://two.example.test)";

        Assert.True(CustomEventMarkup.ContainsHyperlink(text));
        Assert.Null(CustomEventMarkup.GetSingleHyperlink(text));
    }

    [Fact]
    public void ToPlainText_ResolvesMentionsAndCanDropHyperlinkLabels()
    {
        static string Resolve(string slug) => slug == "siim_land" ? "Siim Land" : "";

        var plain = CustomEventMarkup.ToPlainText(
            "Welcome [mention](siim_land). Read [guide](https://example.test). Meet [mention](unknown_slug).",
            keepHyperlinkLabels: false,
            Resolve);

        Assert.Equal("Welcome Siim Land. Read . Meet Unknown Slug.", plain);
    }

    [Fact]
    public void ParseSegments_PreservesStylesAndMergesAdjacentRuns()
    {
        var segments = CustomEventMarkup.ParseSegments(
            "A [bold](B [link](https://example.test)) [strong](C) D",
            keepHyperlinkLabels: true);

        Assert.Equal(
            [
                new CustomEventSegment("A ", CustomEventTextStyle.Regular),
                new CustomEventSegment("B link", CustomEventTextStyle.Bold),
                new CustomEventSegment(" ", CustomEventTextStyle.Regular),
                new CustomEventSegment("C", CustomEventTextStyle.Strong),
                new CustomEventSegment(" D", CustomEventTextStyle.Regular)
            ],
            segments);
    }
}
