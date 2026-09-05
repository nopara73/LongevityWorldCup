using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class SiteStatisticsDashboardPageTests(TestWebApplicationFactory sharedFactory)
{
    [Fact]
    public async Task SiteStatisticsDashboardPage_UsesVersionedLocalAssets()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/internal/site-statistics.html");

        Assert.Contains("/css/site-statistics.css?v=", html);
        Assert.Contains("/js/site-statistics.js?v=", html);
        Assert.Contains("trafficOverview", html);
        Assert.Contains("<span>Timeframe</span>", html);
        Assert.Contains("<option value=\"90d\">90D</option>", html);
        Assert.Contains("<option value=\"alltime\">All-time</option>", html);
        Assert.Contains("Decision Brief", html);
        Assert.Contains("Recommended Investigations", html);
        Assert.Contains("Segment Comparison", html);
        Assert.Contains("Trend Watch", html);
        Assert.Contains("dataQualityStrip", html);
        Assert.Contains("<option value=\"email\">Email</option>", html);
        Assert.Contains("<option value=\"internal\">Internal</option>", html);
        Assert.DoesNotContain("{{ASSET_SITE_STATISTICS_CSS}}", html);
        Assert.DoesNotContain("{{ASSET_SITE_STATISTICS_JS}}", html);
        Assert.DoesNotContain("{{ASSET_POPPINS_REGULAR}}", html);
    }

    [Theory]
    [InlineData("/join")]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    [InlineData("/apply")]
    [InlineData("/proofs")]
    [InlineData("/review")]
    [InlineData("/longevitymaxxing")]
    public async Task OnboardingAndChallengePages_UseVersionedStatisticsTracker(string path)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/js/site-statistics-tracking.js", html);
        Assert.DoesNotContain("{{ASSET_SITE_STATISTICS_TRACKING_JS}}", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/leaderboard")]
    [InlineData("/events")]
    [InlineData("/athlete/michael-lustgarten")]
    [InlineData("/league/pheno")]
    public async Task PublicDashboardEventPages_UseVersionedStatisticsTracker(string path)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/js/site-statistics-tracking.js?v=", html);
        Assert.DoesNotContain("{{ASSET_SITE_STATISTICS_TRACKING_JS}}", html);
    }
}
