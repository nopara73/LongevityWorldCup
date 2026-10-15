using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class AthletePortraitWinkBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string ApprovedPhoto = "6683dd38d8317bb5a68b3053ca8dae7c53aad86cd1c6df1c1662e16fcbe83345";
    private const string Clip = "**/assets/athlete-motion/benjamin-garden-wink-*";

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task ApprovedPortrait_WinksOnceThenReturnsToItsOriginalImage(int width)
    {
        await using var context = await NewContextAsync(width);
        var page = await LoadAsync(context);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await InsertPortraitAsync(page);
        await page.WaitForFunctionAsync("() => window.winkStarts === 1");
        Assert.Equal("none", await page.Locator("video.athlete-portrait-wink")
            .EvaluateAsync<string>("video => getComputedStyle(video).pointerEvents"));
        await Assertions.Expect(page.Locator("video.athlete-portrait-wink")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#winkFixture")).ToBeVisibleAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("window.winkEnds"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));

        // Filters and hydration may replace a portrait. That must not replay the wink.
        await InsertPortraitAsync(page);
        await Assertions.Expect(page.Locator("video.athlete-portrait-wink")).ToHaveCountAsync(0);
        Assert.Equal(1, await page.EvaluateAsync<int>("window.winkStarts"));
        Assert.EndsWith("/athlete/benjamin-garden", await page.Locator("#winkFixture").EvaluateAsync<string>("img => img.closest('a').href"));
    }

    [Theory]
    [InlineData("reduced-motion")]
    [InlineData("different-photo")]
    [InlineData("outside-podium")]
    public async Task OtherContexts_KeepTheirStaticPortraitAndDoNotDownloadTheClip(string scenario)
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
            scenario == "different-photo" ? new string('0', 64) : ApprovedPhoto,
            scenario == "outside-podium");
        await Assertions.Expect(page.Locator("video.athlete-portrait-wink")).ToHaveCountAsync(0);
        Assert.Equal(0, Volatile.Read(ref requests));
        await Assertions.Expect(page.Locator("#winkFixture")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FailedPlayback_LeavesTheOriginalPortraitVisible()
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
        await InsertPortraitAsync(page);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await Assertions.Expect(page.Locator("video.athlete-portrait-wink")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#winkFixture")).ToBeVisibleAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.winkStarts"));
    }

    [Theory]
    [InlineData("reduced-motion")]
    [InlineData("different-photo")]
    public async Task ChangingMotionPreferenceOrPhoto_StopsTheActiveWink(string scenario)
    {
        await using var context = await NewContextAsync();
        var page = await LoadAsync(context);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await InsertPortraitAsync(page);
        await page.WaitForFunctionAsync("() => window.winkStarts === 1");
        if (scenario == "reduced-motion")
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        else
            await page.Locator("#winkFixture").EvaluateAsync("img => img.src = img.src.replace(PHOTO, '0'.repeat(64))"
                .Replace("PHOTO", System.Text.Json.JsonSerializer.Serialize(ApprovedPhoto)));
        await Assertions.Expect(page.Locator("video.athlete-portrait-wink")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#winkFixture")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Hover_ReplaysOncePerPointerEntry_AfterTheOpeningWink()
    {
        await using var context = await NewContextAsync();
        var page = await LoadAsync(context);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await InsertPortraitAsync(page);
        await page.WaitForFunctionAsync("() => window.winkEnds === 1");

        await page.Locator("#winkFixture").HoverAsync();
        await page.WaitForFunctionAsync("() => window.winkStarts === 2");
        await page.WaitForFunctionAsync("() => window.winkEnds === 2");
        await page.Locator("#winkFixture").HoverAsync(new() { Position = new() { X = 30, Y = 30 } });
        Assert.Equal(2, await page.EvaluateAsync<int>("window.winkStarts"));

        await page.Mouse.MoveAsync(0, 0);
        await page.Locator("#winkFixture").HoverAsync();
        await page.WaitForFunctionAsync("() => window.winkStarts === 3");
    }

    [Theory]
    [InlineData(true, 1440)]
    [InlineData(true, 390)]
    [InlineData(false, 1440)]
    [InlineData(false, 390)]
    public async Task ProfileOpeningAndHover_PlayTheWink(bool direct, int width)
    {
        await using var context = await NewContextAsync(width);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true')");
        IPage page;
        if (direct)
        {
            page = await context.NewPageAsync();
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        }
        else
        {
            page = await LoadAsync(context);
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            await page.Locator("a.athlete-name[href='/athlete/benjamin-garden']").First.ClickAsync();
        }

        await page.WaitForFunctionAsync("() => window.profileWinkStarts === 1");
        Assert.True(await page.Locator("#athlete-profile video.athlete-portrait-wink").EvaluateAsync<bool>("video => { const image = video.previousElementSibling.getBoundingClientRect(); const clip = video.getBoundingClientRect(); return Math.abs(clip.x - image.x - 3) < 1 && Math.abs(clip.y - image.y - 3) < 1 && Math.abs(clip.width - image.width + 6) < 1 && Math.abs(clip.height - image.height + 6) < 1; }"));
        await page.WaitForFunctionAsync("() => window.profileWinkEnds === 1");
        await Assertions.Expect(page.Locator("#modalProfilePic")).ToBeVisibleAsync();
        await page.Locator("#modalProfilePic").HoverAsync();
        await page.WaitForFunctionAsync("() => window.profileWinkStarts === 2");
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#athlete-profile video.athlete-portrait-wink")).ToHaveCountAsync(0);

        if (!direct)
        {
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
            await page.Locator("a.athlete-name[href='/athlete/benjamin-garden']").First.ClickAsync();
            await page.WaitForFunctionAsync("() => window.profileWinkStarts === 3");
        }
    }

    [Fact]
    public async Task ReducedMotion_ProfileOpeningAndHover_KeepTheStillPhoto()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.querySelector('#modalProfilePic').hasAttribute('data-full-src') && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.Locator("#modalProfilePic").HoverAsync();
        await Assertions.Expect(page.Locator("video.athlete-portrait-wink")).ToHaveCountAsync(0);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.profileWinkStarts"));
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
        var photo = await File.ReadAllBytesAsync(Path.Combine(root, "athletes", "benjamin_garden", "benjamin_garden.webp"));
        await context.RouteAsync("**/generated/thumbs/athletes/benjamin_garden_thumb_md_*.webp*", route =>
            route.FulfillAsync(new() { ContentType = "image/webp", BodyBytes = photo }));
        await context.AddInitScriptAsync("""
            window.winkStarts = 0;
            window.winkEnds = 0;
            window.profileWinkStarts = 0;
            window.profileWinkEnds = 0;
            const started = new WeakSet();
            for (const [event, key] of [['playing', 'winkStarts'], ['ended', 'winkEnds']]) {
                document.addEventListener(event, e => {
                    if (!e.target.matches?.('video.athlete-portrait-wink')) return;
                    if (event === 'playing') {
                        if (started.has(e.target)) return;
                        started.add(e.target);
                    }
                    window[key]++;
                    if (e.target.previousElementSibling?.id === 'modalProfilePic')
                        window[event === 'playing' ? 'profileWinkStarts' : 'profileWinkEnds']++;
                }, true);
            }
            """);
        return context;
    }

    private static async Task<IPage> LoadAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        return page;
    }

    private static async Task InsertPortraitAsync(IPage page, string photo = ApprovedPhoto, bool outsidePodium = false)
    {
        await page.EvaluateAsync("""
            ({photo, outsidePodium}) => {
                const podium = document.querySelector('.podium');
                podium.replaceChildren();
                podium.style.display = 'flex';
                const card = document.createElement('div');
                card.className = 'podium-item first';
                card.innerHTML = `<a class="athlete-profile-link" href="/athlete/benjamin-garden" aria-label="View stats of Benjamin Garden"><img id="winkFixture" class="podium-portrait" src="/generated/thumbs/athletes/benjamin_garden_thumb_md_${photo}.webp" alt="Benjamin Garden portrait"></a>`;
                (outsidePodium ? document.body : podium).appendChild(card);
            }
            """, new { photo, outsidePodium });
        await page.WaitForFunctionAsync("() => document.querySelector('#winkFixture')?.naturalWidth > 0");
        await page.Locator("#winkFixture").ScrollIntoViewIfNeededAsync();
    }
}
