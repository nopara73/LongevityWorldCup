using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProofPreviewBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static string Original(int n) => $"/athletes/michael_lustgarten/proof_preview_test_{n}.png?v=original-{n}";
    private const string PreviewPattern = "**/api/proof-previews/michael_lustgarten/proof_preview_test_*.png?*";
    private const string OriginalPattern = "**/athletes/michael_lustgarten/proof_preview_test_*.png?*";

    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task GalleryLoadsOnlyPreviewsAndReaderNavigationAlwaysLoadsOriginals(int width)
    {
        var requests = new ConcurrentQueue<string>();
        await using var context = await CreateContextAsync(width, requests);
        var page = await OpenAsync(context);
        var proofs = page.Locator("#proofsGallery img");
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('#proofsGallery img')].every(i => i.complete && i.naturalWidth === 90)");
        Assert.Empty(requests);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));

        await proofs.First.ClickAsync();
        var viewer = page.Locator("#athleteImageViewer");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
        await Assertions.Expect(viewer.Locator(".image-viewer-stage img")).ToHaveAttributeAsync("src", Original(1));
        Assert.Equal(2400, await viewer.Locator(".image-viewer-stage img").EvaluateAsync<int>("i => i.naturalWidth"));
        Assert.Equal([Original(1)], requests.Select(u => new Uri(u).PathAndQuery));

        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 2 of 3");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
        await Assertions.Expect(viewer.Locator(".image-viewer-stage img")).ToHaveAttributeAsync("src", Original(2));
        Assert.Equal([Original(1), Original(2)], requests.Select(u => new Uri(u).PathAndQuery));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(viewer).ToBeHiddenAsync();
        await Assertions.Expect(proofs.Nth(1)).ToBeFocusedAsync();
        await page.GoForwardAsync();
        await Assertions.Expect(viewer).ToBeVisibleAsync();
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
        await Assertions.Expect(viewer.Locator(".image-viewer-stage img")).ToHaveAttributeAsync("src", Original(2));
        Assert.Equal(Original(2), await page.EvaluateAsync<string>("history.state.enlargedImage.source"));
    }

    [Fact]
    public async Task FailedPreviewFallsBackToOriginalAndStillOpensInReader()
    {
        var requests = new ConcurrentQueue<string>();
        await using var context = await CreateContextAsync(390, requests, failPreview: true);
        var page = await OpenAsync(context);
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('#proofsGallery img')].every(i => i.complete && i.naturalWidth === 2400)");
        await Assertions.Expect(page.Locator("#proofsGallery img").First).ToHaveAttributeAsync("src", Original(1));
        await page.Locator("#proofsGallery img").First.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var viewer = page.Locator("#athleteImageViewer");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
        await Assertions.Expect(viewer.Locator(".image-viewer-stage img")).ToHaveAttributeAsync("src", Original(1));
    }

    [Theory]
    [InlineData("anton-eich", 390)]
    [InlineData("roman-v", 1280)]
    [InlineData("ricardo-di-lazzaro-filho", 390)]
    public async Task RealAthleteGalleriesUseBoundedPreviewsBeforeOpeningOriginals(string slug, int width)
    {
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        var page = await context.NewPageAsync();
        var originals = new ConcurrentQueue<string>();
        var previews = new ConcurrentQueue<IResponse>();
        page.Request += (_, request) =>
        {
            var path = new Uri(request.Url).AbsolutePath;
            if (path.StartsWith("/athletes/") && Path.GetFileName(path).StartsWith("proof_")) originals.Enqueue(request.Url);
        };
        page.Response += (_, response) =>
        {
            if (new Uri(response.Url).AbsolutePath.StartsWith("/api/proof-previews/")) previews.Enqueue(response);
        };
        await page.GotoAsync($"/athlete/{slug}");
        await page.Locator("#athlete-proofs").ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync("""
            () => {
                const images = [...document.querySelectorAll('#proofsGallery .proof-item:not([hidden]) img')];
                return images.length > 0 && images.every(i => i.complete && i.naturalWidth > 0);
            }
            """);
        Assert.Empty(originals);
        Assert.NotEmpty(previews);
        foreach (var response in previews)
        {
            Assert.True(response.Ok, response.Url);
            Assert.InRange((await response.BodyAsync()).Length, 1, 128 * 1024);
        }
        var first = page.Locator("#proofsGallery img").First;
        var original = await first.GetAttributeAsync("data-full-src");
        Assert.StartsWith("/athletes/", original);
        await first.ClickAsync();
        await Assertions.Expect(page.Locator("#athleteImageViewer")).ToHaveAttributeAsync("data-image-state", "ready");
        await Assertions.Expect(page.Locator("#athleteImageViewer .image-viewer-stage img")).ToHaveAttributeAsync("src", original!);
        Assert.Contains(originals, url => new Uri(url).PathAndQuery == original);
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, ConcurrentQueue<string> requests, bool failPreview = false)
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = ColorScheme.Dark, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            foreach (var athlete in athletes.OfType<JsonObject>())
                athlete["Proofs"] = new JsonArray(Original(1), Original(2), Original(3));
        });
        await context.RouteAsync(PreviewPattern, route => route.FulfillAsync(new()
        {
            Status = failPreview ? 503 : 200, ContentType = "image/svg+xml", Body = failPreview ? "" : Svg(90, 120)
        }));
        await context.RouteAsync(OriginalPattern, route =>
        {
            requests.Enqueue(route.Request.Url);
            return route.FulfillAsync(new() { Status = 200, ContentType = "image/svg+xml", Body = Svg(2400, 3200) });
        });
        return context;
    }

    private static string Svg(int width, int height) =>
        $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}'><rect width='100%' height='100%' fill='white'/><text x='10' y='30'>Synthetic proof</text></svg>";

    private static async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await page.WaitForFunctionAsync("() => document.querySelector('#leaderboardStatus')?.textContent === 'Leaderboard loaded.'");
        await page.EvaluateAsync("window.openAthleteModalBySlug('michael-lustgarten',{suppressGuessMyAge:true})");
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content:not(.is-loading) #proofsGallery')");
        await page.Locator("#athlete-proofs").ScrollIntoViewIfNeededAsync();
        return page;
    }
}
