using LongevityWorldCup.Website.Business;
using Microsoft.Net.Http.Headers;
using System.Text;

namespace LongevityWorldCup.Website.Middleware;

public sealed class SitemapMiddleware(RequestDelegate next, SitemapService sitemap)
{
    public async Task Invoke(HttpContext context)
    {
        if (!string.Equals(context.Request.Path.Value, "/sitemap.xml", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Response.ContentType = "application/xml; charset=utf-8";
        context.Response.Headers[HeaderNames.CacheControl] = "public, max-age=300, must-revalidate";
        var xml = sitemap.BuildXml();
        context.Response.ContentLength = Encoding.UTF8.GetByteCount(xml);
        if (!HttpMethods.IsHead(context.Request.Method))
            await context.Response.WriteAsync(xml, context.RequestAborted);
    }
}
