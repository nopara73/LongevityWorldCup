using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepagePageTests
{
    [Fact]
    public async Task Homepage_RendersServerSideFirstViewportCompetitionSignal()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("class=\"homepage-first-viewport\"", html);
        Assert.Contains("Live longevity sport", html);
        Assert.Contains("Ultimate League", html);
        Assert.Contains("Pro before Amateur", html);
        Assert.Contains("Apply as athlete", html);
        Assert.Contains("View leaderboard", html);
        Assert.Contains("data-homepage-rank-card=\"true\"", html);
        Assert.DoesNotContain("<!--HOMEPAGE-FIRST-VIEWPORT-->", html);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("EnableScheduledJobs", "false");
                builder.UseSetting("EnableStartupBadgeRefresh", "false");
            });
    }
}
