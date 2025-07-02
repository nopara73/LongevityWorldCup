using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

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

        // ── per-athlete in-memory guess lists ──
        private readonly ConcurrentDictionary<string, List<(DateTime Timestamp, int Age)>> _athleteAgeGuesses
            = new(StringComparer.OrdinalIgnoreCase);

        // ── lock objects for each athlete’s list ──
        private readonly object _athleteAgeGuessesLock = new();

        public AthleteDataService(IWebHostEnvironment env)
        {
            _env = env;
            // Initial load
            LoadAthletesAsync().GetAwaiter().GetResult();

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

                var folder = Path.GetDirectoryName(file)!;         // e.g. "/.../wwwroot/athletes/yan_lin"
                var key = Path.GetFileName(folder);             // e.g. "yan_lin"

                // so we can look up this JsonObject later by slug
                athlete["AthleteSlug"] = key;

                // PROFILE PIC: look for "{key}.*" in that same folder
                var pic = Directory
                    .EnumerateFiles(folder, $"{key}.*", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                athlete["ProfilePic"] = pic is null
                    ? null
                    : $"/athletes/{key}/{Path.GetFileName(pic)}";

                // PROOFS: look for proof_*.ext
                var proofs = new JsonArray();
                var proofFiles = Directory
                    .EnumerateFiles(folder, "proof_*.*", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => ExtractNumber(Path.GetFileNameWithoutExtension(f)));
                foreach (var p in proofFiles)
                    proofs.Add($"/athletes/{key}/{Path.GetFileName(p)}");
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
            // get or create the list
            var list = _athleteAgeGuesses.GetOrAdd(athleteSlug, _ => []);

            lock (_athleteAgeGuessesLock)
            {
                // record
                list.Add((DateTime.UtcNow, ageGuess));

                // compute median
                var ages = list.Select(g => g.Age).OrderBy(a => a).ToArray();
                int cnt = ages.Length;
                double median = (cnt % 2 == 1)
                    ? ages[cnt / 2]
                    : (ages[cnt / 2 - 1] + ages[cnt / 2]) / 2.0;

                // find the matching JSON object
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
        /// Returns (Median, Count) for all guesses recorded so far for athleteSlug.
        /// </summary>
        public (double Median, int Count) GetCrowdStats(string athleteSlug)
        {
            if (_athleteAgeGuesses.TryGetValue(athleteSlug, out var list) && list.Count > 0)
            {
                var ages = list.Select(g => g.Age).OrderBy(a => a).ToArray();
                int cnt = ages.Length;
                double median = (cnt % 2 == 1)
                    ? ages[cnt / 2]
                    : (ages[cnt / 2 - 1] + ages[cnt / 2]) / 2.0;
                return (median, cnt);
            }

            return (0, 0);
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
            GC.SuppressFinalize(this);
        }
    }
}