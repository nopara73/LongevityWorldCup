using System.Net;
using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AthletesDataCacheTests
{
    [Fact]
    public async Task AthletesEndpoint_ReturnsETagAndHonorsConditionalGet()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var firstResponse = await client.GetAsync("/api/data/athletes");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.NotNull(firstResponse.Headers.ETag);
        Assert.True(firstResponse.Headers.ETag!.IsWeak);
        Assert.True(firstResponse.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromSeconds(60), firstResponse.Headers.CacheControl?.MaxAge);
        Assert.True(firstResponse.Headers.CacheControl?.MustRevalidate);

        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.NotEmpty(firstBody);

        using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, "/api/data/athletes");
        conditionalRequest.Headers.IfNoneMatch.Add(firstResponse.Headers.ETag);

        using var secondResponse = await client.SendAsync(conditionalRequest);

        Assert.Equal(HttpStatusCode.NotModified, secondResponse.StatusCode);
        Assert.NotNull(secondResponse.Headers.ETag);
        Assert.Equal(firstResponse.Headers.ETag.Tag, secondResponse.Headers.ETag!.Tag);
        Assert.Equal(firstResponse.Headers.ETag.IsWeak, secondResponse.Headers.ETag.IsWeak);
        Assert.Equal("", await secondResponse.Content.ReadAsStringAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new TestWebApplicationFactory();
    }
}
