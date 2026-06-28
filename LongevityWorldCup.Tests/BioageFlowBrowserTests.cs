using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BioageFlowBrowserTests
{
    [Theory]
    [InlineData("/pheno-age?update=1", "Glucose", "#glucose", "#glucoseUnit", "18.016", "94", "1", "5.2")]
    [InlineData("/bortz-age?update=1", "Hemoglobin A1c (HbA1c)", "#hba1c", "#hba1cUnit", "0.0915", "5.4", "1", "35")]
    public async Task UpdateBioageFlow_UsesSelectedAthleteAndKeepsUnitExamplesInSync(
        string path,
        string biomarkerHeader,
        string inputSelector,
        string unitSelector,
        string initialUnitValue,
        string initialPlaceholder,
        string changedUnitValue,
        string changedPlaceholder)
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
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: []
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

        await page.RouteAsync("**/*", async route =>
        {
            var uri = new Uri(route.Request.Url);
            if (uri.Host is "127.0.0.1" or "localhost")
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

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");

        Assert.Equal("/" + path.TrimStart('/'), new Uri(page.Url).PathAndQuery);
        Assert.Equal("Browser Test Athlete", await page.Locator("#mainPageTitleH2").InnerTextAsync());
        Assert.False(await page.Locator(".lwc-wizard-nav").IsVisibleAsync());
        Assert.False(await page.Locator("#lwcToStep1Btn").IsVisibleAsync());
        Assert.False(await page.Locator("#dobFieldset").IsVisibleAsync());
        Assert.Equal(initialUnitValue, await page.Locator(unitSelector).InputValueAsync());
        Assert.Equal(initialPlaceholder, await page.Locator(inputSelector).GetAttributeAsync("placeholder"));

        Assert.Contains(biomarkerHeader, await ExpandBiomarkerCardAsync(page, inputSelector));
        await page.Locator(unitSelector).SelectOptionAsync(changedUnitValue);

        Assert.Equal(changedUnitValue, await page.Locator(unitSelector).InputValueAsync());
        Assert.Equal(changedPlaceholder, await page.Locator(inputSelector).GetAttributeAsync("placeholder"));

        await page.Locator("button[onclick=\"navigateBackFromBioage()\"]").ClickAsync();
        await page.WaitForURLAsync("**/dashboard");

        Assert.Equal("/dashboard", new Uri(page.Url).AbsolutePath);
        Assert.Empty(errors);
    }

    private static async Task<string> ExpandBiomarkerCardAsync(IPage page, string inputSelector)
    {
        return await page.Locator(inputSelector).EvaluateAsync<string>(
            """
            input => {
                const card = input.closest('.biomarker-card');
                const header = card?.querySelector('.biomarker-card-header');
                if (!card || !header) return '';
                if (!card.classList.contains('active')) header.click();
                return card.classList.contains('active') ? header.textContent.trim() : '';
            }
            """);
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
