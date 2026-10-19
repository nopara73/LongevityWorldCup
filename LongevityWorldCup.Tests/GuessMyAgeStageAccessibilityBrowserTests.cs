using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class GuessMyAgeStageAccessibilityBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task OpenRoundAsync(IPage page)
    {
        await page.GotoAsync("/athlete/patrick-ruff?guessmyage=1",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#detailsModal .modal-content.guess-mode:not(.is-loading)') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            async () => {
                await document.fonts.ready;
                await document.getElementById('modalProfilePic').decode().catch(() => {});
                await Promise.all([
                    ...document.querySelector('#athlete-profile .portrait-wrapper').getAnimations(),
                    ...document.getElementById('guessAgeContainer').getAnimations({ subtree: true })
                ]
                    .map(animation => animation.finished.catch(() => {})));
            }
            """);
    }

    [Theory]
    [InlineData(390, 844, 200)]
    [InlineData(320, 640, 200)]
    [InlineData(640, 360, 200)]
    [InlineData(568, 320, 100)]
    [InlineData(568, 240, 200)]
    [InlineData(720, 450, 200)]
    public async Task EnlargedStage_KeepsGlyphsAndEveryControlReachable(int width, int height, int textPercent)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US",
            ReducedMotion = ReducedMotion.Reduce, HasTouch = true,
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await OpenRoundAsync(page);
        await page.EvaluateAsync(
            """
            percent => {
                document.documentElement.style.fontSize = percent + '%';
                document.getElementById('gmaSkipAll').style.display = 'inline-block';
            }
            """, textPercent);

        // The top of an overflowing landscape card must not lie above scroll zero.
        await page.EvaluateAsync("() => document.querySelector('#detailsModal .modal-content').scrollTop = 0");
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const modal = document.querySelector('#detailsModal .modal-content').getBoundingClientRect();
                const heading = document.querySelector('.gma-heading').getBoundingClientRect();
                const portrait = document.querySelector('#athlete-profile .portrait-wrapper').getBoundingClientRect();
                return heading.top >= modal.top && portrait.top >= modal.top
                    && portrait.width >= 86 && Math.abs(portrait.width - portrait.height) < 1;
            }
            """));

        foreach (var selector in new[] { "#gmaRange", "#gmaSkipAll", ".gma-actions button:not([id]):not(.gma-btn--primary)", ".gma-actions .gma-btn--primary" })
        {
            var control = page.Locator(selector);
            await control.FocusAsync();
            await control.ScrollIntoViewIfNeededAsync();
            var accessible = await control.EvaluateAsync<JsonElement>(
                """
                element => {
                    const modal = document.querySelector('#detailsModal .modal-content');
                    const m = modal.getBoundingClientRect();
                    const r = element.getBoundingClientRect();
                    const text = document.createRange();
                    text.selectNodeContents(element);
                    const glyphs = [...text.getClientRects()];
                    return {
                        visible: r.top >= m.top && r.bottom <= Math.min(m.bottom, innerHeight) + 1
                            && r.left >= m.left && r.right <= m.right,
                        glyphsFit: glyphs.every(g => g.left >= r.left && g.right <= r.right
                            && g.top >= r.top && g.bottom <= r.bottom),
                        touchSize: r.width >= 44 && (element.tagName === 'INPUT' || r.height >= 44),
                        focused: document.activeElement === element,
                        horizontalOverflow: modal.scrollWidth - modal.clientWidth
                    };
                }
                """);
            Assert.True(accessible.GetProperty("visible").GetBoolean(), $"Unreachable {selector}: {accessible}");
            Assert.True(accessible.GetProperty("glyphsFit").GetBoolean(), $"Clipped label {selector}: {accessible}");
            Assert.True(accessible.GetProperty("touchSize").GetBoolean(), $"Small target {selector}: {accessible}");
            Assert.True(accessible.GetProperty("focused").GetBoolean());
            Assert.InRange(accessible.GetProperty("horizontalOverflow").GetDouble(), -1, 1);
        }
        await page.Locator("#gmaSkipAll").ScrollIntoViewIfNeededAsync();
        await page.Locator("#gmaSkipAll").TapAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('guess-mode')");
        Assert.Equal("true", await page.EvaluateAsync<string>("() => localStorage.getItem('gmaSkipAll')"));
        await Assertions.Expect(page.Locator("#closeAthleteDetailsModal")).ToBeFocusedAsync();
    }

    private const string CaptureGlideScript =
        """
        () => {
            const portrait = document.querySelector('#athlete-profile .portrait-wrapper');
            const r = portrait.getBoundingClientRect();
            window.__stageRect = [r.x, r.y, r.width, r.height];
            const original = Element.prototype.animate;
            Element.prototype.animate = function (...args) {
                const animation = original.apply(this, args);
                if (this === portrait && String(args[0][0]?.transform).includes('translate')) {
                    animation.pause();
                    animation.currentTime = 0;
                    window.__portraitGlide = animation;
                }
                return animation;
            };
        }
        """;

    [Theory]
    [InlineData(320, 640, 100, 44, 12)]
    [InlineData(320, 640, 100, 40, 1)]
    [InlineData(320, 640, 100, 40, 12)]
    [InlineData(320, 640, 200, 44, 12)]
    [InlineData(320, 640, 200, 40, 1)]
    [InlineData(320, 640, 200, 40, 12)]
    [InlineData(195, 422, 100, 44, 12)]
    [InlineData(195, 422, 100, 40, 1)]
    [InlineData(195, 422, 100, 40, 12)]
    [InlineData(1440, 900, 200, 40, 1)]
    public async Task PayoffBanners_KeepEveryGlyphInsideTheCard(int width, int height, int textPercent, int guess, int crowdCount)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US", ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/Guess/athlete-age**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200, ContentType = "application/json",
            Body = JsonSerializer.Serialize(new { crowdAge = 60, crowdCount, actualAge = 44, guessAccepted = true })
        }));
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await OpenRoundAsync(page);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await page.Locator("#gmaRange").EvaluateAsync("(range, guess) => { range.value = String(guess); range.dispatchEvent(new Event('input', { bubbles: true })); }", guess);
        var beforeHeight = await page.Locator("#guessAgeContainer").EvaluateAsync<double>("e => e.getBoundingClientRect().height");
        await page.Locator(".gma-btn--primary").ClickAsync();
        await page.Locator(".gma-pop-banner.show").WaitForAsync();
        await GuessMyAgeBrowserTests.PauseClockAsync(page);
        await page.Locator(".gma-pop-banner").ScrollIntoViewIfNeededAsync();
        var bounds = await page.Locator(".gma-pop-banner").EvaluateAsync<JsonElement>(
            """
            banner => {
                const r = banner.getBoundingClientRect();
                const card = document.getElementById('guessAgeContainer').getBoundingClientRect();
                const modal = document.querySelector('#detailsModal .modal-content');
                const walker = document.createTreeWalker(banner, NodeFilter.SHOW_TEXT);
                const glyphs = [];
                let node;
                while ((node = walker.nextNode())) {
                    if (!node.textContent.trim()) continue;
                    const range = document.createRange();
                    range.selectNode(node);
                    glyphs.push(...range.getClientRects());
                }
                return {
                    glyphsFit: glyphs.every(g => g.left >= r.left && g.right <= r.right
                        && g.top >= r.top && g.bottom <= r.bottom),
                    bannerFits: r.left >= card.left && r.right <= card.right,
                    horizontalOverflow: modal.scrollWidth - modal.clientWidth,
                    cardHeight: card.height,
                    portraitWidth: document.querySelector('#athlete-profile .portrait-wrapper').getBoundingClientRect().width
                };
            }
            """);
        Assert.True(bounds.GetProperty("glyphsFit").GetBoolean(), $"Payoff text or emojis escape their banner: {bounds}");
        Assert.True(bounds.GetProperty("bannerFits").GetBoolean(), $"Payoff escapes its card: {bounds}");
        Assert.InRange(bounds.GetProperty("horizontalOverflow").GetDouble(), -1, 1);
        Assert.True(bounds.GetProperty("portraitWidth").GetDouble() >= 86);
        if ((textPercent == 100 && width >= 320) || width >= 769)
            Assert.InRange(bounds.GetProperty("cardHeight").GetDouble() - beforeHeight, -1, 1);
    }

    [Theory]
    [InlineData(320, 640, 200, true)]
    [InlineData(568, 240, 200, true)]
    [InlineData(195, 422, 100, true)]
    [InlineData(320, 640, 200, false)]
    [InlineData(568, 240, 200, false)]
    [InlineData(195, 422, 100, false)]
    public async Task ScrolledResult_BringsTheFocusedCloseControlIntoView(int width, int height, int textPercent, bool reducedMotion)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US",
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/Guess/athlete-age**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200, ContentType = "application/json",
            Body = """{"crowdAge":60,"crowdCount":12,"actualAge":44,"guessAccepted":true}"""
        }));
        var page = await context.NewPageAsync();
        await OpenRoundAsync(page);
        await page.EvaluateAsync("percent => { document.documentElement.style.fontSize = percent + '%'; document.getElementById('gmaSkipAll').style.display = 'inline-block'; }", textPercent);
        await page.Locator(".gma-btn--primary").ScrollIntoViewIfNeededAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => document.querySelector('#detailsModal .modal-content').scrollTop > 0"));
        await page.Locator(".gma-btn--primary").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#gmaRealBubble.is-settled')");
        await Assertions.Expect(page.Locator("#closeAthleteDetailsModal")).ToBeFocusedAsync();
        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            """
            close => {
                const r = close.getBoundingClientRect();
                const m = document.querySelector('#detailsModal .modal-content').getBoundingClientRect();
                return r.top >= m.top && r.bottom <= Math.min(m.bottom, innerHeight)
                    && r.left >= m.left && r.right <= m.right;
            }
            """));
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync("() => getComputedStyle(document.getElementById('detailsModal')).display === 'none'");
    }

    [Theory]
    [InlineData(320, 640, 200, false)]
    [InlineData(320, 640, 200, true)]
    [InlineData(640, 360, 200, false)]
    [InlineData(640, 360, 200, true)]
    [InlineData(568, 240, 200, false)]
    [InlineData(568, 240, 200, true)]
    [InlineData(195, 422, 100, false)]
    [InlineData(195, 422, 100, true)]
    public async Task ScrolledSkip_LeavesCloseVisibleAfterTheGlide(int width, int height, int textPercent, bool skipAll)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US", ReducedMotion = ReducedMotion.NoPreference,
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await OpenRoundAsync(page);
        await page.EvaluateAsync("percent => { document.documentElement.style.fontSize = percent + '%'; document.getElementById('gmaSkipAll').style.display = 'inline-block'; }", textPercent);
        var control = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = skipAll ? "Skip all" : "Skip", Exact = true });
        await control.ScrollIntoViewIfNeededAsync();
        await control.ClickAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('guess-mode')");
        await page.EvaluateAsync(
            """
            async () => {
                const close = document.getElementById('closeAthleteDetailsModal');
                close.getBoundingClientRect();
                await Promise.all([
                    ...close.getAnimations(),
                    ...document.querySelector('#athlete-profile .portrait-wrapper').getAnimations()
                ].map(animation => animation.finished.catch(() => {})));
                await new Promise(requestAnimationFrame);
                await new Promise(requestAnimationFrame);
            }
            """);
        await Assertions.Expect(page.Locator("#closeAthleteDetailsModal")).ToBeFocusedAsync();
        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            """
            close => {
                const r = close.getBoundingClientRect();
                const m = document.querySelector('#detailsModal .modal-content').getBoundingClientRect();
                return r.top >= m.top && r.bottom <= Math.min(m.bottom, innerHeight)
                    && r.left >= m.left && r.right <= m.right;
            }
            """));
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync("() => getComputedStyle(document.getElementById('detailsModal')).display === 'none'");
    }

    [Theory]
    [InlineData(43, true)]
    [InlineData(100, false)]
    public async Task ResultBubbles_KeepTheAnswerReadableAndResetOnTheNextAthlete(int guess, bool overlap)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US", ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/Guess/athlete-age**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200, ContentType = "application/json",
            Body = """{"crowdAge":60,"crowdCount":12,"actualAge":44,"guessAccepted":true}"""
        }));
        var page = await context.NewPageAsync();
        await OpenRoundAsync(page);
        await page.Locator("#gmaRange").EvaluateAsync("(range, guess) => { range.value = String(guess); range.dispatchEvent(new Event('input', { bubbles: true })); }", guess);
        await page.Locator(".gma-btn--primary").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#gmaRealBubble.is-settled')");
        await Assertions.Expect(page.Locator("#gmaBubble")).ToHaveCSSAsync("opacity", overlap ? "0" : "0.4");
        await Assertions.Expect(page.Locator("#gmaRealBubble")).ToHaveTextAsync("44");
        Assert.Contains($"Your guess: {guess}.", await page.Locator("#gmaStatus").TextContentAsync());
        await page.EvaluateAsync("() => window.openAthleteModalBySlug('aaron')");
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content.guess-mode:not(.is-loading)')?.dataset.athleteSlug === 'aaron'");
        await Assertions.Expect(page.Locator("#gmaBubble")).ToHaveCSSAsync("opacity", "1");
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    [InlineData(640, 360, true)]
    public async Task PortraitGlide_StartsAtTheStageAndEndsAtTheCurrentProfileLayout(int width, int height, bool fast)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US",
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        if (fast) await context.AddInitScriptAsync("sessionStorage.setItem('gmaSeen', 'true')");
        var page = await context.NewPageAsync();
        await OpenRoundAsync(page);
        await page.EvaluateAsync(CaptureGlideScript);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Skip", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => window.__portraitGlide");
        var startError = await page.EvaluateAsync<double[]>(
            """
            () => {
                const r = document.querySelector('#athlete-profile .portrait-wrapper').getBoundingClientRect();
                return [r.x, r.y, r.width, r.height].map((value, i) => value - window.__stageRect[i]);
            }
            """);
        Assert.All(startError, error => Assert.InRange(error, -1, 1));
        Assert.Equal(fast ? 320 : 460,
            await page.EvaluateAsync<double>("() => window.__portraitGlide.effect.getTiming().duration"));
        var endError = await page.EvaluateAsync<double[]>(
            """
            async () => {
                const portrait = document.querySelector('#athlete-profile .portrait-wrapper');
                window.__portraitGlide.finish();
                const r = portrait.getBoundingClientRect();
                const end = [r.x, r.y, r.width, r.height];
                await new Promise(requestAnimationFrame);
                await new Promise(requestAnimationFrame);
                const settled = portrait.getBoundingClientRect();
                return [settled.x, settled.y, settled.width, settled.height].map((value, i) => value - end[i]);
            }
            """);
        Assert.All(endError, error => Assert.InRange(error, -1, 1));
        Assert.Equal("", await page.Locator("#athlete-profile .portrait-wrapper").GetAttributeAsync("style") ?? "");
        await Assertions.Expect(page.Locator("#closeAthleteDetailsModal")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("same")]
    [InlineData("different")]
    [InlineData("close")]
    [InlineData("reduced-motion")]
    public async Task PortraitGlide_CancelsOnTeardownAndPreferenceChanges(string action)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await OpenRoundAsync(page);
        await page.EvaluateAsync(CaptureGlideScript);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Skip", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => window.__portraitGlide");
        if (action == "close") await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        else if (action == "reduced-motion") await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        else await page.EvaluateAsync("slug => window.openAthleteModalBySlug(slug)", action == "same" ? "patrick-ruff" : "aaron");
        await page.WaitForFunctionAsync("() => window.__portraitGlide.playState === 'idle'");
        Assert.True(await page.Locator("#athlete-profile .portrait-wrapper").EvaluateAsync<bool>(
            "element => !element.getAnimations().includes(window.__portraitGlide) && element.style.transform === ''"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldImageConflict_DoesNotReopenThePreviousAthlete(bool refreshAlreadyStarted)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US", ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var request = new TaskCompletionSource<IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/Guess/athlete-age**", route =>
        {
            if (refreshAlreadyStarted) return route.FulfillAsync(new RouteFulfillOptions { Status = 409, Body = "{}" });
            request.TrySetResult(route);
            return Task.CompletedTask;
        });
        if (refreshAlreadyStarted)
            await context.RouteAsync("**/api/data/athletes?profileImageRefresh=*", route => { request.TrySetResult(route); return Task.CompletedTask; });
        var page = await context.NewPageAsync();
        await OpenRoundAsync(page);
        await page.EvaluateAsync(
            """
            () => {
                const refresh = window.refreshAthleteAfterStaleGuess;
                window.__staleRefreshCalled = false;
                window.refreshAthleteAfterStaleGuess = (...args) => {
                    window.__staleRefreshCalled = true;
                    return refresh(...args);
                };
            }
            """);
        await page.Locator(".gma-btn--primary").ClickAsync();
        var pending = await request.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await page.EvaluateAsync("() => window.openAthleteModalBySlug('aaron')");
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content.guess-mode:not(.is-loading)')?.dataset.athleteSlug === 'aaron'");
        var response = page.WaitForResponseAsync(r => r.Url == pending.Request.Url);
        if (refreshAlreadyStarted) await pending.FulfillAsync(new RouteFulfillOptions { Response = await pending.FetchAsync() });
        else await pending.FulfillAsync(new RouteFulfillOptions { Status = 409, ContentType = "application/json", Body = "{}" });
        await response;
        await page.EvaluateAsync("async () => { await new Promise(requestAnimationFrame); await new Promise(requestAnimationFrame); }");
        Assert.Equal(refreshAlreadyStarted, await page.EvaluateAsync<bool>("() => window.__staleRefreshCalled"));
        Assert.Equal("aaron", await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-athlete-slug"));
        await Assertions.Expect(page.Locator("#gmaRange")).ToBeEnabledAsync();
    }
}
