using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProofReaderRecoveryBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string FirstProof = "/proof-reader-test/page-1.png?v=original-version";
    private const string SecondProof = "/proof-reader-test/page-2.png?v=original-version";
    private const string ThirdProof = "/proof-reader-test/page-3.png?v=original-version";

    [Theory]
    [InlineData("/leaderboard", 390, false)]
    [InlineData("/about", 320, true)]
    [InlineData("/leaderboard", 1280, false)]
    public async Task FailedProof_RetriesTheVersionedImageAndKeepsPositionZoomAndFocus(string path, int width, bool dark)
    {
        await using var context = await CreateContextAsync(width, dark);
        var failSecond = true;
        var secondRequests = new List<string>();
        await context.RouteAsync("**/proof-reader-test/**", async route =>
        {
            if (route.Request.Url.Contains("page-2"))
            {
                secondRequests.Add(route.Request.Url);
                if (failSecond)
                {
                    await FailImageAsync(route);
                    return;
                }
            }
            await FulfillImageAsync(route);
        });
        var page = await OpenProfileAsync(context, path);
        await page.Locator("#proofsGallery img").First.ClickAsync();
        var viewer = page.Locator("#athleteImageViewer");
        await ExpectStateAsync(viewer, "ready");
        var zoomIn = viewer.GetByRole(AriaRole.Button, new() { Name = "Zoom proof in", Exact = true });
        while (await zoomIn.IsEnabledAsync()) await zoomIn.ClickAsync();
        await viewer.Locator(".image-nav--next").ClickAsync();
        await ExpectStateAsync(viewer, "error");
        await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 2 of 3");
        await Assertions.Expect(viewer.Locator(".image-zoom-status")).ToHaveTextAsync("300%");
        await Assertions.Expect(viewer.Locator(".image-viewer-stage")).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(viewer.Locator(".image-viewer-stage img")).ToBeHiddenAsync();
        await Assertions.Expect(viewer.Locator(".image-viewer-hint")).ToBeHiddenAsync();
        foreach (var button in await viewer.Locator(".image-zoom-controls button").AllAsync())
            await Assertions.Expect(button).ToBeDisabledAsync();
        await Assertions.Expect(viewer.Locator(".image-nav--previous")).ToBeEnabledAsync();
        await Assertions.Expect(viewer.Locator(".image-nav--next")).ToBeEnabledAsync();
        var retry = viewer.GetByRole(AriaRole.Button, new() { Name = "Try again", Exact = true });
        var retryBox = await retry.BoundingBoxAsync();
        Assert.NotNull(retryBox);
        Assert.True(retryBox.Width >= 44 && retryBox.Height >= 44);
        var panelBox = await viewer.Locator(".image-load-feedback").BoundingBoxAsync();
        Assert.NotNull(panelBox);
        Assert.InRange(panelBox.X, 0, width - panelBox.Width + 1);
        Assert.InRange(panelBox.Y, 60, 800 - panelBox.Height);

        failSecond = false;
        var attemptsBeforeRetry = secondRequests.Count;
        await retry.ClickAsync();
        await ExpectStateAsync(viewer, "ready");
        Assert.True(secondRequests.Count > attemptsBeforeRetry);
        Assert.All(secondRequests, url => Assert.Equal(SecondProof, new Uri(url).PathAndQuery));
        await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 2 of 3");
        await Assertions.Expect(viewer.Locator(".image-zoom-status")).ToHaveTextAsync("300%");
        await Assertions.Expect(viewer.Locator(".image-viewer-stage")).ToBeFocusedAsync();
        await Assertions.Expect(viewer.Locator(".image-load-feedback")).ToBeHiddenAsync();
        await Assertions.Expect(viewer.Locator(".image-zoom-fit")).ToBeEnabledAsync();
        await Assertions.Expect(viewer.Locator(".image-viewer-hint")).ToBeVisibleAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(viewer).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#proofsGallery img").Nth(1)).ToBeFocusedAsync();
        await page.GoForwardAsync();
        await ExpectStateAsync(viewer, "ready");
        await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 2 of 3");
    }

    [Fact]
    public async Task FailedProof_KeyboardNavigationWorksFromTheRecoveryPanelAtMobileZoom()
    {
        await using var context = await CreateContextAsync(390);
        await context.RouteAsync("**/proof-reader-test/**", route => route.Request.Url.Contains("page-2")
            ? FulfillImageAsync(route) : FailImageAsync(route));
        var page = await OpenProfileAsync(context);
        await page.Locator("#proofsGallery img").First.ClickAsync();
        var viewer = page.Locator("#athleteImageViewer");
        await ExpectStateAsync(viewer, "error");
        await viewer.Locator(".image-load-retry").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await ExpectStateAsync(viewer, "ready");
        await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 2 of 3");
        await Assertions.Expect(viewer.Locator(".image-viewer-stage")).ToBeFocusedAsync();
        // Once an image is ready, arrows in the zoomed stage scroll the proof.
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 2 of 3");
        await viewer.Locator(".close-btn").FocusAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await ExpectStateAsync(viewer, "error");
        await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 1 of 3");
        await viewer.Locator(".close-btn").FocusAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(viewer.Locator(".image-nav--next")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(viewer.Locator(".close-btn")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedProof_CannotReplaceTheNewSelectionOrReopenAClosedReader(bool closeWhileLoading)
    {
        await using var context = await CreateContextAsync(390);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/proof-reader-test/**", async route =>
        {
            if (route.Request.Url.Contains("page-2"))
            {
                started.TrySetResult();
                await release.Task;
            }
            await FulfillImageAsync(route);
            if (route.Request.Url.Contains("page-2")) completed.TrySetResult();
        });
        var page = await OpenProfileAsync(context);
        try
        {
            await page.Locator("#proofsGallery img").Nth(1).ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var viewer = page.Locator("#athleteImageViewer");
            await ExpectStateAsync(viewer, "loading");
            await Assertions.Expect(viewer.Locator(".image-viewer-stage")).ToHaveAttributeAsync("aria-busy", "true");
            await Assertions.Expect(viewer.Locator(".image-load-title")).ToHaveTextAsync("Loading proof…");
            await Assertions.Expect(viewer.Locator(".image-zoom-in")).ToBeDisabledAsync();
            if (closeWhileLoading)
            {
                await viewer.Locator(".close-btn").ClickAsync();
                await Assertions.Expect(viewer).ToBeHiddenAsync();
                await Assertions.Expect(page.Locator("#proofsGallery img").Nth(1)).ToBeFocusedAsync();
            }
            else
            {
                await viewer.Locator(".image-nav--next").ClickAsync();
                await ExpectStateAsync(viewer, "ready");
            }
            release.TrySetResult();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            if (closeWhileLoading)
            {
                await Assertions.Expect(viewer).ToBeHiddenAsync();
                await Assertions.Expect(page.Locator("#proofsGallery img").Nth(1)).ToBeFocusedAsync();
                await page.Locator("#proofsGallery img").Nth(2).ClickAsync();
            }
            await ExpectStateAsync(viewer, "ready");
            await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 3 of 3");
            Assert.Equal(ThirdProof, new Uri(await viewer.Locator(".image-viewer-stage img").GetAttributeAsync("src") ?? "").PathAndQuery);
            await Assertions.Expect(viewer.Locator(".image-load-feedback")).ToBeHiddenAsync();
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task StalledImage_TimesOutAndCanBeRetriedWithoutReloadingTheProfile()
    {
        await using var context = await CreateContextAsync(390);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delay = true;
        await context.RouteAsync("**/proof-reader-test/**", async route =>
        {
            if (delay)
            {
                started.TrySetResult();
                await release.Task;
            }
            await FulfillImageAsync(route);
        });
        var page = await OpenProfileAsync(context);
        await page.Clock.InstallAsync();
        try
        {
            await page.Locator("#proofsGallery img").First.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var viewer = page.Locator("#athleteImageViewer");
            await ExpectStateAsync(viewer, "loading");
            await page.Clock.FastForwardAsync(15001);
            await ExpectStateAsync(viewer, "error");
            await Assertions.Expect(viewer.Locator(".image-load-retry")).ToBeVisibleAsync();
            await Assertions.Expect(viewer.Locator(".image-viewer-stage")).ToHaveAttributeAsync("aria-busy", "false");
            delay = false;
            release.TrySetResult();
            await viewer.Locator(".image-load-retry").ClickAsync();
            await ExpectStateAsync(viewer, "ready");
            await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 1 of 3");
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleImage_RecoveryKeepsOnlyVisibleControlsInTheFocusLoop(bool portrait)
    {
        await using var context = await CreateContextAsync(320, true, singleProof: true);
        await context.RouteAsync("**/proof-reader-test/**", FailImageAsync);
        var page = await OpenProfileAsync(context, "/about");
        if (portrait)
        {
            var photo = page.Locator("#modalProfilePic");
            await photo.EvaluateAsync("(image, url) => image.dataset.fullSrc = url", FirstProof);
            await photo.ClickAsync();
        }
        else await page.Locator("#proofsGallery img").First.ClickAsync();
        var viewer = page.Locator("#athleteImageViewer");
        await ExpectStateAsync(viewer, "error");
        await Assertions.Expect(viewer.Locator(".image-load-title")).ToHaveTextAsync(portrait ? "This image couldn't load" : "This proof couldn't load");
        await Assertions.Expect(viewer.Locator(".image-nav--next")).ToBeHiddenAsync();
        await Assertions.Expect(viewer.Locator(".image-nav--previous")).ToBeHiddenAsync();
        if (!portrait) await Assertions.Expect(viewer.Locator(".image-position")).ToHaveTextAsync("Proof 1 of 1");
        var close = viewer.Locator(".close-btn");
        var retry = viewer.Locator(".image-load-retry");
        await close.FocusAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(retry).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(close).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(viewer.Locator(".image-viewer-stage")).ToBeFocusedAsync();
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, bool dark = false, bool singleProof = false)
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            var response = await route.FetchAsync();
            var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
            foreach (var athlete in athletes.OfType<JsonObject>())
                athlete["Proofs"] = singleProof ? new JsonArray(FirstProof) : new JsonArray(FirstProof, SecondProof, ThirdProof);
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = athletes.ToJsonString() });
        });
        return context;
    }

    private static async Task<IPage> OpenProfileAsync(IBrowserContext context, string path = "/leaderboard")
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("() => typeof window.openAthleteModalBySlug === 'function' && window.modulesReady");
        await page.EvaluateAsync("() => window.modulesReady");
        if (path == "/leaderboard")
            await page.WaitForFunctionAsync("() => document.querySelector('#leaderboardStatus')?.textContent === 'Leaderboard loaded.'");
        await page.EvaluateAsync("window.openAthleteModalBySlug('michael-lustgarten', { suppressGuessMyAge: true })");
        await page.WaitForFunctionAsync("""
            () => {
                const dialog = document.querySelector('#detailsModal');
                const content = dialog?.querySelector('.modal-content');
                return dialog?.style.display === 'block'
                    && content?.dataset.athleteSlug === 'michael-lustgarten'
                    && !content.classList.contains('is-loading')
                    && document.querySelectorAll('#proofsGallery img').length > 0;
            }
            """);
        // The lazy highlights iframe above the proofs resizes after its data arrives.
        // Wait for its final height so that expansion cannot intercept the proof click.
        await page.Locator("#proofsGallery").ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.WaitForFunctionAsync("""
            () => {
                const frame = document.getElementById('events-frame');
                const embedded = frame?.contentDocument;
                const root = embedded?.getElementById('events-embed-root');
                return root && embedded.fonts.status === 'loaded'
                    && embedded.getElementById('events-root')?.getAttribute('aria-busy') === 'false'
                    && parseFloat(frame.style.height) === Math.ceil(root.getBoundingClientRect().height);
            }
            """);
        return page;
    }

    private async Task FulfillImageAsync(IRoute route)
    {
        // Serve a real repository proof through a controlled image endpoint.
        using var client = App.CreateClient();
        var bytes = await client.GetByteArrayAsync("/athletes/christopher_yamba/proof_1.webp");
        await route.FulfillAsync(new() { Status = 200, ContentType = "image/webp", BodyBytes = bytes });
    }

    private static Task FailImageAsync(IRoute route)
        => route.FulfillAsync(new() { Status = 503, ContentType = "text/plain", Body = "Temporarily unavailable" });

    private static Task ExpectStateAsync(ILocator viewer, string state)
        => Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", state);
}
