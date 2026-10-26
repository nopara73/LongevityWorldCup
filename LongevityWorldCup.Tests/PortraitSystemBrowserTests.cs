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
    private const string MissingPhoto = "/missing-portrait.webp";
    private const string Svg = "<svg xmlns='http://www.w3.org/2000/svg' width='96' height='96'><rect width='96' height='96' fill='teal'/></svg>";

    private async Task<IBrowserContext> ContextAsync()
    {
        var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.NoPreference });
        await context.RouteAsync("**/generated/thumbs/athletes/*", route => route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = Svg }));
        await context.RouteAsync("**/original.webp", route => route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = Svg }));
        await context.RouteAsync("**/missing-portrait.webp", route => route.FulfillAsync(new() { Status = 404, Body = "" }));
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

    [Theory]
    [InlineData("/assets/content-images/play-athlete-placeholder.webp")]
    [InlineData("/assets/content-images/play-athlete-placeholder.jpg")]
    [InlineData("/assets/HdLogo_thumb_sm.png")]
    public async Task IntentionalArtwork_StaysOriginalEvenOnAPortraitSurface(string source)
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await AddAsync(page, source);
        var image = page.Locator("#testPortrait");
        await image.EvaluateAsync("image=>image.decode()");
        await page.EvaluateAsync("() => window.LwcPortrait.scan()");
        await Assertions.Expect(image).ToHaveAttributeAsync("src", source);
        Assert.Null(await image.GetAttributeAsync("data-lwc-portrait"));
        Assert.Null(await image.GetAttributeAsync("data-lwc-portrait-fallback"));
        Assert.True(await image.EvaluateAsync<bool>("image=>image.complete&&image.naturalWidth>0"));
        await page.EvaluateAsync("source=>{testPortrait.src=source;testPortrait.alt='A real athlete'}", Photo);
        await ReadyAsync(page);
        await Assertions.Expect(image).ToHaveAttributeAsync("src", Photo);
    }

    private static Task AddAsync(IPage page, string source = Photo) => page.EvaluateAsync("""
        src => { const img = document.createElement('img'); img.id='testPortrait'; img.className='portrait';
            img.alt='First Athlete'; img.src=src; document.body.append(img); }
        """, source);

    [Theory]
    [InlineData("/assets/content-images/play-athlete-placeholder.webp")]
    [InlineData("/assets/content-images/play-athlete-placeholder.jpg")]
    [InlineData("/assets/content-images/headshot.webp")]
    [InlineData("/assets/content-images/headshot.jpg")]
    [InlineData("/assets/HdLogo_thumb_sm.png")]
    public async Task IntentionalArtwork_IsNotReplacedWhenItsOwnAssetFails(string source)
    {
        await using var context = await ContextAsync();
        await context.RouteAsync("**" + source, route => route.FulfillAsync(new() { Status = 404, Body = "" }));
        var page = await OpenAsync(context);
        await AddAsync(page, source);
        await page.Locator("#testPortrait").EvaluateAsync("image=>image.decode().catch(()=>{})");
        await page.EvaluateAsync("() => window.LwcPortrait.scan()");
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("src", source);
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("data-lwc-portrait"));
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("data-lwc-portrait-fallback"));
    }

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

    internal static async Task AssertApprovedArtworkAsync(ILocator image, string source)
    {
        var absoluteSource = new Uri(new Uri(image.Page.Url), source).AbsoluteUri;
        await Assertions.Expect(image).ToHaveJSPropertyAsync("src", absoluteSource);
        if (await image.GetAttributeAsync("loading") == "lazy") await image.ScrollIntoViewIfNeededAsync();
        await image.EvaluateAsync("image=>image.decode()");
        Assert.Equal(absoluteSource, await image.EvaluateAsync<string>("image=>image.currentSrc"));
        Assert.True(await image.EvaluateAsync<bool>("image=>image.complete&&image.naturalWidth>0"));
        Assert.Null(await image.GetAttributeAsync("data-lwc-portrait"));
        Assert.Null(await image.GetAttributeAsync("data-lwc-portrait-fallback"));
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
        var developing = await image.EvaluateAsync<string[]>("""
            async image => {
                // Interpolation can serialize an unchanged sRGB shadow as oklab.
                // Compare its rendered sRGB color and complete shadow geometry.
                const canvas = document.createElement('canvas');
                canvas.width = canvas.height = 1;
                const context = canvas.getContext('2d');
                const normalize = filter => filter.replace(/(?:color|oklab|oklch|rgba?|lab|lch)\([^)]*\)/g, color => {
                    context.clearRect(0, 0, 1, 1);
                    context.fillStyle = color;
                    context.fillRect(0, 0, 1, 1);
                    return `rgba(${[...context.getImageData(0, 0, 1, 1).data].join(',')})`;
                });
                const baseline = normalize(getComputedStyle(image).filter);
                // Decoding can precede landing. The later homepage ring pulse
                // must not mask the portrait's own develop frame in this check.
                image.closest('.podium-item').classList.remove('is-landed');
                image.style.animationPlayState = 'paused';
                image.dataset.lwcPortraitState = 'developing';
                await new Promise(requestAnimationFrame);
                const animation = image.getAnimations().find(a => a.animationName === 'lwc-portrait-develop');
                if (!animation) throw new Error(`Portrait develop animation did not start: ${getComputedStyle(image).animationName}`);
                animation.pause();
                const duration = Number(animation.effect.getTiming().duration);
                return [baseline, ...[0, .25, .5, .75].map(progress => {
                    animation.currentTime = duration * progress;
                    return normalize(getComputedStyle(image).filter);
                })];
            }
            """);
        Assert.Equal(5, developing.Length);
        foreach (var filter in developing.Skip(1))
        {
            Assert.StartsWith(developing[0], filter);
            Assert.Equal(halo.Split("drop-shadow(").Length, filter.Split("drop-shadow(").Length);
        }
        Assert.Equal(halo, await image.EvaluateAsync<string>("""
            image => {
                image.closest('.podium-item').classList.add('is-landed');
                return getComputedStyle(image).filter;
            }
            """));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        Assert.Equal(halo, await image.EvaluateAsync<string>("image => getComputedStyle(image).filter"));
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
                image.className='portrait';
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
    [InlineData("event-athlete-preview-portrait")]
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

    [Theory]
    [InlineData("brand")]
    [InlineData("organization")]
    [InlineData("illustration")]
    [InlineData("badge")]
    public async Task DeclaredNonPortrait_KeepsItsArtworkEvenInsideAPortraitWrapper(string role)
    {
        await using var context = await ContextAsync();
        await context.RouteAsync("**/artwork-broken.webp", route => route.FulfillAsync(new() { Status = 404, Body = "" }));
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            role => {
                const wrapper=document.createElement('span');wrapper.className='lmx-quote-portrait';
                const image=document.createElement('img');image.id='artwork';image.className='portrait';
                image.dataset.lwcImageRole=role;image.alt='First Athlete';
                image.src='/assets/HdLogo_thumb_sm.png';wrapper.append(image);document.body.append(wrapper);
                window.LwcPortrait.enhance(image);window.LwcPortrait.scan(wrapper);
            }
            """, role);
        var image = page.Locator("#artwork");
        await image.EvaluateAsync("image => image.decode()");
        await Assertions.Expect(image).ToHaveAttributeAsync("src", "/assets/HdLogo_thumb_sm.png");
        Assert.Null(await image.GetAttributeAsync("data-lwc-portrait"));
        Assert.Null(await image.GetAttributeAsync("data-lwc-portrait-state"));
        Assert.Equal("fill", await image.EvaluateAsync<string>("image=>getComputedStyle(image).objectFit"));
        await page.EvaluateAsync("""
            () => {artwork.srcset='/original.webp 500w';artwork.sizes='37px';
                const wrapper=artwork.parentElement;wrapper.remove();document.body.append(wrapper);
                window.LwcPortrait.scan(wrapper);}
            """);
        await image.EvaluateAsync("image => image.decode()");
        await Assertions.Expect(image).ToHaveAttributeAsync("srcset", "/original.webp 500w");
        await Assertions.Expect(image).ToHaveAttributeAsync("sizes", "37px");
        await page.EvaluateAsync("""
            async () => {artwork.removeAttribute('srcset');artwork.removeAttribute('sizes');
                const failed=new Promise(resolve=>artwork.addEventListener('error',resolve,{once:true}));
                artwork.src='/artwork-broken.webp';await failed;window.LwcPortrait.enhance(artwork);}
            """);
        await Assertions.Expect(image).ToHaveAttributeAsync("src", "/artwork-broken.webp");
        Assert.Null(await image.GetAttributeAsync("data-lwc-portrait-fallback"));
        Assert.Null(await image.GetAttributeAsync("data-lwc-portrait"));
        await Assertions.Expect(image).ToHaveAttributeAsync("alt", "First Athlete");
    }

    [Fact]
    public async Task ExplicitEnhance_DoesNotAdoptAnUnclassifiedLogo()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            () => {const image=document.createElement('img');image.id='logo';image.alt='Longevity World Cup';
                image.src='/assets/HdLogo_thumb_sm.png';document.body.append(image);window.LwcPortrait.enhance(image);}
            """);
        var image = page.Locator("#logo");
        await image.EvaluateAsync("image=>image.decode()");
        await Assertions.Expect(image).ToHaveAttributeAsync("src", "/assets/HdLogo_thumb_sm.png");
        Assert.Null(await image.GetAttributeAsync("data-lwc-portrait"));
    }

    [Fact]
    public async Task OnlyAthleteTextFallbacks_ReceivePortraitStyling()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        var before = await page.EvaluateAsync<string[]>("""
            () => {
                document.body.className='events-board';
                document.body.innerHTML='<style>.avatar-fallback{background:orange;color:navy;font-weight:400;box-shadow:0 0 6px orange}</style>'
                    +['brand','organization','illustration','badge','unknown','athlete'].map(role=>
                        `<div id="${role}" class="avatar-fallback" data-lwc-image-role="${role}">FA</div>`).join('');
                document.querySelector('link[href*=portrait-system]').disabled=true;
                window.fallbackStyles=()=>['brand','organization','illustration','badge','unknown'].map(id=>{
                    const s=getComputedStyle(document.getElementById(id));
                    return JSON.stringify([s.backgroundImage,s.backgroundColor,s.color,s.fontWeight,s.boxShadow,s.textShadow,s.letterSpacing]);
                });
                return fallbackStyles();
            }
            """);
        var after = await page.EvaluateAsync<string[]>("""
            async () => {document.querySelector('link[href*=portrait-system]').disabled=false;
                await new Promise(requestAnimationFrame);return fallbackStyles();}
            """);
        Assert.Equal(before, after);
        Assert.Contains("gradient", await page.Locator("#athlete").EvaluateAsync<string>("e=>getComputedStyle(e).backgroundImage"));
        await Assertions.Expect(page.Locator("#athlete")).ToHaveTextAsync("FA");
    }

    [Theory]
    [InlineData("/assets/HdLogo_thumb_sm.png-athlete.webp")]
    [InlineData("/assets/content-images/play-athlete-placeholder-custom.webp")]
    public async Task RealPortraitSharingAPlaceholderPrefix_IsNotReplaced(string source)
    {
        await using var context = await ContextAsync();
        await context.RouteAsync("**" + source, route => route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = Svg }));
        var page = await OpenAsync(context);
        await AddAsync(page, source);
        await ReadyAsync(page);
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("src", source);
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("data-lwc-portrait-fallback"));
    }

    [Fact]
    public async Task BrokenAthletePhoto_RecoversWithItsOwnDecodedIdentity()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await AddAsync(page, MissingPhoto);
        await ReadyAsync(page);
        await AssertDecodedFallbackAsync(page.Locator("#testPortrait"), "FA");
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("alt", "First Athlete");
    }

    [Fact]
    public async Task MissingProfilePhoto_AlsoKeepsTheStickyPortraitsPublicIdentity()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        var found = false;
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            foreach (var athlete in athletes.OfType<System.Text.Json.Nodes.JsonObject>())
            {
                if (!(athlete["Name"]?.GetValue<string>() ?? "").Contains("Michael Lustgarten", StringComparison.Ordinal)) continue;
                found = true;
                athlete["Name"] = "Visible Athlete";
                athlete["DisplayName"] = "Visible Athlete";
                foreach (var key in athlete.Select(p => p.Key).Where(key => key.StartsWith("ProfilePic", StringComparison.Ordinal)).ToArray())
                    athlete[key] = null;
            }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/michael-lustgarten");
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync("Visible Athlete");
        Assert.True(found);
        var artwork = await page.Locator("script[data-default-profile-pic][src*='leaderboard-page']").GetAttributeAsync("data-default-profile-pic");
        await AssertApprovedArtworkAsync(page.Locator("#modalProfilePic"), artwork!);
        await AssertApprovedArtworkAsync(page.Locator("#stickyAthletePortrait"), artwork!);
        await Assertions.Expect(page.Locator("#stickyAthletePortrait")).ToHaveAttributeAsync("data-athlete-name", "Visible Athlete");
        await Assertions.Expect(page.Locator("#stickyAthletePortrait")).ToHaveAttributeAsync("alt", "");
    }

    [Fact]
    public async Task RecycledPortrait_CanRelinquishOwnershipToArtworkAndBecomeAnAthleteAgain()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await AddAsync(page);
        await ReadyAsync(page);
        await page.EvaluateAsync("""
            () => {testPortrait.dataset.lwcImageRole='brand';testPortrait.src='/assets/HdLogo_thumb_sm.png';}
            """);
        await Assertions.Expect(page.Locator("#testPortrait")).Not.ToHaveAttributeAsync("data-lwc-portrait", "");
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("src", "/assets/HdLogo_thumb_sm.png");
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("srcset"));
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("sizes"));
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("data-lwc-portrait-state"));
        await page.Locator("#testPortrait").EvaluateAsync("image=>image.decode()");
        await page.EvaluateAsync("""
            src => {testPortrait.alt='Second Athlete';testPortrait.src=src;testPortrait.dataset.lwcImageRole='athlete';}
            """, Photo.Replace("first", "second"));
        await ReadyAsync(page);
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("alt", "Second Athlete");
        Assert.NotEmpty(await page.Locator("#testPortrait").GetAttributeAsync("srcset") ?? "");
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("data-lwc-portrait-fallback"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecycledMonogram_BecomingBrandArtworkRestoresItsOriginalSource(bool roleOnParent)
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await AddAsync(page, MissingPhoto);
        await ReadyAsync(page);
        await AssertDecodedFallbackAsync(page.Locator("#testPortrait"), "FA");
        await context.RouteAsync("**/missing-portrait.webp", route => route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = Svg }));
        await page.EvaluateAsync("parent => (parent?testPortrait.parentElement:testPortrait).dataset.lwcImageRole='brand'", roleOnParent);
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("src", MissingPhoto);
        await page.Locator("#testPortrait").EvaluateAsync("image=>image.decode()");
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("data-lwc-portrait"));
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("data-lwc-portrait-fallback"));
    }

    [Fact]
    public async Task NewDataPhoto_ReplacesAnOwnedMonogramWithoutRegeneratingOldInitials()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await AddAsync(page, MissingPhoto);
        await ReadyAsync(page);
        await AssertDecodedFallbackAsync(page.Locator("#testPortrait"), "FA");
        var photo = "data:image/svg+xml," + Uri.EscapeDataString(Svg.Replace("teal", "red"));
        await page.EvaluateAsync("source=>{testPortrait.alt='Second Athlete';testPortrait.src=source}", photo);
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("src", photo);
        await ReadyAsync(page);
        Assert.Null(await page.Locator("#testPortrait").GetAttributeAsync("data-lwc-portrait-fallback"));
        await Assertions.Expect(page.Locator("#testPortrait")).ToHaveAttributeAsync("alt", "Second Athlete");
        using var frame = Image.Load<Rgba32>(await page.Locator("#testPortrait").ScreenshotAsync());
        var center = frame[frame.Width / 2, frame.Height / 2];
        Assert.True(center.R > 200 && center.G < 80, "The current data photo was replaced by an old initials fallback.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DetachedPortrait_RecycledAsUnclassifiedArtworkRelinquishesOwnedState(bool reinsertWrapper)
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await AddAsync(page, MissingPhoto);
        await ReadyAsync(page);
        await AssertDecodedFallbackAsync(page.Locator("#testPortrait"), "FA");
        await context.RouteAsync("**/missing-portrait.webp", route => route.FulfillAsync(new() { ContentType = "image/svg+xml", Body = Svg }));
        await page.EvaluateAsync("() => {window.recycledPortrait=testPortrait;testPortrait.remove()}");
        await page.EvaluateAsync("""
            wrapper => {
                const image=window.recycledPortrait;
                image.className='custom-event-avatar';
                image.dataset.lwcImageRole='brand';
                image.alt='Longevity World Cup';
                const target=wrapper?document.body.appendChild(document.createElement('div')):document.body;
                target.append(image);
            }
            """, reinsertWrapper);
        var logo = page.Locator("#testPortrait");
        await Assertions.Expect(logo).ToHaveAttributeAsync("src", MissingPhoto);
        await logo.EvaluateAsync("image=>image.decode()");
        foreach (var attribute in new[] { "data-lwc-portrait", "data-lwc-portrait-fallback", "data-lwc-portrait-state", "data-lwc-portrait-tier", "srcset", "sizes" })
            Assert.Null(await logo.GetAttributeAsync(attribute));
    }
}
