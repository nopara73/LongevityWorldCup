using System.Net;
using System.Xml.Linq;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Business.IndexNow;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class IndexNowIntegrationTests(TestWebApplicationFactory factory)
{
    [Fact]
    public async Task RealPipelineServesExactKeyAndHeadWithoutHtmlOrRedirect()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var store = factory.Services.GetRequiredService<IndexNowStateStore>();
        var path = $"/{store.Key}.txt";
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(store.Key, await response.Content.ReadAsStringAsync());
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.Equal(store.Key.Length, head.Content.Headers.ContentLength);
    }

    [Fact]
    public void CurrentPublicCatalogMatchesSitemapAndUnchangedRescanIsStable()
    {
        var source = factory.Services.GetRequiredService<IndexNowContentSnapshot>();
        var first = source.Build(default);
        var second = source.Build(default);
        Assert.Equal(first, second);
        var sitemap = XDocument.Parse(factory.Services.GetRequiredService<SitemapService>().BuildXml());
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var expected = sitemap.Descendants(ns + "loc").Select(node => node.Value).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, first.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.True(first.Count > 100);
        Assert.DoesNotContain(first.Keys, url => url.Contains("/athletes/", StringComparison.Ordinal) || url.Contains('?'));
    }
}
