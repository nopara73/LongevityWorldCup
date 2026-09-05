using System.Net;
using LongevityWorldCup.Website.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(HttpTestCollections.ReadOnly)]
public sealed class EventBoardRedirectMiddlewareTests(TestWebApplicationFactory sharedFactory)
{
    [Theory]
    [InlineData("GET", 301)]
    [InlineData("HEAD", 301)]
    [InlineData("POST", 308)]
    public async Task LegacyAthleteRedirect_PreservesBasePathAndRemainingQuery(string method, int status)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.PathBase = "/cup";
        context.Request.Path = "/event-board-embed.html";
        context.Request.QueryString = new QueryString("?athlete=ron-lugbill&guessmyage=1&ref=first&ref=second&token=AbC%2B%2F%3D");
        var middleware = new EventBoardRedirectMiddleware(_ => throw new InvalidOperationException("Should redirect."));

        await middleware.Invoke(context);

        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("/cup/athlete/ron-lugbill?guessmyage=1&ref=first&ref=second&token=AbC%2B%2F%3D",
            context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task EventBoardEmbedWithoutAthlete_RedirectsToErrorPage()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/event-board-embed.html?embed=1");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/error/404.html", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task EventBoardEmbedWithoutEmbedFlag_RedirectsToCanonicalAthlete()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/event-board-embed.html?athlete=ron-lugbill&rows=all");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/athlete/ron-lugbill?rows=all", response.Headers.Location?.ToString());
        Assert.False(response.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    public async Task EventBoardEmbedWithEmbedFlag_ServesNoIndexHtml()
    {
        var factory = sharedFactory;
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
