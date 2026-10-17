using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class MediaKitBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static readonly Lazy<Dictionary<string, string>> KitFiles = new(CreateKitFiles);

    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    public async Task Kit_PreviewsWholeFilesWithBoundedThumbnailsAndDownloadsOriginalBytes(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await InstallKitFixtureAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        await Assertions.Expect(page.Locator("#fileList .media-kit-group")).ToHaveCountAsync(4);
        foreach (var image in await page.Locator(".media-file-thumb img").AllAsync())
        {
            await image.ScrollIntoViewIfNeededAsync();
            await image.EvaluateAsync("async image => { await new Promise((resolve, reject) => { if (image.src) resolve(); else { image.onload = resolve; image.onerror = reject; } }); await image.decode(); }");
            var valid = await image.EvaluateAsync<bool>("""
                image => image.naturalWidth > 0 && image.naturalHeight > 0
                    && image.naturalWidth <= 520 && image.naturalHeight <= 520
                    && getComputedStyle(image).objectFit === 'contain'
                """);
            Assert.True(valid, "The preview must show the complete file without retaining a full-size decoded image.");
        }
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.EvaluateAsync("() => document.fonts.ready");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        foreach (var path in new[] { "MediaKit/FactSheet.pdf", "MediaKit/README.txt", "MediaKit/Logos/Transparent.png", "MediaKit/Team/Portrait_lwctext.png" })
        {
            var filename = path.Split('/').Last();
            var download = await page.RunAndWaitForDownloadAsync(() =>
                page.GetByRole(AriaRole.Button, new() { Name = $"Download {filename}", Exact = true }).ClickAsync());
            Assert.Equal(filename, download.SuggestedFilename);
            Assert.Equal(Convert.FromBase64String(KitFiles.Value[path]), await File.ReadAllBytesAsync((await download.PathAsync())!));
        }
        await Assertions.Expect(page.Locator(".media-file-doc .media-file-button").First).ToHaveTextAsync("Download");
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    public async Task Documents_AreTilesThatLeadTheirFoldersGrid(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await InstallKitFixtureAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const docs = [...document.querySelectorAll('#fileList .media-file-row')].filter(row => !/\.(png|jpe?g|gif|webp|svg)$/i.test(row.querySelector('.media-file-name').textContent.trim()));
                if (!docs.length) return ['no documents'];
                for (const doc of docs) {
                    const name = doc.querySelector('.media-file-name').textContent.trim();
                    if (!doc.classList.contains('media-file-tile')) issues.push(`${name}: not a tile`);
                    const thumb = doc.querySelector('.media-file-thumb');
                    const icon = thumb?.querySelector('.media-file-icon');
                    if (!thumb || !icon) { issues.push(`${name}: no file-type well`); continue; }
                    const t = thumb.getBoundingClientRect(), i = icon.getBoundingClientRect(), b = doc.querySelector('.media-file-button').getBoundingClientRect();
                    if (i.left < t.left || i.right > t.right || i.top < t.top || i.bottom > t.bottom || i.height < 32) issues.push(`${name}: file type is not shown in its well`);
                    if (b.top < t.top - 1 || b.bottom > t.bottom + 1 || b.right > t.right + 1 || b.width < 44 || b.height < 44) issues.push(`${name}: download is not the round corner mark`);
                    const grid = doc.parentElement;
                    const firstPicture = [...grid.children].find(child => !docs.includes(child));
                    if (firstPicture && [...grid.children].indexOf(firstPicture) < [...grid.children].indexOf(doc)) issues.push(`${name}: follows a picture`);
                    if (!grid.classList.contains('media-kit-tiles')) issues.push(`${name}: not in the tile grid`);
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }

    [Fact]
    public async Task FailedArchive_PreservesTheFullKitLinkAndExistingRecoveryMessage()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.RouteAsync("**/MediaKit.zip", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fallback")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#fallback")).ToHaveTextAsync("Failed to preview. Use the ZIP link above.");
        await Assertions.Expect(page.Locator("a[href$='MediaKit.zip']").First).ToBeVisibleAsync();
        await context.UnrouteAsync("**/MediaKit.zip");
        await InstallKitFixtureAsync(context);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        await Assertions.Expect(page.Locator("#fallback")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task FailedPrimaryArchive_UsesTheFallbackAndFailedResizeStillShowsTheOriginal()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.RouteAsync("https://cdn.jsdelivr.net/**/MediaKit.zip", route => route.AbortAsync());
        await context.AddInitScriptAsync("window.createImageBitmap = async () => { throw new Error('resize unavailable'); }");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        var image = page.Locator(".media-file-thumb img[data-path='MediaKit/Logos/Logo.png']");
        await image.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(image).ToHaveJSPropertyAsync("naturalWidth", 1800);
        Assert.Equal("contain", await image.EvaluateAsync<string>("image => getComputedStyle(image).objectFit"));
        await Assertions.Expect(page.Locator("#fallback")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("FactSheet.pdf")]
    [InlineData("Transparent.png")]
    public async Task FailedFileDownload_OffersRetryWithoutLosingTheFileOrKeyboardFocus(string filename)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context, failDownloadOnce: true);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var button = page.GetByRole(AriaRole.Button, new() { Name = $"Download {filename}", Exact = true });
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#downloadStatus")).ToContainTextAsync(filename);
        await Assertions.Expect(button).ToHaveAttributeAsync("data-download-failed", "");
        await Assertions.Expect(button).ToBeFocusedAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.Keyboard.PressAsync("Enter"));
        Assert.Equal(filename, download.SuggestedFilename);
        var path = KitFiles.Value.Keys.Single(path => path.EndsWith($"/{filename}", StringComparison.Ordinal));
        Assert.Equal(Convert.FromBase64String(KitFiles.Value[path]), await File.ReadAllBytesAsync((await download.PathAsync())!));
        await Assertions.Expect(button).Not.ToHaveAttributeAsync("data-download-failed", "");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(1440, 900, false, 100)]
    [InlineData(1440, 900, true, 200)]
    [InlineData(390, 844, false, 100)]
    [InlineData(390, 844, true, 200)]
    [InlineData(320, 740, false, 100)]
    [InlineData(320, 740, true, 200)]
    [InlineData(768, 1024, true, 100)]
    public async Task PictureTiles_ShowThePictureAsTheCardWithDownloadOnItsCorner(int width, int height, bool dark, int textSize)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await InstallKitFixtureAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-tile .media-file-button").First).ToBeVisibleAsync();
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + '%'", textSize);
        await page.EvaluateAsync("() => document.fonts.ready");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                for (const tile of document.querySelectorAll('#fileList .media-file-tile')) {
                    const name = tile.querySelector('.media-file-name').textContent.trim();
                    const box = tile.getBoundingClientRect();
                    const thumb = tile.querySelector('.media-file-thumb').getBoundingClientRect();
                    const button = tile.querySelector('.media-file-button');
                    const buttonBox = button.getBoundingClientRect();
                    const caption = tile.querySelector('.media-file-name').getBoundingClientRect();
                    if (Math.abs(thumb.left - box.left) > 2 || Math.abs(thumb.right - box.right) > 2 || Math.abs(thumb.top - box.top) > 2)
                        issues.push(`${name}: the picture should fill the card edge to edge`);
                    if (buttonBox.top < thumb.top - 1 || buttonBox.bottom > thumb.bottom + 1 || buttonBox.right > thumb.right + 1)
                        issues.push(`${name}: download should sit on the picture's corner`);
                    if (buttonBox.width < 44 || buttonBox.height < 44)
                        issues.push(`${name}: download target is ${Math.round(buttonBox.width)}x${Math.round(buttonBox.height)}`);
                    if (button.getAttribute('aria-label') !== `Download ${name}`)
                        issues.push(`${name}: download lost its accessible name`);
                    if (Math.abs(caption.top - thumb.bottom) > 1)
                        issues.push(`${name}: the picture and caption have a ${caption.top - thumb.bottom}px gap`);
                    if (box.bottom - caption.bottom > 2)
                        issues.push(`${name}: the caption should close the card`);
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems.Take(6))}");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }

    [Theory]
    [InlineData(390, 844, 200, false)]
    [InlineData(320, 640, 200, true)]
    [InlineData(480, 844, 150, false)]
    [InlineData(600, 844, 200, true)]
    [InlineData(601, 844, 200, false)]
    [InlineData(768, 1024, 200, true)]
    [InlineData(844, 390, 200, false)]
    [InlineData(1440, 900, 200, true)]
    [InlineData(390, 844, 100, false)]
    [InlineData(1440, 900, 100, true)]
    public async Task DownloadsAndFileNames_KeepWholeWordsAtEnlargedPhoneText(int width, int height, int textSize, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await InstallKitFixtureAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row .media-file-button").First).ToBeVisibleAsync();
        await page.EvaluateAsync("() => document.fonts.ready");
        var normalLabelSize = await page.Locator(".media-kit-primary-download").EvaluateAsync<double>(
            "element => parseFloat(getComputedStyle(element).fontSize)");
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textSize}% !important; }}" });
        await page.WaitForFunctionAsync(
            "size => Math.abs(parseFloat(getComputedStyle(document.documentElement).fontSize) - 16 * size / 100) < 0.1", textSize);
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        Assert.InRange(await page.Locator(".media-kit-primary-download").EvaluateAsync<double>(
            "element => parseFloat(getComputedStyle(element).fontSize)"), normalLabelSize * textSize / 100.0 - 0.1, normalLabelSize * textSize / 100.0 + 0.1);

        var broken = await page.EvaluateAsync<string[]>(
            """
            () => {
                const broken = [];
                const texts = document.querySelectorAll('.media-kit-primary-download, #fileList .media-file-row :is(.media-file-name, .media-file-button)');
                for (const element of texts) {
                    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                    for (let node; (node = walker.nextNode());) {
                        for (const word of node.textContent.matchAll(/[^\s ]+/g)) {
                            const range = document.createRange();
                            range.setStart(node, word.index);
                            range.setEnd(node, word.index + word[0].length);
                            const fragments = [...range.getClientRects()].filter(rect => rect.width > 0);
                            const wholeWidth = fragments.reduce((sum, rect) => sum + rect.width, 0);
                            // Short words stay whole. Overlong names may wrap, but every
                            // fragment must remain visible inside the caption and card.
                            const style = getComputedStyle(element);
                            const contentWidth = element.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                            if (new Set(fragments.map(rect => Math.round(rect.top))).size > 1 && wholeWidth <= contentWidth)
                                broken.push(`"${word[0]}" in ${element.className}`);
                            for (const fragment of fragments) {
                                const box = element.getBoundingClientRect();
                                if (fragment.left < box.left - 1 || fragment.right > box.right + 1
                                    || fragment.top < box.top - 1 || fragment.bottom > box.bottom + 1)
                                    broken.push(`"${word[0]}" overflows ${element.className}`);
                                for (let parent = element.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
                                    const style = getComputedStyle(parent);
                                    if (!['hidden', 'clip'].includes(style.overflowX) && !['hidden', 'clip'].includes(style.overflowY)) continue;
                                    const clip = parent.getBoundingClientRect();
                                    if (fragment.left < clip.left - 1 || fragment.right > clip.right + 1
                                        || fragment.top < clip.top - 1 || fragment.bottom > clip.bottom + 1)
                                        broken.push(`"${word[0]}" is clipped by ${parent.className}`);
                                }
                            }
                        }
                    }
                    const box = element.getBoundingClientRect();
                    if (box.left < -1 || box.right > innerWidth + 1)
                        broken.push(`${element.className} leaves the screen`);
                    if (element.matches('button, a') && (box.width < 44 || box.height < 44))
                        broken.push(`${element.className} loses its touch target`);
                }
                for (const row of document.querySelectorAll('#fileList .media-file-row:not(.media-file-tile)')) {
                    const name = row.querySelector('.media-file-name').getBoundingClientRect();
                    const button = row.querySelector('.media-file-button').getBoundingClientRect();
                    if (Math.min(name.right, button.right) - Math.max(name.left, button.left) > 1
                        && Math.min(name.bottom, button.bottom) - Math.max(name.top, button.top) > 1)
                        broken.push('A document name overlaps its download control');
                }
                return broken;
            }
            """);
        Assert.True(broken.Length == 0, $"{width}x{height} at {textSize}%: {string.Join("; ", broken.Distinct().Take(8))}");
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("png")]
    public async Task OverlongNames_RemainVisibleAndDownloadWithTheirFullName(string extension)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 320, Height = 640 },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        await InstallKitFixtureAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        await page.EvaluateAsync("() => document.fonts.ready");
        var filename = $"Averylongunbrokenfilenametocheckwrappingwithenlargedtext.{extension}";
        var button = page.GetByRole(AriaRole.Button, new() { Name = $"Download {filename}", Exact = true });
        var name = button.Locator("..").Locator(".media-file-name");
        await Assertions.Expect(name).ToHaveTextAsync(filename);
        Assert.True(await name.EvaluateAsync<bool>(
            """
            name => {
                const word = name.firstChild, range = document.createRange();
                range.selectNodeContents(word);
                const fragments = [...range.getClientRects()].filter(rect => rect.width > 0);
                const box = name.getBoundingClientRect();
                return new Set(fragments.map(rect => Math.round(rect.top))).size > 1
                    && fragments.every(rect => rect.left >= box.left - 1 && rect.right <= box.right + 1
                        && rect.top >= box.top - 1 && rect.bottom <= box.bottom + 1);
            }
            """), "The unbroken filename must wrap visibly instead of being clipped or ellipsized.");
        var download = await page.RunAndWaitForDownloadAsync(() => button.TapAsync());
        Assert.Equal(filename, download.SuggestedFilename);
        var originalPath = KitFiles.Value.Keys.Single(path => path.EndsWith($"/{filename}", StringComparison.Ordinal));
        Assert.Equal(Convert.FromBase64String(KitFiles.Value[originalPath]), await File.ReadAllBytesAsync((await download.PathAsync())!));
    }

    [Fact]
    public async Task Downloads_KeepKeyboardOrderAndFocusAndDoNotCapturePreviewClicks()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        await InstallKitFixtureAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        await page.Locator(".media-kit-primary-download").FocusAsync();
        foreach (var button in await page.Locator("#fileList .media-file-button").AllAsync())
        {
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(button).ToBeFocusedAsync();
            Assert.True(await button.EvaluateAsync<bool>("button => getComputedStyle(button).outlineStyle !== 'none' && parseFloat(getComputedStyle(button).outlineWidth) >= 3"));
        }

        var preview = page.Locator(".media-file-thumb").First;
        await preview.ScrollIntoViewIfNeededAsync();
        Assert.True(await preview.EvaluateAsync<bool>("""
            preview => {
                const box = preview.getBoundingClientRect();
                return !document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2).closest('button');
            }
            """));
        var downloads = 0;
        page.Download += (_, _) => downloads++;
        await preview.ClickAsync(new() { Position = new() { X = 20, Y = 20 } });
        await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Download Athlete.png", Exact = true }).ClickAsync());
        await Assertions.Expect(page.Locator("#fileList")).ToBeVisibleAsync();
        Assert.Equal(1, downloads);
    }

    private static async Task InstallKitFixtureAsync(IBrowserContext context, bool failDownloadOnce = false)
    {
        await context.RouteAsync("**/MediaKit.zip", route => route.FulfillAsync(new() { Body = "fixture archive" }));
        // Prepare source images outside the page's preview assertions. Encoding seven
        // megapixels in browser toBlob callbacks can exhaust the five-second row wait
        // under parallel CI load. The page still decodes and resizes the large originals.
        var archiveFiles = JsonSerializer.Serialize(KitFiles.Value);
        await context.RouteAsync("**/jszip.min.js", route => route.FulfillAsync(new()
        {
            ContentType = "application/javascript",
            Body = $$$"""
                const files = new Map(Object.entries({{{archiveFiles}}}).map(([path, base64]) => [
                    path, new Blob([Uint8Array.from(atob(base64), character => character.charCodeAt(0))])
                ]));
                const failedDownloads = new Set();
                window.JSZip = { async loadAsync() {
                    return {
                        forEach(callback) { for (const path of files.keys()) callback(path, { dir: false }); },
                        file(path) { return { async async(type) {
                            if ({{{(failDownloadOnce ? "true" : "false")}}} && type === 'blob' && !failedDownloads.has(path)) {
                                failedDownloads.add(path);
                                throw new Error('File extraction failed');
                            }
                            const blob = files.get(path); return type === 'arraybuffer' ? blob.arrayBuffer() : blob;
                        } }; }
                    };
                }};
                """
        }));
    }

    private static Dictionary<string, string> CreateKitFiles() => new()
    {
        ["MediaKit/FactSheet.pdf"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("original fact sheet bytes")),
        ["MediaKit/README.txt"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("original readme bytes")),
        ["MediaKit/Averylongunbrokenfilenametocheckwrappingwithenlargedtext.txt"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("original long filename bytes")),
        ["MediaKit/Logos/Logo.png"] = CreatePicture(1800, 400),
        ["MediaKit/Logos/Transparent.png"] = CreatePicture(640, 640, transparent: true),
        ["MediaKit/Logos/Averylongunbrokenfilenametocheckwrappingwithenlargedtext.png"] = CreatePicture(400, 300),
        ["MediaKit/Team/Portrait_lwctext.png"] = CreatePicture(1200, 3600),
        ["MediaKit/Athletes/Athlete.png"] = CreatePicture(1600, 1200),
        ["MediaKit/Athletes/michael_lustgarten_with_a_long_caption.png"] = CreatePicture(640, 480)
    };

    private static string CreatePicture(int width, int height, bool transparent = false)
    {
        using var image = new Image<Rgba32>(width, height, transparent ? new Rgba32(0, 0, 0, 0) : new Rgba32(0x12, 0x67, 0x47));
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
                accessor.GetRowSpan(y)[..(width / 4)].Fill(new Rgba32(0xf0, 0xdf, 0xca));
        });
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return Convert.ToBase64String(stream.ToArray());
    }
}
