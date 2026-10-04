using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

/// <summary>A daily game snapshot, independent of competition ranks and social Events.</summary>
public sealed class BloodVsBirthdaysService(
    IAthleteSnapshotProvider athletes,
    TimeProvider clock,
    string snapshotDirectory,
    Func<string, string> versionAsset)
{
    public const string TimeZone = "Asia/Singapore";
    public const int RoundCount = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private BloodBirthdayPuzzle? _current;

    public BloodBirthdayResponse GetToday()
    {
        var now = clock.GetUtcNow();
        var day = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(8)).DateTime);
        BloodBirthdayPuzzle puzzle;
        lock (_gate)
        {
            if (_current?.Day != day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            {
                var path = Path.Combine(snapshotDirectory, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json");
                // An existing snapshot is authoritative. Do not silently replace a damaged
                // or incompatible file and invalidate today's saved answers.
                if (File.Exists(path))
                {
                    using var file = File.OpenRead(path);
                    var loaded = JsonSerializer.Deserialize<BloodBirthdayPuzzle>(file, JsonOptions)
                        ?? throw new InvalidDataException("The daily game snapshot is empty.");
                    Validate(loaded, day);
                    _current = loaded;
                }
                else
                {
                    var created = BuildPuzzle(athletes.GetAthletesSnapshot(), day, now, versionAsset);
                    Directory.CreateDirectory(snapshotDirectory);
                    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            JsonSerializer.Serialize(file, created, JsonOptions);
                            file.Flush(flushToDisk: true);
                        }
                        File.Move(temporary, path);
                        _current = created;
                    }
                    finally
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                }
            }
            puzzle = _current;
        }
        var next = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8));
        return new BloodBirthdayResponse(now, next.ToUniversalTime(), puzzle);
    }

    public static BloodBirthdayPuzzle BuildPuzzle(JsonArray snapshot, DateOnly day,
        DateTimeOffset createdAt, Func<string, string> versionAsset)
    {
        var dayText = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var candidates = new List<BloodBirthdayAthlete>();
        foreach (var source in snapshot.OfType<JsonObject>())
        {
            var datedSource = (JsonObject)source.DeepClone();
            datedSource["Biomarkers"] = new JsonArray((source["Biomarkers"] as JsonArray ?? []).OfType<JsonObject>()
                .Where(entry => DateOnly.TryParseExact(entry["Date"]?.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var testDay) && testDay <= day)
                .Select(entry => (JsonNode?)entry.DeepClone()).ToArray());
            var stats = PhenoStatsCalculator.Compute(datedSource, day.ToDateTime(TimeOnly.MinValue));
            if (stats.LowestBortzAge is not { } bloodAge || !double.IsFinite(bloodAge)
                || stats.BortzAgeReduction is not { } difference || !double.IsFinite(difference)
                || difference >= 0 || stats.LowestBortzAgeDateUtc is not { } date
                || date.Date > day.ToDateTime(TimeOnly.MinValue).Date
                || string.IsNullOrWhiteSpace(stats.Slug)) continue;

            var chrono = bloodAge - difference;
            if (!double.IsFinite(chrono) || chrono <= 0) continue;
            var name = source["DisplayName"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name)) name = stats.Name;
            var portrait = source["ProfilePic"]?.GetValue<string>();
            if (!IsPublicAsset(portrait)) portrait = "/assets/content-images/play-athlete-placeholder.webp";
            var proofs = (source["Proofs"] as JsonArray ?? []).Select(p => p?.GetValue<string>())
                .Where(IsPublicAsset).Select(p => Version(p!, versionAsset)).ToArray();
            if (proofs.Length == 0) continue;
            candidates.Add(new BloodBirthdayAthlete(stats.Slug, name!, Version(portrait!, versionAsset),
                chrono, bloodAge, -difference, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "/athlete/" + stats.Slug.Replace('_', '-'), proofs));
        }

        // Hash ordering is stable across process versions and source order. Prefer
        // opponents with nearby birthday ages, without reusing athletes in the set.
        var remaining = candidates.DistinctBy(a => a.Slug, StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => Hash(dayText + ":" + a.Slug), StringComparer.Ordinal).ToList();
        var rounds = new List<BloodBirthdayRound>();
        while (remaining.Count > 1 && rounds.Count < RoundCount)
        {
            var left = remaining[0];
            remaining.RemoveAt(0);
            var right = remaining.Where(a => Math.Abs(a.ReductionYears - left.ReductionYears) >= 0.3)
                .OrderBy(a => Math.Abs(a.ChronologicalAge - left.ChronologicalAge))
                .ThenBy(a => Hash(dayText + ":pair:" + a.Slug), StringComparer.Ordinal).FirstOrDefault();
            if (right is null) continue;
            remaining.Remove(right);
            // Alternate which side is favoured using the date and pair, not the score.
            if ((Convert.ToInt32(Hash(dayText + ":side:" + left.Slug)[..2], 16) & 1) == 1)
                (left, right) = (right, left);
            rounds.Add(new BloodBirthdayRound(left, right,
                left.ReductionYears > right.ReductionYears ? left.Slug : right.Slug));
        }
        if (rounds.Count != RoundCount)
            throw new InvalidOperationException("There are not enough distinct, evidenced bortz matchups for today's game.");
        var id = dayText + "-" + Hash(string.Join('|', rounds.Select(r =>
            FormattableString.Invariant($"{r.Left.Slug}:{r.Left.BortzAge:R}:{r.Right.Slug}:{r.Right.BortzAge:R}"))))[..12].ToLowerInvariant();
        return new BloodBirthdayPuzzle(1, id, dayText, TimeZone, createdAt, rounds.ToArray());
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsPublicAsset(string? value) => !string.IsNullOrWhiteSpace(value)
        && !value.Contains('\\') && !value.Contains("..", StringComparison.Ordinal)
        && (value.StartsWith("/athletes/", StringComparison.Ordinal)
            || value.StartsWith("/generated/", StringComparison.Ordinal)
            || value.StartsWith("/assets/", StringComparison.Ordinal));

    private static string Version(string asset, Func<string, string> versionAsset)
        => asset.Contains("?v=", StringComparison.Ordinal) || asset.Contains("&v=", StringComparison.Ordinal)
            ? asset : versionAsset(asset);

    private static void Validate(BloodBirthdayPuzzle puzzle, DateOnly day)
    {
        if (puzzle.Version != 1 || puzzle.Day != day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            || puzzle.TimeZone != TimeZone || puzzle.Rounds is null || puzzle.Rounds.Length != RoundCount
            || puzzle.Rounds.Any(r => r is null || r.Left is null || r.Right is null
                || !ValidAthlete(r.Left, day) || !ValidAthlete(r.Right, day)
                || Math.Abs(r.Left.ReductionYears - r.Right.ReductionYears) < 0.3
                || r.WinnerSlug != (r.Left.ReductionYears > r.Right.ReductionYears ? r.Left.Slug : r.Right.Slug))
            || puzzle.Rounds.SelectMany(r => new[] { r.Left.Slug, r.Right.Slug }).Distinct(StringComparer.OrdinalIgnoreCase).Count() != RoundCount * 2)
            throw new InvalidDataException("The daily game snapshot is incompatible.");
    }

    private static bool ValidAthlete(BloodBirthdayAthlete athlete, DateOnly day) =>
        !string.IsNullOrWhiteSpace(athlete.Slug) && !string.IsNullOrWhiteSpace(athlete.Name)
        && double.IsFinite(athlete.BortzAge) && double.IsFinite(athlete.ChronologicalAge)
        && double.IsFinite(athlete.ReductionYears) && athlete.ChronologicalAge > 0 && athlete.ReductionYears > 0
        && Math.Abs(athlete.ChronologicalAge - athlete.BortzAge - athlete.ReductionYears) < 0.000001
        && DateOnly.TryParseExact(athlete.TestDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date <= day
        && IsPublicAsset(athlete.PortraitUrl) && athlete.ProofUrls is { Length: > 0 } && athlete.ProofUrls.All(IsPublicAsset);
}

public sealed record BloodBirthdayAthlete(string Slug, string Name, string PortraitUrl,
    double ChronologicalAge, double BortzAge, double ReductionYears, string TestDate,
    string ProfileUrl, string[] ProofUrls);
public sealed record BloodBirthdayRound(BloodBirthdayAthlete Left, BloodBirthdayAthlete Right, string WinnerSlug);
public sealed record BloodBirthdayPuzzle(int Version, string Id, string Day, string TimeZone,
    DateTimeOffset CreatedAtUtc, BloodBirthdayRound[] Rounds);
public sealed record BloodBirthdayResponse(DateTimeOffset ServerNowUtc, DateTimeOffset NextPuzzleAtUtc, BloodBirthdayPuzzle Puzzle);
