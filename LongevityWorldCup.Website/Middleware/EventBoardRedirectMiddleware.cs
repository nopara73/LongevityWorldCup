namespace LongevityWorldCup.Website.Middleware
{
    /// <summary>
    /// By default, /event-board-embed.html?athlete={slug} redirects permanently
    /// to the canonical /athlete/{slug} page.
    /// If and only if ?embed=1 is present, it will serve the embed content
    /// (so your modals/iframes work). This opt-in prevents Google from
    /// indexing the partial "thingy" by accident.
    /// Always sets X-Robots-Tag noindex on the embed.
    /// </summary>
    public class EventBoardRedirectMiddleware
    {
        private readonly RequestDelegate _next;
        public EventBoardRedirectMiddleware(RequestDelegate next) => _next = next;

        public async Task Invoke(HttpContext ctx)
        {
            var path = ctx.Request.Path.Value;
            if (!string.Equals(path, "/event-board-embed.html", StringComparison.OrdinalIgnoreCase))
            {
                await _next(ctx);
                return;
            }

            var athlete = ctx.Request.Query["athlete"].ToString();
            var embed = ctx.Request.Query["embed"].ToString();

            // no athlete slug -> 404
            if (string.IsNullOrWhiteSpace(athlete))
            {
                ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet";
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // if ?embed=1 then allow embed rendering
            if (embed == "1")
            {
                ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet";
                await _next(ctx);
                return;
            }

            // otherwise redirect to canonical athlete page
            ctx.Response.Redirect($"/athlete/{athlete}", permanent: true);
        }
    }
}
