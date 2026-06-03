using System.Text.Json;
using System.Globalization;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using System.Text;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Middleware
{
    public class HtmlInjectionMiddleware(RequestDelegate next, AthleteOgImageService athleteOgImages, LeagueOgImageService leagueOgImages, PageOgImageService pageOgImages, AssetVersionProvider assetVersionProvider, LeaderboardFactsService leaderboardFacts, SitemapService sitemap, Config config, ILogger<HtmlInjectionMiddleware> logger, IWebHostEnvironment environment)
    {
        private readonly RequestDelegate _next = next;
        private readonly AthleteOgImageService _athleteOgImages = athleteOgImages;
        private readonly LeagueOgImageService _leagueOgImages = leagueOgImages;
        private readonly PageOgImageService _pageOgImages = pageOgImages;
        private readonly AssetVersionProvider _assetVersionProvider = assetVersionProvider;
        private readonly LeaderboardFactsService _leaderboardFacts = leaderboardFacts;
        private readonly SitemapService _sitemap = sitemap;
        private readonly Config _config = config;
        private readonly ILogger<HtmlInjectionMiddleware> _logger = logger;
        private readonly string _webRootPath = ResolveWebRootPath(environment);
        private const string SiteBaseUrl = "https://longevityworldcup.com";
        private const string DefaultOgImagePath = "/assets/og-image.png";
        private const string LongevitymaxxingOgImagePath = "/assets/longevitymaxxing-og.png";
        private const string LeaderboardRowsStartMarker = "<!--LEADERBOARD-TBODY-ROWS-START-->";
        private const string LeaderboardRowsEndMarker = "<!--LEADERBOARD-TBODY-ROWS-END-->";
        private const string LongevitymaxxingPromoStartMarker = "<!--LONGEVITYMAXXING-PROMO-START-->";
        private const string LongevitymaxxingPromoEndMarker = "<!--LONGEVITYMAXXING-PROMO-END-->";
        private const string LeaderboardSkeletonTbodyOpenTag = "<tbody class=\"loading-skeleton\" aria-busy=\"true\">";
        private static readonly HashSet<string> IndexableRoutes = new(StringComparer.OrdinalIgnoreCase)
        {
            "/",
            "/leaderboard",
            "/longevitymaxxing",
            "/events",
            "/media",
            "/about",
            "/history",
            "/ruleset"
        };
        private static readonly IReadOnlyDictionary<string, LeaderboardViewSeo> LeaderboardViewSeoBySlug =
            new Dictionary<string, LeaderboardViewSeo>(StringComparer.OrdinalIgnoreCase)
            {
                ["bortz"] = new(
                    "Bortz Age Leaderboard | Longevity World Cup",
                    "Track Longevity World Cup bortz age rankings for athletes with eligible bortz age results."),
                ["pheno"] = new(
                    "Pheno Age Leaderboard | Longevity World Cup",
                    "Track Longevity World Cup pheno age rankings from verified biological age submissions."),
                ["crowd"] = new(
                    "Crowd Age Leaderboard | Longevity World Cup",
                    "Track the Longevity World Cup crowd age leaderboard for athletes with enough accepted age guesses.")
            };

        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path.Value;
            if (path == "/" || path?.EndsWith(".html") is true || IsLeagueRoute(path) || IsAthleteRoute(path))
            {
                string filePath;

                if (path == "/" || IsLeagueRoute(path) || IsAthleteRoute(path))
                {
                    filePath = Path.Combine(_webRootPath, "index.html");
                }
                else
                {
                    filePath = Path.Combine(_webRootPath, (path ?? "").TrimStart('/'));
                }

                if (File.Exists(filePath))
                {
                    // Read the main HTML file
                    var bodyContent = await File.ReadAllTextAsync(filePath);

                    // Read the header and footer files
                    var head = await File.ReadAllTextAsync(Path.Combine(_webRootPath, "partials", "head.html"));
                    var header = await File.ReadAllTextAsync(Path.Combine(_webRootPath, "partials", "header.html"));
                    var footer = await File.ReadAllTextAsync(Path.Combine(_webRootPath, "partials", "footer.html"));
                    var progressBar = await File.ReadAllTextAsync(Path.Combine(_webRootPath, "partials", "main-progress-bar.html"));
                    var subProgressBar = await File.ReadAllTextAsync(Path.Combine(_webRootPath, "partials", "sub-progress-bar.html"));
                    var leaderboardContent = await File.ReadAllTextAsync(Path.Combine(_webRootPath, "partials", "leaderboard-content.html"));
                    var guessMyAge = await File.ReadAllTextAsync(Path.Combine(_webRootPath, "partials", "guess-my-age.html"));
                    var eventBoardContent = await File.ReadAllTextAsync(Path.Combine(_webRootPath, "partials", "event-board-content.html"));
                    var ageVisualization = await File.ReadAllTextAsync(Path.Combine(_webRootPath, "partials", "age-visualization.html"));
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
                    head = ApplyHeadAssets(head, path ?? string.Empty);

                    // Replace placeholders within leaderboardContent first (since it contains nested placeholders)
                    leaderboardContent = leaderboardContent.Replace("<!--AGE-VISUALIZATION-->", ageVisualization);
                    leaderboardContent = ApplyLeaderboardRows(leaderboardContent, context);
                    leaderboardContent = ApplyLongevitymaxxingPromo(leaderboardContent);

                    // Replace placeholders with header and footer content
                    bodyContent = bodyContent
                        .Replace("<!--HEAD-->", head)
                        .Replace("<!--HEADER-->", ApplySharedAssetPlaceholders(header))
                        .Replace("<!--FOOTER-->", footer)
                        .Replace("<!--MAIN-PROGRESS-BAR-->", progressBar)
                        .Replace("<!--SUB-PROGRESS-BAR-->", subProgressBar)
                        .Replace("<!--LEADERBOARD-CONTENT-->", leaderboardContent)
                        .Replace("<!--GUESS-MY-AGE-->", guessMyAge)
                        .Replace("<!--EVENT-BOARD-CONTENT-->", eventBoardContent)
                        .Replace("<!--AGE-VISUALIZATION-->", ageVisualization)
                        .Replace("{{ASSET_BIOAGEFORM_CSS}}", _assetVersionProvider.AppendVersion("/css/bioageform.css"))
                        .Replace("{{ASSET_CUSTOM_EVENT_MARKUP_JS}}", _assetVersionProvider.AppendVersion("/js/custom-event-markup.js"))
                        .Replace("{{ASSET_LONGEVITYMAXXING_CSS}}", _assetVersionProvider.AppendVersion("/css/longevitymaxxing.css"))
                        .Replace("{{ASSET_LONGEVITYMAXXING_JS}}", _assetVersionProvider.AppendVersion("/js/longevitymaxxing.js"))
                        .Replace("{{ASSET_CUSTOM_EVENT_IMAGE}}", _assetVersionProvider.AppendVersion("/assets/custom_event.png"))
                        .Replace("{{ASSET_POPPINS_REGULAR}}", _assetVersionProvider.AppendVersion("/assets/fonts/Poppins-Regular.ttf"))
                        .Replace("{{ASSET_POPPINS_BOLD}}", _assetVersionProvider.AppendVersion("/assets/fonts/Poppins-Bold.ttf"));
                    bodyContent = ApplySharedAssetPlaceholders(bodyContent);
                    bodyContent = ReplacePageTitle(bodyContent, seo.PageTitle);

                    if (ShouldRemoveJoinGameButtons(path))
                    {
                        bodyContent = RemoveJoinGameButtons(bodyContent);
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

        private string ApplyHeadAssets(string html, string path)
        {
            var config = GetHeadAssetConfig(path);
            var optionalHeadScripts = BuildOptionalHeadScripts(config);
            var modulesBootstrap = BuildModulesBootstrap(config);

            return ApplySharedCssPlaceholders(ApplySharedAssetPlaceholders(html))
                .Replace("{{OPTIONAL_HEAD_SCRIPTS}}", optionalHeadScripts)
                .Replace("{{MODULES_BOOTSTRAP}}", modulesBootstrap)
                .Replace("{{ASSET_FAVICON_ICO}}", _assetVersionProvider.AppendVersion("/assets/favicon.ico"))
                .Replace("{{ASSET_FAVICON_DARK_ICO}}", _assetVersionProvider.AppendVersion("/assets/favicon-dark.ico"))
                .Replace("{{ASSET_FAVICON_192}}", _assetVersionProvider.AppendVersion("/assets/favicon-192x192.png"))
                .Replace("{{ASSET_FAVICON_DARK_192}}", _assetVersionProvider.AppendVersion("/assets/favicon-dark-192x192.png"))
                .Replace("{{ASSET_APPLE_TOUCH_ICON}}", _assetVersionProvider.AppendVersion("/assets/apple-touch-icon.png"))
                .Replace("{{ASSET_APPLE_TOUCH_ICON_DARK}}", _assetVersionProvider.AppendVersion("/assets/apple-touch-icon-dark.png"))
                .Replace("{{ASSET_SITE_WEBMANIFEST}}", _assetVersionProvider.AppendVersion("/assets/site.webmanifest"))
                .Replace("{{ASSET_SITE_DARK_WEBMANIFEST}}", _assetVersionProvider.AppendVersion("/assets/site-dark.webmanifest"))
                .Replace("{{ASSET_BADGES_CSS}}", _assetVersionProvider.AppendVersion("/css/badges.css"))
                .Replace("{{ASSET_FLAG_ICONS_CSS}}", _assetVersionProvider.AppendVersion("/vendor/flag-icons/css/flag-icons.min.css"))
                .Replace("{{ASSET_MISC_JS}}", _assetVersionProvider.AppendVersion("/js/misc.js"))
                .Replace("{{ASSET_LEAGUE_ICONS_JS}}", _assetVersionProvider.AppendVersion("/js/leagueIcons.js"))
                .Replace("{{ASSET_PHENO_AGE_JS}}", _assetVersionProvider.AppendVersion("/js/pheno-age.js"))
                .Replace("{{ASSET_BORTZ_AGE_JS}}", _assetVersionProvider.AppendVersion("/js/bortz-age.js"))
                .Replace("{{ASSET_BADGES_JS}}", _assetVersionProvider.AppendVersion("/js/badges.js"))
                .Replace("{{ASSET_PRO_DISCOUNTS_JS}}", _assetVersionProvider.AppendVersion("/js/pro-discounts.js"))
                .Replace("{{ASSET_PROOF_HELPERS_JS}}", _assetVersionProvider.AppendVersion("/js/proof-helpers.js"))
                .Replace("{{ASSET_AGE_VISUALIZATION_JS}}", _assetVersionProvider.AppendVersion("/js/age-visualization.js"));
        }

        private string ApplyLeaderboardRows(string html, HttpContext context)
        {
            if (!ShouldRenderLeaderboardRows(context))
            {
                return html;
            }

            try
            {
                var rowsHtml = LeaderboardHtmlRenderer.RenderRows(_leaderboardFacts.GetLeaderboardSnapshot());
                return string.IsNullOrWhiteSpace(rowsHtml)
                    ? html
                    : ReplaceLeaderboardRows(html, rowsHtml);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falling back to leaderboard skeleton rows after server row rendering failed.");
                return html;
            }
        }

        private static string ReplaceLeaderboardRows(string html, string rowsHtml)
        {
            var start = html.IndexOf(LeaderboardRowsStartMarker, StringComparison.Ordinal);
            var end = html.IndexOf(LeaderboardRowsEndMarker, StringComparison.Ordinal);
            if (start < 0 || end < 0 || end <= start)
            {
                return html;
            }

            var rowsStart = start + LeaderboardRowsStartMarker.Length;
            var replacedRows = html
                .Remove(rowsStart, end - rowsStart)
                .Insert(rowsStart, $"{Environment.NewLine}{rowsHtml}                ");

            return replacedRows.Replace(
                LeaderboardSkeletonTbodyOpenTag,
                $"<tbody {LeaderboardHtmlRenderer.ServerRenderedTbodyAttributes}>",
                StringComparison.Ordinal);
        }

        private static string RemoveMarkedSection(string html, string startMarker, string endMarker)
        {
            var start = html.IndexOf(startMarker, StringComparison.Ordinal);
            var end = html.IndexOf(endMarker, StringComparison.Ordinal);
            if (start < 0 || end < 0 || end <= start)
            {
                return html;
            }

            var sectionEnd = end + endMarker.Length;
            while (sectionEnd < html.Length && (html[sectionEnd] == '\r' || html[sectionEnd] == '\n'))
            {
                sectionEnd++;
            }

            return html.Remove(start, sectionEnd - start);
        }

        private static bool ShouldRenderLeaderboardRows(HttpContext context)
        {
            var canonicalPath = RouteCanonicalization.GetCanonicalPath(context.Request.Path.Value);
            return string.Equals(canonicalPath, "/leaderboard", StringComparison.OrdinalIgnoreCase) &&
                   !context.Request.QueryString.HasValue;
        }

        private static string ResolveWebRootPath(IWebHostEnvironment environment)
        {
            if (!string.IsNullOrWhiteSpace(environment.WebRootPath) && Directory.Exists(environment.WebRootPath))
                return environment.WebRootPath;

            return Path.Combine(environment.ContentRootPath, "wwwroot");
        }

        private string ApplySharedAssetPlaceholders(string html)
        {
            return html
                .Replace("{{ASSET_FAVICON_128}}", _assetVersionProvider.AppendVersion("/assets/favicon-128x128.png"))
                .Replace("{{ASSET_ROBOTO_LIGHT}}", _assetVersionProvider.AppendVersion("/assets/fonts/Roboto-Light.woff2"))
                .Replace("{{ASSET_ROBOTO_REGULAR}}", _assetVersionProvider.AppendVersion("/assets/fonts/Roboto-Regular.woff2"))
                .Replace("{{ASSET_ROBOTO_BOLD}}", _assetVersionProvider.AppendVersion("/assets/fonts/Roboto-Bold.woff2"))
                .Replace("{{ASSET_ORBITRON_BOLD}}", _assetVersionProvider.AppendVersion("/assets/fonts/Orbitron-Bold.woff2"))
                .Replace("{{ASSET_MERCH_MUG}}", _assetVersionProvider.AppendVersion("/assets/content-images/merch/mug.webp"))
                .Replace("{{ASSET_MERCH_HOODIE}}", _assetVersionProvider.AppendVersion("/assets/content-images/merch/hoodie.webp"))
                .Replace("{{ASSET_MERCH_CAP}}", _assetVersionProvider.AppendVersion("/assets/content-images/merch/cap.webp"))
                .Replace("{{ASSET_DONATION_QR}}", _assetVersionProvider.AppendVersion("/assets/Donation25QR.png"))
                .Replace("{{ASSET_HD_LOGO_THUMB_SM}}", _assetVersionProvider.AppendVersion("/assets/HdLogo_thumb_sm.png"))
                .Replace("{{ASSET_TROLLFACE}}", _assetVersionProvider.AppendVersion("/assets/content-images/trollface.png"));
        }

        private string ApplySharedCssPlaceholders(string html)
        {
            return html
                .Replace("{{ASSET_MOBILE_ROUGHNESS_CSS}}", _assetVersionProvider.AppendVersion("/css/mobile-roughness.css"));
        }

        private string BuildOptionalHeadScripts(HeadAssetConfig config)
        {
            var sb = new StringBuilder();

            if (config.IncludeValidator)
            {
                sb.AppendLine("<script src=\"https://cdn.jsdelivr.net/npm/validator@13.9.0/validator.min.js\" crossorigin=\"anonymous\" defer></script>");
            }

            return sb.ToString().TrimEnd();
        }

        private string BuildModulesBootstrap(HeadAssetConfig config)
        {
            if (config.ModulePaths.Count == 0)
            {
                return string.Empty;
            }

            var imports = string.Join("," + Environment.NewLine, config.ModulePaths.Select(path => $"        import(`{_assetVersionProvider.AppendVersion(path)}`)"));
            return
$@"<script type=""module"">
    window.modulesReady = Promise.all([
{imports}
    ]);
</script>";
        }

        private static HeadAssetConfig GetHeadAssetConfig(string path)
        {
            if (IsLeagueRoute(path) || IsAthleteRoute(path))
            {
                return new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/flags.js",
                        "/js/leagueIcons.js",
                        "/js/pheno-age.js",
                        "/js/bortz-age.js",
                        "/js/badges.js",
                        "/js/age-visualization.js"
                    ]);
            }

            return path.ToLowerInvariant() switch
            {
                "/" or "/index.html" => new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/flags.js",
                        "/js/leagueIcons.js",
                        "/js/pheno-age.js",
                        "/js/bortz-age.js",
                        "/js/badges.js",
                        "/js/age-visualization.js"
                    ]),
                "/leaderboard/leaderboard.html" => new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/flags.js",
                        "/js/leagueIcons.js",
                        "/js/pheno-age.js",
                        "/js/bortz-age.js",
                        "/js/badges.js",
                        "/js/age-visualization.js"
                    ]),
                "/event-board/event-board.html" => new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/flags.js",
                        "/js/leagueIcons.js",
                        "/js/badges.js"
                    ]),
                "/event-board-embed.html" => new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/flags.js",
                        "/js/leagueIcons.js",
                        "/js/badges.js"
                    ]),
                "/play/edit-profile.html" => new HeadAssetConfig(
                    IncludeValidator: true,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/flags.js",
                        "/js/leagueIcons.js"
                    ]),
                "/play/proof-upload.html" => new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/proof-helpers.js"
                    ]),
                "/play/character-selection.html" => new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js"
                    ]),
                "/play/character-customization.html" => new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/flags.js",
                        "/js/leagueIcons.js",
                        "/js/badges.js",
                        "/js/proof-helpers.js",
                        "/js/pro-discounts.js"
                    ]),
                "/onboarding/pheno-age.html" => new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/pheno-age.js",
                        "/js/bioage-rank-preview.js"
                    ]),
                "/onboarding/bortz-age.html" => new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/pheno-age.js",
                        "/js/bortz-age.js",
                        "/js/bioage-rank-preview.js"
                    ]),
                "/onboarding/convergence.html" => new HeadAssetConfig(
                    IncludeValidator: true,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/flags.js",
                        "/js/leagueIcons.js",
                        "/js/proof-helpers.js"
                    ]),
                "/onboarding/join-game.html" => new HeadAssetConfig(
                    IncludeValidator: false,
                    ModulePaths:
                    [
                        "/js/misc.js",
                        "/js/pro-discounts.js"
                    ]),
                _ => HeadAssetConfig.Empty
            };
        }

        private SeoMeta GetSeoMeta(HttpContext context)
        {
            var requestPath = context.Request.Path.Value ?? "/";
            var baseSeo = GetBaseSeoMeta(requestPath);

            if (TryGetAthleteSeoMeta(context, baseSeo, out var athleteSeo))
            {
                return athleteSeo;
            }

            if (TryGetLeaderboardViewSeoMeta(context, out var leaderboardViewSeo))
            {
                return leaderboardViewSeo;
            }

            if (TryGetLeagueSeoMeta(context, out var leagueSeo))
            {
                return leagueSeo;
            }

            return baseSeo;
        }

        private SeoMeta GetBaseSeoMeta(string requestPath)
        {
            var canonicalPath = RouteCanonicalization.GetCanonicalPath(requestPath);
            var canonicalUrl = $"{SiteBaseUrl}{canonicalPath}";
            var defaultOgImage = BuildDefaultOgImageUrl();
            var longevitymaxxingOgImage = BuildOgImageUrl(LongevitymaxxingOgImagePath);

            return canonicalPath switch
            {
                "/" => new SeoMeta(
                    canonicalPath,
                    "Reverse your biological age and climb the Longevity World Cup leaderboard. Compare pheno age and bortz age results in a global anti-aging competition.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup | Reverse Your Biological Age",
                    "Longevity World Cup | Reverse Your Biological Age",
                    "Too old for your sport? Not this one. Reverse your age and rise on the leaderboard.",
                    BuildPageOgImageUrl("home", defaultOgImage)
                ),
                "/leaderboard" => new SeoMeta(
                    canonicalPath,
                    "View the Longevity World Cup leaderboard for verified biological age reduction rankings across athletes, leagues, and categories.",
                    "index, follow",
                    canonicalUrl,
                    "Leaderboard | Longevity World Cup",
                    "Leaderboard | Longevity World Cup",
                    "View the Longevity World Cup leaderboard for verified biological age reduction rankings across athletes, leagues, and categories.",
                    defaultOgImage
                ),
                "/longevitymaxxing" => new SeoMeta(
                    canonicalPath,
                    "Join the Longevitymaxxing Challenge, a 14-day Lifestyle challenge for daily momentum across sleep, exercise, nutrition, and vices.",
                    "index, follow",
                    canonicalUrl,
                    "Longevitymaxxing Challenge | Longevity World Cup",
                    "Longevitymaxxing Challenge | Longevity World Cup",
                    "A 14-day Lifestyle challenge for getting momentum back with a public visual leaderboard.",
                    longevitymaxxingOgImage
                ),
                "/events" => new SeoMeta(
                    canonicalPath,
                    "Track Longevity World Cup highlights, announcements, and major milestones from the current season.",
                    "index, follow",
                    canonicalUrl,
                    "Highlights | Longevity World Cup",
                    "Highlights | Longevity World Cup",
                    "Follow key Longevity World Cup events, season updates, and competition highlights.",
                    BuildPageOgImageUrl("events", defaultOgImage)
                ),
                "/media" => new SeoMeta(
                    canonicalPath,
                    "Download official Longevity World Cup media assets, logos, and press materials.",
                    "index, follow",
                    canonicalUrl,
                    "Media Kit | Longevity World Cup",
                    "Media Kit | Longevity World Cup",
                    "Access the Longevity World Cup media kit with press-ready branding assets and resources.",
                    BuildPageOgImageUrl("media", defaultOgImage)
                ),
                "/about" => new SeoMeta(
                    canonicalPath,
                    "Learn why Longevity World Cup is an open competition where longevity athletes rank by biomarker-based biological age reduction.",
                    "index, follow",
                    canonicalUrl,
                    "About Longevity World Cup",
                    "About Longevity World Cup",
                    "Learn why Longevity World Cup is an open competition where longevity athletes rank by biomarker-based biological age reduction.",
                    BuildPageOgImageUrl("about", defaultOgImage)
                ),
                "/history" => new SeoMeta(
                    canonicalPath,
                    "Read the history of longevity as a sport, from early biological age leaderboards to the Longevity World Cup.",
                    "index, follow",
                    canonicalUrl,
                    "History of Longevity as a Sport | Longevity World Cup",
                    "History of Longevity as a Sport | Longevity World Cup",
                    "Read the history of longevity as a sport, from early biological age leaderboards to the Longevity World Cup.",
                    BuildPageOgImageUrl("history", defaultOgImage)
                ),
                "/ruleset" => new SeoMeta(
                    canonicalPath,
                    "Review the Longevity World Cup ruleset for seasons, tracks, rankings, valid submissions, prizes, and payouts.",
                    "index, follow",
                    canonicalUrl,
                    "Ruleset | Longevity World Cup",
                    "Ruleset | Longevity World Cup",
                    "Review the Longevity World Cup ruleset for seasons, tracks, rankings, valid submissions, prizes, and payouts.",
                    BuildPageOgImageUrl("ruleset", defaultOgImage)
                ),
                _ when !IndexableRoutes.Contains(canonicalPath) => new SeoMeta(
                    canonicalPath,
                    "Longevity World Cup member page.",
                    "noindex, nofollow",
                    canonicalUrl,
                    "Longevity World Cup",
                    "Longevity World Cup",
                    "Longevity World Cup member page.",
                    defaultOgImage
                ),
                _ => new SeoMeta(
                    canonicalPath,
                    "Longevity World Cup - reverse biological age and compete globally.",
                    "index, follow",
                    canonicalUrl,
                    "Longevity World Cup",
                    "Longevity World Cup",
                    "Longevity World Cup - reverse biological age and compete globally.",
                    defaultOgImage
                )
            };
        }

        private string BuildDefaultOgImageUrl()
        {
            return BuildOgImageUrl(DefaultOgImagePath);
        }

        private string BuildPageOgImageUrl(string pageSlug, string fallbackUrl)
        {
            return _pageOgImages.IsConfigured && _pageOgImages.TryGetCurrentPayload(pageSlug, out var payload)
                ? _pageOgImages.BuildVersionedImageUrl(SiteBaseUrl, payload)
                : fallbackUrl;
        }

        private string BuildOgImageUrl(string assetPath)
        {
            return $"{SiteBaseUrl}{_assetVersionProvider.AppendVersion(assetPath)}";
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

        private static string RemoveJoinGameButtons(string html)
        {
            return Regex.Replace(
                html,
                @"\s*<button\b(?=[^>]*\bclass\s*=\s*[""'][^""']*\bjoin-game\b)[\s\S]*?</button>",
                "",
                RegexOptions.IgnoreCase);
        }

        private static bool ShouldRemoveJoinGameButtons(string? path)
        {
            var normalizedPath = RouteCanonicalization.NormalizePath(path);

            return normalizedPath.Equals("/play", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith("/play/", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.Contains("/join-game", StringComparison.OrdinalIgnoreCase);
        }

        private static string EncodeMeta(string value)
        {
            return System.Net.WebUtility.HtmlEncode(value ?? string.Empty);
        }

        private bool TryGetAthleteSeoMeta(HttpContext context, SeoMeta baseSeo, out SeoMeta seo)
        {
            seo = baseSeo;
            if (!TryResolveAthleteSlug(context, baseSeo.CanonicalPath, out var rawSlug))
            {
                return false;
            }

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

            var requestCanonicalPath = RouteCanonicalization.GetCanonicalPath(context.Request.Path.Value);
            var canonicalPath = string.Equals(requestCanonicalPath, "/leaderboard", StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(payload.InternalSlug, "ultimate", StringComparison.OrdinalIgnoreCase)
                ? "/leaderboard"
                : $"/league/{payload.RouteSlug}";
            var canonicalUrl = $"{SiteBaseUrl}{canonicalPath}";
            var title = $"{payload.DisplayName} | Longevity World Cup";
            var top3Text = payload.Top3Names.Count > 0
                ? $" Current top athletes: {string.Join(", ", payload.Top3Names.Take(3))}."
                : "";
            var description = $"Track rankings in the {payload.DisplayName}.{top3Text}";
            var ogImageUrl = _leagueOgImages.IsConfigured
                ? _leagueOgImages.BuildVersionedImageUrl(SiteBaseUrl, payload)
                : BuildDefaultOgImageUrl();

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

        private bool TryGetLeaderboardViewSeoMeta(HttpContext context, out SeoMeta seo)
        {
            seo = null!;
            if (!TryResolveLeaderboardViewSlug(context, out var viewSlug) ||
                !LeaderboardViewSeoBySlug.TryGetValue(viewSlug, out var viewSeo))
            {
                return false;
            }

            var canonicalPath = $"/league/{viewSlug}";
            var canonicalUrl = $"{SiteBaseUrl}{canonicalPath}";
            var defaultOgImage = BuildDefaultOgImageUrl();
            var ogImageUrl = BuildPageOgImageUrl($"view-{viewSlug}", defaultOgImage);

            seo = new SeoMeta(
                canonicalPath,
                viewSeo.Description,
                "index, follow",
                canonicalUrl,
                viewSeo.Title,
                viewSeo.Title,
                viewSeo.Description,
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

        private static bool IsAthleteRoute(string? path)
        {
            return !string.IsNullOrWhiteSpace(path)
                   && path.StartsWith("/athlete/", StringComparison.OrdinalIgnoreCase)
                   && path.Length > "/athlete/".Length;
        }

        private static bool TryResolveAthleteSlug(HttpContext context, string canonicalPath, out string rawSlug)
        {
            rawSlug = "";

            if (IsAthleteRoute(canonicalPath))
            {
                rawSlug = canonicalPath["/athlete/".Length..].Trim('/');
                return !string.IsNullOrWhiteSpace(rawSlug);
            }

            if (!string.Equals(canonicalPath, "/", StringComparison.Ordinal) ||
                !context.Request.Query.TryGetValue("athlete", out var athleteQuery))
            {
                return false;
            }

            rawSlug = athleteQuery.ToString();
            return !string.IsNullOrWhiteSpace(rawSlug);
        }

        private static bool TryResolveLeagueSlug(HttpContext context, out string leagueSlug)
        {
            leagueSlug = "";
            var requestPath = context.Request.Path.Value ?? "";
            var canonicalPath = RouteCanonicalization.GetCanonicalPath(requestPath);

            if (IsLeagueRoute(canonicalPath))
            {
                var raw = canonicalPath["/league/".Length..].Trim('/');
                return LeagueOgImageService.TryNormalizeLeagueSlug(raw, out leagueSlug);
            }

            if (!string.Equals(canonicalPath, "/", StringComparison.Ordinal) &&
                !string.Equals(canonicalPath, "/leaderboard", StringComparison.OrdinalIgnoreCase))
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

            if (string.Equals(canonicalPath, "/leaderboard", StringComparison.OrdinalIgnoreCase))
            {
                return LeagueOgImageService.TryNormalizeLeagueSlug("ultimate", out leagueSlug);
            }

            return false;
        }

        private static bool TryResolveLeaderboardViewSlug(HttpContext context, out string viewSlug)
        {
            viewSlug = "";
            var requestPath = context.Request.Path.Value ?? "";
            var canonicalPath = RouteCanonicalization.GetCanonicalPath(requestPath);

            if (IsLeagueRoute(canonicalPath))
            {
                var raw = canonicalPath["/league/".Length..].Trim('/');
                if (LeaderboardViewSeoBySlug.ContainsKey(raw))
                {
                    viewSlug = raw.ToLowerInvariant();
                    return true;
                }
            }

            if ((!string.Equals(canonicalPath, "/", StringComparison.Ordinal) &&
                 !string.Equals(canonicalPath, "/leaderboard", StringComparison.OrdinalIgnoreCase)) ||
                !context.Request.Query.TryGetValue("view", out var viewQuery))
            {
                return false;
            }

            var candidate = viewQuery.ToString().Trim().ToLowerInvariant();
            if (!LeaderboardViewSeoBySlug.ContainsKey(candidate))
            {
                return false;
            }

            viewSlug = candidate;
            return true;
        }

        private string BuildStructuredDataJson(SeoMeta seo)
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

            var organization = new Dictionary<string, object>
            {
                ["@type"] = "Organization",
                ["@id"] = $"{SiteBaseUrl}/#organization",
                ["name"] = "Longevity World Cup",
                ["url"] = SiteBaseUrl,
                ["logo"] = BuildDefaultOgImageUrl(),
                ["description"] = "An open longevity sport competition where longevity athletes rank by improving biological age measures.",
                ["email"] = "hi@longevityworldcup.com",
                ["founder"] = new Dictionary<string, object>
                {
                    ["@type"] = "Person",
                    ["name"] = "Adam Ficsor",
                    ["alternateName"] = "nopara73"
                },
                ["contactPoint"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["@type"] = "ContactPoint",
                        ["contactType"] = "public inquiries",
                        ["email"] = "hi@longevityworldcup.com",
                        ["availableLanguage"] = new[] { "en" }
                    }
                },
                ["areaServed"] = "Worldwide",
                ["sameAs"] = new[]
                {
                    "https://github.com/nopara73/LongevityWorldCup/",
                    "https://x.com/LongevityWorldC",
                    "https://www.reddit.com/r/LongevityWorldCup/",
                    "https://www.youtube.com/@longevityworldcup",
                    "https://www.instagram.com/LongevityWorldCup/",
                    "https://www.threads.com/@longevityworldcup",
                    "https://www.tiktok.com/@nopara73"
                },
                ["knowsAbout"] = new[]
                {
                    "Longevity sport",
                    "Biological age research",
                    "Biological aging clocks",
                    "Pheno Age",
                    "Bortz Age",
                    "Biomarkers",
                    "Age Reduction",
                    "Longevity athletes",
                    "Leaderboards"
                }
            };

            var website = new Dictionary<string, object>
            {
                ["@type"] = "WebSite",
                ["@id"] = $"{SiteBaseUrl}/#website",
                ["url"] = SiteBaseUrl,
                ["name"] = "Longevity World Cup",
                ["description"] = "Competitive longevity platform with public leaderboards, biological aging clock tools, events, and media updates.",
                ["inLanguage"] = "en",
                ["publisher"] = new Dictionary<string, object>
                {
                    ["@id"] = $"{SiteBaseUrl}/#organization"
                }
            };

            var webApplication = new Dictionary<string, object>
            {
                ["@type"] = "WebApplication",
                ["@id"] = $"{SiteBaseUrl}/#webapp",
                ["name"] = "Longevity World Cup",
                ["url"] = SiteBaseUrl,
                ["description"] = "A web competition where longevity athletes submit biological age results, compare Age Reduction, and compete on public leaderboards.",
                ["applicationCategory"] = "HealthApplication",
                ["operatingSystem"] = "Web",
                ["isAccessibleForFree"] = true,
                ["inLanguage"] = "en",
                ["publisher"] = new Dictionary<string, object>
                {
                    ["@id"] = $"{SiteBaseUrl}/#organization"
                },
                ["provider"] = new Dictionary<string, object>
                {
                    ["@id"] = $"{SiteBaseUrl}/#organization"
                },
                ["audience"] = new Dictionary<string, object>
                {
                    ["@type"] = "Audience",
                    ["audienceType"] = "Longevity athletes and people interested in biological age competition"
                },
                ["featureList"] = new[]
                {
                    "Ultimate League leaderboard",
                    "Pro and Amateur tracks",
                    "Pheno Age biological aging clock tools",
                    "Bortz Age biological aging clock tools",
                    "Biomarker-based result submissions",
                    "Age Reduction comparison",
                    "Crowd Age leaderboard",
                    "Public athlete profiles",
                    "Machine-readable public API"
                },
                ["offers"] = new Dictionary<string, object>
                {
                    ["@type"] = "Offer",
                    ["price"] = "0",
                    ["priceCurrency"] = "USD",
                    ["description"] = "Public leaderboard, biological aging clock tools, and no-auth public API access are free."
                }
            };

            var competitionService = new Dictionary<string, object>
            {
                ["@type"] = "Service",
                ["@id"] = $"{SiteBaseUrl}/#competition-service",
                ["name"] = "Longevity World Cup biological age competition",
                ["url"] = SiteBaseUrl,
                ["serviceType"] = "Longevity sport competition",
                ["description"] = "An open competition where longevity athletes submit biological age results and rank by Age Reduction across public leaderboards.",
                ["provider"] = new Dictionary<string, object>
                {
                    ["@id"] = $"{SiteBaseUrl}/#organization"
                },
                ["areaServed"] = "Worldwide",
                ["audience"] = new Dictionary<string, object>
                {
                    ["@type"] = "Audience",
                    ["audienceType"] = "Longevity athletes, biological age researchers, and people comparing biological aging clock results"
                },
                ["hasOfferCatalog"] = new Dictionary<string, object>
                {
                    ["@type"] = "OfferCatalog",
                    ["name"] = "Longevity World Cup public services",
                    ["itemListElement"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["@type"] = "Offer",
                            ["itemOffered"] = new Dictionary<string, object>
                            {
                                ["@type"] = "Service",
                                ["name"] = "Public biological age leaderboard",
                                ["url"] = $"{SiteBaseUrl}/leaderboard"
                            }
                        },
                        new Dictionary<string, object>
                        {
                            ["@type"] = "Offer",
                            ["itemOffered"] = new Dictionary<string, object>
                            {
                                ["@type"] = "Service",
                                ["name"] = "Pheno Age and Bortz Age calculation API",
                                ["url"] = $"{SiteBaseUrl}/swagger"
                            }
                        },
                        new Dictionary<string, object>
                        {
                            ["@type"] = "Offer",
                            ["itemOffered"] = new Dictionary<string, object>
                            {
                                ["@type"] = "Service",
                                ["name"] = "Public athlete profiles and machine-readable leaderboard facts",
                                ["url"] = $"{SiteBaseUrl}/ai/leaderboard.md"
                            }
                        }
                    }
                }
            };

            var dateModified = _sitemap.GetLastModifiedUtcForPath(seo.CanonicalPath)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var webpage = new Dictionary<string, object>
            {
                ["@type"] = "WebPage",
                ["@id"] = $"{seo.CanonicalUrl}#webpage",
                ["url"] = seo.CanonicalUrl,
                ["name"] = seo.PageTitle,
                ["description"] = seo.Description,
                ["dateModified"] = dateModified,
                ["inLanguage"] = "en",
                ["isPartOf"] = new Dictionary<string, object>
                {
                    ["@id"] = $"{SiteBaseUrl}/#website"
                },
                ["about"] = new Dictionary<string, object>
                {
                    ["@id"] = $"{SiteBaseUrl}/#organization"
                },
                ["primaryImageOfPage"] = seo.OgImageUrl
            };

            var breadcrumbList = new Dictionary<string, object>
            {
                ["@type"] = "BreadcrumbList",
                ["itemListElement"] = breadcrumbItems
            };

            var graph = new List<object>
            {
                organization,
                website,
                webApplication,
                competitionService,
                webpage,
                breadcrumbList
            };

            if (string.Equals(seo.CanonicalPath, "/", StringComparison.Ordinal))
            {
                graph.Add(BuildHomeFaqPage());
            }

            if (string.Equals(seo.CanonicalPath, "/ruleset", StringComparison.OrdinalIgnoreCase))
            {
                graph.Add(BuildRulesetFaqPage());
            }

            var payload = new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org",
                ["@graph"] = graph
            };

            return JsonSerializer.Serialize(payload);
        }

        private static Dictionary<string, object> BuildHomeFaqPage()
        {
            static Dictionary<string, object> Question(string name, string answer)
            {
                return new Dictionary<string, object>
                {
                    ["@type"] = "Question",
                    ["name"] = name,
                    ["acceptedAnswer"] = new Dictionary<string, object>
                    {
                        ["@type"] = "Answer",
                        ["text"] = answer
                    }
                };
            }

            return new Dictionary<string, object>
            {
                ["@type"] = "FAQPage",
                ["@id"] = $"{SiteBaseUrl}/#faq",
                ["mainEntity"] = new object[]
                {
                    Question(
                        "What is Longevity World Cup?",
                        "Longevity World Cup is an open longevity sport competition where longevity athletes submit biological age results and rank on public leaderboards by Age Reduction."),
                    Question(
                        "How do rankings work?",
                        "The Ultimate League ranks Pro athletes before Amateur athletes, then orders each track by Effective Age Reduction and tie breakers."),
                    Question(
                        "Which biological aging clocks are used?",
                        "The current public competition uses Pheno Age for the Amateur path and Bortz Age for the Pro seasonal path."),
                    Question(
                        "How do I join?",
                        "Submit an application with eligible biomarker results and proof. The ruleset explains requirements before you apply.")
                }
            };
        }

        private static Dictionary<string, object> BuildRulesetFaqPage()
        {
            static Dictionary<string, object> Question(string name, string answer)
            {
                return new Dictionary<string, object>
                {
                    ["@type"] = "Question",
                    ["name"] = name,
                    ["acceptedAnswer"] = new Dictionary<string, object>
                    {
                        ["@type"] = "Answer",
                        ["text"] = answer
                    }
                };
            }

            return new Dictionary<string, object>
            {
                ["@type"] = "FAQPage",
                ["@id"] = $"{SiteBaseUrl}/ruleset#faq",
                ["mainEntity"] = new object[]
                {
                    Question(
                        "Who can participate in the Longevity World Cup?",
                        "Anyone interested in longevity and capable of submitting valid test results can participate."),
                    Question(
                        "How do I register for the competition?",
                        "Apply through the Longevity World Cup website."),
                    Question(
                        "Can I withdraw from the competition?",
                        "Yes. Email hi@longevityworldcup.com."),
                    Question(
                        "What is pheno age?",
                        "Pheno age is based on clinical biomarkers like glucose and CRP. It reflects physiological aging, not just years lived, and helps assess health and disease risk."),
                    Question(
                        "What is bortz age?",
                        "Bortz age is based on blood biomarkers such as Cystatin C, HbA1c, and ApoA1. It reflects physiological aging, not just years lived, and helps assess health and disease risk."),
                    Question(
                        "From which biomarkers can I calculate my pheno age?",
                        "Pheno age uses albumin, creatinine, glucose, C-reactive protein, lymphocyte percentage, mean corpuscular volume, red cell distribution width, alkaline phosphatase, and white blood cell count."),
                    Question(
                        "From which biomarkers can I calculate my bortz age?",
                        "Bortz age uses albumin, alkaline phosphatase, urea, total cholesterol, creatinine, Cystatin C, HbA1c, C-reactive protein, GGT, red blood cell count, MCV, RDW, monocytes, neutrophils, lymphocyte percentage, ALT, SHBG, vitamin D, glucose, MCH, and ApoA1."),
                    Question(
                        "Can I use any laboratory for my tests?",
                        "Yes, as long as the lab provides the biomarkers required for that clock."),
                    Question(
                        "Why does my pheno age result differ from other calculators?",
                        "The Longevity World Cup pheno age calculator is built for biological realism. It uses the corrected formula constant and applies biologically justified cutoffs so extreme biomarker values cannot make the calculated age look better while real-world risk gets worse."),
                    Question(
                        "Why does my bortz age result differ from other calculators?",
                        "The Longevity World Cup bortz age calculator is built for biological realism. It applies biologically justified cutoffs so extreme biomarker values cannot make the calculated age look better while real-world risk gets worse."),
                    Question(
                        "What happens if my results arrive late?",
                        "Each season closes in mid-January, giving your lab enough time to process a test from December 31."),
                    Question(
                        "What if there's a tie?",
                        "Ties break by older chronological age, then username alphabetically."),
                    Question(
                        "How is my score calculated if I submit multiple results?",
                        "If you submit multiple test results, the best result is used for your season standing for the relevant clock."),
                    Question(
                        "How are lab detection limits handled in the competition?",
                        "When a lab reports a biomarker value below the detection limit, that limit is used in the calculation. This most often affects CRP; when the limit is unknown, Longevity World Cup defaults to 1 mg/L."),
                    Question(
                        "How can I cheat?",
                        "You can't."),
                    Question(
                        "How does the Longevity World Cup compare to the Rejuvenation Olympics?",
                        "Longevity World Cup emphasizes absolute age reversal, annual seasons, multiple clocks or tracks over time, prize money funded by Bitcoin donations, leagues for category-based rankings, and traditional blood-test-based biological age calculations."),
                    Question(
                        "How much can I edit my profile picture?",
                        "Your profile picture must show you facing the camera, but you can edit it freely, including as a drawing or AI-generated version."),
                    Question(
                        "I'm already an athlete. How can I make changes?",
                        "Use the Athlete Dashboard or email hi@longevityworldcup.com."),
                    Question(
                        "What will sponsorships include?",
                        "Sponsorships are planned for future seasons. Future packages will let companies sponsor athletes for website visibility."),
                    Question(
                        "What if Bitcoin's value changes significantly?",
                        "Prize payouts are denominated in Bitcoin.")
                }
            };
        }

        private static string GetBreadcrumbLabel(string canonicalPath)
        {
            return canonicalPath switch
            {
                "/leaderboard" => "Leaderboard",
                "/longevitymaxxing" => "Longevitymaxxing Challenge",
                "/events" => "Events",
                "/media" => "Media",
                "/about" => "About",
                "/history" => "History",
                "/ruleset" => "Ruleset",
                _ when canonicalPath.StartsWith("/league/", StringComparison.OrdinalIgnoreCase) => "Leaderboard",
                _ => "Page"
            };
        }

        private string ApplyLongevitymaxxingPromo(string html)
        {
            if (ShouldShowLongevitymaxxingPromo())
            {
                return html;
            }

            return RemoveMarkedSection(html, LongevitymaxxingPromoStartMarker, LongevitymaxxingPromoEndMarker);
        }

        private bool ShouldShowLongevitymaxxingPromo()
        {
            var cfg = _config.LongevitymaxxingChallenge ?? new LongevitymaxxingChallengeConfig();
            var start = ParseDateOnly(cfg.StartDate, DateOnly.FromDateTime(DateTime.UtcNow.Date));
            var signupCloses = ParseDateTimeOffset(cfg.SignupClosesAtUtc, new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
            var now = DateTimeOffset.UtcNow;

            return now < signupCloses.ToUniversalTime() && DateOnly.FromDateTime(now.UtcDateTime.Date) < start;
        }

        private static DateOnly ParseDateOnly(string? value, DateOnly fallback)
        {
            return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                ? parsed
                : fallback;
        }

        private static DateTimeOffset ParseDateTimeOffset(string? value, DateTimeOffset fallback)
        {
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed.ToUniversalTime()
                : fallback.ToUniversalTime();
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

        private sealed record LeaderboardViewSeo(string Title, string Description);

        private sealed record HeadAssetConfig(bool IncludeValidator, IReadOnlyList<string> ModulePaths)
        {
            public static readonly HeadAssetConfig Empty = new(false, Array.Empty<string>());
        }
    }
}
