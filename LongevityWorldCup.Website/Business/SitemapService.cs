using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace LongevityWorldCup.Website.Business;

public sealed class SitemapService(LeaderboardFactsService leaderboardFacts, IWebHostEnvironment env)
{
    private const string SiteBaseUrl = "https://longevityworldcup.com";
    private static readonly XNamespace SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public static readonly IReadOnlyList<SitemapRoute> StaticRoutes =
    [
        new("/", "index.html", "daily", 1.0m),
        new("/leaderboard", "leaderboard/leaderboard.html", "daily", 0.9m),
        new("/events", "event-board/event-board.html", "daily", 0.8m),
        new("/media", "misc-pages/media.html", "monthly", 0.6m),
        new("/about", "misc-pages/about.html", "monthly", 0.6m),
        new("/history", "misc-pages/history.html", "monthly", 0.6m),
        new("/ruleset", "misc-pages/ruleset.html", "weekly", 0.7m),
        new("/llms.txt", "llms.txt", "weekly", 0.2m),
        new("/llms-full.txt", "llms-full.txt", "weekly", 0.2m),
        new("/ai/index.md", "ai/index.md", "weekly", 0.2m),
        new("/ai/leaderboard.md", null, "daily", 0.5m),
        new("/ai/athlete-names.md", null, "daily", 0.4m),
        new("/swagger", null, "weekly", 0.3m)
    ];

    public static readonly IReadOnlyList<string> PublicLeaguePaths =
    [
        "/league/amateur",
        "/league/mens",
        "/league/womens",
        "/league/open",
        "/league/silent-generation",
        "/league/baby-boomers",
        "/league/gen-x",
        "/league/millennials",
        "/league/gen-z",
        "/league/gen-alpha",
        "/league/prosperan",
        "/league/bortz",
        "/league/pheno",
        "/league/crowd"
    ];

    public string BuildXml()
    {
        var webRoot = env.WebRootPath;
        var athletesLastModifiedBySlug = GetAthleteJsonLastModifiedBySlug(webRoot);
        var leaderboardLastModified = MaxUtc(athletesLastModifiedBySlug.Values) ?? DateTime.UtcNow.Date;

        var entries = new List<SitemapUrlEntry>();
        foreach (var route in StaticRoutes)
        {
            var lastModified = route.RelativeFilePath is null
                ? leaderboardLastModified
                : GetFileLastModifiedUtc(webRoot, route.RelativeFilePath) ?? leaderboardLastModified;

            entries.Add(new SitemapUrlEntry(route.Path, lastModified, route.ChangeFrequency, route.Priority));
        }

        foreach (var path in PublicLeaguePaths)
        {
            entries.Add(new SitemapUrlEntry(path, leaderboardLastModified, "daily", 0.8m));
        }

        foreach (var row in leaderboardFacts.GetLeaderboardSnapshot().Rows)
        {
            var athleteLastModified = athletesLastModifiedBySlug.TryGetValue(row.Slug, out var value)
                ? value
                : leaderboardLastModified;
            entries.Add(new SitemapUrlEntry(row.AthletePath, athleteLastModified, "weekly", 0.6m));
        }

        return BuildXml(entries);
    }

    public static string BuildXml(IEnumerable<SitemapUrlEntry> entries)
    {
        var distinctEntries = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Path))
            .GroupBy(e => NormalizePath(e.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.LastModifiedUtc).First())
            .OrderBy(e => RouteOrder(e.Path))
            .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(
                SitemapNamespace + "urlset",
                distinctEntries.Select(entry =>
                    new XElement(
                        SitemapNamespace + "url",
                        new XElement(SitemapNamespace + "loc", $"{SiteBaseUrl}{NormalizePath(entry.Path)}"),
                        new XElement(SitemapNamespace + "lastmod", entry.LastModifiedUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        new XElement(SitemapNamespace + "changefreq", entry.ChangeFrequency),
                        new XElement(SitemapNamespace + "priority", entry.Priority.ToString("0.0", CultureInfo.InvariantCulture))))));

        var builder = new StringBuilder();
        builder.AppendLine(doc.Declaration?.ToString() ?? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.Append(doc);
        return builder.ToString();
    }

    private static Dictionary<string, DateTime> GetAthleteJsonLastModifiedBySlug(string webRoot)
    {
        var result = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var athletesDir = Path.Combine(webRoot, "athletes");
        if (!Directory.Exists(athletesDir))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(athletesDir, "athlete.json", SearchOption.AllDirectories))
        {
            var folderName = Path.GetFileName(Path.GetDirectoryName(file) ?? "");
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            var slug = folderName.Replace('-', '_');
            result[slug] = File.GetLastWriteTimeUtc(file);
        }

        return result;
    }

    private static DateTime? GetFileLastModifiedUtc(string webRoot, string relativePath)
    {
        var path = Path.Combine(
            new[] { webRoot }.Concat(relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToArray());
        return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
    }

    private static DateTime? MaxUtc(IEnumerable<DateTime> values)
    {
        DateTime? result = null;
        foreach (var value in values)
        {
            if (result is null || value > result.Value)
            {
                result = value;
            }
        }

        return result;
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    private static int RouteOrder(string path)
    {
        if (string.Equals(path, "/", StringComparison.Ordinal)) return 0;
        if (string.Equals(path, "/leaderboard", StringComparison.OrdinalIgnoreCase)) return 1;
        if (path.StartsWith("/league/", StringComparison.OrdinalIgnoreCase)) return 2;
        if (path.StartsWith("/athlete/", StringComparison.OrdinalIgnoreCase)) return 3;
        if (path.StartsWith("/ai/", StringComparison.OrdinalIgnoreCase)) return 5;
        return 4;
    }
}

public sealed record SitemapRoute(
    string Path,
    string? RelativeFilePath,
    string ChangeFrequency,
    decimal Priority);

public sealed record SitemapUrlEntry(
    string Path,
    DateTime LastModifiedUtc,
    string ChangeFrequency,
    decimal Priority);
