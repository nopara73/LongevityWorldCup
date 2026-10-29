using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

/// <summary>
/// The /play flow moves like one game: screens slide in the direction of travel,
/// one complete live screen is painted at a time, the player card is dealt
/// in, search rows enter and leave, prices settle, the dock slides up, and focus
/// and scroll land where the visitor expects. Reduced motion keeps the landing
/// without any of the movement.
/// </summary>
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayFlowMotionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static Task WaitForSettledMotionAsync(IPage page) => WaitForFiniteAnimationsAsync(page);

    private async Task<IPage> OpenAsync(IBrowserContext context, string path)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("""
            () => !document.documentElement.hasAttribute('data-initial-view')
                && !document.body.classList.contains('play-route-hydrating')
                && document.querySelector('.play-hub-panel:not([hidden])')
                && [...document.querySelectorAll('.flow-action')].some(button => !button.disabled && button.getClientRects().length > 0)
            """);
        await page.EvaluateAsync("() => document.fonts.ready");
        await WaitForSettledMotionAsync(page);
        return page;
    }

    private static async Task<string> PickAmateurAsync(IPage page)
    {
        var name = await page.EvaluateAsync<string>("""
            async () => {
                await window.modulesReady;
                const athletes = await fetch('/api/data/athletes').then(response => response.json());
                const names = athletes.map(athlete => athlete.DisplayName || athlete.Name);
                return athletes.find(athlete => !window.isAthletePro(athlete)
                    && /^[A-Za-z][A-Za-z ]{2,}$/.test(athlete.DisplayName || athlete.Name)
                    && names.filter(other => other.toLowerCase() === (athlete.DisplayName || athlete.Name).toLowerCase()).length === 1)
                    ?.DisplayName ?? athletes.find(athlete => !window.isAthletePro(athlete))?.Name;
            }
            """);
        Assert.False(string.IsNullOrWhiteSpace(name), "The test data should contain an amateur athlete.");
        return name;
    }

    private static async Task SelectAthleteAsync(IPage page, string name)
    {
        var input = page.Locator("#playAthleteInput");
        await input.ClickAsync();
        await input.FillAsync(name);
        await Assertions.Expect(page.Locator("#playAthleteInput-autocomplete-list [role=option]").First).ToBeVisibleAsync();
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();
        await WaitForSettledMotionAsync(page);
    }

    private static Task<bool> IsTransitioningAsync(IPage page) =>
        page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('play-panel-transitioning')");

    private static Task WaitForMoveToSettleAsync(IPage page) =>
        page.WaitForFunctionAsync("() => !document.documentElement.classList.contains('play-panel-transitioning') && !document.querySelector('.play-hub-panel--ghost')");

    [Theory]
    [InlineData(1280, 800, false)]
    [InlineData(820, 1180, true)]
    [InlineData(390, 844, false)]
    [InlineData(390, 844, true)]
    public async Task Moves_SlideInTheirDirectionWithOneCompleteLiveScreen(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await OpenAsync(context, "/select-athlete");
        await SelectAthleteAsync(page, await PickAmateurAsync(page));
        // Exercise a prepared transition; the slow loading path has separate
        // coverage below. The departing identity must not cover the new screen.
        await page.EvaluateAsync("""
            async () => {
                const flow = window.playAthleteFlow;
                const athlete = flow.getStoredSelectedAthlete();
                await Promise.all([
                    flow.renderAthleteDashboardHeader(athlete, {
                        titleElement: document.getElementById('athleteDashboardTitle'),
                        frameElement: document.getElementById('athleteDashboardPicture')
                    }),
                    flow.renderDashboardActions(athlete, {
                        dynamicActionsElement: document.getElementById('athleteDashboardDynamicActions'),
                        discountElement: document.getElementById('athleteDashboardDiscounts')
                    })
                ]);
                flow.renderAthleteDashboardHeader = () => Promise.resolve();
                flow.renderDashboardActions = () => Promise.resolve();
            }
            """);
        // Measure in the frame that creates the move. A protocol round trip can
        // otherwise outlive a 160ms exit on a busy runner.
        await page.EvaluateAsync("""
            () => {
                window.__moveFrames = {};
                new MutationObserver(() => {
                const direction = document.documentElement.dataset.playMove;
                if (!direction || window.__moveFrames[direction]) return;
                const panel = document.getElementById('athleteDashboardPanel');
                const starts = document.getAnimations()
                    .filter(animation => animation.effect?.target === panel || animation.effect?.target === panel.querySelector('.play-dashboard-card'))
                    .map(animation => animation.effect.getKeyframes()[0].transform || '');
                const shift = starts.map(value => /translate(?:X)?\((-?[\d.]+)px/.exec(value)).filter(Boolean).map(match => Number(match[1]));
                if (direction === 'forward') window.__moveFrames.forward = [
                    [...document.querySelectorAll('.play-hub-main h1')].filter(title => title.getClientRects().length).length,
                    document.getElementById('athleteSelectionPanel').hidden ? 1 : 0,
                    document.querySelectorAll('.play-hub-panel:not([hidden])').length,
                    panel.inert ? 1 : 0,
                    shift.length ? Math.max(...shift) : 0,
                    panel.hidden ? 0 : 1,
                    Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) - document.documentElement.clientWidth
                ];
                if (direction === 'back') {
                    const selection = document.getElementById('athleteSelectionPanel');
                    const start = document.getAnimations().find(animation => animation.effect?.target === selection)?.effect.getKeyframes()[0].transform || '';
                    const match = /translateX\((-?[\d.]+)px/.exec(start);
                    window.__moveFrames.back = match ? Number(match[1]) : 0;
                }
                }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-play-move'] });
            }
            """);
        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__moveFrames.forward");
        var forward = await page.EvaluateAsync<double[]>("() => window.__moveFrames.forward");
        Assert.Equal(1, forward[0]);
        Assert.True(forward[1] == 1 && forward[3] == 0, "The departing screen must be hidden while the arriving screen takes input.");
        Assert.Equal(1, forward[2]);
        Assert.True(forward[4] > 10, $"Going deeper should arrive from the right, got a {forward[4]}px start.");
        Assert.True(forward[5] == 1, "The dashboard must be live from the move's first frame.");
        Assert.True(forward[6] <= 1, $"The first motion frame has {forward[6]}px horizontal overflow.");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
        await WaitForMoveToSettleAsync(page);

        await page.Locator("#playDashboardBackBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => Object.hasOwn(window.__moveFrames, 'back')");
        var backShift = await page.EvaluateAsync<double>("() => window.__moveFrames.back");
        Assert.True(backShift < -10, $"Going back should arrive from the left, got a {backShift}px start.");
        await WaitForMoveToSettleAsync(page);
    }

    [Fact]
    public async Task Moves_NeverBlockInputOnTheArrivingScreen()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 }
        });
        var page = await OpenAsync(context, "/select-athlete");
        await SelectAthleteAsync(page, await PickAmateurAsync(page));
        await page.EvaluateAsync("""
            () => {
                new MutationObserver(() => {
                    if (document.documentElement.dataset.playMove !== 'forward') return;
                    const back = document.getElementById('playDashboardBackBtn');
                    const box = back.getBoundingClientRect();
                    const target = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
                    window.__incomingCommandHit = Boolean(target && back.contains(target));
                }).observe(document.documentElement, {attributes:true, attributeFilter:['data-play-move']});
            }
            """);
        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__incomingCommandHit !== undefined");
        Assert.True(await page.EvaluateAsync<bool>("() => window.__incomingCommandHit"),
            "From the arriving frame, a press on the new screen's control must reach that control.");
        await page.Locator("#playDashboardBackBtn").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/select-athlete$"));
        await WaitForMoveToSettleAsync(page);
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task Focus_LandsOnTheNewScreenAndReturnsToTheControlThatLeft(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }
        });
        var page = await OpenAsync(context, "/play");
        var name = await PickAmateurAsync(page);
        await Task.Delay(2000);

        var continueButton = page.Locator("#continueGameBtn");
        await continueButton.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#playAthleteInput")).ToBeFocusedAsync();
        await WaitForMoveToSettleAsync(page);

        await page.Keyboard.TypeAsync(name);
        await Assertions.Expect(page.Locator("#playAthleteInput-autocomplete-list [role=option]").First).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => location.pathname === '/dashboard'");
        await page.WaitForFunctionAsync("""
            () => {
                const active = document.activeElement;
                return active?.matches('#athleteDashboardActions .flow-action:not(.back-button)')
                    && active.textContent.trim().length > 0;
            }
            """);
        await WaitForMoveToSettleAsync(page);

        await page.Locator("#playDashboardBackBtn").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeFocusedAsync();
        await WaitForMoveToSettleAsync(page);

        await page.GoBackAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/play'");
        await Assertions.Expect(continueButton).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Scroll_ComingBackLandsWhereTheVisitorLeft()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 520 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenAsync(context, "/select-athlete");
        await SelectAthleteAsync(page, await PickAmateurAsync(page));
        await page.EvaluateAsync("() => window.scrollTo(0, 120)");
        var before = await page.EvaluateAsync<double>("() => window.scrollY");
        Assert.True(before > 40, "The selection screen should scroll at this height.");

        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/dashboard' && !document.getElementById('athleteDashboardPanel').hidden");
        Assert.True(await page.EvaluateAsync<double>("() => window.scrollY") < 1, "A new screen opens at its top.");
        await page.EvaluateAsync("() => window.scrollTo(0, 150)");
        await page.WaitForFunctionAsync("() => window.scrollY > 40");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var dashboardScroll = await page.EvaluateAsync<double>("() => window.scrollY");

        await page.GoBackAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/select-athlete' && !document.getElementById('athleteSelectionPanel').hidden");
        await page.WaitForFunctionAsync("expected => Math.abs(window.scrollY - expected) <= 2", before);
        await page.GoForwardAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/dashboard' && !document.getElementById('athleteDashboardPanel').hidden");
        await page.WaitForFunctionAsync("expected => Math.abs(window.scrollY - expected) <= 2", dashboardScroll);
    }

    [Fact]
    public async Task EnlargedText_LandsFocusOnAVisibleInlineCommand()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenAsync(context, "/select-athlete");
        await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        await SelectAthleteAsync(page, await PickAmateurAsync(page));
        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/dashboard' && !document.getElementById('athleteDashboardPanel').hidden");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const active = document.activeElement;
                const box = active.getBoundingClientRect();
                return active.matches('#athleteDashboardActions .flow-action:not(.back-button)')
                    && box.top >= 0 && box.bottom <= innerHeight + 1;
            }
            """), "Landing focus must reveal the command when enlarged text keeps the actions inline.");
    }

    [Fact]
    public async Task PendingNavigation_CancelsOldMotionAndCannotReopenAfterBack()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await OpenAsync(context, "/select-athlete");
        var name = await PickAmateurAsync(page);
        // Prime the controller's own request before deliberately freezing motion.
        // The fixture-name lookup does not populate its autocomplete data.
        var input = page.Locator("#playAthleteInput");
        await input.FillAsync(name);
        await Assertions.Expect(page.Locator("#playAthleteInput-autocomplete-list [role=option]").First).ToBeVisibleAsync();
        await input.FillAsync(string.Empty);
        await page.Locator("#playSelectionBackBtn").ClickAsync();
        await page.WaitForURLAsync("**/play");
        await WaitForMoveToSettleAsync(page);
        await page.EvaluateAsync("""
            () => {
                const render = window.playAthleteFlow.renderAthleteDashboardHeader;
                window.playAthleteFlow.renderAthleteDashboardHeader = (...args) => {
                    const header = render(...args);
                    return new Promise(resolve => window.__releaseHeader = () => resolve(header));
                };
                document.getElementById('continueGameBtn').click();
            }
            """);
        await page.WaitForFunctionAsync("() => document.documentElement.classList.contains('play-panel-transitioning')");
        await page.EvaluateAsync("""
            name => {
                const animations = document.getElementById('athleteSelectionPanel').getAnimations();
                if (!animations.length) throw new Error('The previous panel must still be moving.');
                animations.forEach(animation => animation.pause());
                const input = document.getElementById('playAthleteInput');
                input.focus({ preventScroll: true });
                input.value = name;
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
                const confirm = document.getElementById('playConfirmAthleteBtn');
                if (confirm.disabled) throw new Error('The loaded athlete must be selected.');
                confirm.click();
            }
            """, name);
        await page.WaitForFunctionAsync("() => window.__releaseHeader && location.pathname === '/dashboard'");
        await Task.Delay(700);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => document.body.classList.contains('play-route-hydrating')
                && document.body.getAttribute('aria-busy') === 'true'
                && !document.documentElement.hasAttribute('data-flow-dock-hold')
                && !document.documentElement.classList.contains('play-panel-transitioning')
                && !document.querySelector('.play-hub-panel--ghost')
            """), "Finishing or cancelling the previous move must not complete a still-pending navigation.");
        await page.GoBackAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/select-athlete' && !document.body.classList.contains('play-route-hydrating')");
        await page.EvaluateAsync("() => window.__releaseHeader()");
        await WaitForSettledMotionAsync(page);
        Assert.True(await page.Locator("#athleteDashboardPanel").IsHiddenAsync());
        Assert.True(await page.Locator("#athleteSelectionPanel").IsVisibleAsync());
        Assert.Null(await page.Locator("body").GetAttributeAsync("aria-busy"));
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.hasAttribute('data-flow-dock-hold')"));
    }

    [Theory]
    [InlineData(1280, 800, false)]
    [InlineData(390, 844, true)]
    public async Task Search_RowsStayAcrossKeystrokesEnterAndLeave(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await OpenAsync(context, "/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await input.ClickAsync();
        await input.PressSequentiallyAsync("a");
        await Assertions.Expect(page.Locator(".autocomplete-items [role=option]").First).ToBeVisibleAsync();
        await WaitForSettledMotionAsync(page);
        // Capture the list change in its own frame. A protocol round trip on a
        // busy runner can outlive the short leaving animation.
        await page.EvaluateAsync("""
            () => {
                const list = document.getElementById('playAthleteInput-autocomplete-list');
                const rows = [...list.querySelectorAll(':scope > [role=option]')];
                const observer = new MutationObserver(() => {
                    const options = [...list.querySelectorAll(':scope > [role=option]')];
                    const kept = options.filter(option => rows.includes(option)).length;
                    const leaving = [...list.querySelectorAll('.autocomplete-item--leaving')];
                    const cleanLeaving = leaving.every(item => !item.id && !item.hasAttribute('role') && item.getAttribute('aria-hidden') === 'true');
                    const animated = options.filter(option => option.getAnimations().length > 0).length
                        + leaving.filter(item => item.getAnimations().length > 0).length;
                    window.__rowChange = [options.length, kept, leaving.length, cleanLeaving ? 1 : 0, animated,
                        options.every((option, index) => option.id === `playAthleteInput-autocomplete-list-option-${index}`) ? 1 : 0];
                    observer.disconnect();
                });
                observer.observe(list, { childList: true });
            }
            """);
        await input.PressAsync("n");
        await page.WaitForFunctionAsync("() => window.__rowChange");
        var change = await page.EvaluateAsync<int[]>("() => window.__rowChange");
        Assert.True(change[0] > 0, "Typing a letter should still list matches.");
        Assert.True(change[1] > 0, "Rows that still match should stay the same rows, not be redrawn.");
        Assert.True(change[2] > 0 && change[3] == 1, "Rows that stop matching should fade out, already gone from the listbox.");
        Assert.True(change[4] > 0, "Entering, leaving or moving rows should animate.");
        Assert.True(change[5] == 1, "Option ids must follow the visible order.");

        await input.PressAsync("ArrowDown");
        var activeId = await input.GetAttributeAsync("aria-activedescendant");
        Assert.Equal("playAthleteInput-autocomplete-list-option-0", activeId);

        await input.PressAsync("Escape");
        Assert.Equal(0, await page.Locator("#playAthleteInput-autocomplete-list").CountAsync());
        Assert.Equal("false", await input.GetAttributeAsync("aria-expanded"));
        await page.WaitForFunctionAsync("() => !document.querySelector('.autocomplete-items')");
    }

    [Theory]
    [InlineData(1280, 900, false, false)]
    [InlineData(1280, 900, true, true)]
    [InlineData(390, 844, true, false)]
    [InlineData(390, 844, false, true)]
    public async Task Search_ShrinkingResultsNeverScrollForDepartingOrMovingRows(int width, int height, bool dark, bool enlargedText)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await OpenAsync(context, "/select-athlete");
        if (enlargedText) await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        var input = page.Locator("#playAthleteInput");
        // Cover both a new single match and the last existing row moving into first place.
        foreach (var retainLastRow in new[] { false, true })
        {
            await input.FillAsync("a");
            await Assertions.Expect(page.Locator("#playAthleteInput-autocomplete-list [role=option]")).ToHaveCountAsync(5);
            await WaitForSettledMotionAsync(page);
            var frames = await page.EvaluateAsync<int[][]>("""
                async retainLastRow => {
                    const input = document.getElementById('playAthleteInput');
                    const list = document.getElementById('playAthleteInput-autocomplete-list');
                    const rows = [...list.querySelectorAll('[role=option]')];
                    input.value = retainLastRow ? rows.at(-1).dataset.value : 'Ron Lugbill';
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                    const frames = [];
                    const started = performance.now();
                    do {
                        frames.push([list.querySelectorAll('[role=option]').length,
                            list.scrollHeight - list.clientHeight, list.scrollWidth - list.clientWidth]);
                        await new Promise(resolve => requestAnimationFrame(resolve));
                    } while (performance.now() - started < 350);
                    return frames;
                }
                """, retainLastRow);
            Assert.True(frames.Length > 1, "Inspect the initial frame and the moving popup, not only its settled state.");
            Assert.All(frames, frame =>
            {
                Assert.Equal(1, frame[0]);
                Assert.True(frame[1] <= 1 && frame[2] <= 1,
                    $"A single result must fit throughout motion; extra scroll area was {frame[1]}px tall and {frame[2]}px wide.");
            });
        }
    }

    [Fact]
    public async Task SlowPreparation_RevealsAndFocusesTheArrivingPanelBeforeMotionFinishes()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1440, Height = 900 } });
        var page = await OpenAsync(context, "/select-athlete");
        await SelectAthleteAsync(page, await PickAmateurAsync(page));
        await page.EvaluateAsync("""
            () => {
                const render = window.playAthleteFlow.renderAthleteDashboardHeader;
                window.playAthleteFlow.renderAthleteDashboardHeader = (...args) => {
                    const header = render(...args);
                    return new Promise(resolve => window.__releaseHeader = () => resolve(header));
                };
            }
            """);
        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => document.body.classList.contains('play-route-hydrating')");
        await page.EvaluateAsync("() => window.__releaseHeader()");
        await page.WaitForFunctionAsync("() => document.documentElement.dataset.playMove === 'forward'");
        Assert.Equal("visible", await page.Locator("#athleteDashboardPanel").EvaluateAsync<string>("panel => getComputedStyle(panel).visibility"));
        Assert.Null(await page.Locator("body").GetAttributeAsync("aria-busy"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement?.matches('#athleteDashboardActions .flow-action:not(.back-button)')"));
        await WaitForMoveToSettleAsync(page);
    }

    [Theory]
    [InlineData(1280, 800, true, false)]
    [InlineData(1440, 900, false, false)]
    [InlineData(390, 844, false, false)]
    [InlineData(1280, 800, true, true)]
    public async Task Dashboard_CardIsDealtAndThePriceSettlesWithoutChangingItsText(int width, int height, bool dark, bool personalLink)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        // Filesystem/API order does not select a fixed discount breakdown. A
        // second discount legitimately makes the short desktop card need its dock.
        const string athleteName = "Motion Test Athlete";
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "application/json",
            Body = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new
                {
                    Name = athleteName, DisplayName = athleteName,
                    DateOfBirth = new { Year = 1980, Month = 5, Day = 20 },
                    Biomarkers = Array.Empty<object>(),
                    PersonalLink = personalLink ? "https://example.test/motion-athlete" : ""
                }
            })
        }));
        var page = await OpenAsync(context, "/select-athlete");
        await SelectAthleteAsync(page, athleteName);
        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.play-price-settling')");

        var midMove = await page.EvaluateAsync<string[]>("""
            () => {
                const price = document.querySelector('.play-price-settling');
                const counting = price.getAnimations().some(animation => animation.effect.getKeyframes().some(frame => '--play-price' in frame));
                const card = window.matchMedia('(min-width: 761px) and (min-height: 600px)').matches
                    ? document.getElementById('athleteDashboardPanel')
                    : document.querySelector('.play-dashboard-card');
                const dealt = card.getAnimations().some(animation => /rotate/.test(animation.effect.getKeyframes()[0].transform || ''));
                const glint = card.getAnimations({ subtree: true }).some(animation => animation.effect.pseudoElement === '::after');
                return [price.textContent.trim(), String(counting), String(dealt), String(glint), price.previousElementSibling?.textContent.trim() || ''];
            }
            """);
        Assert.Matches(@"^\$\d+$", midMove[0]);
        Assert.Equal("True", midMove[1], ignoreCase: true);
        Assert.Equal("True", midMove[2], ignoreCase: true);
        Assert.Equal("True", midMove[3], ignoreCase: true);
        var finalText = midMove[0];
        var priceButton = page.Locator("#athleteDashboardActions .flow-action").Filter(new() { Has = page.Locator(".pro-new-price") });
        var accessiblePrice = await priceButton.AriaSnapshotAsync();

        await page.WaitForFunctionAsync("() => !document.querySelector('.play-price-settling')");
        Assert.Equal(finalText, (await page.Locator("#athleteDashboardActions .pro-new-price").TextContentAsync())?.Trim());
        await WaitForMoveToSettleAsync(page);
        await WaitForSettledMotionAsync(page);
        Assert.Equal(accessiblePrice, await priceButton.AriaSnapshotAsync());

        if (width >= 1280)
        {
            var docked = await page.EvaluateAsync<bool>("() => document.getElementById('athleteDashboardActions').classList.contains('flow-action-stack--docked')");
            if (personalLink)
                Assert.True(docked, "The additional discount should keep the taller card's commands in the viewport dock.");
            else
                // The deal must not trick the dock into treating the card's commands as off-screen.
                Assert.False(docked, $"{width}x{height}: the dashboard's commands should stay in the card after it lands.");
        }
        Assert.True(await page.Locator("#athleteDashboardActions .flow-action").EvaluateAllAsync<bool>("""
            actions => actions.every(action => {
                const rect = action.getBoundingClientRect();
                return rect.top >= 0 && rect.bottom <= innerHeight;
            })
            """), "Every dashboard command should remain fully inside the viewport after landing.");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(390, 844, true)]
    public async Task Dock_SettlesWithoutHidingItsCommandsBelowTheScreen(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await OpenAsync(context, "/select-athlete");
        await page.EvaluateAsync("""
            () => {
                const stack = document.querySelector('.play-athlete-actions');
                const observer = new MutationObserver(() => {
                    if (!stack.classList.contains('flow-action-stack--dock-entering')) return;
                    window.__dockEntry = [...stack.querySelectorAll('.flow-action')].filter(action => action.getClientRects().length).map(action => {
                        const box = action.getBoundingClientRect();
                        const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
                        return [box.top, box.bottom, innerHeight, hit && (hit === action || action.contains(hit)) ? 1 : 0,
                            action.matches(':disabled, [aria-disabled="true"]') ? 1 : 0];
                    });
                    observer.disconnect();
                });
                observer.observe(stack, { attributes: true, attributeFilter: ['class'] });
            }
            """);
        await page.SetViewportSizeAsync(width, height);
        await page.WaitForFunctionAsync("() => window.__dockEntry");
        var commands = await page.EvaluateAsync<double[][]>("() => window.__dockEntry");
        Assert.NotEmpty(commands);
        Assert.All(commands, command =>
        {
            Assert.True(command[0] >= 0 && command[1] <= command[2] + 1,
                $"A newly docked command is outside the viewport: {command[0]}–{command[1]} in {command[2]}px.");
            // Invalid actions keep their existing disabled hit-testing policy.
            if (command[4] == 0) Assert.Equal(1, command[3]);
        });
        await page.WaitForFunctionAsync("""
            () => {
                const stack = document.querySelector('.play-athlete-actions.flow-action-stack--docked');
                return stack && !stack.classList.contains('flow-action-stack--dock-entering')
                    && Math.abs(new DOMMatrix(getComputedStyle(stack).transform).m42) < 1;
            }
            """);
        var duration = await page.Locator(".play-athlete-actions").EvaluateAsync<double>("""
            stack => Math.max(...getComputedStyle(stack).transitionDuration.split(',').map(value => parseFloat(value) * (value.includes('ms') ? 1 : 1000)))
            """);
        Assert.InRange(duration, 200, 300);
    }

    [Fact]
    public async Task Search_CloseAndReopenHasUniqueIdsAndOnlyLiveKeyboardOptions()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await OpenAsync(context, "/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await input.FillAsync("a");
        await Assertions.Expect(page.Locator("[role=option]").First).ToBeVisibleAsync();
        var clean = await page.EvaluateAsync<bool>("""
            () => {
                const input = document.getElementById('playAthleteInput');
                input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
                input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
                const ids = [...document.querySelectorAll('[id]')].map(element => element.id);
                const leaving = document.querySelector('.autocomplete-items--leaving');
                const active = document.getElementById(input.getAttribute('aria-activedescendant'));
                return new Set(ids).size === ids.length && leaving?.inert
                    && !leaving.querySelector('[id], [role]')
                    && active?.parentElement?.id === 'playAthleteInput-autocomplete-list';
            }
            """);
        Assert.True(clean, "A fading list must relinquish its ids and keyboard options before a new one opens.");
        var expected = await page.Locator("#playAthleteInput-autocomplete-list [aria-selected=true]").GetAttributeAsync("data-value");
        await input.PressAsync("Enter");
        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        Assert.Equal(expected, await page.EvaluateAsync<string>("() => JSON.parse(sessionStorage.getItem('selectedAthlete')).Name"));
        await page.WaitForFunctionAsync("() => !document.querySelector('.autocomplete-items')");
    }

    [Theory]
    [InlineData("counter")]
    [InlineData("property")]
    [InlineData("waapi")]
    [InlineData("pseudo")]
    public async Task UnsupportedMotion_KeepsTheFinalPriceAndCardVisible(string unsupported)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1440, Height = 900 } });
        var page = await OpenAsync(context, "/select-athlete");
        await SelectAthleteAsync(page, await PickAmateurAsync(page));
        await page.EvaluateAsync("""
            unsupported => {
                if (unsupported === 'property') CSS.registerProperty = undefined;
                if (unsupported === 'counter') {
                    const supports = CSS.supports.bind(CSS);
                    CSS.supports = (...args) => args[0] === 'content' ? false : supports(...args);
                }
                if (unsupported === 'waapi') Element.prototype.animate = undefined;
                if (unsupported === 'pseudo') {
                    const animate = Element.prototype.animate;
                    Element.prototype.animate = function(frames, options) {
                        const { pseudoElement, ...rest } = options || {};
                        return animate.call(this, frames, rest);
                    };
                }
            }
            """, unsupported);
        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/dashboard' && !document.getElementById('athleteDashboardPanel').hidden");
        if (unsupported != "pseudo")
        {
            Assert.Equal(0, await page.Locator(".play-price-settling").CountAsync());
            Assert.NotEqual("rgba(0, 0, 0, 0)", await page.Locator("#athleteDashboardActions .pro-new-price").EvaluateAsync<string>("price => getComputedStyle(price).color"));
        }
        await WaitForMoveToSettleAsync(page);
        await WaitForSettledMotionAsync(page);
        Assert.Equal("1", await page.Locator("#athleteDashboardPanel").EvaluateAsync<string>("card => getComputedStyle(card).opacity"));
        Assert.Matches(@"^\$\d+$", (await page.Locator("#athleteDashboardActions .pro-new-price").TextContentAsync())!.Trim());
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.hasAttribute('data-flow-dock-hold')"));
    }

    [Fact]
    public async Task Press_CommandsGiveWayUnderTheFinger()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 }
        });
        var page = await OpenAsync(context, "/select-athlete");
        var back = page.Locator("#playSelectionBackBtn");
        var box = await back.BoundingBoxAsync();
        Assert.NotNull(box);
        await page.Mouse.MoveAsync(box!.X + box.Width / 2, box.Y + box.Height / 2);
        await Task.Delay(250);
        var hover = await back.EvaluateAsync<string>("button => getComputedStyle(button.querySelector('i')).translate");
        Assert.StartsWith("-3px", hover);
        await page.Mouse.DownAsync();
        await Task.Delay(250);
        var pressed = await back.EvaluateAsync<string>("button => getComputedStyle(button).scale");
        await page.Mouse.MoveAsync(5, 5);
        await page.Mouse.UpAsync();
        Assert.Equal("0.965", pressed);
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task ReducedMotion_KeepsTheLandingWithoutAnyMovement(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenAsync(context, "/select-athlete");
        var name = await PickAmateurAsync(page);
        var input = page.Locator("#playAthleteInput");
        await input.ClickAsync();
        await input.PressSequentiallyAsync("a");
        await Assertions.Expect(page.Locator(".autocomplete-items [role=option]").First).ToBeVisibleAsync();
        await input.PressAsync("n");
        Assert.Equal(0, await page.Locator(".autocomplete-item--leaving").CountAsync());
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.querySelector('.autocomplete-items').getAnimations({ subtree: true }).length"));

        await input.FillAsync(name);
        await input.PressAsync("Enter");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.querySelector('.play-athlete-card').getAnimations().length"));
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => location.pathname === '/dashboard' && !document.getElementById('athleteDashboardPanel').hidden");
        var state = await page.EvaluateAsync<int[]>("""
            () => [
                document.querySelectorAll('.play-hub-panel--ghost').length,
                document.documentElement.hasAttribute('data-play-move') ? 1 : 0,
                document.querySelectorAll('.play-price-settling').length,
                document.getElementById('athleteDashboardPanel').getAnimations({ subtree: true }).length,
                document.activeElement?.matches('#athleteDashboardActions .flow-action:not(.back-button)') ? 1 : 0
            ]
            """);
        Assert.Equal(new[] { 0, 0, 0, 0, 1 }, state);

        await page.Locator("#playDashboardBackBtn").ClickAsync();
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeFocusedAsync();
    }
}
