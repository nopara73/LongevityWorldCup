using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DashboardAthleteCardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, true, 1)]
    [InlineData(1440, 900, false, 1)]
    [InlineData(390, 844, false, 1)]
    [InlineData(390, 844, true, 1)]
    [InlineData(320, 640, false, 1)]
    [InlineData(844, 390, false, 1)]
    [InlineData(1280, 600, true, 1)]
    [InlineData(1280, 720, true, 1)]
    [InlineData(1366, 768, true, 1)]
    [InlineData(820, 1180, true, 1)]
    [InlineData(1440, 900, false, 2)]
    [InlineData(820, 1180, true, 2)]
    [InlineData(390, 844, false, 2)]
    [InlineData(320, 640, false, 2)]
    public async Task Dashboard_CaptionsThePortraitWithTheNameAndKeepsActionsWhole(int width, int height, bool isPro, int textScale)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync(
            """
            (isPro => {
                const athlete = {
                    Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete', PersonalLink: 'https://example.test/browser-test-athlete',
                    DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: isPro ? [{ Date: '2026-06-19', Hba1cMmolMol: 35 }] : []
                };
                sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
                localStorage.setItem('selectedAthleteName', athlete.Name);
                localStorage.setItem('hasApplication', 'true');
            })(
            """ + (isPro ? "true" : "false") + ")");
        if (textScale == 2)
            await context.AddInitScriptAsync("document.addEventListener('DOMContentLoaded', () => document.documentElement.style.fontSize = '200%')");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/dashboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => !document.documentElement.hasAttribute('data-initial-view') && document.getElementById('athleteDashboardPanel')?.hidden === false && document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const title = document.getElementById('athleteDashboardTitle').getBoundingClientRect();
                const picture = document.getElementById('athleteDashboardPicture').getBoundingClientRect();
                const actions = document.getElementById('athleteDashboardActions');
                const actionsBox = actions.getBoundingClientRect();
                const back = document.querySelector('#playDashboardBackBtn .flow-action__label');
                const range = document.createRange();
                range.selectNodeContents(back);
                const backLines = new Set(Array.from(range.getClientRects()).map(rect => Math.round(rect.top))).size;
                const icon = document.querySelector('#playDashboardBackBtn > i').getBoundingClientRect();
                const label = back.getBoundingClientRect();
                const iconOverlap = Math.max(0, Math.min(icon.right, label.right) - Math.max(icon.left, label.left));
                return [title.left, title.right, title.top, title.bottom, picture.left, picture.right, picture.top, picture.bottom,
                    actionsBox.top, actions.classList.contains('flow-action-stack--docked') ? 1 : 0, backLines, iconOverlap, actionsBox.bottom];
            }
            """);
        var scenario = $"{width}x{height}, {textScale * 100}% text, {(isPro ? "pro" : "amateur")}";
        var captionBelow = width >= 761 && height >= 600 && textScale == 2;
        Assert.True(layout[0] >= layout[4] - 1 && layout[1] <= layout[5] + 1 && layout[2] >= layout[6] - 1,
            $"{scenario}: the athlete name should stay within the portrait's width.");
        if (captionBelow)
            Assert.InRange(Math.Abs(layout[2] - layout[7]), 0, 2);
        else
            Assert.InRange(Math.Abs(layout[3] - layout[7]), 0, 2);
        if (layout[9] == 1)
            Assert.True(layout[7] <= layout[8] + 1, $"{scenario}: the docked actions cover the portrait ({layout[7]} > {layout[8]}).");
        if (isPro && textScale == 1 && ((width == 390 && height == 844)
            || (width == 1280 && height == 720) || (width == 1366 && height == 768)))
        {
            Assert.True(layout[9] == 0, $"{scenario}: fitting Pro actions should stay inline.");
            Assert.True(layout[12] <= height - 12, $"{scenario}: inline actions exceed the viewport.");
            var minPictureWidth = width < 600 ? 300 : height <= 740 ? 310 : 340;
            Assert.InRange(layout[5] - layout[4], minPictureWidth, width >= 761 ? 560 : 408);
        }
        // Phone docks show an icon. An inline fallback at 200% text may use two
        // whole words on separate lines; all other visible back labels fit one.
        var maxBackLines = textScale == 2 && width < 600 && layout[9] == 0 ? 2 : 1;
        Assert.True(layout[10] <= maxBackLines, $"{scenario}: \"Change athlete\" wraps onto {layout[10]} lines.");
        Assert.True(layout[11] <= 1, $"{scenario}: the back arrow overlaps the label by {layout[11]}px.");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));

        // Every action remains reachable, readable and unobscured when the dock
        // reflows or returns to normal document flow at large text sizes.
        foreach (var action in await page.Locator("#athleteDashboardActions .flow-action").AllAsync())
        {
            await action.FocusAsync();
            await action.ScrollIntoViewIfNeededAsync();
            Assert.True(await action.EvaluateAsync<bool>("""
                button => {
                    const rect = button.getBoundingClientRect();
                    const hit = document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2);
                    return rect.width >= 44 && rect.height >= 44 && button.contains(hit);
                }
                """), $"{scenario}: an action is covered or smaller than its touch target.");
        }
        if (width < 600)
            await page.Locator("#playDashboardBackBtn").TapAsync();
        else
        {
            await page.Locator("#playDashboardBackBtn").FocusAsync();
            await page.Keyboard.PressAsync("Enter");
        }
        await Assertions.Expect(page.Locator("#athleteSelectionPanel")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(1440, 640, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1366, 768, false)]
    public async Task DockedDashboard_IsOneCentredCommandGroup(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        // An amateur's discounts push the actions below the fold, so they dock.
        await context.AddInitScriptAsync(
            """
            const athlete = {
                Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete', PersonalLink: 'https://example.test/browser-test-athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: []
            };
            sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            localStorage.setItem('selectedAthleteName', athlete.Name);
            localStorage.setItem('hasApplication', 'true');
            localStorage.setItem('gmaHasPerfectGuess', '1');
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/dashboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => !document.documentElement.hasAttribute('data-initial-view') && document.getElementById('athleteDashboardPanel')?.hidden === false && document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        await Assertions.Expect(page.Locator("#athleteDashboardActions")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("flow-action-stack--docked"));

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const actions = [...document.querySelectorAll('#athleteDashboardActions .flow-action')].filter(action => action.offsetParent);
                const boxes = actions.map(action => action.getBoundingClientRect());
                const left = Math.min(...boxes.map(box => box.left));
                const right = Math.max(...boxes.map(box => box.right));
                const gaps = boxes.map(box => box.left).sort((a, b) => a - b)
                    .map((x, i, xs) => i === 0 ? 0 : x - boxes.find(box => box.left === xs[i - 1]).right).slice(1);
                const back = document.getElementById('playDashboardBackBtn').getBoundingClientRect();
                return [left, innerWidth - right, Math.max(...gaps), back.left === left ? 1 : 0];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(Math.Abs(layout[0] - layout[1]) <= 4, $"{scenario}: the docked actions should be centred ({layout[0]:F0}px left vs {layout[1]:F0}px right).");
        Assert.True(layout[2] <= 24, $"{scenario}: the docked actions should read as one group, not leave a {layout[2]:F0}px gap.");
        Assert.True(layout[3] == 1, $"{scenario}: Change athlete should lead the group.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DashboardDock_KeepsKeyboardOrderAndFocusAcrossRealResize(bool fallback)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 1600 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete',
                PersonalLink: 'https://example.test/browser-test-athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: []
            }));
            localStorage.setItem('gmaHasPerfectGuess', '1');
            """);
        if (fallback)
            await context.AddInitScriptAsync("Element.prototype.moveBefore = undefined");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/dashboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && document.querySelectorAll('#athleteDashboardActions .flow-action').length === 5");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock.refreshNow(); }");
        var back = page.Locator("#playDashboardBackBtn");
        var edit = page.GetByRole(AriaRole.Button, new() { Name = "Edit profile", Exact = true });
        var stack = page.Locator("#athleteDashboardActions");
        Assert.False(await stack.EvaluateAsync<bool>("s => s.classList.contains('flow-action-stack--docked')"));
        await back.FocusAsync();

        // Check normal-height inline commands before overflowing them. Releasing
        // an existing dock also needs its 48px hysteresis gap, so return to 1024.
        foreach (var height in new[] { 900, 640, 1024, 1600, 640 })
        {
            await page.SetViewportSizeAsync(1440, height);
            await page.WaitForFunctionAsync("docked => document.querySelector('#athleteDashboardActions').classList.contains('flow-action-stack--docked') === docked", height == 640);
            await Assertions.Expect(back).ToBeFocusedAsync();
            Assert.True(await stack.EvaluateAsync<bool>("s => s.classList.contains('flow-action-stack--docked') ? s.firstElementChild.id === 'playDashboardBackBtn' : s.lastElementChild.id === 'playDashboardBackBtn'"));
            if (height == 900)
                Assert.True(await stack.EvaluateAsync<bool>("s => s.closest('#athleteDashboardPanel') !== null && s.getBoundingClientRect().bottom <= innerHeight - 12"),
                    "At normal desktop height the fitting commands should remain inside the player card.");
            if (height == 640)
            {
                await page.Keyboard.PressAsync("Tab");
                await Assertions.Expect(edit).ToBeFocusedAsync();
                await page.Keyboard.PressAsync("Shift+Tab");
                await Assertions.Expect(back).ToBeFocusedAsync();
            }
        }
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#athleteSelectionPanel")).ToBeVisibleAsync();
        Assert.False(await stack.EvaluateAsync<bool>("s => s.classList.contains('flow-action-stack--docked')"));
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    [InlineData(844, 390)]
    public async Task DashboardDock_ActualDoubleTextKeepsCommandsAndIconsWhole(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete',
                PersonalLink: 'https://example.test/browser-test-athlete', Biomarkers: []
            }));
            localStorage.setItem('gmaHasPerfectGuess', '1');
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/dashboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && document.querySelectorAll('#athleteDashboardActions .flow-action').length === 5");
        var before = await page.EvaluateAsync<double>("""
            async () => {
                await document.fonts.ready;
                const label = document.querySelector('#athleteDashboardActions .flow-action__label');
                const before = parseFloat(getComputedStyle(label).fontSize);
                const elements = [document.body, ...document.body.querySelectorAll('*')].filter(e => e instanceof HTMLElement);
                const sizes = elements.map(e => [e, parseFloat(getComputedStyle(e).fontSize)]);
                for (const [e, size] of sizes) e.style.setProperty('font-size', `${size * 2}px`, 'important');
                return before;
            }
            """);
        await WaitForFiniteAnimationsAsync(page);
        var ratio = await page.EvaluateAsync<double>("""
            async before => {
                window.LwcFlowActionDock.refreshNow();
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                const label = document.querySelector('#athleteDashboardActions .flow-action__label');
                return parseFloat(getComputedStyle(label).fontSize) / before;
            }
            """, before);
        Assert.InRange(ratio, 1.99, 2.01);
        if (width >= 761)
        {
            var gaps = await page.EvaluateAsync<double[]>("""
                () => {
                    const stack = document.getElementById('athleteDashboardActions');
                    if (!stack.classList.contains('flow-action-stack--docked')) return [];
                    const boxes = [...stack.querySelectorAll('.flow-action')].map(a => a.getBoundingClientRect())
                        .filter(r => r.width && r.height).sort((a, b) => a.top - b.top || a.left - b.left);
                    return boxes.slice(1).flatMap((box, i) => Math.abs(box.top - boxes[i].top) <= 1 ? [box.left - boxes[i].right] : []);
                }
                """);
            Assert.All(gaps, gap => Assert.InRange(gap, 0, 24));
        }
        foreach (var action in await page.Locator("#athleteDashboardActions .flow-action").AllAsync())
        {
            await action.FocusAsync();
            await action.ScrollIntoViewIfNeededAsync();
            var issues = await action.EvaluateAsync<string[]>("""
                button => {
                    const bounds = button.getBoundingClientRect(), icon = button.querySelector('i').getBoundingClientRect();
                    const range = document.createRange();
                    range.selectNodeContents(button.querySelector('.flow-action__label'));
                    const fragments = [...range.getClientRects()].filter(r => r.width > 0 && r.height > 0);
                    const inside = r => r.left >= bounds.left - 1 && r.right <= bounds.right + 1 && r.top >= bounds.top - 1 && r.bottom <= bounds.bottom + 1;
                    const overlaps = r => r.left < icon.right - 1 && r.right > icon.left + 1 && r.top < icon.bottom - 1 && r.bottom > icon.top + 1;
                    const issues = [];
                    if (fragments.some(r => !inside(r)) || !inside(icon)) issues.push('text or icon outside its command');
                    if (fragments.some(overlaps)) issues.push('icon overlaps text');
                    if (bounds.width < 44 || bounds.height < 44 || !button.contains(document.elementFromPoint((bounds.left + bounds.right) / 2, (bounds.top + bounds.bottom) / 2))) issues.push('command is too small or covered');
                    return issues;
                }
                """);
            Assert.True(issues.Length == 0, $"{width}x{height}, 200% text: {string.Join("; ", issues)}");
        }
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task Dashboard_NameSurvivesPhotoReplacementAndBrokenImages(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.RouteAsync("**/dashboard-white.svg", route => route.FulfillAsync(new()
        {
            ContentType = "image/svg+xml", Body = """<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024"><path fill="white" d="M0 0h1024v1024H0z"/></svg>"""
        }));
        await context.RouteAsync("**/dashboard-broken.jpg", route => route.FulfillAsync(new() { Status = 404 }));
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete', JSON.stringify({Name:'Dashboard Athlete', Biomarkers:[]}));
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/dashboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Dashboard Athlete", Level = 1, Exact = true })).ToBeVisibleAsync();
        await page.EvaluateAsync("window.__dashboardHeading = document.getElementById('athleteDashboardTitle')");
        foreach (var (name, photo) in new[]
        {
            ("Alexandria-Cassandra van der Westhuizen Montgomery-Smythe", "/dashboard-white.svg"),
            ("Another Athlete", "/dashboard-broken.jpg"),
            ("A Missing Portrait", "")
        })
        {
            await page.EvaluateAsync("""
                async ({name, photo}) => {
                    await window.playAthleteFlow.renderAthleteDashboardHeader(
                        {Name:name, DisplayName:name, ProfilePic:photo, Biomarkers:[]},
                        {titleElement:document.getElementById('athleteDashboardTitle'), frameElement:document.getElementById('athleteDashboardPicture')});
                }
                """, new { name, photo });
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = name, Level = 1, Exact = true })).ToBeVisibleAsync();
            Assert.True(await page.EvaluateAsync<bool>("""
                () => {
                    const title = document.getElementById('athleteDashboardTitle');
                    const card = title.closest('.play-dashboard-card').getBoundingClientRect();
                    const rect = title.getBoundingClientRect();
                    const image = document.querySelector('#athleteDashboardPicture img');
                    return window.__dashboardHeading === title && !title.closest('#athleteDashboardPicture')
                        && rect.top >= card.top && rect.bottom <= card.bottom + 1
                        && title.scrollWidth <= title.clientWidth && image.complete && image.naturalWidth > 0;
                }
                """));
        }
    }
}
