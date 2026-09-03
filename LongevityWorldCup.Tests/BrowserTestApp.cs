using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;

namespace LongevityWorldCup.Tests;

public sealed class BrowserTestApp(TestWebApplicationFactory factory, HttpClient client, Uri baseAddress) : IAsyncDisposable
{
    private static readonly Regex StubbedThirdPartyUrl = new(
        @"^https?://(?:cdnjs\.cloudflare\.com|cdn\.jsdelivr\.net|www\.googletagmanager\.com|ipapi\.co)(?:/|$)|^https://github\.com/user-attachments/",
        RegexOptions.IgnoreCase);
    private static int s_nextBrowserContextAddress;

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
        var clientAddress = $"2001:db8::{Interlocked.Increment(ref s_nextBrowserContextAddress):x}";
        if (beforeLoopbackContinueAsync is null)
        {
            await context.RouteAsync(
                "**/api/site-statistics/event",
                route => ContinueWithClientAddressAsync(route, clientAddress));
            await context.RouteAsync(StubbedThirdPartyUrl, FulfillExternalResourceAsync);
            return;
        }

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

                if (uri.AbsolutePath.Equals("/api/site-statistics/event", StringComparison.OrdinalIgnoreCase))
                    await ContinueWithClientAddressAsync(route, clientAddress);
                else
                    await route.ContinueAsync();
                return;
            }

            if (StubbedThirdPartyUrl.IsMatch(uri.AbsoluteUri))
                await FulfillExternalResourceAsync(route);
            else
                await route.ContinueAsync();
        });
    }

    private static Task ContinueWithClientAddressAsync(IRoute route, string clientAddress)
    {
        var headers = route.Request.Headers.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        headers["X-Forwarded-For"] = clientAddress;
        return route.ContinueAsync(new RouteContinueOptions { Headers = headers });
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
