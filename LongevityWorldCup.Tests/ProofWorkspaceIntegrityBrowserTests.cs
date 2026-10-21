using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProofWorkspaceIntegrityBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReuploadedRemoval_AtTheLimitUndoPreservesTheNewOrderAndOriginalBytes(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, onboarding);
        var files = CreatePages(37);
        await UploadAsync(page, files);
        var original = await SourcesAsync(page);
        await page.Locator(".proof-page-remove").Nth(17).ClickAsync();
        await UploadAsync(page, [files[17]]);
        var reordered = original.Where((_, index) => index != 17).Append(original[17]).ToArray();
        Assert.Equal(reordered, await SourcesAsync(page));
        await Assertions.Expect(page.Locator(".proof-undo")).ToBeEnabledAsync();
        await page.Locator(".proof-undo").ClickAsync();
        Assert.Equal(reordered, await SourcesAsync(page));
        Assert.Equal(reordered, await page.EvaluateAsync<string[]>("proofPics"));
        Assert.Equal(37, (await SourcesAsync(page)).Distinct().Count());
        await Assertions.Expect(page.Locator(".proof-page-preview").Last).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidImageFallbacks_DoNotBecomeSubmittablePagesAndValidOriginalsSurvive(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, onboarding);
        var files = CreatePages(2);
        await UploadAsync(page, [files[0]]);
        var original = await SourcesAsync(page);
        await page.EvaluateAsync("() => { window.optimizeImageClient = async () => { throw new Error('Encoder unavailable'); }; }");
        await UploadAsync(page,
        [
            new() { Name = "corrupt.png", MimeType = "image/png", Buffer = [1, 2, 3] },
            new() { Name = "oversized.png", MimeType = "image/png", Buffer = new byte[8 * 1024 * 1024] },
            files[1]
        ]);
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToHaveTextAsync("Some proof files could not be processed. Please try them again as images or PDFs.");
        await page.Locator("#custom-alert-close").ClickAsync();
        var sources = await SourcesAsync(page);
        Assert.Equal(new[] { original[0], "data:image/png;base64," + Convert.ToBase64String(files[1].Buffer) }, sources);
        Assert.Equal(sources, await page.EvaluateAsync<string[]>("proofPics"));
        Assert.Equal(files[1].Name, await page.Locator(".proof-page-source").Last.InnerTextAsync());
        Assert.Equal(0, await page.Locator(".proof-page-pending, .proof-preparation").CountAsync());
        Assert.True(await page.Locator(onboarding ? "#nextButton" : "#submitButton").IsEnabledAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingFileSelections_ArePreparedInOrderWithoutAReadyGap(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, onboarding);
        var files = CreatePages(3);
        await UploadAsync(page, [files[0]]);
        var original = await SourcesAsync(page);
        await page.EvaluateAsync("""
            () => {
                window.__proofWaiters = [];
                window.optimizeImageClient = async raw => {
                    await new Promise(resolve => window.__proofWaiters.push(resolve));
                    return {dataUrl: raw};
                };
            }
            """);
        await page.Locator("#proofPicInput").SetInputFilesAsync(files[1]);
        await page.WaitForFunctionAsync("() => window.__proofWaiters.length === 1");
        // File selection APIs can deliver a second change while the input is disabled.
        await page.Locator("#proofPicInput").SetInputFilesAsync(files[2]);
        await page.EvaluateAsync("window.__proofWaiters[0]()");
        await page.WaitForFunctionAsync("() => window.__proofWaiters.length === 2");
        Assert.True(await page.Locator(onboarding ? "#nextButton" : "#submitButton").IsDisabledAsync());
        await page.Locator(".biomarker-checkbox").First.CheckAsync();
        Assert.True(await page.Locator(onboarding ? "#nextButton" : "#submitButton").IsDisabledAsync());
        await page.EvaluateAsync("window.__proofWaiters[1]()");
        await page.WaitForFunctionAsync("() => !document.querySelector('#proofPicInput').disabled");
        Assert.Equal(new[] { original[0], "data:image/png;base64," + Convert.ToBase64String(files[1].Buffer), "data:image/png;base64," + Convert.ToBase64String(files[2].Buffer) }, await SourcesAsync(page));
        Assert.Equal(0, await page.Locator(".proof-page-pending, .proof-preparation").CountAsync());
    }

    [Fact]
    public async Task PendingFiles_EachHaveAPlaceholderUntilPreparationCompletes()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, false);
        await page.EvaluateAsync("""
            () => {
                window.optimizeImageClient = async raw => {
                    if (!window.__proofReleased) await new Promise(resolve => window.__releaseProof = () => {window.__proofReleased = true; resolve();});
                    return {dataUrl: raw};
                };
            }
            """);
        await page.Locator("#proofPicInput").SetInputFilesAsync(CreatePages(8));
        await page.WaitForFunctionAsync("() => typeof window.__releaseProof === 'function'");
        await Assertions.Expect(page.Locator(".proof-page-pending")).ToHaveCountAsync(8);
        await page.EvaluateAsync("window.__releaseProof()");
        await page.WaitForFunctionAsync("() => !document.querySelector('#proofPicInput').disabled");
        await Assertions.Expect(page.Locator(".proof-page-card")).ToHaveCountAsync(8);
        await Assertions.Expect(page.Locator(".proof-page-pending")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task LongReport_OpensOnTheCurrentThumbnailAndKeyboardSelectionMovesFocus()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.Reduce });
        var page = await PrepareAsync(context, false);
        await UploadAsync(page, CreatePages(37));
        await page.Locator(".proof-page-preview").Last.ClickAsync();
        var current = page.Locator(".proof-review-thumb[aria-current='true']");
        await Assertions.Expect(current).ToHaveAccessibleNameAsync("Proof page 37");
        await ExpectCurrentThumbVisibleAsync(page);
        await current.FocusAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(current).ToHaveAccessibleNameAsync("Proof page 36");
        await Assertions.Expect(current).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Home");
        await Assertions.Expect(current).ToHaveAccessibleNameAsync("Proof page 1");
        await Assertions.Expect(current).ToBeFocusedAsync();
        await ExpectCurrentThumbVisibleAsync(page);
        await page.Keyboard.PressAsync("End");
        await Assertions.Expect(current).ToHaveAccessibleNameAsync("Proof page 37");
        await Assertions.Expect(current).ToBeFocusedAsync();
        await ExpectCurrentThumbVisibleAsync(page);
        await Assertions.Expect(page.Locator(".proof-review-filmstrip [tabindex='0']")).ToHaveCountAsync(1);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(844, 250, false)]
    [InlineData(390, 300, true)]
    [InlineData(320, 844, true)]
    public async Task Viewer_ShowsWholeMixedAspectPagesAndKeepsControlsReachable(int width, int height, bool enlargedText)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce });
        var page = await PrepareAsync(context, false);
        await UploadAsync(page, CreatePages(3));
        if (enlargedText) await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.Locator(".proof-page-preview").First.ClickAsync();
        for (var index = 0; index < 3; index++)
        {
            await page.Locator(".proof-review-thumb").Nth(index).ClickAsync();
            await page.Locator(".proof-review-stage img").EvaluateAsync("image => image.decode()");
            var geometry = await page.EvaluateAsync<double[]>("""
                () => {
                    const stage = document.querySelector('.proof-review-stage');
                    const image = stage.querySelector('img');
                    const s = stage.getBoundingClientRect(), i = image.getBoundingClientRect();
                    const dialog = document.querySelector('.proof-review-dialog');
                    return [s.height, i.width, i.height, image.naturalWidth, image.naturalHeight,
                        i.left-s.left, s.right-i.right, i.top-s.top, s.bottom-i.bottom,
                        dialog.scrollWidth-dialog.clientWidth];
                }
                """);
            Assert.True(geometry[0] >= 44, "The page canvas must retain usable height.");
            Assert.InRange(geometry[1] / geometry[2], geometry[3] / geometry[4] - 0.01, geometry[3] / geometry[4] + 0.01);
            Assert.True(geometry[1] <= geometry[3] + 1 && geometry[2] <= geometry[4] + 1, "Fit page must not upscale small originals.");
            Assert.All(geometry[5..9], value => Assert.True(value >= -1, "Fit page must stay wholly inside the canvas."));
            Assert.InRange(geometry[9], 0, 1);
        }
        await page.Locator(".proof-review-zoom").ClickAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.True(await page.Locator(".proof-review-stage").EvaluateAsync<bool>("e => e.scrollLeft > 0"));
        await page.Locator(".proof-review-close").ClickAsync();
        Assert.False(await page.Locator(".proof-review-dialog").IsVisibleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingPreview_LeavesEveryCornerOfRemoveAsItsOwnPointerAndKeyboardTarget(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.Reduce });
        var page = await PrepareAsync(context, onboarding);
        await UploadAsync(page, CreatePages(2));
        await page.Locator(".proof-page-preview").First.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".proof-page-preview").First).ToBeFocusedAsync();
        var remove = page.Locator(".proof-page-remove").First;
        await remove.ScrollIntoViewIfNeededAsync();
        Assert.True(await remove.EvaluateAsync<bool>("""
            button => {
                const b = button.getBoundingClientRect();
                return [[1,1],[b.width-1,1],[1,b.height-1],[b.width-1,b.height-1],[b.width/2,b.height/2]]
                    .every(([x,y]) => button.contains(document.elementFromPoint(b.left+x,b.top+y)));
            }
            """));
        await remove.ClickAsync(new() { Position = new() { X = 1, Y = 1 } });
        await Assertions.Expect(page.Locator(".proof-page-card")).ToHaveCountAsync(1);
        await page.Locator(".proof-undo").ClickAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".proof-page-remove").First).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".proof-page-card")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".proof-review-dialog")).ToBeHiddenAsync();
    }

    private static async Task ExpectCurrentThumbVisibleAsync(IPage page) => Assert.True(await page.EvaluateAsync<bool>("""
        () => {
            const t = document.querySelector('.proof-review-thumb[aria-current]');
            const a = t.getBoundingClientRect(), b = t.parentElement.getBoundingClientRect();
            return a.left >= b.left && a.right <= b.right;
        }
        """));

    private static async Task<IPage> PrepareAsync(IBrowserContext context, bool onboarding)
    {
        var page = await ProofReviewBrowserTests.PrepareAsync(context, onboarding);
        while (await page.Locator(".proof-page-remove").CountAsync() > 0)
            await page.Locator(".proof-page-remove").First.ClickAsync();
        return page;
    }

    private static async Task UploadAsync(IPage page, FilePayload[] files)
    {
        await page.Locator("#proofPicInput").SetInputFilesAsync(files);
        await page.WaitForFunctionAsync("() => !document.querySelector('#proofPicInput').disabled");
    }

    private static Task<string[]> SourcesAsync(IPage page) => page.Locator("#proofImageContainer img").EvaluateAllAsync<string[]>("images => images.map(image => image.src)");

    private static FilePayload[] CreatePages(int count) => Enumerable.Range(0, count).Select(index =>
    {
        var (width, height) = (index % 3) switch { 0 => (300, 420), 1 => (640, 260), _ => (110, 110) };
        using var image = new Image<Rgba32>(width, height, new Rgba32((byte)(20 + index * 5), 120, 170));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return new FilePayload { Name = $"Laboratory collection and biomarker report {index + 1}.png", MimeType = "image/png", Buffer = stream.ToArray() };
    }).ToArray();
}
