using System.Runtime.ExceptionServices;
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
        catch (Exception startupFailure)
        {
            var cleanupFailures = new List<Exception>();
            try
            {
                client?.Dispose();
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
            }

            try
            {
                await factory.DisposeAsync();
            }
            catch (Exception ex)
            {
                cleanupFailures.Add(ex);
            }

            if (cleanupFailures.Count > 0)
            {
                throw new AggregateException(
                    "The browser application failed to start and cleanup also failed.",
                    new[] { startupFailure }.Concat(cleanupFailures));
            }

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
        Exception? clientFailure = null;
        try
        {
            client.Dispose();
        }
        catch (Exception ex)
        {
            clientFailure = ex;
        }

        try
        {
            await factory.DisposeAsync();
        }
        catch (Exception factoryFailure) when (clientFailure is not null)
        {
            throw new AggregateException(
                "The browser client and application factory both failed to dispose.",
                clientFailure,
                factoryFailure);
        }

        if (clientFailure is not null)
            ExceptionDispatchInfo.Capture(clientFailure).Throw();
    }
}
