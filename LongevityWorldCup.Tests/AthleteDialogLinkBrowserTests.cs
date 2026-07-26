using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AthleteDialogLinkBrowserTests
{
    private const string MichaelSlug = "michael-lustgarten";

    [Theory]
    [InlineData("/about")]
    [InlineData("/history")]
    [InlineData("/events")]
    [InlineData("/longevitymaxxing")]
    [InlineData("/helstab-kihivas")]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task PublicAthleteLinkSurfaces_InstallOneScopedDialogRuntime(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Equal(1, CountOccurrences(html, "id=\"athleteDialogRuntime\""));
        Assert.Equal(1, CountOccurrences(html, "id=\"detailsModal\""));
        Assert.Equal(1, CountOccurrences(html, "window.openAthleteModalBySlug = function"));
        Assert.Contains("data-athlete-dialog-only=\"true\"", html);
        Assert.Contains("@scope (#athleteDialogRuntime)", html);
        Assert.Contains("--athlete-dialog-layer:10020", html);
        Assert.Contains("window.athleteDialogModulesReady = Promise.all", html);
        Assert.DoesNotContain("<!--ATHLETE-DIALOG-", html);
    }

    [Fact]
    public async Task CalculatorHydration_DoesNotWaitForDialogOnlyModules()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app, width: 1024, height: 720);

        var dialogModuleRequestReceived =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDialogModuleResponse =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/age-visualization.js*", async route =>
        {
            dialogModuleRequestReceived.TrySetResult();
            await releaseDialogModuleResponse.Task;
            await route.ContinueAsync();
        });

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);

        try
        {
            await page.GotoAsync(
                "/bortz-age?Year=1980&Month=6&Day=15&Date=2026-06-01",
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await dialogModuleRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await page.WaitForFunctionAsync(
                "() => document.querySelector('#blood-draw-date')?.value === '2026-06-01'");

            var dialogModulesReady = await page.EvaluateAsync<bool>(
                """
                () => Promise.race([
                    window.athleteDialogModulesReady.then(() => true),
                    new Promise(resolve => setTimeout(() => resolve(false), 100))
                ])
                """);
            Assert.False(dialogModulesReady);
        }
        finally
        {
            releaseDialogModuleResponse.TrySetResult();
        }
    }

    [Fact]
    public async Task AboutAthleteLink_QueuesUntilHydratedAndCloseRestoresTheCallingPage()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app, width: 1024, height: 720);
        await context.AddInitScriptAsync(
            """
            Object.defineProperty(window, '__athleteDialogNativeScrollApis', {
                configurable: true,
                value: {
                    scrollTo: window.scrollTo,
                    scrollBy: window.scrollBy,
                    scrollIntoView: Element.prototype.scrollIntoView
                }
            });
            """);

        var miscModuleRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseMiscModuleResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/misc.js*", async route =>
        {
            miscModuleRequestReceived.TrySetResult();
            await releaseMiscModuleResponse.Task;
            await route.ContinueAsync();
        });
        var athleteRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAthleteResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            athleteRequestReceived.TrySetResult();
            await releaseAthleteResponse.Task;
            await route.ContinueAsync();
        });

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);

        try
        {
            await page.GotoAsync("/about", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await AssertSharedDialogInstalledAsync(page);
            var scrollIsolation = await page.EvaluateAsync<ScrollIsolationDiagnostics>(
                """
                () => ({
                    ScrollToIsNative:
                        window.scrollTo === window.__athleteDialogNativeScrollApis.scrollTo,
                    ScrollByIsNative:
                        window.scrollBy === window.__athleteDialogNativeScrollApis.scrollBy,
                    ScrollIntoViewIsNative:
                        Element.prototype.scrollIntoView
                            === window.__athleteDialogNativeScrollApis.scrollIntoView,
                    ScrollRestoration: history.scrollRestoration,
                    ScrollGuardInstalled: window.__scrollGuardInstalled === true
                })
                """);
            Assert.True(scrollIsolation.ScrollToIsNative);
            Assert.True(scrollIsolation.ScrollByIsNative);
            Assert.True(scrollIsolation.ScrollIntoViewIsNative);
            Assert.Equal("auto", scrollIsolation.ScrollRestoration);
            Assert.False(scrollIsolation.ScrollGuardInstalled);
            await page.EvaluateAsync(
                """
                () => {
                    localStorage.removeItem('gmaSkipAll');
                    localStorage.removeItem('gmaAllGuesses');
                    sessionStorage.removeItem('gmaSeen');
                }
                """);

            var athleteLink = page.Locator(
                "main.documentation-page a[href='/athlete/michael-lustgarten']").First;
            await athleteLink.EvaluateAsync(
                "element => element.scrollIntoView({ behavior: 'auto', block: 'center' })");
            await page.EvaluateAsync(
                "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            await athleteLink.FocusAsync();
            var callingScrollY = await page.EvaluateAsync<double>("() => window.scrollY");

            Assert.True(callingScrollY > 0);
            Assert.True(await athleteLink.EvaluateAsync<bool>(
                "element => document.activeElement === element"));

            await athleteLink.ClickAsync();
            await miscModuleRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // A click made before either the modules or athlete index are ready
            // must remain in this document. Falling through to the anchor would
            // reload the homepage athlete route and look deceptively correct.
            Assert.Equal("/about", new Uri(page.Url).AbsolutePath);
            Assert.Equal(1, await page.Locator("main.documentation-page").CountAsync());
            Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
                "element => element.classList.contains('is-loading')"));

            releaseMiscModuleResponse.TrySetResult();
            await athleteRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseAthleteResponse.TrySetResult();
            await WaitForOpenAthleteDialogAsync(page, MichaelSlug);

            Assert.Equal($"/athlete/{MichaelSlug}", new Uri(page.Url).AbsolutePath);
            Assert.Equal(1, await page.Locator("main.documentation-page").CountAsync());
            Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
                "element => element.classList.contains('guess-mode')"));

            var skippedGuess = await page.EvaluateAsync<SkippedGuessDiagnostics>(
                """
                slug => {
                    const guesses = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}');
                    const guess = guesses[slug];
                    return {
                        Exists: Object.prototype.hasOwnProperty.call(guesses, slug),
                        Skipped: guess?.skipped === true,
                        ValueIsNull: guess?.value === null
                    };
                }
                """,
                MichaelSlug);
            Assert.True(skippedGuess.Exists);
            Assert.True(skippedGuess.Skipped);
            Assert.True(skippedGuess.ValueIsNull);

            await CloseAthleteDialogAsync(page, "/about");

            Assert.InRange(
                Math.Abs(await page.EvaluateAsync<double>("() => window.scrollY") - callingScrollY),
                0,
                1);
            Assert.True(await athleteLink.EvaluateAsync<bool>(
                "element => document.activeElement === element"));
        }
        finally
        {
            releaseMiscModuleResponse.TrySetResult();
            releaseAthleteResponse.TrySetResult();
        }
    }

    [Fact]
    public async Task DelegatedHandler_OpensDynamicAndOfficialLinksButLeavesModifiedClicksNative()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app, width: 1100, height: 760);
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "application/json",
            Body =
                """
                [
                  {
                    "Id": "athlete-dialog-dynamic-link",
                    "Type": 1,
                    "Text": "slug[michael_lustgarten]",
                    "OccurredAt": "2026-07-25T08:00:00Z",
                    "Relevance": 10
                  }
                ]
                """
        }));

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);

        await page.GotoAsync("/events", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertSharedDialogInstalledAsync(page);
        var eventLink = page.Locator(
            "#eventsTable tbody a.event-athlete-link[href='/athlete/michael-lustgarten']").First;
        await eventLink.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await InstallOpenTelemetryAsync(page);

        await eventLink.ClickAsync();
        await WaitForOpenAthleteDialogAsync(page, MichaelSlug);
        await AssertSingleSuppressedOpenAsync(page);
        Assert.Equal("/events", await page.EvaluateAsync<string>(
            "() => document.querySelector('main.event-board-page') ? '/events' : ''"));
        await CloseAthleteDialogAsync(page, "/events");

        await page.GotoAsync("/history", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertSharedDialogInstalledAsync(page);
        var historyLink = page.Locator(
            "main.documentation-page a[href='https://www.longevityworldcup.com/athlete/michael-lustgarten']").First;
        await historyLink.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Equal("_blank", await historyLink.GetAttributeAsync("target"));

        var nativeActivations = await historyLink.EvaluateAsync<NativeActivationDiagnostics[]>(
            """
            link => {
                const cases = [
                    { Name: 'Control', init: { ctrlKey: true, button: 0 } },
                    { Name: 'Meta', init: { metaKey: true, button: 0 } },
                    { Name: 'Shift', init: { shiftKey: true, button: 0 } },
                    { Name: 'Alt', init: { altKey: true, button: 0 } },
                    { Name: 'Middle', init: { button: 1 } }
                ];

                return cases.map(testCase => {
                    let reachedWindow = false;
                    let preventedByDialogHandler = false;
                    const stopNativeActivation = event => {
                        reachedWindow = true;
                        preventedByDialogHandler = event.defaultPrevented;
                        event.preventDefault();
                    };
                    window.addEventListener('click', stopNativeActivation, { once: true });
                    link.dispatchEvent(new MouseEvent('click', {
                        bubbles: true,
                        cancelable: true,
                        ...testCase.init
                    }));
                    return {
                        Name: testCase.Name,
                        ReachedWindow: reachedWindow,
                        PreventedByDialogHandler: preventedByDialogHandler
                    };
                });
            }
            """);

        Assert.Equal(5, nativeActivations.Length);
        Assert.All(nativeActivations, activation =>
        {
            Assert.True(activation.ReachedWindow, $"{activation.Name} activation was swallowed.");
            Assert.False(
                activation.PreventedByDialogHandler,
                $"{activation.Name} activation lost its native link behavior.");
        });
        Assert.Equal("/history", new Uri(page.Url).AbsolutePath);
        Assert.False(await page.Locator("#detailsModal").IsVisibleAsync());

        await InstallOpenTelemetryAsync(page);
        var pageCountBeforeHistoryClick = context.Pages.Count;
        await historyLink.ClickAsync();
        await WaitForOpenAthleteDialogAsync(page, MichaelSlug);

        Assert.Equal(pageCountBeforeHistoryClick, context.Pages.Count);
        Assert.Equal(1, await page.Locator("main.documentation-page").CountAsync());
        await AssertSingleSuppressedOpenAsync(page);
        await CloseAthleteDialogAsync(page, "/history");

        await page.EvaluateAsync(
            """
            () => {
                const link = document.createElement('a');
                link.id = 'dynamicApexAthleteLink';
                link.href = 'https://longevityworldcup.com/athlete/michael-lustgarten';
                link.textContent = 'Dynamic apex athlete link';
                document.querySelector('.documentation-document').appendChild(link);
            }
            """);
        await InstallOpenTelemetryAsync(page);
        await page.Locator("#dynamicApexAthleteLink").ClickAsync();
        await WaitForOpenAthleteDialogAsync(page, MichaelSlug);
        await AssertSingleSuppressedOpenAsync(page);
        await CloseAthleteDialogAsync(page, "/history");

        await page.EvaluateAsync(
            """
            () => {
                const link = document.createElement('a');
                link.id = 'unknownAthleteLink';
                link.href = '/athlete/not-a-real-athlete';
                link.textContent = 'Unknown athlete';
                document.querySelector('.documentation-document').appendChild(link);
            }
            """);
        await page.Locator("#unknownAthleteLink").ClickAsync();
        await page.WaitForURLAsync("**/athlete/not-a-real-athlete");
        Assert.Equal("/athlete/not-a-real-athlete", new Uri(page.Url).AbsolutePath);
    }

    [Fact]
    public async Task CalculatorAthleteDialog_PaintsAboveTheFixedActionDock()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app, width: 390, height: 844);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);
        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertSharedDialogInstalledAsync(page);
        await page.WaitForSelectorAsync(".flow-action-stack--docked");
        await page.WaitForFunctionAsync("() => Boolean(window.LwcBioAgeRankPreview)");
        await page.EvaluateAsync(
            """
            () => {
                document.getElementById('phenoAgeResult')?.classList.add('show');
                return window.LwcBioAgeRankPreview.render('phenoAgeRankPreview', {
                    clock: 'pheno',
                    ageReduction: -5,
                    dateOfBirth: new Date(1990, 0, 1)
                });
            }
            """);

        var athleteLink = page.Locator("#phenoAgeRankPreview .bioage-rank-row-name a").First;
        await athleteLink.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var profilePath = Assert.IsType<string>(await athleteLink.GetAttributeAsync("href"));
        Assert.DoesNotContain("_", profilePath);
        var athleteSlug = profilePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();

        await athleteLink.ClickAsync();
        await WaitForOpenAthleteDialogAsync(page, athleteSlug);

        var layers = await page.EvaluateAsync<LayerDiagnostics>(
            """
            () => {
                const modal = document.getElementById('detailsModal');
                const dock = document.querySelector('.flow-action-stack--docked');
                const topmost = document.elementFromPoint(
                    Math.floor(innerWidth / 2),
                    Math.max(0, innerHeight - 8));
                return {
                    Modal: Number.parseInt(getComputedStyle(modal).zIndex, 10),
                    Dock: Number.parseInt(getComputedStyle(dock).zIndex, 10),
                    ModalOwnsBottomEdge: Boolean(topmost?.closest('#detailsModal'))
                };
            }
            """);

        Assert.True(layers.Modal > layers.Dock);
        Assert.True(layers.ModalOwnsBottomEdge);
        await CloseAthleteDialogAsync(page, "/pheno-age");
    }

    [Fact]
    public async Task LeaderboardAndStandaloneLinks_RenderTheSameDialogStructureAndGeometry()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app, width: 1280, height: 900);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(15_000);
        await page.GotoAsync(
            "/leaderboard",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertSharedDialogInstalledAsync(page);
        await page.WaitForFunctionAsync(
            """
            () => [...document.querySelectorAll(
                '.leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name]')]
                .some(row => window.slugifyName(row.dataset.athleteName, true) === 'michael-lustgarten'
                    && row.getBoundingClientRect().width > 0
                    && row.getBoundingClientRect().height > 0)
            """);

        var leaderboardName = page.Locator(
            ".leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name='Michael Lustgarten'] .athlete-name");
        await leaderboardName.ClickAsync();
        await WaitForOpenAthleteDialogAsync(page, MichaelSlug);
        var leaderboardDialog = await MeasureDialogAsync(page);
        await CloseAthleteDialogAsync(page, "/leaderboard");

        await page.GotoAsync("/about", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertSharedDialogInstalledAsync(page);
        var standaloneLink = page.Locator(
            "main.documentation-page a[href='/athlete/michael-lustgarten']").First;
        await standaloneLink.ClickAsync();
        await WaitForOpenAthleteDialogAsync(page, MichaelSlug);
        var standaloneDialog = await MeasureDialogAsync(page);

        Assert.StartsWith("Michael Lustgarten, PhD", await page.Locator("#athleteName").InnerTextAsync());
        Assert.Equal(leaderboardDialog.Structure, standaloneDialog.Structure);
        Assert.Equal(leaderboardDialog.ContentComputedStyle, standaloneDialog.ContentComputedStyle);
        Assert.Equal(leaderboardDialog.Boxes.Length, standaloneDialog.Boxes.Length);
        foreach (var expectedBox in leaderboardDialog.Boxes)
        {
            var actualBox = Assert.Single(
                standaloneDialog.Boxes,
                box => box.Name == expectedBox.Name);
            AssertEquivalentBox(expectedBox, actualBox);
        }

        await CloseAthleteDialogAsync(page, "/about");
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertSharedDialogInstalledAsync(page);
        await page.Locator(
                ".archive-table a[href='/athlete/michael-lustgarten']")
            .First
            .ClickAsync();
        await WaitForOpenAthleteDialogAsync(page, MichaelSlug);
        Assert.StartsWith("Michael Lustgarten, PhD", await page.Locator("#athleteName").InnerTextAsync());
    }

    private static async Task<IBrowserContext> NewContextAsync(
        IBrowser browser,
        BrowserTestApp app,
        int width,
        int height)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/bitcoin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            var body = path.EndsWith("/donation-address", StringComparison.OrdinalIgnoreCase)
                ? """{"address":""}"""
                : path.EndsWith("/btcusd", StringComparison.OrdinalIgnoreCase)
                    ? """{"btcToUsdRate":0}"""
                    : """{"totalReceivedSatoshis":0}""";
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = body
            });
        });
        return context;
    }

    private static async Task AssertSharedDialogInstalledAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => typeof window.openAthleteModalBySlug === 'function'");
        Assert.Equal(1, await page.Locator("#detailsModal").CountAsync());
        Assert.Equal("dialog", await page.Locator("#detailsModal").GetAttributeAsync("role"));
    }

    private static Task WaitForOpenAthleteDialogAsync(IPage page, string athleteSlug) =>
        page.WaitForFunctionAsync(
            """
            slug => {
                const modal = document.getElementById('detailsModal');
                const content = modal?.querySelector('.modal-content');
                return modal?.style.display === 'block'
                    && content?.dataset.athleteSlug === slug
                    && !content.classList.contains('is-loading')
                    && !content.classList.contains('has-load-error')
                    && location.pathname === `/athlete/${slug}`;
            }
            """,
            athleteSlug);

    private static async Task CloseAthleteDialogAsync(IPage page, string expectedPath)
    {
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await page.WaitForFunctionAsync(
            """
            path => document.getElementById('detailsModal')?.style.display === 'none'
                && location.pathname === path
            """,
            expectedPath);
    }

    private static Task InstallOpenTelemetryAsync(IPage page) =>
        page.EvaluateAsync(
            """
            () => {
                const original = window.__athleteDialogOriginalOpen
                    || window.openAthleteModalBySlug;
                if (typeof original !== 'function') {
                    throw new Error('Shared athlete dialog opener is unavailable.');
                }

                window.__athleteDialogOriginalOpen = original;
                window.__athleteDialogOpenCalls = [];
                window.openAthleteModalBySlug = function(slug, options) {
                    window.__athleteDialogOpenCalls.push({
                        slug,
                        suppressGuessMyAge: options?.suppressGuessMyAge === true
                    });
                    return window.__athleteDialogOriginalOpen.call(this, slug, options);
                };
            }
            """);

    private static async Task AssertSingleSuppressedOpenAsync(IPage page)
    {
        var calls = await page.EvaluateAsync<OpenCallDiagnostics[]>(
            "() => window.__athleteDialogOpenCalls || []");
        var call = Assert.Single(calls);
        Assert.Equal(MichaelSlug, call.Slug);
        Assert.True(call.SuppressGuessMyAge);
    }

    private static async Task<DialogDiagnostics> MeasureDialogAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            """
            () => {
                const image = document.getElementById('modalProfilePic');
                return !image?.src || image.complete;
            }
            """);
        await page.EvaluateAsync("() => document.fonts?.ready || Promise.resolve()");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        return await page.EvaluateAsync<DialogDiagnostics>(
            """
            () => {
                const modal = document.getElementById('detailsModal');
                const content = modal.querySelector('.modal-content');
                const structure = element => {
                    const own = `${element.tagName.toLowerCase()}#${element.id || ''}`;
                    return `${own}(${[...element.children].map(structure).join(',')})`;
                };
                const box = (name, element) => {
                    const rect = element.getBoundingClientRect();
                    return {
                        Name: name,
                        X: rect.x,
                        Y: rect.y,
                        Width: rect.width,
                        Height: rect.height
                    };
                };
                const style = getComputedStyle(content);

                return {
                    Structure: structure(modal),
                    ContentComputedStyle: JSON.stringify({
                        position: style.position,
                        display: style.display,
                        width: style.width,
                        maxWidth: style.maxWidth,
                        height: style.height,
                        maxHeight: style.maxHeight,
                        padding: style.padding,
                        overflowY: style.overflowY,
                        borderRadius: style.borderRadius,
                        backgroundColor: style.backgroundColor,
                        boxShadow: style.boxShadow
                    }),
                    Boxes: [
                        box('modal', modal),
                        box('content', content),
                        box('profile', document.getElementById('athlete-profile')),
                        box('portrait', document.getElementById('modalProfilePic')),
                        box('name', document.getElementById('athleteName')),
                        box('info', modal.querySelector('.athlete-info'))
                    ]
                };
            }
            """);
    }

    private static void AssertEquivalentBox(BoxDiagnostics expected, BoxDiagnostics actual)
    {
        const double renderingTolerance = 0.75;
        Assert.InRange(Math.Abs(actual.X - expected.X), 0, renderingTolerance);
        Assert.InRange(Math.Abs(actual.Y - expected.Y), 0, renderingTolerance);
        Assert.InRange(Math.Abs(actual.Width - expected.Width), 0, renderingTolerance);
        Assert.InRange(Math.Abs(actual.Height - expected.Height), 0, renderingTolerance);
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

    private sealed class SkippedGuessDiagnostics
    {
        public bool Exists { get; set; }
        public bool Skipped { get; set; }
        public bool ValueIsNull { get; set; }
    }

    private sealed class NativeActivationDiagnostics
    {
        public string Name { get; set; } = "";
        public bool ReachedWindow { get; set; }
        public bool PreventedByDialogHandler { get; set; }
    }

    private sealed class OpenCallDiagnostics
    {
        public string Slug { get; set; } = "";
        public bool SuppressGuessMyAge { get; set; }
    }

    private sealed class DialogDiagnostics
    {
        public string Structure { get; set; } = "";
        public string ContentComputedStyle { get; set; } = "";
        public BoxDiagnostics[] Boxes { get; set; } = [];
    }

    private sealed class BoxDiagnostics
    {
        public string Name { get; set; } = "";
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed class LayerDiagnostics
    {
        public int Modal { get; set; }
        public int Dock { get; set; }
        public bool ModalOwnsBottomEdge { get; set; }
    }

    private sealed class ScrollIsolationDiagnostics
    {
        public bool ScrollToIsNative { get; set; }
        public bool ScrollByIsNative { get; set; }
        public bool ScrollIntoViewIsNative { get; set; }
        public string ScrollRestoration { get; set; } = "";
        public bool ScrollGuardInstalled { get; set; }
    }
}
