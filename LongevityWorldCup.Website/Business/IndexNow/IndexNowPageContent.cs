using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Middleware;
using System.Collections.Concurrent;

namespace LongevityWorldCup.Website.Business.IndexNow;

// Hash published content bytes, never file/build timestamps or assembly versions.
// Backend source hashes are emitted at build time so Node-free production can detect
// changes to rendering/calculation code without shipping source or reading a Git checkout.
public sealed class IndexNowPageContent(IWebHostEnvironment environment, string? manifestPath = null)
{
    private static readonly Regex Partial = new(@"<!--([A-Z][A-Z-]+)-->", RegexOptions.Compiled);
    private readonly string _webRoot = environment.WebRootPath;
    private readonly string _manifestPath = manifestPath ?? Path.Combine(AppContext.BaseDirectory, "indexnow-content.txt");
    private readonly ConcurrentDictionary<string, (long Length, DateTime ModifiedUtc, string Hash)> _proofHashes = new(StringComparer.Ordinal);

    internal string HashProofs(JsonArray proofs)
    {
        var hashes = new List<string>();
        foreach (var proof in proofs)
        {
            var path = Uri.UnescapeDataString(proof?.GetValue<string>().Split('?')[0] ?? "");
            if (!path.StartsWith("/athletes/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal) || path.Contains('\\'))
                continue;
            var filePath = Path.GetFullPath(Path.Combine(_webRoot, path.TrimStart('/')));
            var root = Path.GetFullPath(Path.Combine(_webRoot, "athletes")) + Path.DirectorySeparatorChar;
            if (!filePath.StartsWith(root, StringComparison.Ordinal)) continue;
            var file = new FileInfo(filePath);
            if (!file.Exists) continue;
            if (!_proofHashes.TryGetValue(filePath, out var cached) || cached.Length != file.Length || cached.ModifiedUtc != file.LastWriteTimeUtc)
            {
                using var stream = file.OpenRead();
                cached = (file.Length, file.LastWriteTimeUtc, Convert.ToHexStringLower(SHA256.HashData(stream)));
                _proofHashes[filePath] = cached;
            }
            hashes.Add(path + ":" + cached.Hash);
        }
        return Hash(string.Join('\n', hashes.Order(StringComparer.Ordinal)));
    }

    public IReadOnlyDictionary<string, string> Build(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_webRoot)) throw new DirectoryNotFoundException(_webRoot);
        // A missing manifest is an incomplete deployment, not a change/removal of every page.
        var backend = File.ReadAllLines(_manifestPath).Order(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var fileHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var route = SitemapService.StaticRoutes.FirstOrDefault(route => route.Path == path);
            var template = route?.RelativeFilePath;
            if (RouteCanonicalization.TryGetPage(path, out var page)) template = page.TemplatePath.TrimStart('/');
            else if (path.StartsWith("/athlete/", StringComparison.Ordinal)) template = "index.html";
            else if (path.StartsWith("/league/", StringComparison.Ordinal) || path.StartsWith("/flag/", StringComparison.Ordinal))
                template = "leaderboard/leaderboard.html";

            if (template is not null && !File.Exists(Path.Combine(_webRoot, template))) continue;
            var files = new SortedSet<string>(StringComparer.Ordinal);
            if (template is not null) AddTemplate(template, files);
            if (template?.EndsWith(".html", StringComparison.Ordinal) == true)
            {
                AddTemplate("partials/head.html", files);
                // Dynamically injected profile dialogs and leaderboard modules are shared dependencies.
                if (IsRankingPage(path)) AddTemplate("partials/leaderboard-content.html", files);
            }

            var parts = files.Select(file => fileHashes.TryGetValue(file, out var hash)
                ? file + ":" + hash
                : file + ":" + (fileHashes[file] = Hash(File.ReadAllText(Path.Combine(_webRoot, file)).Replace("\r\n", "\n", StringComparison.Ordinal))))
                .ToList();
            parts.AddRange(backend.Where(line => AppliesTo(path, line)));
            result[path] = Hash(string.Join('\n', parts));
        }
        return result;
    }

    private void AddTemplate(string relativePath, ISet<string> files)
    {
        if (!File.Exists(Path.Combine(_webRoot, relativePath)) || !files.Add(relativePath)) return;
        var content = File.ReadAllText(Path.Combine(_webRoot, relativePath));
        foreach (Match partial in Partial.Matches(content))
            AddTemplate("partials/" + partial.Groups[1].Value.ToLowerInvariant() + ".html", files);
    }

    private static bool IsRankingPage(string path) => path == "/" || path == "/leaderboard" ||
        path.StartsWith("/athlete/", StringComparison.Ordinal) || path.StartsWith("/league/", StringComparison.Ordinal) ||
        path.StartsWith("/flag/", StringComparison.Ordinal) || path.StartsWith("/ai/", StringComparison.Ordinal);

    private static bool AppliesTo(string path, string line)
    {
        var group = line.Split('|', 2)[0];
        return group switch
        {
            "html" => !path.Contains('.') || path == "/swagger/index.html",
            "ranking" => IsRankingPage(path),
            "events" => path is "/" or "/events" || path.StartsWith("/athlete/", StringComparison.Ordinal),
            "challenge" => path is "/longevitymaxxing" or "/helstab-kihivas",
            "pheno" => path == "/pheno-age" || IsRankingPage(path),
            "bortz" => path == "/bortz-age" || IsRankingPage(path),
            "api" => path.StartsWith("/swagger", StringComparison.Ordinal),
            _ => false
        };
    }

    internal static string Hash(string content) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
