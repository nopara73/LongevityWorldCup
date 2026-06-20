using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class TrackingParamStripperMiddlewareTests
{
    [Fact]
    public async Task TrackingParameters_AreRemovedAndNonTrackingParametersArePreserved()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(
            "/css/badges.css?utm_source=newsletter&ref=keep&utm_campaign=summer&name=Alice%20Bob&empty=");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/css/badges.css?ref=keep&name=Alice%20Bob&empty=", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task TrackingParameters_AreMatchedCaseInsensitivelyAndRepeatedValuesSurvive()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(
            "/css/badges.css?UTM_Source=newsletter&ref=first&fbclid=abc&ref=second");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/css/badges.css?ref=first&ref=second", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task TrackingOnlyQuery_RedirectsToCleanPathWithoutQuestionMark()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/css/badges.css?gclid=abc&utm_medium=email");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/css/badges.css", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CleanQuery_DoesNotRedirect()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/css/badges.css?ref=keep");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
