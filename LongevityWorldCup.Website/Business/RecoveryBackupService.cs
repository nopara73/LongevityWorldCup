using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

public sealed record RecoveryBackupResult(string Path, string Sha256, DateTimeOffset CreatedAtUtc,
    int FileCount, long UncompressedBytes, int RetainedDailyCopies);

public sealed class RecoveryBackupService(DatabaseManager database, string contentRoot, string webRoot,
    TimeProvider clock)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataRoot = Path.GetDirectoryName(database.DbPath)!;
    private sealed record SourceFile(string Path, string ArchivePath, long Length, DateTime LastWriteTimeUtc);
    private sealed class SourceChangedException(string message) : IOException(message) { }
    public string BackupDirectory => Path.Combine(_dataRoot, "RecoveryBackups");

    public async Task<RecoveryBackupResult> CreateVerifiedAsync(CancellationToken ct = default)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return await CreateOnceAsync(ct); }
            catch (SourceChangedException) when (attempt < 2) { await Task.Delay(250, ct); }
        }
    }

    private async Task<RecoveryBackupResult> CreateOnceAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        string? temporary = null;
        try
        {
            RecoveryBundle.EnsureNoLinksInParents(Path.Combine(BackupDirectory, "placeholder"));
            RecoveryBundle.CreatePrivateDirectory(BackupDirectory);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(BackupDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var lockOptions = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) lockOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var processLock = new FileStream(Path.Combine(BackupDirectory, ".lock"), lockOptions);
            temporary = Path.Combine(BackupDirectory, ".pending-" + Guid.NewGuid().ToString("N"));
            RecoveryBundle.CreatePrivateDirectory(temporary);
            var missing = new List<string>();
            var sources = EnumerateSources(missing);
            var captureDirectory = Path.Combine(temporary, "capture");
            RecoveryBundle.CreatePrivateDirectory(captureDirectory);
            // SQLite's online backup API captures committed WAL data; copying the live .db would not.
            var capturedDatabase = await Task.Run(() => database.BackupDatabase(captureDirectory, "snapshot_", 1), ct);
            var dbInfo = new FileInfo(capturedDatabase);
            sources.Add(new SourceFile(capturedDatabase, RecoveryBundle.DatabasePath, dbInfo.Length, dbInfo.LastWriteTimeUtc));
            if (sources.Sum(file => file.Length) > RecoveryBundle.MaximumBytes)
                throw new IOException("Competition data exceeds the supported recovery bundle size.");

            // Capture first, then compress only private immutable copies. Live ledgers
            // must not stay unchanged throughout compression and archive verification.
            var captured = new List<SourceFile>();
            foreach (var source in sources)
            {
                ct.ThrowIfCancellationRequested();
                if (source.ArchivePath == RecoveryBundle.DatabasePath)
                {
                    captured.Add(source);
                    continue;
                }
                var path = Path.Combine(captureDirectory, source.ArchivePath.Replace('/', Path.DirectorySeparatorChar));
                RecoveryBundle.CreatePrivateDirectory(Path.GetDirectoryName(path)!);
                await using (var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var target = RecoveryBundle.CreatePrivateFile(path))
                    await input.CopyToAsync(target, ct);
                var info = new FileInfo(source.Path);
                if (!info.Exists || info.Length != source.Length || info.LastWriteTimeUtc != source.LastWriteTimeUtc ||
                    new FileInfo(path).Length != source.Length)
                    throw new SourceChangedException($"Recovery source '{source.ArchivePath}' changed during capture; keep the previous verified copy.");
                captured.Add(source with { Path = path });
            }

            // A deployment can add/remove athlete files during capture. Never certify
            // a partial tree; once captured, later writes belong to the next backup.
            var current = EnumerateSources(new List<string>());
            var expected = sources.Where(file => file.ArchivePath != RecoveryBundle.DatabasePath).ToArray();
            if (!current.OrderBy(file => file.ArchivePath).SequenceEqual(expected.OrderBy(file => file.ArchivePath)))
                throw new SourceChangedException("Competition files changed during capture; keep the previous verified copy.");
            var created = clock.GetUtcNow();
            var archivePath = Path.Combine(temporary, "bundle.zip");
            var files = new List<RecoveryFile>();
            await using (var output = RecoveryBundle.CreatePrivateFile(archivePath))
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var source in captured.OrderBy(file => file.ArchivePath, StringComparer.Ordinal))
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(source.ArchivePath, CompressionLevel.Fastest);
                        await using (var input = new FileStream(source.Path, FileMode.Open, FileAccess.Read,
                            FileShare.Read | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
                        await using (var target = entry.Open())
                            await input.CopyToAsync(target, ct);
                        files.Add(new RecoveryFile(source.ArchivePath, source.Length,
                            await RecoveryBundle.HashAsync(source.Path, ct), source.LastWriteTimeUtc));
                    }
                    var manifest = new RecoveryManifest(1, created,
                        typeof(RecoveryBackupService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
                        files, missing);
                    await using var stream = archive.CreateEntry(RecoveryBundle.ManifestPath).Open();
                    await JsonSerializer.SerializeAsync(stream, manifest, RecoveryBundle.JsonOptions, ct);
                }
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
            }

            var restored = await RecoveryBundle.ExtractVerifiedAsync(archivePath, Path.Combine(temporary, "verification"), ct);
            var hash = await RecoveryBundle.HashAsync(archivePath, ct);
            ct.ThrowIfCancellationRequested();
            var finalPath = Path.Combine(BackupDirectory, "LWC-Recovery-" +
                created.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".zip");
            // Same-volume atomic publication happens only after a complete extraction and database check.
            File.Move(archivePath, finalPath, overwrite: true);
            PruneDailyCopies();
            return new RecoveryBackupResult(finalPath, hash, created, restored.Manifest.Files.Count,
                restored.Manifest.Files.Sum(file => file.Bytes), DailyCopies().Count());
        }
        finally
        {
            try
            {
                if (temporary is not null && Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            }
            finally { _gate.Release(); }
        }
    }

    private List<SourceFile> EnumerateSources(List<string> missing)
    {
        var files = new List<SourceFile>();
        AddFile(Path.Combine(_dataRoot, "indexnow-state.json"), "state/indexnow-state.json");
        AddFile(Path.Combine(_dataRoot, "public-content-revisions.json"), "state/public-content-revisions.json");
        AddFile(Path.Combine(contentRoot, "AppData", "subscriptions.txt"), "site/AppData/subscriptions.txt");
        AddFile(Path.Combine(contentRoot, "AppData", "paid-invoice-email-sent.txt"), "site/AppData/paid-invoice-email-sent.txt");
        AddDirectory(Path.Combine(_dataRoot, "ApplicationSubmissions"), "state/ApplicationSubmissions", optional: true, suffix: ".zip");
        AddDirectory(Path.Combine(_dataRoot, "ApplicationSubmissionResponses"), "state/ApplicationSubmissionResponses", optional: true, suffix: ".json");
        AddDirectory(Path.Combine(webRoot, "athletes"), "site/wwwroot/athletes", optional: false);
        return files;

        void AddFile(string path, string archivePath)
        {
            RecoveryBundle.ValidatePath(archivePath);
            try { _ = File.GetAttributes(path); }
            catch (FileNotFoundException) { missing.Add(archivePath); return; }
            catch (DirectoryNotFoundException) { missing.Add(archivePath); return; }
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Recovery sources must be regular files.");
            files.Add(new SourceFile(path, archivePath, info.Length, info.LastWriteTimeUtc));
        }

        void AddDirectory(string path, string archivePath, bool optional, string? suffix = null)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                if (!optional) throw new DirectoryNotFoundException("Athlete files are missing; refusing an incomplete backup.");
                missing.Add(archivePath); return;
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new IOException("Recovery sources must not contain directory links.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal))
            {
                var relative = archivePath + "/" + Path.GetFileName(entry);
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) AddDirectory(entry, relative, optional: false, suffix);
                else if (suffix is null || entry.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) AddFile(entry, relative);
            }
        }
    }

    private IEnumerable<string> DailyCopies() => Directory.EnumerateFiles(BackupDirectory, "LWC-Recovery-*.zip")
        .Where(path => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path)["LWC-Recovery-".Length..],
            "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        .OrderDescending(StringComparer.Ordinal);

    private void PruneDailyCopies()
    {
        // Only this service's daily files are managed. Existing .db backups remain untouched.
        foreach (var path in DailyCopies().Skip(7))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Unexpected link in the recovery backup directory.");
            File.Delete(path);
        }
    }
}
