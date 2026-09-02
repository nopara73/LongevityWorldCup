using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PublicPostRateLimitingTests
{
    [Fact]
    public async Task PublicPostEndpointsAreRateLimitedPerClient()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        for (var i = 0; i < 120; i++)
        {
            using var response = await PostEmptyPhenoAgeRequest(client);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var limitedResponse = await PostEmptyPhenoAgeRequest(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.True(limitedResponse.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task NonPostRequestsAreNotRateLimitedByPublicPostLimiter()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        for (var i = 0; i < 125; i++)
        {
            using var response = await client.GetAsync("/api/data/flags");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task AnalyticsRateLimitPreservesTooManyRequestsStatusOverKestrel()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = app.BaseAddress
        };

        for (var i = 0; i < 240; i++)
        {
            using var response = await PostEmptyAnalyticsEvent(client);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var limitedResponse = await PostEmptyAnalyticsEvent(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        Assert.True(limitedResponse.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    private static Task<HttpResponseMessage> PostEmptyPhenoAgeRequest(HttpClient client)
        => client.PostAsync(
            "/api/data/pheno-age",
            new StringContent("{}", Encoding.UTF8, "application/json"));

    private static Task<HttpResponseMessage> PostEmptyAnalyticsEvent(HttpClient client)
        => client.PostAsync(
            "/api/site-statistics/event",
            new StringContent("{}", Encoding.UTF8, "application/json"));
}
