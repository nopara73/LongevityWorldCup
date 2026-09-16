using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Middleware;

/// <summary>Resolves public documents before rendering, independently of preview-image availability.</summary>
public sealed class PublicRouteMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> DiscoveryPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/robots.txt", "/sitemap.xml", "/llms.txt", "/llms-full.txt",
        "/.well-known/agent-card.json", "/ai/index.md", "/ai/leaderboard.md", "/ai/athlete-names.md"
    };

    public async Task Invoke(HttpContext context, AthleteDataService athletes)
    {
        var path = context.Request.Path.Value ?? "/";
        var trimmedPath = path.TrimEnd('/');
        string? canonicalPath = null;
        var query = context.Request.QueryString;

        if (context.Request.Path.StartsWithSegments("/athlete", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/league", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/flag", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmedPath.Split('/');
            var slug = parts.Length == 3 ? Uri.UnescapeDataString(parts[2]) : string.Empty;
            // An encoded delimiter is part of the path, never a query or a second route segment.
            if (string.IsNullOrWhiteSpace(slug) || slug.IndexOfAny(['/', '\\', '?', '#', '%']) >= 0)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            switch (parts[1].ToLowerInvariant())
            {
                case "athlete":
                    var athleteSlug = AthleteSlug.Normalize(slug);
                    if (athletes.GetActiveAthleteSlugs().Contains(athleteSlug))
                        canonicalPath = "/athlete/" + athleteSlug.Replace('_', '-');
                    break;
                case "flag":
                    var flags = athletes.GetAthletesSnapshot().OfType<JsonObject>()
                        .Select(athlete => athlete["Flag"]?.GetValue<string>());
                    if (FlagRouteCatalog.TryResolve(slug, flags, out var flag))
                        canonicalPath = flag.Path;
                    break;
                case "league":
                    var leagueSlug = slug.Trim().ToLowerInvariant();
                    if (leagueSlug == "professional")
                    {
                        canonicalPath = "/leaderboard";
                        if (!context.Request.Query.ContainsKey("filters"))
                            query = query.Add("filters", "professional");
                    }
                    else if (leagueSlug == "pheno-improvement")
                        canonicalPath = "/league/improvement";
                    else if (SitemapService.PublicLeaguePaths.Contains("/league/" + leagueSlug))
                        canonicalPath = "/league/" + leagueSlug;
                    else if (LeagueOgImageService.TryNormalizeLeagueSlug(slug, out var normalizedLeague))
                        canonicalPath = normalizedLeague == "ultimate" ? "/leaderboard" : "/league/" + normalizedLeague;
                    break;
            }

            if (canonicalPath is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }
        else if (trimmedPath.Equals("/swagger", StringComparison.OrdinalIgnoreCase)
            || trimmedPath.Equals("/swagger/index.html", StringComparison.OrdinalIgnoreCase))
            canonicalPath = "/swagger/index.html";
        else if (trimmedPath.Equals("/ai/athletes.md", StringComparison.OrdinalIgnoreCase))
            canonicalPath = "/ai/leaderboard.md";
        else if (DiscoveryPaths.Contains(trimmedPath))
            canonicalPath = trimmedPath.ToLowerInvariant();

        if (canonicalPath is not null && RouteCanonicalization.RedirectToCanonical(context, canonicalPath, query))
            return;

        await next(context);
    }
}
