using System.Net;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Tools;

internal static partial class HomepagePodiumAssets
{
    // The podium, header, typography and layout keep their normal blocking styles.
    private static readonly HashSet<string> EnhancementStyles = new(StringComparer.Ordinal)
    {
        "/css/badges.css", "/css/mobile-roughness.css", "/css/flow-controls.css",
        "/vendor/flag-icons/css/flag-icons.min.css", "/css/entry-journey.css",
        "/css/prize-pool.css", "/css/merch-shelf.css", "/css/touch.css", "/css/pointer.css",
        "/css/motion.css", "/css/empty-state.css", "/css/form-controls.css", "/css/share-sheet.css",
        "/css/charts.css", "/css/scroll-choreography.css", "/css/image-viewer.css",
        "/css/disclosure.css", "/css/celebration.css", "/css/leaderboard-ordering.css",
        "/css/leaderboard-filters.css"
    };

    public static string Preloads(string? portrait) => portrait is null ? string.Empty :
        $"<link rel=\"preload\" as=\"image\" fetchpriority=\"high\" href=\"{WebUtility.HtmlEncode(portrait)}\">\n" +
        "<link rel=\"preload\" as=\"style\" href=\"{{ASSET_LEADERBOARD_CONTENT_CSS}}\">\n" +
        "<link rel=\"preload\" as=\"style\" href=\"{{ASSET_SITE_HEADER_CSS}}\">";

    // With scripting disabled the media query matches immediately. With scripting
    // enabled each sheet joins the cascade in its original position once downloaded.
    public static string DeferEnhancementStyles(string html) => VersionedStyle().Replace(html, match =>
        EnhancementStyles.Contains(match.Groups[1].Value)
            ? match.Value[..^1] + " media=\"not all and (scripting: enabled)\" onload=\"this.media='all'\">"
            : match.Value);

    [GeneratedRegex("""<link rel="stylesheet" href="(/[^"?]+)\?v=[^"]+">""", RegexOptions.CultureInvariant)]
    private static partial Regex VersionedStyle();
}
