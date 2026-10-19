using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthletePortraitLoadingBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task Valerie_ShowsHerLoadedPortraitWhileTheFullImageDownloads(int width)
    {
        await using var context = await CreateContextAsync(width);
        var page = await OpenValerieAsync(context);
        var portrait = page.Locator("#modalProfilePic");
        var thumbnail = await portrait.EvaluateAsync<string>("image => image.currentSrc");
        var fullSource = await portrait.GetAttributeAsync("data-full-src");
        Assert.NotNull(fullSource);
        Assert.NotEqual(thumbnail, new Uri(App.BaseAddress, fullSource).AbsoluteUri);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync(new Regex(Regex.Escape(fullSource) + "$"), async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            await portrait.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var viewer = page.Locator("#athleteImageViewer");
            await Assertions.Expect(viewer).ToHaveClassAsync(new Regex("\\bshow\\b"));
            await SaveEvidenceAsync(page, $"portrait-{width}");

            // Inspect the painted state while the real full-image request is held.
            Assert.Equal("ready", await viewer.GetAttributeAsync("data-image-state"));
            await Assertions.Expect(viewer.Locator(".image-load-feedback")).ToBeHiddenAsync();
            await Assertions.Expect(viewer.Locator(".image-viewer-stage")).ToHaveAttributeAsync("aria-busy", "false");
            var image = viewer.Locator(".image-viewer-stage img");
            await Assertions.Expect(image).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("""
                () => {
                    const image = document.querySelector('#athleteImageViewer .image-viewer-stage img');
                    return image.complete && image.naturalWidth > 0;
                }
                """);
            Assert.Equal(thumbnail, await image.EvaluateAsync<string>("image => image.currentSrc"));
            await Assertions.Expect(viewer.Locator(".close-btn")).ToBeFocusedAsync();

            release.TrySetResult();
            var fullUrl = new Uri(App.BaseAddress, fullSource).AbsoluteUri;
            await page.WaitForFunctionAsync("""
                url => {
                    const image = document.querySelector('#athleteImageViewer .image-viewer-stage img');
                    return image.currentSrc === url && image.complete && image.naturalWidth > 0;
                }
                """, fullUrl);
            await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
            await Assertions.Expect(viewer.Locator(".close-btn")).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(viewer).ToBeHiddenAsync();
            await Assertions.Expect(portrait).ToBeFocusedAsync();
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrStalledFullImage_KeepsTheLoadedPortraitUsable(bool timeout)
    {
        await using var context = await CreateContextAsync(390);
        var page = await OpenValerieAsync(context);
        await page.Clock.InstallAsync();
        var portrait = page.Locator("#modalProfilePic");
        var thumbnail = await portrait.EvaluateAsync<string>("image => image.currentSrc");
        var fullSource = (await portrait.GetAttributeAsync("data-full-src"))!;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync(new Regex(Regex.Escape(fullSource) + "$"), async route =>
        {
            started.TrySetResult();
            if (timeout) await release.Task;
            await route.FulfillAsync(new() { Status = 503, ContentType = "text/plain", Body = "Unavailable" });
            completed.TrySetResult();
        });
        try
        {
            await portrait.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (timeout) await page.Clock.FastForwardAsync(15001);
            release.TrySetResult();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            var viewer = page.Locator("#athleteImageViewer");
            await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
            await Assertions.Expect(viewer.Locator(".image-load-feedback")).ToBeHiddenAsync();
            await Assertions.Expect(viewer.Locator(".image-viewer-stage img")).ToBeVisibleAsync();
            Assert.Equal(thumbnail, await viewer.Locator(".image-viewer-stage img").EvaluateAsync<string>("image => image.currentSrc"));
            await viewer.Locator(".close-btn").ClickAsync();
            await Assertions.Expect(viewer).ToBeHiddenAsync();
            await Assertions.Expect(portrait).ToBeFocusedAsync();
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task ClosingDuringTheUpgrade_PreventsALatePortraitFromReplacingAnotherAthlete()
    {
        await using var context = await CreateContextAsync(390);
        var page = await OpenValerieAsync(context);
        var portrait = page.Locator("#modalProfilePic");
        var fullSource = (await portrait.GetAttributeAsync("data-full-src"))!;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync(new Regex(Regex.Escape(fullSource) + "$"), async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
            completed.TrySetResult();
        });
        try
        {
            await portrait.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var viewer = page.Locator("#athleteImageViewer");
            await viewer.Locator(".close-btn").ClickAsync();
            await Assertions.Expect(viewer).ToBeHiddenAsync();
            await Assertions.Expect(portrait).ToBeFocusedAsync();

            await page.EvaluateAsync("() => window.openAthleteModalBySlug('michael-lustgarten', { suppressGuessMyAge: true })");
            await WaitForPortraitAsync(page, "michael-lustgarten");
            await portrait.ClickAsync();
            await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
            release.TrySetResult();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            Assert.Contains("michael_lustgarten", await viewer.Locator(".image-viewer-stage img").GetAttributeAsync("src"));
            await Assertions.Expect(viewer).ToBeVisibleAsync();
            await Assertions.Expect(viewer.Locator(".image-load-feedback")).ToBeHiddenAsync();
            await Assertions.Expect(viewer.Locator(".close-btn")).ToBeFocusedAsync();
        }
        finally { release.TrySetResult(); }
    }

    private Task<IBrowserContext> CreateContextAsync(int width)
        => AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = ColorScheme.Dark,
            ReducedMotion = ReducedMotion.Reduce
        });

    private static async Task<IPage> OpenValerieAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/valerie-orsoni");
        await WaitForPortraitAsync(page, "valerie-orsoni");
        await page.EvaluateAsync("() => document.fonts.ready");
        return page;
    }

    private static async Task WaitForPortraitAsync(IPage page, string slug)
    {
        await page.WaitForFunctionAsync("""
            slug => {
                const content = document.querySelector('#detailsModal .modal-content');
                const image = document.getElementById('modalProfilePic');
                return content?.dataset.athleteSlug === slug
                    && !content.classList.contains('is-loading')
                    && image.complete && image.naturalWidth > 0;
            }
            """, slug);
    }

    private static async Task SaveEvidenceAsync(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("LWC_UI_ARTIFACT_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}.png") });
    }
}
