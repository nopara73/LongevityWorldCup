using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LeaderboardProofViewerBrowserTests
{
    [Fact]
    public async Task ProofViewer_NavigatesWithButtonsAndKeyboardAndRestoresCurrentProofFocus()
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync(
            "/",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof populateProofsGallery === 'function'");
        await page.EvaluateAsync(
            """
            () => {
                const gallery = document.createElement('div');
                gallery.id = 'proofsGallery';
                gallery.className = 'proofs-gallery';
                document.body.appendChild(gallery);
                populateProofsGallery({
                    Proofs: [
                        '/athletes/christopher_yamba/proof_1.webp',
                        '/athletes/christopher_yamba/proof_2.webp',
                        '/athletes/christopher_yamba/proof_3.webp'
                    ]
                });
            }
            """);

        await page.EvaluateAsync(
            """
            () => {
                const firstProof = document.querySelector('#proofsGallery .proof-item img');
                firstProof.focus();
                openEnlargedView(firstProof);
            }
            """);
        var viewer = page.Locator(".enlarged-portrait.show");
        await viewer.WaitForAsync();

        var previousButton = viewer.Locator(".image-nav--previous");
        var nextButton = viewer.Locator(".image-nav--next");
        var closeButton = viewer.Locator(".close-btn");
        var position = viewer.Locator(".image-position");

        Assert.Equal("Proof 1 of 3", await position.InnerTextAsync());
        Assert.True(await IsFocusedAsync(closeButton));
        await AssertTouchTargetAsync(previousButton);
        await AssertTouchTargetAsync(nextButton);

        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("Proof 2 of 3", await position.InnerTextAsync());
        Assert.Contains("/proof_2.webp", await viewer.Locator("img").GetAttributeAsync("src"));

        await page.Keyboard.PressAsync("End");
        Assert.Equal("Proof 3 of 3", await position.InnerTextAsync());

        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("Proof 1 of 3", await position.InnerTextAsync());

        await previousButton.ClickAsync();
        Assert.Equal("Proof 3 of 3", await position.InnerTextAsync());
        Assert.Contains("/proof_3.webp", await viewer.Locator("img").GetAttributeAsync("src"));

        await nextButton.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await IsFocusedAsync(closeButton));

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".enlarged-portrait").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        Assert.Equal(
            "Enlarge proof image 3",
            await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('aria-label') || document.activeElement?.tagName"));
    }

    private static Task<bool> IsFocusedAsync(ILocator locator) =>
        locator.EvaluateAsync<bool>("element => element === document.activeElement");

    private static async Task AssertTouchTargetAsync(ILocator button)
    {
        var box = await button.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.Width >= 44, $"Expected navigation button width of at least 44px, got {box.Width}px.");
        Assert.True(box.Height >= 44, $"Expected navigation button height of at least 44px, got {box.Height}px.");
    }
}
