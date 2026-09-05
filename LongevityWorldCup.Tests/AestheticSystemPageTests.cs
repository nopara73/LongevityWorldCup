using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class AestheticSystemPageTests(TestWebApplicationFactory sharedFactory)
{
    [Theory]
    [InlineData("/", true)]
    [InlineData("/league/pheno", false)]
    [InlineData("/flag/hu", false)]
    [InlineData("/athlete/nonexistent-athlete", false)]
    [InlineData("/?search=pascoe", false)]
    [InlineData("/?view=pheno", false)]
    public async Task HomepageHeroClass_IsLimitedToTheActualHomepage(string path, bool expectsHomepageHero)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.DoesNotContain("{{BODY_CLASS_ATTRIBUTE}}", html, StringComparison.Ordinal);
        if (expectsHomepageHero)
        {
            Assert.Contains("<body class=\"home-page\">", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("class=\"home-page\"", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SearchDeepLink_UsesLeaderboardChrome()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/?search=pascoe");

        Assert.DoesNotContain("class=\"home-page\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"tagline\">", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/leaderboard")]
    [InlineData("/events")]
    [InlineData("/longevitymaxxing")]
    [InlineData("/play")]
    [InlineData("/apply")]
    [InlineData("/pheno-age")]
    [InlineData("/ruleset")]
    [InlineData("/privacy")]
    public async Task SharedPages_LoadVersionedAestheticSystemLastInHead(string path)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);
        var stylesheetIndex = html.IndexOf("/css/aesthetic-system.css?v=", StringComparison.Ordinal);
        var closingHeadIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);

        Assert.True(stylesheetIndex >= 0);
        Assert.True(closingHeadIndex > stylesheetIndex);
        var stylesheetTagEnd = html.IndexOf('>', stylesheetIndex);
        Assert.True(stylesheetTagEnd > stylesheetIndex);
        var trailingHead = html[(stylesheetTagEnd + 1)..closingHeadIndex];
        Assert.DoesNotContain("<style", trailingHead, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel=\"stylesheet\"", trailingHead, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rel='stylesheet'", trailingHead, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{ASSET_AESTHETIC_SYSTEM_CSS}}", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/leaderboard")]
    [InlineData("/events")]
    [InlineData("/longevitymaxxing")]
    [InlineData("/play")]
    [InlineData("/apply")]
    [InlineData("/pheno-age")]
    [InlineData("/ruleset")]
    public async Task SharedPages_LoadVersionedSelfHostedFontAwesome(string path)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Equal(
            2,
            html.Split("/vendor/font-awesome/6.7.2/css/all.min.css?v=", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("cdnjs.cloudflare.com/ajax/libs/font-awesome", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{{ASSET_FONT_AWESOME_CSS}}", html);
    }

    [Fact]
    public async Task SelfHostedFontAwesome_DistributionIsCompleteAndServedLocally()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/vendor/font-awesome/6.7.2/css/all.min.css");
        var license = await client.GetStringAsync("/vendor/font-awesome/6.7.2/LICENSE.txt");

        Assert.Contains("Font Awesome Free 6.7.2", css);
        Assert.Contains("../webfonts/fa-solid-900.woff2", css);
        Assert.Contains("../webfonts/fa-brands-400.woff2", css);
        Assert.Contains("Font Awesome Free License", license);

        foreach (var fileName in new[]
                 {
                     "fa-brands-400.ttf",
                     "fa-brands-400.woff2",
                     "fa-regular-400.ttf",
                     "fa-regular-400.woff2",
                     "fa-solid-900.ttf",
                     "fa-solid-900.woff2",
                     "fa-v4compatibility.ttf",
                     "fa-v4compatibility.woff2"
                 })
        {
            using var response = await client.GetAsync($"/vendor/font-awesome/6.7.2/webfonts/{fileName}");
            response.EnsureSuccessStatusCode();
            Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 1_000, $"{fileName} was empty.");
        }
    }

    [Theory]
    [InlineData("/error/502.html")]
    [InlineData("/error/503.html")]
    [InlineData("/error/504.html")]
    public async Task FallbackErrors_KeepRecoveryContentCompactHumorousAndCacheSafe(string path)
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/css/error-system.css?v=20260905-1", html);
        Assert.Contains("<figure class=\"visual\">", html);
        Assert.Contains("src=\"/error/herold.png\"", html);
        Assert.Contains("alt=\"Herold waiting through a temporary outage\" width=\"1024\" height=\"1536\"", html);
        Assert.Contains(">Try again</button>", html);
    }

    [Fact]
    public async Task StandaloneInternalTools_KeepTheirIndependentVisualSystem()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/internal/custom-event-designer.html");

        Assert.DoesNotContain("/css/aesthetic-system.css", html);
    }
}
