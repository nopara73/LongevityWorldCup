using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace LongevityWorldCup.Website.Business;

public sealed class SitemapService(LeaderboardFactsService leaderboardFacts, PublicContentFreshness freshness)
{
    public const string SiteBaseUrl = "https://longevityworldcup.com";
    private static readonly XNamespace SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public static readonly IReadOnlyList<SitemapRoute> StaticRoutes =
    [
        new("/", "index.html", "daily", 1.0m),
        new("/leaderboard", "leaderboard/leaderboard.html", "daily", 0.9m),
        new("/longevitymaxxing", "longevitymaxxing/longevitymaxxing.html", "daily", 0.7m),
        new("/helstab-kihivas", "helstab-kihivas/helstab-kihivas.html", "daily", 0.7m),
        new("/events", "event-board/event-board.html", "daily", 0.8m),
        new("/media", "misc-pages/media.html", "monthly", 0.6m),
        new("/about", "misc-pages/about.html", "monthly", 0.6m),
        new("/history", "misc-pages/history.html", "monthly", 0.6m),
        new("/rejuvenation-olympics", "misc-pages/rejuvenation-olympics.html", "monthly", 0.7m),
        new("/ruleset", "misc-pages/ruleset.html", "weekly", 0.7m),
        new("/pheno-age", "onboarding/pheno-age.html", "monthly", 0.7m),
        new("/bortz-age", "onboarding/bortz-age.html", "monthly", 0.7m),
        new("/privacy", "privacy-policy.html", "yearly", 0.3m),
        new("/llms.txt", null, "weekly", 0.2m),
        new("/llms-full.txt", null, "weekly", 0.2m),
        new("/.well-known/agent-card.json", null, "weekly", 0.2m),
        new("/ai/index.md", null, "weekly", 0.2m),
        new("/ai/leaderboard.md", null, "daily", 0.5m),
        new("/ai/athlete-names.md", null, "daily", 0.4m),
        new("/swagger/index.html", null, "weekly", 0.3m),
        .. LeaderboardViewCatalog.Views.Where(view => view.Slug != "ultimate")
            .Select(view => new SitemapRoute(view.MarkdownPath, null, "daily", 0.3m))
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
        "/league/improvement",
        "/league/bortz-improvement",
        "/league/crowd"
    ];

    public string BuildXml()
    {
        var entries = StaticRoutes.Select(route => new SitemapUrlEntry(route.Path,
            GetLastModifiedUtcForPath(route.Path), route.ChangeFrequency, route.Priority)).ToList();
        var snapshot = leaderboardFacts.GetLeaderboardSnapshot();
        foreach (var path in PublicLeaguePaths)
            entries.Add(new SitemapUrlEntry(path, GetLastModifiedUtcForPath(path), "daily", 0.8m));
        foreach (var flag in FlagRouteCatalog.BuildRoutes(snapshot.Rows.Select(row => row.Flag)))
            entries.Add(new SitemapUrlEntry(flag.Path, GetLastModifiedUtcForPath(flag.Path), "daily", 0.7m));
        foreach (var row in snapshot.Rows)
            entries.Add(new SitemapUrlEntry(row.AthletePath, GetLastModifiedUtcForPath(row.AthletePath), "weekly", 0.6m));
        return BuildXml(entries);
    }

    public DateTime? GetLastModifiedUtcForPath(string path) => freshness.GetLastModifiedUtc(NormalizePath(path));

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
                        entry.LastModifiedUtc is { } modified ? new XElement(SitemapNamespace + "lastmod", modified.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture)) : null,
                        new XElement(SitemapNamespace + "changefreq", entry.ChangeFrequency),
                        new XElement(SitemapNamespace + "priority", entry.Priority.ToString("0.0", CultureInfo.InvariantCulture))))));

        var builder = new StringBuilder();
        builder.AppendLine(doc.Declaration?.ToString() ?? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.Append(doc);
        return builder.ToString();
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return (normalized.Length > 1 ? normalized.TrimEnd('/') : normalized).ToLowerInvariant();
    }

    private static int RouteOrder(string path)
    {
        if (string.Equals(path, "/", StringComparison.Ordinal)) return 0;
        if (string.Equals(path, "/leaderboard", StringComparison.OrdinalIgnoreCase)) return 1;
        if (path.StartsWith("/league/", StringComparison.OrdinalIgnoreCase)) return 2;
        if (path.StartsWith("/flag/", StringComparison.OrdinalIgnoreCase)) return 3;
        if (path.StartsWith("/athlete/", StringComparison.OrdinalIgnoreCase)) return 4;
        if (path.StartsWith("/ai/", StringComparison.OrdinalIgnoreCase)) return 6;
        return 5;
    }
}

public sealed record SitemapRoute(
    string Path,
    string? RelativeFilePath,
    string ChangeFrequency,
    decimal Priority);

public sealed record SitemapUrlEntry(
    string Path,
    DateTime? LastModifiedUtc,
    string ChangeFrequency,
    decimal Priority);
