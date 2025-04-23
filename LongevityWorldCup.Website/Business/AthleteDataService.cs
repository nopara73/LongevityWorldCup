using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace LongevityWorldCup.Website.Business
{
    public class AthleteDataService : IDisposable
    {
        public JsonArray Athletes { get; private set; } = []; // Initialize to avoid nullability issue

        private readonly IWebHostEnvironment _env;
        private readonly FileSystemWatcher _jsonWatcher;
        private readonly FileSystemWatcher _profileWatcher;
        private readonly FileSystemWatcher _proofWatcher;
        private readonly SemaphoreSlim _reloadLock = new(1, 1);

        private CancellationTokenSource? _debounceCts;
        private static readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(100);

        public AthleteDataService(IWebHostEnvironment env)
        {
            _env = env;

            // Initial load
            LoadAthletesAsync().GetAwaiter().GetResult();

            // Watch Athletes.json
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            _jsonWatcher = new FileSystemWatcher(dataDir, "Athletes.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
            };
            _jsonWatcher.Changed += (s, e) => DebounceReload();
            _jsonWatcher.Renamed += (s, e) => DebounceReload();
            _jsonWatcher.Deleted += (s, e) => DebounceReload();
            _jsonWatcher.EnableRaisingEvents = true;
            _jsonWatcher.Error += OnWatcherError;

            // Watch profile-pics directory
            var profileDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "profile-pics");
            _profileWatcher = new FileSystemWatcher(profileDir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                Filter = "*.*"
            };
            _profileWatcher.Changed += (s, e) => DebounceReload();
            _profileWatcher.Created += (s, e) => DebounceReload();
            _profileWatcher.Deleted += (s, e) => DebounceReload();
            _profileWatcher.Renamed += (s, e) => DebounceReload();
            _profileWatcher.EnableRaisingEvents = true;
            _profileWatcher.Error += OnWatcherError;

            // Watch proofs directory
            var proofDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "proofs");
            _proofWatcher = new FileSystemWatcher(proofDir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                Filter = "*.*"
            };
            _proofWatcher.Changed += (s, e) => DebounceReload();
            _proofWatcher.Created += (s, e) => DebounceReload();
            _proofWatcher.Deleted += (s, e) => DebounceReload();
            _proofWatcher.Renamed += (s, e) => DebounceReload();
            _proofWatcher.EnableRaisingEvents = true;
            _proofWatcher.Error += OnWatcherError;
        }

        private async Task LoadAthletesAsync()
        {
            var jsonPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "Athletes.json");
            const int maxTries = 5;

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    // Open for read with shared access so we don't crash if file is still being written
                    using var fs = new FileStream(
                        jsonPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs);
                    string text = sr.ReadToEnd();
                    var root = JsonNode.Parse(text)!.AsArray();

                    foreach (JsonObject athlete in root.Cast<JsonObject>())
                    {
                        string name = athlete["Name"]!.GetValue<string>();
                        string fileKey = new string(
                            [.. name.ToLower().Where(c => !Path.GetInvalidFileNameChars().Contains(c))]
                        ).Replace(' ', '_');

                        // ProfilePic
                        var profileDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "profile-pics");
                        var profilePicPath = Directory
                            .EnumerateFiles(profileDir, $"{fileKey}_profile.*")
                            .OrderByDescending(File.GetLastWriteTimeUtc)
                            .FirstOrDefault();
                        athlete["ProfilePic"] = profilePicPath != null
                            ? "/assets/profile-pics/" + Path.GetFileName(profilePicPath)
                            : null;

                        // Proofs
                        var proofDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "proofs");
                        var proofs = new JsonArray();
                        var proofFiles = Directory
                            .EnumerateFiles(proofDir, $"{fileKey}_proof_*.*")
                            .OrderBy(f => ExtractNumber(Path.GetFileNameWithoutExtension(f)));
                        foreach (var file in proofFiles)
                            proofs.Add("/assets/proofs/" + Path.GetFileName(file));
                        athlete["Proofs"] = proofs;
                    }

                    Athletes = root;
                    break;
                }
                catch (IOException) when (attempt < maxTries)
                {
                    await Task.Delay(50);
                }
            }
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

        public void Dispose()
        {
            _jsonWatcher.Dispose();
            _profileWatcher.Dispose();
            _proofWatcher.Dispose();
            _reloadLock.Dispose();          // ← dispose the semaphore
            _debounceCts?.Dispose();       // ← dispose the CTS
            GC.SuppressFinalize(this);
        }
    }
}