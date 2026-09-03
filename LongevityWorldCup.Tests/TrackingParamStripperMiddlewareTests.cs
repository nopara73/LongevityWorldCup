using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(HttpTestCollections.ReadOnly)]
public sealed class TrackingParamStripperMiddlewareTests(TestWebApplicationFactory sharedFactory)
{
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
