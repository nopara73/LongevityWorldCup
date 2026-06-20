using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace LongevityWorldCup.Website.Tools;

public sealed class AssetVersionProvider
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly string _webRootPath;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(PathComparer);

    public AssetVersionProvider(IWebHostEnvironment environment)
    {
        var webRootPath = !string.IsNullOrWhiteSpace(environment.WebRootPath) && Directory.Exists(environment.WebRootPath)
            ? environment.WebRootPath
            : Path.Combine(environment.ContentRootPath, "wwwroot");
        _webRootPath = Path.GetFullPath(webRootPath);
    }

    public string AppendVersion(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith('/'))
        {
            return assetPath;
        }

        var queryIndex = assetPath.IndexOf('?');
        var cleanPath = queryIndex >= 0 ? assetPath[..queryIndex] : assetPath;
        var relativePath = cleanPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_webRootPath, relativePath));

        if (!IsUnderWebRoot(fullPath) || !File.Exists(fullPath))
        {
            return assetPath;
        }

        var lastWriteUtc = File.GetLastWriteTimeUtc(fullPath);
        var version = _cache.AddOrUpdate(
            fullPath,
            _ => new CacheEntry(lastWriteUtc, ComputeHash(fullPath)),
            (_, existing) => existing.LastWriteUtc == lastWriteUtc
                ? existing
                : new CacheEntry(lastWriteUtc, ComputeHash(fullPath)))
            .Version;

        return queryIndex >= 0
            ? $"{assetPath}&v={version}"
            : $"{assetPath}?v={version}";
    }

    private bool IsUnderWebRoot(string fullPath)
    {
        var root = _webRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? _webRootPath
            : _webRootPath + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(root, PathComparison);
    }

    private static string ComputeHash(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    private sealed record CacheEntry(DateTime LastWriteUtc, string Version);
}
