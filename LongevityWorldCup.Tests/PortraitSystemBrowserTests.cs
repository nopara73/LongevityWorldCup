using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PortraitSystemBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Photo = "/generated/thumbs/athletes/first_thumb_sm_abcdef.webp";
    private const string Svg = "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><rect width='96' height='96' fill='teal'/></svg>";

    private async Task<IBrowserContext> ContextAsync()
    {
        var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.NoPreference });
        await context.RouteAsync("**/generated/thumbs/athletes/*", route => route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = Svg }));
        await context.RouteAsync("**/original.webp", route => route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = Svg }));
        await context.RouteAsync("**/portrait-fixture", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = """
                <!doctype html><html><head><link rel="stylesheet" href="/css/portrait-system.css">
                <style>img{width:64px;height:64px;border:3px solid gold;border-radius:50%}</style>
                <script src="/js/portrait-system.js" defer></script></head><body></body></html>
                """
        }));
        return context;
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/portrait-fixture");
        await page.WaitForFunctionAsync("() => !!window.LwcPortrait");
        return page;
    }

    private static Task AddAsync(IPage page, string source = Photo) => page.EvaluateAsync("""
        src => { const img = document.createElement('img'); img.id='testPortrait'; img.className='portrait';
            img.alt='First Athlete'; img.src=src; document.body.append(img); }
        """, source);

    private static Task ReadyAsync(IPage page) => page.WaitForFunctionAsync("""
        () => testPortrait.complete && testPortrait.naturalWidth > 0 && testPortrait.dataset.lwcPortraitState === 'ready'
        """);

    internal static async Task AssertDecodedFallbackAsync(ILocator image, string? initials = null)
    {
        await Assertions.Expect(image).ToHaveAttributeAsync("data-lwc-portrait-fallback", "");
        await Assertions.Expect(image).ToHaveAttributeAsync("data-lwc-portrait-state", "ready");
        Assert.True(await image.EvaluateAsync<bool>("image => image.complete && image.naturalWidth > 0"));
        var source = Uri.UnescapeDataString(await image.GetAttributeAsync("src") ?? "");
        Assert.StartsWith("data:image/svg+xml", source);
        if (initials is not null) Assert.Equal(initials,
            System.Text.RegularExpressions.Regex.Match(source, "<text\\b[^>]*>([^<]*)</text>").Groups[1].Value);
        else Assert.DoesNotContain("<text", source);
    }

    [Fact]
    public async Task ChampionHalo_RemainsPresentDuringPortraitDevelop()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        var image = page.Locator(".podium-item.first .podium-portrait");
        await Assertions.Expect(image).ToHaveAttributeAsync("data-lwc-portrait-state", "ready");
        var halo = await image.EvaluateAsync<string>("image => getComputedStyle(image).filter");
        Assert.Contains("drop-shadow(", halo);
        var developing = await image.EvaluateAsync<string>("""
            image => {
                image.dataset.lwcPortraitState = 'developing';
                return getComputedStyle(image).filter;
            }
            """);
        Assert.StartsWith(halo, developing);
    }

    [Fact]
    public async Task ImageThatFailedBeforeEnhancement_RecoversInsteadOfShimmeringForever()
    {
        await using var context = await ContextAsync();
        await context.RouteAsync("**/already-broken.webp", route => route.FulfillAsync(new() { Status = 404, Body = "" }));
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            async () => {
                const image=document.createElement('img'); image.id='testPortrait'; image.alt='Cached Athlete';
                const failed=new Promise(resolve=>image.addEventListener('error',resolve,{once:true}));
                image.src='/already-broken.webp'; document.body.append(image);
                await failed;
                window.LwcPortrait.enhance(image);
            }
            """);
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("data-lwc-portrait-fallback", "");
        await ReadyAsync(page);
        Assert.Contains(">CA</text>", Uri.UnescapeDataString(await page.Locator("#testPortrait").GetAttributeAsync("src") ?? ""));
    }

    [Fact]
    public async Task SlowRecycledImage_DoesNotPaintThePreviousBitmapWhileItsReplacementLoads()
    {
        await using var context = await ContextAsync();
        await context.RouteAsync("**/generated/thumbs/athletes/*", route => route.FulfillAsync(new()
        {
            ContentType = "image/svg+xml", Body = Svg.Replace("teal", "red")
        }));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/second.webp", async route =>
        {
            await release.Task;
            await route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = Svg });
        });
        var page = await OpenAsync(context);
        await AddAsync(page);
        await ReadyAsync(page);
        try
        {
            await page.EvaluateAsync("() => {testPortrait.src='/second.webp';testPortrait.alt='Second Athlete'}");
            await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("data-lwc-portrait-state", "loading");
            using var frame = Image.Load<Rgba32>(await page.Locator("#testPortrait").ScreenshotAsync());
            var pixel = frame[frame.Width / 2, frame.Height / 2];
            Assert.False(pixel.R > 200 && pixel.G < 80, "The new athlete is still displaying the previous athlete's red bitmap.");
            await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("alt", "Second Athlete");
        }
        finally { release.TrySetResult(); }
        await ReadyAsync(page);
    }

    [Fact]
    public async Task ClearingSource_ReleasesOwnedCandidatesAndCannotRetainOldIdentity()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await AddAsync(page);
        await ReadyAsync(page);
        Assert.NotEmpty(await page.Locator("#testPortrait").GetAttributeAsync("srcset") ?? "");
        await page.EvaluateAsync("() => testPortrait.removeAttribute('src')");
        await Assertions.Expect(page.Locator("#testPortrait")).Not.ToHaveAttributeAsync("srcset", new System.Text.RegularExpressions.Regex(".+"));
        await Assertions.Expect(page.Locator("[data-lwc-portrait-ghost]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("data-lwc-portrait-state", "loading");
    }

    [Fact]
    public async Task ThumbnailFailure_HandsBackToThePagesOriginalPhotoCandidate()
    {
        await using var context = await ContextAsync();
        await context.RouteAsync("**/generated/thumbs/athletes/*", route => route.FulfillAsync(new() { Status = 404, Body = "" }));
        await context.RouteAsync("**/original.webp", route => route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = Svg }));
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            src => { const img=document.createElement('img'); img.id='testPortrait'; img.className='portrait'; img.alt='First Athlete';
                let attempt=0; img.onerror=()=>{ img.src=++attempt===1 ? src.replace('_sm_', '_md_') : '/original.webp'; };
                img.src=src; document.body.append(img); }
            """, Photo);
        await ReadyAsync(page);
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("src", "/original.webp");
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("data-lwc-portrait-fallback"));
    }

    [Theory]
    [InlineData("bioage-rank-row-avatar")]
    [InlineData("lmx-participant-avatar")]
    [InlineData("lmx-mention-avatar")]
    [InlineData("lmx-quote-portrait")]
    [InlineData("lmx-discussion-quick-avatar")]
    [InlineData("lmx-discussion-author-avatar")]
    [InlineData("ro-step-portrait")]
    public async Task ExistingPortraitWrappers_EnhanceTheirImage(string wrapper)
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            ([wrapper,src]) => {const host=document.createElement('span');host.className=wrapper;
                const img=document.createElement('img');img.id='testPortrait';img.src=src;host.append(img);document.body.append(host);}
            """, new[] { wrapper, Photo });
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("data-lwc-portrait-tier", "sm");
        await ReadyAsync(page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OffscreenLoadingPortrait_AnimatesOnlyInViewAndRespectsRuntimeMotionChanges(bool legacyMediaFade)
    {
        await using var context = await ContextAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/pending.webp", async route =>
        {
            await release.Task;
            await route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = Svg });
        });
        var page = await OpenAsync(context);
        if (legacyMediaFade) await page.EvaluateAsync("() => document.documentElement.classList.add('lwc-media-fade')");
        try
        {
            await page.AddStyleTagAsync(new() { Content = "body{min-height:3200px}#testPortrait{display:block;margin-top:2000px}" });
            await AddAsync(page, "/pending.webp");
            var image = page.Locator("#testPortrait");
            if (legacyMediaFade) await image.EvaluateAsync("image => image.dataset.lwcMedia='ready'");
            await Assertions.Expect(image).ToHaveAttributeAsync("data-lwc-portrait-state", "loading");
            await page.EvaluateAsync("() => new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))");
            Assert.Empty(await image.EvaluateAsync<string[]>("image=>image.getAnimations().filter(a=>a.playState==='running').map(a=>a.animationName)"));
            await image.ScrollIntoViewIfNeededAsync();
            await page.WaitForFunctionAsync("() => testPortrait.getAnimations().some(a=>a.animationName==='lwc-portrait-shimmer'&&a.playState==='running')");
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
            Assert.Empty(await image.EvaluateAsync<string[]>("image=>image.getAnimations().filter(a=>a.playState==='running').map(a=>a.animationName)"));
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            await page.WaitForFunctionAsync("() => testPortrait.getAnimations().some(a=>a.animationName==='lwc-portrait-shimmer'&&a.playState==='running')");
            await page.EvaluateAsync("() => window.scrollTo(0,0)");
            await page.WaitForFunctionAsync("() => testPortrait.getAnimations().every(a=>a.playState!=='running')");
            await Assertions.Expect(image).ToHaveAttributeAsync("data-lwc-portrait-state", "loading");
        }
        finally { release.TrySetResult(); }
        await ReadyAsync(page);
    }

    [Fact]
    public async Task RecycledPortrait_WaitsForCurrentDecodeAndNeverOverlaysThePreviousAthlete()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await AddAsync(page);
        await ReadyAsync(page);
        await page.EvaluateAsync("""
            () => {window.decodeReleases=[]; testPortrait.decode=()=>new Promise(resolve=>decodeReleases.push(resolve));
                testPortrait.alt='Second Athlete';testPortrait.src='/generated/thumbs/athletes/second_thumb_sm_abcdef.webp';}
            """);
        await page.WaitForFunctionAsync("() => testPortrait.complete && testPortrait.naturalWidth > 0");
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("data-lwc-portrait-state", "loading");
        await Assertions.Expect(page.Locator("[data-lwc-portrait-ghost]")).ToHaveCountAsync(0);
        await page.EvaluateAsync("""
            () => {const old=decodeReleases.splice(0);testPortrait.src='/generated/thumbs/athletes/third_thumb_sm_abcdef.webp';
                testPortrait.alt='Third Athlete';queueMicrotask(()=>old.forEach(resolve=>resolve()));}
            """);
        await page.WaitForFunctionAsync("() => testPortrait.complete && testPortrait.naturalWidth > 0 && decodeReleases.length > 0");
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("data-lwc-portrait-state", "loading");
        await page.EvaluateAsync("() => decodeReleases.splice(0).forEach(resolve=>resolve())");
        await ReadyAsync(page);
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("alt", "Third Athlete");
    }

    [Fact]
    public async Task DetachedRows_ReleaseResizeObservationAndCanBeReinsertedWithoutDuplicateInitialization()
    {
        await using var context = await ContextAsync();
        await context.AddInitScriptAsync("""
            const Native=ResizeObserver;window.portraitObserved=new Set();
            window.ResizeObserver=class extends Native{
                observe(el,...args){portraitObserved.add(el);return super.observe(el,...args);}
                unobserve(el){portraitObserved.delete(el);return super.unobserve(el);}
            };
            """);
        var page = await OpenAsync(context);
        await AddAsync(page);
        await ReadyAsync(page);
        await page.EvaluateAsync("() => {window.detached=testPortrait;testPortrait.remove()}");
        await page.WaitForFunctionAsync("() => portraitObserved.size === 0", null, new() { Timeout = 3000 });
        await page.EvaluateAsync("() => document.body.append(detached)");
        await ReadyAsync(page);
        await page.AddScriptTagAsync(new() { Url = "/js/portrait-system.js" });
        Assert.Equal(1, await page.EvaluateAsync<int>("portraitObserved.size"));
    }

    [Fact]
    public async Task PageOwnedSrcsetAndSizes_AreNeverOverwritten()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await AddAsync(page);
        await ReadyAsync(page);
        await page.EvaluateAsync("""
            () => {testPortrait.srcset='/original.webp 500w';testPortrait.sizes='37px';testPortrait.style.width='200px';}
            """);
        await page.EvaluateAsync("() => new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))");
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("srcset", "/original.webp 500w");
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("sizes", "37px");
    }

    [Fact]
    public async Task MonogramRecycling_RetainsAltAndUpdatesIdentityWithoutRequestLoops()
    {
        await using var context = await ContextAsync();
        await context.RouteAsync("**/missing.webp", route => route.FulfillAsync(new() { Status = 404, Body = "" }));
        var page = await OpenAsync(context);
        await AddAsync(page, "/missing.webp");
        await ReadyAsync(page);
        var image = page.Locator("#testPortrait");
        await Assertions.Expect(image).ToHaveAttributeAsync("data-lwc-portrait-fallback", "");
        await page.EvaluateAsync("() => testPortrait.alt='Élodie Example'");
        await page.WaitForFunctionAsync("() => decodeURIComponent(testPortrait.src).includes('ÉE')");
        await Assertions.Expect(image).ToHaveAttributeAsync("alt", "Élodie Example");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        Assert.Equal("none", await image.EvaluateAsync<string>("img=>getComputedStyle(img).animationName"));
    }
}
