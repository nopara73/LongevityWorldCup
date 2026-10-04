using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BloodVsBirthdaysTests
{
    [Fact]
    public void DailyAnswers_UseCanonicalClockAndBirthdayAtTest_NotCurrentAge()
    {
        var data = Athletes();
        var puzzle = BloodVsBirthdaysService.BuildPuzzle(data, new DateOnly(2026, 10, 4), DateTimeOffset.Parse("2026-10-04T00:00:00Z"), Version);
        Assert.Equal(5, puzzle.Rounds.Length);
        var people = puzzle.Rounds.SelectMany(r => new[] { r.Left, r.Right }).ToArray();
        Assert.Equal(10, people.Select(p => p.Slug).Distinct().Count());
        foreach (var person in people)
        {
            var source = data.OfType<JsonObject>().Single(a => a["AthleteSlug"]!.GetValue<string>() == person.Slug);
            var canonical = PhenoStatsCalculator.Compute(source, new DateTime(2026, 10, 4));
            Assert.Equal(canonical.LowestBortzAge!.Value, person.BortzAge, 10);
            Assert.Equal(-canonical.BortzAgeReduction!.Value, person.ReductionYears, 10);
            Assert.Equal((canonical.LowestBortzAgeDateUtc!.Value - canonical.DobUtc!.Value).TotalDays / 365.2425, person.ChronologicalAge, 10);
            Assert.NotEqual(canonical.ChronoAge, person.ChronologicalAge);
            Assert.EndsWith("?v=test", person.PortraitUrl);
            Assert.All(person.ProofUrls, url => Assert.EndsWith("?v=test", url));
        }
        foreach (var round in puzzle.Rounds)
        {
            Assert.True(Math.Abs(round.Left.ReductionYears - round.Right.ReductionYears) >= 0.3);
            Assert.Equal(round.Left.ReductionYears > round.Right.ReductionYears ? round.Left.Slug : round.Right.Slug, round.WinnerSlug);
        }
    }

    [Fact]
    public void DailyDraw_IsStableWhenSourceOrderChanges_AndChangesWithDay()
    {
        var data = Athletes();
        var date = new DateOnly(2026, 10, 4);
        var a = BloodVsBirthdaysService.BuildPuzzle(data, date, DateTimeOffset.MinValue, Version);
        var reordered = new JsonArray(data.Reverse().Select(a => a!.DeepClone()).ToArray());
        var b = BloodVsBirthdaysService.BuildPuzzle(reordered, date, DateTimeOffset.MinValue, Version);
        var next = BloodVsBirthdaysService.BuildPuzzle(data, date.AddDays(1), DateTimeOffset.MinValue, Version);
        Assert.Equal(JsonSerializer.Serialize(a), JsonSerializer.Serialize(b));
        Assert.NotEqual(a.Id, next.Id);
        Assert.NotEqual(string.Join(',', a.Rounds.Select(r => r.Left.Slug)), string.Join(',', next.Rounds.Select(r => r.Left.Slug)));
    }

    [Fact]
    public void Snapshot_SurvivesDataEditsAndRestart_AndRollsAtSingaporeMidnight()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LwcBirthdayTests", Guid.NewGuid().ToString("N"));
        var clock = new GameClock(DateTimeOffset.Parse("2026-10-04T15:59:59Z"));
        var source = new GameAthletes(Athletes());
        try
        {
            var service = new BloodVsBirthdaysService(source, clock, directory, Version);
            var first = service.GetToday();
            Assert.Equal("2026-10-04", first.Puzzle.Day);
            Assert.Equal(DateTimeOffset.Parse("2026-10-04T16:00:00Z"), first.NextPuzzleAtUtc);
            source.Data = new JsonArray();
            var restarted = new BloodVsBirthdaysService(source, clock, directory, Version).GetToday();
            Assert.Equal(JsonSerializer.Serialize(first.Puzzle), JsonSerializer.Serialize(restarted.Puzzle));
            source.Data = Athletes();
            clock.Now = first.NextPuzzleAtUtc;
            var tomorrow = service.GetToday();
            Assert.Equal("2026-10-05", tomorrow.Puzzle.Day);
            Assert.NotEqual(first.Puzzle.Id, tomorrow.Puzzle.Id);
            Assert.Equal(2, Directory.GetFiles(directory, "*.json").Length);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void InvalidSnapshot_IsNeverRegeneratedOrCachedAsValid()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LwcBirthdayTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "2026-10-04.json");
            File.WriteAllText(path, "{\"version\":2,\"day\":\"2026-10-04\",\"timeZone\":\"Asia/Singapore\",\"rounds\":[]}");
            var service = new BloodVsBirthdaysService(new GameAthletes(Athletes()), new GameClock(DateTimeOffset.Parse("2026-10-04T01:00:00Z")), directory, Version);
            Assert.Throws<InvalidDataException>(() => service.GetToday());
            Assert.Throws<InvalidDataException>(() => service.GetToday());
            Assert.Contains("\"version\":2", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void MissingEvidence_IncompletePanelsAndFutureTests_CannotBecomeMatchups()
    {
        var data = Athletes();
        var absentProofs = (JsonObject)data[0]!.DeepClone(); absentProofs["AthleteSlug"] = "no_proofs"; absentProofs["Proofs"] = new JsonArray(); data.Add(absentProofs);
        var incomplete = (JsonObject)data[0]!.DeepClone(); incomplete["AthleteSlug"] = "incomplete"; incomplete["Biomarkers"]![0]!.AsObject().Remove("ApoA1GL"); data.Add(incomplete);
        var future = (JsonObject)data[0]!.DeepClone(); future["AthleteSlug"] = "future"; future["Biomarkers"]![0]!["Date"] = "2027-01-01"; data.Add(future);
        for (var offset = 0; offset < 7; offset++)
        {
            var puzzle = BloodVsBirthdaysService.BuildPuzzle(data, new DateOnly(2026, 10, 4).AddDays(offset), DateTimeOffset.MinValue, Version);
            Assert.DoesNotContain(puzzle.Rounds.SelectMany(r => new[] { r.Left.Slug, r.Right.Slug }), s => s is "no_proofs" or "incomplete" or "future");
        }
    }

    internal static JsonArray Athletes() => new(Enumerable.Range(0, 28).Select(i => (JsonNode)new JsonObject
    {
        ["AthleteSlug"] = "test_athlete_" + i, ["Name"] = "Test Athlete " + i,
        ["DateOfBirth"] = new JsonObject { ["Year"] = 1940 + i, ["Month"] = 1, ["Day"] = 1 },
        ["ProfilePic"] = "/assets/content-images/play-athlete-placeholder.webp",
        ["Proofs"] = new JsonArray("/athletes/test_athlete_" + i + "/proof_1.webp"),
        ["Biomarkers"] = new JsonArray(new JsonObject
        {
            ["Date"] = "2026-04-01", ["AlbGL"] = 50d, ["CreatUmolL"] = 75d,
            ["GluMmolL"] = 4.66, ["CrpMgL"] = 0.1 + i * 0.025, ["LymPc"] = 38d,
            ["McvFL"] = 89d, ["RdwPc"] = 12.5, ["AlpUL"] = 55d, ["Wbc1000cellsuL"] = 5d,
            ["NeutrophilPc"] = 52d, ["MonocytePc"] = 6d, ["Rbc10e12L"] = 5.0,
            ["MchPg"] = 28d, ["UreaMmolL"] = 5.5, ["CystatinCMgL"] = 0.9,
            ["Hba1cMmolMol"] = 28 + i * 0.15, ["CholesterolMmolL"] = 4.0,
            ["ApoA1GL"] = 1.85, ["AltUL"] = 29d, ["GgtUL"] = 22d,
            ["ShbgNmolL"] = 42d, ["VitaminDNmolL"] = 112.6
        })
    }).ToArray());

    private static string Version(string value) => value + "?v=test";
    internal sealed class GameAthletes(JsonArray data) : IAthleteSnapshotProvider
    {
        public JsonArray Data { get; set; } = data;
        public JsonArray GetAthletesSnapshot() => (JsonArray)Data.DeepClone();
    }
    private sealed class GameClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
