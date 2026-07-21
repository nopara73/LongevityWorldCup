using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EventBoardBrowserTests
{
    [Fact]
    public async Task CustomEventRowChrome_TogglesDetailsWithoutStealingInteractiveClicks()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 635, Height = 360 }
        });
        var eventsBody = JsonSerializer.Serialize(new[]
        {
            new
            {
                Id = "custom-row-hit-area",
                Type = 6,
                Text = "Launch [details](https://example.test/details)\n\nThe full announcement remains available here.",
                OccurredAt = DateTimeOffset.UtcNow,
                Relevance = 10
            }
        });
        await RouteEventBoardDependenciesAsync(context, eventsBody);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error") errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(
            "/events",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var row = page.Locator("#eventsTable tbody tr.custom-event-row");
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var expander = row.Locator(".custom-event-expander");
        var details = page.Locator("#eventsTable tbody tr.custom-event-details");

        await row.Locator(".col-date").ClickAsync();
        Assert.True(await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));
        Assert.Equal("true", await expander.GetAttributeAsync("aria-expanded"));
        await details.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await row.Locator(".col-date").ClickAsync();
        Assert.False(await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));

        await expander.ClickAsync();
        Assert.True(await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));

        var titleLink = row.Locator(".custom-event-title a");
        Assert.Equal(1, await titleLink.CountAsync());
        await titleLink.EvaluateAsync("element => element.addEventListener('click', event => event.preventDefault(), { once: true })");
        await titleLink.ClickAsync();
        Assert.True(await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task AthleteEmbed_ImprovementLeaderEventStartsSentenceOnNameLine()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 635, Height = 160 }
        });
        await RouteEventBoardDependenciesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.StartsWith("Error fetching athletes:", StringComparison.Ordinal))
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync(
            "/event-board-embed.html?athlete=majoros_gabor&rows=all&viewAll=false&linkNames=false&theme=dark",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync("#eventsTable tbody tr.main-row .event-message-cell");

        var text = (await page.Locator(".event-message-cell").First.InnerTextAsync()).Replace('\u00A0', ' ');
        Assert.Contains("Majoros Gábor (#99) took 1st place in Pheno Improvement", text);

        var diagnosticsJson = await page.EvaluateAsync<string>(
            """
            () => {
                const cell = document.querySelector('.event-message-cell');
                const name = cell?.querySelector('.name-and-rank');
                let textNode = name?.nextSibling || null;
                while (textNode && (textNode.nodeType !== Node.TEXT_NODE || !textNode.textContent.trim())) {
                    textNode = textNode.nextSibling;
                }

                const start = textNode ? textNode.textContent.search(/\S/) : -1;
                const range = document.createRange();
                if (textNode && start >= 0) {
                    range.setStart(textNode, start);
                    range.setEnd(textNode, Math.min(textNode.textContent.length, start + 4));
                }

                const nameRect = name?.getBoundingClientRect();
                const wordRect = textNode && start >= 0 ? range.getBoundingClientRect() : null;
                const cellRect = cell?.getBoundingClientRect();
                return JSON.stringify({
                    text: cell?.innerText || '',
                    display: cell ? getComputedStyle(cell).display : '',
                    cellWidth: cellRect?.width || 0,
                    nameTop: nameRect?.top || 0,
                    wordTop: wordRect?.top || 0,
                    nameRight: nameRect?.right || 0,
                    wordLeft: wordRect?.left || 0
                });
            }
            """);

        using var diagnostics = JsonDocument.Parse(diagnosticsJson);
        var root = diagnostics.RootElement;
        var display = root.GetProperty("display").GetString();
        var nameTop = root.GetProperty("nameTop").GetDouble();
        var wordTop = root.GetProperty("wordTop").GetDouble();

        Assert.Equal("block", display);
        Assert.True(
            Math.Abs(nameTop - wordTop) < 3,
            $"Expected the sentence to continue on the athlete-name line. Diagnostics: {diagnosticsJson}");
        Assert.Empty(errors);
    }

    private static async Task RouteEventBoardDependenciesAsync(IBrowserContext context, string? eventsBody = null)
    {
        eventsBody ??=
            """
            [
              {
                "Id": "event-improvement-leader",
                "Type": 12,
                "Text": "slug[majoros_gabor] clock[pheno] place[1] prev[nopara73] improvement[-9.15] ageReduction[-12.15]",
                "OccurredAt": "2026-06-29T08:00:00Z",
                "Relevance": 10
              }
            ]
            """;

        await context.RouteAsync("**/*", async route =>
        {
            if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var uri))
            {
                await route.ContinueAsync();
                return;
            }

            if ((uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.IsLoopback)
            {
                if (uri.AbsolutePath.Equals("/api/events", StringComparison.OrdinalIgnoreCase))
                {
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 200,
                        ContentType = "application/json",
                        Body = eventsBody
                    });
                    return;
                }

                if (uri.AbsolutePath.Equals("/api/data/athletes", StringComparison.OrdinalIgnoreCase))
                {
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 200,
                        ContentType = "application/json",
                        Body =
                            """
                            [
                              {
                                "Key": "majoros_gabor",
                                "Name": "Majoros Gábor",
                                "CurrentPlacement": 99
                              },
                              {
                                "Key": "nopara73",
                                "Name": "nopara73",
                                "CurrentPlacement": 19
                              }
                            ]
                            """
                    });
                    return;
                }

                await route.ContinueAsync();
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
}
