using System.Globalization;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AiSummaryTests(TestWebApplicationFactory factory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public void PersistedPageDatesAreAvailableImmediatelyAfterRestartIncludingUnicodeUrls()
    {
        var store = new ContentRevisionStore();
        var clock = new SummaryClock();
        const string path = "/athlete/élise";
        var key = "page:" + SitemapService.SiteBaseUrl + new Microsoft.AspNetCore.Http.PathString(path).ToUriComponent();
        store.Observe(key, "baseline", clock.GetUtcNow());
        clock.Advance(TimeSpan.FromMinutes(1));
        store.Observe(key, "changed", clock.GetUtcNow());
        using var freshness = ActivatorUtilities.CreateInstance<PublicContentFreshness>(factory.Services, store, clock);
        Assert.Equal(clock.GetUtcNow().UtcDateTime, freshness.GetLastModifiedUtc(path));
        Assert.Null(freshness.GetLastModifiedUtc("/play/proof-upload.html"));
    }

    [Fact]
    public void ClockViewDoesNotAcquireADailyTimestampWhenItsFactsHaveNotChanged()
    {
        var clock = new SummaryClock();
        var facts = new LeaderboardFactsService(new MutableAthleteSnapshot(Athletes()), new ContentRevisionStore(),
            factory.Services.GetRequiredService<EventDataService>(), clock);
        var before = facts.GetLeagueMarkdown("pheno");
        var crowd = facts.GetLeagueMarkdown("crowd");
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(before, facts.GetLeagueMarkdown("pheno"));
        Assert.NotEqual(crowd, facts.GetLeagueMarkdown("crowd")); // This score uses current chronological age.
    }

    [Fact]
    public void StableFactsSurviveCacheIntervalsAndRestartWhileLiveCrowdChangesAreImmediate()
    {
        var source = new MutableAthleteSnapshot(Athletes());
        var clock = new SummaryClock();
        var path = Path.Combine(factory.WorkingDirectory, "facts-revisions.json");
        var events = factory.Services.GetRequiredService<EventDataService>();
        var facts = new LeaderboardFactsService(source, new ContentRevisionStore(path), events, clock);
        var before = facts.GetLeaderboardMarkdown();
        var names = facts.GetAthleteNamesMarkdown();
        Assert.Null(before.LastModifiedUtc);
        Assert.Contains("facts_changed_at_utc: unknown", before.Markdown);
        Assert.DoesNotContain("generated_at_utc", before.Markdown);

        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(before, facts.GetLeaderboardMarkdown());
        facts = new LeaderboardFactsService(source, new ContentRevisionStore(path), events, clock);
        Assert.Equal(before, facts.GetLeaderboardMarkdown());

        source.Data[0]!["CrowdCount"] = 101;
        var after = facts.GetLeaderboardMarkdown();
        Assert.NotEqual(before.Markdown, after.Markdown);
        Assert.Equal(clock.GetUtcNow(), after.LastModifiedUtc);
        Assert.Equal(names, facts.GetAthleteNamesMarkdown());
        source.Data[0]!["PrivateEmail"] = "private-change@example.com";
        source.Data[0]!["GeneratedAt"] = clock.GetUtcNow().ToString("O");
        clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(after, facts.GetLeaderboardMarkdown());
        facts = new LeaderboardFactsService(source, new ContentRevisionStore(path), events, clock);
        Assert.Equal(after, facts.GetLeaderboardMarkdown());
    }

    [Fact]
    public void EveryAdvertisedViewMatchesTheExistingCompetitionFieldAndOrdering()
    {
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var facts = factory.Services.GetRequiredService<LeaderboardFactsService>();
        var snapshot = facts.Capture();
        var stats = PhenoStatsCalculator.BuildAll(snapshot.Athletes, DateTime.UtcNow.Date);
        Assert.Equal(SitemapService.PublicLeaguePaths.Order(), LeaderboardViewCatalog.Views.Where(v => v.Slug != "ultimate").Select(v => v.HtmlPath).Order());
        foreach (var view in LeaderboardViewCatalog.Views)
        {
            var rows = LeaderboardViewCatalog.SelectRows(view.HtmlPath, snapshot.Leaderboard.Rows, stats);
            Assert.Equal(athletes.GetLeagueSlugsInRankOrder(view.Slug), rows.Select(row => row.Slug));
            var document = facts.GetLeagueMarkdown(view.Slug)!;
            Assert.Contains(SitemapService.SiteBaseUrl + view.HtmlPath, document.Markdown);
            Assert.Contains(view.Rules, document.Markdown);
        }
    }

    [Fact]
    public void CrowdQualificationAndCountTiebreaksUseCurrentImageGuesses()
    {
        var data = Athletes();
        data[0]!["CrowdCount"] = 99;
        var facts = Facts(data);
        var crowd = facts.GetLeagueMarkdown("crowd")!.Markdown;
        Assert.DoesNotContain("[Alpha]", crowd);
        Assert.Contains("[Beta]", crowd);

        data[0]!["CrowdCount"] = 101;
        crowd = facts.GetLeagueMarkdown("crowd")!.Markdown;
        Assert.True(crowd.IndexOf("[Alpha]", StringComparison.Ordinal) < crowd.IndexOf("[Beta]", StringComparison.Ordinal));
        data[0]!["CrowdCount"] = 0;
        data[0]!["CrowdAge"] = null;
        var profile = facts.GetAthleteMarkdown("alpha")!.Markdown;
        Assert.Contains("Crowd age: Not available", profile);
        Assert.Contains("not qualified (100 guesses required)", profile);
        Assert.DoesNotContain("| Crowd Age League |", profile);
    }

    [Fact]
    public void AthleteHistorySeparatesTestDatesFromRecordedPublicAnnouncementsAndMissingResults()
    {
        var data = Athletes();
        using var database = new DatabaseManager(dbPath: Path.Combine(factory.WorkingDirectory, "summary-events.db"));
        using var events = ActivatorUtilities.CreateInstance<EventDataService>(factory.Services, database);
        var clock = new SummaryClock();
        Assert.Equal(0, events.SyncAcceptedResultEvents(data, clock.GetUtcNow().UtcDateTime));
        var newTest = data[0]!["Biomarkers"]![1]!.DeepClone();
        newTest["Date"] = "2026-08-12";
        data[0]!["Biomarkers"]!.AsArray().Add(newTest);
        Assert.Equal(1, events.SyncAcceptedResultEvents(data, clock.GetUtcNow().UtcDateTime));
        var facts = new LeaderboardFactsService(new MutableAthleteSnapshot(data), new ContentRevisionStore(), events, clock);
        var document = facts.GetAthleteMarkdown("ALPHA")!;
        Assert.Contains("source: https://longevityworldcup.com/athlete/alpha", document.Markdown);
        Assert.Contains("| 2024-01-01 | Not available |", document.Markdown);
        Assert.Contains("| 2026-08-12 | 2026-09-16T12:00:00.0000000Z |", document.Markdown);
        Assert.Contains("| 2023-01-01 | Not available | Not available | Not available |", document.Markdown);
        Assert.DoesNotContain("private@example.com", document.Markdown);
        Assert.DoesNotContain("NaN", document.Markdown);
        Assert.DoesNotContain("Infinity", document.Markdown);
        Assert.Null(facts.GetAthleteMarkdown("missing-athlete"));
        Assert.Null(facts.GetLeagueMarkdown("missing-league"));
        Assert.Contains("Current leaders are not declared season winners", facts.GetLeaderboardMarkdown().Markdown);
    }

    [Fact]
    public void ImprovementSummaryUsesLatestMinusWorstRatherThanFirst()
    {
        var data = Athletes();
        var athlete = data[0]!.AsObject();
        var panel = athlete["Biomarkers"]![1]!.DeepClone();
        panel["Date"] = "2025-01-01";
        panel["CrpMgL"] = 12d;
        athlete["Biomarkers"]!.AsArray().Add(panel);
        panel = athlete["Biomarkers"]![1]!.DeepClone();
        panel["Date"] = "2026-01-01";
        athlete["Biomarkers"]!.AsArray().Add(panel);
        var stats = PhenoStatsCalculator.Compute(athlete, new SummaryClock().GetUtcNow().UtcDateTime);
        Assert.NotEqual(stats.PhenoAgeDiffFromBaseline, stats.PhenoAgeImprovementFromWorst);
        var document = Facts(data).GetLeagueMarkdown("improvement")!.Markdown;
        var expected = stats.PhenoAgeImprovementFromWorst!.Value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
        Assert.Contains($"[Alpha](https://longevityworldcup.com/athlete/alpha) | Amateur | {expected} |", document);
        Assert.DoesNotContain("[Beta]", document); // One result does not qualify.
    }

    private LeaderboardFactsService Facts(JsonArray data) => new(new MutableAthleteSnapshot(data), new ContentRevisionStore(),
        factory.Services.GetRequiredService<EventDataService>(), new SummaryClock());

    internal static JsonArray Athletes() => JsonNode.Parse("""
        [
          {"AthleteSlug":"alpha","Name":"Alpha","DateOfBirth":{"Year":1986,"Month":1,"Day":1},
           "Division":"Men's","Flag":"Hungary","CrowdAge":30.0,"CrowdCount":100,"MediaContact":"private@example.com",
           "Biomarkers":[{"Date":"2023-01-01","AlbGL":42},
             {"Date":"2024-01-01","AlbGL":45,"CreatUmolL":80,"GluMmolL":5,"CrpMgL":0.8,"Wbc1000cellsuL":5,"LymPc":32,"McvFL":90,"RdwPc":12.5,"AlpUL":55}]},
          {"AthleteSlug":"beta","Name":"Beta","DateOfBirth":{"Year":1986,"Month":1,"Day":1},
           "Division":"Women's","Flag":"United States","CrowdAge":30.0,"CrowdCount":100,"Biomarkers":[]}
        ]
        """)!.AsArray();
}

internal sealed class MutableAthleteSnapshot(JsonArray data) : IAthleteSnapshotProvider
{
    public JsonArray Data { get; set; } = data;
    public JsonArray GetAthletesSnapshot() => Data.DeepClone().AsArray();
}

internal sealed class SummaryClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan interval) => _now += interval;
}
