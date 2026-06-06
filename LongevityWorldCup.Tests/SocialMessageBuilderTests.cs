using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialMessageBuilderTests
{
    [Fact]
    public void RankEventBuilders_ReturnGoldenMessages()
    {
        const string raw = "slug[siim_land] rank[1] prev[bryan_johnson]";
        var expectedX =
            "New #1 in the Ultimate League \U0001F3C6\n" +
            "Siim Land is now ahead of Bryan Johnson.\n\n" +
            "https://longevityworldcup.com/leaderboard";
        var expectedThreads =
            "Siim Land just climbed to #1 in the Ultimate League \U0001F3C6\n" +
            "Now ahead of Bryan Johnson.\n\n" +
            "https://longevityworldcup.com/leaderboard";
        var expectedSlack =
            "<https://longevityworldcup.com/athlete/siim-land|Siim Land> is now 1st \U0001F947 in Ultimate League, ahead of <https://longevityworldcup.com/athlete/bryan-johnson|Bryan Johnson>";

        Assert.Equal(expectedX, XMessageBuilder.ForEventText(EventType.NewRank, raw, SlugToName));
        Assert.Equal(expectedThreads, ThreadsMessageBuilder.ForEventText(EventType.NewRank, raw, SlugToName));
        Assert.Equal(expectedSlack, SlackMessageBuilder.ForEventText(EventType.NewRank, raw, SlugToName));
    }

    [Fact]
    public void CustomEventBuilders_ReturnGoldenMessages()
    {
        const string raw = "Community update\n\nSiim [bold](wins), [strong](majorly). Welcome [mention](siim_land).";
        const string expectedPlatform = "Community update\n\nSiim wins, majorly. Welcome Siim Land.";
        const string expectedSlack =
            "Community update\n\n" +
            "Siim *wins*, *_majorly_*. Welcome <https://longevityworldcup.com/athlete/siim-land|Siim Land>.";

        Assert.Equal(
            expectedPlatform,
            CustomEventSocialComposer.BuildPlan("event123", raw, 280, SlugToName).PostText);
        Assert.Equal(
            expectedPlatform,
            CustomEventSocialComposer.BuildPlan("event123", raw, 500, SlugToName).PostText);
        Assert.Equal(
            expectedPlatform,
            CustomEventSocialComposer.BuildPlan("event123", raw, 63206, SlugToName).PostText);
        Assert.Equal(
            expectedSlack,
            SlackMessageBuilder.ForEventText(EventType.CustomEvent, raw, SlugToName));
    }

    [Fact]
    public void MilestoneEventBuilders_ReturnGoldenMessages()
    {
        const string raw = "athletes[100]";
        var expectedX =
            "100 athletes are now on the leaderboard, marking the first triple digit milestone.\n\n" +
            "https://longevityworldcup.com/leaderboard";
        var expectedThreads =
            "100 athletes are now on the leaderboard. First triple-digit mark.\n\n" +
            "https://longevityworldcup.com/leaderboard";
        const string expectedSlack =
            "Hit <https://longevityworldcup.com/leaderboard|100> on the leaderboard, triple digits \U0001F3C1";

        Assert.Equal(expectedX, XMessageBuilder.ForEventText(EventType.AthleteCountMilestone, raw, SlugToName));
        Assert.Equal(expectedThreads, ThreadsMessageBuilder.ForEventText(EventType.AthleteCountMilestone, raw, SlugToName));
        Assert.Equal(expectedSlack, SlackMessageBuilder.ForEventText(EventType.AthleteCountMilestone, raw, SlugToName));
    }

    [Fact]
    public void DomainTopFiller_UsesClockSpecificBortzProfileLanguage()
    {
        var xMessage = XMessageBuilder.ForFiller(
            FillerType.DomainTop,
            "domain[immune]",
            SlugToName,
            sampleForBasis: MatureSample,
            getBestDomainWinnerSlug: _ => "siim_land");

        var threadsMessage = ThreadsMessageBuilder.ForFiller(
            FillerType.DomainTop,
            "domain[immune]",
            SlugToName,
            sampleForBasis: MatureSample,
            getBestDomainWinnerSlug: _ => "siim_land");

        Assert.StartsWith("Siim Land currently has the strongest Bortz immune profile.", xMessage);
        Assert.StartsWith("Siim Land currently has the strongest Bortz immune profile.", threadsMessage);
        Assert.DoesNotContain("strongest immune profile in the Longevity World Cup field", xMessage);
        Assert.DoesNotContain("strongest immune profile in the field right now", threadsMessage);
    }

    [Fact]
    public void DomainTopFiller_DoesNotCallInflammationABortzProfile()
    {
        var xMessage = XMessageBuilder.ForFiller(
            FillerType.DomainTop,
            "domain[inflammation]",
            SlugToName,
            sampleForBasis: MatureSample,
            getBestDomainWinnerSlug: _ => "siim_land");

        var threadsMessage = ThreadsMessageBuilder.ForFiller(
            FillerType.DomainTop,
            "domain[inflammation]",
            SlugToName,
            sampleForBasis: MatureSample,
            getBestDomainWinnerSlug: _ => "siim_land");

        Assert.StartsWith("Siim Land currently has the strongest inflammation profile in the Longevity World Cup field.", xMessage);
        Assert.StartsWith("Siim Land currently has the strongest inflammation profile in the Longevity World Cup field.", threadsMessage);
        Assert.DoesNotContain("Bortz inflammation", xMessage);
        Assert.DoesNotContain("Bortz inflammation", threadsMessage);
    }

    private static string SlugToName(string slug)
        => slug switch
        {
            "siim_land" => "Siim Land",
            "bryan_johnson" => "Bryan Johnson",
            _ => slug.Replace('_', ' ')
        };

    private static XPostSampleSize MatureSample(XPostSampleBasis basis)
        => new(basis, N: 21, PhenoCount: 21, BortzCount: 21, CombinedCount: 21);
}
