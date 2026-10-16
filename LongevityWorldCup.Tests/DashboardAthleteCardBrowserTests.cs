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
    [InlineData(320, 640, false, 1)]
    [InlineData(844, 390, false, 1)]
    [InlineData(1280, 600, true, 1)]
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
                    actionsBox.top, actions.classList.contains('flow-action-stack--docked') ? 1 : 0, backLines, iconOverlap];
            }
            """);
        var scenario = $"{width}x{height}, {textScale * 100}% text, {(isPro ? "pro" : "amateur")}";
        Assert.True(layout[0] >= layout[4] - 1 && layout[1] <= layout[5] + 1 && layout[2] >= layout[6] - 1 && layout[3] <= layout[7] + 1,
            $"{scenario}: the athlete name should sit on the portrait as its caption.");
        Assert.True(Math.Abs(layout[3] - layout[7]) <= 2, $"{scenario}: the caption should rest on the portrait's bottom edge.");
        if (layout[9] == 1)
            Assert.True(layout[7] <= layout[8] + 1, $"{scenario}: the docked actions cover the portrait ({layout[7]} > {layout[8]}).");
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
            await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#athleteSelectionPanel")).ToBeVisibleAsync();
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
