using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LeaderboardProofViewerBrowserTests
{
    [Fact]
    public async Task ProofViewer_StopsAtEndsAndRestoresCurrentProofFocus()
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
                const modal = document.getElementById('detailsModal');
                modal.style.display = 'block';
                const gallery = document.createElement('div');
                gallery.id = 'proofsGallery';
                gallery.className = 'proofs-gallery';
                modal.querySelector('.modal-content').appendChild(gallery);
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

        Assert.Equal("region", await viewer.GetAttributeAsync("role"));
        Assert.Equal(1, await page.Locator("#detailsModal > #athleteImageViewer").CountAsync());
        Assert.Equal(1, await page.Locator("[role=dialog]:visible").CountAsync());
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.inert"));

        var previousButton = viewer.Locator(".image-nav--previous");
        var nextButton = viewer.Locator(".image-nav--next");
        var closeButton = viewer.Locator(".close-btn");
        var position = viewer.Locator(".image-position");
        var zoomOutButton = viewer.Locator(".image-zoom-out");
        var zoomInButton = viewer.Locator(".image-zoom-in");
        var fitButton = viewer.Locator(".image-zoom-fit");

        await page.WaitForFunctionAsync(
            "() => { const image = document.querySelector('.enlarged-portrait.show img'); return image?.complete && image.naturalWidth > 0; }");
        var mobileLayout = await viewer.EvaluateAsync<ProofViewerLayoutDiagnostics>(
            """
            element => {
                const stage = element.querySelector('.image-viewer-stage');
                const previous = element.querySelector('.image-nav--previous');
                const next = element.querySelector('.image-nav--next');
                const hint = element.querySelector('.image-viewer-hint');
                const zoomStatus = element.querySelector('.image-zoom-status');
                const stageRect = stage.getBoundingClientRect();
                const previousRect = previous.getBoundingClientRect();
                const nextRect = next.getBoundingClientRect();
                return {
                    StageClientWidth: stage.clientWidth,
                    StageScrollWidth: stage.scrollWidth,
                    StageClientHeight: stage.clientHeight,
                    StageScrollHeight: stage.scrollHeight,
                    NavigationOverlapsStage: previousRect.top < stageRect.bottom || nextRect.top < stageRect.bottom,
                    HintIsVisible: !hint.hidden && getComputedStyle(hint).display !== 'none',
                    ZoomStatus: zoomStatus.textContent.trim()
                };
            }
            """);

        Assert.Equal("Proof 1 of 3", await position.InnerTextAsync());
        Assert.Equal("200%", mobileLayout.ZoomStatus);
        Assert.True(mobileLayout.HintIsVisible);
        Assert.True(
            mobileLayout.StageScrollWidth >= mobileLayout.StageClientWidth * 1.9,
            $"Expected a readable zoomed proof width; client={mobileLayout.StageClientWidth}, scroll={mobileLayout.StageScrollWidth}.");
        Assert.True(mobileLayout.StageScrollHeight >= mobileLayout.StageClientHeight);
        Assert.False(mobileLayout.NavigationOverlapsStage);
        Assert.True(await IsFocusedAsync(closeButton));
        await AssertTouchTargetAsync(previousButton);
        await AssertTouchTargetAsync(nextButton);
        await AssertTouchTargetAsync(zoomOutButton);
        await AssertTouchTargetAsync(zoomInButton);
        await AssertTouchTargetAsync(fitButton);
        Assert.False(await previousButton.IsEnabledAsync());
        Assert.True(await nextButton.IsEnabledAsync());

        await page.Keyboard.PressAsync("ArrowLeft");
        Assert.Equal("Proof 1 of 3", await position.InnerTextAsync());
        Assert.Contains("/proof_1.webp", await viewer.Locator("img").GetAttributeAsync("src"));

        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("Proof 2 of 3", await position.InnerTextAsync());
        Assert.Contains("/proof_2.webp", await viewer.Locator("img").GetAttributeAsync("src"));

        await page.Keyboard.PressAsync("End");
        Assert.Equal("Proof 3 of 3", await position.InnerTextAsync());
        Assert.True(await previousButton.IsEnabledAsync());
        Assert.False(await nextButton.IsEnabledAsync());

        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal("Proof 3 of 3", await position.InnerTextAsync());
        Assert.Contains("/proof_3.webp", await viewer.Locator("img").GetAttributeAsync("src"));

        await previousButton.ClickAsync();
        Assert.Equal("Proof 2 of 3", await position.InnerTextAsync());

        await page.Keyboard.PressAsync("End");
        await previousButton.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await IsFocusedAsync(closeButton));

        await page.Keyboard.PressAsync("Escape");
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.inert"));
        Assert.Equal("true", await page.Locator(".enlarged-portrait").GetAttributeAsync("aria-hidden"));
        Assert.Equal(
            "Enlarge proof image 3",
            await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('aria-label') || document.activeElement?.tagName"));
        await page.Locator(".enlarged-portrait").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Hidden });
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("element => element.inert"));
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

    private sealed class ProofViewerLayoutDiagnostics
    {
        public double StageClientWidth { get; set; }
        public double StageScrollWidth { get; set; }
        public double StageClientHeight { get; set; }
        public double StageScrollHeight { get; set; }
        public bool NavigationOverlapsStage { get; set; }
        public bool HintIsVisible { get; set; }
        public string ZoomStatus { get; set; } = "";
    }
}
