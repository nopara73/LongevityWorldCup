using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

/// <summary>
/// The /play flow moves like one game: screens slide in the direction of travel,
/// the screen being left fades out as an inert ghost, the player card is dealt
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
    private const string WaitForSettledMotion = """
        async () => {
            await Promise.all(document.getAnimations()
                .filter(animation => animation.effect?.getTiming().iterations !== Infinity)
                .map(animation => animation.finished.catch(() => {})));
            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
        }
        """;

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
        await page.EvaluateAsync(WaitForSettledMotion);
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
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();
        await page.EvaluateAsync(WaitForSettledMotion);
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
    public async Task Moves_SlideInTheirDirectionAndTheLeftScreenIsAnInertGhost(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await OpenAsync(context, "/select-athlete");
        await SelectAthleteAsync(page, await PickAmateurAsync(page));

        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => document.documentElement.dataset.playMove === 'forward'");
        var forward = await page.EvaluateAsync<double[]>("""
            () => {
                const panel = document.getElementById('athleteDashboardPanel');
                const ghost = document.querySelector('.play-hub-panel--ghost');
                const starts = document.getAnimations()
                    .filter(animation => animation.effect?.target === panel || animation.effect?.target === panel.querySelector('.play-dashboard-card'))
                    .map(animation => animation.effect.getKeyframes()[0].transform || '');
                const shift = starts.map(value => /translate(?:X)?\((-?[\d.]+)px/.exec(value)).filter(Boolean).map(match => Number(match[1]));
                return [
                    ghost ? 1 : 0,
                    ghost && ghost.inert && ghost.getAttribute('aria-hidden') === 'true' ? 1 : 0,
                    ghost ? ghost.querySelectorAll('[id], [role], [name]').length : -1,
                    ghost ? (getComputedStyle(ghost).pointerEvents === 'none' ? 1 : 0) : 0,
                    shift.length ? Math.max(...shift) : 0,
                    panel.hidden ? 0 : 1
                ];
            }
            """);
        Assert.True(forward[0] == 1, "Leaving athlete selection should leave a ghost of it fading out.");
        Assert.True(forward[1] == 1 && forward[3] == 1, "The ghost must be inert, hidden from assistive technology, and never take pointer input.");
        Assert.Equal(0, forward[2]);
        Assert.True(forward[4] > 10, $"Going deeper should arrive from the right, got a {forward[4]}px start.");
        Assert.True(forward[5] == 1, "The dashboard must be live from the move's first frame.");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
        await WaitForMoveToSettleAsync(page);

        await page.Locator("#playDashboardBackBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => document.documentElement.dataset.playMove === 'back'");
        var backShift = await page.EvaluateAsync<double>("""
            () => {
                const panel = document.getElementById('athleteSelectionPanel');
                const start = document.getAnimations().find(animation => animation.effect?.target === panel)?.effect.getKeyframes()[0].transform || '';
                const match = /translateX\((-?[\d.]+)px/.exec(start);
                return match ? Number(match[1]) : 0;
            }
            """);
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
        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.play-hub-panel--ghost')");

        var hit = await page.EvaluateAsync<bool>("""
            () => {
                const back = document.getElementById('playDashboardBackBtn');
                const box = back.getBoundingClientRect();
                const target = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
                return Boolean(target && back.contains(target));
            }
            """);
        Assert.True(hit, "Mid-move, a press on the arriving screen's control must reach that control, not the ghost.");
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

        await page.GoBackAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/select-athlete' && !document.getElementById('athleteSelectionPanel').hidden");
        await page.WaitForFunctionAsync("expected => Math.abs(window.scrollY - expected) <= 2", before);
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
        await page.EvaluateAsync(WaitForSettledMotion);
        await page.EvaluateAsync("() => { window.__rows = [...document.querySelectorAll('#playAthleteInput-autocomplete-list [role=option]')]; }");

        await input.PressAsync("n");
        var change = await page.EvaluateAsync<int[]>("""
            () => {
                const list = document.getElementById('playAthleteInput-autocomplete-list');
                const options = [...list.querySelectorAll(':scope > [role=option]')];
                const kept = options.filter(option => window.__rows.includes(option)).length;
                const leaving = [...list.querySelectorAll('.autocomplete-item--leaving')];
                const cleanLeaving = leaving.every(item => !item.id && !item.hasAttribute('role') && item.getAttribute('aria-hidden') === 'true');
                const animated = options.filter(option => option.getAnimations().length > 0).length
                    + leaving.filter(item => item.getAnimations().length > 0).length;
                return [options.length, kept, leaving.length, cleanLeaving ? 1 : 0, animated,
                    options.every((option, index) => option.id === `playAthleteInput-autocomplete-list-option-${index}`) ? 1 : 0];
            }
            """);
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
    [InlineData(1280, 800, true)]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, false)]
    public async Task Dashboard_CardIsDealtAndThePriceSettlesWithoutChangingItsText(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await OpenAsync(context, "/select-athlete");
        await SelectAthleteAsync(page, await PickAmateurAsync(page));
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

        await page.WaitForFunctionAsync("() => !document.querySelector('.play-price-settling')");
        Assert.Equal(finalText, (await page.Locator("#athleteDashboardActions .pro-new-price").TextContentAsync())?.Trim());
        await WaitForMoveToSettleAsync(page);
        await page.EvaluateAsync(WaitForSettledMotion);

        if (width >= 1280)
        {
            // The deal must not trick the dock into treating the card's commands as off-screen.
            Assert.False(await page.EvaluateAsync<bool>("() => document.getElementById('athleteDashboardActions').classList.contains('flow-action-stack--docked')"),
                $"{width}x{height}: the dashboard's commands should stay in the card after it lands.");
        }
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(390, 844, true)]
    public async Task Dock_SlidesUpFromTheScreenEdge(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            IsMobile = true,
            HasTouch = true
        });
        var page = await OpenAsync(context, "/select-athlete");
        var slide = await page.EvaluateAsync<double[]>("""
            () => {
                const stack = document.querySelector('.play-athlete-actions.flow-action-stack--docked');
                stack.classList.add('flow-action-stack--dock-entering');
                const shifted = new DOMMatrix(getComputedStyle(stack).transform).m42;
                const height = stack.getBoundingClientRect().height;
                const transition = getComputedStyle(stack).transitionDuration;
                stack.classList.remove('flow-action-stack--dock-entering');
                return [shifted, height, Math.max(...transition.split(',').map(value => parseFloat(value) * (value.includes('ms') ? 1 : 1000)))];
            }
            """);
        Assert.True(slide[0] >= slide[1], $"An entering dock should start fully below the screen edge: {slide[0]}px for a {slide[1]}px bar.");
        Assert.InRange(slide[2], 200, 300);
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
        Assert.True(double.TryParse(pressed, System.Globalization.CultureInfo.InvariantCulture, out var scale) && scale < 0.99,
            $"A pressed command should give way, got scale '{pressed}'.");
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
