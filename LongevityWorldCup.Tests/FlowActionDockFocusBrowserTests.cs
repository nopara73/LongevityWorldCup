using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FlowActionDockFocusBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DomMoves_PreserveFocusScrollAndSingleActivation(bool fallback)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.NoPreference
        });
        if (fallback) await context.AddInitScriptAsync("Element.prototype.moveBefore = undefined; Element.prototype.checkVisibility = undefined");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/pheno-age");
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock && document.documentElement.classList.contains('flow-action-dock-ready')");
        await page.EvaluateAsync("""
            () => {
                const stack = document.getElementById('lwcStepOneActions');
                stack.dataset.flowDock = 'off';
                window.LwcFlowActionDock.refreshNow();
                const back = stack.querySelector('.back-button');
                back.onclick = () => window.testActivations = (window.testActivations || 0) + 1;
                back.focus({ preventScroll: true });
            }
            """);
        var scroll = await page.EvaluateAsync<double>("window.scrollY");
        var back = page.Locator("#bioageStepOneBackButton");
        foreach (var mode in new[] { "always", "off", "always", "off" })
        {
            await page.EvaluateAsync("mode => { document.getElementById('lwcStepOneActions').dataset.flowDock = mode; window.LwcFlowActionDock.refreshNow(); }", mode);
            Assert.Equal(mode == "always", await page.Locator("#lwcStepOneActions").EvaluateAsync<bool>("s => s.classList.contains('flow-action-stack--docked')"));
            await Assertions.Expect(back).ToBeFocusedAsync();
            Assert.InRange(await page.EvaluateAsync<double>("window.scrollY"), scroll - 1, scroll + 1);
            Assert.True(await back.EvaluateAsync<bool>("b => b.getBoundingClientRect().height > 0 && !b.closest('.flow-action-stack--dock-entering')"));
        }
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal(1, await page.EvaluateAsync<int>("window.testActivations"));
    }

    [Theory]
    [InlineData("assigned")]
    [InlineData("assignedThenBlurred")]
    [InlineData("removed")]
    [InlineData("disabled")]
    [InlineData("ariaDisabled")]
    [InlineData("hidden")]
    [InlineData("ancestorHidden")]
    [InlineData("ancestorTransparent")]
    [InlineData("ancestorContentHidden")]
    [InlineData("inert")]
    [InlineData("ariaHidden")]
    public async Task FallbackMove_DoesNotRestoreAnInappropriateOrSupersededTarget(string mutation)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("Element.prototype.moveBefore = undefined");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/pheno-age");
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock && document.documentElement.classList.contains('flow-action-dock-ready')");
        var result = await page.EvaluateAsync<string[]>("""
            mutation => {
                const stack = document.getElementById('lwcStepOneActions');
                stack.dataset.flowDock = 'off';
                window.LwcFlowActionDock.refreshNow();
                const target = stack.querySelector('.back-button');
                const outside = document.createElement('button');
                outside.id = 'newFocus';
                outside.textContent = 'New focus';
                outside.style.cssText = 'position:fixed;top:0;left:0';
                document.body.append(outside);
                target.focus({ preventScroll: true });
                let restorations = 0;
                target.addEventListener('focus', () => restorations++);
                const insert = document.body.insertBefore;
                document.body.insertBefore = function(node, before) {
                    const result = insert.call(this, node, before);
                    if (node !== stack) return result;
                    if (mutation === 'assigned' || mutation === 'assignedThenBlurred') outside.focus({ preventScroll: true });
                    if (mutation === 'assignedThenBlurred') outside.blur();
                    if (mutation === 'removed') target.remove();
                    if (mutation === 'disabled') target.disabled = true;
                    if (mutation === 'ariaDisabled') target.setAttribute('aria-disabled', 'true');
                    if (mutation === 'hidden') target.hidden = true;
                    if (mutation === 'ancestorHidden') stack.style.visibility = 'hidden';
                    if (mutation === 'ancestorTransparent') stack.style.opacity = '0';
                    if (mutation === 'ancestorContentHidden') stack.style.contentVisibility = 'hidden';
                    if (mutation === 'inert') stack.inert = true;
                    if (mutation === 'ariaHidden') stack.setAttribute('aria-hidden', 'true');
                    return result;
                };
                stack.dataset.flowDock = 'always';
                try { window.LwcFlowActionDock.refreshNow(); }
                finally { document.body.insertBefore = insert; }
                return [document.activeElement?.id || '', String(restorations), String(stack.classList.contains('flow-action-stack--docked'))];
            }
            """, mutation);
        Assert.NotEqual("bioageStepOneBackButton", result[0]);
        if (mutation == "assigned") Assert.Equal("newFocus", result[0]);
        Assert.Equal("0", result[1]);
        Assert.Equal("true", result[2]);
    }
}
