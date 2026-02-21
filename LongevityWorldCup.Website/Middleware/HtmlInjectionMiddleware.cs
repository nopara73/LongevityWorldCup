namespace LongevityWorldCup.Website.Middleware
{
    public class HtmlInjectionMiddleware(RequestDelegate next)
    {
        private readonly RequestDelegate _next = next;
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
            if (path == "/" || path?.EndsWith(".html") is true)
            {
                string filePath;

                if (path == "/")
                {
                    filePath = Path.Combine("wwwroot", "index.html");
                }
                else
                {
                    filePath = Path.Combine("wwwroot", path.TrimStart('/'));
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
                    var seo = GetSeoMeta(path ?? "/");

                    head = head
                        .Replace("{{SEO_DESCRIPTION}}", EncodeMeta(seo.Description))
                        .Replace("{{SEO_ROBOTS}}", EncodeMeta(seo.Robots))
                        .Replace("{{SEO_CANONICAL_URL}}", EncodeMeta(seo.CanonicalUrl))
                        .Replace("{{SEO_OG_TITLE}}", EncodeMeta(seo.OgTitle))
                        .Replace("{{SEO_OG_DESCRIPTION}}", EncodeMeta(seo.OgDescription))
                        .Replace("{{SEO_OG_URL}}", EncodeMeta(seo.CanonicalUrl))
                        .Replace("{{SEO_OG_IMAGE}}", EncodeMeta(DefaultOgImage));

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

        private static SeoMeta GetSeoMeta(string requestPath)
        {
            var canonicalPath = RouteCanonicalization.GetCanonicalPath(requestPath);
            var canonicalUrl = $"{SiteBaseUrl}{canonicalPath}";

            return canonicalPath switch
            {
                "/" => new SeoMeta(
                    "Reverse your biological age and climb the Longevity World Cup leaderboard. Compare Pheno Age and Bortz Age results in a global anti-aging competition.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup | Reverse Your Biological Age",
                    "Longevity World Cup | Reverse Your Biological Age",
                    "Too old for your sport? Not this one. Join the Longevity World Cup and rise on the leaderboard by improving your biological age."
                ),
                "/leaderboard" => new SeoMeta(
                    "See the latest Longevity World Cup leaderboard rankings and compare biological age reduction results across athletes.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup Leaderboard",
                    "Longevity World Cup Leaderboard",
                    "Explore current Longevity World Cup standings and discover who is leading the biological age reversal rankings."
                ),
                "/events" => new SeoMeta(
                    "Track Longevity World Cup highlights, announcements, and major milestones from the current season.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup Highlights",
                    "Longevity World Cup Highlights",
                    "Follow key Longevity World Cup events, season updates, and competition highlights."
                ),
                "/media" => new SeoMeta(
                    "Download official Longevity World Cup media assets, logos, and press materials.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup Media Kit",
                    "Longevity World Cup Media Kit",
                    "Access the Longevity World Cup media kit with press-ready branding assets and resources."
                ),
                _ when !IndexableRoutes.Contains(canonicalPath) => new SeoMeta(
                    "Longevity World Cup member page.",
                    "noindex, nofollow",
                    canonicalUrl,
                    "Longevity World Cup",
                    "Longevity World Cup",
                    "Longevity World Cup member page."
                ),
                _ => new SeoMeta(
                    "Longevity World Cup - reverse biological age and compete globally.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup",
                    "Longevity World Cup",
                    "Longevity World Cup - reverse biological age and compete globally."
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

        private sealed record SeoMeta(
            string Description,
            string Robots,
            string CanonicalUrl,
            string PageTitle,
            string OgTitle,
            string OgDescription
        );
    }
}
