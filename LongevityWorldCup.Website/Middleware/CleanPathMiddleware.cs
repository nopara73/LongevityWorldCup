using SQLitePCL;

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
            var raw = context.Request.Path.Value;

            // Normalize: trim slashes, lower-case it
            var normalized = raw?.Trim('/').ToLower();

            // Do not rewrite real files (e.g. .css, .js)
            if (!string.IsNullOrEmpty(normalized) && !normalized.Contains('.'))
            {
                switch (normalized)
                {
                    case "events":
                        context.Request.Path = "/event-board/event-board.html";
                        break;

                    case "leaderboard":
                        context.Request.Path = "/leaderboard/leaderboard.html";
                        break;

                    case "pheno-age":
                        context.Request.Path = "/onboarding/pheno-age.html";
                        break;

                    case "bortz-age":
                        context.Request.Path = "/onboarding/bortz-age.html";
                        break;
                }
            }

            await _next(context);
        }
    }
}
