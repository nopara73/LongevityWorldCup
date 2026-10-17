using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FlowStepActionsRowBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", ".lwc-step-actions", 1440, 900)]
    [InlineData("/bortz-age", ".lwc-step-actions", 1280, 720)]
    [InlineData("/apply", ".flow-action-stack", 1440, 900)]
    [InlineData("/select-athlete", ".play-athlete-actions", 1440, 900)]
    [InlineData("/pheno-age", ".lwc-step-actions", 1024, 768)]
    public async Task DesktopStep_SetsACompactBackBesideItsAction(string path, string stackSelector, int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow?.()");

        var layout = await page.EvaluateAsync<string[]>(
            """
            selector => {
                const stack = [...document.querySelectorAll(selector)].find(s => s.getClientRects().length
                    && s.querySelector(':scope > .back-button') && !s.classList.contains('flow-action-stack--docked'));
                if (!stack) return ['no inline step actions'];
                const back = stack.querySelector(':scope > .back-button').getBoundingClientRect();
                const action = stack.querySelector(':scope > .flow-action:not(.back-button)').getBoundingClientRect();
                const icon = stack.querySelector(':scope > .back-button > i').getBoundingClientRect();
                const label = stack.querySelector(':scope > .back-button .flow-action__label');
                const range = document.createRange();
                range.selectNodeContents(label);
                const text = range.getBoundingClientRect();
                const problems = [];
                if (Math.abs(back.top - action.top) > 1 || Math.abs(back.bottom - action.bottom) > 1) problems.push('Back and the action do not share one row');
                if (back.right > action.left) problems.push('Back is not to the left of the action');
                if (back.width > action.width * 0.6) problems.push(`Back (${back.width}px) is not compact beside the action (${action.width}px)`);
                if (text.left - icon.right > 24) problems.push(`Back's arrow sits ${text.left - icon.right}px from its label`);
                return problems;
            }
            """, stackSelector);
        Assert.True(layout.Length == 0, $"{path} {width}x{height}: {string.Join("; ", layout)}");
    }

    [Theory]
    [InlineData("/pheno-age", 390, 844)]
    [InlineData("/apply", 390, 844)]
    public async Task PhoneStep_KeepsTheDockedRow(string path, int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow?.()");
        await page.WaitForFunctionAsync("() => document.querySelector('.flow-action-stack--docked .back-button')");
        var sameRow = await page.EvaluateAsync<bool>(
            """
            () => {
                const stack = document.querySelector('.flow-action-stack--docked');
                const back = stack.querySelector('.back-button').getBoundingClientRect();
                const action = stack.querySelector('.flow-action:not(.back-button)').getBoundingClientRect();
                return Math.abs(back.top - action.top) <= 1 && back.right <= action.left;
            }
            """);
        Assert.True(sameRow, $"{path}: the phone dock keeps Back beside the action.");
    }
}
