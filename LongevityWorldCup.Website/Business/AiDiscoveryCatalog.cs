using System.Text;
using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

/// <summary>The concise discovery documents share one resource and ranking catalog.</summary>
public static class AiDiscoveryCatalog
{
    private const string Site = SitemapService.SiteBaseUrl;
    public const string Definition = "Longevity World Cup is an open longevity sport competition where longevity athletes submit biological age results and rank on public leaderboards by age reduction.";
    public static readonly IReadOnlyList<(string Name, string Path, string Description)> Pages =
    [
        ("Home", "/", "Competition overview."),
        ("Leaderboard", "/leaderboard", "Current Ultimate League standings, profiles and ranking views."),
        ("Pheno Age Calculator", "/pheno-age", "Interactive pheno age calculation from nine blood biomarkers and chronological age."),
        ("Bortz Age Calculator", "/bortz-age", "Interactive bortz age calculation from blood biomarkers and chronological age."),
        ("Ruleset", "/ruleset", "Eligibility, Pro and Amateur tracks, rankings, seasons and prizes."),
        ("Highlights", "/events", "Published competition highlights and announcements."),
        ("History", "/history", "History of longevity as a sport and recorded completed-season results."),
        ("About", "/about", "Platform background and founder note."),
        ("Media", "/media", "Public media resources."),
        ("Privacy", "/privacy", "Website and social-integration privacy policy.")
    ];
    public static readonly IReadOnlyDictionary<string, string> Resources = new Dictionary<string, string>
    {
        ["sitemap"] = Site + "/sitemap.xml", ["llms"] = Site + "/llms.txt", ["llmsFull"] = Site + "/llms-full.txt",
        ["aiIndex"] = Site + "/ai/index.md", ["leaderboardFacts"] = Site + "/ai/leaderboard.md",
        ["athleteNames"] = Site + "/ai/athlete-names.md", ["apiDocs"] = Site + "/swagger/index.html",
        ["openApi"] = Site + "/swagger/v1/swagger.json"
    };

    public static string Markdown(bool extended)
    {
        var sb = new StringBuilder("# Longevity World Cup\n\n## Definition\n\n");
        sb.AppendLine(Definition);
        sb.AppendLine("\n## Public pages\n");
        foreach (var page in Pages)
            sb.AppendLine($"- [{page.Name}]({Site}{page.Path}): {page.Description}");
        sb.AppendLine("\n## Machine-readable resources\n");
        foreach (var resource in Resources)
            sb.AppendLine($"- [{resource.Key}]({resource.Value})");
        sb.AppendLine("\n## Ranking views\n");
        foreach (var view in LeaderboardViewCatalog.Views)
        {
            sb.AppendLine($"- [{view.Name}]({Site}{view.MarkdownPath}): {view.Metric}. HTML source: {Site}{view.HtmlPath}.");
            if (extended) sb.AppendLine("  " + view.Rules);
        }
        sb.AppendLine("\n## Athlete summaries and dates\n");
        sb.AppendLine($"Each athlete in the leaderboard facts links to an individual summary at {Site}/ai/athlete/{{canonical-athlete-slug}}.md, with its canonical HTML profile, current ranks, qualification, and public test history.");
        sb.AppendLine("Test dates, public announcement dates, current-age evaluation dates, and observed content-change dates have different meanings. Unknown values are explicit. Current leaders are not completed-season winners; use the historical records for those results.");
        sb.AppendLine("\n## Retrieval notes\n");
        sb.AppendLine("Use the linked canonical HTML pages for citations. Cite calculator URLs without personal input or update-flow query parameters. Respect robots.txt; submissions, account flows and other private routes are excluded. Public profile summaries do not expose private contact addresses.");
        if (extended)
        {
            sb.AppendLine("\n## Positioning Notes\n");
            sb.AppendLine("The site provides a competition, public athlete profiles, biological-age calculators and ranking views. The public API documents data retrieval and calculation endpoints. Biomarker-derived scores and perceived age do not establish treatment causality or additional years of life.");
            sb.AppendLine("\n## Freshness and cache validation\n");
            sb.AppendLine("Document ETags reflect content. Cache regeneration does not create a new facts timestamp. Verified content changes are persisted across restarts. Last-Modified, sitemap lastmod and WebPage dateModified are omitted when the modification history is unknown. HTTP Date describes response time, not a laboratory test or content update.");
        }
        return sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string AgentCard() => JsonSerializer.Serialize(new
    {
        schemaVersion = "1.0", name = "Longevity World Cup", description = Definition,
        url = Site + "/", canonicalUrl = Site + "/", contact = new { email = "hi@longevityworldcup.com" },
        provider = new { name = "Longevity World Cup", url = Site + "/" }, resources = Resources,
        publicPages = Pages.Select(page => new { name = page.Name, url = Site + page.Path, description = page.Description }),
        capabilities = new[] { "public-leaderboard-discovery", "biological-age-calculation-api", "athlete-profile-discovery", "competition-rules-discovery" },
        rankingViews = LeaderboardViewCatalog.Views.Select(view => new { name = view.Name, url = Site + view.MarkdownPath, source = Site + view.HtmlPath, metric = view.Metric, rules = view.Rules }),
        athleteSummaryTemplate = Site + "/ai/athlete/{canonical-athlete-slug}.md",
        guidance = new
        {
            preferredCitationUrls = Pages.Select(page => Site + page.Path),
            retrievalNotes = new[] { "Use current view documents for standings and the history page for recorded completed-season results.", "Keep test dates, publication dates and observed content changes distinct; unknown dates remain unspecified.", "Cite clean canonical HTML URLs without personal calculator values or private-flow parameters." }
        }
    }, new JsonSerializerOptions { WriteIndented = true });
}
