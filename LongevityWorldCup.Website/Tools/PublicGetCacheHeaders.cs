using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace LongevityWorldCup.Website.Tools;

public static class PublicGetCacheHeaders
{
    public const int StaticReferenceMaxAgeSeconds = 86_400;
    public const string StaticReferenceCacheControl = "public,max-age=86400,stale-while-revalidate=604800";

    public const int AthleteSnapshotMaxAgeSeconds = 60;
    public const string AthleteSnapshotCacheControl = "public,max-age=60,must-revalidate";

    public const int BitcoinUsdMaxAgeSeconds = 60;
    public const string BitcoinUsdCacheControl = "public,max-age=60,must-revalidate";

    public const int BitcoinTotalReceivedMaxAgeSeconds = 180;
    public const string BitcoinTotalReceivedCacheControl = "public,max-age=180,must-revalidate";

    public const int AiFactsMaxAgeSeconds = 300;
    public const string AiFactsCacheControl = "public,max-age=300,must-revalidate";

    public static string BuildWeakContentETag(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return $"W/\"sha256-{Convert.ToHexString(hash).ToLowerInvariant()}\"";
    }

    public static void Apply(HttpResponse response, string cacheControl, int maxAgeSeconds, string? eTag = null, DateTimeOffset? lastModified = null)
    {
        response.Headers[HeaderNames.CacheControl] = cacheControl;
        response.Headers[HeaderNames.Expires] = DateTimeOffset.UtcNow.AddSeconds(maxAgeSeconds).ToString("R");

        if (!string.IsNullOrWhiteSpace(eTag))
            response.Headers[HeaderNames.ETag] = eTag;

        if (lastModified.HasValue)
            response.Headers[HeaderNames.LastModified] = lastModified.Value.UtcDateTime.ToString("R");
    }

    public static bool RequestHasMatchingETag(IHeaderDictionary headers, string eTag)
    {
        if (!headers.TryGetValue(HeaderNames.IfNoneMatch, out var ifNoneMatchValues))
            return false;

        var strongTag = StripWeakPrefix(eTag);
        foreach (var headerValue in ifNoneMatchValues)
        {
            foreach (var candidate in headerValue?.Split(',') ?? [])
            {
                var trimmed = candidate.Trim();
                if (trimmed == "*")
                    return true;

                if (string.Equals(StripWeakPrefix(trimmed), strongTag, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private static string StripWeakPrefix(string tag)
        => tag.StartsWith("W/", StringComparison.Ordinal) ? tag[2..] : tag;
}
