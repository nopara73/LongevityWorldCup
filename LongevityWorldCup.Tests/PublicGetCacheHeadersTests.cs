using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PublicGetCacheHeadersTests
{
    [Fact]
    public void BuildWeakContentETag_ReturnsStableSha256WeakValidator()
    {
        var eTag = PublicGetCacheHeaders.BuildWeakContentETag("hello");

        Assert.Equal("W/\"sha256-2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824\"", eTag);
    }

    [Theory]
    [InlineData("\"sha256-test\"", "W/\"sha256-test\"")]
    [InlineData("W/\"sha256-test\"", "\"sha256-test\"")]
    [InlineData("\"other\", W/\"sha256-test\"", "\"sha256-test\"")]
    [InlineData("*", "\"sha256-test\"")]
    public void RequestHasMatchingETag_MatchesWildcardAndWeakOrStrongTags(string ifNoneMatch, string eTag)
    {
        var headers = new HeaderDictionary
        {
            [HeaderNames.IfNoneMatch] = ifNoneMatch
        };

        Assert.True(PublicGetCacheHeaders.RequestHasMatchingETag(headers, eTag));
    }

    [Theory]
    [InlineData("\"sha256-other\"", "\"sha256-test\"")]
    [InlineData("W/\"sha256-other\"", "\"sha256-test\"")]
    public void RequestHasMatchingETag_RejectsDifferentTags(string ifNoneMatch, string eTag)
    {
        var headers = new HeaderDictionary
        {
            [HeaderNames.IfNoneMatch] = ifNoneMatch
        };

        Assert.False(PublicGetCacheHeaders.RequestHasMatchingETag(headers, eTag));
    }

    [Fact]
    public void RequestHasMatchingETag_ReturnsFalseWhenHeaderIsMissing()
    {
        Assert.False(PublicGetCacheHeaders.RequestHasMatchingETag(new HeaderDictionary(), "\"sha256-test\""));
    }

    [Fact]
    public void Apply_SetsCacheHeadersAndValidators()
    {
        var context = new DefaultHttpContext();
        var lastModified = new DateTimeOffset(2026, 6, 20, 10, 30, 0, TimeSpan.Zero);

        PublicGetCacheHeaders.Apply(
            context.Response,
            PublicGetCacheHeaders.AiFactsCacheControl,
            PublicGetCacheHeaders.AiFactsMaxAgeSeconds,
            "W/\"sha256-test\"",
            lastModified);

        Assert.Equal(PublicGetCacheHeaders.AiFactsCacheControl, context.Response.Headers.CacheControl);
        Assert.Equal("W/\"sha256-test\"", context.Response.Headers.ETag);
        Assert.Equal("Sat, 20 Jun 2026 10:30:00 GMT", context.Response.Headers.LastModified);
        Assert.False(string.IsNullOrWhiteSpace(context.Response.Headers.Expires));
    }
}
