namespace LongevityWorldCup.Website.Middleware
{
    public class CleanPathMiddleware(RequestDelegate next)
    {
        public async Task Invoke(HttpContext context)
        {
            var originalPath = context.Request.Path;
            context.Items[RouteCanonicalization.CanonicalPathItemKey] =
                RouteCanonicalization.GetCanonicalPath(originalPath.Value);

            if (!RouteCanonicalization.TryGetPage(originalPath, out var page))
            {
                await next(context);
                return;
            }

            if (RouteCanonicalization.RedirectToCanonical(context, page.CanonicalPath))
            {
                return;
            }

            context.Request.Path = page.TemplatePath;
            try
            {
                await next(context);
            }
            finally
            {
                context.Request.Path = originalPath;
            }
        }
    }
}
