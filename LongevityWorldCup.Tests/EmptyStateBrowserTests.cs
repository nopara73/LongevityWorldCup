using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class EmptyStateBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/leaderboard")]
    [InlineData("/events")]
    [InlineData("/longevitymaxxing")]
    [InlineData("/select-athlete")]
    [InlineData("/internal/site-statistics.html")]
    [InlineData("/event-board-embed.html?embed=1&athlete=michael-lustgarten")]
    public async Task SharedPages_LoadOneVersionedPair(string path)
    {
        using var client = App.CreateClient();
        var html = await client.GetStringAsync(path);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "src=\"/js/empty-state.js\\?v=[^\"]+\""));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "href=\"/css/empty-state.css\\?v=[^\"]+\""));
    }

    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    public async Task Search_ReusesEmptyHostAndLeavesContentMotionToTheBoard(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard?search=zzzzemptyreview");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var host = page.Locator(".no-results-empty-state");
        await Assertions.Expect(host).ToHaveAttributeAsync("data-lwc-empty", "search");
        Assert.Equal(0, await host.EvaluateAsync<int>("e => e.getAnimations({subtree:true}).length"));
        await Assertions.Expect(host.Locator("svg")).ToHaveAttributeAsync("aria-hidden", "true");
        await Assertions.Expect(host.Locator("svg")).ToHaveAttributeAsync("focusable", "false");
        await Assertions.Expect(host.Locator("p")).ToHaveTextAsync("No athletes match this leaderboard.");
        await host.EvaluateAsync("e => window.reviewEmptyHost = e");
        var search = page.Locator("#athleteSearch");
        await search.ClickAsync();
        foreach (var query in new[] { "zzzzemptyreview2", "zzzzemptyreview3", "zzzzemptyreview4" })
        {
            await search.FillAsync(query);
            await page.WaitForFunctionAsync("query => new URL(location.href).searchParams.get('search') === query", query);
            Assert.True(await host.EvaluateAsync<bool>("e => e === window.reviewEmptyHost && !e.classList.contains('lwc-empty--enter')"));
        }
        Assert.True(await host.EvaluateAsync<bool>("e => e.getAnimations({subtree:true}).length > 0"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => document.querySelector('.lwc-empty').getAnimations({subtree:true}).length === 0");
        for (var i = 0; i < 3; i++)
        {
            await host.Locator(".show-all-athletes-btn").ClickAsync();
            await Assertions.Expect(host).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("tr[data-athlete-name]:visible").First).ToBeVisibleAsync();
            await search.FillAsync("zzzzemptyreview");
            await Assertions.Expect(host).ToBeVisibleAsync();
        }
        await page.WaitForTimeoutAsync(500);
        await Assertions.Expect(page.Locator(".lwc-empty__ghost")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task FlagSearch_ReplacesTheLoadingPlaceholderWithTheReadyEmptyState()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route => {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/leaderboard?group=flags&search=zzzzemptyreview", new() { WaitUntil = WaitUntilState.Commit });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await page.WaitForFunctionAsync("() => !!window.LwcEmptyState && document.readyState !== 'loading'");
            var loading = page.Locator(".flag-empty[aria-busy=true]");
            await Assertions.Expect(loading).ToHaveTextAsync("Loading flags...");
            await Assertions.Expect(loading.Locator("svg")).ToHaveCountAsync(0);
        }
        finally { release.TrySetResult(); }
        var empty = page.Locator(".flag-empty");
        await Assertions.Expect(empty).ToHaveTextAsync("No flags match this leaderboard.");
        await Assertions.Expect(empty).ToHaveAttributeAsync("data-lwc-empty", "flag");
        await Assertions.Expect(empty.Locator("svg")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".flag-empty[aria-busy=true]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task FilterAndFlagScenes_FollowTheActualBoardAndKeepTheirHosts()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await RouteAthleteFixtureAsync(context, athletes => {
            foreach (var athlete in athletes) { athlete!["CrowdAge"] = null; athlete["CrowdCount"] = 0; }
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator("label[for=view-crowd]").ClickAsync();
        await Assertions.Expect(page.Locator(".no-results-empty-state")).ToHaveAttributeAsync("data-lwc-empty", "filter");
        await page.Locator("#athleteSearch").FillAsync("zzzzemptyreview");
        await Assertions.Expect(page.Locator(".no-results-empty-state")).ToHaveAttributeAsync("data-lwc-empty", "search");
        await page.Locator("label[for=group-flags]").ClickAsync();
        var flag = page.Locator(".flag-empty");
        await Assertions.Expect(flag).ToHaveAttributeAsync("data-lwc-empty", "flag");
        await flag.EvaluateAsync("e => window.reviewFlagHost = e");
        await page.Locator("#athleteSearch").FillAsync("zzzzemptyreviewagain");
        await page.WaitForFunctionAsync("() => new URL(location.href).searchParams.get('search') === 'zzzzemptyreviewagain'");
        Assert.True(await flag.EvaluateAsync<bool>("e => e === window.reviewFlagHost"));
        await Assertions.Expect(flag).ToHaveTextAsync("No flags match this leaderboard.");
        await Assertions.Expect(flag.Locator("svg")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task ReusedHints_TrackTextAndClassesWithoutDecoratingOrdinaryNotes()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/select-athlete");
        await page.EvaluateAsync("() => window.reviewEmptyApi = window.LwcEmptyState");
        await page.AddScriptTagAsync(new() { Url = "/js/empty-state.js" });
        Assert.True(await page.EvaluateAsync<bool>("() => window.LwcEmptyState === window.reviewEmptyApi"));
        await page.Locator("#playAthleteInput").FillAsync("zzzzemptyreview");
        var hint = page.Locator(".athlete-search-empty");
        await Assertions.Expect(hint.Locator("svg")).ToHaveCountAsync(1);
        await page.Locator("#playAthleteInput").FillAsync("");
        await Assertions.Expect(page.Locator(".athlete-search-empty svg")).ToHaveCountAsync(0);
        // Synthetic stress fixture, separate from the real page's empty hint.
        await page.EvaluateAsync("""
            () => {
                const fixture = document.createElement('section');
                fixture.id = 'emptyStress';
                fixture.innerHTML = '<div id="lmxNotes"><div class="lmx-note"><strong>A real note.</strong></div></div><div class="empty-state">Unrelated content</div><div id="hintStress" class="lmx-timezone-empty">No matches</div>';
                document.body.append(fixture);
            }
            """);
        await Assertions.Expect(page.Locator("#hintStress svg")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#lmxNotes svg, #emptyStress > .empty-state svg")).ToHaveCountAsync(0);
        for (var i = 0; i < 10; i++)
        {
            await page.Locator("#hintStress").EvaluateAsync("e => e.textContent = 'No matches again'");
            await Assertions.Expect(page.Locator("#hintStress svg")).ToHaveCountAsync(1);
        }
        await page.Locator("#hintStress").EvaluateAsync("e => e.className = ''");
        await Assertions.Expect(page.Locator("#hintStress svg")).ToHaveCountAsync(0);
        await page.Locator("#emptyStress").EvaluateAsync("e => e.remove()");
    }

    [Fact]
    public async Task RestoreAndReducedMotion_StopActiveEntrancesAndLoops()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Locator("#athleteSearch").ClickAsync();
        await page.Locator("#athleteSearch").FillAsync("zzzzemptyreview");
        await Assertions.Expect(page.Locator(".lwc-empty")).ToBeVisibleAsync();
        await page.EvaluateAsync("() => { dispatchEvent(new PageTransitionEvent('pagehide', {persisted:true})); dispatchEvent(new PageTransitionEvent('pageshow', {persisted:true})); }");
        Assert.Equal(0, await page.Locator(".lwc-empty").EvaluateAsync<int>("e => e.getAnimations({subtree:true}).length"));
        await page.Locator("#athleteSearch").ClickAsync();
        Assert.True(await page.Locator(".lwc-empty").EvaluateAsync<bool>("e => e.getAnimations({subtree:true}).length > 0"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => !document.querySelector('.lwc-empty--enter') && document.querySelector('.lwc-empty').getAnimations({subtree:true}).length === 0");
    }

    [Fact]
    public async Task Statistics_UsesTheSameMotionGateWithoutAnObserverLoop()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/internal/site-statistics.html?tab=Onboarding%20Diagnostics");
        var host = page.Locator(".stats-page .empty-state:visible").First;
        await Assertions.Expect(host.Locator("svg")).ToHaveCountAsync(1);
        Assert.Equal(0, await host.EvaluateAsync<int>("e => e.getAnimations({subtree:true}).length"));
        await host.EvaluateAsync("e => { window.emptyMutations = 0; new MutationObserver(records => window.emptyMutations += records.length).observe(e, {childList:true, subtree:true, attributes:true}); }");
        await page.WaitForTimeoutAsync(300);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.emptyMutations"));
        await page.Locator("#statsRange").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await host.EvaluateAsync<bool>("e => e.getAnimations({subtree:true}).length > 0"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('.lwc-empty')].every(e => e.getAnimations({subtree:true}).length === 0)");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task PlayEmptyHint_AtEnlargedTextRemainsReachableAboveTheActions(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/select-athlete");
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '32px'");
        await page.Locator("#playAthleteInput").FillAsync("zzzzemptyreview");
        await Assertions.Expect(page.Locator(".athlete-search-empty svg")).ToHaveCountAsync(1);
        await WaitForFiniteAnimationsAsync(page);
        await page.EvaluateAsync("() => scrollTo(0, document.documentElement.scrollHeight)");
        await page.WaitForTimeoutAsync(100);
        var metrics = await page.EvaluateAsync<double[]>("""
            () => {
                const hint = document.querySelector('.athlete-search-empty').getBoundingClientRect();
                const actions = document.querySelector('.play-athlete-actions').getBoundingClientRect();
                const placeholder = document.querySelector('.play-athlete-panel > .flow-action-dock-placeholder');
                return [hint.top, hint.bottom, actions.top, placeholder?.getBoundingClientRect().height ?? 0,
                    parseFloat(placeholder?.style.height || '0'), scrollY, document.documentElement.scrollHeight];
            }
            """);
        Assert.True(metrics[0] >= 0 && metrics[1] <= metrics[2] - 4, $"{width}px: hint and dock {string.Join(", ", metrics)}");
    }
}
