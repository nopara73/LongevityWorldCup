using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class HomeScoreboardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    // Isolate the production IIFE with controllable frames/intersections. These
    // are synthetic lifecycle fixtures, separate from the real-page checks.
    private async Task<IPage> ScoreboardAsync(IBrowserContext context, string mode = "", bool realTimers = false)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(App.BaseAddress + "health");
        await page.SetContentAsync("""
            <style>tr{height:50px}.age-reduction{color:rgb(25,120,130)}</style>
            <main class="homepage-main"><div class="podium">
            <div class="podium-item first"><a class="athlete-name" href="/athlete/one">One</a><div class="podium-score"><span class="age-reduction">12.34 years</span></div></div>
            </div><table id="athleteLeaderboard"><tbody>
            <tr id="rank-4"><td><a href="#one">One</a></td><td class="age-reduction-td"><span class="age-reduction">−8.2</span></td></tr>
            <tr id="rank-5"><td><a href="#two">Two</a></td><td class="age-reduction-td"><span class="age-reduction">−4.12</span></td></tr>
            <tr id="rank-6"><td><a href="#three">Three</a></td><td class="age-reduction-td"><span class="age-reduction">+1.0</span></td></tr>
            </tbody></table><div id="leaderboardStatus">Loading</div></main>
            """);
        if (!realTimers)
        {
            await page.Clock.InstallAsync();
            await page.Clock.PauseAtAsync(DateTime.UtcNow.AddMinutes(1));
        }
        await page.EvaluateAsync("""
            mode => {
                history.replaceState(null, '', '/' + (mode === 'hash' ? '#faq' : mode === 'shared' ? '?search=one' : ''));
                window.frames = new Map(); let next = 0;
                window.requestAnimationFrame = fn => { frames.set(++next, fn); return next; };
                window.cancelAnimationFrame = id => frames.delete(id);
                window.paint = now => { const batch=[...frames.values()]; frames.clear(); batch.forEach(fn=>fn(now)); };
                window.intersections=[];
                window.IntersectionObserver = class {
                    constructor(fn){this.fn=fn; intersections.push(this);}
                    observe(){} unobserve(){} disconnect(){this.disconnected=true;}
                };
                window.enter = () => intersections.forEach(o=>o.fn([{isIntersecting:true}]));
                window.started=[];
                const animate=Element.prototype.animate;
                if (mode === 'unsupported') Element.prototype.animate=undefined;
                else Element.prototype.animate=function(...args){ const a=animate.apply(this,args); started.push(a); return a; };
                if (mode === 'hidden') Object.defineProperty(document,'visibilityState',{configurable:true,value:'hidden'});
            }
            """, mode);
        using var client = App.CreateClient();
        var source = await client.GetStringAsync("/js/homepage.js");
        await page.AddScriptTagAsync(new() { Content = source[source.IndexOf("// ===== Living scoreboard", StringComparison.Ordinal)..] });
        return page;
    }

    [Theory]
    [InlineData("pointerdown")]
    [InlineData("keydown")]
    [InlineData("focusin")]
    [InlineData("hidden")]
    [InlineData("reduced")]
    [InlineData("hashchange")]
    [InlineData("pagehide")]
    public async Task Cancellation_PersistsAcrossLateHydrationAndIntersection(string trigger)
    {
        await using var context = await Browser.NewContextAsync();
        var page = await ScoreboardAsync(context);
        Assert.Equal(1, await page.Locator(".is-counting").CountAsync());
        if (trigger == "reduced") await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        else await page.EvaluateAsync("""
            trigger => {
                if(trigger==='hidden') {
                    Object.defineProperty(document,'visibilityState',{configurable:true,value:'hidden'});
                    document.dispatchEvent(new Event('visibilitychange'));
                } else window.dispatchEvent(new Event(trigger));
            }
            """, trigger);
        await Assertions.Expect(page.Locator("[data-home-count], .is-counting, .is-landed, .home-board-settling")).ToHaveCountAsync(0);
        await page.EvaluateAsync("""
            () => {
                const podium=document.querySelector('.podium'); podium.innerHTML=podium.innerHTML;
                document.querySelector('#leaderboardStatus').textContent='Leaderboard loaded.';
                document.querySelector('tbody').append(document.querySelector('tr').cloneNode(true));
            }
            """);
        await page.EvaluateAsync("() => { paint(0); enter(); paint(5000); }");
        Assert.Equal(0, await page.Locator("[data-home-count], .is-counting, .is-landed, .home-board-settling").CountAsync());
        Assert.Equal(0, await page.EvaluateAsync<int>("started.length"));
        Assert.Equal("12.34 years", await page.Locator(".podium .age-reduction").TextContentAsync());
        Assert.NotEqual("none", await page.Locator("tbody").EvaluateAsync<string>("el=>getComputedStyle(el).pointerEvents"));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("shared")]
    [InlineData("hidden")]
    [InlineData("unsupported")]
    public async Task IneligibleVisits_NeverStartDeferredMotion(string mode)
    {
        await using var context = await Browser.NewContextAsync();
        var page = await ScoreboardAsync(context, mode);
        await page.EvaluateAsync("() => { document.querySelector('#leaderboardStatus').textContent='Leaderboard loaded.'; enter(); paint(5000); }");
        Assert.Equal(0, await page.Locator("[data-home-count], .home-scoreboard-live").CountAsync());
        Assert.Equal(0, await page.EvaluateAsync<int>("started.length"));
    }

    [Fact]
    public async Task Counts_KeepCanonicalTextThroughReplacementRefreshAndFrameStalls()
    {
        await using var context = await Browser.NewContextAsync();
        var page = await ScoreboardAsync(context);
        await page.EvaluateAsync("() => { paint(0); paint(600); }");
        var figure = page.Locator(".podium .age-reduction");
        Assert.Equal("12.34 years", await figure.TextContentAsync());
        var shown = await figure.GetAttributeAsync("data-home-count");
        Assert.NotEqual("0.00 years", shown);
        await page.EvaluateAsync("() => { const item=document.querySelector('.podium-item'); item.replaceWith(item.cloneNode(true)); }");
        Assert.Equal(shown, await figure.GetAttributeAsync("data-home-count"));
        await page.EvaluateAsync("() => { paint(700); paint(5000); }");
        Assert.Equal(0, await page.Locator("[data-home-count]").CountAsync());
        Assert.Equal("12.34 years", await figure.TextContentAsync());
        await page.EvaluateAsync("() => { document.querySelector('#leaderboardStatus').textContent='Leaderboard loaded.'; enter(); }");
        Assert.Equal(3, await page.EvaluateAsync<int>("started.length"));
        Assert.Equal(new[] { "−8.2", "−4.12", "+1.0" }, await page.Locator("tbody .age-reduction").AllTextContentsAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => { const y=[...document.querySelectorAll('tr')].map(e=>e.getBoundingClientRect().top); return y.every((v,i)=>!i||v>y[i-1]); }"));
        await page.Locator("tbody .age-reduction").First.EvaluateAsync("el=>el.textContent='−9.9'");
        Assert.Null(await page.Locator("tbody .age-reduction").First.GetAttributeAsync("data-home-count"));
        await page.EvaluateAsync("() => { paint(6000); window.dispatchEvent(new Event('pointerdown')); }");
        Assert.Equal("−9.9", await page.Locator("tbody .age-reduction").First.TextContentAsync());
        Assert.Equal(0, await page.Locator("[data-home-count], .home-board-settling").CountAsync());
        await page.Locator("tbody a").First.FocusAsync();
        await Assertions.Expect(page.Locator("tbody a").First).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Cancellation_DoesNotMoveALinkOutFromUnderAnEdgeClick()
    {
        await using var context = await Browser.NewContextAsync();
        var page = await ScoreboardAsync(context);
        await page.EvaluateAsync("""
            () => {
                document.querySelector('#leaderboardStatus').textContent='Leaderboard loaded.';
                enter(); started.forEach(a=>{a.pause();a.currentTime=0;});
                document.querySelector('tbody a').style.cssText='display:inline-block;height:44px;line-height:44px';
            }
            """);
        var target = (await page.Locator("tbody a").First.BoundingBoxAsync())!;
        await page.Mouse.ClickAsync(target.X + target.Width / 2, target.Y + target.Height - 1);
        // Coordinate input does not wait for the resulting navigation event.
        await Assertions.Expect(page).ToHaveURLAsync(App.BaseAddress + "#one");
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task Cancellation_KeepsScrolledPodiumLinkTargetsInPlace(int width, int height)
    {
        await using var context = await Browser.NewContextAsync(new() { ViewportSize = new() { Width=width, Height=height } });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync(App.BaseAddress.ToString());
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("async () => { await document.fonts.ready; }");
        await page.WaitForTimeoutAsync(2000);
        await page.Locator(".podium-item.first").EvaluateAsync("e=>scrollTo(0,scrollY+e.getBoundingClientRect().top+40)");
        await page.WaitForTimeoutAsync(100);
        var links = page.Locator(".podium a.athlete-name");
        var scores = page.Locator(".podium-score");
        foreach (var text in await scores.AllInnerTextsAsync()) Assert.Contains("\nreduced", text.Replace("\r\n", "\n"));
        var before = await links.EvaluateAllAsync<double[]>("es=>es.map(e=>e.getBoundingClientRect().top)");
        await page.EvaluateAsync("() => window.dispatchEvent(new Event('pointerdown'))");
        var after = await links.EvaluateAllAsync<double[]>("es=>es.map(e=>e.getBoundingClientRect().top)");
        Assert.NotEmpty(before);
        Assert.Equal(before.Length, after.Length);
        for (var i = 0; i < before.Length; i++) Assert.InRange(Math.Abs(before[i] - after[i]), 0, 0.5);
        foreach (var text in await scores.AllInnerTextsAsync()) Assert.Contains("\nreduced", text.Replace("\r\n", "\n"));
        Assert.Equal(0, await page.Locator(".home-scoreboard-live").CountAsync());
    }

    [Fact]
    public async Task TimerDeadline_ReleasesCountsWhenFramesNeverRun()
    {
        await using var context = await Browser.NewContextAsync();
        var page = await ScoreboardAsync(context, realTimers: true);
        // rAF is deliberately stalled, including Playwright's default polling.
        await page.WaitForFunctionAsync("() => !document.querySelector('[data-home-count]')", null,
            new() { PollingInterval = 50 });
        Assert.Equal("12.34 years", await page.Locator(".podium .age-reduction").TextContentAsync());
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    public async Task HomeToBoard_TransitionsAndCleansNamesWithoutPageErrors(int width, int height, bool dark)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width=width, Height=height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("""
            window.boardTransition=false;
            window.boardParsedAtReveal=false;
            window.addEventListener('pagereveal', e=>{
                if(e.viewTransition) window.boardParsedAtReveal=!!document.querySelector('#athleteLeaderboard')
                    && !!document.querySelector('#leaderboardBackAction');
                if(e.viewTransition) e.viewTransition.ready.then(()=>{
                    window.boardTransition=document.querySelector('#athleteLeaderboard')?.style.viewTransitionName==='lwc-board';
                },()=>{});
            });
            """);
        // Hold parsing after the shared header has run. Menu construction must
        // not release the receiving page's rel=expect render blocker early.
        await context.RouteAsync("**/leaderboard", async route =>
        {
            var response = await route.FetchAsync();
            var html = await response.TextAsync();
            await route.FulfillAsync(new()
            {
                Response = response,
                Body = html.Replace("<main data-leaderboard-page=\"full\">",
                    "<script src=\"/test-board-parser-pause.js\"></script><main data-leaderboard-page=\"full\">")
            });
        });
        await context.RouteAsync("**/test-board-parser-pause.js", async route =>
        {
            await Task.Delay(500);
            await route.FulfillAsync(new() { ContentType = "text/javascript", Body = "" });
        });
        var page=await context.NewPageAsync();
        var errors=new List<string>(); page.PageError+=(_,error)=>errors.Add(error);
        await page.GotoAsync("/");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".rankings-btn").ClickAsync();
        await page.WaitForURLAsync("**/leaderboard");
        await page.WaitForFunctionAsync("() => window.boardTransition");
        Assert.True(await page.EvaluateAsync<bool>("() => window.boardParsedAtReveal"));
        await page.WaitForFunctionAsync("() => document.querySelector('#athleteLeaderboard').style.viewTransitionName===''");
        Assert.Empty(errors);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
    }
}
