using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(HttpTestCollections.ReadOnly)]
public sealed class LeaderboardRouteShellTests(TestWebApplicationFactory sharedFactory)
{
    [Theory]
    [InlineData("/flag/hungary")]
    [InlineData("/league/amateur")]
    [InlineData("/league/bortz")]
    public async Task CanonicalLeaderboardRoute_RendersFullLeaderboardShell(string path)
    {
        using var client = sharedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<main data-leaderboard-page=\"full\">", html, StringComparison.Ordinal);
        Assert.Contains("<h1 id=\"leaderboardTitle\">", html, StringComparison.Ordinal);
        Assert.Contains("LoadLeaderboard(false, Infinity);", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"viewAllAthletesBtn\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AthleteRoute_KeepsHomepageShellForSharedProfileDialog()
    {
        using var client = sharedFactory.CreateClient();

        var html = await client.GetStringAsync("/athlete/ron-lugbill");

        Assert.Contains("id=\"viewAllAthletesBtn\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<main data-leaderboard-page=\"full\">", html, StringComparison.Ordinal);
    }
}
