using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BioageLiveInstrumentBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IBrowserContext> NewContextAsync(bool reduce = true, int width = 1440, int height = 900)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US",
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = reduce ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/site-statistics/event", route => route.FulfillAsync(new() { Status = 204 }));
        return context;
    }

    private static async Task OpenPanelAsync(IPage page, string clock)
    {
        await page.GotoAsync($"/{clock}-age");
        await FillBioageStepOneAsync(page, "2026-09-01");
        if (clock == "bortz") await FillBortzBiomarkersAsync(page);
        else await FillPhenoBiomarkersAsync(page);
        await Assertions.Expect(page.Locator("#continueButton")).ToBeVisibleAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.bioageform.show')?.dataset.bioageResultStage === 'rank'");
    }

    [Theory]
    [InlineData("pheno", "")]
    [InlineData("bortz", "")]
    [InlineData("pheno", "8")]
    [InlineData("bortz", "8")]
    public async Task ImmediateContinuation_ValidatesAndCalculatesTheExactEditedPanel(string clock, string value)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        // Exercise the handoff in the same task as input/change; all writes and navigation are mocked.
        var result = await page.EvaluateAsync<double[]>(
            """
            ({ clock, value }) => {
                let handoff = null;
                const capture = (data, difference) => { handoff = { data, difference }; return false; };
                if (clock === 'pheno') storePhenoResultForNextStep = capture;
                else storeBortzResultForNextStep = capture;
                const input = document.getElementById('glucose');
                input.value = value;
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.dispatchEvent(new Event('change', { bubbles: true }));
                const disabled = document.getElementById('continueButton').disabled;
                proceedToNextPage();
                const actual = handoff?.difference;
                if (!value) return [disabled ? 1 : 0, handoff ? 1 : 0];
                calculateResult();
                const age = clock === 'pheno' ? lastCalculatedPhenoAge : lastCalculatedBortzAge;
                const chrono = window.calculateAgeAtDate(new Date(1980, 4, 20), new Date('2026-09-01T00:00:00'));
                return [disabled ? 1 : 0, handoff ? 1 : 0, actual, age - chrono, handoff?.data.Biomarkers[0].GluMmolL];
            }
            """, new { clock, value });
        Assert.Equal(1, result[0]);
        Assert.Equal(value == "" ? 0 : 1, result[1]);
        if (value != "")
        {
            Assert.Equal(result[3], result[2], 9);
            Assert.Equal(8, result[4]);
        }
    }

    [Theory]
    [InlineData("pheno", "dob-year", "1970")]
    [InlineData("bortz", "dob-year", "1970")]
    [InlineData("pheno", "blood-draw-date", "2025-09-01")]
    [InlineData("bortz", "blood-draw-date", "2025-09-01")]
    public async Task PersonalDetails_InvalidateAndRecalculateBeforeReturningToTheResult(string clock, string field, string value)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        var oldAge = await page.Locator("#animatedAge").InnerTextAsync();
        var invalidated = await page.EvaluateAsync<bool>(
            """
            ({field, value}) => {
                lwcSetStep(1);
                const input = document.getElementById(field);
                input.value = value;
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.dispatchEvent(new Event('change', { bubbles: true }));
                return !document.getElementById('continueButton').classList.contains('show');
            }
            """, new { field, value });
        Assert.True(invalidated);
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await page.WaitForFunctionAsync("old => document.getElementById('animatedAge').textContent !== old && !document.getElementById('continueButton').disabled", oldAge);
    }

    [Theory]
    [InlineData("pheno", true)]
    [InlineData("bortz", true)]
    [InlineData("pheno", false)]
    [InlineData("bortz", false)]
    public async Task OffscreenReadout_AlwaysMatchesTheCompletedReading(string clock, bool reduce)
    {
        await using var context = await NewContextAsync(reduce, 390, 844);
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        await page.Locator("#glucose").FocusAsync();
        await Assertions.Expect(page.Locator(".bioage-live-readout--shown")).ToBeVisibleAsync();
        var old = await page.Locator("#animatedAge").InnerTextAsync();
        var scroll = await page.EvaluateAsync<double>("scrollY");
        await page.Locator("#glucose").FillAsync("8");
        await page.WaitForFunctionAsync("old => document.getElementById('animatedAge').textContent !== old", old);
        await Assertions.Expect(page.Locator(".bioage-live-readout__value")).ToHaveTextAsync(await page.Locator("#animatedAge").InnerTextAsync());
        Assert.InRange(await page.EvaluateAsync<double>("scrollY"), scroll - 2, scroll + 2);
        await Assertions.Expect(page.Locator("#glucose")).ToBeFocusedAsync();
        if (reduce) Assert.Equal(0, await page.Locator(".bioage-roll--up, .bioage-roll--down").CountAsync());
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task InvalidDates_RemainSilentDuringLiveEdits(string clock)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        await page.EvaluateAsync("() => { window.__alerts = []; customAlert = message => { window.__alerts.push(message); return Promise.resolve(); }; }");
        await page.EvaluateAsync("() => { const d=document.getElementById('blood-draw-date'); d.value='1970-01-01'; d.dispatchEvent(new Event('input',{bubbles:true})); d.dispatchEvent(new Event('change',{bubbles:true})); }");
        await page.WaitForTimeoutAsync(600);
        Assert.Empty(await page.EvaluateAsync<string[]>("window.__alerts"));
        Assert.False(await page.Locator("#continueButton").IsVisibleAsync());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task InvalidBortzWbc_RemainsSilentAndCannotReuseThePreviousReading(string value)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, "bortz");
        await page.EvaluateAsync("() => { window.__alerts = []; customAlert = message => { window.__alerts.push(message); return Promise.resolve(); }; }");
        await page.Locator("#wbc").FillAsync(value);
        await page.WaitForTimeoutAsync(600);
        Assert.Empty(await page.EvaluateAsync<string[]>("window.__alerts"));
        Assert.True(await page.Locator("#continueButton").IsDisabledAsync());
        var handedOff = await page.EvaluateAsync<bool>("() => { let handedOff=false; storeBortzResultForNextStep=()=>{ handedOff=true; return false; }; proceedToNextPage(); return handedOff; }");
        Assert.False(handedOff);
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task ManualUnitSelection_IsNotUndoneByLiveOrExplicitCalculation(string clock)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        await page.Locator("#albuminUnit").SelectOptionAsync("0.1");
        await page.WaitForTimeoutAsync(600);
        Assert.Equal("0.1", await page.Locator("#albuminUnit").InputValueAsync());
        await page.EvaluateAsync("calculateResult()");
        Assert.Equal("0.1", await page.Locator("#albuminUnit").InputValueAsync());
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task UpdateMode_KeepsItsExplicitCalculationAndExistingPresentation(string clock)
    {
        await using var context = await NewContextAsync();
        await context.AddInitScriptAsync("sessionStorage.setItem('selectedAthlete', JSON.stringify({Name:'Browser Test Athlete',DateOfBirth:{Year:1980,Month:5,Day:20},Biomarkers:[]}))");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/{clock}-age?update=1");
        await page.WaitForFunctionAsync("() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");
        await FillBloodDrawDateAsync(page, "2026-09-01");
        if (clock == "bortz") await FillBortzBiomarkersAsync(page);
        else await FillPhenoBiomarkersAsync(page);
        await page.WaitForTimeoutAsync(600);
        Assert.False(await page.Locator("#continueButton").IsVisibleAsync());
        await page.Locator(".bioage-calculate-button").ClickAsync();
        await Assertions.Expect(page.Locator("#continueButton")).ToBeVisibleAsync();
        Assert.Equal(0, await page.Locator(".bioage-live-instrument, .bioage-live-readout, .bioage-gap-meter, [data-bioage-range], [data-bioage-wake]").CountAsync());
    }

    [Theory]
    [InlineData("pheno", ColorScheme.Light)]
    [InlineData("pheno", ColorScheme.Dark)]
    [InlineData("bortz", ColorScheme.Light)]
    [InlineData("bortz", ColorScheme.Dark)]
    public async Task NarrowPanelWithNativeScrollbar_KeepsEveryUnitReadable(string clock, ColorScheme theme)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"]
        });
        await using var context = await browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US",
            ViewportSize = new() { Width = 160, Height = 450 },
            ColorScheme = theme, ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        await page.EvaluateAsync("() => document.fonts.ready");
        var clipped = await page.Locator("#lwc-step-2 select").EvaluateAllAsync<string[]>(
            """
            selects => {
                const context = document.createElement('canvas').getContext('2d');
                return selects.flatMap(select => {
                    if (!select.getBoundingClientRect().width) return [];
                    const style = getComputedStyle(select);
                    context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                    const width = select.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                    return [...select.options].filter(option => context.measureText(option.textContent.trim()).width > width + 1)
                        .map(option => `${select.id}: ${option.textContent} exceeds ${width}px`);
                });
            }
            """);
        Assert.Empty(clipped);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData("pheno", ColorScheme.Light)]
    [InlineData("pheno", ColorScheme.Dark)]
    [InlineData("bortz", ColorScheme.Light)]
    [InlineData("bortz", ColorScheme.Dark)]
    public async Task WaitingDomains_KeepTheirFieldNamesReadable(string clock, ColorScheme theme)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ColorScheme = theme });
        await page.GotoAsync($"/{clock}-age");
        await FillBioageStepOneAsync(page, "2026-09-01");
        var contrast = await page.Locator("[data-bioage-wake='dormant'] .biomarker-card-header").First.EvaluateAsync<double>(
            """
            element => {
                const parse = value => value.match(/[\d.]+/g).map(Number);
                const foreground = parse(getComputedStyle(element).color);
                const opacity = Number(getComputedStyle(element.closest('fieldset')).opacity);
                let parent = element, background;
                while (parent) {
                    const color = parse(getComputedStyle(parent).backgroundColor);
                    if (color.length === 3 || color[3] > 0) { background = color; break; }
                    parent = parent.parentElement;
                }
                background ||= [255,255,255];
                const effective = foreground.slice(0,3).map((value,index) => value * opacity + background[index] * (1-opacity));
                const luminance = color => color.slice(0,3).map(value => {
                    value /= 255; return value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4;
                }).reduce((total,value,index) => total + value * [.2126,.7152,.0722][index], 0);
                const light = luminance(effective), dark = luminance(background);
                return (Math.max(light,dark) + .05) / (Math.min(light,dark) + .05);
            }
            """);
        Assert.True(contrast >= 4.5, $"{clock} {theme}: waiting field names have only {contrast:F2}:1 contrast.");
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task ReadoutClick_ReturnsToTheResultAfterAnExplicitCalculationAndManualScroll(string clock)
    {
        await using var context = await NewContextAsync(true, 390, 844);
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        await page.Locator(".bioage-calculate-button").ClickAsync();
        await Assertions.Expect(page.Locator("#animatedAge")).ToBeInViewportAsync();
        await page.Locator("#wbc").FocusAsync();
        await Assertions.Expect(page.Locator(".bioage-live-readout--shown")).ToBeVisibleAsync();
        await page.Locator(".bioage-live-readout--shown").ClickAsync();
        await Assertions.Expect(page.Locator("#animatedAge")).ToBeInViewportAsync();
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task ExplicitCalculationDuringArrival_AlignsTheSettledResultBelowTheHeader(string clock)
    {
        await using var context = await NewContextAsync(false, 390, 844);
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.style.fontSize = '200%';
                const result = document.querySelector('.bioageform.show');
                // Hold the real CSS arrival at its initial offset to reproduce a
                // Calculate click during the card's movement without a timer race.
                result.style.animation = 'none';
                void result.offsetWidth;
                result.style.removeProperty('animation');
                const arrival = result.getAnimations().find(animation => animation.animationName === 'bioage-card-settle');
                if (!arrival) throw new Error('The result arrival animation was not created.');
                arrival.pause();
                arrival.currentTime = 0;
                calculateResult();
            }
            """);
        await page.WaitForFunctionAsync("() => document.querySelector('.bioageform.show')?.dataset.bioageResultStage === 'rank'");
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const result = document.querySelector('.bioageform.show');
                result.getAnimations().forEach(animation => animation.finish());
                return [document.querySelector('#validAgeInput > div:first-child').getBoundingClientRect().top,
                    parseFloat(getComputedStyle(document.documentElement).scrollPaddingTop),
                    document.querySelector('#animatedAge').getBoundingClientRect().bottom,
                    document.querySelector('.phenoage-result-actions, .bioage-result-actions').getBoundingClientRect().top];
            }
            """);
        Assert.True(layout[0] >= layout[1] - 1, $"{clock}: the settled label is behind the header ({layout[0]} < {layout[1]}).");
        Assert.True(layout[2] <= layout[3] - 8, $"{clock}: the settled score is behind the actions ({layout[2]} >= {layout[3]}).");
    }
}
