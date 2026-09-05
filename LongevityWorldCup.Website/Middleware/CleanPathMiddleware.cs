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

            if (!string.Equals(originalPath.Value, page.CanonicalPath, StringComparison.Ordinal))
            {
                var location = context.Request.PathBase.Add(new PathString(page.CanonicalPath)).ToUriComponent()
                    + context.Request.QueryString.ToUriComponent();
                context.Response.Redirect(location, permanent: true,
                    preserveMethod: !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method));
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
