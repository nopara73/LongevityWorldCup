using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class SwaggerEventsBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task EventsAreDiscoverableAndExecutable(int width)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = width, Height = 900 }
        });
        // Keep Swagger's interactive requests on this isolated app, rather than its production server URL.
        await context.RouteAsync("**/swagger/v1/swagger.json", async route =>
        {
            var response = await route.FetchAsync();
            var document = JsonNode.Parse(await response.TextAsync())!.AsObject();
            document["servers"] = new JsonArray(new JsonObject { ["url"] = App.BaseAddress.ToString().TrimEnd('/') });
            await route.FulfillAsync(new() { Response = response, Body = document.ToJsonString() });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/swagger/index.html", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        // Must be visible without filtering or manually overriding Swagger's tag limit.
        var operation = page.Locator("#operations-Events-listEvents");
        await operation.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await operation.Locator(".opblock-summary-control").ClickAsync();
        await operation.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }).WaitForAsync();
        await operation.Locator(".renderedMarkdown table tbody tr").First.WaitForAsync();
        Assert.Equal(14, await operation.Locator(".renderedMarkdown table tbody tr").CountAsync());
        Assert.Contains("TestResultAccepted", await operation.InnerTextAsync());
        Assert.Contains("first observed public announcement", await operation.InnerTextAsync());
        await AssertFitsViewportAsync(page);

        var artifactDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.artifacts/events-api"));
        Directory.CreateDirectory(artifactDirectory);
        await operation.Locator(".renderedMarkdown table").ScrollIntoViewIfNeededAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(artifactDirectory, $"swagger-events-{width}.png") });

        await operation.GetByRole(AriaRole.Button, new() { Name = "Execute", Exact = true }).ClickAsync();
        await operation.Locator(".live-responses-table").WaitForAsync();
        Assert.Equal("200", (await operation.Locator(".live-responses-table tbody .response-col_status").InnerTextAsync()).Trim());
        Assert.Contains("/api/events", await operation.Locator(".request-url").InnerTextAsync());
        await AssertFitsViewportAsync(page);
    }

    private static async Task AssertFitsViewportAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<string>("""
            () => document.documentElement.scrollWidth <= innerWidth ? '' : JSON.stringify(
                [...document.querySelectorAll('body *')].filter(element => element.getBoundingClientRect().right > innerWidth)
                    .slice(0, 12).map(element => ({tag: element.tagName, class: element.className, width: element.getBoundingClientRect().width})))
            """);
        Assert.True(string.IsNullOrEmpty(overflow), overflow);
    }
}
