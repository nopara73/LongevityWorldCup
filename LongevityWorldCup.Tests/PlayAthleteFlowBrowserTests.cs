using Microsoft.Playwright;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PlayAthleteFlowBrowserTests
{
    [Fact]
    public async Task NewAthleteNavigation_KeepsJoinUrlPanelAndBackActionsInSync()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.Equal("/play", new Uri(page.Url).AbsolutePath);

        var joinStatsRequestTask = page.WaitForRequestAsync(request =>
            request.Url.Contains("/api/site-statistics/event", StringComparison.OrdinalIgnoreCase) &&
            (request.PostData ?? string.Empty).Contains("\"eventName\":\"onboarding_entry_viewed\"", StringComparison.Ordinal));
        await page.Locator("#newGameBtn").ClickAsync();
        await page.WaitForURLAsync("**/join");
        var joinStatsRequest = await joinStatsRequestTask;
        using (var statsPayload = JsonDocument.Parse(joinStatsRequest.PostData ?? "{}"))
        {
            var root = statsPayload.RootElement;
            Assert.Equal("onboarding_entry_viewed", root.GetProperty("eventName").GetString());
            Assert.Equal("onboarding", root.GetProperty("flow").GetString());
            Assert.Equal("/join", root.GetProperty("route").GetString());
            Assert.Equal("join_game", root.GetProperty("component").GetString());
            Assert.Equal("viewed", root.GetProperty("outcome").GetString());
        }
        await ExpectActivePlayPanelAsync(page, "joinTrackPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        await ExpectActionStackDockedInViewportAsync(page, ".play-join-actions");
        Assert.Equal("/join", new Uri(page.Url).AbsolutePath);
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Start amateur" }).IsVisibleAsync());
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Go pro" }).IsVisibleAsync());
        Assert.True(await page.GetByRole(AriaRole.Link, new() { Name = "Try our longevitymaxxing lifestyle challenge instead" }).IsVisibleAsync());

        await page.GoBackAsync();
        await page.WaitForURLAsync("**/play");
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("/play", new Uri(page.Url).AbsolutePath);

        await page.GoForwardAsync();
        await page.WaitForURLAsync("**/join");
        await ExpectActivePlayPanelAsync(page, "joinTrackPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        await ExpectActionStackDockedInViewportAsync(page, ".play-join-actions");
        Assert.Equal("/join", new Uri(page.Url).AbsolutePath);

        await page.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();
        await page.WaitForURLAsync("**/play");
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("/play", new Uri(page.Url).AbsolutePath);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "joinTrackPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        await ExpectActionStackDockedInViewportAsync(page, ".play-join-actions");
        Assert.Equal("/join", new Uri(page.Url).AbsolutePath);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ExistingAthleteNavigation_KeepsUrlPanelAndBackActionsInSync()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            const athlete = {
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Division: 'Open',
                Flag: 'Hungary',
                PersonalLink: 'https://example.test/browser-test-athlete',
                MediaContact: 'browser-test-athlete@example.test',
                Why: 'Testing the athlete navigation flow.',
                ProfilePic: 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==',
                Biomarkers: []
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.Equal("/play", new Uri(page.Url).AbsolutePath);

        await page.Locator("#continueGameBtn").ClickAsync();
        await page.WaitForURLAsync("**/select-athlete");
        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("/select-athlete", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#athleteSelectionTitle").InnerTextAsync());
        Assert.Equal("Browser Test Athlete", await page.Locator("#playAthleteInput").InputValueAsync());
        Assert.True(await page.Locator("#playConfirmAthleteBtn").IsEnabledAsync());
        await ExpectAthletePictureFallbackAsync(page, "#athleteSelectionPicture");

        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForURLAsync("**/dashboard");
        await ExpectActivePlayPanelAsync(page, "athleteDashboardPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("/dashboard", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#athleteDashboardTitle").InnerTextAsync());
        await ExpectAthletePictureFallbackAsync(page, "#athleteDashboardPicture");
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Longevitymaxxing" }).IsVisibleAsync());

        await page.GoBackAsync();
        await page.WaitForURLAsync("**/select-athlete");
        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("/select-athlete", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#playAthleteInput").InputValueAsync());
        Assert.True(await page.Locator("#playConfirmAthleteBtn").IsEnabledAsync());

        await page.GoForwardAsync();
        await page.WaitForURLAsync("**/dashboard");
        await ExpectActivePlayPanelAsync(page, "athleteDashboardPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("/dashboard", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#athleteDashboardTitle").InnerTextAsync());

        await page.GetByRole(AriaRole.Button, new() { Name = "Edit profile" }).ClickAsync();
        await page.WaitForURLAsync("**/edit-profile");
        Assert.Equal("/edit-profile", new Uri(page.Url).AbsolutePath);
        await page.WaitForFunctionAsync("() => document.querySelector('#character-title')?.textContent?.trim() === 'Browser Test Athlete'");
        Assert.Equal("Browser Test Athlete", await page.Locator("#character-title").InnerTextAsync());
        await page.WaitForFunctionAsync("() => document.querySelector('#divisionDisplaySelect')?.value === 'Open'");
        Assert.Equal("Open", await page.Locator("#divisionDisplaySelect").InputValueAsync());

        await page.GetByRole(AriaRole.Button, new() { Name = "Back" }).ClickAsync();
        await page.WaitForURLAsync("**/dashboard");
        await ExpectActivePlayPanelAsync(page, "athleteDashboardPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("/dashboard", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#athleteDashboardTitle").InnerTextAsync());

        await page.GetByRole(AriaRole.Button, new() { Name = "Change athlete" }).ClickAsync();
        await page.WaitForURLAsync("**/select-athlete");
        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("/select-athlete", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Test Athlete", await page.Locator("#playAthleteInput").InputValueAsync());
        Assert.True(await page.Locator("#playConfirmAthleteBtn").IsEnabledAsync());

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ExistingAthleteSelectionWithStoredAthlete_WaitsForProfilePictureBeforeRevealingPanel()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            const athlete = {
                Name: 'Slow Picture Athlete',
                DisplayName: 'Slow Picture Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Division: 'Open',
                Flag: 'Hungary',
                PersonalLink: 'https://example.test/slow-picture-athlete',
                MediaContact: 'slow-picture-athlete@example.test',
                Why: 'Testing the athlete navigation flow.',
                ProfilePic: '/slow-selected-athlete.svg',
                Biomarkers: []
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var imageRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseImageResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/slow-selected-athlete.svg", async route =>
        {
            imageRequestReceived.TrySetResult();
            await releaseImageResponse.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "image/svg+xml",
                Body =
                    """
                    <svg xmlns="http://www.w3.org/2000/svg" width="240" height="240" viewBox="0 0 240 240">
                        <rect width="240" height="240" fill="#12213a"/>
                        <circle cx="120" cy="84" r="42" fill="#78d6ff"/>
                        <path d="M42 218c16-54 48-82 78-82s62 28 78 82z" fill="#f4f9ff"/>
                    </svg>
                    """
            });
        });

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "playStartPanel");

        await page.Locator("#continueGameBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.location.pathname === '/select-athlete'");
        await imageRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await page.WaitForTimeoutAsync(100);

        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.True(await page.Locator("#athleteSelectionPanel").IsHiddenAsync());

        releaseImageResponse.SetResult();

        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("Slow Picture Athlete", await page.Locator("#athleteSelectionTitle").InnerTextAsync());
        Assert.Equal("Slow Picture Athlete", await page.Locator("#playAthleteInput").InputValueAsync());
        Assert.True(await page.Locator("#playConfirmAthleteBtn").IsEnabledAsync());
        await page.WaitForFunctionAsync(
            """
            () => {
                const image = document.querySelector('#athleteSelectionPicture img');
                return image
                    && image.complete
                    && image.naturalWidth >= 200
                    && image.src.includes('/slow-selected-athlete.svg')
                    && !image.classList.contains('athlete-picture-placeholder');
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ExistingAthleteSelectionWithStoredAthlete_WaitsForProfilePictureDecodeBeforeRevealingPanel()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            const athlete = {
                Name: 'Decode Gated Athlete',
                DisplayName: 'Decode Gated Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Division: 'Open',
                Flag: 'Hungary',
                PersonalLink: 'https://example.test/decode-gated-athlete',
                MediaContact: 'decode-gated-athlete@example.test',
                Why: 'Testing the athlete navigation flow.',
                ProfilePic: '/assets/favicon-512x512.png',
                Biomarkers: []
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');

            const originalDecode = HTMLImageElement.prototype.decode;
            const pendingProfileDecodes = [];
            window.__profilePictureDecodeCount = 0;
            window.__releaseProfilePictureDecode = () => {
                while (pendingProfileDecodes.length) pendingProfileDecodes.shift()();
            };
            HTMLImageElement.prototype.decode = function () {
                const source = this.currentSrc || this.src || '';
                if (source.includes('/assets/favicon-512x512.png')) {
                    window.__profilePictureDecodeCount += 1;
                    return new Promise(resolve => pendingProfileDecodes.push(resolve));
                }
                return originalDecode ? originalDecode.call(this) : Promise.resolve();
            };
            """);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "playStartPanel");

        await page.Locator("#continueGameBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.location.pathname === '/select-athlete'");
        await page.WaitForFunctionAsync(
            """
            () => window.__profilePictureDecodeCount > 0
                || !document.getElementById('athleteSelectionPanel')?.hidden
            """);
        await page.WaitForTimeoutAsync(100);

        Assert.True(await page.EvaluateAsync<bool>("() => window.__profilePictureDecodeCount > 0"));
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.True(await page.Locator("#athleteSelectionPanel").IsHiddenAsync());

        await page.EvaluateAsync("() => window.__releaseProfilePictureDecode()");

        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("Decode Gated Athlete", await page.Locator("#athleteSelectionTitle").InnerTextAsync());
        Assert.Equal("Decode Gated Athlete", await page.Locator("#playAthleteInput").InputValueAsync());
        Assert.True(await page.Locator("#playConfirmAthleteBtn").IsEnabledAsync());
        await page.WaitForFunctionAsync(
            """
            () => {
                const image = document.querySelector('#athleteSelectionPicture img');
                return image
                    && image.complete
                    && image.naturalWidth > 0
                    && image.src.includes('/assets/favicon-512x512.png')
                    && !image.classList.contains('athlete-picture-placeholder');
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task CachedFallbackPicture_CompletesReadinessWithoutASecondLoadEvent()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => typeof window.playAthleteFlow?.replaceAthletePictureImmediately === 'function'");

        var result = await page.EvaluateAsync<string>(
            """
            async () => {
                const image = document.createElement('div');
                let source = '/tiny-athlete.png';
                let sourceComplete = true;
                Object.defineProperties(image, {
                    src: {
                        configurable: true,
                        get: () => source,
                        set: value => {
                            source = value;
                            sourceComplete = !String(value).includes('/assets/content-images/headshot.jpg');
                        }
                    },
                    currentSrc: {
                        configurable: true,
                        get: () => source
                    },
                    naturalWidth: {
                        configurable: true,
                        get: () => 1
                    },
                    naturalHeight: {
                        configurable: true,
                        get: () => 1
                    },
                    complete: {
                        configurable: true,
                        get: () => sourceComplete
                    },
                    decode: {
                        configurable: true,
                        value: () => new Promise(resolve => setTimeout(() => {
                            sourceComplete = true;
                            resolve();
                        }, 0))
                    }
                });

                const frame = document.createElement('div');
                const originalRequestAnimationFrame = window.requestAnimationFrame;
                window.requestAnimationFrame = callback => window.setTimeout(
                    () => callback(performance.now()),
                    0);
                let completed;
                try {
                    const readiness = window.playAthleteFlow.replaceAthletePictureImmediately(
                        frame,
                        image,
                        source);
                    completed = await Promise.race([
                        readiness.then(() => true),
                        new Promise(resolve => setTimeout(() => resolve(false), 500))
                    ]);
                } finally {
                    window.requestAnimationFrame = originalRequestAnimationFrame;
                }

                return `${completed}|${source}|${image.classList.contains('athlete-picture-placeholder')}`;
            }
            """);

        Assert.StartsWith("true|/assets/content-images/headshot.jpg?v=", result, StringComparison.Ordinal);
        Assert.EndsWith("|true", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1280, 720)]
    public async Task ExistingAthleteSelectionWithSavedName_WaitsForAthleteMatchBeforeRevealingPanel(
        int viewportWidth,
        int viewportHeight)
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            window.localStorage.setItem('selectedAthleteName', 'Browser Api Athlete');
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var athleteRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAthletesResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            athleteRequestReceived.TrySetResult();
            await releaseAthletesResponse.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body =
                    """
                    [{
                        "Name":"Browser Api Athlete",
                        "DisplayName":"Browser Api Athlete",
                        "DateOfBirth":{"Year":1980,"Month":5,"Day":20},
                        "Division":"Open",
                        "Flag":"Hungary",
                        "ProfilePic":"/assets/favicon-512x512.png",
                        "Biomarkers":[]
                    }]
                    """
            });
        });

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "playStartPanel");

        await page.Locator("#continueGameBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.location.pathname === '/select-athlete'");
        await athleteRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.True(await page.Locator("#athleteSelectionPanel").IsHiddenAsync());

        releaseAthletesResponse.SetResult();

        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        await page.WaitForFunctionAsync(
            """
            () => {
                const panel = document.getElementById('athleteSelectionPanel');
                const image = document.querySelector('#athleteSelectionPicture img');
                return panel && !panel.hidden
                    && !document.documentElement.classList.contains('play-panel-transitioning')
                    && image
                    && image.complete
                    && image.src.includes('/assets/favicon-512x512.png')
                    && !image.classList.contains('athlete-picture-placeholder');
            }
            """);
        var dockedWhenRefreshed = await page.Locator(".play-athlete-actions").EvaluateAsync<bool>(
            """
            async element => {
                window.LwcFlowActionDock?.refreshNow?.();
                const docked = element.classList.contains('flow-action-stack--docked');
                await new Promise(resolve => {
                    requestAnimationFrame(() => requestAnimationFrame(resolve));
                });
                return docked;
            }
            """);
        Assert.True(dockedWhenRefreshed, "Selection actions should dock as soon as the saved-athlete panel is ready.");
        await page.WaitForFunctionAsync(
            """
            () => {
                const element = document.querySelector('.play-athlete-actions');
                if (!element
                    || !element.classList.contains('flow-action-stack--docked')
                    || element.classList.contains('flow-action-stack--dock-entering')
                    || element.getAnimations().some(animation =>
                        animation.playState === 'pending' || animation.playState === 'running')) {
                    return false;
                }
                const rect = element.getBoundingClientRect();
                return rect.top >= -1 && rect.bottom <= window.innerHeight + 1;
            }
            """,
            null,
            new PageWaitForFunctionOptions { Timeout = 5_000 });
        var actionLayout = await page.Locator(".play-athlete-actions").EvaluateAsync<ActionStackTransitionLayout>(
            """
            element => {
                const rect = element.getBoundingClientRect();
                return {
                    Docked: element.classList.contains('flow-action-stack--docked'),
                    Top: rect.top,
                    Bottom: rect.bottom,
                    ViewportHeight: window.innerHeight
                };
            }
            """);
        Assert.True(actionLayout.Docked, "Selection actions should dock while the saved-athlete panel fades in.");
        Assert.True(actionLayout.Top >= -1, $"Selection actions top {actionLayout.Top} was above the viewport.");
        Assert.True(actionLayout.Bottom <= actionLayout.ViewportHeight + 1,
            $"Selection actions bottom {actionLayout.Bottom} exceeded viewport height {actionLayout.ViewportHeight}.");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("Browser Api Athlete", await page.Locator("#athleteSelectionTitle").InnerTextAsync());
        Assert.Equal("Browser Api Athlete", await page.Locator("#playAthleteInput").InputValueAsync());
        Assert.True(await page.Locator("#playConfirmAthleteBtn").IsEnabledAsync());
        await page.WaitForFunctionAsync(
            """
            () => {
                const image = document.querySelector('#athleteSelectionPicture img');
                return image
                    && image.complete
                    && image.src.includes('/assets/favicon-512x512.png')
                    && !image.classList.contains('athlete-picture-placeholder');
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task ExistingAthleteSelectionWithSavedName_DoesNotRevealSelectionAfterBackBeforeMatchLoads()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            window.localStorage.setItem('selectedAthleteName', 'Browser Api Athlete');
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var athleteRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAthletesResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            athleteRequestReceived.TrySetResult();
            await releaseAthletesResponse.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body =
                    """
                    [{
                        "Name":"Browser Api Athlete",
                        "DisplayName":"Browser Api Athlete",
                        "DateOfBirth":{"Year":1980,"Month":5,"Day":20},
                        "Division":"Open",
                        "Flag":"Hungary",
                        "ProfilePic":"/assets/favicon-512x512.png",
                        "Biomarkers":[]
                    }]
                    """
            });
        });

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "playStartPanel");

        await page.Locator("#continueGameBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.location.pathname === '/select-athlete'");
        await athleteRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.True(await page.Locator("body").EvaluateAsync<bool>(
            "body => body.classList.contains('play-route-hydrating') && body.getAttribute('aria-busy') === 'true'"));
        Assert.False(await page.Locator("#newGameBtn").IsVisibleAsync());

        await page.GoBackAsync();
        await page.WaitForFunctionAsync("() => window.location.pathname === '/play'");
        await ExpectActivePlayPanelAsync(page, "playStartPanel");

        releaseAthletesResponse.SetResult();
        await page.WaitForTimeoutAsync(350);

        Assert.Equal("/play", new Uri(page.Url).AbsolutePath);
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.True(await page.Locator("#athleteSelectionPanel").IsHiddenAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ExistingAthleteSelectionWithHungLookup_LeavesHydrationAtDeadline()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            const nativeSetTimeout = window.setTimeout.bind(window);
            window.setTimeout = (handler, delay, ...args) => nativeSetTimeout(
                handler,
                delay === 8000 ? 120 : delay,
                ...args);
            window.localStorage.setItem('selectedAthleteName', 'Never Arrives Athlete');
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var athleteRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAthletesResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            athleteRequestReceived.TrySetResult();
            await releaseAthletesResponse.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = "[]"
            });
        });

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        await page.Locator("#continueGameBtn").ClickAsync();
        await athleteRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await page.WaitForFunctionAsync(
            """
            () => window.location.pathname === '/select-athlete'
                && !document.body.classList.contains('play-route-hydrating')
                && !document.getElementById('athleteSelectionPanel').hidden
            """);

        Assert.Null(await page.Locator("body").GetAttributeAsync("aria-busy"));
        var backButton = page.Locator("#playSelectionBackBtn");
        Assert.True(await backButton.IsVisibleAsync());
        Assert.True(await backButton.IsEnabledAsync());

        await backButton.ClickAsync();
        await page.WaitForFunctionAsync("() => window.location.pathname === '/play'");
        await ExpectActivePlayPanelAsync(page, "playStartPanel");

        releaseAthletesResponse.SetResult();
        await page.WaitForTimeoutAsync(250);
        Assert.Equal("/play", new Uri(page.Url).AbsolutePath);
        await ExpectActivePlayPanelAsync(page, "playStartPanel");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DirectExistingAthleteSelectionWithSavedName_WaitsForAthleteMatchBeforeRevealingPanel()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        await context.AddInitScriptAsync(
            """
            window.localStorage.setItem('selectedAthleteName', 'Browser Api Athlete');
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var athleteRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAthletesResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            athleteRequestReceived.TrySetResult();
            await releaseAthletesResponse.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body =
                    """
                    [{
                        "Name":"Browser Api Athlete",
                        "DisplayName":"Browser Api Athlete",
                        "DateOfBirth":{"Year":1980,"Month":5,"Day":20},
                        "Division":"Open",
                        "Flag":"Hungary",
                        "ProfilePic":"/assets/favicon-512x512.png",
                        "Biomarkers":[]
                    }]
                    """
            });
        });

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/select-athlete", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await athleteRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await page.Locator("#playStartPanel").IsHiddenAsync());
        Assert.True(await page.Locator("#athleteSelectionPanel").IsHiddenAsync());
        Assert.True(await page.Locator("body").EvaluateAsync<bool>(
            "body => body.classList.contains('play-route-hydrating')"));
        Assert.False(await page.Locator("html").EvaluateAsync<bool>(
            "html => html.classList.contains('play-route-ready')"));

        releaseAthletesResponse.SetResult();

        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("/select-athlete", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Browser Api Athlete", await page.Locator("#athleteSelectionTitle").InnerTextAsync());
        Assert.Equal("Browser Api Athlete", await page.Locator("#playAthleteInput").InputValueAsync());
        Assert.True(await page.Locator("#playConfirmAthleteBtn").IsEnabledAsync());
        await page.WaitForFunctionAsync(
            """
            () => {
                const image = document.querySelector('#athleteSelectionPicture img');
                return image
                    && image.complete
                    && image.src.includes('/assets/favicon-512x512.png')
                    && !image.classList.contains('athlete-picture-placeholder');
            }
            """);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task DashboardRouteWithoutSelectedAthlete_FallsBackToSelectionPanel()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.location.pathname === '/select-athlete'");
        await ExpectActivePlayPanelAsync(page, "athleteSelectionPanel");
        await ExpectNoPlayPanelTransitionAsync(page);
        Assert.Equal("/select-athlete", new Uri(page.Url).AbsolutePath);
        Assert.Equal("Athlete selection", await page.Locator("#athleteSelectionTitle").InnerTextAsync());

        Assert.Empty(errors);
    }

    private static async Task ExpectActivePlayPanelAsync(IPage page, string activePanelId)
    {
        await page.WaitForFunctionAsync(
            """
            activePanelId => {
                const panels = Array.from(document.querySelectorAll('.play-hub-panel'));
                const activePanel = document.getElementById(activePanelId);
                return activePanel
                    && !activePanel.hidden
                    && panels.every(panel => panel.id === activePanelId || panel.hidden);
            }
            """,
            activePanelId);
    }

    private static async Task ExpectNoPlayPanelTransitionAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            """
            () => !document.documentElement.classList.contains('play-panel-transitioning')
            """);
    }

    private static async Task ExpectActionStackDockedInViewportAsync(IPage page, string selector)
    {
        await page.WaitForFunctionAsync(
            """
            selector => {
                const element = document.querySelector(selector);
                if (!element?.classList.contains('flow-action-stack--docked')) return false;

                const rect = element.getBoundingClientRect();
                return rect.top >= -1
                    && rect.left >= -1
                    && rect.right <= window.innerWidth + 1
                    && rect.bottom <= window.innerHeight + 1;
            }
            """,
            selector);
    }

    private static async Task ExpectAthletePictureFallbackAsync(IPage page, string frameSelector)
    {
        await page.WaitForFunctionAsync(
            """
            frameSelector => {
                const image = document.querySelector(`${frameSelector} img`);
                return image
                    && image.complete
                    && image.naturalWidth > 16
                    && image.naturalHeight > 16
                    && image.src.includes('/assets/content-images/headshot.jpg')
                    && image.classList.contains('athlete-picture-placeholder');
            }
            """,
            frameSelector);
    }

    private sealed class ActionStackTransitionLayout
    {
        public bool Docked { get; set; }
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double ViewportHeight { get; set; }
    }

}
