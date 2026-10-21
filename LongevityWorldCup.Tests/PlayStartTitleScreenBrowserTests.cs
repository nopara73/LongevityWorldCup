using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayStartTitleScreenBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string ReadLockupScript =
        """
        () => {
            const issues = [];
            const panel = document.getElementById('playStartPanel');
            const wordmark = document.querySelector('.play-menu-wordmark');
            const hero = wordmark.closest('.play-menu-hero');
            const crest = document.querySelector('.play-menu-crest');
            const logo = document.querySelector('.play-logo-watermark');
            const actions = document.querySelector('.play-menu-actions');
            const docked = actions.classList.contains('flow-action-stack--docked');
            if (!crest || !panel.contains(crest)) return ['the phoenix has no crest in the start panel'];
            if (!crest.contains(logo)) issues.push('the phoenix is not inside its crest');
            if (getComputedStyle(logo).position === 'fixed') issues.push('the phoenix floats on the page instead of the lockup');

            const range = document.createRange();
            range.selectNodeContents(wordmark);
            const title = range.getBoundingClientRect();
            const rule = getComputedStyle(hero, '::after');
            if (getComputedStyle(wordmark).fontSynthesis !== 'style') issues.push('the loaded upright font cannot synthesize the title italic');
            if (rule.content === 'none' || parseFloat(rule.height) < 3 || rule.backgroundColor !== 'rgb(25, 195, 209)')
                issues.push(`the title has no accent rule (${rule.content}, ${rule.height}, ${rule.backgroundColor})`);

            const ring = getComputedStyle(crest, '::before');
            if (parseFloat(ring.borderTopWidth) < 1 || parseFloat(ring.borderTopLeftRadius) < 1)
                issues.push('the crest has no ring');
            const c = crest.getBoundingClientRect();
            if (Math.abs(c.width - c.height) > 1) issues.push(`the crest is not round: ${c.width}x${c.height}`);

            const a = actions.getBoundingClientRect();
            const heroBox = hero.getBoundingClientRect();
            if (!(heroBox.bottom <= c.top + 1 && c.bottom <= a.top + 1))
                issues.push(`order is not title, crest, menu: ${heroBox.bottom}/${c.top}/${c.bottom}/${a.top}`);
            if (title.left < -1 || title.right > innerWidth + 1) issues.push('the title is clipped');

            const doors = [...actions.querySelectorAll('.flow-action')].map(d => d.getBoundingClientRect());
            const shortLandscape = innerWidth >= 480 && innerHeight <= 480;
            if (!shortLandscape) {
                if (doors.length !== 2 || doors[1].top < doors[0].bottom - 1) issues.push('the doors are not one column');
                if (Math.abs(doors[0].width - doors[1].width) > 1) issues.push('the doors are uneven');
                if (Math.abs((doors[0].left + doors[0].right) / 2 - innerWidth / 2) > 2) issues.push('the menu is off centre');
            }

            const gapAbove = c.top - heroBox.bottom;
            const gapBelow = a.top - c.bottom;
            if (Math.abs(gapAbove - gapBelow) > 24)
                issues.push(`the crest is not centred between the title and the menu: ${gapAbove} vs ${gapBelow}`);

            if (!docked) {
                const top = title.top;
                const bottom = innerHeight - a.bottom;
                if (Math.abs(top - bottom) > 48) issues.push(`the lockup is not centred: ${top} above, ${bottom} below`);
            } else {
                const bg = getComputedStyle(actions).backgroundImage;
                if (!bg.includes('16, 24, 32')) issues.push(`the dock is not the stage colour: ${bg}`);
                if (c.bottom > a.top - 8) issues.push('the dock covers the crest');
            }
            if (document.documentElement.scrollHeight > innerHeight + 1)
                issues.push(`the start screen scrolls: ${document.documentElement.scrollHeight} > ${innerHeight}`);
            return issues;
        }
        """;

    [Theory]
    [InlineData(1440, 900, false, false)]
    [InlineData(1280, 720, true, true)]
    [InlineData(820, 1180, false, false)]
    [InlineData(390, 844, true, false)]
    [InlineData(320, 640, false, true)]
    [InlineData(844, 390, false, false)]
    [InlineData(568, 320, false, false)]
    [InlineData(568, 320, true, true)]
    [InlineData(480, 320, true, false)]
    [InlineData(390, 320, false, false)]
    public async Task StartScreen_IsOneTitleLockupOfWordmarkCrestAndMenu(int width, int height, bool dark, bool returning)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 900
        });
        if (returning) await context.AddInitScriptAsync("localStorage.setItem('hasApplication', 'true');");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await page.WaitForFunctionAsync("() => document.documentElement.classList.contains('play-route-ready') && !document.getElementById('newGameBtn').disabled");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");

        var problems = await page.EvaluateAsync<string[]>(ReadLockupScript);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320, 640)]
    [InlineData(390, 844)]
    [InlineData(568, 320)]
    public async Task EnlargedStart_ReservesTheWholeDockAndKeepsBothDoorsReachable(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = ColorScheme.Dark,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await page.WaitForFunctionAsync("() => document.documentElement.classList.contains('play-route-ready') && !document.getElementById('newGameBtn').disabled");
        await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock.refreshNow(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
        var issues = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const actions = document.querySelector('.play-menu-actions');
                const crest = document.querySelector('.play-menu-crest').getBoundingClientRect();
                const box = actions.getBoundingClientRect();
                if (crest.bottom > box.top - 8) issues.push('dock covers the crest');
                for (const button of actions.querySelectorAll('button')) {
                    const r = button.getBoundingClientRect(), range = document.createRange();
                    range.selectNodeContents(button);
                    const text = range.getBoundingClientRect();
                    if (text.left < r.left || text.right > r.right || text.top < r.top || text.bottom > r.bottom)
                        issues.push('clipped label: ' + button.id);
                    if (r.width < 44 || r.height < 44) issues.push('small target: ' + button.id);
                }
                return issues;
            }
            """);
        Assert.Empty(issues);
        foreach (var id in new[] { "newGameBtn", "continueGameBtn" })
        {
            var button = page.Locator("#" + id);
            await button.ScrollIntoViewIfNeededAsync();
            await button.ClickAsync(new() { Trial = true });
        }
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/play", false)]
    [InlineData("/play", true)]
    [InlineData("/join", false)]
    [InlineData("/join", true)]
    [InlineData("/select-athlete", true)]
    [InlineData("/pheno-age", true)]
    [InlineData("/bortz-age", true)]
    [InlineData("/proofs", true)]
    [InlineData("/edit-profile", true)]
    [InlineData("/apply", true)]
    public async Task PlayForwardActions_KeepGreenInEnabledAndHoverStates(string route, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(
            """
            const athlete = { Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: [] };
            sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            sessionStorage.setItem('tempAthlete', JSON.stringify(athlete));
            sessionStorage.setItem('biomarkerData', JSON.stringify({ Biomarkers: [{ Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }] }));
            localStorage.setItem('hasApplication', 'true');
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync(route);
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && document.querySelector('.option-button.green')");
        if (route == "/apply")
        {
            await page.Locator("#name").FillAsync("Browser Test Athlete");
            await page.Locator("#flag").FillAsync("Hungary");
            await page.Locator("#division").SelectOptionAsync(new SelectOptionValue { Label = "Men's" });
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
        }
        // Probe CSS states on the actual controls without submitting any form.
        var colors = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('.option-button.green')].map(button => {
                button.disabled = false;
                button.removeAttribute('aria-disabled');
                const style = getComputedStyle(button);
                return style.backgroundColor + '|' + style.color;
            })
            """);
        Assert.NotEmpty(colors);
        Assert.All(colors, color => Assert.Equal("rgb(31, 122, 56)|rgb(255, 255, 255)", color));
        var button = page.Locator(".option-button.green:visible").First;
        await button.HoverAsync();
        await Assertions.Expect(button).ToHaveCSSAsync("background-color", "rgb(23, 97, 45)");
        await Assertions.Expect(button).ToHaveCSSAsync("color", "rgb(255, 255, 255)");
    }

    [Fact]
    public async Task StartIntro_RaisesTheTitleWordByWordThenTheCrestThenTheMenu()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 },
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await page.WaitForFunctionAsync("() => document.documentElement.classList.contains('play-route-ready') && !document.getElementById('newGameBtn').disabled");

        var timeline = await page.EvaluateAsync<double[]>(
            """
            () => {
                document.body.classList.remove('play-start-preintro');
                document.body.classList.add('play-start-active', 'play-start-intro');
                const delayOf = (element, pseudo) => {
                    const style = getComputedStyle(element, pseudo);
                    if (style.animationName === 'none') return -1;
                    return parseFloat(style.animationDelay) * (style.animationDelay.endsWith('ms') ? 1 : 1000);
                };
                const words = [...document.querySelectorAll('.play-menu-wordmark__word')].map(w => delayOf(w));
                const rule = delayOf(document.querySelector('.play-menu-hero'), '::after');
                const crest = delayOf(document.querySelector('.play-menu-crest'));
                const doors = [...document.querySelectorAll('.play-menu-actions .flow-action')].map(d => delayOf(d));
                return [...words, rule, crest, ...doors];
            }
            """);

        Assert.Equal(7, timeline.Length);
        Assert.All(timeline, delay => Assert.True(delay >= 0, $"An intro part does not animate: {string.Join(", ", timeline)}"));
        var (words, rule, crest, doors) = (timeline[..3], timeline[3], timeline[4], timeline[5..]);
        Assert.True(words[0] < words[1] && words[1] < words[2], $"The title words do not rise one after another: {string.Join(", ", words)}");
        Assert.True(crest > words[0] && rule > words[2], $"The crest and rule do not follow the title: crest {crest}, rule {rule}");
        Assert.True(doors[0] > crest && doors[1] > doors[0], $"The menu does not arrive last, door by door: {string.Join(", ", doors)}");

        await page.SetViewportSizeAsync(390, 844);
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock.refreshNow(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
        var frameIssues = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                document.body.classList.remove('play-start-intro');
                void document.body.offsetWidth;
                document.body.classList.add('play-start-intro');
                const actions = document.querySelector('.play-menu-actions');
                actions.classList.add('flow-action-stack--dock-entering');
                const animations = document.getAnimations().filter(a => a.effect?.target?.closest?.('.play-start-panel, .play-menu-actions'));
                animations.forEach(a => a.pause());
                for (const time of [0, 140, 320, 630, 720, 1000, 1200]) {
                    animations.forEach(a => a.currentTime = time);
                    for (const button of actions.querySelectorAll('button')) {
                        const r = button.getBoundingClientRect();
                        const hit = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2);
                        if (r.top < 0 || r.bottom > innerHeight + 1) issues.push(time + ': clipped door ' + button.id);
                        if (!hit || !button.contains(hit) || getComputedStyle(button).pointerEvents === 'none')
                            issues.push(time + ': unclickable door ' + button.id);
                    }
                }
                actions.classList.remove('flow-action-stack--dock-entering');
                return issues;
            }
            """);
        Assert.Empty(frameIssues);

        await using var reduced = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var still = await reduced.NewPageAsync();
        await still.GotoAsync("/play");
        await still.WaitForFunctionAsync("() => document.documentElement.classList.contains('play-route-ready')");
        var animated = await still.EvaluateAsync<int>(
            """
            () => {
                document.body.classList.add('play-start-active', 'play-start-intro');
                return [...document.querySelectorAll('.play-menu-wordmark__word, .play-menu-crest, .play-menu-actions .flow-action')]
                    .filter(e => getComputedStyle(e).animationName !== 'none').length
                    + (getComputedStyle(document.querySelector('.play-menu-hero'), '::after').animationName !== 'none' ? 1 : 0);
            }
            """);
        Assert.Equal(0, animated);
    }
}
