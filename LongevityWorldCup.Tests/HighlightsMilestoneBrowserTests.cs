using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HighlightsMilestoneBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, ColorScheme.Light)]
    [InlineData(390, 844, ColorScheme.Dark)]
    public async Task HighlightsPage_LetsMilestonesStandOutFromJoins(int width, int height, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/events");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#eventsTable tr.main-row[data-event-type-name=\"Joined\"]') && document.querySelector('#eventsTable tr.main-row[data-event-type-name=\"BiologicalAgeImproved\"], #eventsTable tr.main-row[data-event-type-name=\"NewRank\"], #eventsTable tr.main-row[data-event-type-name=\"DonationReceived\"]')");

        var styles = await page.EvaluateAsync<string[]>(
            """
            () => {
                const join = document.querySelector('#eventsTable tr.main-row[data-event-type-name="Joined"]');
                const milestone = document.querySelector('#eventsTable tr.main-row[data-event-type-name="BiologicalAgeImproved"], #eventsTable tr.main-row[data-event-type-name="NewRank"], #eventsTable tr.main-row[data-event-type-name="DonationReceived"]');
                const describe = row => {
                    const message = getComputedStyle(row.querySelector('.event-message-cell'));
                    const cell = getComputedStyle(row.querySelector('td.event-message'));
                    const avatarCell = getComputedStyle(row.querySelector('td.col-avatar'));
                    return [cell.backgroundColor, message.fontSize, message.fontWeight, avatarCell.boxShadow];
                };
                return [...describe(join), ...describe(milestone)];
            }
            """);
        var scenario = $"{width}x{height} {theme}";
        Assert.NotEqual(styles[0], styles[4]);
        Assert.True(double.Parse(styles[5].Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture)
            > double.Parse(styles[1].Replace("px", ""), System.Globalization.CultureInfo.InvariantCulture),
            $"{scenario}: milestone text ({styles[5]}) should read larger than a join ({styles[1]}).");
        Assert.True(int.Parse(styles[6]) > int.Parse(styles[2]), $"{scenario}: milestone text should be heavier than a join.");
        Assert.True(styles[7] != "none" && styles[3] == "none", $"{scenario}: only milestones carry the accent bar ({styles[3]} / {styles[7]}).");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
    }
}
