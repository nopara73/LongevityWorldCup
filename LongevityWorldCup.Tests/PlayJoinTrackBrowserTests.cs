using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayJoinTrackBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string ReturningAmateur = """
        const athlete = {
            Name: 'Browser Test Athlete',
            DisplayName: 'Browser Test Athlete',
            PersonalLink: 'https://example.test/browser-test-athlete',
            DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
            Biomarkers: []
        };
        window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
        window.localStorage.setItem('selectedAthleteName', athlete.Name);
        window.localStorage.setItem('hasApplication', 'true');
        window.localStorage.setItem('gmaHasPerfectGuess', '1');
        """;

    [Theory]
    [InlineData(1440, 900, false, false)]
    [InlineData(1440, 900, true, false)]
    [InlineData(1280, 720, false, true)]
    [InlineData(768, 1024, true, true)]
    public async Task DesktopTrackCards_LineUpTheirBiomarkersAndActions(int width, int height, bool openAmateurList, bool withDiscount)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        if (withDiscount) await context.AddInitScriptAsync(ReturningAmateur);
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinStartAmateurBtn')?.disabled && !document.getElementById('joinGoProButton')?.disabled");
        if (withDiscount)
            await Assertions.Expect(page.Locator("#joinProDiscountNote")).ToBeVisibleAsync();
        if (openAmateurList)
        {
            await page.Locator(".play-join-card:not(.play-join-card--pro) summary").ClickAsync();
            await Assertions.Expect(page.Locator(".play-join-card:not(.play-join-card--pro) details")).ToHaveAttributeAsync("open", "");
        }
        // Compare the resting layout: no hover lift and no entrance still running.
        await page.Mouse.MoveAsync(0, 0);
        await WaitForFiniteAnimationsAsync(page);

        var rows = await page.EvaluateAsync<double[]>(
            """
            () => {
                const [amateur, pro] = document.querySelectorAll('.play-join-card');
                const top = (card, selector) => card.querySelector(selector).getBoundingClientRect().top;
                return [
                    amateur.getBoundingClientRect().top, pro.getBoundingClientRect().top,
                    top(amateur, '.play-join-card-actions .flow-action'), top(pro, '.play-join-card-actions .flow-action'),
                    amateur.getBoundingClientRect().bottom, pro.getBoundingClientRect().bottom,
                    top(amateur, 'summary'), top(pro, 'summary'),
                    top(amateur, '.play-join-meta dt:nth-of-type(2)'), top(pro, '.play-join-meta dt:nth-of-type(2)'),
                    top(amateur, '.play-join-meta dt:nth-of-type(3)'), top(pro, '.play-join-meta dt:nth-of-type(3)')
                ];
            }
            """);
        var scenario = $"{width}x{height}, list {(openAmateurList ? "open" : "closed")}, discount {withDiscount}";
        var sideBySide = Math.Abs(rows[0] - rows[1]) <= 1;
        if (width >= 960) Assert.True(sideBySide, $"{scenario}: the track cards should sit side by side.");
        if (sideBySide)
        {
            Assert.True(Math.Abs(rows[2] - rows[3]) <= 1, $"{scenario}: the track actions do not line up ({rows[2]} vs {rows[3]}).");
            Assert.True(Math.Abs(rows[4] - rows[5]) <= 1, $"{scenario}: the track cards end at different heights ({rows[4]} vs {rows[5]}).");
            if (!openAmateurList)
                Assert.True(Math.Abs(rows[6] - rows[7]) <= 1, $"{scenario}: the biomarker lists do not line up ({rows[6]} vs {rows[7]}).");
            Assert.True(Math.Abs(rows[8] - rows[9]) <= 1, $"{scenario}: testing windows do not share a row.");
            Assert.True(Math.Abs(rows[10] - rows[11]) <= 1, $"{scenario}: aging clocks do not share a row.");
        }
        await ExpectNoHorizontalOverflowAsync(page);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1024, 768)]
    [InlineData(768, 1024)]
    [InlineData(601, 900)]
    [InlineData(844, 390)]
    [InlineData(600, 320)]
    [InlineData(390, 320)]
    [InlineData(390, 844)]
    [InlineData(360, 740)]
    [InlineData(320, 640)]
    public async Task EnlargedText_KeepsTrackNamesAndDetailsWhole(int width, int height)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(ReturningAmateur);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinMobileGoProButton')?.disabled && !document.getElementById('joinGoProButton')?.disabled");
        await Assertions.Expect(page.Locator("#joinProDiscountNote")).ToBeVisibleAsync();
        var normalActionFont = await page.Locator("#joinTrackBackBtn").EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).fontSize)");
        var normalRootFont = await page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.documentElement).fontSize)");
        await page.EvaluateAsync("() => { document.documentElement.style.setProperty('font-size', '200%', 'important'); }");
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow?.()");
        var enlargedActionFont = await page.Locator("#joinTrackBackBtn").EvaluateAsync<double>("element => parseFloat(getComputedStyle(element).fontSize)");
        var enlargedRootFont = await page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.documentElement).fontSize)");
        Assert.InRange(enlargedRootFont / normalRootFont, 1.99, 2.01);
        Assert.True(enlargedActionFont >= normalActionFont * 1.99, "Actions must retain enlarged text when moving between the dock and the page.");

        const string selector = ".play-join-card .track-name, .play-join-meta dt, .play-join-meta dd, .play-join-actions .flow-action__label";
        var splitWords = await page.EvaluateAsync<string[]>(
            """
            selector => {
                const split = [];
                for (const node of document.querySelectorAll(selector)) {
                    const bounds = node.getBoundingClientRect();
                    if (!bounds.width || !bounds.height) continue;
                    const walker = document.createTreeWalker(node, NodeFilter.SHOW_TEXT);
                    let text;
                    while ((text = walker.nextNode())) {
                        for (const match of text.textContent.matchAll(/\S+/g)) {
                            const range = document.createRange();
                            range.setStart(text, match.index);
                            range.setEnd(text, match.index + match[0].length);
                            const lines = new Set([...range.getClientRects()].map(rect => Math.round(rect.top))).size;
                            if (lines > 1) split.push(match[0]);
                            if ([...range.getClientRects()].some(rect => rect.left < bounds.left - 1 || rect.right > bounds.right + 1))
                                split.push(`${match[0]} (clipped)`);
                        }
                    }
                }
                return split;
            }
            """, selector);
        Assert.True(splitWords.Length == 0, $"{width}px at 200%: words split across lines: {string.Join(", ", splitWords)}");
        var actions = page.Locator(".play-join-actions");
        var docked = await actions.EvaluateAsync<bool>("element => element.classList.contains('flow-action-stack--docked')");
        if (docked)
        {
            var dockHeight = await actions.EvaluateAsync<double>("element => element.getBoundingClientRect().height");
            Assert.True(dockHeight <= Math.Min(280, height / 2d), $"The join actions occupy {dockHeight}px of the {height}px viewport.");
        }
        else
        {
            await Assertions.Expect(page.Locator("#joinMobileStartAmateurBtn > i")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#joinMobileGoProButton > i")).ToBeHiddenAsync();
            foreach (var id in new[] { "joinTrackBackBtn", "joinMobileStartAmateurBtn", "joinMobileGoProButton" })
            {
                var action = page.Locator($"#{id}");
                await action.ScrollIntoViewIfNeededAsync();
                Assert.True(await action.EvaluateAsync<bool>("element => { const r = element.getBoundingClientRect(); const x = r.left + r.width / 2, y = r.top + r.height / 2; return y >= 0 && y <= innerHeight && element.contains(document.elementFromPoint(x, y)); }"), $"{id} must remain reachable in the scrolling page.");
            }
        }
        await ExpectNoHorizontalOverflowAsync(page);
        await ExpectJoinControlsReadableAsync(page);
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(390, 844, true)]
    [InlineData(360, 740, false)]
    [InlineData(360, 740, true)]
    [InlineData(320, 640, false)]
    [InlineData(320, 640, true)]
    [InlineData(390, 320, true)]
    public async Task PhoneDock_GivesTheTracksEvenRoomBesideAnArrowBack(int width, int height, bool dark)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            IsMobile = true,
            HasTouch = true
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinMobileStartAmateurBtn')?.disabled && !document.getElementById('joinMobileGoProButton')?.disabled");
        await ExpectActionStackDockedInViewportAsync(page, ".play-join-actions");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const box = id => document.getElementById(id).getBoundingClientRect();
                const back = box('joinTrackBackBtn');
                const amateur = box('joinMobileStartAmateurBtn');
                const pro = box('joinMobileGoProButton');
                const label = document.querySelector('#joinTrackBackBtn .flow-action__label').getBoundingClientRect();
                return [back.width, back.height, label.width, amateur.width, pro.width, Math.abs(amateur.top - pro.top), Math.abs(back.top - amateur.top)];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(layout[0] <= 56 && layout[0] >= 44 && layout[1] >= 44, $"{scenario}: Back should be the dashboard's arrow square, not a {layout[0]:F0}px button.");
        Assert.True(layout[2] == 0, $"{scenario}: Back's label should give way to its arrow on a phone.");
        Assert.True(Math.Abs(layout[3] - layout[4]) <= 2, $"{scenario}: the two tracks should share the bar evenly ({layout[3]:F0}px vs {layout[4]:F0}px).");
        Assert.True(layout[5] <= 1 && layout[6] <= 1, $"{scenario}: Back and both tracks should share one row.");
        await Assertions.Expect(page.Locator("#joinTrackBackBtn")).ToHaveAccessibleNameAsync("Back");
        await ExpectNoHorizontalOverflowAsync(page);
        await ExpectJoinControlsReadableAsync(page);
    }

    [Theory]
    [InlineData(390, 200, false)]
    [InlineData(390, 200, true)]
    [InlineData(414, 200, false)]
    [InlineData(390, 175, false)]
    [InlineData(360, 150, true)]
    [InlineData(320, 150, false)]
    public async Task EnlargedPhoneText_RestoresTheTwoRowTrackLayout(int width, int textPercent, bool dark)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            IsMobile = true,
            HasTouch = true
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinTrackBackBtn')?.disabled");
        await page.EvaluateAsync("percent => document.documentElement.style.setProperty('font-size', `${percent}%`, 'important')", textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await ExpectActionStackDockedInViewportAsync(page, ".play-join-actions");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const back = document.getElementById('joinTrackBackBtn');
                const amateur = document.getElementById('joinMobileStartAmateurBtn');
                const pro = document.getElementById('joinMobileGoProButton');
                const b = back.getBoundingClientRect(), a = amateur.getBoundingClientRect(), p = pro.getBoundingClientRect();
                if (!back.querySelector('.flow-action__label').getBoundingClientRect().width) issues.push('Back label is hidden');
                if (Math.abs(b.top - a.top) > 1 || p.top <= a.bottom) issues.push('expected Back and Amateur above Pro');
                if (Math.abs(p.left - b.left) > 1 || Math.abs(p.right - a.right) > 1) issues.push('Pro should span the whole second row');
                if (Math.max(b.height, a.height, p.height) - Math.min(b.height, a.height, p.height) > 2) issues.push('button heights differ');
                const range = document.createRange();
                range.selectNodeContents(amateur.querySelector('.flow-action__label'));
                if (new Set([...range.getClientRects()].map(rect => Math.round(rect.top))).size !== 1) issues.push('Start amateur wraps');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px at {textPercent}%: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
        await ExpectJoinControlsReadableAsync(page);

        var backButton = page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true });
        await backButton.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#joinMobileStartAmateurBtn")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#joinMobileGoProButton")).ToBeFocusedAsync();
        await page.EvaluateAsync("() => document.documentElement.style.removeProperty('font-size')");
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await Assertions.Expect(page.Locator("#joinMobileGoProButton")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task JoinDock_PreservesKeyboardOrderAndFocusWhenItMoves(bool fallback, bool enlarged)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = fallback ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        if (fallback) await context.AddInitScriptAsync("Element.prototype.moveBefore = undefined");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinTrackBackBtn')?.disabled");
        if (enlarged)
        {
            await page.EvaluateAsync("() => document.documentElement.style.setProperty('font-size', '200%', 'important')");
            await page.EvaluateAsync("() => document.fonts.ready");
            await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        }
        await ExpectActionStackDockedInViewportAsync(page, ".play-join-actions");
        var back = page.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true });
        await back.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#joinMobileStartAmateurBtn")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#joinMobileGoProButton")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(back).ToBeFocusedAsync();

        var scroll = await page.EvaluateAsync<double>("window.scrollY");
        foreach (var mode in new[] { "off", "always", "off", "always" })
        {
            await page.EvaluateAsync("mode => { const stack = document.querySelector('.play-join-actions'); stack.dataset.flowDock = mode; window.LwcFlowActionDock.refreshNow(); }", mode);
            await Assertions.Expect(back).ToBeFocusedAsync();
            Assert.InRange(await page.EvaluateAsync<double>("window.scrollY"), scroll - 1, scroll + 1);
            await Assertions.Expect(back).ToHaveAccessibleNameAsync("Back");
        }
        await page.EvaluateAsync("() => { document.querySelector('.play-join-actions').dataset.flowDock = 'auto'; }");
        foreach (var size in new[] { new ViewportSize { Width = 1440, Height = 900 }, new ViewportSize { Width = 390, Height = 844 } })
        {
            await page.SetViewportSizeAsync(size.Width, size.Height);
            await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
            await Assertions.Expect(back).ToBeFocusedAsync();
            await Assertions.Expect(back).ToHaveAccessibleNameAsync("Back");
        }
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForURLAsync("**/play");
        await Assertions.Expect(page.Locator("#playStartPanel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#joinTrackPanel")).ToBeHiddenAsync();
    }

    private static async Task ExpectJoinControlsReadableAsync(IPage page)
    {
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const actions = [...document.querySelectorAll('.play-join-actions > .flow-action')]
                    .filter(action => action.getBoundingClientRect().width > 0);
                const inside = (inner, outer) => inner.left >= outer.left - 1 && inner.right <= outer.right + 1
                    && inner.top >= outer.top - 1 && inner.bottom <= outer.bottom + 1;
                for (const action of actions) {
                    const box = action.getBoundingClientRect();
                    if (box.width < 44 || box.height < 44) issues.push(`${action.id}: target below 44px`);
                    const label = action.querySelector('.flow-action__label');
                    if (label.getBoundingClientRect().width) {
                        const range = document.createRange();
                        range.selectNodeContents(label);
                        if ([...range.getClientRects()].some(rect => !inside(rect, box))) issues.push(`${action.id}: clipped text`);
                    }
                    const icon = action.querySelector('i'), iconBox = icon.getBoundingClientRect();
                    if (iconBox.width && !inside(iconBox, box)) issues.push(`${action.id}: clipped icon`);
                    if (action.id === 'joinTrackBackBtn' && !label.getBoundingClientRect().width) {
                        const glyph = getComputedStyle(icon, '::before').content;
                        if (!iconBox.width || !iconBox.height || glyph === 'none' || glyph === 'normal') issues.push('Back: missing arrow');
                        if (Math.abs(iconBox.left + iconBox.width / 2 - box.left - box.width / 2) > 1) issues.push('Back: arrow not centered');
                    }
                }
                for (let i = 0; i < actions.length; i++) for (let j = i + 1; j < actions.length; j++) {
                    const a = actions[i].getBoundingClientRect(), b = actions[j].getBoundingClientRect();
                    if (Math.min(a.right, b.right) > Math.max(a.left, b.left) + 1
                        && Math.min(a.bottom, b.bottom) > Math.max(a.top, b.top) + 1) issues.push('overlapping controls');
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, string.Join("; ", problems));
    }

    [Fact]
    public async Task BiomarkerLists_OpenAndCloseFromTheKeyboard()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/join");
        await page.WaitForSelectorAsync(".play-join-card--pro summary");

        var summary = page.Locator(".play-join-card--pro summary");
        var details = page.Locator(".play-join-card--pro details");
        await summary.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(details).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(details.Locator("li")).ToHaveCountAsync(22);
        await Assertions.Expect(details.Locator("li").Last).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(details).Not.ToHaveAttributeAsync("open", "");
        var outline = await summary.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle");
        Assert.NotEqual("none", outline);
    }
}
