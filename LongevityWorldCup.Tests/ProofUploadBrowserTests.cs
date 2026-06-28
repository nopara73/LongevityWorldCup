using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ProofUploadBrowserTests
{
    [Fact]
    public async Task ResultUpload_WaitsForDelayedProofHelperBeforeBindingUploadControls()
    {
        await using var app = await StartKestrelAppAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US"
        });

        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                Biomarkers: []
            }));
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [
                    { Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }
                ]
            }));
            """);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await RoutePageDependenciesAsync(page, delayProofHelper: true);

        await page.GotoAsync("/play/proof-upload.html", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'");

        Assert.Contains("Browser Test Athlete", await page.Locator("#character-title").InnerTextAsync());
        Assert.Contains("Upload", await page.Locator("#mainProofInstructions").InnerHTMLAsync());
        Assert.Contains("proofs", await page.Locator("#mainProofInstructions").InnerHTMLAsync());
        Assert.True(await page.Locator("#submitButton").IsDisabledAsync());
        Assert.Empty(errors);
    }

    private static async Task RoutePageDependenciesAsync(IPage page, bool delayProofHelper)
    {
        await page.RouteAsync("**/*", async route =>
        {
            var uri = new Uri(route.Request.Url);
            if (uri.Host is "127.0.0.1" or "localhost")
            {
                if (delayProofHelper && uri.AbsolutePath.Equals("/js/proof-helpers.js", StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Delay(350);
                }

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

    private static async Task<KestrelApp> StartKestrelAppAsync()
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

        return new KestrelApp(factory, client, baseAddress);
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

    private sealed class KestrelApp(TestWebApplicationFactory factory, HttpClient client, Uri baseAddress) : IAsyncDisposable
    {
        public Uri BaseAddress { get; } = baseAddress;

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await factory.DisposeAsync();
        }
    }
}
