using System.Net;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
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
    public void BecameProEventBuilders_ReturnGoldenMessages()
    {
        const string raw = "slug[siim_land]";
        var expectedX =
            "Siim Land went Pro.\n\n" +
            "Bortz Age results now place them in the Pro track.\n\n" +
            "https://longevityworldcup.com/athlete/siim-land";
        const string expectedSlack =
            "<https://longevityworldcup.com/athlete/siim-land|Siim Land> went Pro";

        Assert.Equal(expectedX, XMessageBuilder.ForEventText(EventType.BecamePro, raw, SlugToName));
        Assert.Equal(expectedX, ThreadsMessageBuilder.ForEventText(EventType.BecamePro, raw, SlugToName));
        Assert.Equal(expectedSlack, SlackMessageBuilder.ForEventText(EventType.BecamePro, raw, SlugToName));
    }

    [Fact]
    public void MergedSlackRankAndBecameProEvent_ReturnsWentProMessage()
    {
        var items = new[]
        {
            (EventType.NewRank, "slug[siim_land] rank[2] prev[bryan_johnson]"),
            (EventType.BecamePro, "slug[siim_land]")
        };
        const string expected =
            "In the Ultimate League, <https://longevityworldcup.com/athlete/siim-land|Siim Land> is now #2 🥈, ahead of <https://longevityworldcup.com/athlete/bryan-johnson|Bryan Johnson>, and went Pro";

        Assert.Equal(expected, SlackMessageBuilder.ForMergedGroup(items, SlugToName));
    }

    [Theory]
    [InlineData("pheno", "pheno age")]
    [InlineData("bortz", "Bortz Age")]
    public void BiologicalAgeImprovementEventBuilders_ReturnGoldenMessages(string clock, string expectedClockLabel)
    {
        var raw = $"slug[siim_land] clock[{clock}] from[44.21] to[41.8]";
        var expectedX =
            $"Siim Land improved their {expectedClockLabel} from 44.21 to 41.8 years.\n\n" +
            "https://longevityworldcup.com/athlete/siim-land";
        var expectedSlack =
            $"<https://longevityworldcup.com/athlete/siim-land|Siim Land> improved their {expectedClockLabel} from 44.21 to 41.8 years";

        Assert.Equal(expectedX, XMessageBuilder.ForEventText(EventType.BiologicalAgeImproved, raw, SlugToName));
        Assert.Equal(expectedX, ThreadsMessageBuilder.ForEventText(EventType.BiologicalAgeImproved, raw, SlugToName));
        Assert.Equal(expectedSlack, SlackMessageBuilder.ForEventText(EventType.BiologicalAgeImproved, raw, SlugToName));
    }

    [Fact]
    public void CrowdAgeTop10ChangeEventBuilders_ReturnGoldenMessages()
    {
        const string raw = "slug[siim_land] place[3] prevPlace[8] prev[bryan_johnson] crowdAge[35.25] crowdCount[123]";
        var expectedX =
            "Siim Land climbed from 8th to 3rd in Crowd Age with 123 guesses.\n" +
            "Siim Land's Crowd Age is 35.3, 24.8 years below chronological age.\n\n" +
            "https://longevityworldcup.com/athlete/siim-land?ctx=crowd";
        var expectedSlack =
            "<https://longevityworldcup.com/athlete/siim-land|Siim Land> climbed from 8th to 3rd in Crowd Age with 123 guesses. <https://longevityworldcup.com/athlete/siim-land|Siim Land>'s Crowd Age is 35.3, 24.8 years below chronological age.";

        double? ChronoAge(string slug) => string.Equals(slug, "siim_land", StringComparison.OrdinalIgnoreCase) ? 60.0 : null;

        Assert.Equal(expectedX, XMessageBuilder.ForEventText(EventType.CrowdAgeTop10Change, raw, SlugToName, getChronoAgeForSlug: ChronoAge));
        Assert.Equal(expectedX, ThreadsMessageBuilder.ForEventText(EventType.CrowdAgeTop10Change, raw, SlugToName, getChronoAgeForSlug: ChronoAge));
        Assert.Equal(expectedSlack, SlackMessageBuilder.ForEventText(EventType.CrowdAgeTop10Change, raw, SlugToName, getChronoAgeForSlug: ChronoAge));
    }

    [Fact]
    public void CrowdAgeTop10ChangeEventBuilders_PreserveFirstEntryMovement()
    {
        const string raw = "slug[siim_land] place[7] crowdAge[35.25] crowdCount[123]";
        var expectedX =
            "Siim Land just entered the top 10 at 7th in Crowd Age with 123 guesses.\n" +
            "Siim Land's Crowd Age is 35.3, 24.8 years below chronological age.\n\n" +
            "https://longevityworldcup.com/athlete/siim-land?ctx=crowd";

        double? ChronoAge(string slug) => string.Equals(slug, "siim_land", StringComparison.OrdinalIgnoreCase) ? 60.0 : null;

        Assert.Equal(expectedX, XMessageBuilder.ForEventText(EventType.CrowdAgeTop10Change, raw, SlugToName, getChronoAgeForSlug: ChronoAge));
        Assert.Equal(expectedX, ThreadsMessageBuilder.ForEventText(EventType.CrowdAgeTop10Change, raw, SlugToName, getChronoAgeForSlug: ChronoAge));
    }

    [Theory]
    [InlineData("pheno", "Pheno Improvement", "improvement")]
    [InlineData("bortz", "Bortz Improvement", "bortz-improvement")]
    public void AgeImprovementTop10ChangeEventBuilders_ReturnGoldenMessages(string clock, string leaderboardName, string ctx)
    {
        var raw = $"slug[siim_land] clock[{clock}] place[3] prevPlace[8] prev[bryan_johnson] improvement[-6.75] ageReduction[-20.4]";
        var expectedX =
            $"Siim Land climbed from 8th to 3rd in the {leaderboardName} leaderboard, ahead of Bryan Johnson.\n\n" +
            "Improvement: -6.8 years from worst to latest eligible result.\n\n" +
            $"https://longevityworldcup.com/athlete/siim-land?ctx={ctx}";
        var expectedSlack =
            $"<https://longevityworldcup.com/athlete/siim-land|Siim Land> climbed from 8th to 3rd in {leaderboardName}, ahead of <https://longevityworldcup.com/athlete/bryan-johnson|Bryan Johnson> (-6.8 years)";

        Assert.Equal(expectedX, XMessageBuilder.ForEventText(EventType.AgeImprovementTop10Change, raw, SlugToName));
        Assert.Equal(expectedX, ThreadsMessageBuilder.ForEventText(EventType.AgeImprovementTop10Change, raw, SlugToName));
        Assert.Equal(expectedSlack, SlackMessageBuilder.ForEventText(EventType.AgeImprovementTop10Change, raw, SlugToName));
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
    public void FacebookCustomEventBuilder_ReturnsGoldenMessageThroughServicePath()
    {
        var facebookEvents = CreateFacebookEventService();
        facebookEvents.SetAthletesForFacebook([SiimForSocial()]);
        const string raw = "Community update\n\nSiim [bold](wins), [strong](majorly). Welcome [mention](siim_land).";
        const string expected = "Community update\n\nSiim wins, majorly. Welcome Siim Land.";

        Assert.Equal(expected, facebookEvents.TryBuildMessage(EventType.CustomEvent, raw, "event123", visibleOnWebsite: true));
        Assert.Equal(expected, facebookEvents.TryBuildMessage(EventType.CustomEvent, raw, "event123", visibleOnWebsite: false));
        Assert.Null(facebookEvents.TryBuildMessage(EventType.CustomEvent, raw, eventId: null));
        Assert.Null(facebookEvents.TryBuildMessage(EventType.NewRank, "slug[siim_land] rank[1]", "event123"));
    }

    [Fact]
    public void FacebookCustomEventBuilder_KeepsLongTextPostThatThreadsWouldRenderAsImage()
    {
        var facebookEvents = CreateFacebookEventService();
        var body = new string('A', 700);
        var raw = "Long update\n\n" + body;
        var expected = "Long update\n\n" + body;

        Assert.Equal(expected, facebookEvents.TryBuildMessage(EventType.CustomEvent, raw, "event123", visibleOnWebsite: true));
        Assert.Equal(CustomEventPostMode.Image, CustomEventSocialComposer.BuildPlan("event123", raw, 500, SlugToName).Mode);
        Assert.Equal(CustomEventPostMode.Text, CustomEventSocialComposer.BuildPlan("event123", raw, 63206, SlugToName).Mode);
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

    [Theory]
    [InlineData(200, "200 athletes are now on the leaderboard.", "200 athletes are now on the leaderboard.", "The competition reached")]
    [InlineData(222, "222 athletes are now on the leaderboard, perfectly doubled.", "222 athletes are now on the leaderboard. Perfectly doubled.", "perfectly doubled")]
    [InlineData(2048, "2,048 athletes are now on the leaderboard, power-of-two territory.", "2,048 athletes are now on the leaderboard. Power-of-two territory.", "power-of-two territory")]
    [InlineData(8008, "8,008 athletes are now on the leaderboard, calculator humor survived.", "8,008 athletes are now on the leaderboard. Calculator humor survived.", "calculator humor survived")]
    [InlineData(9999, "9,999 athletes are now on the leaderboard, one short of five digits.", "9,999 athletes are now on the leaderboard. One short of five digits.", "one short of five digits")]
    public void MilestoneEventBuilders_ReturnNewMilestoneMessages(
        int count,
        string expectedXLine,
        string expectedThreadsLine,
        string expectedSlackFragment)
    {
        var raw = $"athletes[{count}]";

        Assert.StartsWith(expectedXLine, XMessageBuilder.ForEventText(EventType.AthleteCountMilestone, raw, SlugToName));
        Assert.StartsWith(expectedThreadsLine, ThreadsMessageBuilder.ForEventText(EventType.AthleteCountMilestone, raw, SlugToName));
        Assert.Contains(expectedSlackFragment, SlackMessageBuilder.ForEventText(EventType.AthleteCountMilestone, raw, SlugToName), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AthleteCountMilestoneThresholds_IncludeRoundAndStrongerNumberPosts()
    {
        var expectedIncluded = new[]
        {
            200, 222, 250, 888, 999, 1024, 1234, 1500, 2048, 2222,
            2500, 3000, 3333, 4000, 4444, 5555, 7500, 8008, 8888,
            9999, 11111, 12345, 22222, 54321
        };
        var expectedSkippedAsWeak = new[] { 333, 555, 1492, 1776, 2024, 4321 };

        foreach (var count in expectedIncluded)
            Assert.Contains(count, AthleteDataService.AthleteCountMilestoneThresholds);

        foreach (var count in expectedSkippedAsWeak)
            Assert.DoesNotContain(count, AthleteDataService.AthleteCountMilestoneThresholds);
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

    private static FacebookEventService CreateFacebookEventService()
    {
        var env = new TestWebHostEnvironment();
        var config = new Config
        {
            FacebookPageId = "page-id",
            FacebookPageAccessToken = "facebook-token"
        };
        var client = new FacebookApiClient(
            new HttpClient(new NoOpHttpHandler()),
            config,
            NullLogger<FacebookApiClient>.Instance);
        return new FacebookEventService(
            client,
            NullLogger<FacebookEventService>.Instance,
            new CustomEventImageService(env, NullLogger<CustomEventImageService>.Instance));
    }

    private static AthleteForX SiimForSocial()
        => new(
            "siim_land",
            "Siim Land",
            1,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    private sealed class NoOpHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"facebook-1"}""") });
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
