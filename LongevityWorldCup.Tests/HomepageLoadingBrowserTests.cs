using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class HomepageLoadingBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task Homepage_DiscoversTheTitleStylesBeforeStartingTheBody()
    {
        await using var context = await NewContextAsync(390);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/css/aesthetic-system.css*", async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });

        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.Commit });
            await page.Locator("head #homepage-startup").WaitForAsync(new() { State = WaitForSelectorState.Attached });
            // The parser has reached the startup script. All four preloads match their
            // consuming stylesheets, and Chrome is still prioritizing the document head.
            Assert.True(await page.EvaluateAsync<bool>("""
                () => !document.body && ['aesthetic', 'type', 'layout', 'depth'].every(name => {
                    const style = document.head.querySelector(`link[rel=stylesheet][href^="/css/${name}-system.css?"]`);
                    const preload = document.head.querySelector(`link[rel=preload][as=style][href^="/css/${name}-system.css?"]`);
                    return !!style && preload?.href === style.href;
                })
                """));
        }
        finally { release.TrySetResult(); }

        await ExpectHomepageLoadedAsync(page);
    }

    [Fact]
    public async Task Homepage_MobileToolbarFitsBeforeResponsiveStylesArrive()
    {
        await using var context = await NewContextAsync(390, true);
        var page = await context.NewPageAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/css/mobile-roughness.css*", async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });

        var toolbar = page.Locator(".leaderboard-controls .leaderboard-toolbar");
        double width;
        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.Commit });
            await ExpectHomepageLoadedAsync(page);
            width = await toolbar.EvaluateAsync<double>("bar => bar.getBoundingClientRect().width");
            Assert.InRange(width, 1, await toolbar.EvaluateAsync<double>("bar => bar.parentElement.clientWidth"));
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        }
        finally { release.TrySetResult(); }

        await page.WaitForLoadStateAsync(LoadState.Load);
        Assert.Equal(width, await toolbar.EvaluateAsync<double>("bar => bar.getBoundingClientRect().width"), 1);
    }

    [Fact]
    public async Task Homepage_PaintsItsTitleWhileIconsAndPodiumStylesArePending()
    {
        await using var context = await NewContextAsync(390);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
        await ObserveFirstScreenPaintAsync(page);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Hold(IRoute route)
        {
            await release.Task;
            await route.ContinueAsync();
        }
        await page.RouteAsync("**/css/leaderboard-podium.css*", Hold);
        await page.RouteAsync("**/css/portrait-system.css*", Hold);
        await page.RouteAsync("**/css/dark-system.css*", Hold);
        await page.RouteAsync("**/css/icons.css*", Hold);
        await page.RouteAsync("**/js/icons.js*", Hold);
        await page.RouteAsync("**/vendor/font-awesome/**/all.min.css*", Hold);
        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.Commit });
            await page.WaitForFunctionAsync("() => window.firstScreenPaints.has('header-title')");
        }
        finally
        {
            release.TrySetResult();
        }
        await page.WaitForLoadStateAsync(LoadState.Load);
        await page.WaitForFunctionAsync("() => !!window.LwcIcons && !!document.querySelector('i[data-lwc-icon] svg')");
        await Assertions.Expect(page.Locator("link[rel=stylesheet][href*='font-awesome']")).ToHaveAttributeAsync("media", "all");
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task Homepage_PaintsTheStyledChampionWhileTableStylesAndHeaderScriptsArePending(int width)
    {
        await using var context = await NewContextAsync(width);
        var page = await context.NewPageAsync();
        await ObserveFirstScreenPaintAsync(page);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Hold(IRoute route)
        {
            await release.Task;
            await route.ContinueAsync();
        }
        await page.RouteAsync("**/css/leaderboard-content.css*", Hold);
        await page.RouteAsync("**/css/homepage.css*", Hold);
        await page.RouteAsync("**/css/portrait-system.css*", Hold);
        await page.RouteAsync("**/css/share-sheet.css*", Hold);
        await page.RouteAsync("**/js/celebration.js*", Hold);
        await page.RouteAsync("**/js/site-header.js*", Hold);

        var portrait = page.Locator(".podium-item.first img.podium-portrait");
        double paintedWidth;
        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.Commit });
            await page.WaitForFunctionAsync("""
                () => ['header-title', 'champion-name', 'champion-portrait'].every(id => window.firstScreenPaints.has(id))
                """);
            await Assertions.Expect(page.Locator("head link[rel=preload][as=image]")).ToHaveAttributeAsync("href", (await portrait.GetAttributeAsync("src"))!);
            await Assertions.Expect(portrait).ToHaveCSSAsync("border-radius", "50%");
            paintedWidth = await portrait.EvaluateAsync<double>("img => img.getBoundingClientRect().width");
        }
        finally
        {
            release.TrySetResult();
        }
        await ExpectHomepageLoadedAsync(page);
        Assert.Equal(paintedWidth, await portrait.EvaluateAsync<double>("img => img.getBoundingClientRect().width"), 1);
        await page.WaitForLoadStateAsync(LoadState.Load);
        await Assertions.Expect(page.Locator("link[href*='/css/share-sheet.css']")).ToHaveAttributeAsync("media", "all");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FirstScreen_RespondsBeforeInteractionAssetsArrive(bool touch, bool reduceMotion)
    {
        await using var context = await NewContextAsync(touch ? 390 : 1440, touch);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = reduceMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference });
        await ObserveFirstScreenPaintAsync(page);
        var styles = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scripts = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var layer in new[] { "pointer", "touch", "motion" })
        {
            await page.RouteAsync($"**/css/{layer}.css*", async route => { await styles.Task; await route.ContinueAsync(); });
            await page.RouteAsync($"**/js/{layer}.js*", async route => { await scripts.Task; await route.ContinueAsync(); });
        }
        var link = page.Locator(".podium-item.first .athlete-profile-link").First;
        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.Commit });
            await page.WaitForFunctionAsync("() => window.firstScreenPaints.has('champion-portrait')");
            if (touch)
            {
                var cdp = await context.NewCDPSessionAsync(page);
                var image = link.Locator("img");
                await image.EvaluateAsync("img => img.scrollIntoView({ block: 'center' })");
                async Task PressAsync()
                {
                    var box = (await image.BoundingBoxAsync())!;
                    await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> {
                        ["type"] = "touchStart", ["touchPoints"] = new[] { new { x = box.X + box.Width / 2, y = box.Y + box.Height / 2 } }
                    });
                }
                Task CancelAsync() => cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> {
                    ["type"] = "touchCancel", ["touchPoints"] = Array.Empty<object>()
                });

                await PressAsync();
                await Assertions.Expect(link).ToHaveCSSAsync("filter", "brightness(0.86)");
                if (reduceMotion) await Assertions.Expect(link).ToHaveCSSAsync("scale", "none");
                await CancelAsync();
                await Assertions.Expect(link).ToHaveCSSAsync("filter", "none");
                scripts.TrySetResult();
                await page.WaitForFunctionAsync("() => !!window.LwcTouch");
                await PressAsync();
                await Assertions.Expect(link).ToHaveAttributeAsync("data-lwc-pressed", "control");
                await Assertions.Expect(link).ToHaveCSSAsync("filter", "brightness(0.86)");
                await CancelAsync();
                styles.TrySetResult();
                await page.WaitForLoadStateAsync(LoadState.Load);
                await PressAsync();
                if (reduceMotion)
                {
                    await Assertions.Expect(link).ToHaveCSSAsync("filter", "brightness(0.86)");
                    await Assertions.Expect(link).ToHaveCSSAsync("scale", "none");
                }
                else await Assertions.Expect(link).ToHaveCSSAsync("scale", "0.95");
                await CancelAsync();
                await Assertions.Expect(link).ToHaveCSSAsync("filter", "none");
                await cdp.DetachAsync();
            }
            else
            {
                var image = link.Locator("img");
                await image.HoverAsync();
                if (reduceMotion)
                {
                    await Assertions.Expect(link).ToHaveCSSAsync("translate", "none");
                    await Assertions.Expect(image).ToHaveCSSAsync("transform", "none");
                    await Assertions.Expect(image).ToHaveCSSAsync("outline-style", "solid");
                }
                else await Assertions.Expect(link).ToHaveCSSAsync("translate", "0px -5px");
                await page.Keyboard.PressAsync("Tab");
                await link.FocusAsync();
                await Assertions.Expect(link).ToHaveCSSAsync("outline-style", "solid");
            }
        }
        finally { styles.TrySetResult(); scripts.TrySetResult(); }
        await page.WaitForLoadStateAsync(LoadState.Load);
        foreach (var layer in new[] { "pointer", "touch", "motion" })
            await Assertions.Expect(page.Locator($"link[href*='/css/{layer}.css']")).ToHaveAttributeAsync("media", "all");
    }

    private static Task ObserveFirstScreenPaintAsync(IPage page) => page.AddInitScriptAsync("""
        const pending = new Map([
            ['header-title', 'header .bannertext'],
            ['champion-name', '.podium-item.first:not(.podium-skeleton-item) .athlete-name'],
            ['champion-portrait', '.podium-item.first:not(.podium-skeleton-item) img.podium-portrait']
        ]);
        const marker = new MutationObserver(() => {
            for (const [id, selector] of pending) {
                const node = document.querySelector(selector);
                if (!node) continue;
                node.setAttribute('elementtiming', id);
                pending.delete(id);
            }
            if (!pending.size) marker.disconnect();
        });
        marker.observe(document, { childList: true, subtree: true });
        window.firstScreenPaints = new Set();
        new PerformanceObserver(list => {
            for (const entry of list.getEntries())
                if (entry.renderTime > 0) window.firstScreenPaints.add(entry.identifier);
        }).observe({ type: 'element', buffered: true });
        """);

    [Fact]
    public async Task Homepage_KeepsPaintedAthletesAndCanFindAthletesOutsideTheTopTen()
    {
        await using var context = await NewContextAsync(390);
        await context.AddInitScriptAsync("""
            document.addEventListener('DOMContentLoaded', () => {
                window.originalChampion = document.querySelector('.podium-item.first img');
                window.originalRow = document.querySelector('tr.server-rendered-leaderboard-row:has(img.portrait)');
            }, { capture: true, once: true });
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await ExpectHomepageLoadedAsync(page);
        var rows = page.Locator(".leaderboard tbody tr[data-athlete-name]");
        await Assertions.Expect(rows).ToHaveCountAsync(10);
        Assert.True(await page.EvaluateAsync<bool>("() => window.originalChampion === document.querySelector('.podium-item.first img') && window.originalRow.isConnected"));

        var outsideTopTen = await page.EvaluateAsync<string>("""
            async () => {
                const names = new Set([...document.querySelectorAll('.leaderboard tbody tr[data-athlete-name]')].map(row => row.dataset.athleteName));
                return (await window.getSharedAthletes()).find(athlete => !names.has(athlete.Name)).Name;
            }
            """);
        await page.Locator("#athleteSearch").FillAsync(outsideTopTen);
        await Assertions.Expect(page.Locator(".leaderboard tbody tr[data-athlete-name]:visible").First).ToHaveAttributeAsync("data-athlete-name", outsideTopTen);
        await page.Locator("#athleteSearch").FillAsync("");
        await Assertions.Expect(page.Locator(".leaderboard tbody tr[data-athlete-name]:visible")).ToHaveCountAsync(7);
    }

    [Theory]
    [InlineData(390, "improvement")]
    [InlineData(1440, "bortz-improvement")]
    public async Task Homepage_KeepsFilteredPreviewRowsAndKeyboardFocusDuringHydration(int width, string view)
    {
        await using var context = await NewContextAsync(width);
        var page = await context.NewPageAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/data/athletes", async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });

        try
        {
            await page.GotoAsync($"/?view={view}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var preview = page.Locator(".leaderboard tr.server-rendered-leaderboard-row:visible");
            await Assertions.Expect(preview.First).ToBeVisibleAsync();
            await preview.EvaluateAllAsync<object?>("""
                rows => {
                    window.paintedRows = rows.map(row => ({ row, portrait: row.querySelector('img.portrait') }));
                    window.focusedAthlete = rows.at(-1).querySelector('.athlete-name');
                    window.focusedAthlete.focus({ preventScroll: true });
                }
                """);
        }
        finally
        {
            release.TrySetResult();
        }

        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        Assert.True(await page.EvaluateAsync<bool>(
            "() => window.paintedRows.every(({row, portrait}) => row.isConnected && (!portrait || portrait === row.querySelector('img.portrait')))"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement === window.focusedAthlete"));
    }

    [Theory]
    [InlineData(320, "improvement", "Improvement")]
    [InlineData(1440, "bortz-improvement", "Bortz improvement")]
    public async Task Homepage_LabelsNewlyDisplayedRowsWithTheSelectedMetric(int width, string view, string metric)
    {
        await using var context = await NewContextAsync(width);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await ExpectHomepageLoadedAsync(page);
        await page.Locator(".sidebar-toggle").ClickAsync();
        await page.Locator($"label[data-aging-clock-view='{view}']").ClickAsync();

        await Assertions.Expect(page.Locator("#leaderboardMetricHeader")).ToHaveTextAsync(metric);
        var scores = page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .age-reduction-td");
        await Assertions.Expect(scores.First).ToBeVisibleAsync();
        Assert.All(await scores.EvaluateAllAsync<string[]>("cells => cells.map(cell => cell.dataset.label)"),
            label => Assert.Equal(metric, label));
    }

    [Fact]
    public async Task Homepage_FetchesItsDataWhileModulesAreStillLoading()
    {
        await using var context = await NewContextAsync(1440);
        var page = await context.NewPageAsync();
        var releaseModule = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var athletesRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventsRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        page.Request += (_, request) =>
        {
            var path = new Uri(request.Url).AbsolutePath;
            if (path == "/api/data/athletes") athletesRequested.TrySetResult();
            if (path == "/api/events") eventsRequested.TrySetResult();
        };
        await page.RouteAsync("**/js/misc.js*", async route =>
        {
            await releaseModule.Task;
            await route.ContinueAsync();
        });

        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Task.WhenAll(athletesRequested.Task, eventsRequested.Task).WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            releaseModule.TrySetResult();
        }

        await ExpectHomepageLoadedAsync(page);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/api/data/athletes", 1440)]
    [InlineData("/api/events", 390)]
    public async Task StalledResponseBody_IsAbortedAndRetried_ThenOffersWorkingRecovery(string target, int width)
    {
        await using var context = await NewContextAsync(width);
        await context.AddInitScriptAsync(
            """
            (() => {
                const fetchNormally = window.fetch.bind(window);
                window.publicDataAttempts = {};
                window.publicDataAborts = 0;
                window.holdPublicData = true;
                window.fetch = (input, options) => {
                    const path = new URL(input, location.href).pathname;
                    if (path.startsWith('/api/')) {
                        window.publicDataAttempts[path] = (window.publicDataAttempts[path] || 0) + 1;
                    }
                    if (path === TARGET && window.holdPublicData) {
                        // Headers arrive, but the JSON body never finishes. A timeout
                        // around fetch() alone cannot recover from this failure.
                        return Promise.resolve(new Response(new ReadableStream({
                            start(controller) {
                                options?.signal?.addEventListener('abort', () => {
                                    window.publicDataAborts++;
                                    controller.error(new DOMException('Aborted', 'AbortError'));
                                }, { once: true });
                            }
                        }), { headers: { 'Content-Type': 'application/json' } }));
                    }
                    return fetchNormally(input, options);
                };
            })();
            """.Replace("TARGET", System.Text.Json.JsonSerializer.Serialize(target)));
        var page = await context.NewPageAsync();
        var pausedTime = DateTime.UtcNow;
        await page.Clock.InstallAsync(new() { TimeDate = pausedTime.AddHours(-1) });
        // Module loading must not consume the request timeout before the test advances time.
        await page.Clock.PauseAtAsync(pausedTime);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("() => window.modulesReady");
        await page.WaitForFunctionAsync("path => window.publicDataAttempts[path] === 1", target);
        // Only the held response should cross the synthetic deadline. Let the other
        // shared request finish consuming its real response body before advancing time.
        await page.EvaluateAsync("path => path === '/api/events' ? window.getSharedAthletes() : window.getSharedEvents()", target);
        await page.Clock.FastForwardAsync(10_001);
        await page.WaitForFunctionAsync("path => window.publicDataAttempts[path] === 2", target,
            new() { Timeout = 3000 });
        await page.Clock.FastForwardAsync(10_001);
        await page.WaitForFunctionAsync("() => window.publicDataAborts === 2");
        // Font/size observers can request a fit as the final timeout settles.
        // Fitting must not consume recovery by starting another data request.
        await page.EvaluateAsync("fitHomepageHighlights()");
        await Assertions.Expect(page.Locator("#eventsStatus")).ToHaveTextAsync("Events could not load.");
        Assert.Equal(2, await page.EvaluateAsync<int>("path => window.publicDataAttempts[path]", target));
        Assert.Equal(2, await page.EvaluateAsync<int>("window.publicDataAborts"));
        Assert.Equal(0, await page.Locator("#eventsTable .events-skeleton-row").CountAsync());
        if (target == "/api/data/athletes")
        {
            await Assertions.Expect(page.Locator(".leaderboard-retry-button")).ToBeVisibleAsync();
            Assert.Equal(0, await page.Locator(".podium-skeleton-item").CountAsync());
        }
        await CaptureAsync(page, $"failure-{width}");

        await page.EvaluateAsync("window.holdPublicData = false");
        if (target == "/api/data/athletes") await page.Locator(".leaderboard-retry-button").ClickAsync();
        await page.Locator("#events-root").GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await ExpectHomepageLoadedAsync(page);
        Assert.Equal(3, await page.EvaluateAsync<int>("path => window.publicDataAttempts[path]", target));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await CaptureAsync(page, $"recovered-{width}");
    }

    [Theory]
    [InlineData("/api/bitcoin/total-received")]
    [InlineData("/api/bitcoin/btcusd")]
    public async Task Podium_ShowsAthletesBeforePrizeData_ThenUpdatesAmountsWithoutReplacingCards(string slowPath)
    {
        await using var context = await NewContextAsync(1440);
        var releasePrize = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prizeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var totalRequests = 0;
        await context.RouteAsync("**/api/bitcoin/*", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/bitcoin/total-received") Interlocked.Increment(ref totalRequests);
            if (path == slowPath)
            {
                prizeRequested.TrySetResult();
                await releasePrize.Task;
            }
            if (path is not ("/api/bitcoin/total-received" or "/api/bitcoin/btcusd"))
            {
                await route.ContinueAsync();
                return;
            }
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = path.EndsWith("total-received", StringComparison.Ordinal)
                    ? """{"totalReceivedSatoshis":100000000}"""
                    : """{"btcToUsdRate":50000}"""
            });
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await prizeRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ExpectHomepageLoadedAsync(page);
            await page.Locator(".podium-item.first .athlete-name").FocusAsync();
            await page.EvaluateAsync("window.originalPodiumCard = document.querySelector('.podium-item.first')");
        }
        finally
        {
            releasePrize.TrySetResult();
        }

        await Assertions.Expect(page.Locator(".podium-item.first .prize-money")).ToHaveTextAsync("$27000.00");
        await Assertions.Expect(page.Locator(".podium-item.first .btc-amount")).ToHaveTextAsync("(0.54000000 BTC)");
        await Assertions.Expect(page.Locator(".podium-item.second .prize-money")).ToHaveTextAsync("$11250.00");
        await Assertions.Expect(page.Locator(".podium-item.third .prize-money")).ToHaveTextAsync("$6750.00");
        await Assertions.Expect(page.Locator(".podium-item.first .athlete-name")).ToBeFocusedAsync();
        Assert.True(await page.EvaluateAsync<bool>("window.originalPodiumCard === document.querySelector('.podium-item.first')"));
        Assert.Equal(1, totalRequests);
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task Podium_LoadingPlaceholdersReserveVisiblePrizeSteps(int width)
    {
        await using var context = await NewContextAsync(width);
        await context.AddInitScriptAsync(
            """
            document.addEventListener('DOMContentLoaded', () => {
                // Exercise the client-rendered loading state, even when SSR is available.
                const podium = document.querySelector('.podium');
                podium.replaceChildren();
                delete podium.dataset.serverRendered;
            });
            """);
        var releaseAthletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            await releaseAthletes.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        double[] stepHeights = [];
        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Assertions.Expect(page.Locator(".podium-skeleton-item")).ToHaveCountAsync(3);
            await Assertions.Expect(page.Locator(".podium-skeleton-item .podium-item-lower")).ToHaveCountAsync(3);
            foreach (var step in await page.Locator(".podium-skeleton-item .podium-item-lower").AllAsync())
            {
                await Assertions.Expect(step).ToBeVisibleAsync();
                var bounds = await step.BoundingBoxAsync();
                Assert.NotNull(bounds);
                Assert.True(bounds.Height >= 40, "Loading prize steps must reserve visible space.");
            }
            stepHeights = await page.EvaluateAsync<double[]>(
                """
                () => ['first', 'second', 'third'].map(rank =>
                    document.querySelector(`.podium-skeleton-item.${rank} .podium-item-lower`)
                        .getBoundingClientRect().height)
                """);
            var geometry = await page.EvaluateAsync<double[]>(
                """
                () => ['first', 'second', 'third'].map(rank =>
                    document.querySelector(`.podium-skeleton-item.${rank} .podium-item-lower`)
                        .getBoundingClientRect().top)
                """);
            if (width > 768)
            {
                Assert.True(geometry[0] < geometry[1] && geometry[1] < geometry[2]);
            }
            else
            {
                Assert.True(geometry[0] < geometry[1] && geometry[0] < geometry[2]);
            }
        }
        finally
        {
            releaseAthletes.TrySetResult();
        }
        await ExpectHomepageLoadedAsync(page);
        Assert.Equal(0, await page.Locator(".podium-skeleton-item").CountAsync());
        var ranks = new[] { "first", "second", "third" };
        for (var index = 0; index < ranks.Length; index++)
        {
            var bounds = await page.Locator($".podium-item.{ranks[index]} .podium-item-lower").BoundingBoxAsync();
            Assert.NotNull(bounds);
            Assert.InRange(Math.Abs(bounds.Height - stepHeights[index]), 0, 1);
        }
    }

    [Theory]
    [InlineData("/api/data/athletes")]
    [InlineData("/api/events")]
    public async Task TransientDataFailure_RecoversAutomaticallyWithoutDuplicateConsumerRequests(string target)
    {
        await using var context = await NewContextAsync(390);
        var attempts = 0;
        await context.RouteAsync($"**{target}", async route =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                await route.FulfillAsync(new() { Status = 503, Body = "Temporarily unavailable" });
            else
                await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectHomepageLoadedAsync(page);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task UnavailableExchangeRate_KeepsPodiumAthletesAndDonationLinks()
    {
        await using var context = await NewContextAsync(1440);
        var attempts = 0;
        await context.RouteAsync("**/api/bitcoin/btcusd", async route =>
        {
            Interlocked.Increment(ref attempts);
            await route.FulfillAsync(new() { Status = 503, Body = "Temporarily unavailable" });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectHomepageLoadedAsync(page);
        await Assertions.Expect(page.Locator(".podium-item-lower[aria-busy='false'][href='#contribute']")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator(".podium-item.first .prize-money")).ToHaveTextAsync("—");
        Assert.Equal(2, attempts);
    }

    private async Task<IBrowserContext> NewContextAsync(int width, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            HasTouch = touch,
            IsMobile = touch,
            ViewportSize = new() { Width = width, Height = 1000 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }

    private static async Task ExpectHomepageLoadedAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator("#eventsStatus")).ToHaveTextAsync("Events loaded.");
        await Assertions.Expect(page.Locator(".podium-item:not(.podium-skeleton-item)")).ToHaveCountAsync(3);
        Assert.True(await page.Locator("#eventsTable tr.main-row").CountAsync() > 0);
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("LWC_LOADING_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        await page.Locator("#events-root-index").ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}.png") });
    }
}
