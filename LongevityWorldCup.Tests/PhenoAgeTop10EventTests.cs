using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PhenoAgeTop10EventTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly DatabaseManager _database;
    private EventDataService _events;
    private readonly DateTime _now = DateTime.UtcNow;
    private readonly Dictionary<string, string> _signatures = new();
    private readonly HashSet<string> _corrected = new();

    public PhenoAgeTop10EventTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _database = new DatabaseManager(dbPath: Path.Combine(factory.WorkingDirectory, $"pheno-{Guid.NewGuid():N}.db"));
        _events = ActivatorUtilities.CreateInstance<EventDataService>(factory.Services, _database);
    }

    private static BiologicalAgeLeaderboardEntry Row(string slug, int rank, double reduction = -20) =>
        new(slug, slug, "pheno", rank, 30, reduction);

    private void Sync(params BiologicalAgeLeaderboardEntry[] rows)
    {
        foreach (var row in rows) _signatures.TryAdd(row.Slug, "original");
        _events.SyncPhenoAgeTop10Events(rows, _signatures, _corrected, _now);
    }

    private IReadOnlyList<EventItem> Published() => _events.GetEvents(type: EventType.PhenoAgeTop10Change);

    [Fact]
    public void ExistingStandingsAreSilentAcrossRestartThenEntriesAndClimbsPublishOnce()
    {
        Sync(Row("existing", 6), Row("candidate", 11));
        Assert.Empty(Published());
        _events.Dispose();
        _events = ActivatorUtilities.CreateInstance<EventDataService>(_factory.Services, _database);
        Sync(Row("existing", 6), Row("candidate", 11));
        Assert.Empty(Published());

        _signatures["candidate"] = "new-result";
        Sync(Row("candidate", 6, -21), Row("existing", 7));
        var entry = Assert.Single(Published());
        Assert.Contains("slug[candidate] place[6]", entry.Text);
        Assert.Contains("prev[existing]", entry.Text);
        Assert.DoesNotContain("prevPlace", entry.Text);
        Assert.Contains(_events.GetPendingXEvents(), e => e.Id == entry.Id);
        Assert.Contains(_events.GetPendingThreadsEvents(), e => e.Id == entry.Id);
        Sync(Row("candidate", 6, -21), Row("existing", 7));
        Assert.Single(Published());

        _signatures["candidate"] = "newer-result";
        Sync(Row("candidate", 3, -23), Row("existing", 7));
        var climb = Assert.Single(Published(), e => e.Id != entry.Id);
        Assert.Contains("prevPlace[6]", climb.Text);
        Assert.Contains("place[3]", climb.Text);
        _events.Dispose();
        _events = ActivatorUtilities.CreateInstance<EventDataService>(_factory.Services, _database);
        Sync(Row("candidate", 3, -23), Row("existing", 7));
        Assert.Equal(2, Published().Count);
    }

    [Fact]
    public void PassiveMovesSilentCorrectionsAndNonQualifyingResultsOnlyRebase()
    {
        Sync(Row("passive", 11), Row("corrected", 12), Row("outside", 20), Row("same", 5));
        _corrected.Add("corrected");
        _signatures["corrected"] = "correction";
        _signatures["outside"] = "new-result";
        _signatures["same"] = "new-result";
        Sync(Row("passive", 10), Row("corrected", 6), Row("outside", 11), Row("same", 5));
        Assert.Empty(Published());
        _corrected.Clear();
        Sync(Row("passive", 10), Row("corrected", 6), Row("outside", 11), Row("same", 5));
        Assert.Empty(Published());

        _signatures["same"] = "worse-result";
        Sync(Row("passive", 10), Row("corrected", 6), Row("outside", 11), Row("same", 8));
        Assert.Empty(Published());
        Sync(Row("newcomer", 10), Row("passive", 11), Row("corrected", 6), Row("outside", 12), Row("same", 8));
        Assert.Contains("slug[newcomer]", Assert.Single(Published()).Text);
    }

    [Theory]
    [InlineData("", "entered the top 10 at 6th")]
    [InlineData("prevPlace[8]", "climbed from 8th to 6th")]
    public void MessagesAndSyndicationIdentifyPhenoAgeAndPreserveEventPlacement(string previous, string movement)
    {
        var raw = $"slug[alice] place[6] {previous} ageReduction[-20.4]";
        foreach (var text in new[]
        {
            XMessageBuilder.ForEventText(EventType.PhenoAgeTop10Change, raw, _ => "Alice"),
            ThreadsMessageBuilder.ForEventText(EventType.PhenoAgeTop10Change, raw, _ => "Alice"),
            SlackMessageBuilder.ForEventText(EventType.PhenoAgeTop10Change, raw, _ => "Alice")
        })
        {
            Assert.Contains(movement + " in Pheno Age", text);
            Assert.Contains("Age reduction: -20.4 years", text);
            Assert.Contains("/athlete/alice?ctx=pheno", text);
            Assert.DoesNotContain("Ultimate", text);
            Assert.DoesNotContain("Improvement", text);
        }
        var feed = EventFeedText.Describe(new EventItem("pheno", EventType.PhenoAgeTop10Change, raw, _now, 10, true), _ => "Alice");
        Assert.Contains(movement + " in Pheno Age", feed);
        Assert.DoesNotContain("slug[", feed);
    }

    [Fact]
    public void SimultaneousUltimateRankDoesNotSwallowPhenoSlackAnnouncement()
    {
        var text = SlackMessageBuilder.ForMergedGroup(
            [(EventType.NewRank, "slug[alice] rank[3]"),
             (EventType.PhenoAgeTop10Change, "slug[alice] place[6] prevPlace[8] ageReduction[-20.4]")],
            _ => "Alice");
        Assert.Contains("3rd", text);
        Assert.Contains("Ultimate League", text);
        Assert.Contains("climbed from 8th to 6th in Pheno Age", text);
    }

    public void Dispose()
    {
        _events.Dispose();
        _database.Dispose();
    }
}
