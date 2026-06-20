using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CanonicalRouteTests
{
    [Theory]
    [InlineData("/index.html?ref=test", "/?ref=test")]
    [InlineData("/event-board/event-board.html?view=latest", "/events?view=latest")]
    [InlineData("/leaderboard/leaderboard/", "/leaderboard")]
    [InlineData("/misc-pages/ruleset.html", "/ruleset")]
    [InlineData("/onboarding/convergence.html", "/apply")]
    [InlineData("/RULES/?ref=docs", "/ruleset?ref=docs")]
    public async Task LegacyAliases_RedirectToCanonicalPath(string path, string expectedLocation)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("/Leaderboard?sort=rank")]
    [InlineData("/about/?ref=footer")]
    public async Task CleanRouteVariants_ServeWithoutRedirect(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task CanonicalCleanPath_ServesPageWithoutRedirect()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/about");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
