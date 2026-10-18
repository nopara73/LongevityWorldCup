using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteDialogColumnBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390)]
    [InlineData(360)]
    [InlineData(320)]
    public async Task PhoneProfile_RankTilesShareTheSectionsColumnAndOneRow(int width)
    {
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = ColorScheme.Dark,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/michael-lustgarten", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("""
            () => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'michael-lustgarten'
                && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')
                && document.querySelectorAll('#athleteRankings a').length >= 2
            """);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const ranks = document.getElementById('athleteRankings').getBoundingClientRect();
                const section = document.querySelector('#detailsModal .athlete-info').getBoundingClientRect();
                const tiles = [...document.querySelectorAll('#athleteRankings a')].map(tile => tile.getBoundingClientRect());
                return [ranks.left, ranks.right, section.left, section.right, Math.max(...tiles.map(t => t.top)) - Math.min(...tiles.map(t => t.top))];
            }
            """);
        Assert.True(Math.Abs(layout[0] - layout[2]) <= 1 && Math.Abs(layout[1] - layout[3]) <= 1,
            $"{width}px: the rank tiles span {layout[0]:F0}-{layout[1]:F0}px but the profile sections {layout[2]:F0}-{layout[3]:F0}px.");
        Assert.True(layout[4] <= 1, $"{width}px: the rank tiles should share one row.");
    }
}
