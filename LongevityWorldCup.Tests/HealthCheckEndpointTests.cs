using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HealthCheckEndpointTests
{
    [Fact]
    public async Task HealthEndpoint_ReturnsHealthyJson()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", document.RootElement.GetProperty("status").GetString());

        var websiteCheck = document.RootElement
            .GetProperty("checks")
            .EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == "website");
        Assert.Equal("Healthy", websiteCheck.GetProperty("status").GetString());
        Assert.True(websiteCheck.GetProperty("data").GetProperty("athleteCount").GetInt32() > 0);
    }
}
