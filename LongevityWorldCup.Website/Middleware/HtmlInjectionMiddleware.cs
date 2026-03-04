using System.Text.Json;
using System.Globalization;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Middleware
{
    public class HtmlInjectionMiddleware(RequestDelegate next, AthleteOgImageService athleteOgImages, LeagueOgImageService leagueOgImages)
    {
        private readonly RequestDelegate _next = next;
        private readonly AthleteOgImageService _athleteOgImages = athleteOgImages;
        private readonly LeagueOgImageService _leagueOgImages = leagueOgImages;
        private const string SiteBaseUrl = "https://longevityworldcup.com";
        private const string DefaultOgImage = "https://longevityworldcup.com/assets/og-image.png";
        private static readonly HashSet<string> IndexableRoutes = new(StringComparer.OrdinalIgnoreCase)
        {
            "/",
            "/leaderboard",
            "/events",
            "/media"
        };

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value;
            if (path == "/" || path?.EndsWith(".html") is true || IsLeagueRoute(path))
            {
                string filePath;

                if (path == "/" || IsLeagueRoute(path))
                {
                    filePath = Path.Combine("wwwroot", "index.html");
                }
                else
                {
                    filePath = Path.Combine("wwwroot", (path ?? "").TrimStart('/'));
                }

                if (File.Exists(filePath))
                {
                    // Read the main HTML file
                    var bodyContent = await File.ReadAllTextAsync(filePath);

                    // Read the header and footer files
                    var head = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "head.html"));
                    var header = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "header.html"));
                    var footer = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "footer.html"));
                    var progressBar = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "main-progress-bar.html"));
                    var subProgressBar = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "sub-progress-bar.html"));
                    var leaderboardContent = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "leaderboard-content.html"));
                    var guessMyAge = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "guess-my-age.html"));
                    var eventBoardContent = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "event-board-content.html"));
                    var ageVisualization = await File.ReadAllTextAsync(Path.Combine("wwwroot", "partials", "age-visualization.html"));
                    var seo = GetSeoMeta(context);

                    head = head
                        .Replace("{{SEO_DESCRIPTION}}", EncodeMeta(seo.Description))
                        .Replace("{{SEO_ROBOTS}}", EncodeMeta(seo.Robots))
                        .Replace("{{SEO_CANONICAL_URL}}", EncodeMeta(seo.CanonicalUrl))
                        .Replace("{{SEO_OG_TITLE}}", EncodeMeta(seo.OgTitle))
                        .Replace("{{SEO_OG_DESCRIPTION}}", EncodeMeta(seo.OgDescription))
                        .Replace("{{SEO_OG_URL}}", EncodeMeta(seo.CanonicalUrl))
                        .Replace("{{SEO_OG_IMAGE}}", EncodeMeta(seo.OgImageUrl))
                        .Replace("{{SEO_STRUCTURED_DATA}}", BuildStructuredDataJson(seo));

                    // Replace placeholders within leaderboardContent first (since it contains nested placeholders)
                    leaderboardContent = leaderboardContent.Replace("<!--AGE-VISUALIZATION-->", ageVisualization);

                    // Replace placeholders with header and footer content
                    bodyContent = bodyContent
                        .Replace("<!--HEAD-->", head)
                        .Replace("<!--HEADER-->", header)
                        .Replace("<!--FOOTER-->", footer)
                        .Replace("<!--MAIN-PROGRESS-BAR-->", progressBar)
                        .Replace("<!--SUB-PROGRESS-BAR-->", subProgressBar)
                        .Replace("<!--LEADERBOARD-CONTENT-->", leaderboardContent)
                        .Replace("<!--GUESS-MY-AGE-->", guessMyAge)
                        .Replace("<!--EVENT-BOARD-CONTENT-->", eventBoardContent)
                        .Replace("<!--AGE-VISUALIZATION-->", ageVisualization);
                    bodyContent = ReplacePageTitle(bodyContent, seo.PageTitle);

                    // Optionally remove the play button on certain pages
                    if (path?.Contains("join-game", StringComparison.OrdinalIgnoreCase) is true)
                    {
                        bodyContent = bodyContent.Replace("<button class=\"join-game\">", "<!-- Removed Join Game Button -->");
                    }

                    // Write the modified content to the response
                    context.Response.ContentType = "text/html";
                    if (seo.Robots.StartsWith("noindex", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.Headers["X-Robots-Tag"] = seo.Robots;
                    }
                    await context.Response.WriteAsync(bodyContent);

                    // Short-circuit the pipeline
                    return;
                }
            }

            // For all other requests, continue down the pipeline
            await _next(context);
        }

        private SeoMeta GetSeoMeta(HttpContext context)
        {
            var requestPath = context.Request.Path.Value ?? "/";
            var baseSeo = GetBaseSeoMeta(requestPath);

            if (TryGetAthleteSeoMeta(context, baseSeo, out var athleteSeo))
            {
                return athleteSeo;
            }

            if (TryGetLeagueSeoMeta(context, out var leagueSeo))
            {
                return leagueSeo;
            }

            return baseSeo;
        }

        private static SeoMeta GetBaseSeoMeta(string requestPath)
        {
            var canonicalPath = RouteCanonicalization.GetCanonicalPath(requestPath);
            var canonicalUrl = $"{SiteBaseUrl}{canonicalPath}";

            return canonicalPath switch
            {
                "/" => new SeoMeta(
                    canonicalPath,
                    "Reverse your biological age and climb the Longevity World Cup leaderboard. Compare Pheno Age and Bortz Age results in a global anti-aging competition.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup | Reverse Your Biological Age",
                    "Longevity World Cup | Reverse Your Biological Age",
                    "Too old for your sport? Not this one. Join the Longevity World Cup and rise on the leaderboard by improving your biological age.",
                    DefaultOgImage
                ),
                "/leaderboard" => new SeoMeta(
                    canonicalPath,
                    "See the latest Longevity World Cup leaderboard rankings and compare biological age reduction results across athletes.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup Leaderboard",
                    "Longevity World Cup Leaderboard",
                    "Explore current Longevity World Cup standings and discover who is leading the biological age reversal rankings.",
                    DefaultOgImage
                ),
                "/events" => new SeoMeta(
                    canonicalPath,
                    "Track Longevity World Cup highlights, announcements, and major milestones from the current season.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup Highlights",
                    "Longevity World Cup Highlights",
                    "Follow key Longevity World Cup events, season updates, and competition highlights.",
                    DefaultOgImage
                ),
                "/media" => new SeoMeta(
                    canonicalPath,
                    "Download official Longevity World Cup media assets, logos, and press materials.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup Media Kit",
                    "Longevity World Cup Media Kit",
                    "Access the Longevity World Cup media kit with press-ready branding assets and resources.",
                    DefaultOgImage
                ),
                _ when !IndexableRoutes.Contains(canonicalPath) => new SeoMeta(
                    canonicalPath,
                    "Longevity World Cup member page.",
                    "noindex, nofollow",
                    canonicalUrl,
                    "Longevity World Cup",
                    "Longevity World Cup",
                    "Longevity World Cup member page.",
                    DefaultOgImage
                ),
                _ => new SeoMeta(
                    canonicalPath,
                    "Longevity World Cup - reverse biological age and compete globally.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup",
                    "Longevity World Cup",
                    "Longevity World Cup - reverse biological age and compete globally.",
                    DefaultOgImage
                )
            };
        }

        private static string ReplacePageTitle(string html, string title)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return html;
            }

            const string titleOpen = "<title>";
            const string titleClose = "</title>";
            var replacement = $"<title>{EncodeMeta(title)}</title>";

            var start = html.IndexOf(titleOpen, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                var headClose = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
                if (headClose >= 0)
                {
                    return html.Insert(headClose, $"    {replacement}{Environment.NewLine}");
                }

                return html;
            }

            var end = html.IndexOf(titleClose, start, StringComparison.OrdinalIgnoreCase);
            if (end < 0)
            {
                return html;
            }

            end += titleClose.Length;
            return html.Remove(start, end - start).Insert(start, replacement);
        }

        private static string EncodeMeta(string value)
        {
            return System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
        }

        private bool TryGetAthleteSeoMeta(HttpContext context, SeoMeta baseSeo, out SeoMeta seo)
        {
            seo = baseSeo;
            if (!string.Equals(baseSeo.CanonicalPath, "/", StringComparison.Ordinal) ||
                !context.Request.Query.TryGetValue("athlete", out var athleteQuery))
            {
                return false;
            }

            var rawSlug = athleteQuery.ToString();
            var rawContext = context.Request.Query.TryGetValue("ctx", out var ctxQuery)
                ? ctxQuery.ToString()
                : null;
            if (!_athleteOgImages.IsConfigured || !_athleteOgImages.TryGetCurrentPayload(rawSlug, rawContext, out var payload))
            {
                return false;
            }

            var canonicalPath = $"/athlete/{payload.RouteSlug}";
            var canonicalUrl = $"{SiteBaseUrl}{canonicalPath}";
            var signedReduction = payload.AgeReduction.ToString("+#0.0;-#0.0;0.0", CultureInfo.InvariantCulture);
            var title = $"{payload.Name} | #{payload.Rank} {payload.LeagueName}";
            var description = $"{payload.Name} is ranked #{payload.Rank} in the {payload.LeagueName} with {signedReduction} years age reduction.";
            var ogImageUrl = _athleteOgImages.BuildVersionedImageUrl(SiteBaseUrl, payload);

            seo = new SeoMeta(
                canonicalPath,
                description,
                "index, follow",
                canonicalUrl,
                title,
                title,
                description,
                ogImageUrl
            );

            return true;
        }

        private bool TryGetLeagueSeoMeta(HttpContext context, out SeoMeta seo)
        {
            seo = null!;
            if (!TryResolveLeagueSlug(context, out var leagueSlug))
            {
                return false;
            }

            if (!_leagueOgImages.TryGetCurrentPayload(leagueSlug, out var payload))
            {
                return false;
            }

            var canonicalPath = $"/league/{payload.RouteSlug}";
            var canonicalUrl = $"{SiteBaseUrl}{canonicalPath}";
            var title = $"{payload.DisplayName} | Longevity World Cup";
            var top3Text = payload.Top3Names.Count > 0
                ? $" Current top athletes: {string.Join(", ", payload.Top3Names.Take(3))}."
                : "";
            var description = $"Track rankings in the {payload.DisplayName}.{top3Text}";
            var ogImageUrl = _leagueOgImages.IsConfigured
                ? _leagueOgImages.BuildVersionedImageUrl(SiteBaseUrl, payload)
                : DefaultOgImage;

            seo = new SeoMeta(
                canonicalPath,
                description,
                "index, follow",
                canonicalUrl,
                title,
                title,
                description,
                ogImageUrl
            );
            return true;
        }

        private static bool IsLeagueRoute(string? path)
        {
            return !string.IsNullOrWhiteSpace(path)
                   && path.StartsWith("/league/", StringComparison.OrdinalIgnoreCase)
                   && path.Length > "/league/".Length;
        }

        private static bool TryResolveLeagueSlug(HttpContext context, out string leagueSlug)
        {
            leagueSlug = "";
            var requestPath = context.Request.Path.Value ?? "";

            if (IsLeagueRoute(requestPath))
            {
                var raw = requestPath["/league/".Length..].Trim('/');
                return LeagueOgImageService.TryNormalizeLeagueSlug(raw, out leagueSlug);
            }

            if (!string.Equals(requestPath, "/", StringComparison.Ordinal) &&
                !string.Equals(requestPath, "/leaderboard", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (context.Request.Query.TryGetValue("filters", out var filters))
            {
                var first = filters.ToString()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first) &&
                    LeagueOgImageService.TryNormalizeLeagueSlug(first, out leagueSlug))
                {
                    return true;
                }
            }

            if (string.Equals(requestPath, "/leaderboard", StringComparison.OrdinalIgnoreCase))
            {
                return LeagueOgImageService.TryNormalizeLeagueSlug("ultimate", out leagueSlug);
            }

            return false;
        }

        private static string BuildStructuredDataJson(SeoMeta seo)
        {
            var breadcrumbItems = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["@type"] = "ListItem",
                    ["position"] = 1,
                    ["name"] = "Home",
                    ["item"] = SiteBaseUrl
                }
            };

            if (!string.Equals(seo.CanonicalPath, "/", StringComparison.Ordinal))
            {
                breadcrumbItems.Add(new Dictionary<string, object>
                {
                    ["@type"] = "ListItem",
                    ["position"] = 2,
                    ["name"] = GetBreadcrumbLabel(seo.CanonicalPath),
                    ["item"] = seo.CanonicalUrl
                });
            }

            var payload = new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org",
                ["@graph"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["@type"] = "Organization",
                        ["@id"] = $"{SiteBaseUrl}/#organization",
                        ["name"] = "Longevity World Cup",
                        ["url"] = SiteBaseUrl,
                        ["logo"] = DefaultOgImage
                    },
                    new Dictionary<string, object>
                    {
                        ["@type"] = "WebSite",
                        ["@id"] = $"{SiteBaseUrl}/#website",
                        ["url"] = SiteBaseUrl,
                        ["name"] = "Longevity World Cup",
                        ["publisher"] = new Dictionary<string, object>
                        {
                            ["@id"] = $"{SiteBaseUrl}/#organization"
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["@type"] = "WebPage",
                        ["@id"] = $"{seo.CanonicalUrl}#webpage",
                        ["url"] = seo.CanonicalUrl,
                        ["name"] = seo.PageTitle,
                        ["description"] = seo.Description,
                        ["isPartOf"] = new Dictionary<string, object>
                        {
                            ["@id"] = $"{SiteBaseUrl}/#website"
                        },
                        ["about"] = new Dictionary<string, object>
                        {
                            ["@id"] = $"{SiteBaseUrl}/#organization"
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["@type"] = "BreadcrumbList",
                        ["itemListElement"] = breadcrumbItems
                    }
                }
            };

            return JsonSerializer.Serialize(payload);
        }

        private static string GetBreadcrumbLabel(string canonicalPath)
        {
            return canonicalPath switch
            {
                "/leaderboard" => "Leaderboard",
                "/events" => "Events",
                "/media" => "Media",
                _ => "Page"
            };
        }

        private sealed record SeoMeta(
            string CanonicalPath,
            string Description,
            string Robots,
            string CanonicalUrl,
            string PageTitle,
            string OgTitle,
            string OgDescription,
            string OgImageUrl
        );
    }
}
