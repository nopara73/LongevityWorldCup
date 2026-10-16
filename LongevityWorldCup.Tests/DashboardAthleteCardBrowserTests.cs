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
    [InlineData(1440, 900, true)]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, false)]
    [InlineData(844, 390, false)]
    [InlineData(820, 1180, true)]
    public async Task Dashboard_CaptionsThePortraitWithTheNameAndKeepsActionsWhole(int width, int height, bool isPro)
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
        var page = await context.NewPageAsync();
        await page.GotoAsync("/dashboard");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('athleteDashboardPanel')?.hidden === false && document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const title = document.getElementById('athleteDashboardTitle').getBoundingClientRect();
                const picture = document.getElementById('athleteDashboardPicture').getBoundingClientRect();
                const actions = document.getElementById('athleteDashboardActions');
                const actionsBox = actions.getBoundingClientRect();
                const back = document.querySelector('#playDashboardBackBtn .flow-action__label');
                const backLines = new Set(Array.from(back.getClientRects()).map(rect => Math.round(rect.top))).size;
                const icon = document.querySelector('#playDashboardBackBtn > i').getBoundingClientRect();
                const label = back.getBoundingClientRect();
                const iconOverlap = Math.max(0, Math.min(icon.right, label.right) - Math.max(icon.left, label.left));
                return [title.left, title.right, title.top, title.bottom, picture.left, picture.right, picture.top, picture.bottom,
                    actionsBox.top, actions.classList.contains('flow-action-stack--docked') ? 1 : 0, backLines, iconOverlap];
            }
            """);
        var scenario = $"{width}x{height} {(isPro ? "pro" : "amateur")}";
        Assert.True(layout[0] >= layout[4] - 1 && layout[1] <= layout[5] + 1 && layout[2] >= layout[6] - 1 && layout[3] <= layout[7] + 1,
            $"{scenario}: the athlete name should sit on the portrait as its caption.");
        Assert.True(Math.Abs(layout[3] - layout[7]) <= 2, $"{scenario}: the caption should rest on the portrait's bottom edge.");
        if (layout[9] == 1)
            Assert.True(layout[7] <= layout[8] + 1, $"{scenario}: the docked actions cover the portrait ({layout[7]} > {layout[8]}).");
        // Phone docks show the back action as an icon only; wherever the label shows, it stays on one line.
        Assert.True(layout[10] <= 1, $"{scenario}: \"Change athlete\" wraps onto {layout[10]} lines.");
        Assert.True(layout[11] <= 1, $"{scenario}: the back arrow overlaps the label by {layout[11]}px.");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }
}
