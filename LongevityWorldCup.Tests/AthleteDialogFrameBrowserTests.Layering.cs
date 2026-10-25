using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class AthleteDialogFrameBrowserTests
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task DialogControls_KeepTheirWholeHitAreaAboveTheStickyHeader(int width)
    {
        await using var context = await NewContextAsync(width, 844, dark: false);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/");
        var opener = page.Locator(".leaderboard tbody a.athlete-name").First;
        await opener.ScrollIntoViewIfNeededAsync();
        await page.Locator("#site-sticky-header.visible").WaitForAsync();
        await opener.ClickAsync();
        await WaitForProfileAsync(page);

        var obscured = await page.EvaluateAsync<string[]>("""
            () => ['#closeAthleteDetailsModal', '.modal-content > .athlete-steps [data-step=next]']
                .filter(selector => {
                    const control = document.querySelector(selector);
                    const rect = control.getBoundingClientRect();
                    return rect.width < 44 || rect.height < 44
                        || [[.5, .5], [.15, .25], [.85, .25], [.15, .75], [.85, .75]].some(([x, y]) =>
                            !control.contains(document.elementFromPoint(rect.left + rect.width * x, rect.top + rect.height * y)));
                })
            """);
        Assert.Empty(obscured);
        await page.Locator(".modal-content > .athlete-steps [data-step=next]").ClickAsync();
        await WaitForProfileAsync(page);
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
    }
}
