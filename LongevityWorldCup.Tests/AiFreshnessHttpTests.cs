using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Business.IndexNow;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AiFreshnessHttpTests
{
    [Fact]
    public async Task GeneratedDocumentsHaveMatchingGetHeadAndConditionalValidators()
    {
        var source = new MutableAthleteSnapshot(AiSummaryTests.Athletes());
        await using var factory = CreateFactory(source, new SummaryClock());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var paths = LeaderboardViewCatalog.Views.Select(view => view.MarkdownPath)
            .Concat(["/ai/athlete/alpha.md", "/ai/athlete-names.md", "/llms.txt", "/llms-full.txt", "/ai/index.md", "/.well-known/agent-card.json"]);
        foreach (var path in paths)
        {
            using var get = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            Assert.NotNull(get.Headers.ETag);
            Assert.Null(get.Content.Headers.LastModified); // An initial baseline has no known change date.
            using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
            Assert.Equal(get.StatusCode, head.StatusCode);
            Assert.Equal(get.Headers.ETag, head.Headers.ETag);
            Assert.Equal(get.Content.Headers.LastModified, head.Content.Headers.LastModified);
            Assert.Equal(get.Content.Headers.ContentType, head.Content.Headers.ContentType);
            Assert.Equal((await get.Content.ReadAsByteArrayAsync()).Length, head.Content.Headers.ContentLength);
            Assert.Empty(await head.Content.ReadAsStringAsync());
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
            {
                using var conditional = new HttpRequestMessage(method, path);
                conditional.Headers.IfNoneMatch.Add(get.Headers.ETag!);
                using var response = await client.SendAsync(conditional);
                Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
                Assert.Equal(get.Headers.ETag, response.Headers.ETag);
                Assert.Empty(await response.Content.ReadAsStringAsync());
            }
        }

        using var card = JsonDocument.Parse(await client.GetStringAsync("/.well-known/agent-card.json"));
        var llms = await client.GetStringAsync("/llms-full.txt");
        Assert.Equal(17, card.RootElement.GetProperty("rankingViews").GetArrayLength());
        foreach (var view in card.RootElement.GetProperty("rankingViews").EnumerateArray())
            Assert.Contains(view.GetProperty("url").GetString()!, llms);
        Assert.Equal(await client.GetStringAsync("/llms.txt"), await client.GetStringAsync("/ai/index.md"));
        Assert.Contains("Highlights", llms);
        Assert.DoesNotContain("event schedule", llms, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/AI/League/CROWD.MD/", "/ai/league/crowd.md")]
    [InlineData("/ai/league/ultimate.md", "/ai/leaderboard.md")]
    [InlineData("/AI/Athlete/ALPHA.MD", "/ai/athlete/alpha.md")]
    public async Task NewSummaryAliasesRedirectToOneCanonicalUrl(string path, string canonical)
    {
        await using var factory = CreateFactory(new MutableAthleteSnapshot(AiSummaryTests.Athletes()), new SummaryClock());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync("https://www.longevityworldcup.com" + path + "?ref=test");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://longevityworldcup.com" + canonical + "?ref=test", response.Headers.Location?.ToString());
    }

    [Theory]
    [InlineData("/ai/athlete/nonexistent.md")]
    [InlineData("/ai/league/nonexistent.md")]
    public async Task UnknownSummaryIsARealNonIndexable404(string path)
    {
        await using var factory = CreateFactory(new MutableAthleteSnapshot(AiSummaryTests.Athletes()), new SummaryClock());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains("noindex", response.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task RuntimeChangesUpdateSitemapStructuredDataAndMachineValidatorsWithoutChangingUnrelatedPages()
    {
        var clock = new SummaryClock();
        var source = new MutableAthleteSnapshot([]);
        await using var factory = CreateFactory(source, clock);
        source.Data = factory.Services.GetRequiredService<AthleteDataService>().GetAthletesSnapshot();
        var freshness = factory.Services.GetRequiredService<PublicContentFreshness>();
        freshness.Refresh();
        using var client = factory.CreateClient();
        var beforeSitemap = Dates(await client.GetStringAsync("/sitemap.xml"));
        Assert.Null(beforeSitemap["/"]);
        Assert.Null(Modified(await client.GetStringAsync("/")));
        using var before = await client.GetAsync("/ai/leaderboard.md");
        var names = await client.GetStringAsync("/ai/athlete-names.md");

        clock.Advance(TimeSpan.FromMinutes(12));
        var athlete = source.Data.OfType<JsonObject>().First();
        athlete["CrowdAge"] = 30d;
        athlete["CrowdCount"] = 101;
        freshness.Refresh();
        var afterSitemap = Dates(await client.GetStringAsync("/sitemap.xml"));
        var expected = clock.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss'Z'");
        Assert.Equal(expected, afterSitemap["/"]);
        Assert.Equal(expected, afterSitemap["/leaderboard"]);
        Assert.Equal(expected, Modified(await client.GetStringAsync("/")));
        Assert.Equal(beforeSitemap["/about"], afterSitemap["/about"]);
        using var after = await client.GetAsync("/ai/leaderboard.md");
        Assert.Equal(clock.GetUtcNow(), after.Content.Headers.LastModified);
        Assert.Equal(expected, afterSitemap["/ai/leaderboard.md"]);
        Assert.NotEqual(before.Headers.ETag, after.Headers.ETag);
        Assert.Equal(names, await client.GetStringAsync("/ai/athlete-names.md"));

        clock.Advance(TimeSpan.FromMinutes(12));
        using var unchanged = await client.GetAsync("/ai/leaderboard.md");
        Assert.Equal(after.Headers.ETag, unchanged.Headers.ETag);
        Assert.Equal(after.Content.Headers.LastModified, unchanged.Content.Headers.LastModified);
        Assert.Equal(afterSitemap, Dates(await client.GetStringAsync("/sitemap.xml")));
    }

    [Fact]
    public async Task BackgroundObservationRefreshesRequestedPages()
    {
        var source = new MutableAthleteSnapshot(AiSummaryTests.Athletes());
        var clock = new SummaryClock();
        await using var factory = CreateFactory(source, clock);
        using var client = factory.CreateClient();
        var freshness = factory.Services.GetRequiredService<PublicContentFreshness>();
        freshness.Refresh();
        Assert.Null(freshness.GetLastModifiedUtc("/"));
        source.Data[0]!["CrowdCount"] = 101;
        clock.Advance(TimeSpan.FromMinutes(1));
        var deadline = DateTime.UtcNow.AddSeconds(25);
        while (freshness.GetLastModifiedUtc("/") is null && DateTime.UtcNow < deadline)
            await Task.Delay(200);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, freshness.GetLastModifiedUtc("/"));
        Assert.Equal(clock.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss'Z'"), Modified(await client.GetStringAsync("/")));
    }

    [Fact]
    public async Task PageRenderingDoesNotWaitForOrRequireASiteWideContentScan()
    {
        await using var factory = new TestWebApplicationFactory(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IndexNowPageContent>();
            services.AddSingleton(sp => new IndexNowPageContent(sp.GetRequiredService<IWebHostEnvironment>(), "missing-test-manifest"));
        }));
        // A dependency scan would fail here. The optional metadata must not turn
        // an otherwise valid page into a 500 or make it wait for all other pages.
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(Modified(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task PublishedLegacyStaticCopiesCannotShadowTheGeneratedDiscoveryCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "LwcOldDiscovery", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "athletes"));
            foreach (var path in new[] { "llms.txt", "llms-full.txt", "ai/index.md", ".well-known/agent-card.json" })
            {
                var file = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, "obsolete static discovery copy");
            }
            await using var factory = new TestWebApplicationFactory(builder => builder.UseWebRoot(root));
            using var client = factory.CreateClient();
            foreach (var path in new[] { "/llms.txt", "/llms-full.txt", "/ai/index.md", "/.well-known/agent-card.json" })
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.DoesNotContain("obsolete static", await response.Content.ReadAsStringAsync());
                Assert.Equal(TimeSpan.FromMinutes(5), response.Headers.CacheControl?.MaxAge);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static TestWebApplicationFactory CreateFactory(MutableAthleteSnapshot source, SummaryClock clock) =>
        new(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAthleteSnapshotProvider>();
            services.AddSingleton<IAthleteSnapshotProvider>(source);
            services.AddSingleton<TimeProvider>(clock);
        }));

    private static Dictionary<string, string?> Dates(string xml)
    {
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        return XDocument.Parse(xml).Descendants(ns + "url").ToDictionary(
            url => new Uri(url.Element(ns + "loc")!.Value).AbsolutePath, url => url.Element(ns + "lastmod")?.Value);
    }

    private static string? Modified(string html)
    {
        var match = Regex.Match(html, "<script[^>]*type=\"application/ld\\+json\"[^>]*>(.*?)</script>", RegexOptions.Singleline);
        using var json = JsonDocument.Parse(match.Groups[1].Value);
        var page = json.RootElement.GetProperty("@graph").EnumerateArray().Single(node => node.GetProperty("@type").GetString() == "WebPage");
        return page.TryGetProperty("dateModified", out var modified) ? modified.GetString() : null;
    }
}
