using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed record RecoveryFile(string Path, long Bytes, string Sha256, DateTime LastWriteTimeUtc);
public sealed record RecoveryManifest(int Version, DateTimeOffset CreatedAtUtc, string Release,
    IReadOnlyList<RecoveryFile> Files, IReadOnlyList<string> AbsentOptionalPaths);
public sealed record RecoveryVerification(RecoveryManifest Manifest, IReadOnlyDictionary<string, long> TableRows);

// A data-only archive. Restoring it never loads application config or starts delivery workers.
public static class RecoveryBundle
{
    public const string ManifestPath = "manifest.json";
    public const string DatabasePath = "state/LongevityWorldCup.db";
    internal const long MaximumBytes = 8L * 1024 * 1024 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<RecoveryVerification> ExtractVerifiedAsync(string archivePath, string destination,
        CancellationToken ct = default)
    {
        destination = Path.GetFullPath(destination);
        // Never merge into a running site or overwrite even one existing file.
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("Recovery destination must not already exist.");
        EnsureNoLinksInParents(destination);
        CreatePrivateDirectory(destination);
        var successful = false;
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            if (archive.Entries.Count is < 2 or > 50000)
                throw new InvalidDataException("Unexpected recovery archive entry count.");
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in archive.Entries)
            {
                ValidatePath(entry.FullName, allowManifest: true);
                if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000 ||
                    !entries.TryAdd(entry.FullName, entry))
                    throw new InvalidDataException("Links and duplicate archive entries are not supported.");
            }
            if (!entries.TryGetValue(ManifestPath, out var manifestEntry) || manifestEntry.Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Recovery manifest is missing or too large.");
            RecoveryManifest manifest;
            await using (var stream = manifestEntry.Open())
            using (var bytes = new MemoryStream())
            {
                await CopyExactAsync(stream, bytes, manifestEntry.Length, ct);
                manifest = JsonSerializer.Deserialize<RecoveryManifest>(bytes.ToArray(), JsonOptions)
                    ?? throw new InvalidDataException("Recovery manifest is empty.");
            }
            if (manifest.Version != 1 || manifest.Files is null || manifest.Files.Count == 0 ||
                manifest.AbsentOptionalPaths is null || string.IsNullOrWhiteSpace(manifest.Release))
                throw new InvalidDataException("Unsupported or incomplete recovery manifest.");

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var file in manifest.Files)
            {
                ValidatePath(file.Path);
                if (!paths.Add(file.Path) || file.Bytes < 0 || file.Bytes > MaximumBytes ||
                    file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit) ||
                    !entries.TryGetValue(file.Path, out var entry) ||
                    !string.Equals(entry.FullName, file.Path, StringComparison.Ordinal) || entry.Length != file.Bytes)
                    throw new InvalidDataException("Recovery manifest does not match the archive.");
                total = checked(total + file.Bytes);
                if (total > MaximumBytes)
                    throw new InvalidDataException("Recovery archive exceeds the supported size.");
            }
            if (!paths.Contains(DatabasePath) || entries.Count != paths.Count + 1)
                throw new InvalidDataException("Recovery archive is incomplete or contains unlisted files.");

            foreach (var file in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                var output = Path.Combine(destination, file.Path.Replace('/', Path.DirectorySeparatorChar));
                CreatePrivateDirectory(Path.GetDirectoryName(output)!);
                await using (var input = entries[file.Path].Open())
                await using (var target = CreatePrivateFile(output))
                {
                    await CopyExactAsync(input, target, file.Bytes, ct);
                    await target.FlushAsync(ct);
                }
                if (!string.Equals(await HashAsync(output, ct), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Restored file checksum does not match the recovery manifest.");
                if (file.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    await using var json = File.OpenRead(output);
                    using var document = await JsonDocument.ParseAsync(json, cancellationToken: ct);
                }
                File.SetLastWriteTimeUtc(output, file.LastWriteTimeUtc);
            }
            var rows = ReadDatabase(Path.Combine(destination, DatabasePath.Replace('/', Path.DirectorySeparatorChar)));
            // Keep the exact manifest as part of the private restored copy.
            await using (var target = CreatePrivateFile(Path.Combine(destination, ManifestPath)))
                await JsonSerializer.SerializeAsync(target, manifest, JsonOptions, ct);
            successful = true;
            return new RecoveryVerification(manifest, rows);
        }
        finally
        {
            if (!successful) Directory.Delete(destination, recursive: true);
        }
    }

    internal static IReadOnlyDictionary<string, long> ReadDatabase(string path)
    {
        var uri = new Uri(Path.GetFullPath(path)).AbsoluteUri + "?immutable=1";
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = uri, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        connection.Open();
        using (var integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            using var reader = integrity.ExecuteReader();
            if (!reader.Read() || !string.Equals(reader.GetString(0), "ok", StringComparison.Ordinal) || reader.Read())
                throw new InvalidDataException("Restored SQLite database failed its integrity check.");
        }
        var tables = new List<string>();
        using (var query = connection.CreateCommand())
        {
            query.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var reader = query.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }
        var rows = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
        {
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM \"" + table.Replace("\"", "\"\"") + "\";";
            rows.Add(table, (long)count.ExecuteScalar()!);
        }
        return rows;
    }

    internal static void ValidatePath(string path, bool allowManifest = false)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Contains(':') || path.Contains('\0') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Invalid recovery archive path.");
        var permitted = allowManifest && path == ManifestPath || path == DatabasePath ||
            path is "state/indexnow-state.json" or "state/public-content-revisions.json" or
                "site/AppData/subscriptions.txt" or "site/AppData/paid-invoice-email-sent.txt" ||
            path.StartsWith("state/ApplicationSubmissions/", StringComparison.Ordinal) ||
            path.StartsWith("state/ApplicationSubmissionResponses/", StringComparison.Ordinal) ||
            path.StartsWith("site/wwwroot/athletes/", StringComparison.Ordinal);
        if (!permitted) throw new InvalidDataException("Recovery archive contains an unsupported data path.");
    }

    internal static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    private static async Task CopyExactAsync(Stream source, Stream target, long expected, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) != 0)
        {
            if (read > expected - copied) throw new InvalidDataException("Archive entry exceeds its declared size.");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;
        }
        if (copied != expected) throw new InvalidDataException("Archive entry is truncated.");
    }

    internal static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    internal static FileStream CreatePrivateFile(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    internal static void EnsureNoLinksInParents(string path)
    {
        for (var parent = Directory.GetParent(path); parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Recovery paths must not pass through symbolic links.");
    }
}
