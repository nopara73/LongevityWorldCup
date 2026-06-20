using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EventBoardRedirectMiddlewareTests
{
    [Fact]
    public async Task EventBoardEmbedWithoutAthlete_RedirectsToErrorPage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/event-board-embed.html?embed=1");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/error/404.html", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task EventBoardEmbedWithoutEmbedFlag_RedirectsToCanonicalAthlete()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/event-board-embed.html?athlete=ron-lugbill&rows=all");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/athlete/ron-lugbill", response.Headers.Location?.ToString());
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task EventBoardEmbedWithEmbedFlag_ServesNoIndexHtml()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/event-board-embed.html?athlete=ron-lugbill&embed=1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex, nofollow", GetHeader(response, "X-Robots-Tag"));
        Assert.Null(response.Headers.Location);
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow, noarchive, nosnippet\">", html);
        Assert.Contains("<meta name=\"googlebot\" content=\"noindex, nofollow, noarchive, nosnippet\">", html);
    }

    private static string GetHeader(HttpResponseMessage response, string name)
    {
        Assert.True(response.Headers.TryGetValues(name, out var values), $"Missing response header '{name}'.");
        return Assert.Single(values);
    }
}
