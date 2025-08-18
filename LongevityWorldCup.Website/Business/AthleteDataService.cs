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
using System.Text.Json;

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
    CREATE TABLE IF NOT EXISTS Athletes (
        Key        TEXT PRIMARY KEY,
        AgeGuesses TEXT NOT NULL
    )";
                cmd.ExecuteNonQuery();

                // Try to add column (ignore if exists)
                cmd.CommandText = "ALTER TABLE Athletes ADD COLUMN JoinedAt TEXT;";
                try { cmd.ExecuteNonQuery(); } catch { /* column already exists */ }
            }

            // Initial load
            LoadAthletesAsync().GetAwaiter().GetResult();
            
            foreach (var athleteJson in Athletes.OfType<JsonObject>())
            {
                var athleteKey = athleteJson["AthleteSlug"]!.GetValue<string>();
                using var insertAthleteCmd = _sqliteConnection.CreateCommand();
                insertAthleteCmd.CommandText =
                    "INSERT OR IGNORE INTO Athletes (Key, AgeGuesses) VALUES (@key, @ages)";
                insertAthleteCmd.Parameters.AddWithValue("@key", athleteKey);
                insertAthleteCmd.Parameters.AddWithValue("@ages", "[]");
                insertAthleteCmd.ExecuteNonQuery();
            }
            
            // TODO: Remove later, just refills JoinedAt with better values
            BackfillJoinedAt();

            // Finally, set JoinedAt=now for any that are still null
            using (var fillNow = _sqliteConnection.CreateCommand())
            {
                fillNow.CommandText = "UPDATE Athletes SET JoinedAt=@now WHERE JoinedAt IS NULL OR JoinedAt=''";
                fillNow.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o"));
                fillNow.ExecuteNonQuery();
            }

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
                    BackupDatabase();
                    await Task.Delay(TimeSpan.FromHours(24)).ConfigureAwait(false);
                }
            });
        }
        
         private void BackfillJoinedAt()
        {
            try
            {
                using var cmd = _sqliteConnection.CreateCommand();
                var columnName = "JoinedAt";
                cmd.CommandText =
                $"""
                 BEGIN TRANSACTION;
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='alan_v' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-07-24T09:13:19.0000000Z' WHERE Key='alberto_perez_sciu' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='andreea_vuscan' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='andressa_lohana_de_almeida' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-08-08T08:39:32.0000000Z' WHERE Key='andre_rebolo' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='angela_buzzeo' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='anton_eich' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-06-21T06:01:30.0000000Z' WHERE Key='bas_pronk' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='beatriz_bevilaqua' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='bestape' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='biohacker_babe' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='brandon' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='charlotte_wong' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-07-16T08:39:44.0000000Z' WHERE Key='clayton_brooks' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='cody_hergenroeder' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-07-25T12:35:40.0000000Z' WHERE Key='cosmina_slattengren' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='dave_pascoe' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='david_ruiz_fernandez' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-08-08T08:39:32.0000000Z' WHERE Key='david_x' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-06-04T08:29:10.0000000Z' WHERE Key='devarajan_narayanan' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-07-16T08:46:05.0000000Z' WHERE Key='devin_neko' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='djstern' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='dr_thomas_irawan' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='eatbiohacklove' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='eng_wai_ong' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-05-28T07:24:35.0000000Z' WHERE Key='erin_easterly' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-28T06:27:23.0000000Z' WHERE Key='eugene' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='giorgio_zinetti' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='giuvana_salvador' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='god_dieux' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-06-06T07:39:57.0000000Z' WHERE Key='gui' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='healthoptimisers' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='inka_land' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-05-06T08:39:38.0000000Z' WHERE Key='ivan_morgunov' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-30T08:54:22.0000000Z' WHERE Key='jack_krone' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-05-31T09:20:20.0000000Z' WHERE Key='james_tauber' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='jason_sugar' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='jay_roach' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='jesse' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-07-17T12:05:44.0000000Z' WHERE Key='joao_paulo_longo' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='johan_sandgren' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='john_hannon' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-06-11T06:14:38.0000000Z' WHERE Key='john_prince' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='juan_robalino' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='june_hou' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-07-13T06:32:31.0000000Z' WHERE Key='kaytlyn_merchant' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-06-18T06:36:10.0000000Z' WHERE Key='keith_blondin' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-25T07:45:35.0000000Z' WHERE Key='larissa_schmeing' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='lbron' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='marcin' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='marcus' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='maria_olenina' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='max' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='max_eaimsirakul' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='mel_c' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='michael_lustgarten' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-05-13T10:52:02.0000000Z' WHERE Key='michelle_franz_montan' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='miguel_c' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='mind4u2cn' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='nils' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='nopara73' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='olga_vresca' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-05-09T06:34:00.0000000Z' WHERE Key='olga_wood' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='philipp_schmeing' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-07-17T12:11:58.0000000Z' WHERE Key='ping_long' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='ricardo_di_lazzaro_filho' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='richard_heck' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='richlee' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='rodrigo_quercia' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-06-12T08:08:00.0000000Z' WHERE Key='samantha_lin' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='satya_disha_dieux' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='scottbrylow' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-06-14T10:54:27.0000000Z' WHERE Key='serega' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-08-14T06:40:12.0000000Z' WHERE Key='sergey_vlasov' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='spiderius' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='stellar_madic' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-05-04T06:42:36.0000000Z' WHERE Key='swami_jupiter' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-05-10T06:47:56.0000000Z' WHERE Key='thiago_beber' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='tiat_lim' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='tone_vays' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-26T06:36:53.0000000Z' WHERE Key='troy_hale' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='veron' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='virgil_cain' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='vishal_rao' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='vuscan_horea' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='yan_lin' AND ({columnName} IS NULL OR {columnName}='');
                 UPDATE Athletes SET {columnName}='2025-04-24T08:15:39.0000000Z' WHERE Key='zdenek_sipek' AND ({columnName} IS NULL OR {columnName}='');
                 COMMIT;                   
                 """;
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                // ignored.
            }
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