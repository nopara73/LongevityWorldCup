using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using LongevityWorldCup.Website.Tools;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace LongevityWorldCup.Website.Business;

public interface IAthleteSnapshotProvider
{
    JsonArray GetAthletesSnapshot();
}

public sealed record CrowdAgeLeaderboardEntry(
    string Slug,
    string Name,
    int Rank,
    double CrowdAge,
    double CrowdAgeDifference,
    int CrowdCount);

public sealed record AgeImprovementLeaderboardEntry(
    string Slug,
    string Name,
    string Clock,
    int Rank,
    double Improvement,
    double AgeReduction);

public sealed record BiologicalAgeLeaderboardEntry(
    string Slug,
    string Name,
    string Clock,
    int Rank,
    double BiologicalAge,
    double AgeReduction);

public class AthleteDataService : IAthleteSnapshotProvider, IDisposable
{
    private static readonly Regex IsoDateLike = new(@"^\d{4}-\d{1,2}-\d{1,2}$", RegexOptions.Compiled);
    private static readonly HashSet<string> ProfileImageExtensions =
        new([".webp", ".png", ".jpg", ".jpeg"], StringComparer.OrdinalIgnoreCase);
    internal static readonly IReadOnlyList<int> AthleteCountMilestoneThresholds =
    [
        10,
        42, 69, 100, 123,
        200, 222, 250, 256, 300, 404, 500, 666, 777, 888, 999,
        1000, 1024, 1234, 1337, 1500, 1618,
        2000, 2048, 2222, 2500, 3000, 3141, 3333, 4000, 4444,
        5000, 5555, 6969, 7500, 8008, 8888, 9001, 9999, 10000,
        11111, 12345, 22222, 54321
    ];
    private readonly DateTime _serviceStartUtc = DateTime.UtcNow;
    private static readonly TimeSpan NewAthleteWindow = TimeSpan.FromDays(30);
    private const int EventThumbSizePx = 96;
    private const int EventThumbQuality = 70;
    private const int LeaderboardThumbSizePx = 320;
    private const int LeaderboardThumbQuality = 84;
    private const int CrowdAgeLeaderboardMinimumGuessCount = 100;
    private static readonly TimeSpan GeneratedProfileAssetRetention = TimeSpan.FromDays(7);
    private const string ProfileImageIdProperty = "ProfileImageId";
    private const string LegacyWithoutProfileImageId = "legacy-without-profile-image";

    private JsonArray _athletes = []; // Initialize to avoid nullability issue

    public JsonArray Athletes
    {
        get
        {
            lock (_athletesJsonLock)
                return (JsonArray)_athletes.DeepClone();
        }
    }

