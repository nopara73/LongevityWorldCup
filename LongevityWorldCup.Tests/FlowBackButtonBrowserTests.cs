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
    [InlineData("/pheno-age", 1024, 768)]
    [InlineData("/pheno-age", 1280, 720)]
    [InlineData("/pheno-age", 1440, 900)]
    [InlineData("/bortz-age", 1440, 900)]
    [InlineData("/apply?fake=1", 1440, 900)]
    [InlineData("/select-athlete", 1280, 900)]
    [InlineData("/edit-profile", 1440, 900)]
    [InlineData("/pheno-age", 320, 640)]
    [InlineData("/pheno-age", 390, 844)]
    [InlineData("/apply?fake=1", 768, 1024)]
    [InlineData("/bortz-age", 844, 390)]
    public async Task Back_KeepsItsContentInsideTheActualInlineOrDockedControl(string path, int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(FlowActionDockBrowserTests.FlowAuditStateScript);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await NavigateAndSettleAsync(page, path);
        var selector = BackSelector(path);
        await page.Locator(selector).WaitForAsync();
        await SettleAsync(page);
        await AssertBackFitsAsync(page, selector);

        await DoubleTextAsync(page);
        await page.Locator(selector).EvaluateAsync("back => back.scrollIntoView({block: 'center'})");
        await SettleAsync(page);
        await AssertBackFitsAsync(page, selector);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    [InlineData("/apply?fake=1")]
    [InlineData("/select-athlete")]
    [InlineData("/edit-profile")]
    public async Task DesktopBack_StaysCentredAndFocusedAcrossRealDockTransitions(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 1400 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(FlowActionDockBrowserTests.FlowAuditStateScript);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        var selector = BackSelector(path);
        await page.Locator(selector).WaitForAsync();
        await page.Locator(selector).FocusAsync();
        await SettleAsync(page);
        await ExpectDockedAsync(page, selector, false);
        await AssertBackFitsAsync(page, selector);

        foreach (var height in new[] { 400, 1400, 400, 1400 })
        {
            await page.SetViewportSizeAsync(1440, height);
            await page.EvaluateAsync("window.scrollTo(0, 0)");
            await ExpectDockedAsync(page, selector, height == 400);
            await Assertions.Expect(page.Locator(selector)).ToBeFocusedAsync();
            await AssertBackFitsAsync(page, selector);
        }
    }

    [Fact]
    public async Task InlineBack_UsesVisibleActionsForItsCompactBoundsAndWrapsLongCaptions()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1024, Height = 1400 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/pheno-age");
        const string back = "#bioageStepOneBackButton";
        await ExpectDockedAsync(page, back, false);

        // Exercise the real action stack and visibility observer, without a
        // fixture that pre-sets the spacing or content bounds being asserted.
        foreach (var state in new[] { "hidden", "disabled-hidden", "pending", "removed" })
        {
            await page.EvaluateAsync(
                """
                state => {
                    const stack = document.querySelector('#lwcStepOneActions');
                    let primary = stack.querySelector('#lwcToStep2Btn');
                    if (state === 'hidden') primary.hidden = true;
                    if (state === 'disabled-hidden' || state === 'pending') {
                        primary.hidden = false;
                        stack.classList.add('flow-action-stack--hide-disabled-primary');
                        primary.disabled = true;
                        if (state === 'pending') primary.textContent = 'Submitting...';
                    }
                    if (state === 'removed') primary.remove();
                }
                """, state);
            await page.WaitForFunctionAsync("() => document.querySelector('#lwcStepOneActions').classList.contains('flow-action-stack--single-back-action')");
            var dimensions = await page.Locator(back).EvaluateAsync<double[]>(
                "back => { const b = back.getBoundingClientRect(), s = back.parentElement.getBoundingClientRect(); return [b.width, Math.abs((b.left + b.right - s.left - s.right) / 2)]; }");
            Assert.InRange(dimensions[0], 44, 176);
            Assert.InRange(dimensions[1], 0, 1);
            await AssertBackFitsAsync(page, back);
        }

        await page.Locator(back + " .flow-action__label").EvaluateAsync("label => label.textContent = 'Back to the previous application stage'");
        await DoubleTextAsync(page);
        await AssertBackFitsAsync(page, back);
        await page.EvaluateAsync(
            """
            () => {
                const stack = document.querySelector('#lwcStepOneActions');
                for (let index = 0; index < 2; index++) {
                    const action = document.createElement('button');
                    action.type = 'button';
                    action.className = 'option-button flow-action';
                    action.innerHTML = '<span class="flow-action__label">Another action</span>';
                    stack.append(action);
                }
            }
            """);
        await page.WaitForFunctionAsync("() => !document.querySelector('#lwcStepOneActions').classList.contains('flow-action-stack--single-back-action')");
        await AssertBackFitsAsync(page, back);
    }

    [Fact]
    public async Task EditProfile_BackStaysCompactBeforeAndAfterAChangeAndPendingSubmission()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 1400 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(FlowActionDockBrowserTests.FlowAuditStateScript);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/edit-profile");
        const string back = ".edit-profile-actions .back-button";
        await page.Locator(back).WaitForAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.edit-profile-actions').classList.contains('flow-action-stack--single-back-action')");
        await AssertBackFitsAsync(page, back);
        await page.Locator("#personalLinkInput").FillAsync("https://example.test/changed");
        await Assertions.Expect(page.Locator("#submitButton")).ToBeEnabledAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('.edit-profile-actions').classList.contains('flow-action-stack--single-back-action')");
        await AssertBackFitsAsync(page, back);
        // No submission is sent. Reproduce the existing pending control state.
        await page.Locator("#submitButton").EvaluateAsync("button => { button.disabled = true; button.textContent = 'Submitting...'; }");
        await page.WaitForFunctionAsync("() => document.querySelector('.edit-profile-actions').classList.contains('flow-action-stack--single-back-action')");
        await AssertBackFitsAsync(page, back);
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task CalculatorSecondStep_BackKeepsItsContentAndReturnsToTheFirstStep(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await FlowActionDockBrowserTests.FillBioageStepOneAsync(page, "2026-06-19");
        await AssertBackFitsAsync(page, "#lwcToStep1Btn");
        await DoubleTextAsync(page);
        await AssertBackFitsAsync(page, "#lwcToStep1Btn");
        await page.Locator("#lwcToStep1Btn").ClickAsync();
        await Assertions.Expect(page.Locator("#lwc-step-1")).ToBeVisibleAsync();
        await AssertBackFitsAsync(page, "#bioageStepOneBackButton");
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task ApplicationStages_KeepBackUsableWithChangingPrimaryActions(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply?fake=1");
        for (var scale = 1; scale <= 2; scale++)
        {
            if (scale == 2) await DoubleTextAsync(page);
            for (var stage = 1; stage <= 6; stage++)
            {
                await page.EvaluateAsync("stage => { currentStage = stage; goToStage(stage); }", stage);
                await AssertBackFitsAsync(page, "#backButton");
            }
        }
    }

    private static string BackSelector(string path) => path switch
    {
        "/apply?fake=1" => "#backButton",
        "/select-athlete" => "#playSelectionBackBtn",
        "/edit-profile" => ".edit-profile-actions .back-button",
        _ => "#bioageStepOneBackButton"
    };

    private static Task SettleAsync(IPage page) => page.EvaluateAsync(
        "async () => { await document.fonts.ready; for (let i = 0; i < 4; i++) { window.LwcFlowActionDock?.refreshNow(); await new Promise(requestAnimationFrame); } }");

    private static Task ExpectDockedAsync(IPage page, string selector, bool docked) => page.WaitForFunctionAsync(
        "arg => { window.LwcFlowActionDock.refreshNow(); return document.querySelector(arg.selector).closest('.flow-action-stack').classList.contains('flow-action-stack--docked') === arg.docked; }",
        new { selector, docked });

    private static async Task DoubleTextAsync(IPage page)
    {
        var before = await page.EvaluateAsync<double[]>(
            """
            async () => {
                const elements = [document.body, ...document.body.querySelectorAll('*')].filter(e => e instanceof HTMLElement);
                const sizes = elements.map(e => [e, parseFloat(getComputedStyle(e).fontSize)]);
                const labels = [...document.querySelectorAll('.back-button .flow-action__label')];
                const before = labels.map(e => parseFloat(getComputedStyle(e).fontSize));
                for (const [e, size] of sizes) e.style.setProperty('font-size', `${size * 2}px`, 'important');
                await document.fonts.ready;
                return before;
            }
            """);
        // Visible controls transition their font size; measure the final text.
        await WaitForFiniteAnimationsAsync(page);
        var ratios = await page.EvaluateAsync<double[]>("""
            before => [...document.querySelectorAll('.back-button .flow-action__label')]
                .map((element, index) => parseFloat(getComputedStyle(element).fontSize) / before[index])
            """, before);
        Assert.NotEmpty(ratios);
        Assert.All(ratios, ratio => Assert.InRange(ratio, 1.999, 2.001));
        await SettleAsync(page);
    }

    private static async Task AssertBackFitsAsync(IPage page, string selector)
    {
        await SettleAsync(page);
        var problems = await page.Locator(selector).EvaluateAsync<string[]>(
            """
            back => {
                const problems = [];
                const box = back.getBoundingClientRect();
                const stack = back.closest('.flow-action-stack');
                const container = stack.getBoundingClientRect();
                const inline = !stack.classList.contains('flow-action-stack--docked');
                const iconElement = back.querySelector(':scope > i, :scope > .flow-action__icon');
                const labelElement = back.querySelector('.flow-action__label');
                if (back.querySelectorAll(':scope > i, :scope > .flow-action__icon').length !== 1 || back.querySelectorAll('.flow-action__label').length !== 1) problems.push('duplicate or missing arrow/label');
                const icon = iconElement.getBoundingClientRect();
                const label = labelElement.getBoundingClientRect();
                const range = document.createRange();
                range.selectNodeContents(labelElement);
                const glyphs = [...range.getClientRects()].filter(r => r.width > 0 && r.height > 0);
                if (box.width < 44 || box.height < 44) problems.push('target smaller than 44px');
                if (box.left < container.left - 1 || box.right > container.right + 1 || box.width > innerWidth + 1) problems.push('button exceeds its action group');
                if (icon.width < 1 || !glyphs.length || getComputedStyle(iconElement, '::before').content === 'none') problems.push('arrow or text has no rendered content');
                for (const r of [icon, ...glyphs]) {
                    if (r.left < box.left + 1 || r.right > box.right - 1 || r.top < box.top + 1 || r.bottom > box.bottom - 1) problems.push('arrow or text escapes the button');
                    for (let ancestor = back.parentElement; ancestor; ancestor = ancestor.parentElement) {
                        const style = getComputedStyle(ancestor), bounds = ancestor.getBoundingClientRect();
                        if (/hidden|clip/.test(style.overflowX) && (r.left < bounds.left - 1 || r.right > bounds.right + 1)) problems.push('content clipped by ancestor');
                    }
                }
                if (glyphs.some(r => r.left < icon.right - 1 && r.right > icon.left + 1 && r.top < icon.bottom && r.bottom > icon.top)) problems.push('arrow overlaps text');
                if (inline && innerWidth >= 769) {
                    const gap = label.left - icon.right;
                    const font = parseFloat(getComputedStyle(back).fontSize);
                    if (gap < 0 || gap > font * 0.65 + 1) problems.push(`arrow-label gap ${gap}px does not follow the text size`);
                    if (Math.abs((icon.left + label.right - box.left - box.right) / 2) > 2) problems.push('arrow and label are not centred together');
                }
                const visible = [...stack.querySelectorAll(':scope > .flow-action')].filter(e => e.checkVisibility());
                if (visible.length === 1 && inline && innerWidth >= 769 && box.width > 177) problems.push('lone Back stretches beyond 11rem');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{page.Url} {selector}: {string.Join("; ", problems)}");
    }
}
