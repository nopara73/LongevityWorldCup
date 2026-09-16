using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class CalculatorDiscoveryTests(TestWebApplicationFactory factory)
{
    [Theory]
    [InlineData("/pheno-age", "Pheno Age Calculator")]
    [InlineData("/bortz-age", "Bortz Age Calculator")]
    public async Task PublicCalculator_IsIndexableWithItsOwnStructuredData(string path, string name)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        var canonical = "https://longevityworldcup.com" + path;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("index, follow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Contains("<meta name=\"robots\" content=\"index, follow\"", html);
        Assert.Contains($"<title>{name} | Longevity World Cup</title>", html);
        Assert.Contains($"<link rel=\"canonical\" href=\"{canonical}\"", html);
        Assert.Contains("This is <span class=\"blood-sport-accent\">blood</span> sport", html);

        using var json = JsonDocument.Parse(StructuredData(html));
        var graph = json.RootElement.GetProperty("@graph").EnumerateArray().ToArray();
        var calculator = Assert.Single(graph, node => node.TryGetProperty("@id", out var id)
            && id.GetString() == canonical + "#calculator");
        Assert.Equal("WebApplication", calculator.GetProperty("@type").GetString());
        Assert.Equal(name, calculator.GetProperty("name").GetString());
        Assert.Equal(canonical, calculator.GetProperty("url").GetString());
        Assert.Contains("blood biomarkers", calculator.GetProperty("description").GetString());
        Assert.True(calculator.GetProperty("isAccessibleForFree").GetBoolean());
        var page = Assert.Single(graph, node => node.GetProperty("@type").GetString() == "WebPage");
        Assert.Equal(canonical + "#calculator", page.GetProperty("mainEntity").GetProperty("@id").GetString());
        var breadcrumb = Assert.Single(graph, node => node.GetProperty("@type").GetString() == "BreadcrumbList");
        Assert.Equal(name, breadcrumb.GetProperty("itemListElement").EnumerateArray().Last().GetProperty("name").GetString());

        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("index, follow", Assert.Single(head.Headers.GetValues("X-Robots-Tag")));
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task CalculatorQueries_RemainNoindexWithoutChangingTheBody(string path)
    {
        using var client = factory.CreateClient();
        var publicHtml = await client.GetStringAsync(path);
        foreach (var query in new[]
        {
            "?update=1", "?upgrade=1", "?fake=1", "?utm_source=chatgpt.com",
            "?Year=1980&Month=5&Day=20&Date=2026-09-01&AlbGL=42&CrpMgL=0.7",
            "?unknown=personal-value"
        })
        {
            using var response = await client.GetAsync(path + query);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("noindex, nofollow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
            Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\"", html);
            Assert.Contains($"<link rel=\"canonical\" href=\"https://longevityworldcup.com{path}\"", html);
            Assert.Equal(StructuredData(publicHtml), StructuredData(html));
            Assert.Equal(Body(publicHtml), Body(html));

            using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path + query));
            Assert.Equal("noindex, nofollow", Assert.Single(head.Headers.GetValues("X-Robots-Tag")));
        }
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task DiscoveryResources_ExposeOnlyCleanCalculatorUrls(string path)
    {
        using var client = factory.CreateClient();
        var canonical = "https://longevityworldcup.com" + path;
        var sitemap = XDocument.Parse(await client.GetStringAsync("/sitemap.xml"));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = sitemap.Descendants(ns + "loc").Select(node => node.Value).ToArray();
        Assert.Single(urls, url => url == canonical);
        Assert.DoesNotContain(urls, url => url.StartsWith(canonical + "?", StringComparison.Ordinal));

        var robots = await client.GetStringAsync("/robots.txt");
        var groups = Regex.Split(robots, @"\r?\n\s*\r?\n")
            .Where(group => group.Contains("User-agent:", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(groups);
        foreach (var group in groups)
        {
            Assert.Contains("Allow: " + path + "$", group);
            Assert.DoesNotContain("Disallow: " + path, group);
            Assert.Contains("Disallow: /onboarding/", group);
            Assert.Contains("Disallow: /dashboard", group);
        }

        foreach (var resource in new[] { "/llms.txt", "/llms-full.txt", "/ai/index.md" })
            Assert.Contains(canonical, await client.GetStringAsync(resource));

        using var card = JsonDocument.Parse(await client.GetStringAsync("/.well-known/agent-card.json"));
        Assert.Single(card.RootElement.GetProperty("publicPages").EnumerateArray(),
            page => page.GetProperty("url").GetString() == canonical);
    }

    private static string StructuredData(string html) =>
        Regex.Match(html, "<script type=\"application/ld\\+json\">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;

    private static string Body(string html) => html[html.IndexOf("<body", StringComparison.Ordinal)..];
}
