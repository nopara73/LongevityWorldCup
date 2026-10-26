using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class AthletePortraitMotionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string ApprovedPhoto = "6683dd38d8317bb5a68b3053ca8dae7c53aad86cd1c6df1c1662e16fcbe83345";
    private const string ValeriePhoto = "e280d5d1d7278450a5d37ba7e16eee54719d0acb125f54c18b0b9a9a43c41976";
    private const string Clip = "**/assets/athlete-motion/*";

    private static string Photo(string slug) => slug switch
    {
        "valerie-orsoni" => ValeriePhoto,
        "anna-buky" => "f36cf66df3a3f67ef88c000c32c0ae4093a7c174aaae81bbb2c0691c178fc1ec",
        "martin-helstab" => "03fd37f6337cd4f2d8ecdfa1b824bb970ed8c83be1eeaf29db078d25e02a7966",
        "wen-z" => "9d0f884164da2166825661b263042d63bbebda217511f97a72eec8887853590d",
        "tibor-konkoly" => "d563d8ade182b2286dbbeb6519e54f88e48ad04c40a3111649c4b598b605e501",
        "charlotte-wong" => "708c527def914308e9da42eaac2cde572770f4fe5c06fbb162b1c250c34b3391",
        "slamovits-tibor" => "f1ed3c0f28481d62d0df3f463a5077dacad31c9927894f9fe6ef8dbc87d85b78",
        "qolars" => "da84f1750d8a7b55779f34832c9990de28d12638ad0a2c6fa6102b33061e87f7",
        "markus-mattiasson" => "405a0d4169a2aab78e2a7a0fd4569cc92633f0b202a0f37a7abb2ef431662593",
        "inka-land" => "89251966a304b5b721333bd3a3c02d456f76a230e6549bdbb34c210a60814cca",
        "tamas-toth" => "613ab3be683feb657f8b3e3a6db17ecac97211f90f3d18b16f91bf50735c8781",
        _ => ApprovedPhoto
    };

    [Theory]
    [InlineData(1440, "benjamin-garden")]
    [InlineData(390, "benjamin-garden")]
    [InlineData(1440, "valerie-orsoni")]
    [InlineData(390, "valerie-orsoni")]
    public async Task ApprovedPortrait_PlaysOnceThenReturnsToItsOriginalImage(int width, string slug)
    {
        await using var context = await NewContextAsync(width);
        var page = await LoadAsync(context);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await InsertPortraitAsync(page, slug: slug);
        await page.WaitForFunctionAsync("() => window.motionStarts === 1");
        Assert.Equal("none", await page.Locator("video.athlete-portrait-motion")
            .EvaluateAsync<string>("video => getComputedStyle(video).pointerEvents"));
        await Assertions.Expect(page.Locator("video.athlete-portrait-motion")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#motionFixture")).ToBeVisibleAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("window.motionEnds"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));

        // Filters and hydration may replace a portrait. That must not replay its gesture.
        await InsertPortraitAsync(page, slug: slug);
        await Assertions.Expect(page.Locator("video.athlete-portrait-motion")).ToHaveCountAsync(0);
        Assert.Equal(1, await page.EvaluateAsync<int>("window.motionStarts"));
        Assert.EndsWith($"/athlete/{slug}", await page.Locator("#motionFixture").EvaluateAsync<string>("img => img.closest('a').href"));
    }

    [Theory]
    [InlineData("reduced-motion", "benjamin-garden")]
    [InlineData("different-photo", "benjamin-garden")]
    [InlineData("outside-podium", "benjamin-garden")]
    [InlineData("reduced-motion", "valerie-orsoni")]
    [InlineData("different-photo", "valerie-orsoni")]
    [InlineData("outside-podium", "valerie-orsoni")]
    public async Task OtherContexts_KeepTheirStaticPortraitAndDoNotDownloadTheClip(string scenario, string slug)
    {
        await using var context = await NewContextAsync();
        var requests = 0;
        context.Request += (_, request) =>
        {
            if (request.Url.Contains("/assets/athlete-motion/", StringComparison.Ordinal))
                Interlocked.Increment(ref requests);
        };
        var page = await LoadAsync(context);
        if (scenario != "reduced-motion")
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await InsertPortraitAsync(page,
            scenario == "different-photo" ? new string('0', 64) : Photo(slug),
            scenario == "outside-podium", slug);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await Assertions.Expect(page.Locator("video.athlete-portrait-motion")).ToHaveCountAsync(0);
        Assert.Equal(0, Volatile.Read(ref requests));
        await Assertions.Expect(page.Locator("#motionFixture")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("benjamin-garden")]
    [InlineData("valerie-orsoni")]
    public async Task FailedPlayback_LeavesTheOriginalPortraitVisible(string slug)
    {
        await using var context = await NewContextAsync();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync(Clip, async route =>
        {
            requested.TrySetResult();
            await route.AbortAsync();
        });
        var page = await LoadAsync(context);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await InsertPortraitAsync(page, slug: slug);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Assertions.Expect(page.Locator("video.athlete-portrait-motion")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#motionFixture")).ToBeVisibleAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.motionStarts"));
    }

    [Theory]
    [InlineData("reduced-motion", "benjamin-garden")]
    [InlineData("different-photo", "benjamin-garden")]
    [InlineData("reduced-motion", "valerie-orsoni")]
    [InlineData("different-photo", "valerie-orsoni")]
    public async Task ChangingMotionPreferenceOrPhoto_StopsTheActiveMotion(string scenario, string slug)
    {
        await using var context = await NewContextAsync();
        var page = await LoadAsync(context);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await InsertPortraitAsync(page, slug: slug);
        await page.WaitForFunctionAsync("() => window.motionStarts === 1");
        if (scenario == "reduced-motion")
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        else
            await page.Locator("#motionFixture").EvaluateAsync("img => img.src = img.src.replace(PHOTO, '0'.repeat(64))"
                .Replace("PHOTO", System.Text.Json.JsonSerializer.Serialize(Photo(slug))));
        await Assertions.Expect(page.Locator("video.athlete-portrait-motion")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#motionFixture")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("benjamin-garden")]
    [InlineData("valerie-orsoni")]
    public async Task Hover_ReplaysOncePerPointerEntry_AfterTheOpeningMotion(string slug)
    {
        await using var context = await NewContextAsync();
        var page = await LoadAsync(context);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await InsertPortraitAsync(page, slug: slug);
        await page.WaitForFunctionAsync("() => window.motionEnds === 1");

        await page.Locator("#motionFixture").HoverAsync();
        await page.WaitForFunctionAsync("() => window.motionStarts === 2");
        await page.WaitForFunctionAsync("() => window.motionEnds === 2");
        await page.Locator("#motionFixture").HoverAsync(new() { Position = new() { X = 30, Y = 30 } });
        Assert.Equal(2, await page.EvaluateAsync<int>("window.motionStarts"));

        await page.Mouse.MoveAsync(0, 0);
        await page.Locator("#motionFixture").HoverAsync();
        await page.WaitForFunctionAsync("() => window.motionStarts === 3");
    }

    [Theory]
    [InlineData(true, 1440, "benjamin-garden")]
    [InlineData(true, 390, "benjamin-garden")]
    [InlineData(false, 1440, "benjamin-garden")]
    [InlineData(false, 390, "benjamin-garden")]
    [InlineData(true, 1440, "valerie-orsoni")]
    [InlineData(true, 390, "valerie-orsoni")]
    [InlineData(false, 1440, "valerie-orsoni")]
    [InlineData(false, 390, "valerie-orsoni")]
    [InlineData(true, 1440, "tibor-konkoly")]
    public async Task ProfileOpeningAndHover_PlayTheMotion(bool direct, int width, string slug)
    {
        await using var context = await NewContextAsync(width);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true')");
        // Hold the short first playback while inspecting it; a busy machine must
        // not turn an already completed gesture into a missing-element failure.
        await context.AddInitScriptAsync("window.holdProfileMotion = true");
        IPage page;
        if (direct)
        {
            page = await context.NewPageAsync();
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            await page.GotoAsync($"/athlete/{slug}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        }
        else
        {
            page = await LoadAsync(context, "/leaderboard");
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            var opener = page.Locator($"a.athlete-name[href='/athlete/{slug}']").First;
            await opener.EvaluateAsync("e => e.scrollIntoView({block:'center', behavior:'instant'})");
            await opener.ClickAsync();
        }

        await page.WaitForFunctionAsync("() => window.profileMotionStarts === 1");
        Assert.Contains($"/assets/athlete-motion/{slug}-", await page.Locator("#athlete-profile video.athlete-portrait-motion").EvaluateAsync<string>("video => video.currentSrc"));
        if (slug == "tibor-konkoly")
            Assert.InRange(await page.Locator("#athlete-profile video.athlete-portrait-motion").EvaluateAsync<double>("video => video.duration"), 11.95, 12.05);
        Assert.True(await page.Locator("#athlete-profile video.athlete-portrait-motion").EvaluateAsync<bool>("video => { const image = video.previousElementSibling.getBoundingClientRect(); const clip = video.getBoundingClientRect(); return Math.abs(clip.x - image.x - 3) < 1 && Math.abs(clip.y - image.y - 3) < 1 && Math.abs(clip.width - image.width + 6) < 1 && Math.abs(clip.height - image.height + 6) < 1; }"));
        await page.Locator("#athlete-profile video.athlete-portrait-motion").EvaluateAsync("video => { window.holdProfileMotion = false; return video.play(); }");
        await page.WaitForFunctionAsync("() => window.profileMotionEnds === 1");
        await Assertions.Expect(page.Locator("#modalProfilePic")).ToBeVisibleAsync();
        await page.Locator("#modalProfilePic").HoverAsync();
        await page.WaitForFunctionAsync("() => window.profileMotionStarts === 2");
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#athlete-profile video.athlete-portrait-motion")).ToHaveCountAsync(0);

        if (!direct)
        {
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
            var opener = page.Locator($"a.athlete-name[href='/athlete/{slug}']").First;
            await opener.EvaluateAsync("e => e.scrollIntoView({block:'center', behavior:'instant'})");
            await opener.ClickAsync();
            await page.WaitForFunctionAsync("() => window.profileMotionStarts === 3");
        }
    }

    [Theory]
    [InlineData("benjamin-garden")]
    [InlineData("valerie-orsoni")]
    public async Task ReducedMotion_ProfileOpeningAndHover_KeepTheStillPhoto(string slug)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/athlete/{slug}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.querySelector('#modalProfilePic').hasAttribute('data-full-src') && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.Locator("#modalProfilePic").HoverAsync();
        await Assertions.Expect(page.Locator("video.athlete-portrait-motion")).ToHaveCountAsync(0);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.profileMotionStarts"));
    }

    [Fact]
    public async Task TwoApprovedPodiumPortraits_EachPlayTheirOwnClip()
    {
        await using var context = await NewContextAsync();
        var page = await LoadAsync(context);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.EvaluateAsync("""
            ({benjamin, valerie}) => {
                const podium = document.querySelector('.podium');
                podium.style.display = 'flex';
                podium.innerHTML = `<div class="podium-item first"><a><img class="podium-portrait" src="/generated/thumbs/athletes/benjamin_garden_thumb_md_${benjamin}.webp"></a></div><div class="podium-item second"><a><img class="podium-portrait" src="/generated/thumbs/athletes/valerie_orsoni_thumb_md_${valerie}.webp"></a></div>`;
            }
            """, new { benjamin = ApprovedPhoto, valerie = ValeriePhoto });
        await page.WaitForFunctionAsync("() => window.motionStarts === 2");
        var sources = await page.Locator("video.athlete-portrait-motion").EvaluateAllAsync<string[]>("videos => videos.map(v => v.currentSrc)");
        Assert.Contains(sources, src => src.Contains("benjamin-garden-wink-", StringComparison.Ordinal));
        Assert.Contains(sources, src => src.Contains("valerie-orsoni-double-blink-", StringComparison.Ordinal));
        await page.WaitForFunctionAsync("() => window.motionEnds === 2");
    }

    [Fact]
    public async Task ChangingBetweenApprovedProfilePhotos_ReplacesTheActiveClip()
    {
        await using var context = await NewContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true')");
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.profileMotionStarts === 1");
        await page.Locator("#modalProfilePic").EvaluateAsync("(img, src) => img.src = src",
            $"/generated/profiles/athletes/valerie_orsoni_{ValeriePhoto}.webp");
        await page.WaitForFunctionAsync("() => window.profileMotionStarts === 2");
        var overlay = page.Locator("#athlete-profile video.athlete-portrait-motion");
        await Assertions.Expect(overlay).ToHaveCountAsync(1);
        Assert.Contains("valerie-orsoni-double-blink-", await overlay.EvaluateAsync<string>("video => video.currentSrc"));
    }

    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, false)]
    [InlineData(1440, true)]
    [InlineData(390, true)]
    public async Task ServerRenderedTablePortraits_PlayBeforeHydrationAndAfterDataFailure(int width, bool failRequest)
    {
        await using var context = await NewContextAsync(width);
        await context.AddInitScriptAsync("window.holdTableMotion = true");
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            requested.TrySetResult();
            if (failRequest) await route.AbortAsync();
            else
            {
                await release.Task;
                await route.ContinueAsync();
            }
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (failRequest)
                await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard could not load.");
            var body = page.Locator(".leaderboard table tbody");
            await Assertions.Expect(body).ToHaveAttributeAsync("data-server-rendered", "true");
            var link = body.Locator("a[href='/athlete/anna-buky']").Filter(new() { Has = page.Locator("img.portrait") }).First;
            var image = link.Locator("img.portrait");
            await image.ScrollIntoViewIfNeededAsync();
            await page.WaitForFunctionAsync("() => document.querySelector(\".leaderboard a[href='/athlete/anna-buky'] img.portrait\")?.naturalWidth > 0");

            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            await image.HoverAsync();
            var overlay = link.Locator("video.athlete-portrait-motion");
            await Assertions.Expect(overlay).ToHaveClassAsync("athlete-portrait-motion is-playing");
            Assert.Contains("/assets/athlete-motion/anna-buky-", await overlay.EvaluateAsync<string>("video => video.currentSrc"));
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
            await Assertions.Expect(overlay).ToHaveCountAsync(0);
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            await link.FocusAsync();
            await Assertions.Expect(overlay).ToHaveClassAsync("athlete-portrait-motion is-playing");
            await Assertions.Expect(link).ToBeFocusedAsync();
            await Assertions.Expect(link).ToHaveAttributeAsync("href", "/athlete/anna-buky");
            await Assertions.Expect(body).ToHaveAttributeAsync("data-server-rendered", "true");
            Assert.Equal(2, await page.EvaluateAsync<int>("window.motionStarts"));
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task ApprovedTablePortraits_ReplayOnHover_AndKeepTheirLinksAndAlignment(int width)
    {
        await using var context = await NewContextAsync(width);
        await context.AddInitScriptAsync("window.holdTableMotion = true");
        var page = await LoadAsync(context, "/leaderboard");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        foreach (var slug in new[] { "anna-buky", "martin-helstab", "wen-z", "tibor-konkoly", "charlotte-wong", "slamovits-tibor", "qolars", "markus-mattiasson", "inka-land", "tamas-toth" })
        {
            var image = page.Locator($".leaderboard a[href='/athlete/{slug}'] img.portrait").First;
            await image.HoverAsync();
            var overlay = image.Locator("..").Locator("video.athlete-portrait-motion");
            await Assertions.Expect(overlay).ToHaveClassAsync("athlete-portrait-motion is-playing");
            Assert.Contains($"/assets/athlete-motion/{slug}-", await overlay.EvaluateAsync<string>("video => video.currentSrc"));
            // Loading a cached clip can beat the portrait's hover transition.
            // Measure both layers at their final scale, retaining one-pixel alignment.
            var alignment = await overlay.EvaluateAsync<double[]>("""
                async video => {
                    const photo = video.previousElementSibling;
                    await Promise.all([photo, video].flatMap(element => element.getAnimations()).map(animation => animation.finished.catch(() => {})));
                    const image = photo.getBoundingClientRect(), clip = video.getBoundingClientRect();
                    return [Math.abs((clip.x + clip.width / 2) - (image.x + image.width / 2)),
                        Math.abs((clip.y + clip.height / 2) - (image.y + image.height / 2)),
                        Math.abs(clip.width - (image.width - 6 * 1.1))];
                }
                """);
            Assert.True(alignment.All(delta => delta < 1), $"{slug} at {width}px: alignment deltas {string.Join(", ", alignment)}");
            Assert.EndsWith($"/athlete/{slug}", await image.EvaluateAsync<string>("img => img.closest('a').href"));
            // A profile update invalidates the active clip immediately, even in a row.
            await image.EvaluateAsync("img => img.src = img.src.replace(/[a-f0-9]{64}/g, '0'.repeat(64))");
            await Assertions.Expect(overlay).ToHaveCountAsync(0);
        }
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HoverBeforePortraitLoads_PlaysOnlyIfThePointerRemains(bool remainHovered)
    {
        await using var context = await NewContextAsync();
        await context.AddInitScriptAsync("window.holdTableMotion = true");
        var pendingPhoto = new TaskCompletionSource<IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/generated/thumbs/athletes/anna_buky_*.webp*", route =>
        {
            pendingPhoto.TrySetResult(route);
            return Task.CompletedTask;
        });
        var page = await LoadAsync(context, "/leaderboard");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        var image = page.Locator(".leaderboard a[href='/athlete/anna-buky'] img.portrait").First;
        await image.HoverAsync();
        Assert.Equal(0, await image.EvaluateAsync<int>("img => img.naturalWidth"));
        if (!remainHovered) await page.Mouse.MoveAsync(0, 0);
        var root = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        var photo = await File.ReadAllBytesAsync(Path.Combine(root, "athletes", "anna_buky", "anna_buky.webp"));
        await (await pendingPhoto.Task.WaitAsync(TimeSpan.FromSeconds(15))).FulfillAsync(new() { ContentType = "image/webp", BodyBytes = photo });
        await page.WaitForFunctionAsync("() => document.querySelector(\".leaderboard a[href='/athlete/anna-buky'] img.portrait\").naturalWidth > 0");
        var overlay = image.Locator("..").Locator("video.athlete-portrait-motion");
        if (remainHovered)
            await Assertions.Expect(overlay).ToHaveClassAsync("athlete-portrait-motion is-playing");
        else
            await Assertions.Expect(overlay).ToHaveCountAsync(0);
    }

    private async Task<IBrowserContext> NewContextAsync(int width = 1440)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var root = App.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath;
        foreach (var fileSlug in new[] { "benjamin_garden", "valerie_orsoni", "anna_buky", "martin_helstab", "wen_z", "tibor_konkoly", "charlotte_wong", "slamovits_tibor", "qolars", "markus_mattiasson", "inka_land", "tamas_toth" })
        {
            var path = Directory.EnumerateFiles(Path.Combine(root, "athletes", fileSlug), $"{fileSlug}.*")
                .Single(path => Path.GetExtension(path).ToLowerInvariant() is ".webp" or ".jpg" or ".jpeg" or ".png");
            var photo = await File.ReadAllBytesAsync(path);
            var type = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "image/webp"
            };
            await context.RouteAsync($"**/generated/thumbs/athletes/{fileSlug}_thumb_md_*.webp*", route =>
                route.FulfillAsync(new() { ContentType = type, BodyBytes = photo }));
        }
        await context.AddInitScriptAsync("""
            window.motionStarts = 0;
            window.motionEnds = 0;
            window.profileMotionStarts = 0;
            window.profileMotionEnds = 0;
            const started = new WeakSet();
            for (const [event, key] of [['playing', 'motionStarts'], ['ended', 'motionEnds']]) {
                document.addEventListener(event, e => {
                    if (!e.target.matches?.('video.athlete-portrait-motion')) return;
                    if (event === 'playing') {
                        if (started.has(e.target)) return;
                        started.add(e.target);
                    }
                    window[key]++;
                    if (e.target.previousElementSibling?.id === 'modalProfilePic')
                        window[event === 'playing' ? 'profileMotionStarts' : 'profileMotionEnds']++;
                    if (event === 'playing' && ((window.holdProfileMotion && e.target.previousElementSibling?.id === 'modalProfilePic')
                        || (window.holdTableMotion && e.target.closest('.leaderboard')))) queueMicrotask(() => e.target.pause());
                }, true);
            }
            """);
        return context;
    }

    private static async Task<IPage> LoadAsync(IBrowserContext context, string path = "/")
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        // Homepage cases supply their own portraits. Clear real athletes while
        // reduced motion is still on, before a test enables playback.
        if (path == "/")
            await page.Locator(".podium").EvaluateAsync("podium => podium.replaceChildren()");
        return page;
    }

    private static async Task InsertPortraitAsync(IPage page, string? photo = null, bool outsidePodium = false, string slug = "benjamin-garden")
    {
        await page.EvaluateAsync("""
            ({photo, outsidePodium, slug, fileSlug}) => {
                const podium = document.querySelector('.podium');
                podium.replaceChildren();
                podium.style.display = 'flex';
                const card = document.createElement('div');
                card.className = 'podium-item first';
                card.innerHTML = `<a class="athlete-profile-link" href="/athlete/${slug}"><img id="motionFixture" class="podium-portrait" src="/generated/thumbs/athletes/${fileSlug}_thumb_md_${photo}.webp" alt="Athlete portrait"></a>`;
                (outsidePodium ? document.body : podium).appendChild(card);
            }
            """, new { photo = photo ?? Photo(slug), outsidePodium, slug, fileSlug = slug.Replace('-', '_') });
        await page.WaitForFunctionAsync("() => document.querySelector('#motionFixture')?.naturalWidth > 0");
        await page.Locator("#motionFixture").ScrollIntoViewIfNeededAsync();
    }
}
