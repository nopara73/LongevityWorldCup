using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProofRedactionBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AppliedAreas_ReplaceActualSubmissionBytesAcrossCancelUndoNavigationAndReuploads(bool onboarding, bool pdf)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await PrepareAsync(context, onboarding);
        var files = await ReportsAsync(page, pdf);
        await UploadAsync(page, files);
        var original = await SourcesAsync(page);
        Assert.Equal(2, original.Length);

        // Leave an original in removal history, then restore it by re-uploading.
        await page.Locator(".proof-page-remove").Last.ClickAsync();
        await UploadAsync(page, files);
        await page.Locator(".proof-page-preview").First.ClickAsync();
        await StartAsync(page);
        await DrawAsync(page, 50, 170, 600, 170);
        await page.Locator(".proof-review-close").ClickAsync(); // Cancel all drafts.
        Assert.Equal(original, await SourcesAsync(page));
        await StartAsync(page);
        await DrawAsync(page, 50, 170, 600, 170);
        await page.Locator(".proof-review-thumb").Nth(1).ClickAsync();
        await ReadyAsync(page);
        await DrawAsync(page, 50, 170, 600, 170);
        await page.Locator(".proof-edit-undo").ClickAsync();
        await Assertions.Expect(page.Locator(".proof-edit-apply")).ToHaveTextAsync("Apply (1)");
        await page.Locator(".proof-review-thumb").First.ClickAsync();
        await ReadyAsync(page);
        await ApplyAsync(page);
        var firstEdit = await SourcesAsync(page);
        Assert.NotEqual(original[0], firstEdit[0]);
        Assert.Equal(original[1], firstEdit[1]);

        // Applying is an irreversible boundary. A new edit's Undo/Cancel cannot
        // recover an already hidden area, even after changing pages.
        await StartAsync(page);
        await DrawAsync(page, 700, 900, 160, 80);
        await page.Locator(".proof-edit-undo").ClickAsync();
        await page.Locator(".proof-review-thumb").Nth(1).ClickAsync();
        await ReadyAsync(page);
        await DrawAsync(page, 50, 170, 600, 170);
        await ApplyAsync(page);
        var applied = await SourcesAsync(page);
        Assert.Equal(firstEdit[0], applied[0]);
        Assert.NotEqual(original[1], applied[1]);
        await SaveEvidenceAsync(page, $"{(onboarding ? "apply" : "proofs")}-{(pdf ? "pdf" : "image")}-applied");
        await StartAsync(page);
        await DrawAsync(page, 700, 900, 160, 80);
        await page.Locator(".proof-review-close").ClickAsync();
        await page.Locator(".proof-review-close").ClickAsync();
        await Assertions.Expect(page.Locator(".proof-page-preview").Nth(1)).ToBeFocusedAsync();

        // Removal history, original file fingerprints, and optimized-image
        // fingerprints must all lead to the latest safe copy.
        await page.Locator(".proof-undo").ClickAsync();
        Assert.Equal(applied, await SourcesAsync(page));
        await page.Locator(".proof-page-remove").Last.ClickAsync();
        await UploadAsync(page, files.Select(file => new FilePayload { Name = "renamed-" + file.Name, MimeType = file.MimeType, Buffer = file.Buffer }).ToArray());
        await page.Locator(".proof-undo").ClickAsync();
        Assert.Equal(applied, await SourcesAsync(page));
        if (!pdf)
        {
            // Different file bytes that decode/optimize to the same original.
            await UploadAsync(page, files.Select(file => new FilePayload { Name = "trailing-" + file.Name, MimeType = file.MimeType, Buffer = [.. file.Buffer, 0, 0] }).ToArray());
            Assert.Equal(applied, await SourcesAsync(page));
        }
        var originalsInDom = await page.EvaluateAsync<bool>("sources => sources.some(source => document.documentElement.outerHTML.includes(source))", original);
        Assert.False(originalsInDom);

        if (onboarding)
        {
            await page.Locator("#nextButton").ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Final details", Exact = true }).WaitForAsync();
            await page.Locator("#backButton").ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Don't trust, verify", Exact = true }).WaitForAsync();
            Assert.Equal(applied, await SourcesAsync(page));
            await page.Locator("#nextButton").ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Final details", Exact = true }).WaitForAsync();
            await page.Locator("#nextButton").ClickAsync();
        }

        var requestTask = page.WaitForRequestAsync(request => request.Url.EndsWith("/api/application/application") && request.Method == "POST");
        await page.Locator(onboarding ? "#nextButton" : "#submitButton").ClickAsync();
        var request = await requestTask;
        using var payload = JsonDocument.Parse(request.PostData!);
        var submitted = payload.RootElement.GetProperty("proofPics").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal(applied, submitted);
        Assert.DoesNotContain(original[0], request.PostData!);
        Assert.DoesNotContain(original[1], request.PostData!);
        for (var index = 0; index < submitted.Length; index++) AssertFlattened(original[index], submitted[index]);
    }

    [Fact]
    public async Task FailedEncoding_IsAtomicAndRetryKeepsTheSelectionsWithoutAnOriginalFallback()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await PrepareAsync(context, false);
        await UploadAsync(page, await ReportsAsync(page, false));
        var original = await SourcesAsync(page);
        await page.Locator(".biomarker-checkbox").First.CheckAsync();
        await page.Locator(".proof-page-preview").First.ClickAsync();
        await StartAsync(page);
        await DrawAsync(page, 50, 170, 600, 170);
        await page.Locator(".proof-review-thumb").Last.ClickAsync();
        await ReadyAsync(page);
        await DrawAsync(page, 50, 170, 600, 170);
        await page.EvaluateAsync("""
            () => {
                window.__originalProofToBlob = HTMLCanvasElement.prototype.toBlob;
                let count = 0;
                HTMLCanvasElement.prototype.toBlob = function(callback, ...args) {
                    if (++count >= 3) { callback(null); return; }
                    return window.__originalProofToBlob.call(this, callback, ...args);
                };
            }
            """);
        await page.Locator(".proof-edit-apply").ClickAsync();
        await Assertions.Expect(page.Locator(".proof-edit-status")).ToContainTextAsync("Could not apply");
        Assert.Equal(original, await SourcesAsync(page));
        await Assertions.Expect(page.Locator(".proof-edit-apply")).ToBeEnabledAsync();
        await page.EvaluateAsync("() => { HTMLCanvasElement.prototype.toBlob = window.__originalProofToBlob; }");
        await ApplyAsync(page);
        var applied = await SourcesAsync(page);
        for (var index = 0; index < applied.Length; index++) AssertFlattened(original[index], applied[index]);
        Assert.False(await page.Locator(".biomarker-checkbox").First.IsCheckedAsync());
    }

    [Theory]
    [InlineData(390, false, false)]
    [InlineData(390, true, false)]
    [InlineData(320, false, true)]
    [InlineData(1280, true, true)]
    public async Task TouchPinchPanAndKeyboard_KeepSelectionsPreciseAndControlsReachable(int width, bool dark, bool largeText)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, HasTouch = true,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await PrepareAsync(context, false);
        await UploadAsync(page, await ReportsAsync(page, false));
        if (largeText) await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.Locator(".proof-page-preview").First.TapAsync();
        await StartAsync(page);
        var stage = page.Locator(".proof-edit-stage");
        var cdp = await context.NewCDPSessionAsync(page);
        var box = (await stage.BoundingBoxAsync())!;
        var x = box.X + box.Width / 2; var y = box.Y + box.Height / 2;
        var before = await page.Locator(".proof-edit-sheet").BoundingBoxAsync();
        await TouchAsync(cdp, "touchStart", (x - 25, y), (x + 25, y));
        await TouchAsync(cdp, "touchMove", (x - 65, y - 15), (x + 65, y + 15));
        await TouchAsync(cdp, "touchEnd");
        Assert.True((await page.Locator(".proof-edit-sheet").BoundingBoxAsync())!.Width > before!.Width * 2);
        await Assertions.Expect(page.Locator(".proof-edit-apply")).ToBeDisabledAsync();
        // Finger drawing uses native touch input, not synthetic DOM pointer events.
        await TouchAsync(cdp, "touchStart", (x - 60, y - 30));
        await TouchAsync(cdp, "touchMove", (x + 60, y + 30));
        await TouchAsync(cdp, "touchEnd");
        await Assertions.Expect(page.Locator(".proof-edit-apply")).ToHaveTextAsync("Apply (1)");
        await page.Locator(".proof-edit-move").TapAsync();
        await Assertions.Expect(page.Locator(".proof-edit-move")).ToHaveAttributeAsync("aria-pressed", "true");
        var transform = await page.Locator(".proof-edit-sheet").GetAttributeAsync("style");
        await TouchAsync(cdp, "touchStart", (x, y));
        await TouchAsync(cdp, "touchMove", (x - 40, y - 50));
        await TouchAsync(cdp, "touchEnd");
        Assert.NotEqual(transform, await page.Locator(".proof-edit-sheet").GetAttributeAsync("style"));
        await page.Locator(".proof-edit-hide").ClickAsync();
        await stage.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var area = page.Locator(".proof-edit-sheet rect[data-area]").Last;
        var originalX = await area.GetAttributeAsync("x");
        var originalWidth = await area.GetAttributeAsync("width");
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.NotEqual(originalX, await area.GetAttributeAsync("x"));
        await page.Keyboard.PressAsync("Shift+ArrowRight");
        Assert.NotEqual(originalWidth, await area.GetAttributeAsync("width"));
        await page.Keyboard.PressAsync("Delete");
        await Assertions.Expect(page.Locator(".proof-edit-apply")).ToHaveTextAsync("Apply (1)");
        await page.Keyboard.PressAsync("Control+z");
        await Assertions.Expect(page.Locator(".proof-edit-apply")).ToHaveTextAsync("Apply (2)");
        await page.Keyboard.PressAsync("PageDown");
        await Assertions.Expect(stage).ToHaveAccessibleNameAsync("Hide details on proof page, area 1 of 2 selected");
        await page.Keyboard.PressAsync("PageUp");
        await Assertions.Expect(stage).ToHaveAccessibleNameAsync("Hide details on proof page, area 2 of 2 selected");
        await page.Keyboard.PressAsync("Delete");
        await Assertions.Expect(page.Locator(".proof-edit-apply")).ToHaveTextAsync("Apply (1)");
        await page.Keyboard.PressAsync("Control+z");
        await SaveEvidenceAsync(page, $"touch-{width}-{(dark ? "dark" : "light")}{(largeText ? "-large-text" : "")}");
        // Actual layout/focus checks, including 200% text and narrow screens.
        foreach (var control in await page.Locator(".proof-edit-controls button, .proof-review-close").AllAsync())
        {
            await control.ScrollIntoViewIfNeededAsync();
            var rect = (await control.BoundingBoxAsync())!;
            Assert.True(rect.Width >= 44 && rect.Height >= 44);
            Assert.InRange(rect.X, 0, width - rect.Width + 1);
            Assert.True(await control.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1"));
        }
        await page.Locator(".proof-edit-apply").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".proof-review-close")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".proof-review-hide")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".proof-page-preview").First).ToBeFocusedAsync();
        await cdp.DetachAsync();
    }

    [Theory]
    [InlineData(1280, 900, false)]
    [InlineData(780, 1688, true)]
    public async Task NativeTwoHundredPercentZoom_LeavesEditingAndReviewUsable(int width, int height, bool dark)
    {
        // Full headless Chrome reads its real zoom preference; viewport resizing,
        // device scale emulation and pinch/pageScaleFactor are not browser zoom.
        var profile = Path.Combine(Path.GetTempPath(), "lwc-proof-zoom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(profile, "Default"));
        await File.WriteAllTextAsync(Path.Combine(profile, "Default", "Preferences"),
            JsonSerializer.Serialize(new { partition = new { default_zoom_level = new { x = Math.Log(2) / Math.Log(1.2) } } }));
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var context = await playwright.Chromium.LaunchPersistentContextAsync(profile, new()
            {
                Channel = "chromium", Headless = true, ViewportSize = ViewportSize.NoViewport,
                BaseURL = App.BaseAddress.ToString(),
                Args = [$"--window-size={width},{height}"], ReducedMotion = ReducedMotion.Reduce,
                ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
            });
            context.SetDefaultTimeout(15_000);
            await context.RouteAsync("**/*", route => route.Request.Method == "GET" ? route.ContinueAsync() : route.AbortAsync());
            var page = await PrepareAsync(context, false);
            await page.BringToFrontAsync();
            await UploadAsync(page, await ReportsAsync(page, false));
            var original = await SourcesAsync(page);
            await page.Locator(".proof-page-preview").First.ClickAsync();
            await StartAsync(page);
            var cdp = await context.NewCDPSessionAsync(page);
            var metrics = (await cdp.SendAsync("Page.getLayoutMetrics"))!.Value;
            Assert.Equal(2, metrics.GetProperty("cssVisualViewport").GetProperty("zoom").GetDouble(), 4);
            Assert.Equal(1, metrics.GetProperty("cssVisualViewport").GetProperty("scale").GetDouble(), 4);
            Assert.InRange(await page.EvaluateAsync<double>("innerWidth"), width / 2.0 - 30, width / 2.0 + 1);
            await DrawAsync(page, 50, 170, 600, 170);
            await page.Locator(".proof-edit-zoom-in").ClickAsync();
            await page.Locator(".proof-edit-zoom-in").ClickAsync();
            await page.Locator(".proof-edit-stage").FocusAsync();
            await page.Keyboard.PressAsync("Alt+ArrowRight");
            var name = $"native-200-percent-{width}-{(dark ? "dark" : "light")}";
            await SaveEvidenceAsync(page, name, cdp);
            await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory(), name + ".json"), metrics.GetRawText());
            Assert.True(await page.Locator(".proof-review-dialog").EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1"));
            Assert.True(await page.Locator(".proof-review-dialog").EvaluateAsync<bool>("""
                dialog => {
                    const d = dialog.getBoundingClientRect();
                    return ['.proof-review-close', '.proof-edit-apply'].every(selector => {
                        const b = dialog.querySelector(selector).getBoundingClientRect();
                        return b.top >= d.top && b.bottom <= d.bottom;
                    });
                }
                """), "Cancel and Apply must both remain visible at native 200% zoom.");
            await ApplyAsync(page);
            AssertFlattened(original[0], (await SourcesAsync(page))[0]);
            await SaveEvidenceAsync(page, name + "-applied", cdp);
            await page.Locator(".proof-review-close").ClickAsync();
            await Assertions.Expect(page.Locator(".proof-page-preview").First).ToBeFocusedAsync();
        }
        finally { Directory.Delete(profile, recursive: true); }
    }

    private static string EvidenceDirectory()
    {
        var root = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        var path = root is null ? Path.Combine(FindRepositoryRoot(), ".artifacts", "proof-redaction", "renders")
            : Path.Combine(root, "test-results", "proof-redaction");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task SaveEvidenceAsync(IPage page, string name, ICDPSession? cdp = null)
    {
        var path = Path.Combine(EvidenceDirectory(), name + ".png");
        if (cdp is null) await page.ScreenshotAsync(new() { Path = path });
        else
        {
            // Capture the compositor at native zoom without Playwright's CSS-viewport clip.
            var screenshot = (await cdp.SendAsync("Page.captureScreenshot", new Dictionary<string, object>
            { ["format"] = "png", ["fromSurface"] = true, ["captureBeyondViewport"] = false }))!.Value;
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(screenshot.GetProperty("data").GetString()!));
        }
    }

    private static Task TouchAsync(ICDPSession cdp, string type, params (float x, float y)[] points) => cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
    { ["type"] = type, ["touchPoints"] = points.Select((p, index) => new { x = p.x, y = p.y, id = index + 1 }).ToArray() });

    private static async Task<IPage> PrepareAsync(IBrowserContext context, bool onboarding)
    {
        var page = await ProofReviewBrowserTests.PrepareAsync(context, onboarding);
        // These are intercepted requests; no application or synthetic report reaches the server.
        await context.RouteAsync("**/api/application/**", route => route.FulfillAsync(new()
        { Status = 200, ContentType = "application/json", Body = "{\"paymentRequired\":false}" }));
        while (await page.Locator(".proof-page-remove").CountAsync() > 0) await page.Locator(".proof-page-remove").First.ClickAsync();
        await page.EvaluateAsync("""
            () => {
                sessionStorage.setItem('contactEmail','synthetic@example.test');
                sessionStorage.setItem('biomarkerData', JSON.stringify({DateOfBirth:{Year:1980,Month:5,Day:20},Biomarkers:[{
                    Date:'2026-10-01',Wbc1000cellsuL:6.1,LymPc:31,McvFL:89,RdwPc:12.5,AlbGL:43,AlpUL:72,CreatUmolL:82,GluMmolL:5,CrpMgL:1.2
                }]}));
            }
            """);
        return page;
    }

    private static async Task<FilePayload[]> ReportsAsync(IPage page, bool pdf)
    {
        var images = await page.EvaluateAsync<string[]>("""
            () => [1,2].map(number => {
                const canvas = document.createElement('canvas'); canvas.width=1000; canvas.height=1300;
                const c=canvas.getContext('2d'); c.fillStyle='#fff'; c.fillRect(0,0,1000,1300);
                c.fillStyle='#183b47'; c.font='bold 36px Arial'; c.fillText('EXAMPLE LABORATORY '+number,60,85);
                c.fillStyle='#111'; c.font='28px Arial';
                ['Name: Example Athlete','Address: 123 Example Street','Patient ID: EXAMPLE-012345'].forEach((s,i)=>c.fillText(s,60,210+i*55));
                ['Collection date: 1 October 2026','Albumin: 43 g/L','Glucose: 5.0 mmol/L','Creatinine: 82 µmol/L','Source: Example Laboratory'].forEach((s,i)=>c.fillText(s,60,410+i*65));
                return canvas.toDataURL('image/png');
            })
            """);
        if (!pdf) return images.Select((source, index) => new FilePayload
        { Name = $"Synthetic report {index + 1}.png", MimeType = "image/png", Buffer = Convert.FromBase64String(source.Split(',')[1]) }).ToArray();
        // Deterministic PDF rasterization at the converter boundary. The separate
        // rendered review also exercises the real pinned PDF.js with a two-page PDF.
        await page.EvaluateAsync("""
            images => {
                window.pdfjsLib = {GlobalWorkerOptions:{},getDocument:()=>({promise:Promise.resolve({numPages:2,
                    getPage:async n=>({getViewport:()=>({width:1000,height:1300}),render:({canvasContext})=>({promise:(async()=>{
                        const image=new Image();image.src=images[n-1];await image.decode();canvasContext.drawImage(image,0,0);
                    })()})}),destroy:async()=>{}})})};
            }
            """, images);
        return [new() { Name = "Synthetic report.pdf", MimeType = "application/pdf", Buffer = Encoding.ASCII.GetBytes("%PDF synthetic converter fixture") }];
    }

    private static async Task UploadAsync(IPage page, FilePayload[] files)
    {
        await page.Locator("#proofPicInput").SetInputFilesAsync(files);
        await page.WaitForFunctionAsync("() => !document.querySelector('#proofPicInput').disabled");
    }
    private static Task<string[]> SourcesAsync(IPage page) => page.EvaluateAsync<string[]>("proofPics.slice()");
    private static Task ReadyAsync(IPage page) => page.WaitForFunctionAsync("() => !!document.querySelector('.proof-edit-sheet:not([hidden])')");
    private static async Task StartAsync(IPage page) { await page.Locator(".proof-review-hide").ClickAsync(); await ReadyAsync(page); }
    private static async Task ApplyAsync(IPage page) { await page.Locator(".proof-edit-apply").ClickAsync(); await Assertions.Expect(page.Locator(".proof-edit-stage")).ToHaveCountAsync(0); }
    private static async Task DrawAsync(IPage page, float x, float y, float width, float height)
    {
        var b = (await page.Locator(".proof-edit-sheet").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(b.X + x * b.Width / 1000, b.Y + y * b.Height / 1300);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(b.X + (x + width) * b.Width / 1000, b.Y + (y + height) * b.Height / 1300, new() { Steps = 8 });
        await page.Mouse.UpAsync();
    }
    private static void AssertFlattened(string original, string applied)
    {
        var bytes = Convert.FromBase64String(applied.Split(',')[1]);
        Assert.InRange(bytes.Length, 1, 1572864);
        using var before = Image.Load<Rgba32>(Convert.FromBase64String(original.Split(',')[1]));
        using var after = Image.Load<Rgba32>(bytes);
        Assert.Equal(before.Size, after.Size);
        Assert.Null(after.Metadata.ExifProfile);
        for (var y = 180; y < 330; y++) for (var x = 60; x < 640; x++)
        {
            var pixel = after[x,y];
            Assert.True(pixel.R < 12 && pixel.G < 12 && pixel.B < 12 && pixel.A == 255, $"Hidden pixel {x},{y} is not opaque black.");
        }
        long difference = 0; var glyphs = 0;
        for (var y = 370; y < 730; y++) for (var x = 40; x < 950; x++)
        {
            var a = before[x,y]; var b = after[x,y];
            difference += Math.Abs(a.R-b.R) + Math.Abs(a.G-b.G) + Math.Abs(a.B-b.B);
            if (b.R < 90 && b.G < 90 && b.B < 90) glyphs++;
        }
        Assert.True(difference / (360.0 * 910 * 3) < 3, "Unmarked evidence changed beyond encoding tolerance.");
        Assert.True(glyphs > 2000, "Dates, source, lab values and units must retain readable glyphs.");
    }
}
