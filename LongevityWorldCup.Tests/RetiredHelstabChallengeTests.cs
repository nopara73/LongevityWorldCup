using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class RetiredHelstabChallengeTests(TestWebApplicationFactory factory)
{
    public static IEnumerable<object[]> RetiredPaths =>
        from path in new[]
        {
            "/helstab-kihivas", "/helstab-kihivas?legacy=test", "/HELSTAB-KIHIVAS/",
            "/helstab-kihivas/helstab-kihivas", "/helstab-kihivas/helstab-kihivas.html",
            "/HELSTAB-KIHIVAS/HELSTAB-KIHIVAS.HTML/", "/css/helstab-kihivas.css"
        }
        from method in new[] { "GET", "HEAD" }
        select new object[] { path, method };

    [Theory]
    [MemberData(nameof(RetiredPaths))]
    public async Task ChallengeAndPublicArtifacts_ReturnReal404WithoutRedirect(string path, string method)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("noindex, nofollow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        if (method == "HEAD")
            Assert.Empty(html);
        else
        {
            Assert.Contains("<title>404 Not Found - Longevity World Cup</title>", html);
            Assert.DoesNotContain("helstabSignupForm", html);
        }
    }

    [Fact]
    public async Task RetiredChallengePreview_ReturnsNotFound()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/og/page/helstab-kihivas.png");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/longevitymaxxing", "Longevitymaxxing Challenge")]
    [InlineData("/athlete/martin-helstab", "Martin Helstáb")]
    public async Task ActiveChallengeAndAthleteProfile_RemainAvailable(string path, string title)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Contains(title, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Sitemap_ExcludesRetiredChallengeAndKeepsActivePages()
    {
        using var client = factory.CreateClient();
        var sitemap = await client.GetStringAsync("/sitemap.xml");

        Assert.DoesNotContain("helstab-kihivas", sitemap);
        Assert.Contains("https://longevityworldcup.com/longevitymaxxing", sitemap);
        Assert.Contains("https://longevityworldcup.com/athlete/martin-helstab", sitemap);
    }
}
