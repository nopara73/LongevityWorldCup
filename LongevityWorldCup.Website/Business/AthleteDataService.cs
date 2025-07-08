using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business
{
    public class AthleteDataService : IDisposable
    {
        public JsonArray Athletes { get; private set; } = []; // Initialize to avoid nullability issue

        private readonly IWebHostEnvironment _env;
        private readonly FileSystemWatcher _athleteWatcher;
        private readonly SemaphoreSlim _reloadLock = new(1, 1);

        private CancellationTokenSource? _debounceCts;
        private static readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(100);

        private const string DatabaseFileName = "LongevityWorldCup.db";

        private readonly SqliteConnection _sqliteConnection;

        private const int MaxBackupFiles = 5;
        private readonly string _backupDir;

        public AthleteDataService(IWebHostEnvironment env)
        {
            _env = env;

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
        CREATE TABLE IF NOT EXISTS AthleteAgeGuesses (
            Id             INTEGER PRIMARY KEY AUTOINCREMENT,
            AthleteSlug    TEXT    NOT NULL,
            TimestampUtc   TEXT    NOT NULL,
            Age            INTEGER NOT NULL
        )";
                cmd.ExecuteNonQuery();
            }

            // Initial load
            LoadAthletesAsync().GetAwaiter().GetResult();

            // Hydrate persisted age‐guess stats from SQLite
            foreach (var athleteJson in Athletes.OfType<JsonObject>())
            {
                var slug = athleteJson["AthleteSlug"]!.GetValue<string>();
                var (median, count) = GetCrowdStats(slug);
                athleteJson["CrowdAge"] = median;
                athleteJson["CrowdCount"] = count;
            }

            // Watch the new per-athlete folders recursively
            var athletesDir = Path.Combine(env.WebRootPath, "athletes");
            _athleteWatcher = new FileSystemWatcher(athletesDir)
            {
                IncludeSubdirectories = true,
                // watch for changes to file‐names, directory‐names, and writes
                NotifyFilter = NotifyFilters.FileName
                 | NotifyFilters.DirectoryName
                 | NotifyFilters.LastWrite,
                Filter = "*.*"
            };
            _athleteWatcher.Changed += (s, e) => DebounceReload();
            _athleteWatcher.Created += (s, e) => DebounceReload();
            _athleteWatcher.Deleted += (s, e) => DebounceReload();
            _athleteWatcher.Renamed += (s, e) => DebounceReload();
            _athleteWatcher.EnableRaisingEvents = true;
            _athleteWatcher.Error += OnWatcherError;

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
                    }
                }
            });

            // kick off a daily backup + retention
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromHours(24)).ConfigureAwait(false);
                    BackupDatabase();
                }
            });
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
                using var insertCmd = _sqliteConnection.CreateCommand();
                insertCmd.CommandText =
                    "INSERT INTO AthleteAgeGuesses (AthleteSlug, TimestampUtc, Age) VALUES (@slug, @ts, @age)";
                insertCmd.Parameters.AddWithValue("@slug", athleteSlug);
                insertCmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.ToString("o"));
                insertCmd.Parameters.AddWithValue("@age", ageGuess);
                insertCmd.ExecuteNonQuery();

                // fetch all ages to compute median & count
                var ages = new List<int>();
                using var selectCmd = _sqliteConnection.CreateCommand();
                selectCmd.CommandText =
                    "SELECT Age FROM AthleteAgeGuesses WHERE AthleteSlug = @slug ORDER BY Age";
                selectCmd.Parameters.AddWithValue("@slug", athleteSlug);
                using var reader = selectCmd.ExecuteReader();
                while (reader.Read())
                    ages.Add(reader.GetInt32(0));

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
                var ages = new List<int>();
                using var cmd = _sqliteConnection.CreateCommand();
                cmd.CommandText =
                    "SELECT Age FROM AthleteAgeGuesses WHERE AthleteSlug = @slug ORDER BY Age";
                cmd.Parameters.AddWithValue("@slug", athleteSlug);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    ages.Add(reader.GetInt32(0));

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

        public void Dispose()
        {
            _athleteWatcher.Dispose();
            _reloadLock.Dispose();          // ← dispose the semaphore
            _debounceCts?.Dispose();       // ← dispose the CTS
            _sqliteConnection.Close();
            _sqliteConnection.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}