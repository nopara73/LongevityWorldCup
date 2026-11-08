using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class MediaKitBrowserTests
{
    [Fact]
    public async Task ReducedMotion_KeepsDownloadAndPreviewGeometryStill()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await InstallKitFixtureAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Download Transparent.png", Exact = true });
        await button.HoverAsync();
        await Assertions.Expect(button).ToHaveCSSAsync("scale", "none");
        await Assertions.Expect(button).ToHaveCSSAsync("transform", "none");
        await Assertions.Expect(button.Locator("..").Locator("img")).ToHaveCSSAsync("scale", "none");
        await page.Mouse.DownAsync();
        await Assertions.Expect(button).ToHaveCSSAsync("scale", "none");
        await page.Mouse.UpAsync();
        var kit = page.Locator(".media-kit-primary-download");
        await kit.HoverAsync();
        await page.Mouse.DownAsync();
        await Assertions.Expect(kit).ToHaveCSSAsync("scale", "none");
        await page.Mouse.MoveAsync(0, 0);
        await page.Mouse.UpAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullKit_CollectsExistingAndLatePreviewsWithoutAnimatingTheGrid(bool late)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        var preview = new TaskCompletionSource();
        if (late)
            await context.RouteAsync("**/MediaKit.zip*", async route =>
            {
                if (route.Request.ResourceType == "fetch") await preview.Task;
                await route.FallbackAsync();
            });
        await context.AddInitScriptAsync("""
            window.tileWaves = 0;
            const animate = Element.prototype.animate;
            Element.prototype.animate = function(...args) {
                if (this.matches('.media-file-tile') && Array.isArray(args[0]) && args[0].some(frame => 'boxShadow' in frame)) window.tileWaves++;
                return animate.apply(this, args);
            };
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (!late) await Assertions.Expect(page.Locator(".media-file-tile")).ToHaveCountAsync(KitFiles.Value.Count);
        var kit = page.Locator(".media-kit-primary-download");
        await kit.FocusAsync();
        try
        {
            await page.RunAndWaitForDownloadAsync(() => page.Keyboard.PressAsync("Enter"));
            await Assertions.Expect(kit).ToHaveAttributeAsync("data-taken", "");
        }
        finally { preview.TrySetResult(); }
        await Assertions.Expect(page.Locator(".media-file-tile[data-collected]")).ToHaveCountAsync(KitFiles.Value.Count);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.tileWaves"));
    }

    [Fact]
    public async Task FileTake_BusyGuardsRepeatedClicksAndSupersedesThePreviousConfirmation()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.AddInitScriptAsync("""
            let zipLibrary;
            window.extractions = 0;
            Object.defineProperty(window, 'JSZip', { get: () => zipLibrary, set(value) {
                zipLibrary = { async loadAsync(...args) {
                    const zip = await value.loadAsync(...args), file = zip.file;
                    zip.file = path => {
                        const entry = file(path), extract = entry.async;
                        entry.async = async type => {
                            if (type === 'blob') {
                                window.extractions++;
                                await new Promise((resolve, reject) => { window.finishTake = resolve; window.failTake = reject; });
                            }
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
        var button = page.GetByRole(AriaRole.Button, new() { Name = "Download FactSheet.pdf", Exact = true });
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-busy", "true");
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal(1, await page.EvaluateAsync<int>("window.extractions"));
        var download = await page.RunAndWaitForDownloadAsync(() => page.EvaluateAsync("window.finishTake()"));
        Assert.Equal(Convert.FromBase64String(KitFiles.Value["MediaKit/FactSheet.pdf"]), await File.ReadAllBytesAsync((await download.PathAsync())!));
        await Assertions.Expect(button).ToHaveAttributeAsync("data-downloaded", "");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-busy", "true");
        Assert.Equal("media-take-spin", await button.EvaluateAsync<string>("e => getComputedStyle(e, '::after').animationName"));
        await page.EvaluateAsync("window.failTake(new Error('fixture extraction failure'))");
        await Assertions.Expect(button).ToHaveAttributeAsync("data-download-failed", "");
        await Assertions.Expect(button).Not.ToHaveAttributeAsync("aria-busy", "true");
        await Assertions.Expect(button).Not.ToHaveAttributeAsync("data-downloaded", "");
        await Assertions.Expect(button.Locator("..")).ToHaveAttributeAsync("data-collected", "");
        await Assertions.Expect(button).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.RunAndWaitForDownloadAsync(() => page.EvaluateAsync("window.finishTake()"));
        await Assertions.Expect(button).Not.ToHaveAttributeAsync("data-download-failed", "");
        await Assertions.Expect(page.Locator("#downloadStatus")).ToBeEmptyAsync();
        await Assertions.Expect(button).Not.ToHaveAttributeAsync("data-downloaded", "");
        await Assertions.Expect(button.Locator("..")).ToHaveAttributeAsync("data-collected", "");
    }
}
