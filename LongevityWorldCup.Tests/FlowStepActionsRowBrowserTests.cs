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
    [InlineData("/apply", ".flow-action-stack", 1440, 1000)]
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
        await NavigateDockReadyAsync(page, path);
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
                if (!stack.firstElementChild.matches('.back-button')) problems.push('DOM order does not start with Back');
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
    [InlineData("/pheno-age", 390, 844, 0)]
    [InlineData("/apply", 390, 844, 0)]
    // The identity step fits a 900px desktop; the privacy story is the stage that overflows it.
    [InlineData("/apply", 1440, 900, 3)]
    public async Task OverflowingStep_KeepsTheDockedRow(string path, int width, int height, int stage)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await NavigateDockReadyAsync(page, path);
        if (stage > 0)
            await page.EvaluateAsync("stage => { currentStage = stage; goToStage(stage); }", stage);
        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow?.()");
        await page.WaitForFunctionAsync("() => document.querySelector('.flow-action-stack--docked .back-button')");
        var sameRow = await page.EvaluateAsync<bool>(
            """
            () => {
                const stack = document.querySelector('.flow-action-stack--docked');
                const back = stack.querySelector('.back-button').getBoundingClientRect();
                const action = stack.querySelector('.flow-action:not(.back-button)').getBoundingClientRect();
                return stack.firstElementChild.matches('.back-button')
                    && Math.abs(back.top - action.top) <= 1 && back.right <= action.left;
            }
            """);
        Assert.True(sameRow, $"{path} {width}x{height}: the overflow dock keeps Back beside the action.");
    }

    [Theory]
    [InlineData("/pheno-age", "#lwcStepOneActions")]
    [InlineData("/bortz-age", "#lwcStepOneActions")]
    [InlineData("/apply?fake=1", ".convergence-actions")]
    [InlineData("/select-athlete", ".play-athlete-actions")]
    public async Task StepOrderAndFocus_SurviveInlineDockedAndInlineAgain(string path, string selector)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 1000 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateDockReadyAsync(page, path);
        var stack = page.Locator(selector).First;
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await Assertions.Expect(stack).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("--docked"));
        var back = stack.Locator(".back-button");
        await back.FocusAsync();
        await Assertions.Expect(back).ToBeFocusedAsync();

        foreach (var height in new[] { 460, 1000 })
        {
            await page.SetViewportSizeAsync(1280, height);
            await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
            if (height == 460)
                await Assertions.Expect(stack).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("--docked"));
            else
                await Assertions.Expect(stack).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("--docked"));
            await Assertions.Expect(back).ToBeFocusedAsync();
            Assert.True(await stack.EvaluateAsync<bool>("s => s.firstElementChild.matches('.back-button')"));
        }

        var primary = stack.Locator(":scope > .flow-action:not(.back-button)");
        if (await primary.IsEnabledAsync())
        {
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(primary).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Shift+Tab");
            await Assertions.Expect(back).ToBeFocusedAsync();
        }
    }

    [Theory]
    [InlineData(1024, false)]
    [InlineData(1280, true)]
    [InlineData(1440, true)]
    public async Task DesktopStep_WrapsLongLabelsWithoutClippingOrReversingOrder(int width, bool doubleText)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateDockReadyAsync(page, "/pheno-age");
        await page.EvaluateAsync("""
            doubleText => {
                const stack = document.getElementById('lwcStepOneActions');
                stack.dataset.flowDock = 'off';
                stack.querySelector('.flow-action:not(.back-button) .flow-action__label').textContent = 'Continue to review my saved biological age result';
                if (doubleText) {
                    const elements = [stack, ...stack.querySelectorAll('*')];
                    const sizes = elements.map(e => parseFloat(getComputedStyle(e).fontSize));
                    elements.forEach((e, i) => e.style.setProperty('font-size', `${sizes[i] * 2}px`, 'important'));
                }
                window.LwcFlowActionDock.refreshNow();
            }
            """, doubleText);
        await page.EvaluateAsync("async () => { await document.fonts.ready; for(let i=0;i<4;i++) await new Promise(requestAnimationFrame); }");
        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const stack = document.getElementById('lwcStepOneActions');
                const buttons = [...stack.children];
                const issues = [];
                if (!buttons[0].matches('.back-button')) issues.push('Back is not first in the DOM');
                const [back, primary] = buttons.map(b => b.getBoundingClientRect());
                if (!(back.bottom <= primary.top + 1 || back.right <= primary.left)) issues.push('Actions overlap or read in the opposite order');
                for (const button of buttons) {
                    const box = button.getBoundingClientRect();
                    const range = document.createRange();
                    range.selectNodeContents(button.querySelector('.flow-action__label'));
                    for (const text of range.getClientRects()) {
                        if (text.left < box.left || text.right > box.right + 1 || text.top < box.top || text.bottom > box.bottom + 1) issues.push('Visible label is clipped');
                    }
                }
                if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('Horizontal overflow');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, string.Join("; ", problems));
    }

    [Theory]
    [InlineData("/apply")]
    [InlineData("/select-athlete")]
    public async Task DisabledPrimary_HasADistinctBoundaryAndIsSkippedByKeyboard(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateDockReadyAsync(page, path);
        // Locate the ready task's pair rather than another hidden panel's actions.
        var primary = page.Locator("[data-flow-dock]:visible > .flow-action:not(.back-button)").First;
        var back = page.Locator("[data-flow-dock]:visible > .back-button").First;
        await Assertions.Expect(primary).ToBeDisabledAsync();
        Assert.Equal("dashed", await primary.EvaluateAsync<string>("b => getComputedStyle(b).borderTopStyle"));
        Assert.Equal("solid", await back.EvaluateAsync<string>("b => getComputedStyle(b).borderTopStyle"));
        await back.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(primary).Not.ToBeFocusedAsync();
        Assert.False(await primary.EvaluateAsync<bool>("b => b.getBoundingClientRect().width === 0"));
    }

    private static async Task NavigateDockReadyAsync(IPage page, string path)
    {
        await NavigateAndSettleAsync(page, path);
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock && document.documentElement.classList.contains('flow-action-dock-ready')");
        await page.Locator("[data-flow-dock]:visible").First.WaitForAsync();
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
    }

    [Theory]
    [InlineData("/pheno-age", 320)]
    [InlineData("/pheno-age", 390)]
    [InlineData("/apply", 320)]
    [InlineData("/apply", 390)]
    public async Task PhoneStep_ActualDoubleTextKeepsBackWholeAndItsIconSeparate(string path, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce, IsMobile = true, HasTouch = true
        });
        var page = await context.NewPageAsync();
        await NavigateDockReadyAsync(page, path);
        await page.WaitForFunctionAsync("() => document.querySelector('.flow-action-stack--docked .back-button')");
        var result = await page.EvaluateAsync<string[]>("""
            () => {
                const stack = document.querySelector('.flow-action-stack--docked');
                const elements = [stack, ...stack.querySelectorAll('*')];
                const sizes = elements.map(e => parseFloat(getComputedStyle(e).fontSize));
                elements.forEach((e,i) => e.style.setProperty('font-size', `${sizes[i] * 2}px`, 'important'));
                window.LwcFlowActionDock.refreshNow();
                const back = stack.querySelector('.back-button');
                const label = back.querySelector('.flow-action__label');
                const range = document.createRange();
                range.selectNodeContents(label);
                const glyphs = [...range.getClientRects()].filter(r=>r.width>0);
                const box = back.getBoundingClientRect();
                const icon = back.querySelector('i').getBoundingClientRect();
                const issues = [];
                if (new Set(glyphs.map(r=>Math.round(r.top))).size !== 1) issues.push('Back splits across lines');
                if (glyphs.some(r=>r.left < icon.right || r.right > box.right || r.top < box.top || r.bottom > box.bottom)) issues.push('Back label overlaps or clips');
                if (box.left < 0 || box.right > innerWidth) issues.push('Back overflows the viewport');
                for (const caption of stack.querySelectorAll('.flow-action__label')) {
                    const walker = document.createTreeWalker(caption, NodeFilter.SHOW_TEXT);
                    let node;
                    while(node = walker.nextNode()) for(const word of node.textContent.matchAll(/\S+/g)) {
                        const wordRange = document.createRange();
                        wordRange.setStart(node, word.index); wordRange.setEnd(node, word.index + word[0].length);
                        if(new Set([...wordRange.getClientRects()].map(r=>Math.round(r.top))).size > 1) issues.push('Split caption word: ' + word[0]);
                    }
                }
                return issues;
            }
            """);
        Assert.True(result.Length == 0, string.Join("; ", result));
    }

    [Theory]
    [InlineData("/pheno-age", 320)]
    [InlineData("/pheno-age", 390)]
    [InlineData("/bortz-age", 390)]
    public async Task Calculator_ActualDoubleTextKeepsDateAndProgressLabelsInsideTheirCells(string path, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce, IsMobile = true, HasTouch = true
        });
        var page = await context.NewPageAsync();
        await NavigateDockReadyAsync(page, path);
        await page.EvaluateAsync("""
            () => {
                const elements = [...document.querySelectorAll('body, body *')];
                const sizes = elements.map(e => parseFloat(getComputedStyle(e).fontSize));
                elements.forEach((e,i) => { if(sizes[i] > 0) e.style.setProperty('font-size', `${sizes[i] * 2}px`, 'important'); });
                window.LwcFlowActionDock.refreshNow();
            }
            """);
        await page.EvaluateAsync("async () => { await document.fonts.ready; for(let i=0;i<4;i++) await new Promise(requestAnimationFrame); }");
        var issues = await page.EvaluateAsync<string[]>("""
            () => {
                const issues = [];
                for(const label of document.querySelectorAll('#dobFieldset > label, .progress-container .stage-label')) {
                    const cell = label.matches('.stage-label') ? label.closest('.stage') : label;
                    const box = cell.getBoundingClientRect();
                    const range = document.createRange(); range.selectNodeContents(label);
                    if (innerWidth >= 390 && label.matches('.stage-label')
                        && new Set([...range.getClientRects()].filter(r=>r.width>0).map(r=>Math.round(r.top))).size > 1)
                        issues.push(label.textContent.trim() + ' wraps despite available progress width');
                    for(const text of range.getClientRects()) {
                        if(text.width && (text.left < box.left - 1 || text.right > box.right + 1)) issues.push(label.textContent.trim() + ' overflows its cell');
                    }
                }
                if(document.documentElement.scrollWidth > innerWidth + 1) issues.push('Horizontal overflow');
                return issues;
            }
            """);
        Assert.True(issues.Length == 0, string.Join("; ", issues));
    }
}
