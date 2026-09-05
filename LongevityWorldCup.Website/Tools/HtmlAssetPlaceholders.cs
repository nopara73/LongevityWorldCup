using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Tools;

/// <summary>
/// Resolves only the asset placeholders present in the assembled page.
/// Versions are reused within a render and checked again on the next request.
/// </summary>
public static partial class HtmlAssetPlaceholders
{
    private static readonly FrozenDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["{{ASSET_AGE_VISUALIZATION_JS}}"] = "/js/age-visualization.js",
        ["{{ASSET_APPLE_TOUCH_ICON_DARK}}"] = "/assets/apple-touch-icon-dark.png",
        ["{{ASSET_APPLE_TOUCH_ICON}}"] = "/assets/apple-touch-icon.png",
        ["{{ASSET_BADGES_CSS}}"] = "/css/badges.css",
        ["{{ASSET_BADGES_JS}}"] = "/js/badges.js",
        ["{{ASSET_BEAN_WAITING_PNG}}"] = "/assets/content-images/bean-waiting.png",
        ["{{ASSET_BEAN_WAITING_WEBP}}"] = "/assets/content-images/bean-waiting.webp",
        ["{{ASSET_BIOAGEFORM_CSS}}"] = "/css/bioageform.css",
        ["{{ASSET_BORTZ_AGE_JS}}"] = "/js/bortz-age.js",
        ["{{ASSET_CUSTOM_EVENT_IMAGE}}"] = "/assets/custom_event.png",
        ["{{ASSET_CUSTOM_EVENT_MARKUP_JS}}"] = "/js/custom-event-markup.js",
        ["{{ASSET_DONATION_QR}}"] = "/assets/Donation25QR.png",
        ["{{ASSET_FAVICON_128}}"] = "/assets/favicon-128x128.png",
        ["{{ASSET_FAVICON_192}}"] = "/assets/favicon-192x192.png",
        ["{{ASSET_FAVICON_512}}"] = "/assets/favicon-512x512.png",
        ["{{ASSET_FAVICON_DARK_192}}"] = "/assets/favicon-dark-192x192.png",
        ["{{ASSET_FAVICON_DARK_512}}"] = "/assets/favicon-dark-512x512.png",
        ["{{ASSET_FAVICON_DARK_ICO}}"] = "/assets/favicon-dark.ico",
        ["{{ASSET_FAVICON_ICO}}"] = "/assets/favicon.ico",
        ["{{ASSET_FLAG_ICONS_CSS}}"] = "/vendor/flag-icons/css/flag-icons.min.css",
        ["{{ASSET_FLOW_CONTROLS_CSS}}"] = "/css/flow-controls.css",
        ["{{ASSET_FONT_AWESOME_CSS}}"] = "/vendor/font-awesome/6.7.2/css/all.min.css",
        ["{{ASSET_HD_LOGO_THUMB_SM}}"] = "/assets/HdLogo_thumb_sm.png",
        ["{{ASSET_HEADSHOT_JPEG}}"] = "/assets/content-images/headshot.jpg",
        ["{{ASSET_HEADSHOT_WEBP}}"] = "/assets/content-images/headshot.webp",
        ["{{ASSET_HELSTAB_KIHIVAS_CSS}}"] = "/css/helstab-kihivas.css",
        ["{{ASSET_JUST_TRACK_IT_IMAGE}}"] = "/assets/content-images/JustTrackIt.jpg",
        ["{{ASSET_LEAGUE_ICONS_JS}}"] = "/js/leagueIcons.js",
        ["{{ASSET_LONGEVITYMAXXING_CSS}}"] = "/css/longevitymaxxing.css",
        ["{{ASSET_LONGEVITYMAXXING_JS}}"] = "/js/longevitymaxxing.js",
        ["{{ASSET_MARTIN_HELSTAB_PROFILE_IMAGE}}"] = "/athletes/martin_helstab/martin_helstab.webp",
        ["{{ASSET_MERCH_CAP}}"] = "/assets/content-images/merch/cap.webp",
        ["{{ASSET_MERCH_HOODIE}}"] = "/assets/content-images/merch/hoodie.webp",
        ["{{ASSET_MERCH_MUG}}"] = "/assets/content-images/merch/mug.webp",
        ["{{ASSET_MISC_JS}}"] = "/js/misc.js",
        ["{{ASSET_MOBILE_ROUGHNESS_CSS}}"] = "/css/mobile-roughness.css",
        ["{{ASSET_ORBITRON_BOLD}}"] = "/assets/fonts/Orbitron-Bold.woff2",
        ["{{ASSET_PHENO_AGE_JS}}"] = "/js/pheno-age.js",
        ["{{ASSET_PLAY_ATHLETE_FLOW_CSS}}"] = "/css/play-athlete-flow.css",
        ["{{ASSET_PLAY_ATHLETE_PLACEHOLDER_JPEG}}"] = "/assets/content-images/play-athlete-placeholder.jpg",
        ["{{ASSET_PLAY_ATHLETE_PLACEHOLDER_WEBP}}"] = "/assets/content-images/play-athlete-placeholder.webp",
        ["{{ASSET_PLAY_MENU_CSS}}"] = "/css/play-menu.css",
        ["{{ASSET_POPPINS_BOLD}}"] = "/assets/fonts/Poppins-Bold.ttf",
        ["{{ASSET_POPPINS_REGULAR}}"] = "/assets/fonts/Poppins-Regular.ttf",
        ["{{ASSET_PRO_DISCOUNTS_JS}}"] = "/js/pro-discounts.js",
        ["{{ASSET_PROOF_HELPERS_JS}}"] = "/js/proof-helpers.js",
        ["{{ASSET_ROBOTO_BOLD}}"] = "/assets/fonts/Roboto-Bold.woff2",
        ["{{ASSET_ROBOTO_LIGHT}}"] = "/assets/fonts/Roboto-Light.woff2",
        ["{{ASSET_ROBOTO_REGULAR}}"] = "/assets/fonts/Roboto-Regular.woff2",
        ["{{ASSET_SITE_DARK_WEBMANIFEST}}"] = "/assets/site-dark.webmanifest",
        ["{{ASSET_SITE_STATISTICS_CSS}}"] = "/css/site-statistics.css",
        ["{{ASSET_SITE_STATISTICS_JS}}"] = "/js/site-statistics.js",
        ["{{ASSET_SITE_STATISTICS_TRACKING_JS}}"] = "/js/site-statistics-tracking.js",
        ["{{ASSET_SITE_WEBMANIFEST}}"] = "/assets/site.webmanifest",
        ["{{ASSET_TROLLFACE}}"] = "/assets/content-images/trollface.png",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string Replace(string html, Func<string, string> appendVersion)
    {
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        return AssetPlaceholder().Replace(html, match =>
        {
            if (!Paths.TryGetValue(match.Value, out var path))
                return match.Value;

            if (!versions.TryGetValue(path, out var versionedPath))
            {
                versionedPath = appendVersion(path);
                versions.Add(path, versionedPath);
            }

            return versionedPath;
        });
    }

    [GeneratedRegex(@"\{\{ASSET_[A-Z0-9_]+\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex AssetPlaceholder();
}
