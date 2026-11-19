using System.Net;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Tools;

internal static partial class HomepagePodiumAssets
{
    public const string SectionStyles = """
        <link rel="stylesheet" href="{{ASSET_HOMEPAGE_CSS}}">
        <link rel="stylesheet" href="{{ASSET_ENTRY_JOURNEY_CSS}}">
        <link rel="stylesheet" href="{{ASSET_PRIZE_POOL_CSS}}">
        <link rel="stylesheet" href="{{ASSET_MERCH_SHELF_CSS}}">
        """;

    private const string EnhancementMedia = "not all and (scripting:enabled)";
    // The podium, header, typography and layout keep their normal blocking styles.
    private static readonly HashSet<string> EnhancementStyles = new(StringComparer.Ordinal)
    {
        "/vendor/font-awesome/6.7.2/css/all.min.css", "/css/icons.css", "/css/portrait-system.css",
        "/css/badges.css", "/css/mobile-roughness.css", "/css/flow-controls.css",
        "/vendor/flag-icons/css/flag-icons.min.css", "/css/entry-journey.css",
        "/css/prize-pool.css", "/css/merch-shelf.css", "/css/touch.css", "/css/pointer.css",
        "/css/motion.css", "/css/empty-state.css", "/css/form-controls.css", "/css/share-sheet.css",
        "/css/charts.css", "/css/scroll-choreography.css", "/css/image-viewer.css",
        "/css/disclosure.css", "/css/celebration.css", "/css/leaderboard-ordering.css",
        "/css/leaderboard-filters.css"
    };

    public static string PrepareHead(string head, string? portrait)
    {
        // The populated homepage can paint its title with the metric-adjusted fallback
        // while Roboto loads on demand. Only preload styles for the header and podium.
        var preloads = portrait is null
            ? """
              <link rel="preload" href="{{ASSET_ROBOTO_REGULAR}}" as="font" type="font/woff2" crossorigin>
              <link rel="preload" href="{{ASSET_ROBOTO_BOLD}}" as="font" type="font/woff2" crossorigin>
              <link rel="preload" href="{{ASSET_FONT_AWESOME_CSS}}" as="style">
              """
            : $$$"""
              <link rel="preload" as="style" fetchpriority="high" href="{{ASSET_SITE_HEADER_CSS}}">
              <link rel="preload" as="image" fetchpriority="high" href="{{{WebUtility.HtmlEncode(portrait)}}}">
              <link rel="preload" as="style" href="{{ASSET_LEADERBOARD_PODIUM_CSS}}">
              <script>
              document.addEventListener('load', ({ target }) => {
                  if (target instanceof HTMLLinkElement && target.media === '{{{EnhancementMedia}}}') target.media = 'all';
              }, true);
              </script>
              """;
        return head.Replace("<!--FIRST-SCREEN-PRELOADS-->", preloads, StringComparison.Ordinal)
            .Replace("{{ICON_SCRIPT_LOADING}}", portrait is null ? "" : " defer", StringComparison.Ordinal);
    }

    public static string PrioritizeStyles(string html) => VersionedStyle().Replace(html, match =>
    {
        var path = match.Groups[1].Value;
        // Later sections wait for their styles in the body, behind the title and winners.
        if (path is "/css/leaderboard-content.css" or "/css/homepage.css")
            return match.Value[..^1] + " fetchpriority=\"low\">";

        // This entire sheet is inside the same media query. Light pages can paint
        // immediately and a live theme change still enables the dark surfaces.
        if (path == "/css/dark-system.css")
            return match.Value[..^1] + " media=\"(prefers-color-scheme: dark) and (forced-colors: none)\">";

        // With scripting disabled the media query matches immediately. Otherwise each
        // enhancement joins the cascade in its original position once downloaded.
        return EnhancementStyles.Contains(path)
            ? match.Value[..^1] + $" media=\"{EnhancementMedia}\">"
            : match.Value;
    });

    [GeneratedRegex("""<link rel="stylesheet" href="(/[^"?]+)\?v=[^"]+">""", RegexOptions.CultureInvariant)]
    private static partial Regex VersionedStyle();
}
