using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class LeaderboardSharePreviewTests(TestWebApplicationFactory factory)
{
    [Theory]
    [InlineData("/leaderboard")]
    [InlineData("/league/pheno")]
    [InlineData("/league/bortz")]
    [InlineData("/league/improvement")]
    [InlineData("/league/bortz-improvement")]
    [InlineData("/league/crowd")]
    [InlineData("/league/womens")]
    [InlineData("/league/mens")]
    [InlineData("/league/millennials")]
    [InlineData("/league/gen-alpha")]
    [InlineData("/flag/hungary")]
    [InlineData("/flag/united-states")]
    [InlineData("/flag/conquer-aging-or-die-trying")]
    [InlineData("/flag/live-long-enough-to-live-forever")]
    [InlineData("/flag/hungary?filters=Women%27s")]
    [InlineData("/leaderboard?filters=women%27s%2Chungary")]
    [InlineData("/leaderboard?filters=men%27s%2Chungary")]
    [InlineData("/flag/hungary?filters=hungary,Women%27s&view=pheno")]
    [InlineData("/league/bortz?filters=Men%27s")]
    [InlineData("/?filters=Hungary,Women%27s&view=pheno")]
    [InlineData("/leaderboard?group=flags")]
    [InlineData("/leaderboard?group=flags&view=pheno&filters=Women%27s")]
    public async Task DocumentImageAndVersionedEndpointDescribeTheSelectedBoard(string route)
    {
        using var client = factory.CreateClient();
        var images = factory.Services.GetRequiredService<LeagueOgImageService>();
        var uri = new Uri("https://longevityworldcup.com" + route);
        Assert.True(images.TryGetLeaderboardPayload(uri.AbsolutePath, new QueryString(uri.Query).ToQuery(), out var payload));
        var html = await client.GetStringAsync(route);
        var imageUrl = Meta(html, "og:image");
        Assert.Equal(images.BuildVersionedImageUrl("https://longevityworldcup.com", payload), imageUrl);
        Assert.Equal(payload.PreviewTitle + " | Longevity World Cup", Meta(html, "og:title"));
        Assert.Equal("https://longevityworldcup.com" + payload.SharePath, Meta(html, "og:url"));
        using var response = await client.GetAsync(new Uri(imageUrl).PathAndQuery);
        response.EnsureSuccessStatusCode();
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString());
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(await File.ReadAllBytesAsync((await images.EnsureRenderedImageAsync(payload))!), bytes);
        using var image = Image.Load(bytes);
        Assert.Equal((1200, 630), (image.Width, image.Height));
        var output = Environment.GetEnvironmentVariable("LWC_SHARE_PREVIEW_ARTIFACTS");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            var name = Regex.Replace(route, "[^a-zA-Z0-9]+", "-").Trim('-');
            await File.WriteAllBytesAsync(Path.Combine(output, name + ".png"), bytes);
            image.Mutate(ctx => ctx.Resize(600, 315));
            await image.SaveAsPngAsync(Path.Combine(output, name + "-chat.png"));
        }
    }

    [Theory]
    [InlineData("?view=redacted")]
    [InlineData("?filters=Women's&redact=1")]
    [InlineData("?filters=Women's&search=private-arbitrary-search")]
    [InlineData("?view=pheno&sort=age&dir=desc")]
    [InlineData("?filters=unknown-cohort")]
    public async Task UnsupportedOrPrivateSelectionsHaveNoPodiumAndDoNotCacheSearchVariants(string query)
    {
        var images = factory.Services.GetRequiredService<LeagueOgImageService>();
        Assert.True(images.TryGetLeaderboardPayload("/leaderboard", new QueryString(query).ToQuery(), out var payload));
        Assert.True(payload.Neutral);
        Assert.Empty(payload.Top3Slugs);
        Assert.Empty(payload.Top3Names);
        Assert.DoesNotContain("private-arbitrary-search", images.BuildVersionedImageUrl("", payload));
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/leaderboard" + query);
        using var response = await client.GetAsync(new Uri(Meta(html, "og:image")).PathAndQuery);
        response.EnsureSuccessStatusCode();
        using var old = await client.GetAsync(images.BuildVersionedImageUrl("", payload).Replace(payload.Signature, "stale"));
        Assert.True(old.Headers.CacheControl!.MustRevalidate);
        Assert.DoesNotContain("immutable", old.Headers.CacheControl.ToString());
    }

    [Fact]
    public void PayloadUsesClockEligibilityAndFullPrecisionOrderingInsteadOfTheUltimatePodium()
    {
        var roster = new JsonArray(
            Athlete("alpha", "Women's", "Hungary", 4, 0, -15, 100, amateur: true),
            Athlete("bravo", "Men's", "Hungary", 1.2, 1, -10, 100),
            Athlete("charlie", "Women's", "Hungary", 3, 1.2, -10, 150),
            Athlete("delta", "Women's", "United States", 2, 1.5, -20, 100),
            Athlete("echo", "Men's", "United States", 2, 2, -30, 99));
        var images = new LeagueOgImageService(factory.Services.GetRequiredService<IWebHostEnvironment>(),
            new MutableAthleteSnapshot(roster), NullLogger<LeagueOgImageService>.Instance);
        void AssertPodium(string path, string query, params string[] expected)
        {
            Assert.True(images.TryGetLeaderboardPayload(path, new QueryString(query).ToQuery(), out var payload));
            Assert.Equal(expected, payload.Top3Slugs);
        }
        AssertPodium("/leaderboard", "", "bravo", "charlie", "delta");
        AssertPodium("/league/pheno", "", "alpha", "bravo", "charlie");
        AssertPodium("/league/bortz", "", "bravo", "charlie", "delta");
        AssertPodium("/league/improvement", "", "alpha", "charlie", "delta");
        AssertPodium("/league/bortz-improvement", "", "charlie", "delta", "bravo");
        AssertPodium("/league/crowd", "", "delta", "alpha", "charlie");
        AssertPodium("/flag/hungary", "?filters=Hungary,Women's", "charlie", "alpha");
        AssertPodium("/flag/hungary", "?filters=Hungary,Women's&view=pheno", "alpha", "charlie");
        AssertPodium("/flag/hungary", "?filters=Hungary,Men's", "bravo");
        AssertPodium("/flag/hungary", "?filters=Women's", "charlie", "delta", "alpha");
        AssertPodium("/league/pheno", "?filters=United%20States,Women's,Amateur");
        Assert.True(images.TryGetLeaderboardPayload("/leaderboard", new QueryString("?filters=Hungary,Women's").ToQuery(), out var first));
        Assert.True(images.TryGetLeaderboardPayload("/flag/hungary", new QueryString("?filters=women%2527s,hungary&utm_source=test").ToQuery(), out var alias));
        Assert.Equal(first.Signature, alias.Signature);
        Assert.Equal("Hungary Women's League", first.DisplayName);
        Assert.Equal("Ultimate", first.Subtitle);
        roster[2]!["Biomarkers"]![1]!["AlbGL"] = 45;
        Assert.True(images.TryGetLeaderboardPayload("/leaderboard", new QueryString("?filters=Hungary,Women's").ToQuery(), out var changedScore));
        Assert.Equal(first.Top3Slugs, changedScore.Top3Slugs);
        Assert.NotEqual(first.Scores, changedScore.Scores);
        Assert.NotEqual(first.Signature, changedScore.Signature);
    }

    [Fact]
    public async Task UnsupportedMultiFlagSelectionsStayGenericAndUnknownRoutesStillReturn404()
    {
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/league/womens?filters=Hungary,United%20States,Women's");
        Assert.Contains("/assets/og-image.png", Meta(html, "og:image"));
        Assert.Equal("2 Flags Women's League | Longevity World Cup", Meta(html, "og:title"));
        foreach (var path in new[] { "/flag/no-such-flag", "/league/no-such-league", "/og/leaderboard.png?path=/flag/no-such-flag" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private static JsonObject Athlete(string slug, string division, string flag, double worst, double latest, double crowd, int count, bool amateur = false)
    {
        var first = RedemptionArcTests.Panel("2025-01-01", worst);
        var last = RedemptionArcTests.Panel("2026-01-01", latest);
        if (amateur) { first.Remove("CystatinCMgL"); last.Remove("CystatinCMgL"); }
        var athlete = RedemptionArcTests.Athlete(slug, first, last);
        athlete["Division"] = division;
        athlete["Flag"] = flag;
        athlete["CrowdAge"] = (DateTime.UtcNow.Date - new DateTime(1980, 1, 1)).TotalDays / 365.2425 + crowd;
        athlete["CrowdCount"] = count;
        return athlete;
    }

    [Fact]
    public async Task FlagTeamsKeepTiedRanksAndIncompleteTeamsAndEmptyFieldsStayEmpty()
    {
        var roster = new JsonArray();
        foreach (var flag in new[] { "Hungary", "United States", "Bulgaria" })
            for (var i = 0; i < (flag == "Bulgaria" ? 2 : 3); i++)
                roster.Add(Athlete(flag.Replace(' ', '_') + i, "Women's", flag, 2, 1, -10, 100));
        var images = new LeagueOgImageService(factory.Services.GetRequiredService<IWebHostEnvironment>(),
            new MutableAthleteSnapshot(roster), NullLogger<LeagueOgImageService>.Instance);
        Assert.True(images.TryGetLeaderboardPayload("/leaderboard", new QueryString("?group=flags").ToQuery(), out var teams));
        Assert.Equal(["Hungary", "United States", "Bulgaria"], teams.Top3Names);
        Assert.Equal(["#1", "#1", "—"], teams.Ranks);
        Assert.Equal(teams.Scores[0], teams.Scores[1]);
        Assert.Equal("2/3 to qualify", teams.Scores[2]);
        Assert.True(images.TryGetLeaderboardPayload("/leaderboard", new QueryString("?filters=Women's,Amateur").ToQuery(), out var empty));
        Assert.Empty(empty.Top3Names);
        Assert.False(empty.Neutral);
        using var image = Image.Load((await images.EnsureRenderedImageAsync(empty))!);
        Assert.Equal((1200, 630), (image.Width, image.Height));
    }

    internal static string Meta(string html, string key) => WebUtility.HtmlDecode(Regex.Match(html,
        "<meta property=\"" + Regex.Escape(key) + "\" content=\"([^\"]*)\"").Groups[1].Value);
}
