using System.Net;
using System.Net.Http.Headers;
using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PublicGetCachingTests
{
    [Theory]
    [InlineData("/api/data/flags")]
    [InlineData("/api/data/divisions")]
    [InlineData("/api/bitcoin/donation-address")]
    public async Task StableReferenceEndpoints_ReturnLongPublicCacheHeadersAndHonorConditionalGet(string path)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var firstResponse = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.True(firstResponse.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromDays(1), firstResponse.Headers.CacheControl?.MaxAge);
        AssertCacheControlExtension(firstResponse.Headers.CacheControl, "stale-while-revalidate", "604800");
        Assert.NotNull(firstResponse.Headers.ETag);
        Assert.True(firstResponse.Headers.ETag!.IsWeak);

        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.NotEmpty(firstBody);

        using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, path);
        conditionalRequest.Headers.IfNoneMatch.Add(firstResponse.Headers.ETag);

        using var secondResponse = await client.SendAsync(conditionalRequest);

        Assert.Equal(HttpStatusCode.NotModified, secondResponse.StatusCode);
        Assert.True(secondResponse.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromDays(1), secondResponse.Headers.CacheControl?.MaxAge);
        AssertCacheControlExtension(secondResponse.Headers.CacheControl, "stale-while-revalidate", "604800");
        Assert.NotNull(secondResponse.Headers.ETag);
        Assert.Equal(firstResponse.Headers.ETag.Tag, secondResponse.Headers.ETag!.Tag);
        Assert.Equal(firstResponse.Headers.ETag.IsWeak, secondResponse.Headers.ETag.IsWeak);
        Assert.Equal("", await secondResponse.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/ai/leaderboard.md")]
    [InlineData("/ai/athlete-names.md")]
    public async Task AiMarkdownEndpoints_ReturnValidatorsAndHonorConditionalGet(string path)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var firstResponse = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.True(firstResponse.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromMinutes(5), firstResponse.Headers.CacheControl?.MaxAge);
        Assert.True(firstResponse.Headers.CacheControl?.MustRevalidate);
        Assert.NotNull(firstResponse.Headers.ETag);
        Assert.True(firstResponse.Headers.ETag!.IsWeak);
        Assert.NotNull(firstResponse.Content.Headers.LastModified);

        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.NotEmpty(firstBody);

        using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, path);
        conditionalRequest.Headers.IfNoneMatch.Add(firstResponse.Headers.ETag);

        using var secondResponse = await client.SendAsync(conditionalRequest);

        Assert.Equal(HttpStatusCode.NotModified, secondResponse.StatusCode);
        Assert.True(secondResponse.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromMinutes(5), secondResponse.Headers.CacheControl?.MaxAge);
        Assert.True(secondResponse.Headers.CacheControl?.MustRevalidate);
        Assert.NotNull(secondResponse.Headers.ETag);
        Assert.Equal(firstResponse.Headers.ETag.Tag, secondResponse.Headers.ETag!.Tag);
        Assert.Equal(firstResponse.Headers.ETag.IsWeak, secondResponse.Headers.ETag.IsWeak);
        Assert.Equal("", await secondResponse.Content.ReadAsStringAsync());
    }

    private static void AssertCacheControlExtension(CacheControlHeaderValue? cacheControl, string name, string value)
    {
        Assert.NotNull(cacheControl);
        Assert.Contains(cacheControl!.Extensions, extension =>
            string.Equals(extension.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(extension.Value, value, StringComparison.Ordinal));
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new TestWebApplicationFactory();
    }
}
