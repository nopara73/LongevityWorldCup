namespace LongevityWorldCup.Website.Middleware
{
    internal static class RouteCanonicalization
    {
        internal const string CanonicalPathItemKey = "__LwcCanonicalPath";

        // Keep public paths, their templates, and every historical alias together.
        // Physical assets, API endpoints, embeds, and error fallback files are not page aliases.
        internal static readonly IReadOnlyList<PageRoute> Pages =
        [
            new("/", "/index.html", "/index.html"),
            new("/events", "/event-board/event-board.html", "/event-board", "/event-board/event-board", "/event-board/event-board.html"),
            new("/leaderboard", "/leaderboard/leaderboard.html", "/leaderboard/leaderboard", "/leaderboard/leaderboard.html"),
            new("/longevitymaxxing", "/longevitymaxxing/longevitymaxxing.html", "/longevitymaxxing/longevitymaxxing", "/longevitymaxxing/longevitymaxxing.html"),
            new("/helstab-kihivas", "/helstab-kihivas/helstab-kihivas.html", "/helstab-kihivas/helstab-kihivas", "/helstab-kihivas/helstab-kihivas.html"),
            new("/media", "/misc-pages/media.html", "/misc-pages/media", "/misc-pages/media.html"),
            new("/about", "/misc-pages/about.html", "/misc-pages/about", "/misc-pages/about.html"),
            new("/history", "/misc-pages/history.html", "/misc-pages/history", "/misc-pages/history.html"),
            new("/ruleset", "/misc-pages/ruleset.html", "/rules", "/misc-pages/ruleset", "/misc-pages/ruleset.html"),
            new("/privacy", "/privacy-policy.html", "/privacy-policy", "/privacy-policy.html"),
            new("/play", "/play/menu.html", "/play/menu", "/play/menu.html"),
            new("/join", "/play/menu.html", "/start", "/onboarding/join-game", "/onboarding/join-game.html"),
            new("/apply", "/onboarding/convergence.html", "/onboarding/convergence", "/onboarding/convergence.html"),
            new("/review", "/onboarding/application-review.html", "/onboarding/application-review", "/onboarding/application-review.html"),
            new("/proofs", "/play/proof-upload.html", "/play/proof-upload", "/play/proof-upload.html"),
            new("/select-athlete", "/play/menu.html", "/play/character-selection", "/play/character-selection.html"),
            new("/dashboard", "/play/menu.html", "/customize-athlete", "/play/character-customization", "/play/character-customization.html"),
            new("/edit-profile", "/play/edit-profile.html", "/play/edit-profile", "/play/edit-profile.html"),
            new("/unsubscribe", "/unsubscribe.html", "/unsubscribe.html"),
            new("/pheno-age", "/onboarding/pheno-age.html", "/onboarding/pheno-age", "/onboarding/pheno-age.html"),
            new("/bortz-age", "/onboarding/bortz-age.html", "/onboarding/bortz-age", "/onboarding/bortz-age.html")
        ];

        private static readonly IReadOnlyDictionary<string, PageRoute> PageByPath = Pages
            .SelectMany(page => page.Aliases.Prepend(page.CanonicalPath).Select(path => (path, page)))
            .ToDictionary(entry => entry.path, entry => entry.page, StringComparer.OrdinalIgnoreCase);

        internal static bool TryGetPage(PathString path, out PageRoute page)
        {
            // Request.Path is already decoded. A literal ? or # here belongs to
            // the path, not a query or fragment, and must not resolve as an alias.
            var value = path.Value ?? "/";
            return PageByPath.TryGetValue(value.Length > 1 ? value.TrimEnd('/') : value, out page!);
        }

        public static string NormalizePath(string? rawPath)
        {
            var path = string.IsNullOrWhiteSpace(rawPath) ? "/" : rawPath.Trim();
            if (!path.StartsWith('/')) path = "/" + path;
            var suffixIndex = path.AsSpan().IndexOfAny('?', '#');
            if (suffixIndex >= 0) path = path[..suffixIndex];
            return path.Length > 1 ? path.TrimEnd('/') : path;
        }

        public static string GetCanonicalPath(string? rawPath)
        {
            var normalized = NormalizePath(rawPath);
            return PageByPath.TryGetValue(normalized, out var page)
                ? page.CanonicalPath
                : normalized.ToLowerInvariant();
        }

        internal sealed record PageRoute(string CanonicalPath, string TemplatePath, params string[] Aliases);
    }
}
