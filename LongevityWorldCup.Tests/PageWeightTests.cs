using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class PageWeightTests(TestWebApplicationFactory factory)
{
    [Theory]
    [InlineData("/", 110_000)]
    [InlineData("/athlete/michael-lustgarten", 110_000)]
    [InlineData("/leaderboard", 430_000)]
    [InlineData("/events", 80_000)]
    [InlineData("/about", 100_000)]
    [InlineData("/pheno-age", 230_000)]
    [InlineData("/bortz-age", 285_000)]
    public async Task PublicDocuments_DoNotInlineSharedApplicationCode(string path, int budget)
    {
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync(path);

        var bytes = Encoding.UTF8.GetByteCount(html);
        Assert.True(bytes < budget, $"{path} sent {bytes:N0} bytes, exceeding its {budget:N0}-byte HTML budget.");
        Assert.Contains("/js/leaderboard-page.js?v=", html);
        Assert.Contains("/js/guess-my-age.js?v=", html);
        Assert.DoesNotContain("window.openAthleteModalBySlug = function", html);
        Assert.DoesNotContain("{{ASSET_", html);

        // Dynamic font URLs still use the same version as the head's preload.
        Assert.Matches(@"src: url\('/assets/fonts/Roboto-Regular\.woff2\?v=[^']+'\)", html);
    }

    [Fact]
    public async Task SharedScriptsAndStyles_AreVersionedCacheableAndReusableAcrossPages()
    {
        using var client = factory.CreateClient();
        var home = await client.GetStringAsync("/");
        var leaderboard = await client.GetStringAsync("/leaderboard");
        var paths = new[]
        {
            "/js/site-header.js", "/js/site-footer.js", "/js/leaderboard-page.js",
            "/js/leaderboard-embed-listener.js", "/js/guess-my-age.js",
            "/css/site-header.css", "/css/site-footer.css", "/css/leaderboard-content.css",
            "/css/guess-my-age.css", "/css/age-visualization.css"
        };
        foreach (var path in paths)
        {
            var match = Regex.Match(home, Regex.Escape(path) + @"\?v=[^""']+");
            Assert.True(match.Success, $"Missing versioned asset: {path}");
            Assert.Contains(match.Value, leaderboard);
            using var response = await client.GetAsync(match.Value);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("immutable", response.Headers.CacheControl!.ToString());
            Assert.Equal(TimeSpan.FromDays(365), response.Headers.CacheControl.MaxAge);
            var content = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("{{ASSET_", content);
            Assert.NotNull(response.Headers.ETag);

            using var request = new HttpRequestMessage(HttpMethod.Get, match.Value);
            request.Headers.IfNoneMatch.Add(response.Headers.ETag);
            using var cached = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
        }
    }

    [Theory]
    [InlineData("leaderboard-content")]
    [InlineData("guess-my-age")]
    [InlineData("age-visualization")]
    public async Task EmbeddedDialogStyles_UseTheSameRulesInsideTheirOwnScope(string name)
    {
        using var client = factory.CreateClient();
        var shared = await client.GetStringAsync($"/css/{name}.css");
        var scoped = await client.GetStringAsync($"/css/athlete-dialog/{name}.css");
        Assert.Equal("@scope (#athleteDialogRuntime) {\n" + Regex.Replace(shared, @":root\b", ":scope", RegexOptions.IgnoreCase) + "}\n", scoped);
    }
}
