using Microsoft.Playwright;
using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ImageViewerBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IPage> OpenDocumentAsync(IBrowserContext context, string path = "/ruleset")
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var baseline = Environment.GetEnvironmentVariable("LWC_VIEWER_BASELINE_DIR");
        if (!string.IsNullOrEmpty(baseline))
        {
            await context.RouteAsync("**/js/image-viewer.js?*", route => route.FulfillAsync(new() { ContentType = "application/javascript", Path = Path.Combine(baseline, "before-image-viewer.js") }));
            await context.RouteAsync("**/css/image-viewer.css?*", route => route.FulfillAsync(new() { ContentType = "text/css", Path = Path.Combine(baseline, "before-image-viewer.css") }));
        }
        await context.RouteAsync("https://github.com/user-attachments/assets/**", async route =>
        {
            var assets = Environment.GetEnvironmentVariable("LWC_VIEWER_DOCUMENT_IMAGES");
            var file = assets is null ? null : Path.Combine(assets, new Uri(route.Request.Url).Segments.Last());
            if (file is not null) Assert.True(File.Exists(file), $"Missing real capture image: {file}");
            if (file is not null && File.Exists(file))
                await route.FulfillAsync(new() { ContentType = "image/png", Path = file });
            else
                await route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = "<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='800'><rect width='1200' height='800' fill='#e8efe9'/><text x='100' y='400' font-size='50'>Synthetic viewer regression fixture</text></svg>" });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => !!window.LwcImageViewer");
        var image = page.Locator(".documentation-document img[data-lwc-viewable]:visible").First;
        await image.ScrollIntoViewIfNeededAsync();
        await image.EvaluateAsync("async img => { await document.fonts.ready; await img.decode(); img.scrollIntoView({block:'center',behavior:'instant'}); }");
        return page;
    }

    [Fact]
    public async Task PictureTap_DoesNotDismiss_AndDoubleTapZooms()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        var page = await OpenDocumentAsync(context);
        await page.Locator("img[data-lwc-viewable]:visible").First.ClickAsync();
        var viewer = page.Locator("dialog.lwc-viewer");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-state", "open");
        await viewer.Locator(".lwc-viewer-picture").ClickAsync();
        await page.WaitForTimeoutAsync(650);
        await Assertions.Expect(viewer).ToBeVisibleAsync();
        await viewer.Locator(".lwc-viewer-picture").DblClickAsync();
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-zoomed", "");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackDuringAnimation_ClosesAndReturnsFocus(bool duringNavigation)
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        var page = await OpenDocumentAsync(context);
        await page.EvaluateAsync("() => { window.viewerSource = document.querySelector('img[data-lwc-viewable]'); window.viewerSource.click(); }");
        if (duringNavigation)
        {
            await Assertions.Expect(page.Locator("dialog.lwc-viewer")).ToHaveAttributeAsync("data-state", "open");
            await page.Keyboard.PressAsync("ArrowRight");
        }
        await page.EvaluateAsync("history.back()");
        await Assertions.Expect(page.Locator("dialog.lwc-viewer")).ToBeHiddenAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement.matches('img[data-lwc-viewable]')"));
        Assert.Equal(0, await page.Locator("[data-lwc-view-away]").CountAsync());
    }

    [Theory]
    [InlineData("/ruleset", 1440, 900, ColorScheme.Light)]
    [InlineData("/ruleset", 1440, 900, ColorScheme.Dark)]
    [InlineData("/ruleset", 390, 844, ColorScheme.Light)]
    [InlineData("/ruleset", 390, 844, ColorScheme.Dark)]
    [InlineData("/about", 1440, 900, ColorScheme.Light)]
    [InlineData("/about", 1440, 900, ColorScheme.Dark)]
    [InlineData("/about", 390, 844, ColorScheme.Light)]
    [InlineData("/about", 390, 844, ColorScheme.Dark)]
    [InlineData("/history", 1440, 900, ColorScheme.Light)]
    [InlineData("/history", 1440, 900, ColorScheme.Dark)]
    [InlineData("/history", 390, 844, ColorScheme.Light)]
    [InlineData("/history", 390, 844, ColorScheme.Dark)]
    public async Task DocumentViewer_CaptureMatchedStates(string path, int width, int height, ColorScheme theme)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = height }, ColorScheme = theme
        });
        var page = await OpenDocumentAsync(context, path);
        var directory = Environment.GetEnvironmentVariable("LWC_VIEWER_CAPTURE_DIR");
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{path.Trim('/')}-{width}-{theme}-page.png") });
        }
        await page.Locator("img[data-lwc-viewable]:visible").First.ClickAsync();
        var viewer = page.Locator("dialog.lwc-viewer");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-state", "open");
        await page.EvaluateAsync("async () => { await document.querySelector('.lwc-viewer-picture').decode(); }").WaitAsync(TimeSpan.FromSeconds(10));
        await WaitForFiniteAnimationsAsync(page, ".lwc-viewer, .lwc-viewer *");
        if (!string.IsNullOrEmpty(directory))
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{path.Trim('/')}-{width}-{theme}-open.png") });
        var overflow = await viewer.EvaluateAsync<bool>("e => e.scrollWidth > e.clientWidth || e.scrollHeight > e.clientHeight");
        Assert.False(overflow);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(viewer).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(320, 568, ColorScheme.Light)]
    [InlineData(768, 1024, ColorScheme.Dark)]
    [InlineData(844, 390, ColorScheme.Light)]
    [InlineData(195, 422, ColorScheme.Dark)]
    public async Task ReaderControls_FitWithLargeTextAndStayInTheFocusLoop(int width, int height, ColorScheme theme)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenDocumentAsync(context);
        await page.EvaluateAsync("document.documentElement.style.fontSize='200%'");
        await page.Locator("img[data-lwc-viewable]:visible").First.ClickAsync();
        var viewer = page.Locator("dialog.lwc-viewer");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-state", "open");
        var issues = await viewer.EvaluateAsync<string[]>("""
            viewer => {
                const issues=[];
                const elements=[...viewer.querySelectorAll('button,.lwc-viewer-stage')].filter(e=>e.checkVisibility()&&!e.closest('[hidden]'));
                for (let i=0;i<elements.length;i++) {
                    const a=elements[i].getBoundingClientRect();
                    if(a.left<0||a.top<0||a.right>innerWidth+1||a.bottom>innerHeight+1) issues.push(elements[i].className+' outside viewport');
                    if(elements[i].tagName==='BUTTON'&&(a.width<46||a.height<46)) issues.push('small touch target');
                    for(let j=i+1;j<elements.length;j++) {
                        const b=elements[j].getBoundingClientRect();
                        if(a.left<b.right&&a.right>b.left&&a.top<b.bottom&&a.bottom>b.top) issues.push(elements[i].className+' overlaps '+elements[j].className);
                    }
                }
                if(viewer.scrollWidth>viewer.clientWidth||viewer.scrollHeight>viewer.clientHeight) issues.push('dialog overflows');
                return issues;
            }
            """);
        Assert.Empty(issues);
        Assert.Empty(await viewer.EvaluateAsync<string[]>("e=>e.getAnimations({subtree:true}).map(a=>a.constructor.name)"));
        for (var i = 0; i < 12; i++)
        {
            await page.Keyboard.PressAsync(i % 2 == 0 ? "Tab" : "Shift+Tab");
            Assert.True(await viewer.EvaluateAsync<bool>("e=>e.contains(document.activeElement)"));
        }
        var scroll = await page.EvaluateAsync<double>("scrollY");
        await viewer.Locator(".lwc-viewer-stage").FocusAsync();
        foreach (var key in new[] { "PageDown", "PageUp", "Space", "ArrowDown" }) await page.Keyboard.PressAsync(key);
        Assert.Equal(scroll, await page.EvaluateAsync<double>("scrollY"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(viewer).ToBeHiddenAsync();
        Assert.Equal(scroll, await page.EvaluateAsync<double>("scrollY"));
    }

    [Fact]
    public async Task TouchPinchPanSwipeAndFlick_KeepOneReaderAndRestoreTheSelectedSource()
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = 390, Height = 844 },
            IsMobile = true, HasTouch = true, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenDocumentAsync(context);
        await page.Locator("img[data-lwc-viewable]:visible").First.TapAsync();
        var viewer = page.Locator("dialog.lwc-viewer");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-state", "open");
        var cdp = await context.NewCDPSessionAsync(page);
        async Task Touch(string type, params (int X, int Y, int Id)[] points)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = type,
                ["touchPoints"] = points.Select(p => new { x = p.X, y = p.Y, id = p.Id }).ToArray() });
            // Keep separate physical gestures apart: instantaneous CDP streams
            // suppress the following compatibility click in Chromium.
            await page.WaitForTimeoutAsync(80);
        }
        await Touch("touchStart", (140, 400, 0), (250, 400, 1));
        await Touch("touchMove", (70, 400, 0), (320, 400, 1));
        await Touch("touchEnd");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-zoomed", "");
        var beforePan = await viewer.Locator("img").GetAttributeAsync("style");
        await Touch("touchStart", (220, 400, 0));
        await Touch("touchMove", (120, 450, 0));
        await Touch("touchEnd");
        Assert.NotEqual(beforePan, await viewer.Locator("img").GetAttributeAsync("style"));
        await viewer.Locator(".lwc-viewer-zoom-fit").TapAsync();
        await Assertions.Expect(viewer).Not.ToHaveAttributeAsync("data-zoomed", "");
        await Touch("touchStart", (300, 400, 0));
        await Touch("touchMove", (80, 400, 0));
        await Touch("touchEnd");
        await Assertions.Expect(viewer.Locator(".lwc-viewer-position")).ToHaveTextAsync("Image 2 of 3");
        await Touch("touchStart", (190, 400, 0));
        await Touch("touchMove", (190, 600, 0));
        await Touch("touchCancel");
        await Assertions.Expect(viewer).ToBeVisibleAsync();
        await Assertions.Expect(viewer).Not.ToHaveAttributeAsync("data-zoomed", "");
        await Touch("touchStart", (190, 400, 0));
        await Touch("touchMove", (190, 650, 0));
        await Touch("touchEnd");
        await Assertions.Expect(viewer).ToBeHiddenAsync();
        Assert.Equal(1, await page.Locator("dialog.lwc-viewer").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.activeElement === document.querySelectorAll('img[data-lwc-viewable]')[1]"));
    }

    [Theory]
    [InlineData(320, 568, ColorScheme.Light)]
    [InlineData(320, 568, ColorScheme.Dark)]
    [InlineData(844, 390, ColorScheme.Light)]
    [InlineData(844, 390, ColorScheme.Dark)]
    [InlineData(195, 422, ColorScheme.Light)]
    [InlineData(195, 422, ColorScheme.Dark)]
    public async Task ReaderError_EnlargedTextKeepsRecoveryReachable(int width, int height, ColorScheme theme)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenDocumentAsync(context);
        await page.EvaluateAsync("() => {document.documentElement.style.fontSize='200%'; const image=document.querySelector('img[data-lwc-viewable]'); image.dataset.lwcViewSrc='/viewer-missing-image.png'; window.LwcImageViewer.open(image,{kind:'proof',images:[image]});}");
        var viewer = page.Locator(".lwc-viewer");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "error");
        var panel = viewer.Locator(".lwc-viewer-feedback");
        Assert.True(await panel.EvaluateAsync<bool>("e=>e.scrollWidth<=e.clientWidth+1"));
        if (width >= 320) Assert.True(await panel.EvaluateAsync<bool>("e=>e.scrollHeight<=e.clientHeight+1"));
        var retry = viewer.Locator(".lwc-viewer-retry");
        await retry.FocusAsync();
        await retry.ScrollIntoViewIfNeededAsync();
        Assert.True(await retry.EvaluateAsync<bool>("e=>{const r=e.getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight&&r.width>=46&&r.height>=46;}"));
        if (Environment.GetEnvironmentVariable("LWC_VIEWER_CAPTURE_DIR") is { Length: > 0 } directory)
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"text-200-error-{width}-{theme}.png") });
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(viewer).ToBeHiddenAsync();
    }

    [Fact]
    public async Task NativeBrowserZoom_ReflowsTheReaderAtTwoHundredPercent()
    {
        // A private Chromium extension exercises real browser zoom. CSS zoom,
        // viewport resizing and device emulation do not verify this behavior.
        var temporary = Directory.CreateTempSubdirectory("lwc-viewer-zoom-");
        try
        {
            var extension = Path.Combine(temporary.FullName, "extension");
            Directory.CreateDirectory(extension);
            await File.WriteAllTextAsync(Path.Combine(extension, "manifest.json"), """
                {"manifest_version":3,"name":"Local viewer zoom verification","version":"1.0","permissions":["tabs"],"background":{"service_worker":"background.js"}}
                """);
            await File.WriteAllTextAsync(Path.Combine(extension, "background.js"), "chrome.runtime.onInstalled.addListener(() => {});");
            await File.WriteAllTextAsync(Path.Combine(extension, "control.html"), "<!doctype html><title>Local zoom control</title>");
            using var playwright = await Playwright.CreateAsync();
            await using var context = await playwright.Chromium.LaunchPersistentContextAsync(Path.Combine(temporary.FullName, "profile"), new()
            {
                Channel = "chromium", Headless = true, BaseURL = App.BaseAddress.ToString(),
                ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce,
                Args = [$"--disable-extensions-except={extension}", $"--load-extension={extension}", "--disable-features=OverlayScrollbar,FluentOverlayScrollbar"]
            });
            var page = await OpenDocumentAsync(context);
            var cdp = await context.NewCDPSessionAsync(page);
            var extensionId = "";
            for (var attempt = 0; attempt < 40 && extensionId.Length == 0; attempt++)
            {
                var targets = await cdp.SendAsync("Target.getTargets");
                extensionId = Regex.Match(targets?.ToString() ?? "", @"chrome-extension://([a-p]+)/background.js").Groups[1].Value;
                if (extensionId.Length == 0) await Task.Delay(100);
            }
            Assert.NotEmpty(extensionId);
            var control = await context.NewPageAsync();
            await control.GotoAsync($"chrome-extension://{extensionId}/control.html");
            foreach (var width in new[] { 1440, 390 })
            foreach (var theme in new[] { ColorScheme.Light, ColorScheme.Dark })
            {
                await page.SetViewportSizeAsync(width, width == 1440 ? 900 : 844);
                await page.EmulateMediaAsync(new() { ColorScheme = theme });
                var zoom = await control.EvaluateAsync<double>("""
                    async url => {
                        const tab = (await chrome.tabs.query({})).find(t => t.url === url);
                        await chrome.tabs.setZoomSettings(tab.id, {mode:'automatic', scope:'per-tab'});
                        await chrome.tabs.setZoom(tab.id, 2);
                        return await chrome.tabs.getZoom(tab.id);
                    }
                    """, page.Url);
                Assert.Equal(2, zoom);
                await page.WaitForFunctionAsync("width => Math.abs(innerWidth - width / 2) <= 1 && devicePixelRatio === 2", width);
                Assert.InRange(await page.EvaluateAsync<int>("innerWidth"), width / 2 - 1, width / 2 + 1);
                Assert.Equal(2, await page.EvaluateAsync<double>("devicePixelRatio"));
                await page.Locator("img[data-lwc-viewable]:visible").First.ClickAsync();
                var viewer = page.Locator("dialog.lwc-viewer");
                await Assertions.Expect(viewer).ToHaveAttributeAsync("data-state", "open");
                await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
                Assert.True(await viewer.EvaluateAsync<bool>("""
                    e => e.scrollWidth === e.clientWidth && e.scrollHeight === e.clientHeight &&
                        [...e.querySelectorAll('button')].filter(b=>b.checkVisibility()&&!b.closest('[hidden]')).every(b=>{
                            const r=b.getBoundingClientRect(); return r.left>=0&&r.top>=0&&r.right<=innerWidth+1&&r.bottom<=innerHeight+1&&r.width>=46&&r.height>=46;
                        })
                    """));
                var directory = Environment.GetEnvironmentVariable("LWC_VIEWER_CAPTURE_DIR");
                if (!string.IsNullOrEmpty(directory))
                    {
                    var screenshot = await cdp.SendAsync("Page.captureScreenshot", new() { ["format"] = "png", ["captureBeyondViewport"] = false });
                    await File.WriteAllBytesAsync(Path.Combine(directory, $"native-zoom-200-{width}-{theme}.png"), Convert.FromBase64String(screenshot!.Value.GetProperty("data").GetString()!));
                }
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(viewer).ToBeHiddenAsync();
                await page.EvaluateAsync("() => { const image=document.querySelector('img[data-lwc-viewable]'); image.dataset.lwcViewSrc='/viewer-missing-image.png'; window.LwcImageViewer.open(image,{kind:'proof'}); }");
                await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "error");
                var feedback = viewer.Locator(".lwc-viewer-feedback");
                Assert.True(await feedback.EvaluateAsync<bool>("e=>e.scrollHeight<=e.clientHeight+1 && e.scrollWidth<=e.clientWidth+1"));
                await Assertions.Expect(viewer.Locator(".lwc-viewer-retry")).ToBeVisibleAsync();
                if (!string.IsNullOrEmpty(directory))
                {
                    var screenshot = await cdp.SendAsync("Page.captureScreenshot", new() { ["format"] = "png", ["captureBeyondViewport"] = false });
                    await File.WriteAllBytesAsync(Path.Combine(directory, $"native-zoom-200-error-{width}-{theme}.png"), Convert.FromBase64String(screenshot!.Value.GetProperty("data").GetString()!));
                }
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(viewer).ToBeHiddenAsync();
                await page.EvaluateAsync("delete document.querySelector('img[data-lwc-viewable]').dataset.lwcViewSrc");
                await control.EvaluateAsync("async url => { const tab=(await chrome.tabs.query({})).find(t=>t.url===url); await chrome.tabs.setZoom(tab.id,1); }", page.Url);
                await page.WaitForFunctionAsync("width => Math.abs(innerWidth - width) <= 1 && devicePixelRatio === 1", width);
            }
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(temporary.FullName));
            temporary.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(1440, 900, ColorScheme.Light)]
    [InlineData(1440, 900, ColorScheme.Dark)]
    [InlineData(390, 844, ColorScheme.Light)]
    [InlineData(390, 844, ColorScheme.Dark)]
    public async Task AthleteAndQrEntrypoints_KeepLoadedImagesAndReturnFocus(int width, int height, ColorScheme theme)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme, ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var baseline = Environment.GetEnvironmentVariable("LWC_VIEWER_BASELINE_DIR");
        if (!string.IsNullOrEmpty(baseline))
        {
            foreach (var name in new[] { "image-viewer", "leaderboard-page" })
                await context.RouteAsync($"**/js/{name}.js?*", route => route.FulfillAsync(new() { ContentType = "application/javascript", Path = Path.Combine(baseline, $"before-{name}.js") }));
            foreach (var name in new[] { "image-viewer", "leaderboard-content" })
                await context.RouteAsync($"**/css/{name}.css?*", route => route.FulfillAsync(new() { ContentType = "text/css", Path = Path.Combine(baseline, $"before-{name}.css") }));
            var markup = await File.ReadAllTextAsync(Path.Combine(baseline, "before-athlete-viewer.html"));
            await context.AddInitScriptAsync("document.addEventListener('DOMContentLoaded',()=>document.querySelector('#detailsModal')?.insertAdjacentHTML('beforeend'," + System.Text.Json.JsonSerializer.Serialize(markup) + ")); ");
        }
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/christopher-yamba", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'christopher-yamba' && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        foreach (var kind in new[] { "portrait", "proof" })
        {
            var source = page.Locator(kind == "portrait" ? "#modalProfilePic" : "#proofsGallery img").Nth(kind == "proof" ? 1 : 0);
            await source.EvaluateAsync("async e=>{await document.fonts.ready; await e.decode(); e.scrollIntoView({block:'center',behavior:'instant'});}");
            await source.ClickAsync();
            var viewer = page.Locator(baseline is null ? "dialog.lwc-viewer" : "#athleteImageViewer");
            await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
            if (baseline is null) await Assertions.Expect(viewer).ToHaveAttributeAsync("data-state", "open");
            await viewer.Locator("img").EvaluateAsync("async e=>await e.decode()");
            if (kind == "proof") await viewer.GetByRole(AriaRole.Button, new() { Name = "Fit proof to viewer", Exact = true }).ClickAsync();
            await CaptureAsync($"athlete-{kind}-{width}-{theme}.png");
            Assert.True(await viewer.EvaluateAsync<bool>("e=>e.scrollWidth<=e.clientWidth+1 && e.scrollHeight<=e.clientHeight+1"));
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(viewer).ToBeHiddenAsync();
            await Assertions.Expect(source).ToBeFocusedAsync();
        }
        await page.GotoAsync("/");
        var qr = page.Locator("img.qr-code-img");
        await qr.EvaluateAsync("async e=>{await document.fonts.ready; e.scrollIntoView({block:'center',behavior:'instant'}); await e.decode();}");
        await qr.ClickAsync();
        var qrViewer = page.Locator("dialog.lwc-viewer");
        await Assertions.Expect(qrViewer).ToHaveAttributeAsync("data-state", "open");
        await qrViewer.Locator("img").EvaluateAsync("async e=>await e.decode()");
        await CaptureAsync($"donation-qr-{width}-{theme}.png");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(qrViewer).ToBeHiddenAsync();
        await Assertions.Expect(qr).ToBeFocusedAsync();

        async Task CaptureAsync(string name)
        {
            if (Environment.GetEnvironmentVariable("LWC_VIEWER_CAPTURE_DIR") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            await WaitForFiniteAnimationsAsync(page, ".lwc-viewer, .lwc-viewer *");
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name) });
        }
    }
}
