using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class RedditDesignerBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(360)]
    public async Task RedditDestination_PreviewsPersistsAndQueuesIndependently(int width)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = width, Height = 850 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var payload = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/custom-events", async route =>
        {
            using var json = JsonDocument.Parse(route.Request.PostData!);
            payload.TrySetResult(json.RootElement.Clone());
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200, ContentType = "application/json",
                Body = JsonSerializer.Serialize(new { success = true, eventId = new string('a', 32), queuedTargets = new[] { "reddit" } })
            });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/custom-event-designer.html", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        foreach (var id in new[] { "sendWebpage", "sendSlack", "sendX" }) await page.Locator("#" + id).UncheckAsync();
        await page.GetByLabel("Reddit", new PageGetByLabelOptions { Exact = true }).CheckAsync();
        await page.Locator("#titleInput").FillAsync("An LWC announcement!");
        await page.Locator("#contentInput").FillAsync("Keep the complete announcement copy.");
        var preview = page.Locator(".preview-block").Filter(new LocatorFilterOptions { HasText = "Reddit Preview" });
        await preview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Contains("r/LongevityWorldCup", await preview.InnerTextAsync());
        Assert.Contains("Keep the complete announcement copy.", await preview.InnerTextAsync());
        Assert.Equal(1, await preview.Locator("svg").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));

        var screenshotDirectory = Environment.GetEnvironmentVariable("LWC_REDDIT_SCREENSHOT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(screenshotDirectory))
        {
            Directory.CreateDirectory(screenshotDirectory);
            await preview.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(screenshotDirectory, $"reddit-preview-{width}.png") });
        }

        await page.Locator("#designerSecretInput").FillAsync("test-only-designer-key");
        await page.Locator("#postEventBtn").ClickAsync();
        var sent = await payload.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(sent.GetProperty("sendToReddit").GetBoolean());
        foreach (var name in new[] { "sendToWebpage", "sendToSlack", "sendToX", "sendToThreads", "sendToFacebook", "sendToMastodon", "sendToNostr" })
            Assert.False(sent.GetProperty(name).GetBoolean());
        await page.ReloadAsync();
        Assert.True(await page.Locator("#sendReddit").IsCheckedAsync());
        Assert.False(await page.Locator("#sendX").IsCheckedAsync());
    }
}
