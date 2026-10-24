using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FormControlsBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IBrowserContext> ContextAsync()
    {
        var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }

    [Fact]
    public async Task Labels_FollowInitialInvalidStateSharedAssociationsAndRemovedFields()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply");
        await page.WaitForFunctionAsync("() => !!window.LwcFormControls");
        await page.Locator("#name").FillAsync("Review fixture");
        await Assertions.Expect(page.Locator("label[for=name]")).ToHaveAttributeAsync("data-lwc-label", new System.Text.RegularExpressions.Regex("focus"));
        // Supplemental fixture: two fields deliberately share one semantic label.
        await page.EvaluateAsync("""
            () => {
                const fixture = document.createElement('div'); fixture.id = 'controls-fixture';
                fixture.innerHTML = '<label id="shared-label">Shared name</label><label id="new-label">New name</label><input id="one" aria-labelledby="shared-label" aria-invalid="true"><input id="two" aria-labelledby="shared-label" aria-invalid="true">';
                document.body.append(fixture);
            }
            """);
        await Assertions.Expect(page.Locator("#shared-label")).ToHaveAttributeAsync("data-lwc-label", new System.Text.RegularExpressions.Regex("invalid"));
        await page.EvaluateAsync("document.querySelector('#one').removeAttribute('aria-invalid')");
        await Assertions.Expect(page.Locator("#shared-label")).ToHaveAttributeAsync("data-lwc-label", new System.Text.RegularExpressions.Regex("invalid"));
        await page.EvaluateAsync("document.querySelector('#two').setAttribute('aria-labelledby', 'new-label')");
        await Assertions.Expect(page.Locator("#shared-label")).Not.ToHaveAttributeAsync("data-lwc-label", new System.Text.RegularExpressions.Regex("invalid"));
        await Assertions.Expect(page.Locator("#new-label")).ToHaveAttributeAsync("data-lwc-label", new System.Text.RegularExpressions.Regex("invalid"));
        await page.EvaluateAsync("document.querySelector('#two').remove()");
        await Assertions.Expect(page.Locator("#new-label")).Not.ToHaveAttributeAsync("data-lwc-label", new System.Text.RegularExpressions.Regex("invalid"));
    }

    [Fact]
    public async Task Stepper_PreservesPrecisionEventsReadOnlyAndHoldCancellation()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/pheno-age");
        await page.WaitForFunctionAsync("() => !!window.LwcFormControls");
        await page.EvaluateAsync("""
            () => {
                const fixture = document.createElement('fieldset'); fixture.id = 'stepper-fixture';
                fixture.innerHTML = '<label for="decimal">Decimal</label><input id="decimal" type="number" data-lwc-stepper min="0.1" max="0.5" step="0.1" value="0.2">';
                document.body.prepend(fixture);
                window.stepEvents = [];
                for (const type of ['input','change']) fixture.addEventListener(type, e => window.stepEvents.push([type,e.target.value]));
                window.LwcFormControls.refresh();
            }
            """);
        var up = page.Locator("#stepper-fixture [data-step=up]");
        var input = page.Locator("#decimal");
        await up.ClickAsync();
        await Assertions.Expect(input).ToHaveValueAsync("0.3");
        Assert.Equal("[[\"input\",\"0.3\"],[\"change\",\"0.3\"]]", await page.EvaluateAsync<string>("JSON.stringify(window.stepEvents)"));
        await input.EvaluateAsync("el => el.readOnly = true");
        await Assertions.Expect(up).ToBeDisabledAsync();
        await input.EvaluateAsync("el => el.readOnly = false");
        await Assertions.Expect(up).ToBeEnabledAsync();
        await page.EvaluateAsync("document.querySelector('#stepper-fixture').disabled = true");
        await Assertions.Expect(up).ToBeDisabledAsync();
        await page.EvaluateAsync("document.querySelector('#stepper-fixture').disabled = false");
        await Assertions.Expect(up).ToBeEnabledAsync();
        await up.DispatchEventAsync("pointerdown", new { button = 0 });
        await page.EvaluateAsync("window.dispatchEvent(new Event('blur'))");
        await page.WaitForTimeoutAsync(550);
        await Assertions.Expect(input).ToHaveValueAsync("0.4");
        await up.ClickAsync();
        await Assertions.Expect(input).ToHaveValueAsync("0.5");
        await Assertions.Expect(up).ToBeDisabledAsync();
        await input.FillAsync("0.1");
        await input.PressAsync("ArrowUp");
        await Assertions.Expect(input).ToHaveValueAsync("0.2");
        await up.DispatchEventAsync("pointerdown", new { button = 0 });
        await input.EvaluateAsync("el => { window.detachedField = el; el.closest('fieldset').remove(); }");
        await page.WaitForTimeoutAsync(550);
        Assert.Equal("0.3", await page.EvaluateAsync<string>("window.detachedField.value"));
    }

    [Fact]
    public async Task ReducedMotion_CancelsActiveFeedbackAndPreventsNewAnimations()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply");
        await page.WaitForFunctionAsync("() => !!window.LwcFormControls");
        await page.Locator("#name").ClickAsync();
        await page.Locator("#name").EvaluateAsync("el => el.setAttribute('aria-invalid', 'true')");
        await page.WaitForFunctionAsync("() => document.querySelector('#name').getAnimations().some(a => a.constructor.name === 'Animation')");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => !document.querySelector('#name').getAnimations().some(a => a.playState === 'running')");
        await page.Locator("#name").EvaluateAsync("el => { el.removeAttribute('aria-invalid'); el.setAttribute('aria-invalid', 'true'); }");
        Assert.False(await page.Locator("#name").EvaluateAsync<bool>("el => el.getAnimations().some(a => a.playState === 'running')"));
    }
}
