using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class MediaKitBrowserTests
{
    [Fact]
    public async Task FirstFormatSelection_LeavesOffscreenTilesAndThePreviewInPlace()
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.NoPreference
        });
        await InstallKitFixtureAsync(context, kitFiles: PressKitFiles());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        await page.Locator(".media-file-tile[data-lwc-enter-state='wait']").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        var hero = page.Locator(".media-kit-featured .media-file-tile");
        var issues = await hero.EvaluateAsync<string[]>("""
            tile => {
                const pending = [...document.querySelectorAll('.media-file-tile[data-lwc-enter-state="wait"]')].filter(t => t !== tile);
                const image = tile.querySelector('img'), name = tile.querySelector('.media-file-name');
                const originalText = [...name.childNodes];
                const radio = tile.querySelectorAll('input')[1];
                radio.focus();
                radio.click();
                return [
                    ...(!pending.length || pending.some(t => t.dataset.lwcEnterState !== 'wait') ? ['unrelated entrances changed'] : []),
                    ...(image !== tile.querySelector('img') || originalText.some(node => node.parentNode !== name) ? ['preview or caption rebuilt'] : []),
                    ...(name.textContent !== 'Hero.jpg' || radio !== document.activeElement ? ['selection or focus lost'] : [])
                ];
            }
            """);
        Assert.Empty(issues);
    }

    [Theory]
    [InlineData(1440, ReducedMotion.NoPreference)]
    [InlineData(390, ReducedMotion.Reduce)]
    public async Task Formats_KeepTheOriginalDownloadAndFeedbackWithTheRequestedFile(int width, ReducedMotion motion)
    {
        var files = new Dictionary<string, string>
        {
            ["MediaKit/FactSheet.pdf"] = KitFiles.Value["MediaKit/FactSheet.pdf"],
            ["MediaKit/FactSheet.txt"] = KitFiles.Value["MediaKit/README.txt"],
            ["MediaKit/Team/FactSheet.txt"] = KitFiles.Value["MediaKit/README.txt"],
            ["MediaKit/Team/FactSheet.pdf"] = KitFiles.Value["MediaKit/FactSheet.pdf"]
        };
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = motion
        });
        await InstallKitFixtureAsync(context, kitFiles: files);
        await context.AddInitScriptAsync("""
            let library;
            Object.defineProperty(window, 'JSZip', { get: () => library, set(value) {
                library = { async loadAsync(...args) {
                    const zip = await value.loadAsync(...args), file = zip.file;
                    zip.file = path => {
                        const entry = file(path), extract = entry.async;
                        entry.async = async type => {
                            if (type === 'blob' && path === 'MediaKit/FactSheet.pdf')
                                await new Promise((resolve, reject) => { window.finishPdf = resolve; window.failPdf = reject; });
                            return extract(type);
                        };
                        return entry;
                    };
                    return zip;
                }};
            }});
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        var root = page.Locator(".media-kit-root .media-file-tile");
        var team = page.Locator(".media-kit-group:not(.media-kit-root)");
        await Assertions.Expect(root).ToHaveCountAsync(1);
        await Assertions.Expect(team.Locator(".media-file-tile")).ToHaveCountAsync(1);
        await Assertions.Expect(team.Locator(".media-kit-group-count")).ToHaveTextAsync("2");
        var group = root.GetByRole(AriaRole.Radiogroup, new() { Name = "FactSheet formats" });
        var pdf = group.GetByRole(AriaRole.Radio).First;
        var txt = group.GetByRole(AriaRole.Radio).Last;
        var take = root.GetByRole(AriaRole.Button, new() { NameRegex = new("^Download ") });
        await take.ClickAsync();
        await Assertions.Expect(take).ToHaveAttributeAsync("aria-busy", "true");
        await pdf.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(txt).ToBeCheckedAsync();
        await Assertions.Expect(txt).ToBeFocusedAsync();
        await Assertions.Expect(take).Not.ToHaveAttributeAsync("aria-busy", "true");
        await Assertions.Expect(team.GetByRole(AriaRole.Radio).First).ToBeCheckedAsync();
        var download = await page.RunAndWaitForDownloadAsync(() => page.EvaluateAsync("window.finishPdf()"));
        Assert.Equal("FactSheet.pdf", download.SuggestedFilename);
        Assert.Equal(Convert.FromBase64String(files["MediaKit/FactSheet.pdf"]), await File.ReadAllBytesAsync((await download.PathAsync())!));
        Assert.Null(await root.GetAttributeAsync("data-collected"));
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(take).ToBeFocusedAsync();
        download = await page.RunAndWaitForDownloadAsync(() => page.Keyboard.PressAsync("Enter"));
        Assert.Equal("FactSheet.txt", download.SuggestedFilename);
        Assert.Equal(Convert.FromBase64String(files["MediaKit/FactSheet.txt"]), await File.ReadAllBytesAsync((await download.PathAsync())!));

        await pdf.ClickAsync();
        await Assertions.Expect(root).ToHaveAttributeAsync("data-collected", "");
        await take.ClickAsync();
        await page.EvaluateAsync("window.failPdf(new Error('fixture extraction failure'))");
        await Assertions.Expect(take).ToHaveAttributeAsync("data-download-failed", "");
        await Assertions.Expect(page.Locator("#downloadStatus")).ToContainTextAsync("FactSheet.pdf");
        await txt.ClickAsync();
        Assert.Null(await take.GetAttributeAsync("data-download-failed"));
        Assert.Null(await take.GetAttributeAsync("aria-description"));
        Assert.Null(await take.GetAttributeAsync("title"));
        await Assertions.Expect(page.Locator("#downloadStatus")).ToBeEmptyAsync();
    }
}
