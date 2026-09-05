using System.Net;
using LongevityWorldCup.Website.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(HttpTestCollections.ReadOnly)]
public sealed class TrackingParamStripperMiddlewareTests(TestWebApplicationFactory sharedFactory)
{
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public async Task NonNavigationRequests_KeepTheirMethodAndQuery(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = "/api/application/submit";
        context.Request.QueryString = new QueryString("?utm_source=callback&token=AbC%2B123");
        var called = false;
        var middleware = new TrackingParamStripperMiddleware(ctx =>
        {
            called = true;
            Assert.Equal(method, ctx.Request.Method);
            Assert.Equal("?utm_source=callback&token=AbC%2B123", ctx.Request.QueryString.Value);
            ctx.Response.StatusCode = 204;
            return Task.CompletedTask;
        });

        await middleware.Invoke(context);

        Assert.True(called);
        Assert.Equal(204, context.Response.StatusCode);
        Assert.Empty(context.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task ApiPostWithTrackingParameter_ReachesValidationWithoutRedirect()
    {
        using var client = sharedFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/data/pheno-age?utm_source=legacy-client", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task TrackingParameters_AreRemovedAndNonTrackingParametersArePreserved()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(
            "/css/badges.css?utm_source=newsletter&ref=keep&utm_campaign=summer&name=Alice%20Bob&empty=");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/css/badges.css?ref=keep&name=Alice%20Bob&empty=", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task TrackingParameters_AreMatchedCaseInsensitivelyAndRepeatedValuesSurvive()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(
            "/css/badges.css?UTM_Source=newsletter&ref=first&fbclid=abc&ref=second");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/css/badges.css?ref=first&ref=second", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task TrackingOnlyQuery_RedirectsToCleanPathWithoutQuestionMark()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/css/badges.css?gclid=abc&utm_medium=email");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/css/badges.css", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CleanQuery_DoesNotRedirect()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/css/badges.css?ref=keep");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
