using System.Net;
using System.Net.Http.Json;
using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PublicApiCorsTests
{
    private const string ArbitraryOrigin = "https://public-api-client.example";
    private const string TrustedSiteOrigin = "https://www.longevityworldcup.com";

    [Fact]
    public async Task PublicDataGet_AllowsAnyOrigin()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/data/flags");
        request.Headers.Add("Origin", ArbitraryOrigin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task PublicDataPostPreflight_AllowsAnyOrigin()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/data/pheno-age");
        request.Headers.Add("Origin", ArbitraryOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("POST", response.Headers.GetValues("Access-Control-Allow-Methods"));
        Assert.Contains("content-type", response.Headers.GetValues("Access-Control-Allow-Headers"));
    }

    [Fact]
    public async Task PublicDataValidationError_AllowsAnyOrigin()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/data/pheno-age")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Origin", ArbitraryOrigin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task NonPublicEndpoints_PreserveRestrictedOriginPolicy()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        using var arbitraryRequest = new HttpRequestMessage(HttpMethod.Get, "/health");
        arbitraryRequest.Headers.Add("Origin", ArbitraryOrigin);
        using var trustedRequest = new HttpRequestMessage(HttpMethod.Get, "/health");
        trustedRequest.Headers.Add("Origin", TrustedSiteOrigin);

        using var arbitraryResponse = await client.SendAsync(arbitraryRequest);
        using var trustedResponse = await client.SendAsync(trustedRequest);

        Assert.Equal(HttpStatusCode.OK, arbitraryResponse.StatusCode);
        Assert.False(arbitraryResponse.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(HttpStatusCode.OK, trustedResponse.StatusCode);
        Assert.Equal(
            TrustedSiteOrigin,
            Assert.Single(trustedResponse.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task NonPublicNotFound_ReExecutesRoutedErrorEndpoint()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/route-that-does-not-exist");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/error/404.html", response.Headers.Location?.ToString());
    }
}
