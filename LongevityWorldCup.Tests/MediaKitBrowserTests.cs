using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed partial class MediaKitBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
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
    [InlineData(1440, 900, false, 100)]
    [InlineData(1440, 900, true, 200)]
    [InlineData(390, 844, true, 100)]
    [InlineData(390, 844, false, 200)]
    [InlineData(320, 740, false, 100)]
    [InlineData(320, 740, true, 200)]
    [InlineData(195, 422, false, 100)]
    [InlineData(768, 1024, false, 200)]
    public async Task Documents_AreDownloadRowsThatLeadTheirFolder(int width, int height, bool dark, int textSize)
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
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textSize}% !important; }}" });
        await page.EvaluateAsync("() => document.fonts.ready");
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const docs = [...document.querySelectorAll('#fileList .media-file-row')].filter(row => !/\.(png|jpe?g|gif|webp|svg)$/i.test(row.querySelector('.media-file-name').textContent.trim()));
                if (!docs.length) return ['no documents'];
                for (const doc of docs) {
                    const name = doc.querySelector('.media-file-name').textContent.trim();
                    const list = doc.parentElement;
                    if (!list.classList.contains('media-kit-docs')) issues.push(`${name}: not in a document list`);
                    const thumb = doc.querySelector('.media-file-thumb');
                    const icon = thumb?.querySelector('.media-file-icon');
                    if (!thumb || !icon) { issues.push(`${name}: no file-type well`); continue; }
                    if (icon.getAttribute('aria-hidden') !== 'true') issues.push(`${name}: duplicate accessible icon text`);
                    if (!getComputedStyle(icon, '::before').content || getComputedStyle(icon, '::before').content === 'none') issues.push(`${name}: missing file-type glyph`);
                    const t = thumb.getBoundingClientRect(), i = icon.getBoundingClientRect(), b = doc.querySelector('.media-file-button').getBoundingClientRect();
                    const n = doc.querySelector('.media-file-name').getBoundingClientRect(), row = doc.getBoundingClientRect();
                    if (i.left < t.left - 1 || i.right > t.right + 1 || i.top < t.top - 1 || i.bottom > t.bottom + 1 || i.height < 24) issues.push(`${name}: file type is not shown in its well`);
                    if (Math.min(t.right, b.right) - Math.max(t.left, b.left) > 1
                        && Math.min(t.bottom, b.bottom) - Math.max(t.top, b.top) > 1)
                        issues.push(`${name}: file-type well is covered by its download control`);
                    if (b.width < 44 || b.height < 44) issues.push(`${name}: download target is ${Math.round(b.width)}x${Math.round(b.height)}`);
                    if (b.left < row.left || b.right > row.right + 1 || n.left < row.left || n.right > row.right + 1) issues.push(`${name}: the row does not hold its name and download`);
                    // A document row reads file type, name, download; when narrow, the name takes
                    // its own line below. Either way it stays as short as its contents.
                    const stacked = n.top >= t.bottom - 1;
                    if (!stacked && (t.right > n.left + 1 || n.right > b.left + 1)) issues.push(`${name}: not read left to right`);
                    if (stacked && (b.left < t.right || Math.abs(b.top + b.height / 2 - (t.top + t.height / 2)) > 2)) issues.push(`${name}: download is not beside the file type`);
                    const content = stacked ? Math.max(t.height, b.height) + n.height : Math.max(t.height, n.height, b.height);
                    const rem = parseFloat(getComputedStyle(document.documentElement).fontSize);
                    if (row.height > content + 5 * rem) issues.push(`${name}: the row is ${Math.round(row.height)}px tall for ${Math.round(content)}px of content`);
                    if (n.width < Math.min(row.width * 0.4, 120)) issues.push(`${name}: the name is squeezed to ${Math.round(n.width)}px`);
                    const group = list.parentElement;
                    const pictures = group.querySelector('.media-kit-tiles:not(.media-kit-docs)');
                    if (pictures && list.compareDocumentPosition(pictures) !== Node.DOCUMENT_POSITION_FOLLOWING) issues.push(`${name}: follows the pictures`);
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }

    [Fact]
    public async Task FolderContents_KeepEveryFileOnceAndDocumentsBeforeImagesInDownloadOrder()
    {
        var files = new Dictionary<string, string>
        {
            ["MediaKit/Mixed/A-picture.png"] = KitFiles.Value["MediaKit/Logos/Logo.png"],
            ["MediaKit/Mixed/Z-fact-sheet.PDF"] = KitFiles.Value["MediaKit/FactSheet.pdf"],
            ["MediaKit/Mixed/B-readme.txt"] = KitFiles.Value["MediaKit/README.txt"],
            ["MediaKit/Documents/B.txt"] = KitFiles.Value["MediaKit/README.txt"],
            ["MediaKit/Documents/A.pdf"] = KitFiles.Value["MediaKit/FactSheet.pdf"],
            ["MediaKit/Pictures/B.png"] = KitFiles.Value["MediaKit/Logos/Logo.png"],
            ["MediaKit/Pictures/A.png"] = KitFiles.Value["MediaKit/Logos/Transparent.png"]
        };
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await InstallKitFixtureAsync(context, kitFiles: files);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(files.Count);
        await Assertions.Expect(page.Locator(".media-kit-group-title")).ToHaveTextAsync(["Documents", "Mixed", "Pictures"]);
        var expected = new[] { "A.pdf", "B.txt", "B-readme.txt", "Z-fact-sheet.PDF", "A-picture.png", "A.png", "B.png" };
        await Assertions.Expect(page.Locator(".media-file-name")).ToHaveTextAsync(expected);
        await Assertions.Expect(page.Locator(".media-file-button")).ToHaveCountAsync(expected.Length);
        await page.Locator(".media-kit-primary-download").FocusAsync();
        foreach (var filename in expected)
        {
            var button = page.GetByRole(AriaRole.Button, new() { Name = $"Download {filename}", Exact = true });
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(button).ToBeFocusedAsync();
            var download = await page.RunAndWaitForDownloadAsync(() => page.Keyboard.PressAsync("Enter"));
            Assert.Equal(filename, download.SuggestedFilename);
            var path = files.Keys.Single(path => path.EndsWith($"/{filename}", StringComparison.Ordinal));
            Assert.Equal(Convert.FromBase64String(files[path]), await File.ReadAllBytesAsync((await download.PathAsync())!));
        }
        await Assertions.Expect(page.Locator(".media-file-doc .fa-file-pdf")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator(".media-file-doc .fa-file-lines")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task EmptyArchive_DoesNotInventFilesOrFoldersAndKeepsTheZipDownload()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context, kitFiles: new Dictionary<string, string>());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#loading")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#fileList .media-kit-group")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#fallback")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".media-kit-primary-download")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FailedArchive_PreservesTheFullKitLinkAndExistingRecoveryMessage()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.RouteAsync("**/MediaKit.zip*", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fallback")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#fallback")).ToHaveTextAsync("Failed to preview. Use the ZIP link above.");
        await Assertions.Expect(page.Locator("a.media-kit-primary-download").First).ToBeVisibleAsync();
        await context.UnrouteAsync("**/MediaKit.zip*");
        await InstallKitFixtureAsync(context);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        await Assertions.Expect(page.Locator("#fallback")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task FailedArchiveRead_RetriesAndFailedResizeStillShowsTheOriginal()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        var attempts = 0;
        await context.RouteAsync("**/MediaKit.zip*", route => ++attempts == 1 ? route.AbortAsync() : route.FulfillAsync(new() { Body = "fixture archive" }));
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
                for (const tile of document.querySelectorAll('#fileList .media-file-tile:not(.media-file-doc)')) {
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
                for (const row of document.querySelectorAll('#fileList .media-file-row')) {
                    const name = row.querySelector('.media-file-name').getBoundingClientRect();
                    const button = row.querySelector('.media-file-button').getBoundingClientRect();
                    if (Math.min(name.right, button.right) - Math.max(name.left, button.left) > 1
                        && Math.min(name.bottom, button.bottom) - Math.max(name.top, button.top) > 1)
                        broken.push('A file name overlaps its download control');
                    const control = row.querySelector('.media-file-button');
                    if (getComputedStyle(control, '::before').content === 'none')
                        broken.push('A download control has no visible icon');
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

        var preview = page.Locator(".media-file-thumb:has(img)").First;
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

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 768, true)]
    [InlineData(768, 1024, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task PressPage_BandsTheCardAndFeaturesTheKitsOwnPictures(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await InstallKitFixtureAsync(context, kitFiles: PressKitFiles());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(PressKitFiles().Count);
        await page.EvaluateAsync("() => document.fonts.ready");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const panel = document.querySelector('.media-kit-panel').getBoundingClientRect();
                const head = document.querySelector('.media-kit-head');
                if (!head) return ['no cover band'];
                for (const selector of ['.media-kit-title', '.media-kit-intro', '.media-kit-primary-download'])
                    if (!head.querySelector(selector)) issues.push(`${selector} is outside the cover band`);
                const headBox = head.getBoundingClientRect();
                if (Math.abs(headBox.top - panel.top) > 2 || getComputedStyle(head).backgroundColor === getComputedStyle(document.querySelector('.media-kit-panel')).backgroundColor)
                    issues.push('the cover is not a band at the top of the card');
                const contact = document.querySelector('.media-kit-contact').getBoundingClientRect();
                if (Math.abs(contact.bottom - panel.bottom) > 2 || Math.abs(contact.left - panel.left) > 2)
                    issues.push('the press contact does not close the card');

                const root = document.querySelector('#fileList .media-kit-group');
                if (!root?.classList.contains('media-kit-root')) return [...issues, 'the kit top level is not first'];
                const featured = root.querySelector('.media-kit-featured');
                const docs = root.querySelector('.media-kit-docs');
                if (!featured || featured.children.length !== 2) return [...issues, 'top-level pictures are not featured'];
                if (!docs || docs.querySelector('.media-file-thumb img')) issues.push('top-level documents are not their own strip');
                if (featured.compareDocumentPosition(docs) !== Node.DOCUMENT_POSITION_FOLLOWING) issues.push('featured pictures do not lead');
                const hero = featured.querySelector('.media-file-tile').getBoundingClientRect();
                const folderTile = document.querySelector('.media-kit-group:not(.media-kit-root) .media-file-tile:not(.media-file-doc)').getBoundingClientRect();
                if (hero.width < folderTile.width * 1.4) issues.push(`a featured picture (${Math.round(hero.width)}px) is not larger than a folder tile (${Math.round(folderTile.width)}px)`);
                const docTile = docs.querySelector('.media-file-tile').getBoundingClientRect();
                if (docTile.height > hero.height * 0.75) issues.push('the document strip is as tall as the featured pictures');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 768, true)]
    [InlineData(768, 1024, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task PressPage_SitsOnTheLandingSheetWithDownloadRowsWideLogosAndAContactCloser(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await InstallKitFixtureAsync(context, kitFiles: PressKitFiles());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(PressKitFiles().Count);
        await Assertions.Expect(page.Locator(".media-kit-contact")).ToContainTextAsync("Press contact:");
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const box = element => element.getBoundingClientRect();
                const panel = document.querySelector('.media-kit-panel');
                if (!panel.classList.contains('landing-sheet')) issues.push('the page is not on the landing sheet');
                const cover = document.querySelector('.media-kit-head');
                const bar = getComputedStyle(cover, '::before');
                if (bar.content === 'none' || Math.round(parseFloat(bar.height)) !== 4) issues.push('the cover has no accent bar');
                const title = box(document.querySelector('.media-kit-title')), lead = box(document.querySelector('.media-kit-intro'));
                const download = box(document.querySelector('.media-kit-primary-download'));
                if (Math.abs(title.left - lead.left) > 2 || Math.abs(title.left - download.left) > 2) issues.push('the cover is not one left edge');
                if (!['left', 'start'].includes(getComputedStyle(document.querySelector('.media-kit-title')).textAlign)) issues.push('the title is centred');
                if (innerWidth <= 560 && (Math.abs(box(panel).left) > 1 || Math.abs(box(panel).right - innerWidth) > 1)) issues.push('the phone sheet does not run edge to edge');

                const hero = box(document.querySelector('.media-kit-featured .media-file-tile'));
                for (const doc of document.querySelectorAll('.media-file-doc')) {
                    const name = doc.querySelector('.media-file-name').textContent;
                    if (box(doc).height > hero.height * 0.5) issues.push(`${name}: a ${Math.round(box(doc).height)}px block, not a download row`);
                }
                const team = [...document.querySelectorAll('.media-kit-group')].find(group => group.querySelector('.media-kit-group-title')?.textContent === 'Team');
                const teamDoc = box(team.querySelector('.media-file-doc')), teamPicture = box(team.querySelector('.media-file-tile:not(.media-file-doc)'));
                if (teamDoc.height > teamPicture.height * 0.75 && teamDoc.width < box(team).width * 0.9) issues.push('a folder document takes a picture-sized tile');

                const logos = [...document.querySelectorAll('.media-kit-group')].find(group => group.querySelector('.media-kit-group-title')?.textContent === 'Logos');
                const logoList = logos.querySelector('.media-kit-tiles');
                if (!logoList.classList.contains('media-kit-logos')) issues.push('logos are not their own wide row');
                const athlete = box(document.querySelector('.media-kit-group:not(.media-kit-root) .media-kit-tiles:not(.media-kit-logos, .media-kit-docs) .media-file-tile'));
                if (innerWidth >= 768 && box(logoList.querySelector('.media-file-tile')).width < athlete.width * 1.3) issues.push('logo wells are no wider than photo wells');

                const contact = document.querySelector('.media-kit-contact');
                if (getComputedStyle(contact).backgroundColor === getComputedStyle(cover).backgroundColor) issues.push('the contact band is not the accent closer');
                if (Math.abs(box(contact).bottom - box(panel).bottom) > 2) issues.push('the contact does not close the sheet');
                if (parseFloat(getComputedStyle(contact.querySelector('a')).fontSize) < 18) issues.push('the press address is small print');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, 100, false)]
    [InlineData(1024, 768, 100, true)]
    [InlineData(768, 1024, 100, false)]
    [InlineData(390, 844, 100, true)]
    [InlineData(1440, 900, 200, false)]
    [InlineData(390, 844, 200, false)]
    public async Task FolderSections_AreNumberedRuledSectionsWithEvenRowsInCaseBlindOrder(int width, int height, int textSize, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await InstallKitFixtureAsync(context, kitFiles: PressKitFiles());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(PressKitFiles().Count);
        await Assertions.Expect(page.Locator(".media-kit-group-title")).ToHaveTextAsync(["athletes", "Logos", "Team"]);
        await Assertions.Expect(page.Locator(".media-kit-group-count")).ToHaveTextAsync(["10", "3", "6"]);
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + '%'", textSize);
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                for (const group of document.querySelectorAll('.media-kit-group:not(.media-kit-root)')) {
                    const name = group.querySelector('.media-kit-group-title').textContent;
                    const style = getComputedStyle(group);
                    // Folders are open sections on the sheet: a hairline above, no boxed card.
                    if (parseFloat(style.borderTopWidth) < 1 || style.borderTopStyle === 'none') issues.push(`${name}: no hairline above the section`);
                    if (parseFloat(style.borderLeftWidth) > 0 && style.borderLeftStyle !== 'none') issues.push(`${name}: boxed as a card`);
                    const head = group.querySelector('.media-kit-group-head');
                    const index = head && getComputedStyle(head, '::before');
                    if (!head || !index.content.includes('counter(')) issues.push(`${name}: no section number`);
                    const tiles = [...group.querySelectorAll('.media-kit-tiles:not(.media-kit-docs) .media-file-tile')];
                    const rows = new Map();
                    for (const tile of tiles) {
                        const top = Math.round(tile.getBoundingClientRect().top);
                        rows.set(top, (rows.get(top) || 0) + 1);
                    }
                    const counts = [...rows.values()];
                    const columns = Math.max(...counts), last = counts[counts.length - 1];
                    if (columns > 1 && last < Math.ceil(columns / 2)) issues.push(`${name}: rows of ${counts.join('/')}`);
                    const spread = values => Math.max(...values) - Math.min(...values);
                    const widths = tiles.map(tile => tile.getBoundingClientRect().width);
                    if (spread(widths) > 1.5) issues.push(`${name}: uneven tile widths ${widths.map(Math.round).join('/')}`);
                    const wells = tiles.map(tile => tile.querySelector('.media-file-thumb').getBoundingClientRect().height);
                    if (spread(wells) > 1.5) issues.push(`${name}: uneven wells ${wells.map(Math.round).join('/')}`);
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height} at {textSize}%: {string.Join("; ", problems)}");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task Thumbnails_ShowTheirLoadingFilledAndFailedStatesInsteadOfAnEmptyWell()
    {
        await using var loadingContext = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await InstallKitFixtureAsync(loadingContext);
        var archive = new TaskCompletionSource();
        await loadingContext.RouteAsync("**/jszip.min.js", async route => { await archive.Task; await route.FallbackAsync(); });
        await loadingContext.AddInitScriptAsync("window.createImageBitmap = () => new Promise(() => {});");
        var page = await loadingContext.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.Commit });
        await Assertions.Expect(page.Locator("#loading .media-kit-skeleton span")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("#loading .media-kit-skeleton span").First).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#loading")).ToContainTextAsync("Loading…");
        archive.SetResult();
        var pending = page.Locator(".media-file-thumb:has(img[data-path='MediaKit/Logos/Logo.png'])");
        await pending.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(pending).ToHaveAttributeAsync("data-state", "loading");
        await Assertions.Expect(page.Locator("#loading")).ToBeHiddenAsync();

        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await InstallKitFixtureAsync(context);
        page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var photo = page.Locator(".media-file-thumb:has(img[data-path='MediaKit/Athletes/Athlete.png'])");
        var cutout = page.Locator(".media-file-thumb:has(img[data-path='MediaKit/Logos/Transparent.png'])");
        await photo.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(photo).ToHaveAttributeAsync("data-state", "ready");
        await Assertions.Expect(photo).ToHaveAttributeAsync("data-fill", "photo");
        Assert.StartsWith("url(", await photo.EvaluateAsync<string>("thumb => getComputedStyle(thumb, '::before').backgroundImage"));
        await cutout.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(cutout).ToHaveAttributeAsync("data-fill", "transparent");
        Assert.Contains("conic-gradient", await cutout.EvaluateAsync<string>("thumb => getComputedStyle(thumb).backgroundImage"));

        await using var failedContext = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await InstallKitFixtureAsync(failedContext);
        await failedContext.AddInitScriptAsync("""
            const source = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, 'src');
            Object.defineProperty(HTMLImageElement.prototype, 'src', {
                ...source,
                set(value) { source.set.call(this, location.origin + '/missing-thumbnail.png'); }
            });
            """);
        page = await failedContext.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        var failed = page.Locator(".media-kit-group").Nth(1).Locator(".media-file-thumb").First;
        await failed.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(failed).ToHaveAttributeAsync("data-state", "failed");
        await Assertions.Expect(failed.Locator("img")).ToHaveCountAsync(0);
        Assert.NotEqual("none", await failed.EvaluateAsync<string>("thumb => getComputedStyle(thumb, '::after').content"));
        var download = await page.RunAndWaitForDownloadAsync(() => failed.Locator("..").Locator(".media-file-button").ClickAsync());
        Assert.EndsWith(".png", download.SuggestedFilename);
    }

    [Fact]
    public async Task FailedDownload_MarksItsTileWithTheWarningColour()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await InstallKitFixtureAsync(context, failDownloadOnce: true);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Download Transparent.png", Exact = true });
        var normal = await button.EvaluateAsync<string>("button => getComputedStyle(button).borderTopColor");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("data-download-failed", "");
        await page.Mouse.MoveAsync(0, 0);
        var colours = await button.EvaluateAsync<string[]>("""
            button => {
                const probe = document.createElement('span');
                probe.style.color = 'var(--lwc-warning)';
                document.body.appendChild(probe);
                const warning = getComputedStyle(probe).color;
                probe.remove();
                return [getComputedStyle(button).borderTopColor, getComputedStyle(button.closest('.media-file-tile')).borderTopColor, warning];
            }
            """);
        Assert.NotEqual(normal, colours[0]);
        Assert.Equal(colours[2], colours[0]);
        Assert.Equal(colours[2], colours[1]);
    }

    [Theory]
    [InlineData(1440, 3)]
    [InlineData(1024, 3)]
    [InlineData(320, 1)]
    public async Task BalancedFolders_UseFullRowsWithoutEmptyTracksAndReflowWithText(int width, int expectedTeamColumns)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var files = PressKitFiles();
        files["MediaKit/One/Only.txt"] = KitFiles.Value["MediaKit/README.txt"];
        await InstallKitFixtureAsync(context, kitFiles: files);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/media");
        await Assertions.Expect(page.Locator(".media-file-row")).ToHaveCountAsync(files.Count);
        if (width == 320) await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        var team = page.Locator(".media-kit-group").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Team", Exact = true }) }).Locator("ul:not(.media-kit-docs)");
        await page.WaitForFunctionAsync("([list, columns]) => getComputedStyle(list).gridTemplateColumns.split(' ').length === columns",
            new object[] { await team.ElementHandleAsync() ?? throw new InvalidOperationException(), expectedTeamColumns });
        var one = page.Locator(".media-kit-group").Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "One", Exact = true }) }).Locator("ul");
        Assert.Equal(1, await one.EvaluateAsync<int>("list => getComputedStyle(list).gridTemplateColumns.split(' ').length"));
        var button = team.Locator("button").First;
        await button.FocusAsync();
        var order = await page.Locator(".media-file-name").AllTextContentsAsync();
        await page.SetViewportSizeAsync(768, 600);
        await page.SetViewportSizeAsync(width, 900);
        await Assertions.Expect(button).ToBeFocusedAsync();
        Assert.Equal(order, await page.Locator(".media-file-name").AllTextContentsAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ThumbnailResources_CloseBitmapsOnCanvasFailureAndReleaseUrlsOnDeparture()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await InstallKitFixtureAsync(context);
        await context.AddInitScriptAsync("""
            window.mediaResources = { urls: [], revoked: [], bitmaps: 0, closed: 0 };
            const create = URL.createObjectURL.bind(URL), revoke = URL.revokeObjectURL.bind(URL);
            URL.createObjectURL = blob => { const url = create(blob); mediaResources.urls.push(url); return url; };
            URL.revokeObjectURL = url => { mediaResources.revoked.push(url); revoke(url); };
            const bitmap = window.createImageBitmap.bind(window);
            window.createImageBitmap = async (...args) => {
                const value = await bitmap(...args); mediaResources.bitmaps++;
                const close = value.close.bind(value);
                value.close = () => { mediaResources.closed++; close(); };
                return value;
            };
            const draw = CanvasRenderingContext2D.prototype.drawImage;
            let fail = true;
            CanvasRenderingContext2D.prototype.drawImage = function(...args) {
                if (fail) { fail = false; throw new Error('canvas unavailable'); }
                return draw.apply(this, args);
            };
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        await Assertions.Expect(page.Locator(".media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        foreach (var image in await page.Locator(".media-file-thumb img").AllAsync())
        {
            await image.ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(image.Locator("..")).ToHaveAttributeAsync("data-state", "ready");
        }
        Assert.True(await page.EvaluateAsync<bool>("mediaResources.bitmaps > 0 && mediaResources.closed === mediaResources.bitmaps"));
        await page.EvaluateAsync("dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }))");
        Assert.True(await page.EvaluateAsync<bool>("mediaResources.urls.every(url => !mediaResources.revoked.includes(url))"));
        await page.EvaluateAsync("dispatchEvent(new PageTransitionEvent('pagehide', { persisted: false }))");
        Assert.True(await page.EvaluateAsync<bool>("mediaResources.urls.every(url => mediaResources.revoked.includes(url))"));
    }

    [Theory]
    [InlineData("FactSheet.pdf", "application/pdf")]
    [InlineData("README.txt", "text/plain")]
    [InlineData("Transparent.png", "image/png")]
    public async Task Downloads_KeepTheOriginalBytesAndDeclareTheirFileType(string filename, string type)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.AddInitScriptAsync("""
            const downloadTypes = new Map();
            window.fileDownloads = [];
            const create = URL.createObjectURL.bind(URL);
            URL.createObjectURL = blob => {
                const url = create(blob);
                downloadTypes.set(url, blob.type);
                return url;
            };
            document.addEventListener('click', event => {
                const link = event.target;
                if (link instanceof HTMLAnchorElement && link.download) {
                    fileDownloads.push({ filename: link.download, type: downloadTypes.get(link.href) });
                }
            }, true);
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        var download = await page.RunAndWaitForDownloadAsync(() =>
            page.GetByRole(AriaRole.Button, new() { Name = $"Download {filename}", Exact = true }).ClickAsync());
        Assert.Equal(filename, await page.EvaluateAsync<string>("fileDownloads.at(-1).filename"));
        Assert.Equal(type, await page.EvaluateAsync<string>("fileDownloads.at(-1).type"));
        var path = KitFiles.Value.Keys.Single(path => path.EndsWith($"/{filename}", StringComparison.Ordinal));
        Assert.Equal(Convert.FromBase64String(KitFiles.Value[path]), await File.ReadAllBytesAsync((await download.PathAsync())!));
    }

    [Fact]
    public async Task FileNames_StayLiteralAndPrototypeNamesRemainDocuments()
    {
        var files = new Dictionary<string, string>
        {
            ["MediaKit/<img onerror=alert(1)>.txt"] = KitFiles.Value["MediaKit/README.txt"],
            ["MediaKit/notes.constructor"] = KitFiles.Value["MediaKit/README.txt"]
        };
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context, kitFiles: files);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        await Assertions.Expect(page.Locator(".media-file-doc")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator(".media-file-name")).ToHaveTextAsync(["<img onerror=alert(1)>.txt", "notes.constructor"]);
        await Assertions.Expect(page.Locator("img[onerror]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Download <img onerror=alert(1)>.txt", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task TransparentThumbnails_DetectSmallCutoutsBetweenSampledPixels()
    {
        using var image = new Image<Rgba32>(520, 520, new Rgba32(0x12, 0x67, 0x47));
        image[1, 0] = new Rgba32(0, 0, 0, 0);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        var files = new Dictionary<string, string> { ["MediaKit/SmallCutout.png"] = Convert.ToBase64String(stream.ToArray()) };
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context, kitFiles: files);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        var thumb = page.Locator(".media-file-thumb");
        await Assertions.Expect(thumb).ToHaveAttributeAsync("data-state", "ready");
        await Assertions.Expect(thumb).ToHaveAttributeAsync("data-fill", "transparent");
    }

    [Theory]
    [InlineData(160, false, 100)]
    [InlineData(160, true, 100)]
    [InlineData(280, false, 200)]
    public async Task NarrowFolderDocuments_KeepTheirDrawnGlyphClearOfTheDownloadControl(int width, bool dark, int textSize)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await InstallKitFixtureAsync(context, kitFiles: PressKitFiles());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        await Assertions.Expect(page.Locator(".media-file-row")).ToHaveCountAsync(PressKitFiles().Count);
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textSize}% !important; }}" });
        await page.EvaluateAsync("() => document.fonts.ready");
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const context = document.createElement('canvas').getContext('2d');
                for (const icon of document.querySelectorAll('.media-kit-group:not(.media-kit-root) .media-file-icon')) {
                    const style = getComputedStyle(icon, '::before');
                    const box = icon.getBoundingClientRect();
                    const button = icon.closest('.media-file-tile').querySelector('button').getBoundingClientRect();
                    context.font = style.fontStyle + ' ' + style.fontWeight + ' ' + style.fontSize + ' ' + style.fontFamily;
                    const glyph = context.measureText(style.content.slice(1, -1));
                    const origin = box.left + (box.width - glyph.width) / 2;
                    const baseline = box.top + (box.height - glyph.fontBoundingBoxAscent - glyph.fontBoundingBoxDescent) / 2 + glyph.fontBoundingBoxAscent;
                    const ink = {
                        left: origin - glyph.actualBoundingBoxLeft,
                        right: origin + glyph.actualBoundingBoxRight,
                        top: baseline - glyph.actualBoundingBoxAscent,
                        bottom: baseline + glyph.actualBoundingBoxDescent
                    };
                    if (Math.min(ink.right, button.right) - Math.max(ink.left, button.left) > 1
                        && Math.min(ink.bottom, button.bottom) - Math.max(ink.top, button.top) > 1)
                        issues.push('The document glyph is covered by its download button');
                    for (let parent = icon.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
                        const style = getComputedStyle(parent);
                        if (!['hidden', 'clip'].includes(style.overflowX) && !['hidden', 'clip'].includes(style.overflowY)) continue;
                        const clip = parent.getBoundingClientRect();
                        if (ink.left < clip.left - 1 || ink.right > clip.right + 1 || ink.top < clip.top - 1 || ink.bottom > clip.bottom + 1)
                            issues.push('The drawn document glyph is clipped');
                    }
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, string.Join("; ", problems));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    private static Dictionary<string, string> PressKitFiles()
    {
        var photo = KitFiles.Value["MediaKit/Athletes/Athlete.png"];
        var files = new Dictionary<string, string>
        {
            ["MediaKit/FactSheet.pdf"] = KitFiles.Value["MediaKit/FactSheet.pdf"],
            ["MediaKit/README.txt"] = KitFiles.Value["MediaKit/README.txt"],
            ["MediaKit/Hero.jpg"] = photo,
            ["MediaKit/Hero.png"] = photo,
            ["MediaKit/Logos/Logo.png"] = KitFiles.Value["MediaKit/Logos/Logo.png"],
            ["MediaKit/Logos/Transparent.png"] = KitFiles.Value["MediaKit/Logos/Transparent.png"],
            ["MediaKit/Logos/White.png"] = KitFiles.Value["MediaKit/Logos/Logo.png"],
            ["MediaKit/Team/Bio.txt"] = KitFiles.Value["MediaKit/README.txt"]
        };
        for (var i = 0; i < 5; i++) files[$"MediaKit/Team/Member{i}.png"] = photo;
        for (var i = 0; i < 10; i++) files[$"MediaKit/athletes/athlete_{i}.jpg"] = photo;
        return files;
    }

    private static async Task InstallKitFixtureAsync(IBrowserContext context, bool failDownloadOnce = false, IReadOnlyDictionary<string, string>? kitFiles = null)
    {
        await context.RouteAsync("**/MediaKit.zip*", route => route.FulfillAsync(new() { Body = "fixture archive" }));
        // Prepare source images outside the page's preview assertions. Encoding seven
        // megapixels in browser toBlob callbacks can exhaust the five-second row wait
        // under parallel CI load. The page still decodes and resizes the large originals.
        var archiveFiles = JsonSerializer.Serialize(kitFiles ?? KitFiles.Value);
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
                        forEach(callback) {
                            callback('MediaKit/Empty/', { dir: true });
                            for (const path of files.keys()) callback(path, { dir: false });
                        },
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
