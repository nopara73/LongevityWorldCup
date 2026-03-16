using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace LongevityWorldCup.Website.Tools;

public sealed class AssetVersionProvider
{
    private readonly string _webRootPath;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public AssetVersionProvider(IWebHostEnvironment environment)
    {
        _webRootPath = environment.WebRootPath;
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
        var fullPath = Path.Combine(_webRootPath, relativePath);

        if (!File.Exists(fullPath))
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

    private static string ComputeHash(string fullPath)
    {
        using var stream = File.OpenRead(fullPath);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    private sealed record CacheEntry(DateTime LastWriteUtc, string Version);
}
