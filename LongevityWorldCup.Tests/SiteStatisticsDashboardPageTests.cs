using Xunit;
using System.Runtime.CompilerServices;

namespace LongevityWorldCup.Tests;

public sealed class SiteStatisticsDashboardPageTests
{
    [Fact]
    public async Task SiteStatisticsDashboardPage_UsesVersionedLocalAssets()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/internal/site-statistics.html");

        Assert.Contains("/css/site-statistics.css?v=", html);
        Assert.Contains("/js/site-statistics.js?v=", html);
        Assert.Contains("Decision Brief", html);
        Assert.Contains("Recommended Investigations", html);
        Assert.Contains("Segment Comparison", html);
        Assert.Contains("Trend Watch", html);
        Assert.Contains("dataQualityStrip", html);
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
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/js/site-statistics-tracking.js", html);
        Assert.DoesNotContain("{{ASSET_SITE_STATISTICS_TRACKING_JS}}", html);
    }

    [Fact]
    public void SiteStatisticsTracker_SupportsCurrentJoinMenuControls()
    {
        var repoRoot = FindRepoRoot();
        var menu = File.ReadAllText(Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "play", "menu.html"));
        var tracker = File.ReadAllText(Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "js", "site-statistics-tracking.js"));

        Assert.Contains("id=\"joinStartAmateurBtn\"", menu);
        Assert.Contains("id=\"joinGoProButton\"", menu);
        Assert.Contains("play-join-biomarkers", menu);
        Assert.Contains("joinStartAmateurBtn", tracker);
        Assert.Contains("joinGoProButton", tracker);
        Assert.Contains(".play-join-biomarkers details", tracker);
        Assert.Contains(".play-join-card--pro", tracker);
        Assert.Contains("function setupSpaRouteTracking()", tracker);
        Assert.Contains("trackJoinPanelViewForCurrentRoute", tracker);
        Assert.Contains("window.history[method] = function ()", tracker);
        Assert.Contains("return \"internal\";", tracker);
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var dir = Path.GetDirectoryName(sourceFilePath)!;
        while (dir is not null && !File.Exists(Path.Combine(dir, "LongevityWorldCup.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