    public IReadOnlySet<string> GetActiveAthleteSlugs()
    {
        lock (_athletesJsonLock)
        {
            return _athletes
                .OfType<JsonObject>()
                .Select(a => a["AthleteSlug"]?.GetValue<string>())
                .Where(slug => !string.IsNullOrWhiteSpace(slug))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)!;
        }
    }

    private readonly IWebHostEnvironment _env;
    private readonly EventDataService _eventDataService;
    private readonly FileSystemWatcher _athleteWatcher;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private readonly DrainableOperationLifetime _reloadOperations = new(nameof(AthleteDataService));
    private readonly CancellationTokenSource _reloadWorkerCts = new();
    private readonly SemaphoreSlim _reloadSignal = new(0, 1);
    private readonly Task _reloadWorkerTask;
    private readonly ILogger<AthleteDataService>? _logger;
    private int _disposed;
    private static readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(100);

    private const string DatabaseFileName = "LongevityWorldCup.db";

    private readonly DatabaseManager _db;

    private readonly string _athletesRootDir;
    private readonly string _profileThumbDir;
    private readonly string _publishedProfileDir;
    private readonly object _pendingLock = new();
    private readonly HashSet<string> _pendingChangedSlugs = new(StringComparer.OrdinalIgnoreCase);

    // single-column biomarker/test signature to detect new/changed test submissions
    private const string TestSigColumn = "TestSig";
    private const string HadBortzColumn = "HadBortz";
    private const string LastLowestPhenoAgeColumn = "LastLowestPhenoAge";
    private const string LastLowestBortzAgeColumn = "LastLowestBortzAge";
    private const string LastLowestPhenoAgeDateColumn = "LastLowestPhenoAgeDate";
    private const string LastLowestBortzAgeDateColumn = "LastLowestBortzAgeDate";
    private const string CrowdAgeProfileImageIdColumn = "CrowdAgeProfileImageId";
    private const string CrowdAgeTop10PlacementColumn = "CrowdAgeTop10Placement";
    private const string PhenoImprovementTop10PlacementColumn = "PhenoImprovementTop10Placement";
    private const string BortzImprovementTop10PlacementColumn = "BortzImprovementTop10Placement";

    // NEW: notify listeners (e.g., BadgeDataService) after reloads
    public event Action? AthletesChanged;

    private readonly object _athletesJsonLock = new();

    public AthleteDataService(
        IWebHostEnvironment env,
        EventDataService eventDataService,
        DatabaseManager db,
        ILogger<AthleteDataService>? logger = null)
    {
        _env = env;
        _eventDataService = eventDataService;
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger;

        var dataDir = EnvironmentHelpers.GetDataDir();
        Directory.CreateDirectory(dataDir);
        _profileThumbDir = Path.Combine(env.WebRootPath, "generated", "thumbs", "athletes");
        Directory.CreateDirectory(_profileThumbDir);
        _publishedProfileDir = Path.Combine(env.WebRootPath, "generated", "profiles", "athletes");
        Directory.CreateDirectory(_publishedProfileDir);

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

                TryAddAthletesColumn(sqlite, "JoinedAt TEXT");

                // Ensure Placements, CurrentPlacement, LastAgeDiff columns exist
                cmd.CommandText = "PRAGMA table_info(Athletes);";
                var hasPlacements = false;
                var hasCurrentPlacement = false;
                var hasLastAgeDiff = false;
                var hasTestSig = false; // track if our signature column exists
                var hasHadBortz = false;
                var hasLastLowestPhenoAge = false;
                var hasLastLowestBortzAge = false;
                var hasLastLowestPhenoAgeDate = false;
                var hasLastLowestBortzAgeDate = false;
                var hasCrowdAgeProfileImageId = false;
                var hasCrowdAgeTop10Placement = false;
                var hasPhenoImprovementTop10Placement = false;
                var hasBortzImprovementTop10Placement = false;
                using (var r = cmd.ExecuteReader())
                {
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
                        if (string.Equals(colName, HadBortzColumn, StringComparison.OrdinalIgnoreCase))
                            hasHadBortz = true;
                        if (string.Equals(colName, LastLowestPhenoAgeColumn, StringComparison.OrdinalIgnoreCase))
                            hasLastLowestPhenoAge = true;
                        if (string.Equals(colName, LastLowestBortzAgeColumn, StringComparison.OrdinalIgnoreCase))
                            hasLastLowestBortzAge = true;
                        if (string.Equals(colName, LastLowestPhenoAgeDateColumn, StringComparison.OrdinalIgnoreCase))
                            hasLastLowestPhenoAgeDate = true;
                        if (string.Equals(colName, LastLowestBortzAgeDateColumn, StringComparison.OrdinalIgnoreCase))
                            hasLastLowestBortzAgeDate = true;
                        if (string.Equals(colName, CrowdAgeProfileImageIdColumn, StringComparison.OrdinalIgnoreCase))
                            hasCrowdAgeProfileImageId = true;
                        if (string.Equals(colName, CrowdAgeTop10PlacementColumn, StringComparison.OrdinalIgnoreCase))
                            hasCrowdAgeTop10Placement = true;
                        if (string.Equals(colName, PhenoImprovementTop10PlacementColumn, StringComparison.OrdinalIgnoreCase))
                            hasPhenoImprovementTop10Placement = true;
                        if (string.Equals(colName, BortzImprovementTop10PlacementColumn, StringComparison.OrdinalIgnoreCase))
                            hasBortzImprovementTop10Placement = true;
                    }
                }

                if (!hasPlacements)
                {
                    TryAddAthletesColumn(sqlite, "Placements TEXT NOT NULL DEFAULT '[]'");

                    using var backfill = sqlite.CreateCommand();
                    backfill.CommandText = "UPDATE Athletes SET Placements='[]' WHERE Placements IS NULL OR Placements='';";
                    backfill.ExecuteNonQuery();
                }

                if (!hasCurrentPlacement)
                {
                    TryAddAthletesColumn(sqlite, "CurrentPlacement INTEGER NULL");
                }

                if (!hasLastAgeDiff)
                {
                    TryAddAthletesColumn(sqlite, "LastAgeDiff REAL NULL");
                }

                // add single signature column (one-time)
                if (!hasTestSig)
                {
                    TryAddAthletesColumn(sqlite, $"{TestSigColumn} TEXT NULL");
                }

                if (!hasHadBortz)
                {
                    TryAddAthletesColumn(sqlite, $"{HadBortzColumn} INTEGER NULL");
                }

                if (!hasLastLowestPhenoAge)
                {
                    TryAddAthletesColumn(sqlite, $"{LastLowestPhenoAgeColumn} REAL NULL");
                }

                if (!hasLastLowestBortzAge)
                {
                    TryAddAthletesColumn(sqlite, $"{LastLowestBortzAgeColumn} REAL NULL");
                }

                if (!hasLastLowestPhenoAgeDate)
                {
                    TryAddAthletesColumn(sqlite, $"{LastLowestPhenoAgeDateColumn} TEXT NULL");
                }

                if (!hasLastLowestBortzAgeDate)
                {
                    TryAddAthletesColumn(sqlite, $"{LastLowestBortzAgeDateColumn} TEXT NULL");
                }

                if (!hasCrowdAgeProfileImageId)
                {
                    TryAddAthletesColumn(sqlite, $"{CrowdAgeProfileImageIdColumn} TEXT NULL");
                }

                if (!hasCrowdAgeTop10Placement)
                {
                    TryAddAthletesColumn(sqlite, $"{CrowdAgeTop10PlacementColumn} INTEGER NULL");
                }

                if (!hasPhenoImprovementTop10Placement)
                {
                    TryAddAthletesColumn(sqlite, $"{PhenoImprovementTop10PlacementColumn} INTEGER NULL");
                }

                if (!hasBortzImprovementTop10Placement)
                {
                    TryAddAthletesColumn(sqlite, $"{BortzImprovementTop10PlacementColumn} INTEGER NULL");
                }
            }
        });

        // Initial load
        _athletes = LoadAthletesAsync().GetAwaiter().GetResult();

        var newlyJoined = EnsureDbRowsForNewAthletes();

        // Existing guesses predate profile-image versioning. Treat the image that is
        // public at migration time as their continuity baseline; future image changes
        // then create a clean boundary without deleting the historical guesses.
        SyncAgeGuessProfileImageIds(migrateLegacyGuesses: true);

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
        ReloadCrowdStatsCore();
        SyncCrowdAgeTop10Placements(emitEvents: false);
        HydrateAgeImprovementIntoAthletesJson();
        HydrateNewFlagsIntoAthletesJson();
        HydrateCurrentPlacementIntoAthletesJson(); // NOTE: no DB persist here
        HydrateBadgesIntoAthletesJson();           // badges into athlete JSON

        // persist biomarker/test signature for all athletes (so we can detect new/changed tests later)
        var changedSigsAtStartup = SyncBiomarkerSignatures(); // returns slugs whose signatures changed
        SyncAgeImprovementTop10Placements(emitEvents: true, eventSubjectSlugs: changedSigsAtStartup);
        HydratePlacementsIntoAthletesJson();
        var becameProAtStartup = SyncProTrackStates(newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>()));
        var bioAgeImprovementsAtStartup = SyncBestBioAgeStates(
            changedSigsAtStartup,
            newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>()));

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

        if (becameProAtStartup.Count > 0)
        {
            eventDataService.CreateBecameProEvents(becameProAtStartup, skipIfExists: true);
        }

        if (bioAgeImprovementsAtStartup.Count > 0)
        {
            eventDataService.CreateBiologicalAgeImprovementEvents(bioAgeImprovementsAtStartup, skipIfExists: true);
        }

        DetectAndEmitRankUpsForSlugs(
            changedSlugs: changedSigsAtStartup,
            newcomerSlugs: newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>())
        );

        DetectAndEmitAthleteCountMilestones(); // emit milestones retroactively and at startup

        _db.DatabaseChanged += OnDatabaseChanged;

        PushAthleteDirectoryToEvents();

        // Close the startup gap between the initial snapshot and watcher activation.
        // Run after constructor initialization so the rescan cannot overlap startup work.
        _reloadWorkerTask = ProcessReloadRequestsAsync(_reloadWorkerCts.Token);
        DebounceReload();
    }

    private static void TryAddAthletesColumn(SqliteConnection sqlite, string columnDefinition)
    {
        using var alter = sqlite.CreateCommand();
        alter.CommandText = $"ALTER TABLE Athletes ADD COLUMN {columnDefinition};";
        try
        {
            alter.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (IsDuplicateColumnException(ex))
        {
            // Parallel test hosts can race between PRAGMA table_info and ALTER TABLE.
        }
    }

    private static bool IsDuplicateColumnException(SqliteException ex)
    {
        return ex.SqliteErrorCode == 1 &&
               ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase);
    }

    private void OnDatabaseChanged()
    {
        using var operation = _reloadOperations.TryEnter();
        if (operation is null)
            return;

        _reloadLock.Wait();
        try
        {
            ReloadCrowdStatsCore();
            SyncCrowdAgeTop10Placements(emitEvents: false);
            HydrateAgeImprovementIntoAthletesJson();
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

    private async Task<JsonArray> LoadAthletesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Build up a JsonArray by reading every athlete.json under wwwroot/athletes
        var athletesRoot = new JsonArray();
        var activeGeneratedProfileAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var athletesDir = Path.Combine(_env.WebRootPath, "athletes");
        var files = Directory.EnumerateFiles(athletesDir, "athlete.json", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // retry read in case the file is mid-write
            string text = "";
            for (int i = 0; ; i++)
            {
                try
                {
                    text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (IOException) when (i < 5)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
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
                .Where(path => ProfileImageExtensions.Contains(Path.GetExtension(path)))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(path => GetProfileImageExtensionPriority(Path.GetExtension(path)))
                .FirstOrDefault();
            var publishedProfile = pic is null
                ? null
                : await PublishProfileImageSnapshotAsync(pic, folderName, cancellationToken).ConfigureAwait(false);
            var profileImageId = publishedProfile?.ImageId;
            athlete[ProfileImageIdProperty] = profileImageId;
            var profilePicUrl = publishedProfile?.Url;
            if (publishedProfile is not null)
            {
                activeGeneratedProfileAssets.Add(Path.GetFullPath(publishedProfile.Path));
                activeGeneratedProfileAssets.Add(Path.GetFullPath(
                    GetProfileThumbPath(folderName, "_thumb_sm", publishedProfile.ImageId)));
                activeGeneratedProfileAssets.Add(Path.GetFullPath(
                    GetProfileThumbPath(folderName, "_thumb_md", publishedProfile.ImageId)));
            }
            athlete["ProfilePic"] = profilePicUrl;
            athlete["ProfilePicThumb"] = publishedProfile is null
                ? profilePicUrl
                : BuildOrGetProfileThumbUrl(
                    sourceImagePath: publishedProfile.Path,
                    sourceImageId: publishedProfile.ImageId,
                    folderName: folderName,
                    thumbSuffix: "_thumb_sm",
                    sizePx: EventThumbSizePx,
                    quality: EventThumbQuality,
                    cancellationToken: cancellationToken) ?? profilePicUrl;
            athlete["ProfilePicLeaderboardThumb"] = publishedProfile is null
                ? profilePicUrl
                : BuildOrGetProfileThumbUrl(
                    sourceImagePath: publishedProfile.Path,
                    sourceImageId: publishedProfile.ImageId,
                    folderName: folderName,
                    thumbSuffix: "_thumb_md",
                    sizePx: LeaderboardThumbSizePx,
                    quality: LeaderboardThumbQuality,
                    cancellationToken: cancellationToken) ?? profilePicUrl;

            // PROOFS: look for proof_*.ext
            var proofs = new JsonArray();
            var proofFiles = Directory
                .EnumerateFiles(folder, "proof_*.*", SearchOption.TopDirectoryOnly)
                .OrderBy(f => ExtractNumber(Path.GetFileNameWithoutExtension(f)));
            foreach (var p in proofFiles)
                proofs.Add(BuildVersionedAthleteAssetUrl(
                    folderName,
                    Path.GetFileName(p),
                    File.GetLastWriteTimeUtc(p).Ticks.ToString(CultureInfo.InvariantCulture)));
            athlete["Proofs"] = proofs;

            CanonicalizeIsoDatesInPlace(athlete);
            athletesRoot.Add(athlete);
        }

        cancellationToken.ThrowIfCancellationRequested();
        PruneStaleGeneratedProfileAssets(activeGeneratedProfileAssets, cancellationToken);
        return athletesRoot;
    }

    private static string BuildVersionedAthleteAssetUrl(string folderName, string fileName, string version)
        => $"/athletes/{folderName}/{fileName}?v={version}";

    private static int GetProfileImageExtensionPriority(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".webp" => 0,
            ".png" => 1,
            ".jpg" => 2,
            ".jpeg" => 3,
            _ => int.MaxValue
        };

    private async Task<PublishedProfileImage> PublishProfileImageSnapshotAsync(
        string sourcePath,
        string folderName,
        CancellationToken cancellationToken)
    {
        byte[] sourceBytes;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var stream = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    bufferSize: 64 * 1024,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                sourceBytes = buffer.ToArray();
                break;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        var imageId = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var publishedFileName = $"{folderName}_{imageId}{extension}";
        var publishedPath = Path.Combine(_publishedProfileDir, publishedFileName);
        if (!HasExpectedLength(publishedPath, sourceBytes.Length))
        {
            var pendingPath = Path.Combine(
                _publishedProfileDir,
                $".{publishedFileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(pendingPath, sourceBytes, cancellationToken).ConfigureAwait(false);
                try
                {
                    File.Move(pendingPath, publishedPath, overwrite: false);
                }
                catch (IOException) when (HasExpectedLength(publishedPath, sourceBytes.Length))
                {
                    // Another app instance atomically published these same hash-bound bytes.
                }
                catch (UnauthorizedAccessException) when (HasExpectedLength(publishedPath, sourceBytes.Length))
                {
                    // Windows can report access denied while another instance has just
                    // opened the winning immutable file. Its hash-bound length is enough
                    // to treat this publisher as the idempotent loser.
                }
            }
            finally
            {
                if (File.Exists(pendingPath))
                    File.Delete(pendingPath);
            }
        }

        return new PublishedProfileImage(
            imageId,
            publishedPath,
            $"/generated/profiles/athletes/{publishedFileName}?v={imageId}");
    }

    private static bool HasExpectedLength(string path, long expectedLength)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length == expectedLength;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record PublishedProfileImage(string ImageId, string Path, string Url);

    private string? BuildOrGetProfileThumbUrl(
        string sourceImagePath,
        string sourceImageId,
        string folderName,
        string thumbSuffix,
        int sizePx,
        int quality,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(sourceImagePath) ||
            string.IsNullOrWhiteSpace(sourceImageId) ||
            !File.Exists(sourceImagePath))
            return null;

        // Content-addressed filenames keep old immutable URLs bound to the exact
        // portrait bytes they were generated from.
        var thumbFileName = $"{folderName}{thumbSuffix}_{sourceImageId}.webp";
        var thumbPath = GetProfileThumbPath(folderName, thumbSuffix, sourceImageId);
        if (string.IsNullOrWhiteSpace(thumbPath))
            return null;

        string? pendingThumbPath = null;
        try
        {
            var needsGenerate = !File.Exists(thumbPath) ||
                                new FileInfo(thumbPath).Length <= 0;

            if (needsGenerate)
            {
                pendingThumbPath = Path.Combine(
                    _profileThumbDir,
                    $".{thumbFileName}.{Guid.NewGuid():N}.tmp");
                using var image = Image.Load(sourceImagePath);
                cancellationToken.ThrowIfCancellationRequested();
                image.Mutate(ctx => ctx
                    .AutoOrient()
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(sizePx, sizePx),
                        Mode = ResizeMode.Crop,
                        Position = AnchorPositionMode.Center
                    }));

                image.Metadata.ExifProfile = null;
                image.Save(pendingThumbPath, new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = quality
                });
                cancellationToken.ThrowIfCancellationRequested();
                var expectedThumbLength = new FileInfo(pendingThumbPath).Length;
                if (File.Exists(thumbPath) && !HasExpectedLength(thumbPath, expectedThumbLength))
                    File.Delete(thumbPath);

                try
                {
                    File.Move(pendingThumbPath, thumbPath, overwrite: false);
                    pendingThumbPath = null;
                }
                catch (IOException) when (HasExpectedLength(thumbPath, expectedThumbLength))
                {
                    // Another instance published the same content-addressed thumbnail.
                }
                catch (UnauthorizedAccessException) when (HasExpectedLength(thumbPath, expectedThumbLength))
                {
                    // Treat the already-published immutable file as the winner.
                }
            }

            var publishedThumbInfo = new FileInfo(thumbPath);
            if (!publishedThumbInfo.Exists || publishedThumbInfo.Length <= 0)
                return null;

            return $"/generated/thumbs/athletes/{thumbFileName}?v={sourceImageId}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(pendingThumbPath) && File.Exists(pendingThumbPath))
            {
                try
                {
                    File.Delete(pendingThumbPath);
                }
                catch
                {
                    // A failed cleanup must not hide the usable original portrait fallback.
                }
            }

        }
    }

    private string GetProfileThumbPath(string folderName, string thumbSuffix, string sourceImageId)
        => Path.Combine(_profileThumbDir, $"{folderName}{thumbSuffix}_{sourceImageId}.webp");

    private void PruneStaleGeneratedProfileAssets(
        IReadOnlySet<string> activePaths,
        CancellationToken cancellationToken)
    {
        var cutoffUtc = DateTime.UtcNow - GeneratedProfileAssetRetention;
        PruneDirectory(_publishedProfileDir, activePaths, cutoffUtc, cancellationToken);
        PruneDirectory(_profileThumbDir, activePaths, cutoffUtc, cancellationToken);
    }

    private static void PruneDirectory(
        string directory,
        IReadOnlySet<string> activePaths,
        DateTime cutoffUtc,
        CancellationToken cancellationToken)
    {
        foreach (var path in Directory
                     .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => !path.EndsWith(".inactive", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inactiveMarkerPath = path + ".inactive";
            try
            {
                if (activePaths.Contains(Path.GetFullPath(path)))
                {
                    if (File.Exists(inactiveMarkerPath))
                        File.Delete(inactiveMarkerPath);
                    continue;
                }

                if (!File.Exists(inactiveMarkerPath))
                {
                    try
                    {
                        using var marker = new FileStream(
                            inactiveMarkerPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.Read);
                    }
                    catch (IOException) when (File.Exists(inactiveMarkerPath))
                    {
                        // Another instance recorded the same deactivation boundary.
                    }

                    continue;
                }

                if (File.GetLastWriteTimeUtc(inactiveMarkerPath) > cutoffUtc)
                    continue;

                File.Delete(path);
                File.Delete(inactiveMarkerPath);
            }
            catch (IOException)
            {
                // A concurrent render or static response can retain the file until
                // the next full athlete reload.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort retention cleanup must never block athlete hydration.
            }
        }

        foreach (var markerPath in Directory.EnumerateFiles(
                     directory,
                     "*.inactive",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var assetPath = markerPath[..^".inactive".Length];
                if (!File.Exists(assetPath))
                    File.Delete(markerPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void DebounceReload()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        try
        {
            _reloadSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // One queued signal is enough; the worker restarts the debounce
            // window whenever it observes additional changes.
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
        }
    }

    private async Task ProcessReloadRequestsAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                await _reloadSignal.WaitAsync(cancellationToken).ConfigureAwait(false);

                var restartDebounce = true;
                while (restartDebounce)
                {
                    await Task.Delay(_debounceInterval, cancellationToken).ConfigureAwait(false);
                    restartDebounce = false;
                    while (_reloadSignal.Wait(0))
                        restartDebounce = true;
                }

                await ReloadFromSourceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Athlete source reload failed; the watcher remains active for the next change.");
            }
        }
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

        var payload = new List<(int Count, DateTime OccurredAtUtc)>();
        foreach (var t in AthleteCountMilestoneThresholds)
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

    private async Task ReloadFromSourceAsync(CancellationToken cancellationToken)
    {
        using var operation = _reloadOperations.TryEnter();
        if (operation is null)
            return;

        var notify = false;

        await _reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var reloadedAthletes = await LoadAthletesAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            // Readers must never observe the base JSON between its reset of derived
            // values and the database-backed hydration that follows. Holding the
            // snapshot lock publishes the entire reload as one atomic transition.
            lock (_athletesJsonLock)
            {
                _athletes = reloadedAthletes;
                var newlyJoined = EnsureDbRowsForNewAthletes();

                SyncAgeGuessProfileImageIds(migrateLegacyGuesses: false);
                ReloadCrowdStatsCore();
                // A new profile image can remove the athlete from the Crowd Age field.
                // Keep stored placements current without publishing incidental social events.
                SyncCrowdAgeTop10Placements(emitEvents: false);
                HydrateAgeImprovementIntoAthletesJson();
                HydratePlacementsIntoAthletesJson();
                HydrateNewFlagsIntoAthletesJson();
                HydrateCurrentPlacementIntoAthletesJson(); // NOTE: no DB persist here
                HydrateBadgesIntoAthletesJson();           // badges into athlete JSON

                // recompute and persist biomarker/test signatures after reload
                var changedSigs = SyncBiomarkerSignatures();
                SyncAgeImprovementTop10Placements(emitEvents: true, eventSubjectSlugs: changedSigs);
                var becamePro = SyncProTrackStates(newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>()));
                var bioAgeImprovements = SyncBestBioAgeStates(
                    changedSigs,
                    newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>()));

                if (newlyJoined.Count > 0)
                {
                    var payload = BuildJoinedPayloadWithReplaced(newlyJoined);
                    _eventDataService.CreateJoinedEventsForAthletes(payload, skipIfExists: true);
                }

                if (becamePro.Count > 0)
                {
                    _eventDataService.CreateBecameProEvents(becamePro, skipIfExists: true);
                }

                if (bioAgeImprovements.Count > 0)
                {
                    _eventDataService.CreateBiologicalAgeImprovementEvents(bioAgeImprovements, skipIfExists: true);
                }

                DetectAndEmitRankUpsForSlugs(
                    changedSlugs: changedSigs,
                    newcomerSlugs: newlyJoined.Select(x => x.Athlete["AthleteSlug"]!.GetValue<string>())
                );

                DetectAndEmitAthleteCountMilestones(); // emit milestones on reload/new joins
                notify = true;
            }
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
        // An Error event means one or more filesystem notifications may already
        // have been lost, so restarting the watcher alone is not sufficient.
        DebounceReload();
    }

    private static int ExtractNumber(string fileNameWithoutExtension)
    {
        var parts = fileNameWithoutExtension.Split('_');
        if (int.TryParse(parts.Last(), out var number))
            return number;
        return int.MaxValue;
    }

    /// <summary>
    /// Adds a timestamped age guess for the profile image the visitor saw, then
    /// updates that athlete's current CrowdAge (median) and CrowdCount.
    /// </summary>
    public bool TryAddAgeGuess(string athleteSlug, string profileImageId, int ageGuess)
    {
        using var operation = _reloadOperations.Enter();
        int cnt;
        double median;

        _reloadLock.Wait();
        try
        {
            if (!TryGetProfileImageIdentity(athleteSlug, out var canonicalAthleteSlug, out var currentProfileImageId) ||
                !string.Equals(currentProfileImageId, profileImageId, StringComparison.Ordinal))
            {
                return false;
            }

            cnt = 0;
            median = 0;

            var persisted = _db.Run(sqlite =>
            {
                using var transaction = sqlite.BeginTransaction(deferred: false);
                using var selectJsonCmd = sqlite.CreateCommand();
                selectJsonCmd.Transaction = transaction;
                selectJsonCmd.CommandText =
                    "SELECT AgeGuesses FROM Athletes WHERE Key = @key";
                selectJsonCmd.Parameters.AddWithValue("@key", canonicalAthleteSlug);
                if (selectJsonCmd.ExecuteScalar() is not string existingJson)
                    return false;

                var ageArray = JsonSerializer.Deserialize<List<JsonObject>>(existingJson) ?? [];
                ageArray.Add(new JsonObject
                {
                    ["TimestampUtc"] = DateTime.UtcNow.ToString("o"),
                    ["AgeGuess"] = ageGuess,
                    [ProfileImageIdProperty] = currentProfileImageId
                });

                // store back
                var updatedJson = JsonSerializer.Serialize(ageArray);
                using var updateCmd = sqlite.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText =
                    "UPDATE Athletes SET AgeGuesses = @ages WHERE Key = @key";
                updateCmd.Parameters.AddWithValue("@ages", updatedJson);
                updateCmd.Parameters.AddWithValue("@key", canonicalAthleteSlug);
                if (updateCmd.ExecuteNonQuery() != 1)
                    return false;

                transaction.Commit();
                (median, cnt) = CalculateCrowdStats(ageArray, currentProfileImageId);
                return true;
            });

            if (!persisted)
                return false;

            lock (_athletesJsonLock)
            {
                var athleteJson = _athletes
                    .OfType<JsonObject>()
                    .FirstOrDefault(o => string.Equals(
                        o["AthleteSlug"]?.GetValue<string>(),
                        canonicalAthleteSlug,
                        StringComparison.OrdinalIgnoreCase));

                if (athleteJson != null)
                {
                    athleteJson["CrowdCount"] = cnt;
                    athleteJson["CrowdAge"] = median;
                }
            }

            SyncCrowdAgeTop10Placements(emitEvents: true, eventSubjectSlugs: new[] { canonicalAthleteSlug });
        }
        finally
        {
            _reloadLock.Release();
        }

        PushAthleteDirectoryToEvents();
        AthletesChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Re-reads all medians and counts from SQLite and updates the in-memory JSON.
    /// </summary>
    public void ReloadCrowdStats()
    {
        using var operation = _reloadOperations.Enter();
        _reloadLock.Wait();
        try
        {
            ReloadCrowdStatsCore();
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private void ReloadCrowdStatsCore()
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
    /// Returns (Median, Count) for guesses tied to the athlete's current profile image.
    /// </summary>
    public (double Median, int Count) GetCrowdStats(string athleteSlug)
    {
        if (!TryGetProfileImageIdentity(
                athleteSlug,
                out var canonicalAthleteSlug,
                out var currentProfileImageId))
            return (0, 0);

        return ReadCrowdStats(canonicalAthleteSlug, currentProfileImageId);
    }

    public bool TryGetCrowdStatsForProfileImage(
        string athleteSlug,
        string expectedProfileImageId,
        out (double Median, int Count) stats)
    {
        using var operation = _reloadOperations.Enter();
        stats = (0, 0);
        _reloadLock.Wait();
        try
        {
            if (!TryGetProfileImageIdentity(
                    athleteSlug,
                    out var canonicalAthleteSlug,
                    out var currentProfileImageId) ||
                !string.Equals(currentProfileImageId, expectedProfileImageId, StringComparison.Ordinal))
            {
                return false;
            }

            stats = ReadCrowdStats(canonicalAthleteSlug, currentProfileImageId);
            return true;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private (double Median, int Count) ReadCrowdStats(
        string canonicalAthleteSlug,
        string profileImageId)
    {
        return _db.Run(sqlite =>
        {
            using var selectAgesJson = sqlite.CreateCommand();
            selectAgesJson.CommandText =
                "SELECT AgeGuesses FROM Athletes WHERE Key = @key";
            selectAgesJson.Parameters.AddWithValue("@key", canonicalAthleteSlug);
            var agesJsonText = (selectAgesJson.ExecuteScalar() as string) ?? "[]";
            var allGuesses = JsonSerializer.Deserialize<List<JsonObject>>(agesJsonText)!;
            return CalculateCrowdStats(allGuesses, profileImageId);
        });
    }

    public bool TryGetProfileImageId(string athleteSlug, out string profileImageId)
        => TryGetProfileImageIdentity(athleteSlug, out _, out profileImageId);

    private bool TryGetProfileImageIdentity(
        string athleteSlug,
        out string canonicalAthleteSlug,
        out string profileImageId)
    {
        canonicalAthleteSlug = "";
        profileImageId = "";
        lock (_athletesJsonLock)
        {
            var athlete = _athletes
                .OfType<JsonObject>()
                .FirstOrDefault(o => string.Equals(
                    o["AthleteSlug"]?.GetValue<string>(),
                    athleteSlug,
                    StringComparison.OrdinalIgnoreCase));
            if (athlete?[ProfileImageIdProperty] is not JsonValue imageIdValue ||
                !imageIdValue.TryGetValue<string>(out var imageId) ||
                string.IsNullOrWhiteSpace(imageId))
            {
                return false;
            }

            canonicalAthleteSlug = athlete["AthleteSlug"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(canonicalAthleteSlug))
                return false;

            profileImageId = imageId;
            return true;
        }
    }

    private static (double Median, int Count) CalculateCrowdStats(
        IEnumerable<JsonObject> guesses,
        string profileImageId)
    {
        var ages = guesses
            .Where(node =>
                node[ProfileImageIdProperty] is JsonValue imageIdValue &&
                imageIdValue.TryGetValue<string>(out var guessProfileImageId) &&
                string.Equals(guessProfileImageId, profileImageId, StringComparison.Ordinal))
            .Select(node => node["AgeGuess"]!.GetValue<int>())
            .OrderBy(val => val)
            .ToList();

        var count = ages.Count;
        if (count == 0)
            return (0, 0);

        var median = count % 2 == 1
            ? ages[count / 2]
            : (ages[count / 2 - 1] + ages[count / 2]) / 2.0;
        return (median, count);
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
        var results = new List<(CompetitionRankCandidate Candidate, JsonObject Obj)>();

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
            var phenoDiff = r.AgeReduction ?? 0;
            var hasBortz = r.BortzAgeReduction.HasValue &&
                           !double.IsNaN(r.BortzAgeReduction.Value) &&
                           !double.IsInfinity(r.BortzAgeReduction.Value);
            var effectiveDiff = hasBortz ? r.BortzAgeReduction!.Value : phenoDiff;

            var obj = new JsonObject
            {
                ["AthleteSlug"] = slug,
                ["Name"] = name,
                ["ChronologicalAge"] = chronoToday,
                ["LowestPhenoAge"] = lowestPheno,
                ["AgeDifference"] = Math.Round(effectiveDiff, 2)
            };
            if (r.LowestBortzAge.HasValue)
                obj["LowestBortzAge"] = Math.Round(r.LowestBortzAge.Value, 2);
            if (r.PhenoAgeDiffFromBaseline.HasValue)
                obj["PhenoAgeDiffFromBaseline"] = Math.Round(r.PhenoAgeDiffFromBaseline.Value, 2);
            if (r.PhenoAgeImprovementFromWorst.HasValue)
                obj["PhenoAgeImprovementFromWorst"] = Math.Round(r.PhenoAgeImprovementFromWorst.Value, 2);
            if (r.BortzAgeDiffFromBaseline.HasValue)
                obj["BortzAgeDiffFromBaseline"] = Math.Round(r.BortzAgeDiffFromBaseline.Value, 2);
            if (r.BortzAgeImprovementFromWorst.HasValue)
                obj["BortzAgeImprovementFromWorst"] = Math.Round(r.BortzAgeImprovementFromWorst.Value, 2);

            results.Add((new CompetitionRankCandidate(slug, name, hasBortz, effectiveDiff, dobUtc), obj));
        }

        var arr = new JsonArray();
        var objectBySlug = results.ToDictionary(t => t.Candidate.Slug, t => t.Obj, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in CompetitionRanking.SortByCompetitionRules(results.Select(t => t.Candidate)))
        {
            var o = objectBySlug[candidate.Slug];
            arr.Add(o);
        }

        return arr;
    }

    public HypotheticalRankResult CalculateHypotheticalRank(double chronologicalAge, double biologicalAge, DateTime dobUtc, bool hasBortz)
    {
        var asOf = DateTime.UtcNow.Date;
        var athletesSnapshot = GetAthletesSnapshot();
        var statsMap = PhenoStatsCalculator.BuildAll(athletesSnapshot, asOf);
        var candidates = new List<CompetitionRankCandidate>();

        foreach (var athlete in athletesSnapshot.OfType<JsonObject>())
        {
            var slug = athlete["AthleteSlug"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(slug)) continue;
            if (!statsMap.TryGetValue(slug, out var r)) continue;
            if (!r.DobUtc.HasValue) continue;

            var rowHasBortz = r.BortzAgeReduction.HasValue &&
                              !double.IsNaN(r.BortzAgeReduction.Value) &&
                              !double.IsInfinity(r.BortzAgeReduction.Value);
            var effectiveDiff = rowHasBortz ? r.BortzAgeReduction!.Value : (r.AgeReduction ?? 0);

            candidates.Add(new CompetitionRankCandidate(
                slug,
                r.Name ?? "",
                rowHasBortz,
                effectiveDiff,
                r.DobUtc.Value));
        }

        return CompetitionRanking.CalculateHypothetical(candidates, chronologicalAge, biologicalAge, dobUtc, hasBortz);
    }

    public IReadOnlyList<AthleteForX> GetAthletesForX()
    {
        var order = GetRankingsOrder();
        var snapshot = GetAthletesSnapshot();
        var extBySlug = new Dictionary<string, (string? PodcastLink, string? XHandle, string? MediaContact, string? DisplayName)>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in snapshot.OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;
            var link = o["PodcastLink"]?.GetValue<string>() ?? o["podcastLink"]?.GetValue<string>();
            var media = o["MediaContact"]?.GetValue<string>();
            var displayName = o["DisplayName"]?.GetValue<string>();
            var handle = SocialContactParser.TryBuildMention(media, SocialPlatform.X);
            extBySlug[slug] = (
                string.IsNullOrWhiteSpace(link) ? null : link.Trim(),
                handle,
                string.IsNullOrWhiteSpace(media) ? null : media.Trim(),
                string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim());
        }
        var list = new List<AthleteForX>();
        var rank = 0;
        foreach (var o in order.OfType<JsonObject>())
        {
            rank++;
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;
            extBySlug.TryGetValue(slug, out var ext);
            var name = ext.DisplayName ?? o["Name"]?.GetValue<string>() ?? "";
            double? lowestPheno = null;
            if (o["LowestPhenoAge"] is JsonValue pv && pv.TryGetValue<double>(out var p)) lowestPheno = p;
            double? lowestBortz = null;
            if (o["LowestBortzAge"] is JsonValue bv && bv.TryGetValue<double>(out var b)) lowestBortz = b;
            double? chrono = null;
            if (o["ChronologicalAge"] is JsonValue cv && cv.TryGetValue<double>(out var c)) chrono = c;
            double? phenoDiff = null;
            if (o["PhenoAgeDiffFromBaseline"] is JsonValue pdv && pdv.TryGetValue<double>(out var pd)) phenoDiff = pd;
            double? bortzDiff = null;
            if (o["BortzAgeDiffFromBaseline"] is JsonValue bdv && bdv.TryGetValue<double>(out var bd)) bortzDiff = bd;
            list.Add(new AthleteForX(slug, name, rank, lowestPheno, lowestBortz, chrono, phenoDiff, bortzDiff, ext.PodcastLink, ext.XHandle, ext.MediaContact));
        }
        return list;
    }

    public bool TryGetCrowdAgeLeaderboardEntry(string athleteSlug, out CrowdAgeLeaderboardEntry entry)
    {
        entry = null!;
        var normalized = NormalizeAthleteSlug(athleteSlug);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var sorted = CompetitionRanking.SortByCrowdAgeRules(GetCrowdAgeRankCandidates()).ToList();
        var index = sorted.FindIndex(t => string.Equals(NormalizeAthleteSlug(t.Slug), normalized, StringComparison.Ordinal));
        if (index < 0)
            return false;

        var candidate = sorted[index];
        entry = new CrowdAgeLeaderboardEntry(
            NormalizeAthleteSlug(candidate.Slug),
            candidate.Name,
            index + 1,
            candidate.CrowdAge,
            candidate.CrowdAgeReduction,
            candidate.CrowdCount);
        return true;
    }

    public bool TryGetAgeImprovementLeaderboardEntry(string athleteSlug, string clock, out AgeImprovementLeaderboardEntry entry)
    {
        entry = null!;
        var normalized = NormalizeAthleteSlug(athleteSlug);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase))
        {
            var sorted = CompetitionRanking.SortByBortzAgeImprovementRules(GetBortzAgeImprovementRankCandidates()).ToList();
            var index = sorted.FindIndex(t => string.Equals(NormalizeAthleteSlug(t.Slug), normalized, StringComparison.Ordinal));
            if (index < 0)
                return false;

            var candidate = sorted[index];
            entry = new AgeImprovementLeaderboardEntry(
                NormalizeAthleteSlug(candidate.Slug),
                candidate.Name,
                "bortz",
                index + 1,
                candidate.BortzAgeImprovement,
                candidate.BortzAgeReduction);
            return true;
        }

        var phenoSorted = CompetitionRanking.SortByPhenoAgeImprovementRules(GetPhenoAgeImprovementRankCandidates()).ToList();
        var phenoIndex = phenoSorted.FindIndex(t => string.Equals(NormalizeAthleteSlug(t.Slug), normalized, StringComparison.Ordinal));
        if (phenoIndex < 0)
            return false;

        var phenoCandidate = phenoSorted[phenoIndex];
        entry = new AgeImprovementLeaderboardEntry(
            NormalizeAthleteSlug(phenoCandidate.Slug),
            phenoCandidate.Name,
            "pheno",
            phenoIndex + 1,
            phenoCandidate.PhenoAgeImprovement,
            phenoCandidate.PhenoAgeReduction);
        return true;
    }

    public bool TryGetBaselineImprovementLeaderboardEntry(string athleteSlug, string clock, out AgeImprovementLeaderboardEntry entry)
    {
        entry = null!;
        var normalized = NormalizeAthleteSlug(athleteSlug);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (!string.Equals(clock, "pheno", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase))
            return false;

        var useBortz = string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase);
        var ranked = PhenoStatsCalculator.BuildAll(GetAthletesSnapshot(), DateTime.UtcNow.Date)
            .Values
            .Select(result => new
            {
                Result = result,
                Improvement = useBortz ? result.BortzAgeDiffFromBaseline : result.PhenoAgeDiffFromBaseline,
                SubmissionCount = useBortz ? result.BortzSubmissionCount : result.SubmissionCount,
                AgeReduction = useBortz ? result.BortzAgeReduction : result.AgeReduction
            })
            .Where(row =>
                row.SubmissionCount >= 2 &&
                row.Improvement.HasValue &&
                double.IsFinite(row.Improvement.Value))
            .OrderBy(row => row.Improvement!.Value)
            .ThenBy(row => row.Result.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var athlete = ranked.FirstOrDefault(row =>
            string.Equals(NormalizeAthleteSlug(row.Result.Slug), normalized, StringComparison.Ordinal));
        if (athlete is null || athlete.Improvement is not { } improvement)
            return false;

        var rank = ranked
            .Select(row => row.Improvement!.Value)
            .Distinct()
            .TakeWhile(value => value < improvement)
            .Count() + 1;
        entry = new AgeImprovementLeaderboardEntry(
            NormalizeAthleteSlug(athlete.Result.Slug),
            athlete.Result.Name,
            useBortz ? "bortz" : "pheno",
            rank,
            improvement,
            athlete.AgeReduction is { } ageReduction && double.IsFinite(ageReduction)
                ? ageReduction
                : 0d);
        return true;
    }

    public bool TryGetBiologicalAgeLeaderboardEntry(string athleteSlug, string clock, out BiologicalAgeLeaderboardEntry entry)
    {
        entry = null!;
        var normalized = NormalizeAthleteSlug(athleteSlug);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        if (!string.Equals(clock, "pheno", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase))
            return false;

        var useBortz = string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase);
        var stats = PhenoStatsCalculator.BuildAll(GetAthletesSnapshot(), DateTime.UtcNow.Date);
        var ranked = stats.Values
            .Where(result => result.DobUtc.HasValue)
            .Select(result => new
            {
                Result = result,
                BiologicalAge = useBortz ? result.LowestBortzAge : result.LowestPhenoAge,
                AgeReduction = useBortz ? result.BortzAgeReduction : result.AgeReduction,
                SubmissionCount = useBortz ? result.BortzSubmissionCount : result.SubmissionCount
            })
            .Where(row =>
                row.SubmissionCount > 0 &&
                row.BiologicalAge.HasValue &&
                double.IsFinite(row.BiologicalAge.Value) &&
                row.AgeReduction.HasValue &&
                double.IsFinite(row.AgeReduction.Value))
            .OrderBy(row => row.AgeReduction!.Value)
            .ThenBy(row => row.Result.DobUtc!.Value)
            .ThenBy(row => row.Result.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var index = ranked.FindIndex(row =>
            string.Equals(NormalizeAthleteSlug(row.Result.Slug), normalized, StringComparison.Ordinal));
        if (index < 0)
            return false;

        var athlete = ranked[index];
        entry = new BiologicalAgeLeaderboardEntry(
            NormalizeAthleteSlug(athlete.Result.Slug),
            athlete.Result.Name,
            useBortz ? "bortz" : "pheno",
            index + 1,
            athlete.BiologicalAge!.Value,
            athlete.AgeReduction!.Value);
        return true;
    }

    private IReadOnlyList<CrowdAgeRankCandidate> GetCrowdAgeRankCandidates()
    {
        var snapshot = GetAthletesSnapshot();
        var asOf = DateTime.UtcNow.Date;
        var list = new List<CrowdAgeRankCandidate>();
        foreach (var o in snapshot.OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;

            if (o["CrowdAge"] is not JsonValue cv || !cv.TryGetValue<double>(out var crowdAge) || !double.IsFinite(crowdAge))
                continue;

            int crowdCount = 0;
            if (o["CrowdCount"] is JsonValue cc && cc.TryGetValue<int>(out var cnt))
                crowdCount = cnt;
            if (crowdCount < CrowdAgeLeaderboardMinimumGuessCount) continue;

            var name = o["DisplayName"]?.GetValue<string>() ?? o["Name"]?.GetValue<string>() ?? slug;
            if (o["DateOfBirth"] is not JsonObject dobNode)
                continue;

            DateTime dobUtc;
            try
            {
                dobUtc = new DateTime(
                    dobNode["Year"]!.GetValue<int>(),
                    dobNode["Month"]!.GetValue<int>(),
                    dobNode["Day"]!.GetValue<int>(),
                    0,
                    0,
                    0,
                    DateTimeKind.Utc);
            }
            catch
            {
                continue;
            }

            var chronologicalAge = CalculateAgeAtDate(dobUtc, asOf);
            var crowdAgeReduction = crowdAge - chronologicalAge;

            list.Add(new CrowdAgeRankCandidate(slug, name, crowdAge, crowdAgeReduction, crowdCount, dobUtc));
        }

        return list;
    }

    private IReadOnlyList<(string Slug, int Place, double CrowdAge, int CrowdCount)> GetCrowdAgeTop10Snapshot()
    {
        return CompetitionRanking.SortByCrowdAgeRules(GetCrowdAgeRankCandidates())
            .Take(10)
            .Select((candidate, index) => (
                candidate.Slug,
                Place: index + 1,
                candidate.CrowdAge,
                candidate.CrowdCount))
            .ToList();
    }

    private void SyncCrowdAgeTop10Placements(bool emitEvents, IEnumerable<string>? eventSubjectSlugs = null)
    {
        var currentTop10 = GetCrowdAgeTop10Snapshot();
        var currentBySlug = currentTop10.ToDictionary(x => x.Slug, x => x, StringComparer.OrdinalIgnoreCase);
        var eventSubjects = BuildTop10EventSubjectSet(emitEvents, eventSubjectSlugs);
        var changed = new List<(string AthleteSlug, DateTime OccurredAtUtc, int Place, int? PreviousPlace, string? PreviousSlug, double CrowdAge, int CrowdCount)>();
        var nowUtc = DateTime.UtcNow;

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var select = sqlite.CreateCommand();
            select.Transaction = tx;
            select.CommandText = $"SELECT Key, {CrowdAgeTop10PlacementColumn} FROM Athletes";

            var stored = new List<(string Slug, int? Place)>();
            using (var r = select.ExecuteReader())
            {
                while (r.Read())
                {
                    var slug = r.GetString(0);
                    var place = r.IsDBNull(1) ? (int?)null : r.GetInt32(1);
                    stored.Add((slug, place));
                }
            }

            var previousSlugByPlace = stored
                .Where(x => x.Place is >= 1 and <= 10)
                .GroupBy(x => x.Place!.Value)
                .ToDictionary(g => g.Key, g => g.First().Slug);

            using var update = sqlite.CreateCommand();
            update.Transaction = tx;
            update.CommandText = $"UPDATE Athletes SET {CrowdAgeTop10PlacementColumn}=@place WHERE Key=@slug";
            var pPlace = update.Parameters.Add("@place", SqliteType.Integer);
            var pSlug = update.Parameters.Add("@slug", SqliteType.Text);

            foreach (var (slug, previousPlace) in stored)
            {
                currentBySlug.TryGetValue(slug, out var current);
                int? currentPlace = current.Place is >= 1 and <= 10 ? current.Place : null;

                if (emitEvents &&
                    currentPlace.HasValue &&
                    previousPlace != currentPlace)
                {
                    previousSlugByPlace.TryGetValue(currentPlace.Value, out var previousSlug);
                    if (string.Equals(previousSlug, slug, StringComparison.OrdinalIgnoreCase))
                        previousSlug = null;

                    if (ShouldEmitTop10PlacementChangeEvent(slug, previousPlace, currentPlace.Value, previousSlug, eventSubjects))
                    {
                        changed.Add((
                            slug,
                            nowUtc,
                            currentPlace.Value,
                            previousPlace,
                            previousSlug,
                            current.CrowdAge,
                            current.CrowdCount));
                    }
                }

                if (previousPlace == currentPlace) continue;

                pPlace.Value = currentPlace.HasValue ? currentPlace.Value : DBNull.Value;
                pSlug.Value = slug;
                update.ExecuteNonQuery();
            }

            tx.Commit();
        });

        if (changed.Count > 0)
            _eventDataService.CreateCrowdAgeTop10ChangeEvents(changed, skipIfExists: true);
    }

    private IReadOnlyList<PhenoAgeImprovementRankCandidate> GetPhenoAgeImprovementRankCandidates()
    {
        var snapshot = GetAthletesSnapshot();
        var statsMap = PhenoStatsCalculator.BuildAll(snapshot, DateTime.UtcNow.Date);
        var list = new List<PhenoAgeImprovementRankCandidate>();

        foreach (var r in statsMap.Values)
        {
            if (!r.DobUtc.HasValue ||
                !r.PhenoAgeImprovementFromWorst.HasValue ||
                !double.IsFinite(r.PhenoAgeImprovementFromWorst.Value) ||
                !r.AgeReduction.HasValue ||
                !double.IsFinite(r.AgeReduction.Value))
            {
                continue;
            }

            list.Add(new PhenoAgeImprovementRankCandidate(
                r.Slug,
                r.Name ?? "",
                r.PhenoAgeImprovementFromWorst.Value,
                r.AgeReduction.Value,
                r.DobUtc.Value));
        }

        return list;
    }

    private IReadOnlyList<BortzAgeImprovementRankCandidate> GetBortzAgeImprovementRankCandidates()
    {
        var snapshot = GetAthletesSnapshot();
        var statsMap = PhenoStatsCalculator.BuildAll(snapshot, DateTime.UtcNow.Date);
        var list = new List<BortzAgeImprovementRankCandidate>();

        foreach (var r in statsMap.Values)
        {
            if (!r.DobUtc.HasValue ||
                !r.BortzAgeImprovementFromWorst.HasValue ||
                !double.IsFinite(r.BortzAgeImprovementFromWorst.Value) ||
                !r.BortzAgeReduction.HasValue ||
                !double.IsFinite(r.BortzAgeReduction.Value))
            {
                continue;
            }

            list.Add(new BortzAgeImprovementRankCandidate(
                r.Slug,
                r.Name ?? "",
                r.BortzAgeImprovementFromWorst.Value,
                r.BortzAgeReduction.Value,
                r.DobUtc.Value));
        }

        return list;
    }

    private IReadOnlyList<(string Slug, int Place, string Clock, double Improvement, double AgeReduction)> GetAgeImprovementTop10Snapshot(string clock)
    {
        if (string.Equals(clock, "pheno", StringComparison.OrdinalIgnoreCase))
        {
            return CompetitionRanking.SortByPhenoAgeImprovementRules(GetPhenoAgeImprovementRankCandidates())
                .Take(10)
                .Select((candidate, index) => (
                    candidate.Slug,
                    Place: index + 1,
                    Clock: "pheno",
                    Improvement: candidate.PhenoAgeImprovement,
                    AgeReduction: candidate.PhenoAgeReduction))
                .ToList();
        }

        if (string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase))
        {
            return CompetitionRanking.SortByBortzAgeImprovementRules(GetBortzAgeImprovementRankCandidates())
                .Take(10)
                .Select((candidate, index) => (
                    candidate.Slug,
                    Place: index + 1,
                    Clock: "bortz",
                    Improvement: candidate.BortzAgeImprovement,
                    AgeReduction: candidate.BortzAgeReduction))
                .ToList();
        }

        return [];
    }

    private void SyncAgeImprovementTop10Placements(bool emitEvents, IEnumerable<string>? eventSubjectSlugs = null)
    {
        SyncAgeImprovementTop10Placements(
            clock: "pheno",
            placementColumn: PhenoImprovementTop10PlacementColumn,
            currentTop10: GetAgeImprovementTop10Snapshot("pheno"),
            emitEvents: emitEvents,
            eventSubjectSlugs: eventSubjectSlugs);

        SyncAgeImprovementTop10Placements(
            clock: "bortz",
            placementColumn: BortzImprovementTop10PlacementColumn,
            currentTop10: GetAgeImprovementTop10Snapshot("bortz"),
            emitEvents: emitEvents,
            eventSubjectSlugs: eventSubjectSlugs);
    }

    private void SyncAgeImprovementTop10Placements(
        string clock,
        string placementColumn,
        IReadOnlyList<(string Slug, int Place, string Clock, double Improvement, double AgeReduction)> currentTop10,
        bool emitEvents,
        IEnumerable<string>? eventSubjectSlugs)
    {
        var currentBySlug = currentTop10.ToDictionary(x => x.Slug, x => x, StringComparer.OrdinalIgnoreCase);
        var eventSubjects = BuildTop10EventSubjectSet(emitEvents, eventSubjectSlugs);
        var changed = new List<(string AthleteSlug, DateTime OccurredAtUtc, string Clock, int Place, int? PreviousPlace, string? PreviousSlug, double Improvement, double AgeReduction)>();
        var nowUtc = DateTime.UtcNow;

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var select = sqlite.CreateCommand();
            select.Transaction = tx;
            select.CommandText = $"SELECT Key, {placementColumn} FROM Athletes";

            var stored = new List<(string Slug, int? Place)>();
            using (var r = select.ExecuteReader())
            {
                while (r.Read())
                {
                    var slug = r.GetString(0);
                    var place = r.IsDBNull(1) ? (int?)null : r.GetInt32(1);
                    stored.Add((slug, place));
                }
            }

            var previousSlugByPlace = stored
                .Where(x => x.Place is >= 1 and <= 10)
                .GroupBy(x => x.Place!.Value)
                .ToDictionary(g => g.Key, g => g.First().Slug);

            using var update = sqlite.CreateCommand();
            update.Transaction = tx;
            update.CommandText = $"UPDATE Athletes SET {placementColumn}=@place WHERE Key=@slug";
            var pPlace = update.Parameters.Add("@place", SqliteType.Integer);
            var pSlug = update.Parameters.Add("@slug", SqliteType.Text);

            foreach (var (slug, previousPlace) in stored)
            {
                currentBySlug.TryGetValue(slug, out var current);
                int? currentPlace = current.Place is >= 1 and <= 10 ? current.Place : null;

                if (emitEvents &&
                    currentPlace.HasValue &&
                    previousPlace != currentPlace)
                {
                    previousSlugByPlace.TryGetValue(currentPlace.Value, out var previousSlug);
                    if (string.Equals(previousSlug, slug, StringComparison.OrdinalIgnoreCase))
                        previousSlug = null;

                    if (ShouldEmitTop10PlacementChangeEvent(slug, previousPlace, currentPlace.Value, previousSlug, eventSubjects))
                    {
                        changed.Add((
                            slug,
                            nowUtc,
                            clock,
                            currentPlace.Value,
                            previousPlace,
                            previousSlug,
                            current.Improvement,
                            current.AgeReduction));
                    }
                }

                if (previousPlace == currentPlace) continue;

                pPlace.Value = currentPlace.HasValue ? currentPlace.Value : DBNull.Value;
                pSlug.Value = slug;
                update.ExecuteNonQuery();
            }

            tx.Commit();
        });

        if (changed.Count > 0)
            _eventDataService.CreateAgeImprovementTop10ChangeEvents(changed, skipIfExists: true);
    }

    private static HashSet<string> BuildTop10EventSubjectSet(bool emitEvents, IEnumerable<string>? eventSubjectSlugs)
    {
        if (!emitEvents)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return (eventSubjectSlugs ?? Enumerable.Empty<string>())
            .Where(slug => !string.IsNullOrWhiteSpace(slug))
            .Select(slug => slug.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static bool ShouldEmitTop10PlacementChangeEvent(
        string slug,
        int? previousPlace,
        int currentPlace,
        string? previousSlug,
        IReadOnlySet<string> eventSubjectSlugs)
    {
        if (string.IsNullOrWhiteSpace(slug) || !eventSubjectSlugs.Contains(slug))
            return false;

        if (string.IsNullOrWhiteSpace(previousSlug))
            return false;

        return !previousPlace.HasValue || currentPlace < previousPlace.Value;
    }

    private static double CalculateAgeAtDate(DateTime birthDateUtc, DateTime atDateUtc)
    {
        return Math.Round((atDateUtc.Date - birthDateUtc.Date).TotalDays / 365.2425, 2);
    }

    public IReadOnlyList<string> GetRecentNewcomersForX()
    {
        const int windowDays = 7;
        var cutoff = DateTime.UtcNow.AddDays(-windowDays);
        var slugs = new List<string>();
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Key, JoinedAt FROM Athletes WHERE JoinedAt >= @cutoff";
            cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("o"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                slugs.Add(key);
            }
        });

        if (slugs.Count == 0) return Array.Empty<string>();

        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in GetAthletesSnapshot().OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(slug)) existing.Add(slug);
        }

        return slugs.Where(s => existing.Contains(s)).ToList();
    }

    public string? GetBestDomainWinnerSlug(string domainKey)
    {
        var label = BestDomainBadgeLabelForDomainKey(domainKey);
        if (label is null) return null;
        var labels = BadgeLabelQueryVariants(label);
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            var labelPlaceholders = AddBadgeLabelParameters(cmd, labels);
            cmd.CommandText =
                "SELECT DISTINCT AthleteSlug FROM BadgeAwards " +
                $"WHERE BadgeLabel IN ({labelPlaceholders}) AND LeagueCategory='Global' AND Place=1 " +
                "LIMIT 2";
            using var r = cmd.ExecuteReader();
            string? onlyHolder = null;
            while (r.Read())
            {
                var slug = r.GetString(0);
                if (string.IsNullOrWhiteSpace(slug))
                    continue;

                if (onlyHolder is null)
                {
                    onlyHolder = slug;
                    continue;
                }

                return null;
            }

            return onlyHolder;
        });
    }

    private static string? BestDomainBadgeLabelForDomainKey(string? domainKey)
    {
        var normalizedDomain = (domainKey ?? string.Empty)
            .Trim()
            .Replace('-', '_')
            .Replace(' ', '_')
            .ToLowerInvariant();

        return normalizedDomain switch
        {
            "liver" => "Best domain – liver",
            "kidney" => "Best domain – kidney",
            "metabolic" => "Best domain – metabolic",
            "inflammation" => "Best domain – inflammation",
            "immune" => "Best domain – immune",
            "vitamin_d" => "Best domain – vitamin D",
            _ => null
        };
    }

    public bool HasSingleGlobalPlaceOneBadgeHolder(string badgeLabel)
    {
        if (string.IsNullOrWhiteSpace(badgeLabel))
            return false;

        var labels = BadgeLabelQueryVariants(badgeLabel);
        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            var labelPlaceholders = AddBadgeLabelParameters(cmd, labels);
            cmd.CommandText =
                "SELECT DISTINCT AthleteSlug FROM BadgeAwards " +
                $"WHERE BadgeLabel IN ({labelPlaceholders}) AND LeagueCategory='Global' AND Place=1 " +
                "LIMIT 2";

            using var r = cmd.ExecuteReader();
            string? onlyHolder = null;
            while (r.Read())
            {
                if (r.IsDBNull(0))
                    continue;

                var slug = r.GetString(0);
                if (string.IsNullOrWhiteSpace(slug))
                    continue;

                if (onlyHolder is null)
                {
                    onlyHolder = slug;
                    continue;
                }

                return false;
            }

            return !string.IsNullOrWhiteSpace(onlyHolder);
        });
    }

    private static string AddBadgeLabelParameters(SqliteCommand cmd, IReadOnlyList<string> labels)
    {
        var names = new List<string>(labels.Count);
        for (var i = 0; i < labels.Count; i++)
        {
            var name = $"@badgeLabel{i}";
            cmd.Parameters.AddWithValue(name, labels[i]);
            names.Add(name);
        }

        return string.Join(",", names);
    }

    private static IReadOnlyList<string> BadgeLabelQueryVariants(string badgeLabel)
    {
        var canonical = EventHelpers.NormalizeBadgeLabel(badgeLabel);
        var variants = new HashSet<string>(StringComparer.Ordinal)
        {
            canonical,
            canonical.Replace(" – ", " - ", StringComparison.Ordinal)
        };

        switch (canonical)
        {
            case "Chronological age – oldest":
                variants.Add("Chronological Age – Oldest");
                variants.Add("Chronological Age - Oldest");
                break;
            case "Chronological age – youngest":
                variants.Add("Chronological Age – Youngest");
                variants.Add("Chronological Age - Youngest");
                break;
            case "Pheno Age – lowest":
                variants.Add("PhenoAge – Lowest");
                variants.Add("PhenoAge - Lowest");
                break;
            case "Bortz Age – lowest":
                variants.Add("Bortz Age – Lowest");
                variants.Add("Bortz Age - Lowest");
                break;
            case "Crowd – most guessed":
                variants.Add("Crowd – Most Guessed");
                variants.Add("Crowd - Most Guessed");
                break;
            case "Crowd – age gap (chrono−crowd)":
                variants.Add("Crowd – Age Gap (Chrono−Crowd)");
                variants.Add("Crowd - Age Gap (Chrono−Crowd)");
                break;
            case "Crowd Age – lowest":
                variants.Add("Crowd Age – lowest");
                variants.Add("Crowd Age - lowest");
                variants.Add("Crowd – Lowest Crowd Age");
                variants.Add("Crowd - Lowest Crowd Age");
                variants.Add("Crowd – lowest crowd age");
                variants.Add("Crowd - lowest crowd age");
                break;
            case "First applicants":
                variants.Add("First Applicants");
                break;
            case "Perfect application":
                variants.Add("Perfect Application");
                break;
        }

        if (canonical.StartsWith("Best domain – ", StringComparison.Ordinal))
        {
            var domain = canonical["Best domain – ".Length..].Trim();
            var legacyDomain = string.Equals(domain, "vitamin D", StringComparison.Ordinal)
                ? "Vitamin D"
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(domain);
            variants.Add("Best Domain – " + legacyDomain);
            variants.Add("Best Domain - " + legacyDomain);
        }

        return variants.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
    }

    public IReadOnlyList<string> GetTop3SlugsForLeague(string leagueSlug)
    {
        return GetLeagueSlugsInRankOrder(leagueSlug)
            .Take(3)
            .ToList();
    }

    public int GetLeagueFieldSize(string leagueSlug)
    {
        return GetLeagueSlugsInRankOrder(leagueSlug).Count;
    }

    public int GetLeagueBortzFieldSize(string leagueSlug)
    {
        return GetLeagueSlugsInRankOrder(leagueSlug, requireBortz: true).Count;
    }

    private IReadOnlyList<string> GetLeagueSlugsInRankOrder(string leagueSlug, bool requireBortz = false)
    {
        if (!requireBortz && string.Equals(leagueSlug, "crowd", StringComparison.OrdinalIgnoreCase))
        {
            return CompetitionRanking.SortByCrowdAgeRules(GetCrowdAgeRankCandidates())
                .Select(t => t.Slug)
                .ToList();
        }

        if (!requireBortz &&
            (string.Equals(leagueSlug, "improvement", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(leagueSlug, "pheno-improvement", StringComparison.OrdinalIgnoreCase)))
        {
            return CompetitionRanking.SortByPhenoAgeImprovementRules(GetPhenoAgeImprovementRankCandidates())
                .Select(t => t.Slug)
                .ToList();
        }

        if (!requireBortz && string.Equals(leagueSlug, "bortz-improvement", StringComparison.OrdinalIgnoreCase))
        {
            return CompetitionRanking.SortByBortzAgeImprovementRules(GetBortzAgeImprovementRankCandidates())
                .Select(t => t.Slug)
                .ToList();
        }

        var order = GetRankingsOrder();
        if (order.Count == 0) return Array.Empty<string>();
        if (string.Equals(leagueSlug, "ultimate", StringComparison.OrdinalIgnoreCase))
        {
            return order.OfType<JsonObject>()
                .Where(o => !requireBortz || o["LowestBortzAge"] is JsonValue)
                .Select(o => o["AthleteSlug"]?.GetValue<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList();
        }

        var snapshot = GetAthletesSnapshot();
        var divisionBySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var generationBySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exclusiveBySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in snapshot.OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;
            var div = o["Division"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(div)) divisionBySlug[slug] = div;
            var gen = GenerationResolver.ResolveFromAthleteJson(o);
            if (!string.IsNullOrWhiteSpace(gen)) generationBySlug[slug] = gen;
            var ex = o["ExclusiveLeague"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(ex)) exclusiveBySlug[slug] = ex;
        }

        string? targetDivision = leagueSlug?.ToLowerInvariant() switch { "mens" => "Men's", "womens" => "Women's", "open" => "Open", _ => null };
        string? targetGeneration = leagueSlug?.ToLowerInvariant() switch
        {
            "silent-generation" => "Silent Generation",
            "baby-boomers" => "Baby Boomers",
            "gen-x" => "Gen X",
            "millennials" => "Millennials",
            "gen-z" => "Gen Z",
            "gen-alpha" => "Gen Alpha",
            _ => null
        };
        string? targetExclusive = string.Equals(leagueSlug, "prosperan", StringComparison.OrdinalIgnoreCase) ? "Prosperan" : null;
        var isAmateur = string.Equals(leagueSlug, "amateur", StringComparison.OrdinalIgnoreCase);
        if (targetDivision == null && targetGeneration == null && targetExclusive == null && !isAmateur)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var o in order.OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;
            if (requireBortz && o["LowestBortzAge"] is not JsonValue) continue;
            var match = targetDivision != null && divisionBySlug.TryGetValue(slug, out var d) && string.Equals(d, targetDivision, StringComparison.OrdinalIgnoreCase)
                || targetGeneration != null && generationBySlug.TryGetValue(slug, out var g) && string.Equals(g, targetGeneration, StringComparison.OrdinalIgnoreCase)
                || targetExclusive != null && exclusiveBySlug.TryGetValue(slug, out var e) && string.Equals(e, targetExclusive, StringComparison.OrdinalIgnoreCase)
                || isAmateur && o["LowestBortzAge"] is not JsonValue;
            if (match)
                list.Add(slug);
        }

        return list;
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
        using var operation = _reloadOperations.Enter();
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

    private void HydrateAgeImprovementIntoAthletesJson()
    {
        var snapshot = GetAthletesSnapshot();
        var statsMap = PhenoStatsCalculator.BuildAll(snapshot, DateTime.UtcNow.Date);

        lock (_athletesJsonLock)
        {
            foreach (var athleteJson in _athletes.OfType<JsonObject>())
            {
                var slug = athleteJson["AthleteSlug"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(slug) ||
                    !statsMap.TryGetValue(slug, out var stats))
                {
                    athleteJson.Remove("PhenoAgeImprovementFromWorst");
                    athleteJson.Remove("BortzAgeImprovementFromWorst");
                    continue;
                }

                if (stats.PhenoAgeImprovementFromWorst.HasValue &&
                    double.IsFinite(stats.PhenoAgeImprovementFromWorst.Value))
                {
                    athleteJson["PhenoAgeImprovementFromWorst"] = stats.PhenoAgeImprovementFromWorst.Value;
                }
                else
                {
                    athleteJson.Remove("PhenoAgeImprovementFromWorst");
                }

                if (stats.BortzAgeImprovementFromWorst.HasValue &&
                    double.IsFinite(stats.BortzAgeImprovementFromWorst.Value))
                {
                    athleteJson["BortzAgeImprovementFromWorst"] = stats.BortzAgeImprovementFromWorst.Value;
                }
                else
                {
                    athleteJson.Remove("BortzAgeImprovementFromWorst");
                }
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
        using var operation = _reloadOperations.Enter();
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        var reloadOperationsDrained = _reloadOperations.StopAndDrainAsync();
        _db.DatabaseChanged -= OnDatabaseChanged;
        _athleteWatcher.Dispose();
        _reloadWorkerCts.Cancel();

        try
        {
            // Cancellation stops debounce and source I/O before publication. If
            // atomic snapshot publication has already begun, join it to completion
            // before the database and synchronization primitives can be disposed.
            _reloadWorkerTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        reloadOperationsDrained.GetAwaiter().GetResult();
        _reloadWorkerCts.Dispose();
        _reloadSignal.Dispose();
        _reloadLock.Dispose();
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

    private void SyncAgeGuessProfileImageIds(bool migrateLegacyGuesses)
    {
        Dictionary<string, string> currentImageIds;
        lock (_athletesJsonLock)
        {
            currentImageIds = _athletes
                .OfType<JsonObject>()
                .Select(athlete => (
                    Slug: athlete["AthleteSlug"]?.GetValue<string>() ?? "",
                    ImageId: athlete[ProfileImageIdProperty] is JsonValue imageIdValue &&
                             imageIdValue.TryGetValue<string>(out var imageId)
                        ? imageId
                        : ""))
                .Where(item => !string.IsNullOrWhiteSpace(item.Slug))
                .ToDictionary(item => item.Slug, item => item.ImageId, StringComparer.OrdinalIgnoreCase);
        }

        _db.Run(sqlite =>
        {
            // Acquire the write reservation before reading whole-row JSON so a
            // second app instance cannot append a guess that this migration then
            // overwrites with its stale pre-transaction copy.
            using var transaction = sqlite.BeginTransaction(deferred: false);
            var rows = new List<(string Slug, string AgeGuesses, string? StoredImageId)>();
            using (var select = sqlite.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText = $"SELECT Key, AgeGuesses, {CrowdAgeProfileImageIdColumn} FROM Athletes";
                using var reader = select.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add((
                        reader.GetString(0),
                        reader.IsDBNull(1) ? "[]" : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2)));
                }
            }

            using var update = sqlite.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                $"UPDATE Athletes SET AgeGuesses=@ageGuesses, {CrowdAgeProfileImageIdColumn}=@imageId WHERE Key=@slug";
            var ageGuessesParameter = update.Parameters.Add("@ageGuesses", SqliteType.Text);
            var imageIdParameter = update.Parameters.Add("@imageId", SqliteType.Text);
            var slugParameter = update.Parameters.Add("@slug", SqliteType.Text);

            foreach (var row in rows)
            {
                var currentImageId = currentImageIds.GetValueOrDefault(row.Slug, "");
                var ageGuessesJson = row.AgeGuesses;
                var guessesChanged = false;

                // A NULL stored image ID means this row has never crossed the migration
                // boundary. Missing-ID guesses written during a compatible rollback are
                // safe to bind only while the stored and current image identities agree.
                // If they differ, stamp the guesses with the inert sentinel immediately;
                // otherwise updating the row marker could make a later restart mistake
                // those ambiguous guesses for guesses made against the new portrait.
                var storedMarkerMatchesCurrent =
                    row.StoredImageId is not null &&
                    string.Equals(row.StoredImageId, currentImageId, StringComparison.Ordinal);
                var storedImageMatchesCurrent =
                    storedMarkerMatchesCurrent &&
                    !string.IsNullOrWhiteSpace(currentImageId);
                var shouldProcessMissingGuessIds =
                    row.StoredImageId is null ||
                    (migrateLegacyGuesses && storedMarkerMatchesCurrent) ||
                    (row.StoredImageId is not null && !storedMarkerMatchesCurrent);
                if (shouldProcessMissingGuessIds)
                {
                    var guesses = JsonSerializer.Deserialize<List<JsonObject>>(ageGuessesJson) ?? [];
                    var migrationImageId = row.StoredImageId is null || storedImageMatchesCurrent
                        ? (string.IsNullOrWhiteSpace(currentImageId)
                            ? LegacyWithoutProfileImageId
                            : currentImageId)
                        : LegacyWithoutProfileImageId;
                    foreach (var guess in guesses)
                    {
                        if (guess[ProfileImageIdProperty] is JsonValue existingImageId &&
                            existingImageId.TryGetValue<string>(out var value) &&
                            !string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }

                        guess[ProfileImageIdProperty] = migrationImageId;
                        guessesChanged = true;
                    }

                    if (guessesChanged)
                        ageGuessesJson = JsonSerializer.Serialize(guesses);
                }

                if (!guessesChanged &&
                    string.Equals(row.StoredImageId, currentImageId, StringComparison.Ordinal))
                {
                    continue;
                }

                ageGuessesParameter.Value = ageGuessesJson;
                imageIdParameter.Value = currentImageId;
                slugParameter.Value = row.Slug;
                update.ExecuteNonQuery();
            }

            transaction.Commit();
        });
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

    private List<(string AthleteSlug, DateTime OccurredAtUtc)> SyncProTrackStates(IEnumerable<string>? newcomerSlugs)
    {
        var newcomers = newcomerSlugs?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = BuildProTrackMap();
        var nowUtc = DateTime.UtcNow;
        var becamePro = new List<(string AthleteSlug, DateTime OccurredAtUtc)>();

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var select = sqlite.CreateCommand();
            select.Transaction = tx;
            select.CommandText = $"SELECT Key, {HadBortzColumn} FROM Athletes";

            var stored = new List<(string Slug, int? HadBortz)>();
            using (var r = select.ExecuteReader())
            {
                while (r.Read())
                {
                    var slug = r.GetString(0);
                    int? hadBortz = r.IsDBNull(1) ? null : r.GetInt32(1);
                    stored.Add((slug, hadBortz));
                }
            }

            using var update = sqlite.CreateCommand();
            update.Transaction = tx;
            update.CommandText = $"UPDATE Athletes SET {HadBortzColumn}=@hadBortz WHERE Key=@slug";
            var pHadBortz = update.Parameters.Add("@hadBortz", SqliteType.Integer);
            var pSlug = update.Parameters.Add("@slug", SqliteType.Text);

            foreach (var (slug, hadBortz) in stored)
            {
                var hasBortzNow = current.TryGetValue(slug, out var isPro) && isPro;
                if (hadBortz == 0 && hasBortzNow && !newcomers.Contains(slug))
                {
                    becamePro.Add((slug, nowUtc));
                }

                var currentValue = hasBortzNow ? 1 : 0;
                if (hadBortz == currentValue) continue;

                pHadBortz.Value = currentValue;
                pSlug.Value = slug;
                update.ExecuteNonQuery();
            }

            tx.Commit();
        });

        return becamePro;
    }

    private Dictionary<string, bool> BuildProTrackMap()
    {
        var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var rankings = GetRankingsOrder();
        foreach (var o in rankings.OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;

            map[slug] = o["LowestBortzAge"] is JsonValue bortzVal &&
                        bortzVal.TryGetValue<double>(out var bortzAge) &&
                        double.IsFinite(bortzAge);
        }

        return map;
    }

    private List<(string AthleteSlug, DateTime OccurredAtUtc, string Clock, double PreviousAge, double NewAge)> SyncBestBioAgeStates(
        IEnumerable<string>? changedSlugs,
        IEnumerable<string>? newcomerSlugs)
    {
        var changed = changedSlugs?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                      ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newcomers = newcomerSlugs?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = BuildBestBioAgeMap();
        var improvements = new List<(string AthleteSlug, DateTime OccurredAtUtc, string Clock, double PreviousAge, double NewAge)>();
        var eventDateCorrections = new List<(string AthleteSlug, string Clock, double NewAge, DateTime ResultDateUtc)>();

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var select = sqlite.CreateCommand();
            select.Transaction = tx;
            select.CommandText =
                $"SELECT Key, {LastLowestPhenoAgeColumn}, {LastLowestBortzAgeColumn}, " +
                $"{LastLowestPhenoAgeDateColumn}, {LastLowestBortzAgeDateColumn} FROM Athletes";

            var stored = new List<(string Slug, double? LastPheno, double? LastBortz, DateTime? LastPhenoDateUtc, DateTime? LastBortzDateUtc)>();
            using (var r = select.ExecuteReader())
            {
                while (r.Read())
                {
                    var slug = r.GetString(0);
                    var lastPheno = r.IsDBNull(1) ? (double?)null : r.GetDouble(1);
                    var lastBortz = r.IsDBNull(2) ? (double?)null : r.GetDouble(2);
                    var lastPhenoDateUtc = r.IsDBNull(3) ? null : ParseStoredResultDate(r.GetString(3));
                    var lastBortzDateUtc = r.IsDBNull(4) ? null : ParseStoredResultDate(r.GetString(4));
                    stored.Add((slug, lastPheno, lastBortz, lastPhenoDateUtc, lastBortzDateUtc));
                }
            }

            using var update = sqlite.CreateCommand();
            update.Transaction = tx;
            update.CommandText =
                $"UPDATE Athletes SET {LastLowestPhenoAgeColumn}=@pheno, {LastLowestBortzAgeColumn}=@bortz, " +
                $"{LastLowestPhenoAgeDateColumn}=@phenoDate, {LastLowestBortzAgeDateColumn}=@bortzDate WHERE Key=@slug";
            var pPheno = update.Parameters.Add("@pheno", SqliteType.Real);
            var pBortz = update.Parameters.Add("@bortz", SqliteType.Real);
            var pPhenoDate = update.Parameters.Add("@phenoDate", SqliteType.Text);
            var pBortzDate = update.Parameters.Add("@bortzDate", SqliteType.Text);
            var pSlug = update.Parameters.Add("@slug", SqliteType.Text);

            foreach (var (slug, lastPheno, lastBortz, lastPhenoDateUtc, lastBortzDateUtc) in stored)
            {
                current.TryGetValue(slug, out var currentAges);
                var currentPheno = currentAges.Pheno;
                var currentBortz = currentAges.Bortz;
                var currentPhenoDateUtc = currentAges.PhenoDateUtc;
                var currentBortzDateUtc = currentAges.BortzDateUtc;
                var canEmit = changed.Contains(slug) && !newcomers.Contains(slug);

                if (!lastPhenoDateUtc.HasValue && currentPheno.HasValue && currentPhenoDateUtc.HasValue)
                    eventDateCorrections.Add((slug, "pheno", currentPheno.Value, currentPhenoDateUtc.Value));

                if (!lastBortzDateUtc.HasValue && currentBortz.HasValue && currentBortzDateUtc.HasValue)
                    eventDateCorrections.Add((slug, "bortz", currentBortz.Value, currentBortzDateUtc.Value));

                if (ShouldEmitBiologicalAgeImprovement(canEmit, lastPheno, lastPhenoDateUtc, currentPheno, currentPhenoDateUtc))
                    improvements.Add((slug, currentPhenoDateUtc!.Value, "pheno", lastPheno!.Value, currentPheno!.Value));

                if (ShouldEmitBiologicalAgeImprovement(canEmit, lastBortz, lastBortzDateUtc, currentBortz, currentBortzDateUtc))
                    improvements.Add((slug, currentBortzDateUtc!.Value, "bortz", lastBortz!.Value, currentBortz!.Value));

                if (SameNullableDouble(lastPheno, currentPheno) &&
                    SameNullableDouble(lastBortz, currentBortz) &&
                    SameNullableDate(lastPhenoDateUtc, currentPhenoDateUtc) &&
                    SameNullableDate(lastBortzDateUtc, currentBortzDateUtc))
                    continue;

                pPheno.Value = currentPheno.HasValue ? currentPheno.Value : DBNull.Value;
                pBortz.Value = currentBortz.HasValue ? currentBortz.Value : DBNull.Value;
                pPhenoDate.Value = currentPhenoDateUtc.HasValue ? currentPhenoDateUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;
                pBortzDate.Value = currentBortzDateUtc.HasValue ? currentBortzDateUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;
                pSlug.Value = slug;
                update.ExecuteNonQuery();
            }

            tx.Commit();
        });

        if (eventDateCorrections.Count > 0)
            _eventDataService.ReconcileBiologicalAgeImprovementEventDates(eventDateCorrections);

        return improvements;
    }

    private Dictionary<string, (double? Pheno, DateTime? PhenoDateUtc, double? Bortz, DateTime? BortzDateUtc)> BuildBestBioAgeMap()
    {
        var snapshot = GetAthletesSnapshot();
        var statsMap = PhenoStatsCalculator.BuildAll(snapshot, DateTime.UtcNow.Date);
        var map = new Dictionary<string, (double? Pheno, DateTime? PhenoDateUtc, double? Bortz, DateTime? BortzDateUtc)>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in statsMap.Values)
        {
            var pheno = result.SubmissionCount > 0 && result.LowestPhenoAge.HasValue && double.IsFinite(result.LowestPhenoAge.Value)
                ? result.LowestPhenoAge
                : null;
            var bortz = result.BortzSubmissionCount > 0 && result.LowestBortzAge.HasValue && double.IsFinite(result.LowestBortzAge.Value)
                ? result.LowestBortzAge
                : null;
            map[result.Slug] = (
                pheno,
                pheno.HasValue ? result.LowestPhenoAgeDateUtc : null,
                bortz,
                bortz.HasValue ? result.LowestBortzAgeDateUtc : null);
        }

        return map;
    }

    private static bool IsImproved(double? previous, double? current)
    {
        return previous.HasValue &&
               current.HasValue &&
               double.IsFinite(previous.Value) &&
               double.IsFinite(current.Value) &&
               current.Value < previous.Value;
    }

    internal static bool ShouldEmitBiologicalAgeImprovement(
        bool canEmit,
        double? previousAge,
        DateTime? previousBestDateUtc,
        double? currentAge,
        DateTime? currentBestDateUtc)
    {
        return canEmit &&
               IsImproved(previousAge, currentAge) &&
               previousBestDateUtc.HasValue &&
               currentBestDateUtc.HasValue &&
               currentBestDateUtc.Value.Date >= previousBestDateUtc.Value.Date;
    }

    private static DateTime? ParseStoredResultDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc)
            : null;

    private static bool SameNullableDate(DateTime? left, DateTime? right) =>
        left?.Date == right?.Date;

    private static bool SameNullableDouble(double? left, double? right)
    {
        if (!left.HasValue || !right.HasValue)
            return !left.HasValue && !right.HasValue;

        return Math.Abs(left.Value - right.Value) < 0.0000001;
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
        _eventDataService.SetAthletesForX(GetAthletesForX());
        var athletesSnapshot = GetAthletesSnapshot();
        var bioList = new List<(string Slug, double? ChronologicalAge, double? LowestPhenoAge, double? LowestBortzAge)>();
        foreach (var o in athletesSnapshot.OfType<JsonObject>())
        {
            var slug = o["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;

            double? chrono = null;
            double? pheno = null;
            double? bortz = null;

            if (o["ChronoAge"] is JsonValue chronoVal && chronoVal.TryGetValue<double>(out var chronoOut))
                chrono = chronoOut;
            else if (o["ChronologicalAge"] is JsonValue chronologicalAgeVal && chronologicalAgeVal.TryGetValue<double>(out var chronologicalAgeOut))
                chrono = chronologicalAgeOut;

            if (o["LowestPhenoAge"] is JsonValue phenoVal && phenoVal.TryGetValue<double>(out var phenoOut))
                pheno = phenoOut;

            if (o["LowestBortzAge"] is JsonValue bortzVal && bortzVal.TryGetValue<double>(out var bortzOut))
                bortz = bortzOut;

            bioList.Add((slug, chrono, pheno, bortz));
        }

        _eventDataService.SetAthleteBio(bioList);
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

    private static string NormalizeAthleteSlug(string? slug)
    {
        return (slug ?? string.Empty).Trim().Replace('-', '_').ToLowerInvariant();
    }
}
