using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SilentAthleteCorrectionTests
{
    [Fact]
    public void CorrectionAndRestartAreSilentWhileTheNextRealResultStillCreatesEvents()
    {
        using var workspace = new Workspace();
        HashSet<string> original;
        using (var baseline = workspace.Factory())
        {
            original = EventIds(baseline);
            Assert.Equal(2, Placement(baseline));
        }

        workspace.WriteCorrected();
        using (var corrected = workspace.Factory())
        {
            Assert.True(original.SetEquals(EventIds(corrected)));
            Assert.Equal(1, Placement(corrected));
            Assert.Equal(0, Pending(corrected));
            Assert.Equal(workspace.CorrectedSignature, Scalar(corrected, "SELECT TestSig FROM Athletes WHERE Key='correction_target';"));
            Assert.Equal(1L, Scalar(corrected, "SELECT COUNT(*) FROM AcceptedAthleteResults WHERE AthleteSlug='correction_target' AND ResultDate='2025-08-31';"));
        }

        using (var restarted = workspace.Factory())
            Assert.True(original.SetEquals(EventIds(restarted)));

        workspace.WriteRealResult();
        using var next = workspace.Factory();
        var events = next.Services.GetRequiredService<EventDataService>();
        Assert.Contains(events.GetEvents(EventType.TestResultAccepted), e => e.Text == "slug[correction_target] date[2025-09-01]");
        Assert.Contains(events.GetEvents(EventType.BiologicalAgeImproved), e => e.Text.StartsWith("slug[correction_target]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FileWatcherCorrectionRebaselinesAwardsWithoutCreatingEvents()
    {
        using var workspace = new Workspace();
        using var factory = workspace.Factory();
        var original = EventIds(factory);
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe()
        {
            var athlete = athletes.GetAthletesSnapshot().OfType<JsonObject>().Single(a => a["AthleteSlug"]!.GetValue<string>() == "correction_target");
            if (AthleteDataService.ComputeBiomarkerSignature(athlete) == workspace.CorrectedSignature)
                reloaded.TrySetResult();
        }
        athletes.AthletesChanged += Observe;
        try
        {
            workspace.WriteCorrected();
            await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { athletes.AthletesChanged -= Observe; }
        factory.Services.GetRequiredService<BadgeDataService>().ComputeAndPersistAwards();
        Assert.True(original.SetEquals(EventIds(factory)));
        Assert.Equal(1, Placement(factory));
        Assert.Equal(0, Pending(factory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenuineBadgeChangesAlongsideAPendingCorrectionStillCreateEvents(bool correctionAlreadyPending)
    {
        using var workspace = new Workspace();
        HashSet<string> original;
        using (var baseline = workspace.Factory()) original = EventIds(baseline);
        workspace.WriteCorrected();
        if (correctionAlreadyPending)
        {
            using var interrupted = workspace.Factory(refreshBadges: false);
            Assert.True(original.SetEquals(EventIds(interrupted)));
            Assert.Equal(1, Pending(interrupted));
        }
        workspace.WritePeerResult();
        using var factory = workspace.Factory();
        var events = factory.Services.GetRequiredService<EventDataService>();
        var addedBadges = events.GetEvents(EventType.BadgeAward).Where(e => !original.Contains(e.Id)).ToList();
        Assert.Contains(addedBadges, e => e.Text.Contains("peer", StringComparison.Ordinal) && e.Text.Contains("submissions", StringComparison.Ordinal));
        Assert.DoesNotContain(addedBadges, e => e.Text.StartsWith("slug[correction_target]", StringComparison.Ordinal));
        Assert.Contains(events.GetEvents(EventType.TestResultAccepted), e => e.Text == "slug[peer] date[2025-09-01]");
        Assert.DoesNotContain(events.GetEvents(EventType.TestResultAccepted), e => e.Text == "slug[correction_target] date[2025-08-31]");
        Assert.Equal(0, Pending(factory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestartBeforeBadgeRecomputeRetainsTheSilentCorrection(bool proCorrection)
    {
        using var workspace = new Workspace(proCorrection);
        HashSet<string> original;
        using (var baseline = workspace.Factory()) original = EventIds(baseline);
        workspace.WriteCorrected();
        using (var interrupted = workspace.Factory(refreshBadges: false))
        {
            Assert.True(original.SetEquals(EventIds(interrupted)));
            Assert.Equal(1, Pending(interrupted));
        }
        if (proCorrection) workspace.ResetProState();
        using var restarted = workspace.Factory();
        Assert.True(original.SetEquals(EventIds(restarted)));
        Assert.Equal(0, Pending(restarted));
    }

    [Fact]
    public void FailedBadgePersistenceDoesNotConsumeTheSilentCorrection()
    {
        using var workspace = new Workspace();
        HashSet<string> original;
        using (var baseline = workspace.Factory()) original = EventIds(baseline);
        workspace.WriteCorrected();
        using var factory = workspace.Factory(refreshBadges: false);
        Assert.Equal(1, Pending(factory));
        var awardsBefore = Scalar(factory, "SELECT COUNT(*) FROM BadgeAwards;");
        Execute(factory, "CREATE TRIGGER FailCorrectionAwards BEFORE INSERT ON BadgeAwards BEGIN SELECT RAISE(ABORT, 'Simulated badge persistence failure'); END;");
        Assert.Throws<SqliteException>(() => factory.Services.GetRequiredService<BadgeDataService>());
        Assert.Equal(1, Pending(factory));
        Assert.Equal(awardsBefore, Scalar(factory, "SELECT COUNT(*) FROM BadgeAwards;"));
        Assert.True(original.SetEquals(EventIds(factory)));
        Execute(factory, "DROP TRIGGER FailCorrectionAwards;");
        factory.Services.GetRequiredService<BadgeDataService>();
        Assert.Equal(0, Pending(factory));
        Assert.True(original.SetEquals(EventIds(factory)));
    }

    [Fact]
    public void ApprovedManifestIsBoundToExactSignaturesAndDoesNotMuteOtherUpdates()
    {
        using var stream = typeof(AthleteDataCorrections).Assembly.GetManifestResourceStream("LongevityWorldCup.Website.DataCorrections.BiomarkerAudit20261018.json")!;
        var entries = JsonSerializer.Deserialize<AthleteDataCorrections.Correction[]>(stream)!;
        Assert.Equal(11, entries.Length);
        Assert.Equal(7, entries.Sum(e => e.Dates.Length));
        Assert.Equal(16, entries.Sum(e => e.Fields.Length));
        var catalog = new AthleteDataCorrections();
        foreach (var entry in entries)
        {
            Assert.Matches("^[0-9a-f]{64}$", entry.PreviousSignature);
            Assert.Matches("^[0-9a-f]{64}$", entry.CorrectedSignature);
            Assert.NotEqual(entry.PreviousSignature, entry.CorrectedSignature);
            Assert.True(catalog.IsSilentTransition(entry.AthleteSlug, entry.PreviousSignature, entry.CorrectedSignature));
            Assert.False(catalog.IsSilentTransition(entry.AthleteSlug, null, entry.CorrectedSignature));
            Assert.False(catalog.IsSilentTransition(entry.AthleteSlug, entry.PreviousSignature, new string('0', 64)));
            Assert.False(catalog.IsSilentTransition("another_athlete", entry.PreviousSignature, entry.CorrectedSignature));
        }
    }

    private static HashSet<string> EventIds(TestWebApplicationFactory factory) => factory.Services.GetRequiredService<DatabaseManager>().Run(sqlite =>
    {
        using var command = sqlite.CreateCommand();
        command.CommandText = "SELECT Id FROM Events;";
        using var reader = command.ExecuteReader();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    });
    private static object? Scalar(TestWebApplicationFactory factory, string sql) => factory.Services.GetRequiredService<DatabaseManager>().Run(sqlite =>
    {
        using var command = sqlite.CreateCommand();command.CommandText = sql;return command.ExecuteScalar();
    });
    private static void Execute(TestWebApplicationFactory factory, string sql) => Scalar(factory, sql);
    private static int Placement(TestWebApplicationFactory factory) => Convert.ToInt32(Scalar(factory, "SELECT CurrentPlacement FROM Athletes WHERE Key='correction_target';"));
    private static int Pending(TestWebApplicationFactory factory) => Convert.ToInt32(Scalar(factory, "SELECT COUNT(*) FROM AthleteCorrectionBaselines WHERE BadgeBaselinePending=1;"));

    private sealed class Workspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "lwc-silent-corrections-" + Guid.NewGuid().ToString("N"));
        private readonly JsonObject _before;
        private readonly JsonObject _corrected;
        private readonly AthleteDataCorrections _catalog;
        internal string CorrectedSignature => AthleteDataService.ComputeBiomarkerSignature(_corrected);

        internal Workspace(bool proCorrection = false)
        {
            _before = Athlete("correction_target", Result("2025-08-20", 1));
            _corrected = Athlete("correction_target", Result("2025-08-31", 50));
            if (proCorrection)
            {
                var panel = _corrected["Biomarkers"]![0]!.AsObject();
                foreach (var (key, value) in new Dictionary<string, double>
                {
                    ["UreaMmolL"] = 4.8,["CholesterolMmolL"] = 4.5,["CystatinCMgL"] = 0.8,["Hba1cMmolMol"] = 32,
                    ["GgtUL"] = 20,["Rbc10e12L"] = 5,["MonocytePc"] = 6,["NeutrophilPc"] = 40,
                    ["AltUL"] = 20,["ShbgNmolL"] = 30,["VitaminDNmolL"] = 80,["MchPg"] = 30,["ApoA1GL"] = 1.5
                }) panel[key] = value;
            }
            var oldPanel = _before["Biomarkers"]![0]!.AsObject();
            var correctedPanel = _corrected["Biomarkers"]![0]!.AsObject();
            var fields = correctedPanel.Where(p => !JsonNode.DeepEquals(p.Value, oldPanel[p.Key]))
                .Select(p => new AthleteDataCorrections.FieldCorrection("2025-08-31", p.Key, oldPanel[p.Key]?.DeepClone())).ToArray();
            _catalog = new AthleteDataCorrections([new("correction_target", AthleteDataService.ComputeBiomarkerSignature(_before), CorrectedSignature, [new("2025-08-20", "2025-08-31")], fields)]);
            Write(_before);
            Write(Athlete("peer", Result("2025-08-20", 30)));
        }
        internal void WriteCorrected() => Write(_corrected);
        internal void ResetProState()
        {
            using var sqlite = new SqliteConnection("Data Source=" + Path.Combine(_root, "test.db") + ";Pooling=False");
            sqlite.Open();
            using var command = sqlite.CreateCommand();
            command.CommandText = "UPDATE Athletes SET HadBortz=0 WHERE Key='correction_target';";
            command.ExecuteNonQuery();
        }
        internal void WriteRealResult() => Write(Athlete("correction_target", Result("2025-08-31", 50), Result("2025-09-01", 60)));
        internal void WritePeerResult() => Write(Athlete("peer", Result("2025-08-20", 30), Result("2025-09-01", 30)));
        private void Write(JsonObject athlete)
        {
            var folder = Path.Combine(_root, "wwwroot", "athletes", athlete["AthleteSlug"]!.GetValue<string>());
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "athlete.json"), athlete.ToJsonString());
        }
        internal TestWebApplicationFactory Factory(bool refreshBadges = true) => new(builder =>
        {
            builder.UseWebRoot(Path.Combine(_root, "wwwroot"));
            builder.UseSetting("EnableStartupBadgeRefresh", refreshBadges ? "true" : "false");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DatabaseManager>();
                services.AddSingleton(_ => new DatabaseManager(dbPath: Path.Combine(_root, "test.db")));
                services.RemoveAll<AthleteDataCorrections>();
                services.AddSingleton(_catalog);
            });
        });
        private static JsonObject Athlete(string slug, params JsonObject[] panels) => JsonNode.Parse(new JsonObject
        {
            ["AthleteSlug"] = slug,["Name"] = slug,["Division"] = "Open",["Flag"] = "Earth",
            ["DateOfBirth"] = new JsonObject { ["Year"] = 1980,["Month"] = 1,["Day"] = 2 },
            ["Biomarkers"] = new JsonArray(panels.Select(p => p.DeepClone()).ToArray())
        }.ToJsonString())!.AsObject();
        private static JsonObject Result(string date, double lymphocytes) => new()
        {
            ["Date"] = date,["AlbGL"] = 45,["CreatUmolL"] = 95,["GluMmolL"] = 4,["CrpMgL"] = 1,
            ["LymPc"] = lymphocytes,["McvFL"] = 86.5,["RdwPc"] = 12.5,["AlpUL"] = 50,["Wbc1000cellsuL"] = 5.48
        };
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
