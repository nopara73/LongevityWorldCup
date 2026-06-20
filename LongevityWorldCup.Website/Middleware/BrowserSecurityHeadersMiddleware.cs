namespace LongevityWorldCup.Website.Middleware;

public sealed class BrowserSecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'; " +
        "script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://www.googletagmanager.com; " +
        "style-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; " +
        "img-src 'self' data: blob: https:; " +
        "font-src 'self' data: https://cdnjs.cloudflare.com; " +
        "connect-src 'self' https:; " +
        "media-src 'self' data: blob: https:; " +
        "worker-src 'self' blob: https://cdnjs.cloudflare.com; " +
        "frame-src 'self' https://www.youtube.com https://www.youtube-nocookie.com; " +
        "frame-ancestors 'self'; " +
        "form-action 'self'; " +
        "manifest-src 'self'";

    private readonly RequestDelegate _next = next;

    public Task Invoke(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers.TryAdd("Content-Security-Policy", ContentSecurityPolicy);
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "SAMEORIGIN");
            headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
            headers.TryAdd("Permissions-Policy", "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");
            headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin-allow-popups");

            return Task.CompletedTask;
        });

        return _next(context);
    }
}
