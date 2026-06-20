using System.Net;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public class SitemapDiscoveryTests
{
    private static readonly string[] PublicLeaguePaths =
    [
        "/league/amateur",
        "/league/mens",
        "/league/womens",
        "/league/open",
        "/league/silent-generation",
        "/league/baby-boomers",
        "/league/gen-x",
        "/league/millennials",
        "/league/gen-z",
        "/league/gen-alpha",
        "/league/prosperan",
        "/league/bortz",
        "/league/pheno",
        "/league/improvement",
        "/league/bortz-improvement",
        "/league/crowd"
    ];

    [Fact]
    public void SitemapRouteCatalog_IncludesPublicLeagueRoutes()
    {
        foreach (var path in PublicLeaguePaths)
        {
            Assert.Contains(path, SitemapService.PublicLeaguePaths);
        }
    }

    [Fact]
    public void SitemapRouteCatalog_IncludesPublicApiDocs()
    {
        Assert.Contains(SitemapService.StaticRoutes, route => route.Path == "/swagger");
    }

    [Fact]
    public void SitemapRouteCatalog_IncludesPrivacyPolicy()
    {
        Assert.Contains(SitemapService.StaticRoutes, route => route.Path == "/privacy");
    }

    [Fact]
    public void BuildXml_IncludesLeagueAndAthleteRoutes()
    {
        var xml = SitemapService.BuildXml(
        [
            new SitemapUrlEntry("/", new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc), "daily", 1.0m),
            new SitemapUrlEntry("/league/amateur", new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc), "daily", 0.8m),
            new SitemapUrlEntry("/athlete/michael-lustgarten", new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc), "weekly", 0.6m)
        ]);
        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locs = doc.Descendants(ns + "loc")
            .Select(x => x.Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("https://longevityworldcup.com/", locs);
        Assert.Contains("https://longevityworldcup.com/league/amateur", locs);
        Assert.Contains("https://longevityworldcup.com/athlete/michael-lustgarten", locs);
    }

    [Fact]
    public void BuildXml_NormalizesAndDeduplicatesRoutesUsingNewestLastModified()
    {
        var xml = SitemapService.BuildXml(
        [
            new SitemapUrlEntry("/Leaderboard/", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), "weekly", 0.1m),
            new SitemapUrlEntry("leaderboard", new DateTime(2026, 5, 30, 0, 0, 0, DateTimeKind.Utc), "daily", 0.9m),
            new SitemapUrlEntry("/league/Amateur/", new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc), "daily", 0.8m),
            new SitemapUrlEntry("/league/amateur", new DateTime(2026, 5, 21, 0, 0, 0, DateTimeKind.Utc), "daily", 0.8m)
        ]);

        var doc = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = doc.Descendants(ns + "url")
            .Select(url => new
            {
                Loc = url.Element(ns + "loc")?.Value,
                LastModified = url.Element(ns + "lastmod")?.Value
            })
            .ToList();

        var leaderboard = Assert.Single(urls, url => url.Loc == "https://longevityworldcup.com/leaderboard");
        Assert.Equal("2026-05-30", leaderboard.LastModified);
        var amateur = Assert.Single(urls, url => url.Loc == "https://longevityworldcup.com/league/amateur");
        Assert.Equal("2026-05-21", amateur.LastModified);
    }

    [Fact]
    public async Task SitemapEndpoint_ReturnsXmlWithShortPublicCacheHeaders()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync("/sitemap.xml");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl?.MaxAge);
        Assert.True(response.Headers.CacheControl?.MustRevalidate);

        var xml = await response.Content.ReadAsStringAsync();
        Assert.Contains("<urlset", xml, StringComparison.Ordinal);
        Assert.Contains("https://longevityworldcup.com/", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Robots_AllowsPublicLeagueAndAthleteRoutes()
    {
        var robotsPath = FindRepoFile(Path.Combine("LongevityWorldCup.Website", "wwwroot", "robots.txt"));
        var robots = File.ReadAllLines(robotsPath);

        Assert.Contains("Allow: /league/", robots);
        Assert.Contains("Allow: /athlete/", robots);
        Assert.Contains("Allow: /swagger", robots);
        Assert.DoesNotContain("Disallow: /league/", robots);
        Assert.DoesNotContain("Disallow: /athlete/", robots);
    }

    [Fact]
    public void Robots_AllowsGptBotAndMachineDiscoveryResources()
    {
        var robotsPath = FindRepoFile(Path.Combine("LongevityWorldCup.Website", "wwwroot", "robots.txt"));
        var robots = File.ReadAllText(robotsPath);

        Assert.Contains("User-agent: GPTBot", robots);
        Assert.Contains("Allow: /llms.txt", robots);
        Assert.Contains("Allow: /llms-full.txt", robots);
        Assert.Contains("Allow: /.well-known/agent-card.json", robots);
        Assert.Contains("Allow: /ai/index.md", robots);
        Assert.Contains("Allow: /swagger", robots);
    }

    [Fact]
    public void HeadPartial_ExposesMachineDiscoveryLinks()
    {
        var headPath = FindRepoFile(Path.Combine("LongevityWorldCup.Website", "wwwroot", "partials", "head.html"));
        var head = File.ReadAllText(headPath);

        Assert.Contains("rel=\"llms-full\"", head);
        Assert.Contains("rel=\"agent-manifest\"", head);
        Assert.Contains("rel=\"api\"", head);
        Assert.Contains("rel=\"service-desc\"", head);
    }

    [Fact]
    public void LlmsFiles_DescribeDefinitionAndPrivacy()
    {
        var llmsPath = FindRepoFile(Path.Combine("LongevityWorldCup.Website", "wwwroot", "llms.txt"));
        var llmsFullPath = FindRepoFile(Path.Combine("LongevityWorldCup.Website", "wwwroot", "llms-full.txt"));
        var llms = File.ReadAllText(llmsPath);
        var llmsFull = File.ReadAllText(llmsFullPath);

        Assert.Contains("## Definition", llms);
        Assert.Contains("https://longevityworldcup.com/privacy", llms);
        Assert.Contains("## Definition", llmsFull);
        Assert.Contains("## Positioning Notes", llmsFull);
        Assert.Contains("https://longevityworldcup.com/privacy", llmsFull);
    }

    private static string FindRepoFile(string relativePath, [CallerFilePath] string sourceFilePath = "")
    {
        var startDirectory = Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory;
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find {relativePath} from {startDirectory}.");
    }
}
