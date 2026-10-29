using Microsoft.Playwright;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ApplyStageLifecycleBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static async Task ReadyAsync(IPage page)
        => await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && document.getElementById('nextButton')?.disabled === false");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortraitSave_FractionalOrScaledLayoutKeepsARealSelection(bool scaled)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("""
            scaled => {
                currentStage = 4; goToStage(4);
                // Native 110/150% browser zoom also produces fractional layout
                // bounds. Cover the coordinate mismatch deterministically here.
                const stage = document.querySelector('.convergence-crop-stage');
                if (scaled) stage.style.zoom = '.9';
                else stage.style.width = '300.5px';
            }
            """, scaled);
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInput").SetInputFilesAsync(
            Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
        await page.WaitForFunctionAsync("() => document.getElementById('cropperImage').cropper?.ready && !document.getElementById('cropButton').disabled");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const cropper = document.getElementById('cropperImage').cropper;
                const before = cropper.getData();
                cropper.setData({ x: -100, y: -100, width: 1200, height: 1200 });
                const after = cropper.getData();
                return before.width > 0 && before.height > 0
                    && JSON.stringify(before) === JSON.stringify(after);
            }
            """), "A real crop must be initialized, and attempts to crop outside the image must still be rejected.");
        await page.Locator("#cropButton").ClickAsync();
        await Assertions.Expect(page.Locator("#croppingPart")).ToBeHiddenAsync();
        var dimensions = await page.EvaluateAsync<int[]>("""
            async () => { const image = new Image(); image.src = profilePic; await image.decode(); return [image.naturalWidth, image.naturalHeight]; }
            """);
        Assert.Equal(new[] { 768, 768 }, dimensions);
        Assert.False(await page.EvaluateAsync<bool>("() => applicationProfilePending"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortraitSave_RejectsEmptyExportAndAllowsRetry(bool encodingFailure)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        var before = await page.EvaluateAsync<string?>("() => profilePic");
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInput").SetInputFilesAsync(Path.Combine(webRoot, "assets", "logo.png"));
        await page.WaitForFunctionAsync("() => document.getElementById('cropperImage').cropper?.ready && !document.getElementById('cropButton').disabled");
        await page.EvaluateAsync("""
            encodingFailure => {
                const cropper = document.getElementById('cropperImage').cropper;
                const original = cropper.toCanvas;
                cropper.toCanvas = async () => {
                    cropper.toCanvas = original;
                    const canvas = document.createElement('canvas');
                    if (encodingFailure) canvas.toDataURL = () => 'data:,';
                    else canvas.width = canvas.height = 0;
                    return canvas;
                };
            }
            """, encodingFailure);
        await page.Locator("#cropButton").ClickAsync();
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToContainTextAsync("Profile picture crop failed.");
        Assert.Equal(before, await page.EvaluateAsync<string?>("() => profilePic"));
        Assert.True(await page.EvaluateAsync<bool>("() => applicationProfilePending"));
        await page.Locator("#custom-alert-close").ClickAsync();
        await Assertions.Expect(page.Locator("#cropButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
        await page.Locator("#cropButton").ClickAsync();
        await Assertions.Expect(page.Locator("#croppingPart")).ToBeHiddenAsync();
        Assert.StartsWith("data:image/", await page.EvaluateAsync<string>("() => profilePic"));
    }

    [Fact]
    public async Task UnreadablePortrait_PreservesTheDraftAndAllowsAnotherImage()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.RouteAsync("**/cropperjs@2.3.1/dist/cropper.min.js", route =>
        {
            return route.FulfillAsync(new()
            {
                Path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cropper.min.js"),
                Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
                ContentType = "application/javascript"
            });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        const string draft = "() => JSON.stringify({profilePic, fields: [...document.querySelectorAll('#name, #division, #flag, #why, #personalLink, #mediaContact')].map(field => [field.id, field.value])})";
        var before = await page.EvaluateAsync<string>(draft);
        await page.Locator("#profilePicInput").SetInputFilesAsync(new FilePayload
        {
            Name = "unreadable.png", MimeType = "image/png", Buffer = "not image bytes"u8.ToArray()
        });
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToContainTextAsync("Profile picture upload failed.");
        await page.Locator("#custom-alert-close").ClickAsync();
        await Assertions.Expect(page.Locator("#croppingPart")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#uploadButton")).ToBeFocusedAsync();
        Assert.Equal(before, await page.EvaluateAsync<string>(draft));
        Assert.False(await page.EvaluateAsync<bool>("() => applicationProfilePending"));
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInput").SetInputFilesAsync(Path.Combine(webRoot, "assets", "logo.png"));
        await page.WaitForFunctionAsync("() => document.getElementById('cropperImage').cropper?.ready && !document.getElementById('cropButton').disabled");
        await page.Locator("#cancelProfileCropButton").ClickAsync();
        await Assertions.Expect(page.Locator("#uploadButton")).ToBeFocusedAsync();
        Assert.Equal(before, await page.EvaluateAsync<string>(draft));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortraitCanvasExport_ObsoleteCompletionPreservesTheNewSelection(bool reject)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        var before = await page.EvaluateAsync<string?>("() => profilePic");
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInput").SetInputFilesAsync(Path.Combine(webRoot, "assets", "logo.png"));
        await page.WaitForFunctionAsync("() => document.getElementById('cropperImage').cropper?.ready");
        await page.EvaluateAsync("""
            async reject => {
                const cropper = document.getElementById('cropperImage').cropper;
                const canvas = await cropper.toCanvas();
                window.oldPortraitCropper = cropper;
                cropper.toCanvas = () => new Promise((resolve, fail) => {
                    window.finishOldPortraitExport = () => reject ? fail(new Error('Old export failed')) : resolve(canvas);
                });
            }
            """, reject);
        await page.Locator("#cropButton").ClickAsync();
        await page.WaitForFunctionAsync("() => window.finishOldPortraitExport");
        await page.Locator("#profilePicInput").SetInputFilesAsync(
            Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
        await page.WaitForFunctionAsync("() => { const current = document.getElementById('cropperImage').cropper; return current?.ready && current !== window.oldPortraitCropper; }");
        await page.EvaluateAsync("() => window.finishOldPortraitExport()");
        await Assertions.Expect(page.Locator("#cropButton")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#croppingPart")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#custom-alert")).ToBeHiddenAsync();
        Assert.Equal(before, await page.EvaluateAsync<string?>("() => profilePic"));
        Assert.True(await page.EvaluateAsync<bool>("() => applicationProfilePending"));
        await page.Locator("#cancelProfileCropButton").ClickAsync();
    }

    [Fact]
    public async Task PortraitReadinessWhileHidden_InitializesTheSelectionWhenReturning()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("""
            () => {
                currentStage = 4; goToStage(4);
                const prototype = customElements.get('cropper-image').prototype;
                const original = prototype.$ready;
                window.heldPortraitReady = [];
                prototype.$ready = function (callback) {
                    const ready = original.call(this, callback);
                    if (callback) return ready;
                    return new Promise((resolve, reject) => window.heldPortraitReady.push(() => ready.then(resolve, reject)));
                };
                window.releasePortraitReady = () => {
                    prototype.$ready = original;
                    for (const release of window.heldPortraitReady) release();
                };
            }
            """);
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInput").SetInputFilesAsync(
            Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
        await page.WaitForFunctionAsync("() => window.heldPortraitReady.length > 0 && document.getElementById('cropperImage').cropper");
        await page.Locator("#backButton").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.convergence-crop-stage').clientWidth === 0");
        await page.EvaluateAsync("() => window.releasePortraitReady()");
        await page.WaitForFunctionAsync("() => document.getElementById('cropperImage').cropper.ready");
        await page.Locator("#nextButton").ClickAsync();
        await page.WaitForFunctionAsync("() => { const c = document.getElementById('cropperImage').cropper; return c.ready && !c.disabled && Math.abs(c.getContainerData().width - document.querySelector('.convergence-crop-stage').clientWidth) <= 1; }");
        Assert.True(await page.EvaluateAsync<bool>("() => { const box = document.getElementById('cropperImage').cropper.getCropBoxData(); return box.width > 0 && box.height > 0; }"),
            "Returning after readiness completed offscreen must initialize a real selection before enabling Crop.");
        var pixels = await page.EvaluateAsync<int[]>("""
            async () => {
                const canvas = await document.getElementById('cropperImage').cropper.toCanvas({ width: 64, height: 64 });
                return [canvas.width, canvas.height, canvas.getContext('2d').getImageData(32, 32, 1, 1).data[3]];
            }
            """);
        Assert.Equal(new[] { 64, 64, 255 }, pixels);
        await page.Locator("#cancelProfileCropButton").ClickAsync();
    }

    [Fact]
    public async Task PortraitCrop_NativeTouchDragCannotAlsoNavigateBack()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, HasTouch = true
        });
        await context.RouteAsync("**/cropperjs@2.3.1/dist/cropper.min.js", route =>
        {
            return route.FulfillAsync(new()
            {
                Path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cropper.min.js"),
                Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
                ContentType = "application/javascript"
            });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInput").SetInputFilesAsync(Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
        await page.WaitForFunctionAsync("() => document.getElementById('cropperImage').cropper?.ready");
        await page.Locator(".cropper-face").ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync("() => { document.getElementById('cropperImage').cropper.setCropBoxData({width:210,height:210,left:10,top:10}); }");
        var before = await page.EvaluateAsync<double>("() => document.getElementById('cropperImage').cropper.getData().x");
        var box = (await page.Locator(".cropper-face").BoundingBoxAsync())!;
        var x = box.X + box.Width / 3;
        var y = box.Y + box.Height / 2;
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x, y, id = 1 } } });
        for (var step = 1; step <= 8; step++)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x = x + step * 12, y, id = 1 } } });
            await page.WaitForTimeoutAsync(20);
        }
        await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        Assert.Equal(4, await page.EvaluateAsync<int>("() => currentStage"));
        Assert.True(await page.EvaluateAsync<double>("() => document.getElementById('cropperImage').cropper.getData().x") > before + 5,
            "The cropper must still receive and move with the native touch drag.");
        await cdp.DetachAsync();
    }

    [Fact]
    public async Task PortraitReplacement_DisablesCropBeforeTheNewImageInitializes()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.RouteAsync("**/cropperjs@2.3.1/dist/cropper.min.js", route =>
        {
            return route.FulfillAsync(new()
            {
                Path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cropper.min.js"),
                Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
                ContentType = "application/javascript"
            });
        });
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInput").SetInputFilesAsync(Path.Combine(webRoot, "assets", "logo.png"));
        await page.WaitForFunctionAsync("() => document.getElementById('cropperImage').cropper?.ready && !document.getElementById('cropButton').disabled");
        await Assertions.Expect(page.Locator("#cropButton")).ToBeEnabledAsync();
        await page.EvaluateAsync("""
            () => {
                const RealCropper = window.LwcPortraitCropper;
                window.LwcPortraitCropper = class extends RealCropper {
                    constructor(image, options) {
                        const actionEnabled = !document.getElementById('cropButton').disabled;
                        super(image, options);
                        window.replacementCropInitialization = {actionEnabled, pending: !this.ready};
                    }
                };
            }
            """);
        await page.Locator("#profilePicInput").SetInputFilesAsync(Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
        await page.WaitForFunctionAsync("() => window.replacementCropInitialization");
        Assert.True(await page.EvaluateAsync<bool>("() => window.replacementCropInitialization.pending"));
        Assert.False(await page.EvaluateAsync<bool>("() => window.replacementCropInitialization.actionEnabled"),
            "Crop must be disabled before a replacement portrait starts initializing.");
        await Assertions.Expect(page.Locator("#cropButton")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
        await page.Locator("#cancelProfileCropButton").ClickAsync();
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(761, 700, 100)]
    [InlineData(768, 700, 100)]
    [InlineData(844, 390, 100)]
    [InlineData(1024, 480, 100)]
    [InlineData(844, 390, 200)]
    [InlineData(1440, 900, 200)]
    [InlineData(700, 700, 100)]
    [InlineData(720, 700, 100)]
    [InlineData(760, 700, 100)]
    [InlineData(720, 700, 200)]
    [InlineData(160, 320, 100)]
    [InlineData(195, 422, 100)]
    public async Task DockedActions_KeepTheFieldEdgesOnTabletsAndShortWindows(int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("async () => { await document.fonts.ready; currentStage = 6; goToStage(6); window.LwcFlowActionDock.refreshNow(); }");
        await page.WaitForFunctionAsync("() => document.querySelector('.convergence-actions.flow-action-stack--docked')");
        var edges = await page.EvaluateAsync<double[]>("""
            () => {
                const rect = selector => document.querySelector(selector).getBoundingClientRect();
                const card = rect('.convergence-stage-layout'), dock = rect('.convergence-actions');
                const field = rect('#personalLink'), back = rect('#backButton'), next = rect('#nextButton');
                return [card.left, card.right, dock.left, dock.right, field.left, field.right,
                    back.left, next.right, back.height, next.height, dock.height, innerHeight];
            }
            """);
        if (width >= 761)
        {
            Assert.InRange(Math.Abs(edges[0] - edges[2]), 0, 1.5);
            Assert.InRange(Math.Abs(edges[1] - edges[3]), 0, 1.5);
        }
        Assert.InRange(Math.Abs(edges[4] - edges[6]), 0, 1.5);
        Assert.InRange(Math.Abs(edges[5] - edges[7]), 0, 1.5);
        Assert.True(edges[8] >= 44 && edges[9] >= 44 && edges[10] < edges[11] / 2,
            "Both actions must stay usable without consuming the short window.");
        await page.Locator("#backButton").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#nextButton")).ToBeFocusedAsync();
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task FirstBanner_StartsDownloadingWhileAParserScriptIsDelayed()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/field-validation.js*", async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });
        await context.RouteAsync("**/assets/content-images/apply-arena.*", async route =>
        {
            requested.TrySetResult();
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1", new() { WaitUntil = WaitUntilState.Commit });
        var started = false;
        try
        {
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(3));
            started = true;
        }
        catch (TimeoutException) { }
        finally { release.TrySetResult(); }
        Assert.True(started, "The first above-the-fold banner must download before delayed parser scripts finish.");
        await ReadyAsync(page);
        await page.Locator("#illustrationImage").EvaluateAsync("image => image.decode()");
    }

    [Theory]
    [InlineData(761)]
    [InlineData(768)]
    public async Task TabletInlineActions_KeepTheSameRowAndTabOrderWhenDocking(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 1100 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        foreach (var (height, stage, docked) in new[] { (1100, 2, false), (700, 3, true), (1100, 2, false) })
        {
            await page.SetViewportSizeAsync(width, height);
            await page.EvaluateAsync("stage => { currentStage = stage; goToStage(stage); window.LwcFlowActionDock.refreshNow(); }", stage);
            await page.WaitForFunctionAsync("docked => document.querySelector('.convergence-actions').classList.contains('flow-action-stack--docked') === docked", docked);
            await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
            var state = await page.EvaluateAsync<double[]>("""
                () => {
                    const actions = document.querySelector('.convergence-actions');
                    const back = document.getElementById('backButton').getBoundingClientRect();
                    const next = document.getElementById('nextButton').getBoundingClientRect();
                    return [actions.firstElementChild.id === 'backButton' ? 1 : 0, back.top, next.top, back.right, next.left];
                }
                """);
            Assert.Equal(1, state[0]);
            Assert.InRange(Math.Abs(state[1] - state[2]), 0, 1);
            Assert.True(state[3] <= state[4], "Back must precede Next in the same visual row.");
            await page.Locator("#backButton").FocusAsync();
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeFocusedAsync();
            await ExpectNoHorizontalOverflowAsync(page);
        }
    }

    [Fact]
    public async Task BannerIdentity_AndExternalListenersSurviveEveryStage()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("""
            () => {
                window.savedBanner = document.getElementById('illustrationImage');
                window.bannerEvents = 0;
                savedBanner.addEventListener('review-banner', () => window.bannerEvents++);
                for (const stage of [2, 3, 4, 5, 6, 7, 6, 5, 4, 3, 2, 1, 4]) {
                    currentStage = stage; goToStage(stage);
                    document.getElementById('illustrationImage').dispatchEvent(new Event('review-banner'));
                }
            }
            """);
        Assert.True(await page.EvaluateAsync<bool>("() => savedBanner.isConnected && savedBanner === document.getElementById('illustrationImage') && bannerEvents === 13"));
        await page.RunAndWaitForFileChooserAsync(() => page.Locator("#illustrationImage").ClickAsync());
        await page.EvaluateAsync("() => { currentStage = 3; goToStage(3); window.unexpectedChooser = 0; document.getElementById('profilePicInput').addEventListener('click', () => unexpectedChooser++); }");
        await page.Locator("#illustrationImage").ClickAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("() => unexpectedChooser"));
    }

    [Fact]
    public async Task DelayedAndFailedBanners_NeverPaintThePreviousStageOrBlockNavigation()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/assets/content-images/apply-encouragement.*", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        await context.RouteAsync("**/assets/content-images/apply-interview.*", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.Locator("#illustrationImage").EvaluateAsync("image => image.decode()");
        await page.Locator("#nextButton").ClickAsync();
        try
        {
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("hidden", await page.Locator("#illustrationImage").EvaluateAsync<string>("image => getComputedStyle(image).visibility"));
            await Assertions.Expect(page.Locator("#why")).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
            await page.Locator("#nextButton").ClickAsync();
            await page.WaitForFunctionAsync("() => document.getElementById('illustrationPicture').dataset.bannerState === 'failed'");
        }
        finally { release.TrySetResult(); }
        Assert.Equal("hidden", await page.Locator("#illustrationImage").EvaluateAsync<string>("image => getComputedStyle(image).visibility"));
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("The price of glory");
        await page.Locator("#nextButton").ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('illustrationPicture').dataset.bannerState === 'ready'");
        Assert.Contains("apply-reveal", await page.Locator("#illustrationImage").EvaluateAsync<string>("image => image.currentSrc"));
    }

    [Fact]
    public async Task RapidAndInterruptedEntrances_KeepControlsAndFocusStable()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.NoPreference });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("async () => { await document.fonts.ready; currentStage = 2; goToStage(2, { focus: true }); }");
        var positions = await page.EvaluateAsync<double[][]>("""
            async () => {
                const positions = [];
                for (let frame = 0; frame < 20; frame++) {
                    const box = document.getElementById('why').getBoundingClientRect();
                    positions.push([box.left, box.top, parseFloat(getComputedStyle(document.getElementById('whyField')).opacity)]);
                    await new Promise(requestAnimationFrame);
                }
                return positions;
            }
            """);
        Assert.All(positions, box =>
        {
            Assert.InRange(Math.Abs(box[0] - positions[0][0]), 0, 0.5);
            Assert.InRange(Math.Abs(box[1] - positions[0][1]), 0, 0.5);
            Assert.Equal(1, box[2]);
        });
        await Assertions.Expect(page.Locator("#why")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => { for (const stage of [3, 4, 3, 2, 1, 2]) { currentStage = stage; goToStage(stage, { focus: true }); } goToStage(2); }");
        Assert.False(await page.Locator(".convergence-stage-layout").EvaluateAsync<bool>("card => card.classList.contains('convergence-stage-enter')"));
        await page.EvaluateAsync("() => { currentStage = 1; goToStage(1, { focus: true }); }");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => !document.querySelector('.convergence-stage-enter')");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        Assert.Equal("none", await page.Locator("h1").EvaluateAsync<string>("title => getComputedStyle(title).animationName"));
        await Assertions.Expect(page.Locator("#name")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => { currentStage = 3; goToStage(3); }");
        await page.WaitForFunctionAsync("() => !document.querySelector('.convergence-stage-enter')");
        await Assertions.Expect(page.Locator("#subStage3")).ToHaveAttributeAsync("aria-current", "step");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PortraitCrop_RemainsPendingAcrossBackAndLateFileReads(bool delayRead)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.RouteAsync("**/cropperjs@2.3.1/dist/cropper.min.js", route =>
        {
            return route.FulfillAsync(new()
            {
                Path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cropper.min.js"),
                Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
                ContentType = "application/javascript"
            });
        });
        if (delayRead) await context.AddInitScriptAsync("""
            window.FileReader = class extends FileReader {
                constructor() {
                    super();
                    window.releaseProfileRead = null;
                    let released = false;
                    this.addEventListener('load', event => {
                        if (released) return;
                        event.stopImmediatePropagation();
                        window.releaseProfileRead = () => { released = true; this.dispatchEvent(new ProgressEvent('load')); };
                    });
                }
            };
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInput").SetInputFilesAsync(Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
        if (delayRead) await page.WaitForFunctionAsync("() => window.releaseProfileRead");
        else await Assertions.Expect(page.Locator("#croppingPart")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
        await page.Locator("#backButton").ClickAsync();
        if (delayRead) await page.EvaluateAsync("() => window.releaseProfileRead()");
        await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
        await page.Locator("#nextButton").ClickAsync();
        await Assertions.Expect(page.Locator("#croppingPart")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#croppingPart .cropper-crop-box')?.getBoundingClientRect().width > 0");
        await page.Locator("#cancelProfileCropButton").ClickAsync();
        await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#uploadPart")).ToBeVisibleAsync();

        // Encoding may finish after Back. It must keep the saved portrait without
        // painting it over another banner or enabling an invalid identity stage.
        await page.Locator("#profilePicInput").SetInputFilesAsync(Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
        if (delayRead)
        {
            await page.WaitForFunctionAsync("() => applicationProfilePending");
            await page.WaitForFunctionAsync("() => window.releaseProfileRead");
            await page.EvaluateAsync("() => window.releaseProfileRead()");
        }
        await page.WaitForFunctionAsync("() => document.querySelector('#croppingPart .cropper-crop-box')?.getBoundingClientRect().width > 0");
        await page.WaitForFunctionAsync("async () => (await document.getElementById('cropperImage').cropper?.toCanvas())?.width > 0");
        await page.EvaluateAsync("() => { window.optimizeImageClient = raw => new Promise(resolve => { window.releaseProfileSave = () => resolve({ dataUrl: raw }); }); }");
        await page.Locator("#cropButton").ClickAsync();
        await page.WaitForFunctionAsync("() => window.releaseProfileSave");
        Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('cropperImage').cropper.disabled"));
        await page.EvaluateAsync("() => { currentStage = 1; document.getElementById('name').value = ''; goToStage(1); window.releaseProfileSave(); }");
        await page.WaitForFunctionAsync("() => !applicationProfilePending");
        await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#profileImage")).ToBeHiddenAsync();
        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        await Assertions.Expect(page.Locator("#profileImage")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();

        // Selecting another file while the previous crop encodes makes that
        // older result obsolete. Keep the newer crop and its pending readiness.
        async Task SelectPortraitAsync()
        {
            await page.Locator("#profilePicInput").SetInputFilesAsync(Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
            if (delayRead)
            {
                await page.WaitForFunctionAsync("() => window.releaseProfileRead");
                await page.EvaluateAsync("() => window.releaseProfileRead()");
            }
            await page.WaitForFunctionAsync("async () => (await document.getElementById('cropperImage').cropper?.toCanvas())?.width > 0");
        }
        await SelectPortraitAsync();
        await page.EvaluateAsync("() => { window.releaseProfileSave = null; }");
        await page.Locator("#cropButton").ClickAsync();
        await page.WaitForFunctionAsync("() => window.releaseProfileSave");
        await SelectPortraitAsync();
        Assert.False(await page.EvaluateAsync<bool>("() => document.getElementById('cropperImage').cropper.disabled"));
        await page.EvaluateAsync("() => window.releaseProfileSave()");
        await Assertions.Expect(page.Locator("#cropButton")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#croppingPart")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => applicationProfilePending"));
        await page.Locator("#cancelProfileCropButton").ClickAsync();
        await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
    }

    [Theory]
    [InlineData(160, 320)]
    [InlineData(195, 422)]
    public async Task NarrowReflow_KeepsNativeSelectionAndActionWordsWhole(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.Locator("#division").SelectOptionAsync("Women's");
        foreach (var stage in new[] { 1, 3, 4, 7 })
        {
            await page.EvaluateAsync("stage => { currentStage = stage; goToStage(stage); window.LwcFlowActionDock.refreshNow(); }", stage);
            await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
            await page.WaitForFunctionAsync("() => !document.querySelector('.convergence-actions.flow-action-stack--dock-entering')");
            var issues = await page.EvaluateAsync<string[]>("""
                () => {
                    const issues = [];
                    for (const button of document.querySelectorAll('.convergence-actions button, .convergence-upload-action button')) {
                        const box = button.getBoundingClientRect();
                        if (!box.width) continue;
                        const icon = button.querySelector('i')?.getBoundingClientRect();
                        const label = button.querySelector('.flow-action__label');
                        const walker = document.createTreeWalker(label, NodeFilter.SHOW_TEXT);
                        let node;
                        while (node = walker.nextNode()) for (const word of node.textContent.matchAll(/\S+/g)) {
                            const range = document.createRange();
                            range.setStart(node, word.index); range.setEnd(node, word.index + word[0].length);
                            const glyphs = [...range.getClientRects()];
                            if (new Set(glyphs.map(rect => Math.round(rect.top))).size > 1) issues.push('Split word: ' + word[0]);
                            if (glyphs.some(rect => rect.left < box.left - 1 || rect.right > box.right + 1)) issues.push('Clipped word: ' + word[0]);
                            if (icon && glyphs.some(rect => Math.min(rect.right, icon.right) - Math.max(rect.left, icon.left) > 1
                                && Math.min(rect.bottom, icon.bottom) - Math.max(rect.top, icon.top) > 1)) issues.push('Icon overlaps: ' + word[0]);
                        }
                    }
                    const select = document.getElementById('division');
                    if (select.getBoundingClientRect().width) {
                        const style = getComputedStyle(select), context = document.createElement('canvas').getContext('2d');
                        // Numeric variants can make style.font empty; retain the rendered font size.
                        context.font = `${style.fontStyle} ${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                        context.letterSpacing = style.letterSpacing;
                        const available = select.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight) - 20;
                        if (context.measureText(select.selectedOptions[0].textContent).width > available + 1) issues.push('Native selected text is clipped');
                    }
                    return issues;
                }
                """);
            Assert.Empty(issues);
            var overflow = await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth - innerWidth");
            var outside = overflow > 1 ? await page.EvaluateAsync<string>("() => JSON.stringify([...document.body.querySelectorAll('*')].filter(e => e.getBoundingClientRect().width && e.getBoundingClientRect().right > innerWidth + 1).map(e => [e.tagName, e.id, e.className, e.textContent.slice(0, 80), e.getBoundingClientRect().right]))") : "";
            Assert.True(overflow <= 1, $"Stage {stage}: {overflow}px horizontal overflow at {width}px. {outside}");
        }
    }

    [Theory]
    [InlineData(160, 320, 100)]
    [InlineData(195, 422, 100)]
    [InlineData(390, 844, 200)]
    public async Task PortraitCrop_ReflowsInsideTheAvailableStage(int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.RouteAsync("**/cropperjs@2.3.1/dist/cropper.min.js", route =>
        {
            return route.FulfillAsync(new()
            {
                Path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cropper.min.js"),
                Headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" },
                ContentType = "application/javascript"
            });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await ReadyAsync(page);
        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        var webRoot = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        await page.Locator("#profilePicInput").SetInputFilesAsync(Path.Combine(webRoot, "assets", "content-images", "play-athlete-placeholder.jpg"));
        await page.WaitForFunctionAsync("async () => (await document.getElementById('cropperImage').cropper?.toCanvas())?.width > 0");
        await page.SetViewportSizeAsync(width, height);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await page.WaitForFunctionAsync("""
            () => {
                const preview = document.querySelector('.convergence-crop-preview').getBoundingClientRect();
                const container = document.querySelector('#croppingPart .cropper-container').getBoundingClientRect();
                const crop = document.querySelector('#croppingPart .cropper-crop-box').getBoundingClientRect();
                return container.width > 0 && container.left >= preview.left - 1 && container.right <= preview.right + 1
                    && crop.width > 0 && crop.height > 0 && crop.left >= container.left - 1 && crop.right <= container.right + 1
                    && crop.top >= container.top - 1 && crop.bottom <= container.bottom + 1;
            }
            """, options: new() { Timeout = 5_000 });
        await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
        await page.Locator("#backButton").ClickAsync();
        await page.SetViewportSizeAsync(width + 20, height);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        Assert.True(await page.EvaluateAsync<bool>("() => document.getElementById('cropperImage').cropper.disabled"));
        await page.Locator("#nextButton").ClickAsync();
        await page.WaitForFunctionAsync("() => { const image = document.getElementById('cropperImage'); const stage = document.querySelector('.convergence-crop-stage'); return image.cropper.ready && !image.cropper.disabled && Math.abs(image.cropper.getContainerData().width - stage.clientWidth) <= 1; }");
        await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
        await page.WaitForFunctionAsync("() => !document.getElementById('cropButton').disabled");
        await Assertions.Expect(page.Locator("#cropButton")).ToBeFocusedAsync();
        await page.Locator("#cancelProfileCropButton").ClickAsync();
        await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
        await ExpectNoHorizontalOverflowAsync(page);
    }
}
