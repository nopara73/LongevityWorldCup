using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using System.Threading;

namespace LongevityWorldCup.Website.Business
{
    public class AthleteDataService : IDisposable
    {
        public JsonArray Athletes { get; private set; } = []; // Initialize to avoid nullability issue

        private readonly IWebHostEnvironment _env;
        private readonly FileSystemWatcher _jsonWatcher;
        private readonly FileSystemWatcher _profileWatcher;
        private readonly FileSystemWatcher _proofWatcher;
        private readonly object _reloadLock = new();

        public AthleteDataService(IWebHostEnvironment env)
        {
            _env = env;

            // Initial load
            LoadAthletes();

            // Watch Athletes.json
            var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "Data");
            _jsonWatcher = new FileSystemWatcher(dataDir, "Athletes.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
            };
            _jsonWatcher.Changed += OnSourceChanged;
            _jsonWatcher.Renamed += OnSourceChanged;
            _jsonWatcher.Deleted += OnSourceChanged;
            _jsonWatcher.EnableRaisingEvents = true;

            // Watch profile-pics directory
            var profileDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "profile-pics");
            _profileWatcher = new FileSystemWatcher(profileDir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                Filter = "*.*"
            };
            _profileWatcher.Changed += OnSourceChanged;
            _profileWatcher.Created += OnSourceChanged;
            _profileWatcher.Deleted += OnSourceChanged;
            _profileWatcher.Renamed += OnSourceChanged;
            _profileWatcher.EnableRaisingEvents = true;

            // Watch proofs directory
            var proofDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "proofs");
            _proofWatcher = new FileSystemWatcher(proofDir)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                Filter = "*.*"
            };
            _proofWatcher.Changed += OnSourceChanged;
            _proofWatcher.Created += OnSourceChanged;
            _proofWatcher.Deleted += OnSourceChanged;
            _proofWatcher.Renamed += OnSourceChanged;
            _proofWatcher.EnableRaisingEvents = true;
        }

        private void LoadAthletes()
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
                    Thread.Sleep(50);
                }
            }
        }

        private void OnSourceChanged(object sender, FileSystemEventArgs e)
        {
            lock (_reloadLock)
            {
                LoadAthletes();
            }
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
            GC.SuppressFinalize(this);
        }
    }
}