using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LongevityWorldCup.Website.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class CrawlUrlConsistencyTests(TestWebApplicationFactory sharedFactory)
{
    private HttpClient CreateClient() => sharedFactory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    public static IEnumerable<object[]> MissingPages =>
        from path in new[]
        {
            "/missing-public-page", "/athlete/does-not-exist", "/league/does-not-exist", "/flag/does-not-exist",
            "/athlete", "/athlete/", "/league/", "/flag/", "/athlete/ron-lugbill/extra",
            "/flag/hungary/extra", "/league/amateur/extra", "/flag/hungary%3Fextra", "/league/amateur%23extra",
            "/error/404", "/error/404.html", "/event-board-embed.html?embed=1"
        }
        from method in new[] { "GET", "HEAD", "POST" }
        select new object[] { path, method };

    [Theory]
    [MemberData(nameof(MissingPages))]
    public async Task MissingPages_ReturnReal404WithoutRedirect(string path, string method)
    {
        using var client = CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("noindex, nofollow", Header(response, "X-Robots-Tag"));
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        if (method == "HEAD")
            Assert.Empty(body);
        else
        {
            Assert.Contains("<title>404 Not Found - Longevity World Cup</title>", body);
            Assert.Contains("name=\"robots\" content=\"noindex, nofollow\"", body);
        }
    }

    [Theory]
    [InlineData("/athlete/Ron_Lugbill/", "/athlete/ron-lugbill")]
    [InlineData("/ATHLETE/RON-LUGBILL", "/athlete/ron-lugbill")]
    [InlineData("/League/Amateur/", "/league/amateur")]
    [InlineData("/league/Gen_X", "/league/gen-x")]
    [InlineData("/league/Women%27s", "/league/womens")]
    [InlineData("/league/pheno-improvement", "/league/improvement")]
    [InlineData("/league/ultimate", "/leaderboard")]
    [InlineData("/FLAG/Hungary/", "/flag/hungary")]
    [InlineData("/flag/Magyarorsz%C3%A1g", "/flag/hungary")]
    [InlineData("/swagger", "/swagger/index.html")]
    [InlineData("/Swagger/Index.html/", "/swagger/index.html")]
    [InlineData("/ai/athletes.md", "/ai/leaderboard.md")]
    [InlineData("/AI/Leaderboard.md/", "/ai/leaderboard.md")]
    [InlineData("/Leaderboard/", "/leaderboard")]
    [InlineData("/privacy-policy.html", "/privacy")]
    public async Task PublicAliases_ConsolidateHostAndPathInOneHop(string path, string canonical)
    {
        using var client = CreateClient();
        const string query = "?ref=first&ref=second&token=AbC%2B%2F%3D";
        foreach (var host in new[] { "longevityworldcup.com", "www.longevityworldcup.com" })
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head, HttpMethod.Post })
        {
            using var request = new HttpRequestMessage(method, $"https://{host}{path}{query}");
            using var response = await client.SendAsync(request);
            Assert.Equal(method == HttpMethod.Post ? HttpStatusCode.PermanentRedirect : HttpStatusCode.MovedPermanently, response.StatusCode);
            var expected = (host.StartsWith("www.", StringComparison.Ordinal) ? "https://longevityworldcup.com" : "") + canonical + query;
            Assert.Equal(expected, response.Headers.Location?.OriginalString);
        }

        using var target = await client.GetAsync(canonical);
        Assert.Equal(HttpStatusCode.OK, target.StatusCode);
        Assert.Null(target.Headers.Location);
    }

    [Theory]
    [InlineData("", "?filters=professional")]
    [InlineData("?ref=a%2Bb&ref=c", "?ref=a%2Bb&ref=c&filters=professional")]
    [InlineData("?filters=amateur&ref=a%2Bb", "?filters=amateur&ref=a%2Bb")]
    public async Task LegacyProfessionalLeague_PreservesFilterAndQuerySemantics(string query, string expectedQuery)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/league/professional" + query);
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/leaderboard" + expectedQuery, response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/privacy")]
    [InlineData("/athlete/ron-lugbill")]
    [InlineData("/league/amateur")]
    [InlineData("/flag/hungary")]
    [InlineData("/swagger/index.html")]
    [InlineData("/llms.txt")]
    [InlineData("/sitemap.xml")]
    public async Task WwwPublicDocuments_RedirectToCanonicalHost(string path)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("https://www.longevityworldcup.com" + path + "?ref=kept");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://longevityworldcup.com" + path + "?ref=kept", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/api/data/flags")]
    [InlineData("/health")]
    [InlineData("/css/aesthetic-system.css")]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/event-board-embed.html?athlete=ron-lugbill&embed=1")]
    public async Task WwwNonDocumentContracts_RemainAvailableWithoutRedirect(string path)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("https://www.longevityworldcup.com" + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("example.onion")]
    [InlineData("www.longevityworldcup.com.evil.example")]
    public async Task OtherHosts_AreNeverRedirectedToThePublicOrigin(string host)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync($"https://{host}/about");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("GET", 301)]
    [InlineData("HEAD", 301)]
    [InlineData("POST", 308)]
    [InlineData("PUT", 308)]
    public async Task CanonicalRedirect_PreservesBasePathQueryAndRequestBody(string method, int status)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("www.longevityworldcup.com");
        context.Request.Method = method;
        context.Request.PathBase = "/cup";
        context.Request.Path = "/About/";
        context.Request.QueryString = new QueryString("?token=AbC%2B%2F%3D&ref=one&ref=two");
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("preserve me"));
        var middleware = new CleanPathMiddleware(_ => throw new InvalidOperationException("Should redirect."));
        await middleware.Invoke(context);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("https://longevityworldcup.com/cup/about?token=AbC%2B%2F%3D&ref=one&ref=two", context.Response.Headers.Location.ToString());
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Fact]
    public async Task LegacyAthletePage_ConsolidatesHostSlugAndRemainingQuery()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("https://www.longevityworldcup.com/event-board-embed.html?athlete=Ron_Lugbill&guessmyage=1&ref=first&ref=second");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("https://longevityworldcup.com/athlete/ron-lugbill?guessmyage=1&ref=first&ref=second", response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/api/data/flags")]
    [InlineData("/ai/leaderboard.md")]
    public async Task UnsupportedMethod_Remains405(string path)
    {
        using var client = CreateClient();
        using var response = await client.PostAsync(path, new StringContent("{}"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Privacy_HtmlAndGetHeadHeadersAgreeOnIndexing()
    {
        using var client = CreateClient();
        using var get = await client.GetAsync("/privacy");
        using var request = new HttpRequestMessage(HttpMethod.Head, "/privacy");
        using var head = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("index, follow", Header(get, "X-Robots-Tag"));
        Assert.Equal(Header(get, "X-Robots-Tag"), Header(head, "X-Robots-Tag"));
        var html = await get.Content.ReadAsStringAsync();
        Assert.Contains("name=\"robots\" content=\"index, follow\"", html);
        Assert.Contains("rel=\"canonical\" href=\"https://longevityworldcup.com/privacy\"", html);
        Assert.DoesNotContain("{{SEO_", html);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.Equal(get.Content.Headers.ContentLength, head.Content.Headers.ContentLength);
    }

    [Theory]
    [InlineData("/ai/leaderboard.md")]
    [InlineData("/ai/athlete-names.md")]
    public async Task AiDocuments_HeadMatchesGetAndSupportsConditionalRequests(string path)
    {
        using var client = CreateClient();
        using var get = await client.GetAsync(path);
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, path);
        using var head = await client.SendAsync(headRequest);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("text/markdown", head.Content.Headers.ContentType?.MediaType);
        Assert.Equal(get.Content.Headers.ContentType, head.Content.Headers.ContentType);
        Assert.Equal((await get.Content.ReadAsByteArrayAsync()).LongLength, head.Content.Headers.ContentLength);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.Equal(get.Headers.ETag, head.Headers.ETag);
        Assert.Equal(get.Headers.CacheControl?.ToString(), head.Headers.CacheControl?.ToString());
        Assert.Equal(get.Content.Headers.LastModified, head.Content.Headers.LastModified);
        Assert.Equal(Header(get, "X-Robots-Tag"), Header(head, "X-Robots-Tag"));
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            using var conditional = new HttpRequestMessage(method, path);
            conditional.Headers.IfNoneMatch.Add(get.Headers.ETag!);
            using var response = await client.SendAsync(conditional);
            Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(get.Headers.ETag, response.Headers.ETag);
        }
    }

    [Fact]
    public async Task EverySitemapUrl_Returns200ToHeadWithoutRedirectOrNoindex()
    {
        using var client = CreateClient();
        var xml = XDocument.Parse(await client.GetStringAsync("/sitemap.xml"));
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        foreach (var location in xml.Descendants(ns + "loc").Select(element => element.Value))
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, location);
            using var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{location} returned {response.StatusCode}");
            Assert.Null(response.Headers.Location);
            if (response.Headers.TryGetValues("X-Robots-Tag", out var robots))
                Assert.DoesNotContain("noindex", string.Join(",", robots), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        }
    }

    [Fact]
    public async Task Robots_AllCrawlerGroupsAllowPublicDataAndExcludePrivateApis()
    {
        using var client = CreateClient();
        var robots = await client.GetStringAsync("/robots.txt");
        // Evaluate longest-match precedence for each published group, including exact
        // endpoint endings and queries. A blanket Allow: / cannot override Disallow: /api/.
        var groups = new List<List<(bool Allow, string Pattern)>>();
        List<(bool Allow, string Pattern)>? current = null;
        foreach (var raw in robots.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.StartsWith("User-agent:", StringComparison.OrdinalIgnoreCase))
            {
                if (current is null || current.Count > 0)
                {
                    current = [];
                    groups.Add(current);
                }
            }
            else if (line.StartsWith("Allow:", StringComparison.OrdinalIgnoreCase))
                current!.Add((true, line[6..].Trim()));
            else if (line.StartsWith("Disallow:", StringComparison.OrdinalIgnoreCase))
                current!.Add((false, line[9..].Trim()));
        }
        Assert.Equal(2, groups.Count);
        foreach (var group in groups)
        {
            bool Allowed(string path) => group
                .Where(rule => Regex.IsMatch(path, "^" + Regex.Escape(rule.Pattern).Replace("\\*", ".*").Replace("\\$", "$")))
                .OrderByDescending(rule => rule.Pattern.TrimEnd('$').Length)
                .ThenByDescending(rule => rule.Allow)
                .First().Allow;
            foreach (var endpoint in new[] { "/api/data/athletes", "/api/data/flags", "/api/data/divisions", "/api/events", "/api/bitcoin/total-received", "/api/bitcoin/btcusd", "/api/bitcoin/donation-address" })
            {
                Assert.True(Allowed(endpoint), endpoint);
                Assert.True(Allowed(endpoint + "?v=1&ref=search"), endpoint);
                Assert.False(Allowed(endpoint + "/private"), endpoint);
                Assert.False(Allowed(endpoint + "-private"), endpoint);
            }
            foreach (var path in new[] { "/api/application/application", "/api/application/submission-status?token=secret", "/api/site-statistics/dashboard", "/api/Guess/athlete-age", "/athletes/ron-lugbill/proof.pdf", "/join", "/review", "/event-board-embed.html" })
                Assert.False(Allowed(path), path);
            foreach (var path in new[] { "/privacy", "/athlete/ron-lugbill", "/league/amateur", "/flag/hungary", "/ai/leaderboard.md", "/llms.txt" })
                Assert.True(Allowed(path), path);
        }
    }

    private static string Header(HttpResponseMessage response, string name) => Assert.Single(response.Headers.GetValues(name));
}
