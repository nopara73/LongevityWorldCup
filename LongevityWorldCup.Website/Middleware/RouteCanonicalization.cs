namespace LongevityWorldCup.Website.Middleware
{
    internal static class RouteCanonicalization
    {
        private static readonly Dictionary<string, string> CanonicalAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["/index.html"] = "/",
            ["/events"] = "/events",
            ["/event-board"] = "/events",
            ["/event-board/event-board"] = "/events",
            ["/event-board/event-board.html"] = "/events",
            ["/leaderboard"] = "/leaderboard",
            ["/leaderboard/leaderboard"] = "/leaderboard",
            ["/leaderboard/leaderboard.html"] = "/leaderboard",
            ["/media"] = "/media",
            ["/misc-pages/media"] = "/media",
            ["/misc-pages/media.html"] = "/media",
            ["/play"] = "/play",
            ["/play/menu"] = "/play",
            ["/play/menu.html"] = "/play",
            ["/join"] = "/join",
            ["/start"] = "/join",
            ["/onboarding/join-game"] = "/join",
            ["/onboarding/join-game.html"] = "/join",
            ["/apply"] = "/apply",
            ["/onboarding/convergence"] = "/apply",
            ["/onboarding/convergence.html"] = "/apply",
            ["/review"] = "/review",
            ["/onboarding/application-review"] = "/review",
            ["/onboarding/application-review.html"] = "/review",
            ["/proofs"] = "/proofs",
            ["/play/proof-upload"] = "/proofs",
            ["/play/proof-upload.html"] = "/proofs",
            ["/select-athlete"] = "/select-athlete",
            ["/play/character-selection"] = "/select-athlete",
            ["/play/character-selection.html"] = "/select-athlete",
            ["/dashboard"] = "/dashboard",
            ["/customize-athlete"] = "/dashboard",
            ["/play/character-customization"] = "/dashboard",
            ["/play/character-customization.html"] = "/dashboard",
            ["/edit-profile"] = "/edit-profile",
            ["/play/edit-profile"] = "/edit-profile",
            ["/play/edit-profile.html"] = "/edit-profile",
            ["/pheno-age"] = "/pheno-age",
            ["/onboarding/pheno-age"] = "/pheno-age",
            ["/onboarding/pheno-age.html"] = "/pheno-age",
            ["/bortz-age"] = "/bortz-age",
            ["/onboarding/bortz-age"] = "/bortz-age",
            ["/onboarding/bortz-age.html"] = "/bortz-age"
        };

        public static string NormalizePath(string? rawPath)
        {
            var path = string.IsNullOrWhiteSpace(rawPath) ? "/" : rawPath.Trim();

            if (!path.StartsWith('/'))
            {
                path = "/" + path;
            }

            path = path.Split('?', '#')[0];

            if (path.Length > 1)
            {
                path = path.TrimEnd('/');
            }

            return path;
        }

        public static string GetCanonicalPath(string? rawPath)
        {
            var normalized = NormalizePath(rawPath);

            if (CanonicalAliases.TryGetValue(normalized, out var canonical))
            {
                return canonical;
            }

            return normalized.ToLowerInvariant();
        }
    }
}
