using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Business.IndexNow;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class IndexNowContentTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 16, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "IndexNowContentTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void PublishedProfileOnlyEventsDoNotNotifySharedAnnouncements()
    {
        var athletes = Athletes();
        var before = Build(athletes);
        foreach (var type in new[] { EventType.TestResultAccepted, EventType.SeasonFinalResult })
        {
            var after = Build(athletes, [new("accepted", type, "slug[alpha] published a result", Now, 5, true)]);
            Assert.NotEqual(before[Url("/athlete/alpha")], after[Url("/athlete/alpha")]);
            Assert.Equal(before[Url("/athlete/beta")], after[Url("/athlete/beta")]);
            Assert.Equal(before[Url("/")], after[Url("/")]);
            Assert.Equal(before[Url("/events")], after[Url("/events")]);
        }
    }

    [Fact]
    public void UnpublishedFutureAndSocialOnlyEventsDoNotAffectPublicContent()
    {
        var athletes = Athletes();
        Assert.Equal(Build(athletes), Build(athletes,
        [
            new("hidden", EventType.CustomEvent, "unpublished", Now, 5, false),
            new("future", EventType.CustomEvent, "scheduled", Now.AddDays(1), 5, true)
        ]));
    }

    [Fact]
    public void SharedHighlightUpdatesOnlyItsDestinations()
    {
        var athletes = Athletes();
        var before = Build(athletes);
        var after = Build(athletes, [new("public", EventType.BiologicalAgeImproved, "slug[alpha] improved", Now, 5, true)]);
        Assert.NotEqual(before[Url("/")], after[Url("/")]);
        Assert.NotEqual(before[Url("/events")], after[Url("/events")]);
        Assert.NotEqual(before[Url("/athlete/alpha")], after[Url("/athlete/alpha")]);
        Assert.Equal(before[Url("/leaderboard")], after[Url("/leaderboard")]);
        Assert.Equal(before[Url("/about")], after[Url("/about")]);
    }

    [Theory]
    [InlineData(EventType.BadgeAward, "slug[alpha] badge[Perfect application] place[1] cat[Global]")]
    [InlineData(EventType.NewRank, "slug[alpha] rank[42]")]
    public void EventsExcludedByMainFeedOnlyInvalidateTheProfile(EventType type, string text)
    {
        var athletes = Athletes();
        var before = Build(athletes);
        var after = Build(athletes, [new("profile", type, text, Now, 5, true)]);
        Assert.Equal([Url("/athlete/alpha")], Changed(before, after));
    }

    [Fact]
    public void BiographyChangesDoNotInvalidateOtherAthletesOrRankings()
    {
        var athletes = Athletes();
        var before = Build(athletes);
        athletes[0]!["Why"] = "Updated public biography";
        var after = Build(athletes);
        Assert.Equal([Url("/athlete/alpha")], Changed(before, after));
    }

    [Fact]
    public void MembershipChangesNotifyOldAndNewFlagAndLeaguePages()
    {
        var athletes = Athletes();
        var before = Build(athletes);
        athletes[0]!["Flag"] = "United States";
        athletes[0]!["Division"] = "Women's";
        var after = Build(athletes);
        Assert.Contains(Url("/flag/hungary"), before.Keys);
        Assert.DoesNotContain(Url("/flag/hungary"), after.Keys);
        Assert.Contains(Url("/league/mens"), Changed(before, after));
        Assert.Contains(Url("/league/womens"), Changed(before, after));
        Assert.Contains(Url("/flag/united-states"), Changed(before, after));
        Assert.Equal(before[Url("/about")], after[Url("/about")]);
    }

    [Fact]
    public void PrivateFieldsTimestampChangesAndResultReorderingDoNotCauseNotifications()
    {
        var athletes = Athletes();
        var before = Build(athletes);
        athletes[0]!["PrivateEmail"] = "do-not-publish@example.com";
        athletes[0]!["MediaContact"] = "another-private-address@example.com";
        athletes[0]!["GeneratedAt"] = "2099-01-01";
        athletes[0]!["Proofs"] = new JsonArray("/athletes/alpha/proof_1.webp?v=99999");
        var biomarkers = athletes[0]!["Biomarkers"]!.AsArray();
        athletes[0]!["Biomarkers"] = new JsonArray(biomarkers.Reverse().Select(node => node!.DeepClone()).ToArray());
        Assert.Equal(before, Build(athletes));
    }

    [Fact]
    public void ResultCorrectionsAndProfilePhotosChangeTheProfile()
    {
        var athletes = Athletes();
        var before = Build(athletes);
        athletes[0]!["Biomarkers"]![0]!["AlbGL"] = 45;
        Assert.NotEqual(before[Url("/athlete/alpha")], Build(athletes)[Url("/athlete/alpha")]);
        athletes = Athletes();
        athletes[0]!["ProfileImageId"] = "new-content-hash";
        var after = Build(athletes);
        Assert.NotEqual(before[Url("/athlete/alpha")], after[Url("/athlete/alpha")]);
        Assert.NotEqual(before[Url("/flag/hungary")], after[Url("/flag/hungary")]);
        Assert.Equal(before[Url("/flag/united-states")], after[Url("/flag/united-states")]);
    }

    [Fact]
    public void CrowdCountTiebreakChangesNotifyBothProfilesAndCrowdLeagueWithinSameCountBucket()
    {
        var athletes = Athletes();
        athletes[0]!["CrowdAge"] = 30;
        athletes[1]!["CrowdAge"] = 30;
        athletes[0]!["CrowdCount"] = 101;
        athletes[1]!["CrowdCount"] = 102;
        var before = Build(athletes);
        athletes[0]!["CrowdCount"] = 103;
        var after = Build(athletes);
        Assert.NotEqual(before[Url("/league/crowd")], after[Url("/league/crowd")]);
        Assert.NotEqual(before[Url("/athlete/alpha")], after[Url("/athlete/alpha")]);
        Assert.NotEqual(before[Url("/athlete/beta")], after[Url("/athlete/beta")]);
        Assert.Equal(before[Url("/leaderboard")], after[Url("/leaderboard")]);
        Assert.Equal(before[Url("/flag/hungary")], after[Url("/flag/hungary")]);
    }

    [Fact]
    public void RemovedAthleteAndFlagDisappearFromCurrentEligibleCatalog()
    {
        var athletes = Athletes();
        var before = Build(athletes);
        athletes.RemoveAt(0);
        var after = Build(athletes);
        Assert.Contains(Url("/athlete/alpha"), before.Keys);
        Assert.DoesNotContain(Url("/athlete/alpha"), after.Keys);
        Assert.DoesNotContain(Url("/flag/hungary"), after.Keys);
        Assert.All(after.Keys, url => Assert.True(IndexNowContentSnapshot.IsCanonicalUrl(url)));
    }

    [Fact]
    public void DeployedPageAndNestedPartialHashesIgnoreTimestampsAndUnrelatedFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "misc-pages"));
        Directory.CreateDirectory(Path.Combine(_root, "partials"));
        File.WriteAllText(Path.Combine(_root, "misc-pages", "about.html"), "<body><!--ABOUT-CONTENT--></body>");
        File.WriteAllText(Path.Combine(_root, "partials", "about-content.html"), "About LWC");
        File.WriteAllText(Path.Combine(_root, "index.html"), "Home");
        var manifest = Path.Combine(_root, "manifest.txt");
        File.WriteAllText(manifest, "html|HtmlInjectionMiddleware.cs|same-code\nchallenge|Challenge.cs|old-code");
        var pages = new IndexNowPageContent(new IndexNowTests.TestEnvironment(_root), manifest);
        var before = pages.Build(["/", "/about"], default);
        File.SetLastWriteTimeUtc(Path.Combine(_root, "index.html"), DateTime.UtcNow.AddDays(1));
        File.WriteAllText(Path.Combine(_root, "unrelated.css"), "not a public document change");
        File.WriteAllText(manifest, "html|HtmlInjectionMiddleware.cs|same-code\nchallenge|Challenge.cs|new-code");
        Assert.Equal(before, pages.Build(["/", "/about"], default));
        File.WriteAllText(Path.Combine(_root, "partials", "about-content.html"), "Updated mission");
        var after = pages.Build(["/", "/about"], default);
        Assert.Equal(before["/"], after["/"]);
        Assert.NotEqual(before["/about"], after["/about"]);
        File.WriteAllText(manifest, "html|HtmlInjectionMiddleware.cs|changed-rendering");
        Assert.NotEqual(after["/"], pages.Build(["/"], default)["/"]);
    }

    [Fact]
    public void ProofHashUsesBytesInsteadOfCacheVersionAndRejectsArbitraryPaths()
    {
        Directory.CreateDirectory(Path.Combine(_root, "athletes", "alpha"));
        var path = Path.Combine(_root, "athletes", "alpha", "proof_1.webp");
        File.WriteAllText(path, "original bytes");
        var pages = new IndexNowPageContent(new IndexNowTests.TestEnvironment(_root));
        var before = pages.HashProofs(new JsonArray("/athletes/alpha/proof_1.webp?v=1"));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(1));
        Assert.Equal(before, pages.HashProofs(new JsonArray("/athletes/alpha/proof_1.webp?v=2")));
        File.WriteAllText(path, "corrected bytes");
        Assert.NotEqual(before, pages.HashProofs(new JsonArray("/athletes/alpha/proof_1.webp?v=3")));
        Assert.Equal(pages.HashProofs([]), pages.HashProofs(new JsonArray("https://evil.example/proof", "/athletes/../config.json")));
    }

    private static Dictionary<string, string> Build(JsonArray athletes, IReadOnlyList<EventItem>? events = null)
    {
        var ranked = new JsonArray(athletes.OfType<JsonObject>().Select(a => (JsonNode)new JsonObject
        {
            ["AthleteSlug"] = a["AthleteSlug"]!.DeepClone(), ["Name"] = a["Name"]!.DeepClone(),
            ["ChronologicalAge"] = 40, ["LowestPhenoAge"] = 30, ["AgeDifference"] = -10
        }).ToArray());
        var leaderboard = LeaderboardSnapshotBuilder.Build(ranked, athletes);
        var paths = SitemapService.StaticRoutes.Select(r => r.Path).Concat(SitemapService.PublicLeaguePaths)
            .Concat(leaderboard.Rows.Select(r => r.AthletePath))
            .Concat(FlagRouteCatalog.BuildRoutes(leaderboard.Rows.Select(r => r.Flag)).Select(f => f.Path));
        return IndexNowContentSnapshot.Build(athletes, leaderboard, events ?? [], paths.ToDictionary(p => p, _ => "template"), null, Now).ToDictionary();
    }

    private static JsonArray Athletes() => JsonNode.Parse("""
        [
          {"AthleteSlug":"alpha","Name":"Alpha","DateOfBirth":{"Year":1986,"Month":1,"Day":1},
           "Division":"Men's","Flag":"Hungary","Why":"Longevity","ProfileImageId":"portrait-a",
           "Proofs":["/athletes/alpha/proof_1.webp?v=1"],"MediaContact":"private@example.com",
           "Biomarkers":[{"Date":"2025-01-01","AlbGL":42},{"Date":"2025-02-01","AlbGL":43}]},
          {"AthleteSlug":"beta","Name":"Beta","DateOfBirth":{"Year":1986,"Month":1,"Day":1},
           "Division":"Women's","Flag":"United States","Why":"Health","ProfileImageId":"portrait-b","Biomarkers":[]}
        ]
        """)!.AsArray();

    private static string Url(string path) => IndexNowContentSnapshot.SiteBaseUrl + path;
    private static string[] Changed(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
        after.Keys.Where(url => !before.TryGetValue(url, out var hash) || after[url] != hash).Order(StringComparer.Ordinal).ToArray();

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
