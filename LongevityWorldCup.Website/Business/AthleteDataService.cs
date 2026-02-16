using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using LongevityWorldCup.Website.Tools;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Business;

public class AthleteDataService : IDisposable
{
    private static readonly Regex IsoDateLike = new(@"^\d{4}-\d{1,2}-\d{1,2}$", RegexOptions.Compiled);
    private readonly DateTime _serviceStartUtc = DateTime.UtcNow;
    private static readonly TimeSpan NewAthleteWindow = TimeSpan.FromDays(30);

    private JsonArray _athletes = []; // Initialize to avoid nullability issue

    public JsonArray Athletes
    {
        get
        {
            lock (_athletesJsonLock)
                return (JsonArray)_athletes.DeepClone();
        }
    }

    private readonly IWebHostEnvironment _env;
    private readonly EventDataService _eventDataService;
    private readonly FileSystemWatcher _athleteWatcher;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private CancellationTokenSource? _debounceCts;
    private static readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(100);

    private const string DatabaseFileName = "LongevityWorldCup.db";

    private readonly DatabaseManager _db;

    private readonly string _athletesRootDir;
    private readonly object _pendingLock = new();
    private readonly HashSet<string> _pendingChangedSlugs = new(StringComparer.OrdinalIgnoreCase);

    // single-column biomarker/test signature to detect new/changed test submissions
    private const string TestSigColumn = "TestSig";

    // NEW: notify listeners (e.g., BadgeDataService) after reloads
    public event Action? AthletesChanged;
    
    private readonly object _athletesJsonLock = new();

    public AthleteDataService(IWebHostEnvironment env, EventDataService eventDataService, DatabaseManager db)
    {
        _env = env;
        _eventDataService = eventDataService;
        _db = db ?? throw new ArgumentNullException(nameof(db));

        var dataDir = EnvironmentHelpers.GetDataDir();
        Directory.CreateDirectory(dataDir);

        _db.Run(sqlite =>
        {
            using (var cmd = sqlite.CreateCommand())
            {
                cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Athletes (
            Key        TEXT PRIMARY KEY,
            AgeGuesses TEXT NOT NULL
        )";
                cmd.ExecuteNonQuery();

                // Try to add column (ignore if exists)
                cmd.CommandText = "ALTER TABLE Athletes ADD COLUMN JoinedAt TEXT;";
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch
                {
                    /* column already exists */
                }

                // Ensure Placements, CurrentPlacement, LastAgeDiff columns exist
                cmd.CommandText = "PRAGMA table_info(Athletes);";
                using var r = cmd.ExecuteReader();
                var hasPlacements = false;
                var hasCurrentPlacement = false;
                var hasLastAgeDiff = false;
                var hasTestSig = false; // track if our signature column exists
                while (r.Read())
                {
                    var colName = r.GetString(1);
                    if (string.Equals(colName, "Placements", StringComparison.OrdinalIgnoreCase))
                        hasPlacements = true;
                    if (string.Equals(colName, "CurrentPlacement", StringComparison.OrdinalIgnoreCase))
                        hasCurrentPlacement = true;
                    if (string.Equals(colName, "LastAgeDiff", StringComparison.OrdinalIgnoreCase))
                        hasLastAgeDiff = true;
                    if (string.Equals(colName, TestSigColumn, StringComparison.OrdinalIgnoreCase))
                        hasTestSig = true;
                }

                if (!hasPlacements)
                {
                    using var alter = sqlite.CreateCommand();
                    alter.CommandText = "ALTER TABLE Athletes ADD COLUMN Placements TEXT NOT NULL DEFAULT '[]';";
                    alter.ExecuteNonQuery();

                    using var backfill = sqlite.CreateCommand();
                    backfill.CommandText = "UPDATE Athletes SET Placements='[]' WHERE Placements IS NULL OR Placements='';";
                    backfill.ExecuteNonQuery();
                }

                if (!hasCurrentPlacement)
                {
                    using var alter2 = sqlite.CreateCommand();
                    alter2.CommandText = "ALTER TABLE Athletes ADD COLUMN CurrentPlacement INTEGER NULL;";
                    alter2.ExecuteNonQuery();
                }

                if (!hasLastAgeDiff)
                {
                    using var alter3 = sqlite.CreateCommand();
                    alter3.CommandText = "ALTER TABLE Athletes ADD COLUMN LastAgeDiff REAL NULL;";
                    alter3.ExecuteNonQuery();
                }

                // add single signature column (one-time)
                if (!hasTestSig)
                {
                    using var alter4 = sqlite.CreateCommand();
                    alter4.CommandText = $"ALTER TABLE Athletes ADD COLUMN {TestSigColumn} TEXT NULL;";
                    alter4.ExecuteNonQuery();
                }
            }
        });

        // Initial load
        LoadAthletesAsync().GetAwaiter().GetResult();

        var newlyJoined = EnsureDbRowsForNewAthletes();

        // Finally, set JoinedAt=now for any that are still null
        if (newlyJoined.Count > 0)
        {
            _db.Run(sqlite =>
            {
                using var fillNow = sqlite.CreateCommand();
                var keys = newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>()).ToList();
                var placeholders = string.Join(",", keys.Select((_, i) => $"@k{i}"));
                fillNow.CommandText = $"UPDATE Athletes SET JoinedAt=@now WHERE (JoinedAt IS NULL OR JoinedAt='') AND Key IN ({placeholders})";
                fillNow.Parameters.AddWithValue("@now", _serviceStartUtc.ToString("o"));
                for (int i = 0; i < keys.Count; i++) fillNow.Parameters.AddWithValue($"@k{i}", keys[i]);
                fillNow.ExecuteNonQuery();
            });
        }

        // Hydrate persisted age‐guess stats from SQLite
        ReloadCrowdStats();
        HydratePlacementsIntoAthletesJson();
        HydrateNewFlagsIntoAthletesJson();
        HydrateCurrentPlacementIntoAthletesJson(); // NOTE: no DB persist here
        HydrateBadgesIntoAthletesJson();           // badges into athlete JSON

