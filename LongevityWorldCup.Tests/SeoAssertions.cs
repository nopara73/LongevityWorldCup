using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace LongevityWorldCup.Tests;

// Only inspects responses and JSON already obtained by the calling tests.
internal static class SeoAssertions
{
    private const string Origin = "https://longevityworldcup.com";

    public static void PublicPageMetadata(HttpResponseMessage response, string html, string path)
    {
        if (response.Headers.TryGetValues("X-Robots-Tag", out var directives))
            Assert.DoesNotMatch("(?i)\\b(noindex|nofollow|none)\\b", string.Join(", ", directives));
        Assert.Contains("<meta name=\"robots\" content=\"index, follow\"", html);

        var page = Assert.Single(PageStructuredDataTests.ReadGraph(html),
            node => node.TryGetProperty("@id", out var id) && id.GetString() == Origin + path + "#webpage");
        PageIdentity(page, path);
    }

    public static void PageIdentity(JsonElement page, string path)
    {
        Assert.Equal(Origin + path, page.GetProperty("url").GetString());
        Assert.False(string.IsNullOrWhiteSpace(page.GetProperty("name").GetString()));
    }

    public static void SitemapPublicPages(string xml)
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locations = XDocument.Parse(xml).Elements(ns + "urlset").Elements(ns + "url")
            .Elements(ns + "loc").Select(location => location.Value).ToArray();
        foreach (var path in new[] { "/", "/leaderboard" })
            Assert.Single(locations, location => location == Origin + path);
    }
}
