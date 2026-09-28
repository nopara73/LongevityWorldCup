using System.Net;
using System.Net.Http.Json;
using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(HttpTestCollections.ReadOnly)]
public sealed class PublicApiCorsTests(TestWebApplicationFactory sharedFactory)
{
    private const string ArbitraryOrigin = "https://public-api-client.example";
    private const string TrustedSiteOrigin = "https://www.longevityworldcup.com";

    [Theory]
    [InlineData("/api/data/flags")]
    [InlineData("/api/events")]
    [InlineData("/api/Events/")]
    public async Task PublicDataGet_AllowsAnyOrigin(string path)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Origin", ArbitraryOrigin);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Theory]
    [InlineData("/api/data/pheno-age", "POST")]
    [InlineData("/api/events", "GET")]
    [InlineData("/api/Events/", "GET")]
    public async Task PublicDataPreflight_AllowsAnyOrigin(string path, string method)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", ArbitraryOrigin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains(method, response.Headers.GetValues("Access-Control-Allow-Methods"));
        Assert.Contains("content-type", response.Headers.GetValues("Access-Control-Allow-Headers"));
    }

    [Fact]
    public async Task PublicDataValidationError_AllowsAnyOrigin()
    {
        var factory = sharedFactory;
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

    [Theory]
    [InlineData("/api/custom-events")]
    [InlineData("/api/events-private")]
    [InlineData("/api/events/admin")]
    public async Task NonPublicEventPreflight_DoesNotAllowArbitraryOrigins(string path)
    {
        using var client = sharedFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", ArbitraryOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        using var response = await client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task NonPublicEndpoints_PreserveRestrictedOriginPolicy()
    {
        var factory = sharedFactory;
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
        var factory = sharedFactory;
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/route-that-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("404 Not Found", await response.Content.ReadAsStringAsync());
    }
}
