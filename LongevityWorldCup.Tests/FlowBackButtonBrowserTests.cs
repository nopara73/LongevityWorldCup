using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FlowBackButtonBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", 1440, 900)]
    [InlineData("/bortz-age", 1024, 768)]
    [InlineData("/apply", 1440, 900)]
    [InlineData("/select-athlete", 1280, 720)]
    public async Task DesktopBack_SetsItsArrowBesideItsLabel(string path, int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow?.()");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const backs = [...document.querySelectorAll('.flow-action-stack:not(.flow-action-stack--docked) > .back-button.flow-action--icon-left')]
                    .filter(button => button.getClientRects().length);
                if (!backs.length) return ['no inline Back'];
                const problems = [];
                for (const back of backs) {
                    const box = back.getBoundingClientRect();
                    const icon = back.querySelector(':scope > i').getBoundingClientRect();
                    const range = document.createRange();
                    range.selectNodeContents(back.querySelector('.flow-action__label'));
                    const label = range.getBoundingClientRect();
                    const gap = label.left - icon.right;
                    if (gap < 0 || gap > 16) problems.push(`arrow sits ${Math.round(gap)}px from its label`);
                    const groupCentre = (icon.left + label.right) / 2;
                    if (Math.abs(groupCentre - (box.left + box.right) / 2) > 3) problems.push('arrow and label are not centred together');
                }
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{path} {width}x{height}: {string.Join("; ", problems)}");
    }
}
