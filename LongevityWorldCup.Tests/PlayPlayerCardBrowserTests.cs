using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayPlayerCardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string ProAthlete = """
        const athlete = {
            Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete', PersonalLink: 'https://example.test/browser-test-athlete',
            DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: [{ Date: '2026-06-19', Hba1cMmolMol: 35 }]
        };
        sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
        localStorage.setItem('selectedAthleteName', athlete.Name);
        localStorage.setItem('hasApplication', 'true');
        """;

    [Theory]
    [InlineData("/select-athlete", "#athleteSelectionPanel", 1440, 900, false)]
    [InlineData("/select-athlete", "#athleteSelectionPanel", 1024, 768, true)]
    [InlineData("/dashboard", "#athleteDashboardPanel", 1440, 900, false)]
    [InlineData("/dashboard", "#athleteDashboardPanel", 1280, 720, true)]
    public async Task Desktop_PortraitHeadsOneCardThatHoldsTheScreensControls(string path, string panel, int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        if (path == "/dashboard") await context.AddInitScriptAsync(ProAthlete);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync(
            "panel => !document.body.classList.contains('play-route-hydrating') && document.querySelector(panel)?.hidden === false && document.querySelector(panel + ' img.illustration')?.complete",
            panel);
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");

        var layout = await page.EvaluateAsync<double[]>(
            """
            panel => {
                const card = document.querySelector(panel);
                const box = card.getBoundingClientRect();
                const style = getComputedStyle(card);
                const frame = card.querySelector('.athlete-picture-frame').getBoundingClientRect();
                const controls = card.querySelector(panel === '#athleteSelectionPanel' ? '#playAthleteInput' : '.play-dashboard-actions .flow-action').getBoundingClientRect();
                return [parseFloat(style.borderTopWidth), style.backgroundColor === 'rgba(0, 0, 0, 0)' ? 0 : 1,
                    box.left, box.right, box.top, box.bottom, frame.left, frame.right, frame.top, frame.width, frame.height,
                    controls.left, controls.right, controls.top, controls.bottom];
            }
            """, panel);
        var scenario = $"{path} {width}x{height}";
        Assert.True(layout[0] >= 1 && layout[1] == 1, $"{scenario}: the screen should be one bordered card.");
        Assert.True(Math.Abs(layout[6] - layout[2]) <= 2 && Math.Abs(layout[7] - layout[3]) <= 2 && Math.Abs(layout[8] - layout[4]) <= 2,
            $"{scenario}: the portrait should be the card's full-width header.");
        Assert.True(layout[9] >= layout[10] * 1.4, $"{scenario}: the header portrait should be wide, not a {layout[9]:F0}x{layout[10]:F0} square.");
        Assert.True(layout[11] > layout[2] && layout[12] < layout[3] && layout[13] > layout[8] + layout[10] - 1 && layout[14] < layout[5],
            $"{scenario}: the screen's controls should sit in the card's body under the portrait.");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }
}
