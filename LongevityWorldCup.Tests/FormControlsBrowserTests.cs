using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FormControlsBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task StandaloneDesigner_KeepsPinkSwitchesAndNativeActivation(int width)
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 900);
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/internal/custom-event-designer.html");
        var toggle = page.GetByRole(AriaRole.Switch, new() { Name = "Webpage", Exact = true });
        await Assertions.Expect(toggle).ToBeCheckedAsync();
        await Assertions.Expect(toggle).ToHaveCSSAsync("background-color", "rgb(255, 64, 129)");
        Assert.True(await toggle.EvaluateAsync<bool>("""
            el => { const r=el.getBoundingClientRect(); return r.width>r.height && el.closest('label').getBoundingClientRect().height>=44; }
            """));
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(toggle).Not.ToBeCheckedAsync();
        await page.Locator("label.check").Filter(new() { Has = toggle }).ClickAsync();
        await Assertions.Expect(toggle).ToBeCheckedAsync();
        Assert.False(await page.EvaluateAsync<bool>("() => Boolean(window.LwcFormControls)"));
    }

    [Theory]
    [InlineData(195, 422)]
    [InlineData(320, 390)]
    [InlineData(720, 450)]
    public async Task Picker_UsesAvailableHeightAndKeepsTheSelectedOptionVisible(int width, int height)
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/pheno-age");
        var picker = page.Locator("#dob-month");
        await picker.SelectOptionAsync("12");
        await picker.EvaluateAsync("el => el.scrollIntoView({block:'end'})");
        await picker.ClickAsync();
        Assert.True(await picker.EvaluateAsync<bool>("""
            el => {
                const r = el.selectedOptions[0].getBoundingClientRect();
                return r.top >= 0 && r.bottom <= innerHeight && r.left >= 0 && r.right <= innerWidth;
            }
            """));
        await page.Keyboard.PressAsync("Home");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(picker).ToHaveValueAsync("1");
        await picker.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(picker).ToHaveValueAsync("12");
    }

    [Theory]
    [InlineData(1440, "Escape")]
    [InlineData(390, "Escape")]
    [InlineData(1440, "Tab")]
    [InlineData(390, "Tab")]
    public async Task PickerDismissal_StaysInsideThePicker(int width, string key)
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 900);
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/athlete/michael-lustgarten");
        var picker = page.Locator("#ageResultSelect");
        var profile = page.Locator("#detailsModal");
        await picker.ClickAsync();
        Assert.True(await picker.EvaluateAsync<bool>("el => el.matches(':open')"));
        await page.Keyboard.PressAsync(key);
        await Assertions.Expect(profile).ToBeVisibleAsync();
        await Assertions.Expect(picker).ToBeFocusedAsync();
        Assert.False(await picker.EvaluateAsync<bool>("el => el.matches(':open')"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(profile).ToBeHiddenAsync();
    }

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
    public async Task SegmentedControls_FollowProgrammaticSelectionAndClassChanges()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/health");
        await page.SetContentAsync("""
            <div data-lwc-segment-group style="position:relative;display:flex;width:240px">
                <button id="first" aria-selected="true" style="width:120px;height:44px">First</button>
                <button id="second" aria-selected="false" style="width:120px;height:44px">Second</button>
            </div>
            """);
        await page.AddScriptTagAsync(new() { Url = new Uri(App.BaseAddress, "/js/form-controls.js").ToString() });
        await Assertions.Expect(page.Locator("#first")).ToHaveAttributeAsync("data-lwc-segment-on", "");
        await page.EvaluateAsync("""
            () => {
                document.getElementById('first').setAttribute('aria-selected', 'false');
                document.getElementById('second').setAttribute('aria-selected', 'true');
            }
            """);
        await Assertions.Expect(page.Locator("#second")).ToHaveAttributeAsync("data-lwc-segment-on", "");
        await page.EvaluateAsync("""
            () => {
                document.getElementById('second').setAttribute('aria-selected', 'false');
                document.getElementById('first').classList.add('active');
            }
            """);
        await Assertions.Expect(page.Locator("#first")).ToHaveAttributeAsync("data-lwc-segment-on", "");
        await Assertions.Expect(page.Locator("#second")).Not.ToHaveAttributeAsync("data-lwc-segment-on", "");
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
