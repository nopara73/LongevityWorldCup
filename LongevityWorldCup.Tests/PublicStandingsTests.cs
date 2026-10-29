using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PublicStandingsTests
{
    [Fact]
    public void Preview_MatchesCanonicalOrderAndPrecisionIncludingTieAcrossCutoff_AndKeepsPrivateDataOut()
    {
        var data = new JsonArray(Enumerable.Range(1, 9).Reverse().Select(i => (JsonNode)Athlete(i)).ToArray());
        var source = new MutableAthleteSnapshot(data);
        var clock = new SummaryClock();
        var service = new PublicStandingsService(source, clock);
        var result = service.Get();
        var expected = new PublicLeaderboardSnapshot(data, clock.GetUtcNow().UtcDateTime.Date)
            .Select("/leaderboard", QueryCollection.Empty).Snapshot.Rows.Take(8).ToArray();
        Assert.Equal(8, result.Rows.Count);
        Assert.Equal(expected.Select(a => a.AthletePath), result.Rows.Select(a => a.Path));
        Assert.Equal(expected.Select(a => a.EffectiveAgeReductionYears), result.Rows.Select(a => a.AgeReduction));
        Assert.All(result.Rows, row => Assert.Equal(2, row.Decimals));
        Assert.Equal(Enumerable.Range(1, 8), result.Rows.Select(a => a.Rank));
        var json = JsonSerializer.Serialize(result.Rows);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("DateOfBirth", json);
        Assert.DoesNotContain("Biomarkers", json);
        Assert.Same(result, service.Get());
        data[0]!["Name"] = "Changed";
        Assert.NotSame(result, service.Get());
    }

    [Theory]
    [InlineData("[1,2,12,4]", false, 12)]
    [InlineData("[1,2,12,4]", true, 12)]
    [InlineData("[1,2,0]", false, null)]
    [InlineData("[1,2,-1]", true, null)]
    [InlineData("[1,2,null]", false, null)]
    [InlineData("[1,2,2.5]", false, null)]
    [InlineData("[1,2]", false, null)]
    [InlineData("bad json", true, null)]
    public void PreviousMonth_ParsesBothStorageFormatsAndRejectsUnknownPlacements(string value, bool encoded, int? expected)
    {
        var athlete = Athlete(1);
        athlete["Placements"] = encoded ? JsonValue.Create(value) : JsonNode.Parse(value);
        var service = new PublicStandingsService(new MutableAthleteSnapshot(new JsonArray(athlete)));
        Assert.Equal(expected, Assert.Single(service.Get().Rows).LastMonthRank);
    }

    [Fact]
    public void Cache_ExpiresAtUtcDayBoundary_AndDoesNotRetainOlderResults()
    {
        var clock = new MutableClock { Now = DateTimeOffset.Parse("2026-10-24T23:59:59Z") };
        var service = new PublicStandingsService(new MutableAthleteSnapshot(new JsonArray(Athlete(1))), clock);
        var before = service.Get();
        Assert.Same(before, service.Get());
        clock.Now = clock.Now.AddSeconds(1);
        var after = service.Get();
        Assert.NotSame(before, after);
        Assert.Same(after, service.Get());
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static JsonObject Athlete(int i) => new()
    {
        ["AthleteSlug"] = $"athlete_{i}", ["Name"] = $"Athlete {i}",
        ["DateOfBirth"] = new JsonObject { ["Year"] = 1980, ["Month"] = 1, ["Day"] = 1 },
        ["MediaContact"] = "secret@example.com", ["Placements"] = new JsonArray(1, 2, 12, 4),
        ["Biomarkers"] = new JsonArray(new JsonObject
        {
            ["Date"] = "2026-01-01", ["AlbGL"] = 45, ["CreatUmolL"] = 80, ["GluMmolL"] = 5,
            ["CrpMgL"] = 0.8, ["Wbc1000cellsuL"] = 5, ["LymPc"] = 32, ["McvFL"] = 90,
            ["RdwPc"] = 12.5, ["AlpUL"] = 55
        })
    };
}

[Collection(HttpTestCollections.ReadOnly)]
public sealed class PublicStandingsHttpTests(TestWebApplicationFactory factory)
{
    [Fact]
    public async Task Standings_ReturnsOnlyPublicPreviewFields_AndHonorsConditionalCaching()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/data/standings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Public);
        Assert.True(response.Headers.ETag?.IsWeak);
        var body = await response.Content.ReadAsStringAsync();
        var rows = JsonNode.Parse(body)!.AsArray();
        Assert.Equal(8, rows.Count);
        var source = factory.Services.GetRequiredService<IAthleteSnapshotProvider>();
        var expected = new PublicLeaderboardSnapshot(source.GetAthletesSnapshot(), DateTime.UtcNow.Date)
            .Select("/leaderboard", QueryCollection.Empty).Snapshot.Rows.Take(8).ToArray();
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.Equal(new[] { "ageReduction", "decimals", "lastMonthRank", "name", "path", "rank", "thumb" }, rows[i]!.AsObject().Select(p => p.Key).Order().ToArray());
            Assert.Equal(expected[i].AthletePath, rows[i]!["path"]!.GetValue<string>());
            Assert.Equal(expected[i].MetricDecimals, rows[i]!["decimals"]!.GetValue<int>());
        }
        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/data/standings");
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var cached = await client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, cached.StatusCode);
        Assert.Equal(response.Headers.ETag, cached.Headers.ETag);
        Assert.Equal(response.Headers.CacheControl!.ToString(), cached.Headers.CacheControl!.ToString());
        Assert.Equal("", await cached.Content.ReadAsStringAsync());
    }
}
