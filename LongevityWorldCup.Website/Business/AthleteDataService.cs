using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using LongevityWorldCup.Website.Tools;
using System.Text.Json;
using System.Linq;
using System.Threading;
using System.IO;

namespace LongevityWorldCup.Website.Business;

public class AthleteDataService : IDisposable
{
    private readonly DateTime _serviceStartUtc = DateTime.UtcNow;

    // NEW: rolling window for new-athlete detection (currently ~1 month)
    private static readonly TimeSpan NewAthleteWindow = TimeSpan.FromDays(30);

    private const double RankEventScoreEpsilon = 0.0001;

    public JsonArray Athletes { get; private set; } = []; // Initialize to avoid nullability issue

    private readonly IWebHostEnvironment _env;
    private readonly EventDataService _eventDataService;
    private readonly FileSystemWatcher _athleteWatcher;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private CancellationTokenSource? _debounceCts;
    private static readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(100);

    private const string DatabaseFileName = "LongevityWorldCup.db";

    private readonly SqliteConnection _sqliteConnection;

    private const int MaxBackupFiles = 5;
    private readonly string _backupDir;

    private readonly string _athletesRootDir;
    private readonly object _pendingLock = new();
    private readonly HashSet<string> _pendingChangedSlugs = new(StringComparer.OrdinalIgnoreCase);

    public AthleteDataService(IWebHostEnvironment env, EventDataService eventDataService)
    {
        _env = env;
        _eventDataService = eventDataService;

        var dataDir = EnvironmentHelpers.GetDataDir();
        Directory.CreateDirectory(dataDir);

        // set up backup directory
        _backupDir = Path.Combine(dataDir, "Backups");
        Directory.CreateDirectory(_backupDir);

        var dbPath = Path.Combine(dataDir, DatabaseFileName);
        _sqliteConnection = new SqliteConnection($"Data Source={dbPath}");
        _sqliteConnection.Open();
        using (var cmd = _sqliteConnection.CreateCommand())
        {
            cmd.CommandText = @"
    CREATE TABLE IF NOT EXISTS Athletes (
        Key        TEXT PRIMARY KEY,
        AgeGuesses TEXT NOT NULL
    )";
            cmd.ExecuteNonQuery();

            // Try to add column (ignore if exists)
            cmd.CommandText = "ALTER TABLE Athletes ADD COLUMN JoinedAt TEXT;";
            try { cmd.ExecuteNonQuery(); } catch { /* column already exists */ }

            // Ensure Placements & CurrentPlacement columns exist
            cmd.CommandText = "PRAGMA table_info(Athletes);";
            using var r = cmd.ExecuteReader();
            var hasPlacements = false;
            var hasCurrentPlacement = false;
            var hasLastAgeDiff = false;
            while (r.Read())
            {
                var colName = r.GetString(1);
                if (string.Equals(colName, "Placements", StringComparison.OrdinalIgnoreCase))
                    hasPlacements = true;
                if (string.Equals(colName, "CurrentPlacement", StringComparison.OrdinalIgnoreCase))
                    hasCurrentPlacement = true;
                if (string.Equals(colName, "LastAgeDiff", StringComparison.OrdinalIgnoreCase))
                    hasLastAgeDiff = true;
            }

            if (!hasPlacements)
            {
                using var alter = _sqliteConnection.CreateCommand();
                alter.CommandText = "ALTER TABLE Athletes ADD COLUMN Placements TEXT NOT NULL DEFAULT '[]';";
                alter.ExecuteNonQuery();

                using var backfill = _sqliteConnection.CreateCommand();
                backfill.CommandText = "UPDATE Athletes SET Placements='[]' WHERE Placements IS NULL OR Placements='';";
                backfill.ExecuteNonQuery();
            }
            if (!hasCurrentPlacement)
            {
                using var alter2 = _sqliteConnection.CreateCommand();
                alter2.CommandText = "ALTER TABLE Athletes ADD COLUMN CurrentPlacement INTEGER NULL;";
                alter2.ExecuteNonQuery();
            }
            if (!hasLastAgeDiff)
            {
                using var alter3 = _sqliteConnection.CreateCommand();
                alter3.CommandText = "ALTER TABLE Athletes ADD COLUMN LastAgeDiff REAL NULL;";
                alter3.ExecuteNonQuery();
            }
        }

        // Initial load
        LoadAthletesAsync().GetAwaiter().GetResult();

        var newlyJoined = EnsureDbRowsForNewAthletes();

        // Finally, set JoinedAt=now for any that are still null
        if (newlyJoined.Count > 0)
        {
            using var fillNow = _sqliteConnection.CreateCommand();
            var keys = newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>()).ToList();
            var placeholders = string.Join(",", keys.Select((_, i) => $"@k{i}"));
            fillNow.CommandText = $"UPDATE Athletes SET JoinedAt=@now WHERE (JoinedAt IS NULL OR JoinedAt='') AND Key IN ({placeholders})";
            fillNow.Parameters.AddWithValue("@now", _serviceStartUtc.ToString("o"));
            for (int i = 0; i < keys.Count; i++) fillNow.Parameters.AddWithValue($"@k{i}", keys[i]);
            fillNow.ExecuteNonQuery();
        }

