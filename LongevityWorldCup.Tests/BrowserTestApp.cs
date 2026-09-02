using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;

namespace LongevityWorldCup.Tests;

public sealed class BrowserTestApp(TestWebApplicationFactory factory, HttpClient client, Uri baseAddress) : IAsyncDisposable
{
    public Uri BaseAddress { get; } = baseAddress;
    public IServiceProvider Services => factory.Services;

    public HttpClient CreateClient()
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = BaseAddress
        });

    public static async Task<BrowserTestApp> StartAsync()
    {
        var factory = new TestWebApplicationFactory();
        HttpClient? client = null;
        try
        {
            factory.UseKestrel(0);
            factory.StartServer();
            var baseAddress = factory.ClientOptions.BaseAddress;

            client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = baseAddress
            });

            using var response = await client.GetAsync("/health");
            response.EnsureSuccessStatusCode();

            return new BrowserTestApp(factory, client, baseAddress);
        }
        catch
        {
            client?.Dispose();
            await factory.DisposeAsync();
            throw;
        }
    }

    public static async Task RouteExternalResourcesAsync(
        IBrowserContext context,
        Func<Uri, Task>? beforeLoopbackContinueAsync = null)
    {
        await context.RouteAsync("**/*", async route =>
        {
            if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var uri))
            {
                await route.ContinueAsync();
                return;
            }

            if ((uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.IsLoopback)
            {
                if (beforeLoopbackContinueAsync is not null)
                    await beforeLoopbackContinueAsync(uri);

                await route.ContinueAsync();
                return;
            }

            await FulfillExternalResourceAsync(route);
        });
    }

    private static async Task FulfillExternalResourceAsync(IRoute route)
    {
        var uri = new Uri(route.Request.Url);
        if (uri.Host.Equals("ipapi.co", StringComparison.OrdinalIgnoreCase))
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"country_code":"HU","region_code":""}"""
            });
            return;
        }

        if (route.Request.ResourceType == "script")
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/javascript",
                Body = uri.AbsolutePath.Contains("/aos/", StringComparison.OrdinalIgnoreCase)
                    ? "window.AOS={init(){},refresh(){}};"
                    : ""
            });
            return;
        }

        await route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = route.Request.ResourceType == "stylesheet" ? "text/css" : "text/plain",
            Body = ""
        });
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }
}
