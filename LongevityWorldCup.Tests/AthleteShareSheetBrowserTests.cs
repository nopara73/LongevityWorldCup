using Microsoft.Playwright;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// The athlete dialog's Share action opens a sheet: the card the link unfurls into,
// one copy action, and the destinations as one row of equal tiles.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteShareSheetBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(768, 1024, true)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task ShareSheet_PreviewsTheLinkCardAboveTheActions(int width, int height, bool dark)
    {
        await using var context = await CreateAsync(width, height, dark, copyFails: false);
        var page = await OpenShareSheetAsync(context);

        var preview = page.Locator("#athleteSharePreview");
        await Assertions.Expect(preview).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.athlete-share-preview')?.classList.contains('is-loaded')");
        var image = await preview.EvaluateAsync<PreviewImage>(
            "img => ({ path: new URL(img.currentSrc || img.src).pathname, naturalWidth: img.naturalWidth, naturalHeight: img.naturalHeight, ratio: img.getBoundingClientRect().width / img.getBoundingClientRect().height })");
        Assert.Equal("/og/athlete/michael-lustgarten.png", image.Path);
        Assert.Equal(1200, image.NaturalWidth);
        Assert.Equal(630, image.NaturalHeight);
        Assert.InRange(image.Ratio, 1.86, 1.95);

        var order = await page.EvaluateAsync<double[]>(
            "() => ['.athlete-share-preview', '#copyAthleteProfileLink', '.athlete-share-destinations'].map(s => document.querySelector(s).getBoundingClientRect().top)");
        Assert.True(order[0] < order[1] && order[1] < order[2], "Preview, then copy, then destinations.");
        await ExpectSheetInViewportAsync(page);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, 100)]
    [InlineData(390, 844, 100)]
    [InlineData(320, 640, 100)]
    [InlineData(320, 640, 200)]
    [InlineData(390, 844, 200)]
    [InlineData(160, 422, 100)]
    [InlineData(844, 390, 100)]
    public async Task ShareSheet_KeepsEqualTilesAndWholeLabelsAsTextGrows(int width, int height, int textPercent)
    {
        await using var context = await CreateAsync(width, height, dark: false, copyFails: false);
        var page = await OpenShareSheetAsync(context);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("() => { const menu=document.getElementById('athleteShareMenu'); menu.scrollIntoView({block:'nearest'}); }");

        var tiles = await page.Locator(".athlete-share-destinations > a").EvaluateAllAsync<Tile[]>(
            """
            links => links.map(link => {
                const box = link.getBoundingClientRect();
                const icon = link.querySelector('i').getBoundingClientRect();
                const label = [...link.childNodes].filter(node => node.nodeType === Node.TEXT_NODE && node.textContent.trim()).pop();
                const range = document.createRange();
                range.selectNodeContents(label);
                const text = range.getBoundingClientRect();
                return { top: box.top, width: box.width, height: box.height, iconBottom: icon.bottom, iconCenter: icon.left + icon.width / 2, center: box.left + box.width / 2, textTop: text.top, labelFits: [...range.getClientRects()].every(rect => rect.left >= box.left - 1 && rect.right <= box.right + 1 && rect.bottom <= box.bottom + 1) };
            })
            """);
        Assert.Equal(4, tiles.Length);
        foreach (var tile in tiles)
        {
            Assert.InRange(Math.Abs(tile.Width - tiles[0].Width), 0, 1.5);
            Assert.True(tile.LabelFits, "Every destination label's rendered glyphs must fit its tile.");
            Assert.True(tile.Height >= 56, $"Destination tiles must be at least 56px tall, got {tile.Height}.");
            Assert.InRange(Math.Abs(tile.IconCenter - tile.Center), 0, 2);
            Assert.True(tile.TextTop >= tile.IconBottom - 1, "Each destination's icon sits above its name.");
        }

        var copy = await page.Locator("#copyAthleteProfileLink").EvaluateAsync<double[]>(
            "button => { const box = button.getBoundingClientRect(); const row = document.querySelector('.athlete-share-destinations').getBoundingClientRect(); return [box.width, row.width, box.height]; }");
        Assert.InRange(Math.Abs(copy[0] - copy[1]), 0, 1);
        Assert.True(copy[2] >= 44);
        var ordered = await page.EvaluateAsync<bool>(
            """
            () => {
                const menu = document.querySelector('#athleteShareMenu');
                const preview = menu.querySelector('.athlete-share-preview');
                const copy = menu.querySelector('#copyAthleteProfileLink');
                const destinations = menu.querySelector('.athlete-share-destinations');
                const box = copy.getBoundingClientRect();
                const range = document.createRange(); range.selectNodeContents(copy);
                const labelFits = [...range.getClientRects()].every(r => r.left >= box.left - 1 && r.right <= box.right + 1 && r.top >= box.top - 1 && r.bottom <= box.bottom + 1);
                return labelFits && box.bottom <= destinations.getBoundingClientRect().top + 1
                    && (getComputedStyle(preview).display === 'none' || preview.hidden || preview.getBoundingClientRect().bottom <= box.top + 1);
            }
            """);
        Assert.True(ordered, "The full card, Copy link and destination tiles must have separate rows, including inside a scrolling sheet.");
        await ExpectSheetInViewportAsync(page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnlargedShareMenu_KeepsNativeScrollingAndLastKeyboardControlInView(bool dark)
    {
        await using var context = await CreateAsync(320, 640, dark, copyFails: false);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.querySelector('#leaderboardStatus')?.textContent === 'Leaderboard loaded.'");
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.AddStyleTagAsync(new() { Content = "::-webkit-scrollbar{width:15px;height:15px}" });
        await page.Locator("#athleteLeaderboard .athlete-name").Filter(new() { HasText = "Joe V" }).ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'joe-v' && !document.querySelector('#detailsModal .modal-content').matches('.is-loading,.has-load-error')");
        var share = page.Locator("#shareAthleteProfile");
        await share.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => document.querySelector('.athlete-share-preview')?.classList.contains('is-loaded')");
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await ExpectSheetInViewportAsync(page);

        var menu = page.Locator("#athleteShareMenu");
        Assert.True(await menu.EvaluateAsync<bool>("e => e.scrollHeight > e.clientHeight"), "Long enlarged content needs a reachable native scroller.");
        var box = (await menu.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.WheelAsync(0, 180);
        await page.WaitForFunctionAsync("() => document.getElementById('athleteShareMenu').scrollTop > 0");
        for (var n = 0; n < 4; n++)
        {
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await page.EvaluateAsync<bool>(
                """
                () => {
                    const e = document.activeElement, r = e.getBoundingClientRect();
                    return [0.15, 0.5, 0.85].every(x => [0.15, 0.5, 0.85].every(y => {
                        const hit = document.elementFromPoint(r.x + r.width * x, r.y + r.height * y);
                        return !!hit && e.contains(hit);
                    }));
                }
                """), "Every part of each focused control must remain clear of the profile controls.");
        }
        var last = page.Locator("#shareAthleteProfileEmail");
        await Assertions.Expect(last).ToBeFocusedAsync();
        Assert.True(await last.EvaluateAsync<bool>(
            """
            e => {
                const box = e.getBoundingClientRect(), menu = e.closest('#athleteShareMenu').getBoundingClientRect();
                const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
                return box.left >= 0 && box.right <= innerWidth && box.top >= Math.max(0, menu.top)
                    && box.bottom <= Math.min(innerHeight, menu.bottom) && !!hit && e.contains(hit);
            }
            """), "The last keyboard control must be fully visible and unobscured.");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(share).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("/athlete/michael-lustgarten?ctx=pheno")]
    [InlineData("/leaderboard")]
    public async Task Preview_UsesTheSharedProfilesCurrentSignatureAcrossPageContexts(string path)
    {
        await using var context = await CreateAsync(390, 844, false, false);
        // An explicit profile choice made during hydration must beat the
        // containing page's initial athlete, regardless of network timing.
        var releaseData = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            await releaseData.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.WaitForFunctionAsync("() => typeof window.openAthleteModalBySlug === 'function'");
            await page.EvaluateAsync("() => window.openAthleteModalBySlug('ron-lugbill', {suppressGuessMyAge:true})");
        }
        finally { releaseData.TrySetResult(); }
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'ron-lugbill' && !document.querySelector('#detailsModal .modal-content').matches('.is-loading,.has-load-error')");
        await page.Locator("#shareAthleteProfile").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.athlete-share-preview')?.classList.contains('is-loaded')");
        var images = App.Services.GetRequiredService<LongevityWorldCup.Website.Business.AthleteOgImageService>();
        Assert.True(images.TryGetCurrentPayload("ron-lugbill", out var payload));
        var imageUrl = await page.Locator("#athleteSharePreview").GetAttributeAsync("src");
        Assert.Equal(images.BuildVersionedImageUrl("", payload), new Uri(imageUrl!).PathAndQuery);
        Assert.EndsWith("/athlete/ron-lugbill", await page.Locator("#shareAthleteProfile").GetAttributeAsync("data-share-url"));
    }

    [Fact]
    public async Task Reopening_RefreshesMetadataAndIgnoresOldImageEvents()
    {
        await using var context = await CreateAsync(390, 844, false, false);
        var revision = 0;
        await context.RouteAsync("**/og/athlete/*/preview", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = "{\"imageUrl\":\"/og/athlete/michael-lustgarten.png?v=revision-" + ++revision + "\"}"
        }));
        var page = await OpenShareSheetAsync(context);
        await page.WaitForFunctionAsync("() => document.querySelector('.athlete-share-preview')?.classList.contains('is-loaded')");
        await page.EvaluateAsync("() => { window.__oldPreview = document.getElementById('athleteSharePreview'); }");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#shareAthleteProfile").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.athlete-share-preview')?.classList.contains('is-loaded') && document.getElementById('athleteSharePreview').src.includes('revision-2')");
        await page.EvaluateAsync("() => { window.__oldPreview.dispatchEvent(new Event('error')); window.__oldPreview.dispatchEvent(new Event('load')); }");
        await Assertions.Expect(page.Locator(".athlete-share-preview")).ToBeVisibleAsync();
        Assert.Equal(2, revision);
        await Assertions.Expect(page.Locator("#copyAthleteProfileLink")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task LateMetadata_CannotReplaceAReopenedPreview()
    {
        await using var context = await CreateAsync(390, 844, false, false);
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var settled = new TaskCompletionSource();
        var revision = 0;
        await context.RouteAsync("**/og/athlete/*/preview", async route =>
        {
            var requestRevision = ++revision;
            if (requestRevision == 1) { started.SetResult(); await release.Task; }
            try { await route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"imageUrl\":\"/og/athlete/michael-lustgarten.png?v=revision-" + requestRevision + "\"}" }); }
            catch (PlaywrightException) when (requestRevision == 1) { /* Closing aborts this request. */ }
            finally { if (requestRevision == 1) settled.TrySetResult(); }
        });
        var page = await OpenShareSheetAsync(context);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#shareAthleteProfile").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.athlete-share-preview')?.classList.contains('is-loaded')");
        release.SetResult();
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await Assertions.Expect(page.Locator("#athleteSharePreview")).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex("revision-2$"));
        await page.Locator("#copyAthleteProfileLink").ClickAsync();
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToHaveTextAsync("Copied");
    }

    [Fact]
    public async Task CopiedLink_IsConfirmedWithACheckOnTheShareAction()
    {
        await using var context = await CreateAsync(1280, 760, dark: false, copyFails: false);
        var page = await OpenShareSheetAsync(context);
        await page.Locator("#copyAthleteProfileLink").ClickAsync();

        var share = page.Locator("#shareAthleteProfile");
        await Assertions.Expect(share).ToHaveTextAsync("Copied");
        await Assertions.Expect(share.Locator("i.fa-check")).ToHaveCountAsync(1);
        await Assertions.Expect(share).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bis-copied\b"));
        await Assertions.Expect(share).ToHaveTextAsync("Share", new() { Timeout = 5000 });
        await Assertions.Expect(share.Locator("i.fa-share-nodes")).ToHaveCountAsync(1);
        await Assertions.Expect(share).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bis-copied\b"));
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1280, 760)]
    public async Task FailedCopy_GivesTheLinkTheRoomThePreviewHad(int width, int height)
    {
        await using var context = await CreateAsync(width, height, dark: true, copyFails: true);
        var page = await OpenShareSheetAsync(context);
        await page.Locator("#copyAthleteProfileLink").ClickAsync();
        await Assertions.Expect(page.Locator("#athleteShareLink")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator(".athlete-share-preview")).ToBeHiddenAsync();
        await ExpectSheetInViewportAsync(page);
    }

    private async Task<IBrowserContext> CreateAsync(int width, int height, bool dark, bool copyFails)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            RecordHarPath = Environment.GetEnvironmentVariable("LWC_SHARE_TRACE_DIRECTORY") is { Length: > 0 } trace
                ? Path.Combine(trace, $"share-{Guid.NewGuid():N}.har") : null,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('gmaSkipAll','true');" +
            "Object.defineProperty(navigator,'clipboard',{configurable:true,value:{writeText:() => " +
            (copyFails ? "Promise.reject(new DOMException('Blocked','NotAllowedError'))" : "Promise.resolve()") + "}});");
        return context;
    }

    private static async Task<IPage> OpenShareSheetAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/michael-lustgarten", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => { const c = document.querySelector('#detailsModal .modal-content'); return c?.dataset.athleteSlug === 'michael-lustgarten' && !c.matches('.is-loading,.has-load-error'); }");
        var share = page.Locator("#shareAthleteProfile");
        await share.ScrollIntoViewIfNeededAsync();
        await share.ClickAsync();
        await Assertions.Expect(page.Locator("#athleteShareMenu")).ToBeVisibleAsync();
        return page;
    }

    private static async Task ExpectSheetInViewportAsync(IPage page)
    {
        var fits = await page.Locator("#athleteShareMenu").EvaluateAsync<bool>(
            "el => { const box = el.getBoundingClientRect(); return box.left >= 0 && box.right <= innerWidth + 1 && box.top >= 0 && box.bottom <= innerHeight + 1 && el.scrollWidth <= el.clientWidth; }");
        Assert.True(fits, "The share sheet must fit the viewport without sideways scrolling.");
    }

    private sealed class PreviewImage
    {
        public string Path { get; set; } = "";
        public int NaturalWidth { get; set; }
        public int NaturalHeight { get; set; }
        public double Ratio { get; set; }
    }

    private sealed class Tile
    {
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double IconBottom { get; set; }
        public double IconCenter { get; set; }
        public double Center { get; set; }
        public double TextTop { get; set; }
        public bool LabelFits { get; set; }
    }
}