        // Hydrate persisted age‐guess stats from SQLite
        ReloadCrowdStats();
        HydratePlacementsIntoAthletesJson();
        HydrateNewFlagsIntoAthletesJson();
        HydrateCurrentPlacementIntoAthletesJson(); // NOTE: no DB persist here

        // Watch the new per-athlete folders recursively
        var athletesDir = Path.Combine(env.WebRootPath, "athletes");
        _athletesRootDir = athletesDir;
        _athleteWatcher = new FileSystemWatcher(athletesDir)
        {
            IncludeSubdirectories = true,
            // watch for changes to file‐names, directory‐names, and writes
            NotifyFilter = NotifyFilters.FileName
                           | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite,
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
            var payload = newlyJoined.Select(x =>
            {
                var slug = x.Athlete["AthleteSlug"]!.GetValue<string>();
                int? rank = null;
                var rp = x.Athlete["CurrentPlacement"];
                if (rp is JsonValue jv && jv.TryGetValue<int>(out var pos)) rank = pos;
                return (slug, x.JoinedAt, rank);
            });
            eventDataService.CreateJoinedEventsForAthletes(payload, skipIfExists: true);
        }

        // ⬇⬇⬇ Detect rank-ups on startup against the LAST persisted snapshot, then persist the new snapshot
        DetectAndEmitRankUpsAgainstDb(newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>()));

        // Start a poll‐loop to detect external DB writes and reload stats
        _ = Task.Run(async () =>
        {
            var lastWrite = File.GetLastWriteTimeUtc(dbPath);
            while (true)   // you could wire this to a CancellationToken if you like
            {
                await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

                var newWrite = File.GetLastWriteTimeUtc(dbPath);
                if (newWrite > lastWrite)
                {
                    lastWrite = newWrite;
                    ReloadCrowdStats();
                    HydratePlacementsIntoAthletesJson();
                    HydrateNewFlagsIntoAthletesJson();
                    HydratePlacementsIntoAthletesJson();
                    HydrateCurrentPlacementIntoAthletesJson(); // NOTE: no DB persist here
                    PushAthleteDirectoryToEvents();
                }
            }
        });

        // kick off a daily backup + retention
        _ = Task.Run(async () =>
        {
            while (true)
            {
                BackupDatabase();
                await Task.Delay(TimeSpan.FromHours(24)).ConfigureAwait(false);
            }
        });

        PushAthleteDirectoryToEvents();
    }

    public IEnumerable<(JsonObject Athlete, DateTime JoinedAt)> GetAthletesJoinedData()
    {
        var result = new List<(JsonObject, DateTime)>();
        using var cmd = _sqliteConnection.CreateCommand();
        cmd.CommandText = "SELECT Key, JoinedAt FROM Athletes";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            var joinedAt = DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind);

            var athleteJson = Athletes
                .OfType<JsonObject>()
                .FirstOrDefault(a =>
                    string.Equals(a["AthleteSlug"]?.GetValue<string>(), key, StringComparison.OrdinalIgnoreCase));

            if (athleteJson != null)
                result.Add((athleteJson, joinedAt));
        }

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
            for (int i = 0; ; i++)
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

            var folder = Path.GetDirectoryName(file)!;         // e.g. "/.../wwwroot/athletes/michelle_franz-montan"
            var folderName = Path.GetFileName(folder);             // e.g. "michelle_franz-montan"

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

            athletesRoot.Add(athlete);
        }

        Athletes = athletesRoot;
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
            catch (TaskCanceledException) { }
        }, CancellationToken.None);
    }

    private async Task OnSourceChangedAsync(object sender, FileSystemEventArgs? e)
    {
        await _reloadLock.WaitAsync();
        try
        {
            await LoadAthletesAsync();

            var newlyJoined = EnsureDbRowsForNewAthletes();

            ReloadCrowdStats();
            HydratePlacementsIntoAthletesJson();
            HydrateNewFlagsIntoAthletesJson();
            HydratePlacementsIntoAthletesJson();
            HydrateCurrentPlacementIntoAthletesJson(); // NOTE: no DB persist here

            if (newlyJoined.Count > 0)
            {
                var payload = newlyJoined.Select(x =>
                {
                    var slug = x.Athlete["AthleteSlug"]!.GetValue<string>();
                    int? rank = null;
                    var rp = x.Athlete["CurrentPlacement"];
                    if (rp is JsonValue jv && jv.TryGetValue<int>(out var pos)) rank = pos;
                    return (slug, x.JoinedAt, rank);
                });
                _eventDataService.CreateJoinedEventsForAthletes(payload, skipIfExists: true);
            }

            // Compare against DB snapshot and emit rank-ups (athletes can only move up or hold)
            DetectAndEmitRankUpsAgainstDb(newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>()));
            PushAthleteDirectoryToEvents();
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>
    /// Fired if the FileSystemWatcher’s internal buffer overflows or another error occurs.
    /// You can log it or recreate the watcher here.
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // Example: log and restart the watcher
        var watcher = (FileSystemWatcher)sender;

        // temporarily disable and re-enable to clear the buffer
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
        lock (_sqliteConnection)
        {
            // insert the new guess
            using var selectJsonCmd = _sqliteConnection.CreateCommand();
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
            var updatedJson = JsonSerializer.Serialize(ageArray);

            using var updateCmd = _sqliteConnection.CreateCommand();
            updateCmd.CommandText =
                "UPDATE Athletes SET AgeGuesses = @ages WHERE Key = @key";
            updateCmd.Parameters.AddWithValue("@ages", updatedJson);
            updateCmd.Parameters.AddWithValue("@key", athleteSlug);
            updateCmd.ExecuteNonQuery();

            // fetch all ages to compute median & count
            using var selectAgesJson = _sqliteConnection.CreateCommand();
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

            // update the in-memory JSON
            var athleteJson = Athletes
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

        PushAthleteDirectoryToEvents();
    }

    /// <summary>
    /// Re‐reads all medians & counts from SQLite and updates the in‐memory JSON.
    /// </summary>
    public void ReloadCrowdStats()
    {
        lock (_sqliteConnection)
        {
            foreach (var athleteJson in Athletes.OfType<JsonObject>())
            {
                var slug = athleteJson["AthleteSlug"]!.GetValue<string>();
                var (median, count) = GetCrowdStats(slug);
                athleteJson["CrowdAge"] = median;
                athleteJson["CrowdCount"] = count;
            }
        }
    }

    /// <summary>
    /// Creates a timestamped backup, then prunes old backups so only the newest
    /// `MaxBackupFiles` remain.
    /// </summary>
    public void BackupDatabase()
    {
        // checkpoint WAL so the main file is up-to-date
        using var chk = _sqliteConnection.CreateCommand();
        chk.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        chk.ExecuteNonQuery();

        // copy into a new file
        var backupFile = Path.Combine(
            _backupDir,
            $"LongevityWC_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db");
        lock (_sqliteConnection)
        {
            using var dest = new SqliteConnection($"Data Source={backupFile}");
            dest.Open();
            _sqliteConnection.BackupDatabase(dest);
        }

        // prune old backups
        var files = Directory
            .GetFiles(_backupDir, "*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(MaxBackupFiles);
        foreach (var f in files)
            f.Delete();
    }

    /// <summary>
    /// Returns (Median, Count) for all guesses recorded so far for athleteSlug.
    /// </summary>
    public (double Median, int Count) GetCrowdStats(string athleteSlug)
    {
        lock (_sqliteConnection)
        {
            using var selectAgesJson = _sqliteConnection.CreateCommand();
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
        }
    }

    public int GetActualAge(string athleteSlug)
    {
        // find the athlete JSON by slug
        var athleteJson = Athletes
            .OfType<JsonObject>()
            .FirstOrDefault(o => string.Equals(
                o["AthleteSlug"]?.GetValue<string>(),
                athleteSlug,
                StringComparison.OrdinalIgnoreCase));
        if (athleteJson is null)
            return 0;

        // read the DOB node
        var dobNode = athleteJson["DateOfBirth"]?.AsObject();
        if (dobNode is null)
            return 0;

        // extract year/month/day
        int year = dobNode["Year"]!.GetValue<int>();
        int month = dobNode["Month"]!.GetValue<int>();
        int day = dobNode["Day"]!.GetValue<int>();

        // compute age as of today (UTC)
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
        var results = new List<(double AgeReduction, DateTime DobUtc, string Name, JsonObject Obj)>();

        foreach (var athlete in Athletes.OfType<JsonObject>())
        {
            var name = athlete["Name"]?.GetValue<string>() ?? "";
            var slug = athlete["AthleteSlug"]?.GetValue<string>() ?? "";

            var dobNode = athlete["DateOfBirth"]?.AsObject();
            if (dobNode is null) continue;

            int y = dobNode["Year"]!.GetValue<int>();
            int m = dobNode["Month"]!.GetValue<int>();
            int d = dobNode["Day"]!.GetValue<int>();
            var dobUtc = new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc);

            double AgeYears(DateTime date) => (date.Date - dobUtc.Date).TotalDays / 365.2425;

            var chronoToday = Math.Round(AgeYears(asOf), 2);
            double lowestPheno = double.PositiveInfinity;

            if (athlete["Biomarkers"] is JsonArray biomArr)
            {
                foreach (var entry in biomArr.OfType<JsonObject>())
                {
                    var entryDate = asOf;
                    var ds = entry["Date"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(ds) &&
                        DateTime.TryParse(ds, null,
                            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    {
                        entryDate = DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc);
                    }

                    var ageAtEntry = AgeYears(entryDate);

                    if (TryGet(entry, "AlbGL", out var alb) &&
                        TryGet(entry, "CreatUmolL", out var creat) &&
                        TryGet(entry, "GluMmolL", out var glu) &&
                        TryGet(entry, "CrpMgL", out var crpMgL) &&
                        TryGet(entry, "Wbc1000cellsuL", out var wbc) &&
                        TryGet(entry, "LymPc", out var lym) &&
                        TryGet(entry, "McvFL", out var mcv) &&
                        TryGet(entry, "RdwPc", out var rdw) &&
                        TryGet(entry, "AlpUL", out var alp) &&
                        crpMgL > 0)
                    {
                        var ph = Tools.PhenoAgeHelper.CalculatePhenoAgeFromRaw(
                            ageAtEntry, alb, creat, glu, crpMgL, wbc, lym, mcv, rdw, alp);

                        if (!double.IsNaN(ph) && !double.IsInfinity(ph) && ph < lowestPheno)
                            lowestPheno = ph;
                    }
                }
            }

            if (double.IsNaN(lowestPheno) || double.IsInfinity(lowestPheno))
                lowestPheno = chronoToday;

            var ageDiff = Math.Round(lowestPheno - chronoToday, 2);

            var obj = new JsonObject
            {
                ["AthleteSlug"] = slug,
                ["Name"] = name,
                ["ChronologicalAge"] = chronoToday,
                ["LowestPhenoAge"] = Math.Round(lowestPheno, 2),
                ["AgeDifference"] = ageDiff
            };

            results.Add((ageDiff, dobUtc, name, obj));
        }

        var arr = new JsonArray();
        foreach (var o in SortByCompetitionRules(results).Select(t => t.Obj))
            arr.Add(o);

        return arr;

        static bool TryGet(JsonObject o, string key, out double v)
        {
            v = 0;
            try
            {
                var n = o[key];
                if (n is null) return false;
                v = n.GetValue<double>();
                return !double.IsNaN(v) && !double.IsInfinity(v);
            }
            catch { return false; }
        }
    }

    private static IOrderedEnumerable<(double AgeReduction, DateTime DobUtc, string Name, JsonObject Obj)>
        SortByCompetitionRules(IEnumerable<(double AgeReduction, DateTime DobUtc, string Name, JsonObject Obj)> rows)
    {
        return rows
            .OrderBy(t => t.AgeReduction)                 // 1) more negative is better
            .ThenBy(t => t.DobUtc)                        // 2) older (earlier DOB) wins
            .ThenBy(t => t.Name, StringComparer.Ordinal); // 3) alphabetical
    }

    public int?[] GetPlacements(string athleteSlug)
    {
        lock (_sqliteConnection)
        {
            using var cmd = _sqliteConnection.CreateCommand();
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
        }
    }

    public void SetPlacements(string athleteSlug, int?[] placements)
    {
        if (placements is null) throw new ArgumentNullException(nameof(placements));
        if (placements.Length != 4) placements = new[] { placements.ElementAtOrDefault(0), placements.ElementAtOrDefault(1), placements.ElementAtOrDefault(2), placements.ElementAtOrDefault(3) };

        var json = JsonSerializer.Serialize(placements);

        lock (_sqliteConnection)
        {
            using var cmd = _sqliteConnection.CreateCommand();
            cmd.CommandText = "UPDATE Athletes SET Placements=@p WHERE Key=@k";
            cmd.Parameters.AddWithValue("@p", json);
            cmd.Parameters.AddWithValue("@k", athleteSlug);
            cmd.ExecuteNonQuery();
        }

        var athleteJson = Athletes
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
        foreach (var athleteJson in Athletes.OfType<JsonObject>())
        {
            var slug = athleteJson["AthleteSlug"]!.GetValue<string>();
            var p = GetPlacements(slug);
            var arr = new JsonArray();
            foreach (var v in p) arr.Add(v is int x ? JsonValue.Create(x) : null);
            athleteJson["Placements"] = arr;
        }
    }

    // NEW: compute IsNew from SQLite JoinedAt using the NewAthleteWindow
    private void HydrateNewFlagsIntoAthletesJson()
    {
        lock (_sqliteConnection)
        {
            var cutoffUtc = DateTime.UtcNow - NewAthleteWindow;

            using var cmd = _sqliteConnection.CreateCommand();
            cmd.CommandText = "SELECT Key, JoinedAt FROM Athletes";

            var joinedByKey = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    var joinedAtText = reader.IsDBNull(1) ? null : reader.GetString(1);
                    if (string.IsNullOrWhiteSpace(joinedAtText)) continue;

                    if (DateTime.TryParse(joinedAtText, null, System.Globalization.DateTimeStyles.RoundtripKind, out var joinedAt))
                    {
                        joinedByKey[key] = joinedAt;
                    }
                }
            }

            foreach (var athleteJson in Athletes.OfType<JsonObject>())
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

        foreach (var athlete in Athletes.OfType<JsonObject>())
        {
            var slug = athlete["AthleteSlug"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(slug) && map.TryGetValue(slug, out var pos))
                athlete["CurrentPlacement"] = pos;
            else
                athlete["CurrentPlacement"] = null;
        }

        // IMPORTANT: we DO NOT persist the snapshot here anymore.
        // Persistence must only happen AFTER we compare and possibly emit events.
    }

    public void Dispose()
    {
        _athleteWatcher.Dispose();
        _reloadLock.Dispose();          // ← dispose the semaphore
        _debounceCts?.Dispose();       // ← dispose the CTS
        _sqliteConnection.Close();
        _sqliteConnection.Dispose();
        GC.SuppressFinalize(this);
    }

    private List<(JsonObject Athlete, DateTime JoinedAt)> EnsureDbRowsForNewAthletes()
    {
        var newlyJoined = new List<(JsonObject, DateTime)>();
        foreach (var athleteJson in Athletes.OfType<JsonObject>())
        {
            var slug = athleteJson["AthleteSlug"]!.GetValue<string>();
            using var cmd = _sqliteConnection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO Athletes (Key, AgeGuesses, JoinedAt) VALUES (@k, @ages, @joined)";
            cmd.Parameters.AddWithValue("@k", slug);
            cmd.Parameters.AddWithValue("@ages", "[]");
            cmd.Parameters.AddWithValue("@joined", _serviceStartUtc.ToString("o"));
            var rows = cmd.ExecuteNonQuery();
            if (rows == 1)
            {
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
        lock (_sqliteConnection)
        {
            using var cmd = _sqliteConnection.CreateCommand();
            cmd.CommandText = "SELECT Key, CurrentPlacement FROM Athletes";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                int? cp = r.IsDBNull(1) ? (int?)null : r.GetInt32(1);
                map[key] = cp;
            }
        }
        return map;
    }

    private Dictionary<string, double?> LoadStoredLastAgeDifferences()
    {
        var map = new Dictionary<string, double?>(StringComparer.OrdinalIgnoreCase);
        lock (_sqliteConnection)
        {
            using var cmd = _sqliteConnection.CreateCommand();
            cmd.CommandText = "SELECT Key, LastAgeDiff FROM Athletes";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                double? val = r.IsDBNull(1) ? (double?)null : r.GetDouble(1);
                map[key] = val;
            }
        }
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
                if (o["AgeDifference"] is JsonValue jv && jv.TryGetValue<double>(out var diff) && !double.IsNaN(diff) && !double.IsInfinity(diff))
                    map[slug] = diff;
            }
        }
        return map;
    }

    private void PersistCurrentPlacementsSnapshot(Dictionary<string, int> current, Dictionary<string, double> currentAgeDiffs)
    {
        lock (_sqliteConnection)
        {
            using var tx = _sqliteConnection.BeginTransaction();
            using (var clear = _sqliteConnection.CreateCommand())
            {
                clear.Transaction = tx;
                clear.CommandText = "UPDATE Athletes SET CurrentPlacement=NULL";
                clear.ExecuteNonQuery();
            }
            using (var upd = _sqliteConnection.CreateCommand())
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
        }
    }

    /// <summary>
    /// Compares current top-10 against the previously persisted snapshot in SQLite,
    /// emits NewRank events for improvements (including entering top-10, carrying who was previously on that position),
    /// then persists the new snapshot (all placements).
    /// If there is NO previously persisted placement (all NULL) => first run: do NOT emit events, only persist.
    /// </summary>
    private void DetectAndEmitRankUpsAgainstDb(IEnumerable<string>? newcomerSlugs)
    {
        var before = LoadStoredCurrentPlacements();

        // FIRST RUN GUARD: if there's no non-null placement in DB, we have no baseline yet.
        // In that case, don't emit any events — just persist the current snapshot and return.
        var hasAnyBaseline = before.Values.Any(v => v.HasValue);
        if (!hasAnyBaseline)
        {
            var initialSnapshot = BuildRankMap(); // full table
            var initialDiffs = BuildAgeDiffMap();
            PersistCurrentPlacementsSnapshot(initialSnapshot, initialDiffs);
            return;
        }

        var newcomers = newcomerSlugs?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var beforeDiffs = LoadStoredLastAgeDifferences();
        var nowDiffs = BuildAgeDiffMap();

        // Build reverse map of the previous snapshot: rank -> slug
        var beforeByRank = new Dictionary<int, string>(capacity: before.Count);
        foreach (var kv in before)
        {
            if (kv.Value is int rank && rank >= 1)
                beforeByRank[rank] = kv.Key;
        }

        var afterTop10 = BuildRankMap(limit: 10);
        var nowUtc = DateTime.UtcNow;

        // NEW: include ReplacedSlug in the payload
        var changes = new List<(string AthleteSlug, DateTime OccurredAtUtc, int Rank, string? ReplacedSlug)>();

        foreach (var kv in afterTop10)
        {
            if (newcomers.Contains(kv.Key)) continue; // Joined path already handles initial rank event

            var slug = kv.Key;
            var newRank = kv.Value; // 1..10

            // Who previously held this exact rank (in the baseline)?
            string? replacedSlug = null;
            if (beforeByRank.TryGetValue(newRank, out var prevHolder) &&
                !string.Equals(prevHolder, slug, StringComparison.OrdinalIgnoreCase))
            {
                replacedSlug = prevHolder;
            }

            var scoreImproved =
    beforeDiffs.TryGetValue(slug, out var prevDiffNullable) &&
    prevDiffNullable.HasValue &&
    nowDiffs.TryGetValue(slug, out var currDiff) &&
    currDiff < prevDiffNullable.Value - RankEventScoreEpsilon;

            if ((!before.TryGetValue(slug, out var prev) || prev is null || prev > 10))
            {
                if (scoreImproved)
                    changes.Add((slug, nowUtc, newRank, replacedSlug));
            }
            else if (newRank < prev.Value)
            {
                if (scoreImproved)
                    changes.Add((slug, nowUtc, newRank, replacedSlug));
            }
        }

        if (changes.Count > 0)
            _eventDataService.CreateNewRankEvents(changes, skipIfExists: true);

        // Persist full snapshot AFTER emitting events
        var afterAll = BuildRankMap(); // full table
        var afterDiffs = BuildAgeDiffMap();
        PersistCurrentPlacementsSnapshot(afterAll, afterDiffs);
    }

    private void PushAthleteDirectoryToEvents()
    {
        var list = new List<(string Slug, string Name, int? CurrentRank)>();
        foreach (var o in Athletes.OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;
            var name = o["Name"]?.GetValue<string>() ?? "";
            int? rank = null;
            var rp = o["CurrentPlacement"];
            if (rp is JsonValue jv && jv.TryGetValue<int>(out var pos)) rank = pos;
            list.Add((slug, name, rank));
        }
        _eventDataService.SetAthleteDirectory(list);
    }
}