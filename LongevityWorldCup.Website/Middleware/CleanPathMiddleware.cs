namespace LongevityWorldCup.Website.Middleware
{
    public class CleanPathMiddleware
    {
        private readonly RequestDelegate _next;

        public CleanPathMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var rawPath = context.Request.Path.Value;
            var normalizedPath = RouteCanonicalization.NormalizePath(rawPath).ToLowerInvariant();
            var canonicalPath = RouteCanonicalization.GetCanonicalPath(rawPath);

            if (!string.Equals(normalizedPath, canonicalPath, StringComparison.Ordinal))
            {
                context.Response.Redirect($"{canonicalPath}{context.Request.QueryString}", permanent: true);
                return;
            }

            // Do not rewrite real files (e.g. .css, .js).
            if (canonicalPath.Length > 1 && !canonicalPath.Contains('.'))
            {
                switch (canonicalPath)
                {
                    case "/events":
                        context.Request.Path = "/event-board/event-board.html";
                        break;

                    case "/leaderboard":
                        context.Request.Path = "/leaderboard/leaderboard.html";
                        break;

                    case "/media":
                        context.Request.Path = "/misc-pages/media.html";
                        break;

                    case "/pheno-age":
                        context.Request.Path = "/onboarding/pheno-age.html";
                        break;

                    case "/bortz-age":
                        context.Request.Path = "/onboarding/bortz-age.html";
                        break;

                    case "/play":
                        context.Request.Path = "/play/menu.html";
                        break;

                    case "/join":
                        context.Request.Path = "/onboarding/join-game.html";
                        break;

                    case "/apply":
                        context.Request.Path = "/onboarding/convergence.html";
                        break;

                    case "/review":
                        context.Request.Path = "/onboarding/application-review.html";
                        break;

                    case "/proofs":
                        context.Request.Path = "/play/proof-upload.html";
                        break;

                    case "/select-athlete":
                        context.Request.Path = "/play/character-selection.html";
                        break;

                    case "/dashboard":
                        context.Request.Path = "/play/character-customization.html";
                        break;

                    case "/edit-profile":
                        context.Request.Path = "/play/edit-profile.html";
                        break;

                    case "/unsubscribe":
                        context.Request.Path = "/unsubscribe.html";
                        break;
                }
            }

            await _next(context);
        }
    }
}