        // persist biomarker/test signature for all athletes (so we can detect new/changed tests later)
        var changedSigsAtStartup = SyncBiomarkerSignatures(); // returns slugs whose signatures changed

        // Watch the per-athlete folders recursively
        var athletesDir = Path.Combine(env.WebRootPath, "athletes");
        _athletesRootDir = athletesDir;
        _athleteWatcher = new FileSystemWatcher(athletesDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            Filter = "*.*"
        };
        _athleteWatcher.Changed += OnFsEvent;
        _athleteWatcher.Created += OnFsEvent;
        _athleteWatcher.Deleted += OnFsEvent;
        _athleteWatcher.Renamed += OnFsRenamed;
        _athleteWatcher.EnableRaisingEvents = true;
        _athleteWatcher.Error += OnWatcherError;

        // Build payload with current rank for each newcomer (after CurrentPlacement is hydrated)
        if (newlyJoined.Count > 0)
        {
            var payload = BuildJoinedPayloadWithReplaced(newlyJoined);
            eventDataService.CreateJoinedEventsForAthletes(payload, skipIfExists: true);
        }

        DetectAndEmitRankUpsForSlugs(
            changedSlugs: changedSigsAtStartup,
            newcomerSlugs: newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>())
        );

        DetectAndEmitAthleteCountMilestones(); // emit milestones retroactively and at startup

        _db.DatabaseChanged += OnDatabaseChanged;

        PushAthleteDirectoryToEvents();
    }

    private void OnDatabaseChanged()
    {
        _reloadLock.Wait();
        try
        {
            ReloadCrowdStats();
            HydratePlacementsIntoAthletesJson();
            HydrateNewFlagsIntoAthletesJson();
            HydrateCurrentPlacementIntoAthletesJson(); // NOTE: no DB persist here
            HydrateBadgesIntoAthletesJson();           // badges refresh when DB changed
        }
        finally
        {
            _reloadLock.Release();
        }

        PushAthleteDirectoryToEvents();
        AthletesChanged?.Invoke();
    }

    public IEnumerable<(JsonObject Athlete, DateTime JoinedAt)> GetAthletesJoinedData()
    {
        var athletesSnapshot = GetAthletesSnapshot();
        var bySlug = athletesSnapshot
            .OfType<JsonObject>()
            .Select(a => (Slug: a["AthleteSlug"]?.GetValue<string>() ?? "", Obj: a))
            .Where(t => !string.IsNullOrWhiteSpace(t.Slug))
            .ToDictionary(t => t.Slug, t => t.Obj, StringComparer.OrdinalIgnoreCase);

        var result = new List<(JsonObject, DateTime)>();
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Key, JoinedAt FROM Athletes";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                if (reader.IsDBNull(1))
                    continue;

                var joinedAt = DateTime.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind);

                if (bySlug.TryGetValue(key, out var athleteJson))
                    result.Add((athleteJson, joinedAt));
            }
        });

        return result;
    }

    private async Task LoadAthletesAsync()
    {
        // Build up a JsonArray by reading every athlete.json under wwwroot/athletes
        var athletesRoot = new JsonArray();
        var athletesDir = Path.Combine(_env.WebRootPath, "athletes");
        var files = Directory.EnumerateFiles(athletesDir, "athlete.json", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            // retry read in case the file is mid-write
            string text = "";
            for (int i = 0;; i++)
            {
                try
                {
                    text = File.ReadAllText(file);
                    break;
                }
                catch (IOException) when (i < 5)
                {
                    await Task.Delay(50);
                }
            }

            var athlete = JsonNode.Parse(text)!.AsObject();

            athlete["CrowdAge"] = 0;
            athlete["CrowdCount"] = 0;
            athlete["IsNew"] = false;

            var folder = Path.GetDirectoryName(file)!; // e.g. "/.../wwwroot/athletes/michelle_franz-montan"
            var folderName = Path.GetFileName(folder); // e.g. "michelle_franz-montan"

            // so we can look up this JsonObject later by slug
            athlete["AthleteSlug"] = folderName.Replace('-', '_'); // e.g. "michelle_franz_montan"

            // PROFILE PIC: look for "{key}.*" in that same folder
            var pic = Directory
                .EnumerateFiles(folder, $"{folderName}.*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            athlete["ProfilePic"] = pic is null
                ? null
                : $"/athletes/{folderName}/{Path.GetFileName(pic)}";

            // PROOFS: look for proof_*.ext
            var proofs = new JsonArray();
            var proofFiles = Directory
                .EnumerateFiles(folder, "proof_*.*", SearchOption.TopDirectoryOnly)
                .OrderBy(f => ExtractNumber(Path.GetFileNameWithoutExtension(f)));
            foreach (var p in proofFiles)
                proofs.Add($"/athletes/{folderName}/{Path.GetFileName(p)}");
            athlete["Proofs"] = proofs;

            CanonicalizeIsoDatesInPlace(athlete);
            athletesRoot.Add(athlete);
        }

        lock (_athletesJsonLock) _athletes = athletesRoot;
    }

    private void DebounceReload()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounceInterval, token);
                if (!token.IsCancellationRequested)
                    await OnSourceChangedAsync(this, null);
            }
            catch (TaskCanceledException)
            {
            }
        }, CancellationToken.None);
    }
    
    private static void CanonicalizeIsoDatesInPlace(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var keys = obj.Select(kv => kv.Key).ToList();
            foreach (var key in keys)
            {
                var val = obj[key];
                if (val is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) && IsoDateLike.IsMatch(s) && DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var dt))
                {
                    obj[key] = dt.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                else if (val is not null)
                {
                    CanonicalizeIsoDatesInPlace(val);
                }
            }
        }
        else if (node is JsonArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var val = arr[i];
                if (val is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) && IsoDateLike.IsMatch(s) && DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var dt))
                {
                    arr[i] = dt.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                else if (val is not null)
                {
                    CanonicalizeIsoDatesInPlace(val);
                }
            }
        }
    }
    
    // Detect and emit athlete-count milestones (retroactive + ongoing).
    // Uses the N-th athlete's JoinedAt timestamp as the event time.
    private void DetectAndEmitAthleteCountMilestones()
    {
        var joined = GetAthletesJoinedData()
            .OrderBy(x => x.JoinedAt)
            .ToList();

        if (joined.Count == 0)
            return;

        // Fun-first, spaced milestones (marketing-friendly, not too dense)
        int[] thresholds = new[]
        {
            10,
            42, 69, 100, 123,
            256, 300, 404, 500, 666, 777,
            1000, 1337, 1618,
            2000, 3141,
            5000, 6969, 9001, 10000
        };

        var payload = new List<(int Count, DateTime OccurredAtUtc)>();
        foreach (var t in thresholds)
        {
            if (joined.Count >= t)
            {
                var when = DateTime.SpecifyKind(joined[t - 1].JoinedAt, DateTimeKind.Utc);
                payload.Add((t, when));
            }
        }

        if (payload.Count > 0)
        {
            _eventDataService.CreateAthleteCountMilestoneEvents(payload, skipIfExists: true);
        }
    }

    private async Task OnSourceChangedAsync(object sender, FileSystemEventArgs? e)
    {
        var notify = false;

        await _reloadLock.WaitAsync();
        try
        {
            await LoadAthletesAsync();

            var newlyJoined = EnsureDbRowsForNewAthletes();

            ReloadCrowdStats();
            HydratePlacementsIntoAthletesJson();
            HydrateNewFlagsIntoAthletesJson();
            HydrateCurrentPlacementIntoAthletesJson(); // NOTE: no DB persist here
            HydrateBadgesIntoAthletesJson();           // badges into athlete JSON

            // recompute and persist biomarker/test signatures after reload
            var changedSigs = SyncBiomarkerSignatures();

            if (newlyJoined.Count > 0)
            {
                var payload = BuildJoinedPayloadWithReplaced(newlyJoined);
                _eventDataService.CreateJoinedEventsForAthletes(payload, skipIfExists: true);
            }

            DetectAndEmitRankUpsForSlugs(
                changedSlugs: changedSigs,
                newcomerSlugs: newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>())
            );

            DetectAndEmitAthleteCountMilestones(); // emit milestones on reload/new joins

            notify = true;
        }
        finally
        {
            _reloadLock.Release();
        }

        if (notify)
        {
            PushAthleteDirectoryToEvents();
            AthletesChanged?.Invoke();
        }
    }

    /// <summary>
    /// Fired if the FileSystemWatcher’s internal buffer overflows or another error occurs.
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Example: log and restart the watcher
        var watcher = (FileSystemWatcher)sender;

        watcher.EnableRaisingEvents = false;
        watcher.EnableRaisingEvents = true;
    }

    private static int ExtractNumber(string fileNameWithoutExtension)
    {
        var parts = fileNameWithoutExtension.Split('_');
        if (int.TryParse(parts.Last(), out var number))
            return number;
        return int.MaxValue;
    }

    /// <summary>
    /// Adds a timestamped age guess for the given athleteSlug, updates
    /// that athlete’s CrowdAge (median) and CrowdCount in the loaded JSON.
    /// </summary>
    public void AddAgeGuess(string athleteSlug, int ageGuess)
    {
        int cnt;
        double median;

        _reloadLock.Wait();
        try
        {
            cnt = 0;
            median = 0;

            _db.Run(sqlite =>
            {
                // insert the new guess
                using var selectJsonCmd = sqlite.CreateCommand();
                selectJsonCmd.CommandText =
                    "SELECT AgeGuesses FROM Athletes WHERE Key = @key";
                selectJsonCmd.Parameters.AddWithValue("@key", athleteSlug);
                var existingJson = (selectJsonCmd.ExecuteScalar() as string) ?? "[]";

                var ageArray = JsonSerializer.Deserialize<List<JsonObject>>(existingJson)!;
                ageArray.Add(new JsonObject
                {
                    ["TimestampUtc"] = DateTime.UtcNow.ToString("o"),
                    ["AgeGuess"] = ageGuess
                });

                // store back
                var updatedJson = JsonSerializer.Serialize(ageArray);
                using var updateCmd = sqlite.CreateCommand();
                updateCmd.CommandText =
                    "UPDATE Athletes SET AgeGuesses = @ages WHERE Key = @key";
                updateCmd.Parameters.AddWithValue("@ages", updatedJson);
                updateCmd.Parameters.AddWithValue("@key", athleteSlug);
                updateCmd.ExecuteNonQuery();

                // re-read and compute median and count
                using var selectCmd = sqlite.CreateCommand();
                selectCmd.CommandText =
                    "SELECT AgeGuesses FROM Athletes WHERE Key = @key";
                selectCmd.Parameters.AddWithValue("@key", athleteSlug);
                var allJson = (selectCmd.ExecuteScalar() as string) ?? "[]";

                var allGuesses = JsonSerializer.Deserialize<List<JsonObject>>(allJson)!;
                var ages = allGuesses
                    .Select(node => node["AgeGuess"]!.GetValue<int>())
                    .OrderBy(val => val)
                    .ToList();

                cnt = ages.Count;
                median = 0;
                if (cnt > 0)
                {
                    median = (cnt % 2 == 1)
                        ? ages[cnt / 2]
                        : (ages[cnt / 2 - 1] + ages[cnt / 2]) / 2.0;
                }
            });

            lock (_athletesJsonLock)
            {
                var athleteJson = _athletes
                    .OfType<JsonObject>()
                    .FirstOrDefault(o => string.Equals(
                        o["AthleteSlug"]?.GetValue<string>(),
                        athleteSlug,
                        StringComparison.OrdinalIgnoreCase));

                if (athleteJson != null)
                {
                    athleteJson["CrowdCount"] = cnt;
                    athleteJson["CrowdAge"] = median;
                }
            }
        }
        finally
        {
            _reloadLock.Release();
        }

        PushAthleteDirectoryToEvents();
        AthletesChanged?.Invoke();
    }

    /// <summary>
    /// Re‐reads all medians & counts from SQLite and updates the in‐memory JSON.
    /// </summary>
    public void ReloadCrowdStats()
    {
        List<string> slugs;
        lock (_athletesJsonLock)
        {
            slugs = _athletes
                .OfType<JsonObject>()
                .Select(a => a["AthleteSlug"]!.GetValue<string>())
                .ToList();
        }

        var stats = new Dictionary<string, (double Median, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in slugs)
        {
            var (median, count) = GetCrowdStats(slug);
            stats[slug] = (median, count);
        }

        lock (_athletesJsonLock)
        {
            foreach (var athleteJson in _athletes.OfType<JsonObject>())
            {
                var slug = athleteJson["AthleteSlug"]!.GetValue<string>();
                if (stats.TryGetValue(slug, out var s))
                {
                    athleteJson["CrowdAge"] = s.Median;
                    athleteJson["CrowdCount"] = s.Count;
                }
            }
        }
    }

    /// <summary>
    /// Returns (Median, Count) for all guesses recorded so far for athleteSlug.
    /// </summary>
    public (double Median, int Count) GetCrowdStats(string athleteSlug)
    {
        return _db.Run(sqlite =>
        {
            using var selectAgesJson = sqlite.CreateCommand();
            selectAgesJson.CommandText =
                "SELECT AgeGuesses FROM Athletes WHERE Key = @key";
            selectAgesJson.Parameters.AddWithValue("@key", athleteSlug);
            var agesJsonText = (selectAgesJson.ExecuteScalar() as string) ?? "[]";
            var allGuesses = JsonSerializer.Deserialize<List<JsonObject>>(agesJsonText)!;
            var ages = allGuesses
                .Select(node => node["AgeGuess"]!.GetValue<int>())
                .OrderBy(val => val)
                .ToList();

            int cnt = ages.Count;
            double median = 0;
            if (cnt > 0)
            {
                median = (cnt % 2 == 1)
                    ? ages[cnt / 2]
                    : (ages[cnt / 2 - 1] + ages[cnt / 2]) / 2.0;
            }

            return (median, cnt);
        });
    }

    public int GetActualAge(string athleteSlug)
    {
        var athletesSnapshot = GetAthletesSnapshot();
        var athleteJson = athletesSnapshot
            .OfType<JsonObject>()
            .FirstOrDefault(o => string.Equals(
                o["AthleteSlug"]?.GetValue<string>(),
                athleteSlug,
                StringComparison.OrdinalIgnoreCase));
        if (athleteJson is null)
            return 0;

        var dobNode = athleteJson["DateOfBirth"]?.AsObject();
        if (dobNode is null)
            return 0;

        int year = dobNode["Year"]!.GetValue<int>();
        int month = dobNode["Month"]!.GetValue<int>();
        int day = dobNode["Day"]!.GetValue<int>();

        var dob = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        var today = DateTime.UtcNow.Date;
        int age = today.Year - dob.Year;
        if (today < dob.AddYears(age))
            age--;

        return age;
    }

    public JsonArray GetRankingsOrder(DateTime? asOfUtc = null)
    {
        var asOf = (asOfUtc ?? DateTime.UtcNow).Date;
        var athletesSnapshot = GetAthletesSnapshot();
        var results = new List<(bool HasBortz, double EffectiveReduction, DateTime DobUtc, string Name, JsonObject Obj)>();

        var statsMap = PhenoStatsCalculator.BuildAll(athletesSnapshot, asOf);

        foreach (var athlete in athletesSnapshot.OfType<JsonObject>())
        {
            var slug = athlete["AthleteSlug"]?.GetValue<string>() ?? "";
            if (!statsMap.TryGetValue(slug, out var r)) continue;
            if (!r.DobUtc.HasValue) continue;

            var name = r.Name ?? "";
            var dobUtc = r.DobUtc.Value;

            var chronoToday = r.ChronoAge.HasValue ? Math.Round(r.ChronoAge.Value, 2) : 0;
            var lowestPheno = r.LowestPhenoAge.HasValue ? Math.Round(r.LowestPhenoAge.Value, 2) : chronoToday;
            var phenoDiff = Math.Round(r.AgeReduction ?? 0, 2);
            var hasBortz = r.BortzAgeReduction.HasValue &&
                           !double.IsNaN(r.BortzAgeReduction.Value) &&
                           !double.IsInfinity(r.BortzAgeReduction.Value);
            var effectiveDiff = hasBortz ? Math.Round(r.BortzAgeReduction!.Value, 2) : phenoDiff;

            var obj = new JsonObject
            {
                ["AthleteSlug"] = slug,
                ["Name"] = name,
                ["ChronologicalAge"] = chronoToday,
                ["LowestPhenoAge"] = lowestPheno,
                ["AgeDifference"] = effectiveDiff
            };

            results.Add((hasBortz, effectiveDiff, dobUtc, name, obj));
        }

        var arr = new JsonArray();
        foreach (var o in SortByCompetitionRules(results).Select(t => t.Obj))
            arr.Add(o);

        return arr;
    }

    private static IOrderedEnumerable<(bool HasBortz, double EffectiveReduction, DateTime DobUtc, string Name, JsonObject Obj)>
        SortByCompetitionRules(IEnumerable<(bool HasBortz, double EffectiveReduction, DateTime DobUtc, string Name, JsonObject Obj)> rows)
    {
        return rows
            .OrderByDescending(t => t.HasBortz) // 1) Bortz participants are ranked ahead of Pheno-only
            .ThenBy(t => t.EffectiveReduction) // 2) more negative is better
            .ThenBy(t => t.DobUtc) // 3) older (earlier DOB) wins
            .ThenBy(t => t.Name, StringComparer.Ordinal); // 4) alphabetical
    }

    public int?[] GetPlacements(string athleteSlug)
    {
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Placements FROM Athletes WHERE Key=@k";
            cmd.Parameters.AddWithValue("@k", athleteSlug);
            var txt = cmd.ExecuteScalar() as string ?? "[]";

            int?[] result;
            try
            {
                var arr = JsonSerializer.Deserialize<int?[]>(txt) ?? Array.Empty<int?>();
                result = new int?[4];
                for (int i = 0; i < 4; i++)
                    result[i] = i < arr.Length ? arr[i] : null;
            }
            catch
            {
                result = new int?[4];
            }

            return result;
        });
    }

    public void SetPlacements(string athleteSlug, int?[] placements)
    {
        if (placements is null) throw new ArgumentNullException(nameof(placements));
        if (placements.Length != 4) placements = new[] { placements.ElementAtOrDefault(0), placements.ElementAtOrDefault(1), placements.ElementAtOrDefault(2), placements.ElementAtOrDefault(3) };

        var json = JsonSerializer.Serialize(placements);

        _reloadLock.Wait();
        try
        {
            _db.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = "UPDATE Athletes SET Placements=@p WHERE Key=@k";
                cmd.Parameters.AddWithValue("@p", json);
                cmd.Parameters.AddWithValue("@k", athleteSlug);
                cmd.ExecuteNonQuery();
            });

            lock (_athletesJsonLock)
            {
                var athleteJson = _athletes
                    .OfType<JsonObject>()
                    .FirstOrDefault(o => string.Equals(
                        o["AthleteSlug"]?.GetValue<string>(),
                        athleteSlug,
                        StringComparison.OrdinalIgnoreCase));

                if (athleteJson != null)
                {
                    var arr = new JsonArray();
                    foreach (var v in placements) arr.Add(v is int x ? JsonValue.Create(x) : null);
                    athleteJson["Placements"] = arr;
                }
            }
        }
        finally
        {
            _reloadLock.Release();
        }

        PushAthleteDirectoryToEvents();
    }

    public void UpdatePlacements(string athleteSlug, int? yesterday = null, int? weekly = null, int? monthly = null, int? yearly = null)
    {
        var p = GetPlacements(athleteSlug);
        if (yesterday.HasValue) p[0] = yesterday;
        if (weekly.HasValue) p[1] = weekly;
        if (monthly.HasValue) p[2] = monthly;
        if (yearly.HasValue) p[3] = yearly;
        SetPlacements(athleteSlug, p);
    }

    private void HydratePlacementsIntoAthletesJson()
    {
        List<string> slugs;
        lock (_athletesJsonLock)
        {
            slugs = _athletes
                .OfType<JsonObject>()
                .Select(a => a["AthleteSlug"]!.GetValue<string>())
                .ToList();
        }

        var bySlug = new Dictionary<string, int?[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var slug in slugs)
            bySlug[slug] = GetPlacements(slug);

        lock (_athletesJsonLock)
        {
            foreach (var athleteJson in _athletes.OfType<JsonObject>())
            {
                var slug = athleteJson["AthleteSlug"]!.GetValue<string>();
                if (!bySlug.TryGetValue(slug, out var p))
                    continue;

                var arr = new JsonArray();
                foreach (var v in p) arr.Add(v is int x ? JsonValue.Create(x) : null);
                athleteJson["Placements"] = arr;
            }
        }
    }

    // compute IsNew from SQLite JoinedAt using the NewAthleteWindow
    private void HydrateNewFlagsIntoAthletesJson()
    {
        var cutoffUtc = DateTime.UtcNow - NewAthleteWindow;

        var joinedByKey = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Key, JoinedAt FROM Athletes";

            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    var joinedAtText = reader.IsDBNull(1) ? null : reader.GetString(1);
                    if (string.IsNullOrWhiteSpace(joinedAtText)) continue;

                    if (DateTime.TryParse(joinedAtText, null, DateTimeStyles.RoundtripKind, out var joinedAt))
                    {
                        joinedByKey[key] = joinedAt;
                    }
                }
            }
        });

        lock (_athletesJsonLock)
        {
            foreach (var athleteJson in _athletes.OfType<JsonObject>())
            {
                var slug = athleteJson["AthleteSlug"]?.GetValue<string>();
                var isNew = false;

                if (!string.IsNullOrEmpty(slug) && joinedByKey.TryGetValue(slug, out var joinedAt))
                    isNew = joinedAt >= cutoffUtc;

                athleteJson["IsNew"] = isNew;
            }
        }
    }

    private void HydrateCurrentPlacementIntoAthletesJson()
    {
        var order = GetRankingsOrder();
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] is JsonObject o)
            {
                var slug = o["AthleteSlug"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(slug)) map[slug] = i + 1;
            }
        }

        lock (_athletesJsonLock)
        {
            foreach (var athlete in _athletes.OfType<JsonObject>())
            {
                var slug = athlete["AthleteSlug"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(slug) && map.TryGetValue(slug, out var pos))
                    athlete["CurrentPlacement"] = pos;
                else
                    athlete["CurrentPlacement"] = null;
            }
        }

        // IMPORTANT: we DO NOT persist the snapshot here anymore.
        // Persistence must only happen AFTER we compare and possibly emit events.
    }

    // Public entry-point so other services (e.g., BadgeDataService) can trigger a badges refresh.
    public void RefreshBadgesFromDatabase()
    {
        _reloadLock.Wait();
        try
        {
            HydrateBadgesIntoAthletesJson();
        }
        finally
        {
            _reloadLock.Release();
        }
    }
    
    // badges from BadgeAwards -> injected into athlete JSON objects
    private void HydrateBadgesIntoAthletesJson()
    {
        // Schema (BadgeAwards): BadgeLabel, LeagueCategory, LeagueValue, Place, AthleteSlug, DefinitionHash, UpdatedAt
        var byAthlete = new Dictionary<string, JsonArray>(StringComparer.OrdinalIgnoreCase);

        try
        {
            _db.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = "SELECT BadgeLabel, LeagueCategory, LeagueValue, Place, AthleteSlug FROM BadgeAwards";
                using var r = cmd.ExecuteReader();

                while (r.Read())
                {
                    var label = r.GetString(0);
                    var cat = r.GetString(1);
                    var val = r.IsDBNull(2) ? null : r.GetString(2);
                    int? place = r.IsDBNull(3) ? (int?)null : r.GetInt32(3);
                    var slug = r.GetString(4);

                    if (!byAthlete.TryGetValue(slug, out var list))
                    {
                        list = new JsonArray();
                        byAthlete[slug] = list;
                    }

                    var badge = new JsonObject
                    {
                        ["Label"] = label,
                        ["LeagueCategory"] = cat,
                        ["LeagueValue"] = val is null ? null : JsonValue.Create(val),
                        ["Place"] = place is int p ? JsonValue.Create(p) : null
                    };
                    list.Add(badge);
                }
            });
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 /* no such table: BadgeAwards */)
        {
            // If BadgeAwards doesn't exist yet, we leave badges empty.
        }

        lock (_athletesJsonLock)
        {
            foreach (var athleteJson in _athletes.OfType<JsonObject>())
            {
                var slug = athleteJson["AthleteSlug"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(slug))
                {
                    athleteJson["Badges"] = new JsonArray();
                    continue;
                }

                if (byAthlete.TryGetValue(slug, out var arr))
                    athleteJson["Badges"] = arr;
                else
                    athleteJson["Badges"] = new JsonArray();
            }
        }
    }
    
    public void UpdateAthletesJsonInPlace(Action<JsonObject> mutator)
    {
        if (mutator is null) throw new ArgumentNullException(nameof(mutator));

        lock (_athletesJsonLock)
        {
            foreach (var o in _athletes.OfType<JsonObject>())
                mutator(o);
        }
    }
    
    public JsonArray GetAthletesSnapshot()
    {
        lock (_athletesJsonLock)
            return (JsonArray)_athletes.DeepClone();
    }

    public void Dispose()
    {
        _db.DatabaseChanged -= OnDatabaseChanged;
        _athleteWatcher.Dispose();
        _reloadLock.Dispose();
        _debounceCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    private List<(JsonObject Athlete, DateTime JoinedAt)> EnsureDbRowsForNewAthletes()
    {
        List<string> slugs;
        lock (_athletesJsonLock)
        {
            slugs = _athletes
                .OfType<JsonObject>()
                .Select(a => a["AthleteSlug"]!.GetValue<string>())
                .ToList();
        }

        var inserted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _db.Run(sqlite =>
        {
            foreach (var slug in slugs)
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO Athletes (Key, AgeGuesses, JoinedAt) VALUES (@k, @ages, @joined)";
                cmd.Parameters.AddWithValue("@k", slug);
                cmd.Parameters.AddWithValue("@ages", "[]");
                cmd.Parameters.AddWithValue("@joined", _serviceStartUtc.ToString("o"));
                var rows = cmd.ExecuteNonQuery();
                if (rows == 1)
                    inserted.Add(slug);
            }
        });

        var newlyJoined = new List<(JsonObject, DateTime)>();
        if (inserted.Count == 0)
            return newlyJoined;

        lock (_athletesJsonLock)
        {
            foreach (var athleteJson in _athletes.OfType<JsonObject>())
            {
                var slug = athleteJson["AthleteSlug"]!.GetValue<string>();
                if (!inserted.Contains(slug))
                    continue;

                athleteJson["IsNew"] = true;
                newlyJoined.Add((athleteJson, _serviceStartUtc));
            }
        }

        return newlyJoined;
    }

    private Dictionary<string, int> BuildRankMap(int limit = int.MaxValue)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var order = GetRankingsOrder();
        for (int i = 0; i < order.Count && i < limit; i++)
        {
            if (order[i] is JsonObject o)
            {
                var slug = o["AthleteSlug"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(slug))
                    map[slug] = i + 1;
            }
        }

        return map;
    }

    private void OnFsEvent(object? sender, FileSystemEventArgs e)
    {
        TryRecordChangedSlug(e.FullPath);
        DebounceReload();
    }

    private void OnFsRenamed(object? sender, RenamedEventArgs e)
    {
        TryRecordChangedSlug(e.OldFullPath);
        TryRecordChangedSlug(e.FullPath);
        DebounceReload();
    }

    private void TryRecordChangedSlug(string path)
    {
        if (IsAthleteJsonPath(path, out var slug))
        {
            lock (_pendingLock) _pendingChangedSlugs.Add(slug);
        }
    }

    private bool IsAthleteJsonPath(string path, out string slug)
    {
        slug = "";
        if (string.IsNullOrWhiteSpace(path)) return false;
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(_athletesRootDir);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(Path.GetFileName(full), "athlete.json", StringComparison.OrdinalIgnoreCase)) return false;
        var rel = Path.GetRelativePath(root, full);
        var parts = rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        var folder = parts[0];
        slug = folder.Replace('-', '_');
        return true;
    }

    private Dictionary<string, int?> LoadStoredCurrentPlacements()
    {
        var map = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Key, CurrentPlacement FROM Athletes";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                int? cp = r.IsDBNull(1) ? (int?)null : r.GetInt32(1);
                map[key] = cp;
            }
        });

        return map;
    }

    private Dictionary<string, double?> LoadStoredLastAgeDifferences()
    {
        var map = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Key, LastAgeDiff FROM Athletes";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                double? val = r.IsDBNull(1) ? (double?)null : r.GetDouble(1);
                map[key] = val;
            }
        });

        return map;
    }

    private Dictionary<string, double> BuildAgeDiffMap()
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var order = GetRankingsOrder();
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] is JsonObject o)
            {
                var slug = o["AthleteSlug"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(slug)) continue;
                if (o["AgeDifference"] is JsonValue jv && jv.TryGetValue<double>(out var diff) &&
                    !double.IsNaN(diff) && !double.IsInfinity(diff))
                {
                    map[slug] = diff;
                }
            }
        }

        return map;
    }

    private void PersistCurrentPlacementsSnapshot(Dictionary<string, int> current, Dictionary<string, double> currentAgeDiffs)
    {
        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();
            using (var clear = sqlite.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "UPDATE Athletes SET CurrentPlacement=NULL";
                clear.ExecuteNonQuery();
            }

            using (var upd = sqlite.CreateCommand())
            {
                upd.Transaction = tx;
                upd.CommandText = "UPDATE Athletes SET CurrentPlacement=@p, LastAgeDiff=@d WHERE Key=@k";
                var pP = upd.Parameters.Add("@p", SqliteType.Integer);
                var pD = upd.Parameters.Add("@d", SqliteType.Real);
                var pK = upd.Parameters.Add("@k", SqliteType.Text);
                foreach (var kv in current)
                {
                    pP.Value = kv.Value;
                    pK.Value = kv.Key;
                    if (currentAgeDiffs.TryGetValue(kv.Key, out var diff))
                        pD.Value = diff;
                    else
                        pD.Value = DBNull.Value;
                    upd.ExecuteNonQuery();
                }
            }

            tx.Commit();
        });
    }

    /// <summary>
    /// For the given set of slugs (whose biomarker signatures changed),
    /// compare current full-table ranks against the last persisted snapshot and
    /// emit NewRank events for any numeric improvement (smaller number is better).
    /// Newcomers are excluded (handled by Join pipeline). Then persist the new snapshot.
    /// If there is no baseline (all NULL) => first run: persist only, do not emit.
    /// </summary>
    private void DetectAndEmitRankUpsForSlugs(IEnumerable<string> changedSlugs, IEnumerable<string>? newcomerSlugs)
    {
        var before = LoadStoredCurrentPlacements();

        // FIRST RUN GUARD: no baseline => persist only
        var hasAnyBaseline = before.Values.Any(v => v.HasValue);
        if (!hasAnyBaseline)
        {
            var initialSnapshot = BuildRankMap(); // full table
            var initialDiffs = BuildAgeDiffMap();
            PersistCurrentPlacementsSnapshot(initialSnapshot, initialDiffs);
            return;
        }

        var changed = new HashSet<string>(changedSlugs ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        var newcomers = newcomerSlugs?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // AFTER: full ranking (slug -> rank) and helper (rank -> slug)
        var afterAll = BuildRankMap();
        var afterAllByRank = afterAll.ToDictionary(kv => kv.Value, kv => kv.Key);

        // BEFORE: rank -> slug (used to try to find the previous holder at a given rank)
        var beforeByRank = new Dictionary<int, string>(capacity: before.Count);
        foreach (var kv in before)
        {
            if (kv.Value is int r && r >= 1) beforeByRank[r] = kv.Key;
        }

        var nowUtc = DateTime.UtcNow;
        var changes = new List<(string AthleteSlug, DateTime OccurredAtUtc, int Rank, string? ReplacedSlug)>();

        if (changed.Count > 0)
        {
            foreach (var slug in changed)
            {
                // skip if this athlete isn't ranked now (shouldn't happen, but be defensive)
                if (!afterAll.TryGetValue(slug, out var newRank)) continue;

                // newcomers handled elsewhere
                if (newcomers.Contains(slug)) continue;

                // need a baseline rank to compare
                if (!before.TryGetValue(slug, out var prevRankNullable) || !prevRankNullable.HasValue) continue;

                var prevRank = prevRankNullable.Value;
                if (newRank >= prevRank) continue; // no numeric improvement

                // try to identify who was replaced at this new rank (best effort)
                string? replacedSlug = null;

                // 1) exact previous holder of the new rank
                if (beforeByRank.TryGetValue(newRank, out var prevHolder) &&
                    !string.Equals(prevHolder, slug, StringComparison.OrdinalIgnoreCase))
                {
                    replacedSlug = prevHolder;
                }

                // 2) fallback: somebody who was >= newRank and moved down (or disappeared)
                if (replacedSlug is null)
                {
                    string? candidate = null;
                    var bestPrev = int.MaxValue;

                    foreach (var p in before)
                    {
                        if (!p.Value.HasValue) continue;
                        var pr = p.Value.Value;
                        if (pr < newRank) continue;
                        if (string.Equals(p.Key, slug, StringComparison.OrdinalIgnoreCase)) continue;

                        // moved down or disappeared
                        if (!afterAll.TryGetValue(p.Key, out var ar) || ar > pr)
                        {
                            if (pr < bestPrev)
                            {
                                bestPrev = pr;
                                candidate = p.Key;
                            }
                        }
                    }

                    replacedSlug = candidate;
                }

                // 3) last-ditch: the athlete that sits right below the new rank now
                if (replacedSlug is null && afterAllByRank.TryGetValue(newRank + 1, out var afterNext))
                    replacedSlug = afterNext;

                changes.Add((slug, nowUtc, newRank, replacedSlug));
            }

            if (changes.Count > 0)
                _eventDataService.CreateNewRankEvents(changes, skipIfExists: true);
        }

        // Persist full snapshot AFTER emitting events (always, even if no changes)
        var afterAllFinal = BuildRankMap(); // full table
        var afterDiffs = BuildAgeDiffMap(); // persisted for visibility/debug
        PersistCurrentPlacementsSnapshot(afterAllFinal, afterDiffs);
    }


    /// <summary>
    /// Build the joined payload enriched with the previous holder of each newcomer's rank.
    /// </summary>
    private IEnumerable<(string Slug, DateTime JoinedAtUtc, int? Rank, string? ReplacedSlug)>
        BuildJoinedPayloadWithReplaced(IEnumerable<(JsonObject Athlete, DateTime JoinedAt)> newlyJoined)
    {
        // Snapshot BEFORE the join (what was persisted last time)
        var before = LoadStoredCurrentPlacements();

        // rank -> slug map for BEFORE
        var beforeByRank = new Dictionary<int, string>();
        foreach (var kv in before)
            if (kv.Value is int r && r >= 1)
                beforeByRank[r] = kv.Key;

        // AFTER maps (current in-memory ranking)
        var afterAll = BuildRankMap();
        var afterAllByRank = afterAll.ToDictionary(kv => kv.Value, kv => kv.Key);

        var newcomerSet = newlyJoined
            .Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var x in newlyJoined)
        {
            var slug = x.Athlete["AthleteSlug"]!.GetValue<string>();
            int? rank = null;
            var rp = x.Athlete["CurrentPlacement"];
            if (rp is JsonValue jv && jv.TryGetValue<int>(out var pos)) rank = pos;

            string? replaced = null;

            if (rank.HasValue)
            {
                var newRank = rank.Value;

                // 1) whoever held that rank before
                if (beforeByRank.TryGetValue(newRank, out var prevHolder) &&
                    !string.Equals(prevHolder, slug, StringComparison.OrdinalIgnoreCase))
                {
                    replaced = prevHolder;
                }

                // 2) fallback: find someone who was >= newRank and moved down/got pushed out
                if (replaced is null)
                {
                    string? candidate = null;
                    var bestPrev = int.MaxValue;
                    foreach (var p in before)
                    {
                        if (!p.Value.HasValue) continue;
                        var pr = p.Value.Value;
                        if (pr < newRank) continue;
                        if (string.Equals(p.Key, slug, StringComparison.OrdinalIgnoreCase)) continue;

                        // moved down (or disappeared)
                        if (!afterAll.TryGetValue(p.Key, out var ar) || ar > pr)
                        {
                            if (pr < bestPrev)
                            {
                                bestPrev = pr;
                                candidate = p.Key;
                            }
                        }
                    }

                    replaced = candidate;
                }

                // 3) last-ditch: the person that sits right below newcomer now (but exclude newcomers)
                if (replaced is null && afterAllByRank.TryGetValue(newRank + 1, out var afterNext) && !newcomerSet.Contains(afterNext))
                    replaced = afterNext;
            }

            yield return (slug, x.JoinedAt, rank, replaced);
        }
    }

    private void PushAthleteDirectoryToEvents()
    {
        var athletesSnapshot = GetAthletesSnapshot();
        var list = new List<(string Slug, string Name, int? CurrentRank)>();
        var podcastList = new List<(string Slug, string PodcastLink)>();
        foreach (var o in athletesSnapshot.OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;
            var name = o["Name"]?.GetValue<string>() ?? "";
            int? rank = null;
            var rp = o["CurrentPlacement"];
            if (rp is JsonValue jv && jv.TryGetValue<int>(out var pos)) rank = pos;
            list.Add((slug, name, rank));

            var link = o["PodcastLink"]?.GetValue<string>() ?? o["podcastLink"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(link))
                podcastList.Add((slug, link.Trim()));
        }

        _eventDataService.SetAthleteDirectory(list);
        _eventDataService.SetPodcastLinks(podcastList);
    }

    // ===== biomarker/test signature helpers (single-column persistence) =====

    private List<string> SyncBiomarkerSignatures()
    {
        // Computes a deterministic signature from Biomarkers and stores it in Athletes.TestSig.
        // If nothing changed, no write occurs. This is called on startup and after reloads.
        var athletesSnapshot = GetAthletesSnapshot();
        var changed = new List<string>();
        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            foreach (var athlete in athletesSnapshot.OfType<JsonObject>())
            {
                var slug = athlete["AthleteSlug"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(slug)) continue;

                var newSig = ComputeBiomarkerSignature(athlete);
                string? oldSig;

                using (var sel = sqlite.CreateCommand())
                {
                    sel.Transaction = tx;
                    sel.CommandText = $"SELECT {TestSigColumn} FROM Athletes WHERE Key=@k";
                    sel.Parameters.AddWithValue("@k", slug);
                    var o = sel.ExecuteScalar();
                    oldSig = o is DBNull or null ? null : (string)o;
                }

                if (!string.Equals(oldSig, newSig, StringComparison.Ordinal))
                {
                    using var upd = sqlite.CreateCommand();
                    upd.Transaction = tx;
                    upd.CommandText = $"UPDATE Athletes SET {TestSigColumn}=@s WHERE Key=@k";
                    upd.Parameters.AddWithValue("@s", (object?)newSig ?? DBNull.Value);
                    upd.Parameters.AddWithValue("@k", slug);
                    upd.ExecuteNonQuery();

                    changed.Add(slug);
                }
            }

            tx.Commit();
        });

        return changed;
    }

    private static string ComputeBiomarkerSignature(JsonObject athlete)
    {
        // Canonicalize each biomarker record from present values only:
        // Date + numeric biomarker key/value pairs sorted by key.
        // This keeps signatures stable when new optional biomarker fields are introduced.
        if (athlete["Biomarkers"] is not JsonArray arr || arr.Count == 0)
            return Sha256Hex(string.Empty);

        var lines = new List<string>(arr.Count);

        foreach (var node in arr.OfType<JsonObject>())
        {
            string dateStr = "";
            var ds = node["Date"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(ds) &&
                DateTime.TryParse(ds, null, DateTimeStyles.RoundtripKind, out var parsed))
            {
                var d = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
                dateStr = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            var parts = new List<string> { dateStr };
            foreach (var kv in node.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (string.Equals(kv.Key, "Date", StringComparison.OrdinalIgnoreCase)) continue;
                if (kv.Value is not JsonValue jv) continue;
                if (!jv.TryGetValue<double>(out var num)) continue;
                if (double.IsNaN(num) || double.IsInfinity(num)) continue;

                var val = num.ToString("R", CultureInfo.InvariantCulture);
                parts.Add($"{kv.Key}={val}");
            }

            var line = string.Join("|", parts);
            lines.Add(line);
        }

        lines.Sort(StringComparer.Ordinal);
        var canonical = string.Join("\n", lines);
        return Sha256Hex(canonical);
    }

    private static string Sha256Hex(string input)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input ?? "");
        var hash = sha.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
}
