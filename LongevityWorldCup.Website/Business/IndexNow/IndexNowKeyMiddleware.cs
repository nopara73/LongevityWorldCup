using System.Text;

namespace LongevityWorldCup.Website.Business.IndexNow;

public sealed class IndexNowKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        // Do not resolve the durable ledger for ordinary requests. A damaged ledger disables
        // notifications/key verification without affecting the website's public content.
        if (!path.EndsWith(".txt", StringComparison.Ordinal) || !IndexNowStateStore.IsValidKey(path.TrimStart('/')[..^4]))
        {
            await next(context);
            return;
        }
        IndexNowStateStore store;
        try { store = context.RequestServices.GetRequiredService<IndexNowStateStore>(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            await next(context);
            return;
        }
        if (!string.Equals(context.Request.Path.Value, $"/{store.Key}.txt", StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "public, max-age=3600";
        context.Response.Headers["X-Robots-Tag"] = "noindex";
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers.Allow = "GET, HEAD";
            return;
        }

        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.ContentLength = Encoding.UTF8.GetByteCount(store.Key);
        if (HttpMethods.IsGet(context.Request.Method))
            await context.Response.WriteAsync(store.Key, Encoding.UTF8, context.RequestAborted);
    }
}
