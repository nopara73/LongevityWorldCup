using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProofGalleryBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, false)]
    [InlineData(1280, true)]
    public async Task LongGallery_ShowsNumberedPreviewsWithoutAnInnerScroll(int width, bool dark)
    {
        await using var context = await CreateContextAsync(14, width, dark);
        var page = await OpenProfileAsync(context);
        await Assertions.Expect(page.Locator("#proofsGallery .proof-item:visible")).ToHaveCountAsync(6);
        Assert.Equal(Enumerable.Range(1, 6).Select(i => $"Proof {i}"),
            await page.Locator("#proofsGallery .proof-item:visible .proof-number").AllTextContentsAsync());
        var gallery = page.Locator("#proofsGallery");
        Assert.True(await gallery.EvaluateAsync<bool>("e => e.scrollHeight <= e.clientHeight + 1"));
        Assert.InRange((await gallery.BoundingBoxAsync())!.Height, 1, 700);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Show all 14 proofs", Exact = true }))
            .ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task ExpandingAndCollapsing_KeepsOrderFocusAndTheCurrentUrl()
    {
        await using var context = await CreateContextAsync(14);
        var page = await OpenProfileAsync(context);
        var url = page.Url;
        var toggle = page.Locator(".proofs-toggle");
        await Assertions.Expect(toggle).ToBeVisibleAsync();
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#proofsGallery .proof-item:visible")).ToHaveCountAsync(14);
        Assert.Equal(Enumerable.Range(1, 14).Select(i => $"Proof {i}"),
            await page.Locator("#proofsGallery .proof-number").AllTextContentsAsync());
        Assert.Equal(Enumerable.Range(1, 14).Select(ProofUrl),
            await page.Locator("#proofsGallery img").EvaluateAllAsync<string[]>("images => images.map(i => i.getAttribute('src'))"));
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(page.Locator("#proofsGallery .proof-item:visible")).ToHaveCountAsync(6);
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        Assert.Equal(url, page.Url);
    }

    [Fact]
    public async Task LeavingTheReader_RevealsAndFocusesTheSelectedProof()
    {
        await using var context = await CreateContextAsync(14);
        var page = await OpenProfileAsync(context);
        await page.Locator("#proofsGallery img").First.ClickAsync();
        var viewer = page.Locator("#athleteImageViewer");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 14 of 14");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
        await page.GoBackAsync();
        await Assertions.Expect(viewer).ToBeHiddenAsync();
        var lastProof = page.Locator("#proofsGallery img").Last;
        await Assertions.Expect(lastProof).ToBeFocusedAsync();
        var bounds = (await page.Locator("#proofsGallery .proof-item").Last.BoundingBoxAsync())!;
        Assert.InRange(bounds.Y, 0, 900 - bounds.Height);
        await Assertions.Expect(page.Locator(".proofs-toggle")).ToHaveAttributeAsync("aria-expanded", "true");
        await page.GoForwardAsync();
        await Assertions.Expect(viewer).ToBeVisibleAsync();
        await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 14 of 14");
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 1 of 14");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(viewer).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#proofsGallery img").First).ToBeFocusedAsync();
        var firstBounds = (await page.Locator("#proofsGallery .proof-item").First.BoundingBoxAsync())!;
        var headerBounds = (await page.Locator("#detailsModal .modal-sticky-header").BoundingBoxAsync())!;
        Assert.InRange(firstBounds.Y, headerBounds.Y + headerBounds.Height - 1, 900 - firstBounds.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    public async Task ShortAndEmptySets_DoNotNeedAnExpansionControl(int count)
    {
        await using var context = await CreateContextAsync(count);
        var page = await OpenProfileAsync(context);
        await Assertions.Expect(page.Locator(".proofs-toggle")).ToBeHiddenAsync();
        Assert.Equal(count, await page.Locator("#proofsGallery img").CountAsync());
        if (count == 0)
            await Assertions.Expect(page.Locator(".proofs-empty")).ToHaveTextAsync("No public proofs available yet.");
    }

    private static string ProofUrl(int number) => $"/public-proof-gallery-test/page-{number}.svg?v=original";

    private async Task<IBrowserContext> CreateContextAsync(int count, int width = 390, bool dark = false)
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            var response = await route.FetchAsync();
            var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
            foreach (var athlete in athletes.OfType<JsonObject>())
                athlete["Proofs"] = new JsonArray(Enumerable.Range(1, count).Select(i => JsonValue.Create(ProofUrl(i))).ToArray<JsonNode?>());
            await route.FulfillAsync(new() { Response = response, Body = athletes.ToJsonString() });
        });
        await context.RouteAsync("**/public-proof-gallery-test/**", route => route.FulfillAsync(new()
        {
            Status = 200, ContentType = "image/svg+xml",
            Body = "<svg xmlns='http://www.w3.org/2000/svg' width='300' height='420'><rect width='300' height='420' fill='white'/><text x='30' y='50'>Synthetic proof</text></svg>"
        }));
        return context;
    }

    private static async Task<IPage> OpenProfileAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await page.WaitForFunctionAsync("() => document.querySelector('#leaderboardStatus')?.textContent === 'Leaderboard loaded.'");
        await page.EvaluateAsync("window.openAthleteModalBySlug('michael-lustgarten',{suppressGuessMyAge:true})");
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content:not(.is-loading) #proofsGallery')");
        await page.Locator("#athlete-proofs").ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync("() => document.fonts.ready");
        return page;
    }
}
