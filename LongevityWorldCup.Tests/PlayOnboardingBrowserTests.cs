using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayOnboardingBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string ReturningAmateur = """
        const athlete = {
            Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete', PersonalLink: 'https://example.test/browser-test-athlete',
            DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: []
        };
        sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
        localStorage.setItem('selectedAthleteName', athlete.Name);
        localStorage.setItem('hasApplication', 'true');
        localStorage.setItem('gmaHasPerfectGuess', '1');
        """;

    private async Task<IPage> OpenJoinAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinTrackBackBtn')?.disabled && !document.getElementById('joinGoProButton')?.disabled");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
        return page;
    }

    [Theory]
    [InlineData(1440, 900, false, false)]
    [InlineData(1440, 900, true, true)]
    [InlineData(1280, 720, false, true)]
    [InlineData(1024, 768, true, false)]
    [InlineData(768, 1024, false, false)]
    public async Task WideJoin_IsOneCardHeadedByTheStartStageBand(int width, int height, bool dark, bool withDiscount)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        if (withDiscount) await context.AddInitScriptAsync(ReturningAmateur);
        var page = await OpenJoinAsync(context);
        if (withDiscount) await Assertions.Expect(page.Locator("#joinProDiscountNote")).ToBeVisibleAsync();

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const panel = document.getElementById('joinTrackPanel');
                const head = panel.querySelector('.play-join-head');
                const foot = panel.querySelector('.play-join-foot');
                if (!head || !foot) return ['the join panel has no band or foot'];
                const p = panel.getBoundingClientRect(), h = head.getBoundingClientRect(), f = foot.getBoundingClientRect();
                const ps = getComputedStyle(panel);
                if (parseFloat(ps.borderTopWidth) < 1 || parseFloat(ps.borderTopLeftRadius) < 12) issues.push('panel is not a rounded bordered card');
                if (ps.backgroundColor === 'rgba(0, 0, 0, 0)') issues.push('panel has no surface');
                const stage = getComputedStyle(document.documentElement).getPropertyValue('--background-color').trim();
                const probe = document.createElement('i');
                probe.style.color = stage;
                document.body.append(probe);
                const stageRgb = getComputedStyle(probe).color;
                probe.remove();
                if (getComputedStyle(head).backgroundColor !== stageRgb) issues.push(`band ${getComputedStyle(head).backgroundColor} is not the start stage ${stageRgb}`);
                if (getComputedStyle(panel.querySelector('.play-join-title')).color !== 'rgb(255, 255, 255)') issues.push('title is not white on the band');
                if (Math.abs(h.top - p.top) > 2 || Math.abs(h.left - p.left) > 2 || Math.abs(h.right - p.right) > 2) issues.push('band is not the card header');
                if (Math.abs(f.bottom - p.bottom) > 2 || f.left < p.left - 1 || f.right > p.right + 1) issues.push('foot is not the card foot');
                for (const card of panel.querySelectorAll('.play-join-card')) {
                    const c = card.getBoundingClientRect();
                    // The tiles rise out of the band's lower edge, below its lead line.
                    const intro = panel.querySelector('.play-join-intro').getBoundingClientRect();
                    if (c.top < intro.bottom || c.top >= h.bottom || c.bottom > f.top || c.left < p.left || c.right > p.right) issues.push('track tile does not rise out of the band into the card body');
                }
                const back = document.getElementById('joinTrackBackBtn');
                const actions = document.querySelector('.play-join-actions');
                if (!actions.classList.contains('flow-action-stack--docked')) {
                    const b = back.getBoundingClientRect(), n = panel.querySelector('.play-join-challenge-note').getBoundingClientRect();
                    if (!foot.contains(back)) issues.push('Back left the foot');
                    if (b.left - p.left > 40) issues.push('Back should lead the foot');
                    if (n.left < b.right) issues.push('the detour should follow Back');
                    if (Math.abs((b.top + b.bottom) / 2 - (n.top + n.bottom) / 2) > 12 && n.top < b.bottom) issues.push('Back and the detour should share the foot row');
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        if (height <= 820 && !withDiscount)
            Assert.False(await HasDockClassAsync(page, ".play-join-actions"), $"{width}x{height}: Back should stay in the card foot.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320, 640, false)]
    [InlineData(390, 844, true)]
    [InlineData(768, 1024, false)]
    [InlineData(844, 390, true)]
    [InlineData(1440, 900, false)]
    public async Task StartDoors_ReflowLongLabelsWithoutCoveringTheWordmark(int width, int height, bool returning)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = width < 600
        });
        if (returning) await context.AddInitScriptAsync("localStorage.setItem('hasApplication', 'true');");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await page.WaitForFunctionAsync("() => document.documentElement.classList.contains('play-route-ready') && !document.getElementById('newGameBtn').disabled");
        await page.EvaluateAsync(
            """
            async () => {
                document.documentElement.style.setProperty('font-size', '200%', 'important');
                for (const label of document.querySelectorAll('.play-menu-actions .flow-action__label'))
                    label.textContent += ' — continue to my chosen competition';
                await document.fonts.ready;
                window.LwcFlowActionDock.refreshNow();
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
            }
            """);
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const wordmark = document.querySelector('.play-menu-wordmark');
                const range = document.createRange();
                range.selectNodeContents(wordmark);
                const brand = range.getBoundingClientRect();
                const actions = document.querySelector('.play-menu-actions');
                const a = actions.getBoundingClientRect();
                if (brand.left < -1 || brand.right > innerWidth + 1) issues.push('wordmark is clipped');
                if (brand.bottom > a.top - 1) issues.push('actions cover the wordmark');
                if (actions.classList.contains('flow-action-stack--docked')
                    && (a.top < 0 || a.bottom > innerHeight + 1 || a.height > innerHeight * 0.5 + 1))
                    issues.push('the oversized action dock must return to the page');
                for (const label of actions.querySelectorAll('.flow-action__label')) {
                    const text = label.firstChild;
                    const button = label.closest('button').getBoundingClientRect();
                    for (const match of text.textContent.matchAll(/\S+/g)) {
                        range.setStart(text, match.index);
                        range.setEnd(text, match.index + match[0].length);
                        const rects = [...range.getClientRects()];
                        if (new Set(rects.map(r => Math.round(r.top))).size > 1) issues.push(`split word: ${match[0]}`);
                        if (rects.some(r => r.left < button.left - 1 || r.right > button.right + 1)) issues.push(`clipped word: ${match[0]}`);
                    }
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        foreach (var id in new[] { "newGameBtn", "continueGameBtn" })
        {
            await page.Locator($"#{id}").FocusAsync();
            await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
            Assert.True(await page.Locator($"#{id}").EvaluateAsync<bool>(
                """
                e => {
                    const r = e.getBoundingClientRect(), x = (r.left + r.right) / 2, y = (r.top + r.bottom) / 2;
                    const hit = document.elementFromPoint(x, y);
                    return y >= 0 && y <= innerHeight && hit && (hit === e || e.contains(hit));
                }
                """), $"{width}x{height}: {id} is not reachable.");
        }
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, 320, false)]
    [InlineData(390, 320, true)]
    [InlineData(844, 390, true)]
    public async Task InlineJoinFoot_KeepsVisualOrderInSyncWithKeyboardOrder(int width, int height, bool longLabels)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = true
        });
        var page = await OpenJoinAsync(context);
        await page.EvaluateAsync(
            """
            longLabels => {
                document.documentElement.style.setProperty('font-size', '200%', 'important');
                if (longLabels) {
                    for (const label of document.querySelectorAll('.play-join-mobile-track-action .flow-action__label'))
                        label.textContent = 'Start my chosen longevity competition';
                }
                window.LwcFlowActionDock.refreshNow();
            }
            """, longLabels);
        await page.WaitForFunctionAsync("() => !document.querySelector('.play-join-actions').classList.contains('flow-action-stack--docked')");
        var order = await page.EvaluateAsync<bool>(
            """
            () => {
                const actions = document.querySelector('.play-join-actions');
                const note = document.querySelector('.play-join-challenge-note');
                const placeholder = document.querySelector('.play-join-foot > .flow-action-dock-placeholder');
                return actions.parentElement.classList.contains('play-join-foot')
                    && placeholder.hidden
                    && !!(actions.compareDocumentPosition(note) & Node.DOCUMENT_POSITION_FOLLOWING)
                    && note.getBoundingClientRect().top >= actions.getBoundingClientRect().bottom - 1;
            }
            """);
        Assert.True(order, $"{width}x{height}: the inline actions must precede the detour in visual and keyboard order.");
        await page.Locator("#joinMobileGoProButton").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#joinStartChallengeLink")).ToBeFocusedAsync();
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 768, true)]
    [InlineData(390, 844, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task TrackTiles_HaveAHeadBandAboveASpecList(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await OpenJoinAsync(context);

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                for (const card of document.querySelectorAll('.play-join-card')) {
                    const name = card.querySelector('.track-name').textContent.trim();
                    const head = card.querySelector('.play-join-card-head');
                    if (!head) { issues.push(`${name}: no head band`); continue; }
                    const cs = getComputedStyle(card), hs = getComputedStyle(head);
                    if (hs.backgroundColor === cs.backgroundColor || hs.backgroundColor === 'rgba(0, 0, 0, 0)') issues.push(`${name}: head is not a band`);
                    if (parseFloat(hs.borderBottomWidth) < 1) issues.push(`${name}: head has no divider`);
                    const h = head.getBoundingClientRect(), c = card.getBoundingClientRect();
                    if (Math.abs(h.top - c.top) > 2 || Math.abs(h.left - c.left) > 2 || Math.abs(h.right - c.right) > 2) issues.push(`${name}: head is not flush with the tile`);
                    if (!head.contains(card.querySelector('h2')) || !head.contains(card.querySelector('.play-join-tagline'))) issues.push(`${name}: name and rank should sit in the head`);
                    const meta = card.querySelector('.play-join-meta').getBoundingClientRect();
                    if (meta.top < h.bottom) issues.push(`${name}: spec list overlaps the head`);
                    const terms = [...card.querySelectorAll('.play-join-meta dt')];
                    for (const dt of terms) {
                        const style = getComputedStyle(dt);
                        if (style.textTransform !== 'uppercase') issues.push(`${name}: ${dt.textContent} is not a spec label`);
                        if (dt.textContent.trim().endsWith(':')) issues.push(`${name}: ${dt.textContent} keeps a colon`);
                        const range = document.createRange();
                        range.selectNodeContents(dt);
                        const bounds = dt.getBoundingClientRect();
                        if ([...range.getClientRects()].some(r => r.left < bounds.left - 1 || r.right > bounds.right + 1)) issues.push(`${name}: ${dt.textContent.trim()} is clipped`);
                    }
                    for (const dt of terms.slice(1)) {
                        if (parseFloat(getComputedStyle(dt).borderTopWidth) < 1) issues.push(`${name}: spec rows have no dividers`);
                        const dd = dt.nextElementSibling;
                        const label = dt.getBoundingClientRect(), value = dd.getBoundingClientRect();
                        const stacked = getComputedStyle(card.querySelector('.play-join-meta')).gridTemplateColumns.split(' ').length === 1;
                        if (stacked ? value.top < label.bottom - 1 : Math.abs(label.top - value.top) > 1) issues.push(`${name}: ${dt.textContent.trim()} and its value are not paired`);
                    }
                    const list = card.querySelector('.play-join-biomarkers');
                    const ls = getComputedStyle(list);
                    if (parseFloat(ls.borderLeftWidth) < 1 || parseFloat(ls.borderTopLeftRadius) < 4) issues.push(`${name}: biomarkers are not a bordered row`);
                    const ribbon = card.querySelector('.play-join-ribbon > span');
                    if (ribbon && getComputedStyle(card.querySelector('.play-join-ribbon')).position !== 'static') {
                        // Map each corner of the rank line's text into the rotated ribbon's own frame.
                        const r = ribbon.getBoundingClientRect();
                        const cx = (r.left + r.right) / 2, cy = (r.top + r.bottom) / 2;
                        const halfW = ribbon.offsetWidth / 2, halfH = ribbon.offsetHeight / 2, k = Math.SQRT1_2;
                        const range = document.createRange();
                        range.selectNodeContents(card.querySelector('.play-join-tagline'));
                        const covered = [...range.getClientRects()].some(t =>
                            [[t.left, t.top], [t.right, t.top], [t.left, t.bottom], [t.right, t.bottom]].some(([x, y]) => {
                                const dx = x - cx, dy = y - cy;
                                return Math.abs(dx * k + dy * k) < halfW && Math.abs(-dx * k + dy * k) < halfH;
                            }));
                        if (covered) issues.push(`${name}: the ribbon covers the rank line`);
                    }
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1280, 720, true)]
    [InlineData(768, 1024, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    [InlineData(844, 390, true)]
    public async Task TrackSpecs_ReflowLongLabelsAndEnlargedTextWithoutClipping(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync(ReturningAmateur);
        var page = await OpenJoinAsync(context);
        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.style.setProperty('font-size', '200%', 'important');
                for (const label of document.querySelectorAll('.play-join-meta dt:first-of-type'))
                    label.textContent = 'Zulässiger Untersuchungszeitraum';
                for (const name of document.querySelectorAll('.play-join-card .track-name'))
                    name.textContent += ' longevity athlete';
                for (const details of document.querySelectorAll('.play-join-biomarkers details')) details.open = true;
            }
            """);
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                for (const card of document.querySelectorAll('.play-join-card')) {
                    const c = card.getBoundingClientRect();
                    for (const element of card.querySelectorAll('dt, dd, h2, .play-join-tagline, summary')) {
                        const b = element.getBoundingClientRect();
                        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                        let text;
                        while ((text = walker.nextNode())) {
                            if (!text.textContent.trim()) continue;
                            const range = document.createRange();
                            range.selectNodeContents(text);
                            if ([...range.getClientRects()].some(r => r.left < Math.max(b.left, c.left) - 1 || r.right > Math.min(b.right, c.right) + 1))
                                issues.push(`clipped text: ${text.textContent.trim()}`);
                        }
                    }
                    for (const label of card.querySelectorAll('dt')) {
                        const a = label.getBoundingClientRect(), b = label.nextElementSibling.getBoundingClientRect();
                        if (a.left < b.right && a.right > b.left && a.top < b.bottom - 1 && a.bottom > b.top + 1)
                            issues.push(`overlapping spec: ${label.textContent}`);
                        if (label.nextElementSibling.nextElementSibling?.tagName === 'DT'
                            && label.nextElementSibling.nextElementSibling.getBoundingClientRect().top < b.bottom - 1)
                            issues.push(`value runs into the next spec: ${label.textContent}`);
                    }
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, true)]
    public async Task PhoneJoin_OpensOnTheStageAndKeepsTheDetourAboveTheDockReserve(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await OpenJoinAsync(context);
        Assert.True(await HasDockClassAsync(page, ".play-join-actions"));

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const head = document.querySelector('.play-join-head');
                if (!head) return ['no band'];
                const hs = getComputedStyle(head);
                if (hs.color !== 'rgb(255, 255, 255)' || parseFloat(hs.borderTopLeftRadius) > 0) issues.push('band is not an edge-to-edge night stage');
                const h = head.getBoundingClientRect();
                const header = document.querySelector('header[role=banner]').getBoundingClientRect();
                if (h.top - header.bottom > 1) issues.push(`stage starts ${Math.round(h.top - header.bottom)}px under the site header`);
                const cards = [...document.querySelectorAll('.play-join-card')].map(c => c.getBoundingClientRect());
                const intro = document.querySelector('.play-join-intro').getBoundingClientRect();
                if (cards[0].top < intro.bottom || cards[0].top >= h.bottom) issues.push('tracks do not rise out of the stage');
                if (Math.abs(h.left - cards[0].left) > 1 || Math.abs(h.right - cards[0].right) > 1) issues.push('stage content and tiles do not share edges');
                const note = document.querySelector('.play-join-challenge-note').getBoundingClientRect();
                const reserve = document.querySelector('.play-join-foot > .flow-action-dock-placeholder');
                if (note.top < cards[1].bottom) issues.push('detour overlaps the pro track');
                if (note.top - cards[1].bottom > 40) issues.push(`detour floats ${Math.round(note.top - cards[1].bottom)}px below the tracks`);
                if (reserve && reserve.getBoundingClientRect().top < note.bottom - 1) issues.push('the dock reserve sits between the tracks and the detour');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1280, 720, true)]
    [InlineData(768, 1024, false)]
    [InlineData(390, 844, false)]
    [InlineData(320, 640, true)]
    public async Task StartDoors_StackAsOneMenuColumnAndTheSecondIsOutlinedOnTheStage(int width, int height, bool returning)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        if (returning) await context.AddInitScriptAsync("localStorage.setItem('hasApplication', 'true');");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await page.WaitForFunctionAsync("() => document.documentElement.classList.contains('play-route-ready') && !document.getElementById('newGameBtn').disabled");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const actions = document.querySelector('.play-menu-actions');
                const [first, second] = actions.querySelectorAll('.flow-action');
                if (!first.classList.contains('green')) issues.push('the first door should be the primary');
                const s = getComputedStyle(second);
                if (s.color !== 'rgb(255, 255, 255)') issues.push(`second door text ${s.color}`);
                if (parseFloat(s.borderTopWidth) < 1 || !/rgba\(255, 255, 255, 0\.[2-9]/.test(s.borderTopColor)) issues.push(`second door border ${s.borderTopColor}`);
                const bg = s.backgroundColor.match(/[\d.]+/g).map(Number);
                if (bg.length === 4 ? bg[3] > 0.2 : (bg[0] + bg[1] + bg[2]) / 3 > 90) issues.push(`second door is a pale slab ${s.backgroundColor}`);
                const a = first.getBoundingClientRect(), b = second.getBoundingClientRect();
                if (b.top < a.bottom - 1) issues.push('doors should stack as one menu column');
                if (Math.abs(a.width - b.width) > 2 || Math.abs(a.height - b.height) > 1) issues.push('doors should be even');
                if (innerWidth >= 761 && !actions.classList.contains('flow-action-stack--docked')) {
                    const as = getComputedStyle(actions);
                    if (parseFloat(as.borderTopWidth) > 0 || as.backgroundColor !== 'rgba(0, 0, 0, 0)') issues.push('the menu column should sit on the stage, not on a plate');
                    if (a.width > 352 + 1) issues.push(`menu column is ${a.width}px wide`);
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }
}
