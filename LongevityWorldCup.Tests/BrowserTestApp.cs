using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using System.Net;
using System.Net.Sockets;

namespace LongevityWorldCup.Tests;

internal sealed class BrowserTestApp(TestWebApplicationFactory factory, HttpClient client, Uri baseAddress) : IAsyncDisposable
{
    public Uri BaseAddress { get; } = baseAddress;

    public static async Task<BrowserTestApp> StartAsync()
    {
        var port = GetFreeTcpPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}");
        var factory = new TestWebApplicationFactory();
        factory.UseKestrel(port);
        factory.StartServer();

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = baseAddress
        });

        using var response = await client.GetAsync("/health");
        response.EnsureSuccessStatusCode();

        return new BrowserTestApp(factory, client, baseAddress);
    }

    public static async Task RouteExternalResourcesAsync(IBrowserContext context)
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
                await route.ContinueAsync();
                return;
            }

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
        });
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
