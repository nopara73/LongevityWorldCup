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
    [InlineData("pheno", 1280)]
    [InlineData("bortz", 390)]
    [InlineData("pheno", 390)]
    [InlineData("bortz", 1280)]
    public async Task BrowserBack_RecomputesRestoredFieldsAndKeepsJoinAvailable(string clock, int width)
    {
        await using var context = await NewContextAsync(width: width);
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        var reading = await page.Locator(".bioage-live-readout__value").InnerTextAsync();
        await page.Locator("#continueButton").ClickAsync();
        await Assertions.Expect(page.Locator("#name")).ToBeVisibleAsync();
        await page.GoBackAsync();
        await Assertions.Expect(page.Locator(".bioage-live-readout__value")).ToHaveTextAsync(reading);
        await Assertions.Expect(page.Locator("#continueButton")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#continueButton")).ToBeEnabledAsync();
        await page.Locator("#continueButton").ClickAsync();
        await Assertions.Expect(page.Locator("#name")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("pheno", false)]
    [InlineData("pheno", true)]
    [InlineData("bortz", false)]
    [InlineData("bortz", true)]
    public async Task HistoryRestoration_RecalculatesValuesRestoredWithoutInputEvents(string clock, bool cached)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        var reading = await page.Locator(".bioage-live-readout__value").InnerTextAsync();
        await page.EvaluateAsync("""
            ({clock, cached}) => {
                hideCalculatedResult();
                sessionStorage.removeItem(`bioageDraft:${clock}:v1`);
                document.getElementById('glucose').value = '8';
                window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: cached }));
            }
            """, new { clock, cached });
        await Assertions.Expect(page.Locator("#continueButton")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#continueButton")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator(".bioage-live-readout__value")).Not.ToHaveTextAsync(reading);
        await Assertions.Expect(page.Locator(".bioage-live-readout__value")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d+\.\d$"));
    }

    [Theory]
    [InlineData("pheno", 1280)]
    [InlineData("bortz", 390)]
    public async Task Join_StartsTheCurrentApplicationInsteadOfRecoveringAnOlderCheckout(string clock, int width)
    {
        await using var context = await NewContextAsync(width: width);
        var recoveries = 0;
        await context.RouteAsync("**/api/application/submission-status", route =>
        {
            Interlocked.Increment(ref recoveries);
            return route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = """{"success":true,"paymentRequired":true,"checkoutLink":"/api/payments/stripe/checkout/old-order","invoiceId":"old-invoice"}"""
            });
        });
        await context.RouteAsync("**/api/payments/stripe/checkout/old-order", route => route.FulfillAsync(new() { Status = 404, Body = "Retired checkout" }));
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        await page.EvaluateAsync("""
            () => localStorage.setItem('pendingApplicationSubmission', JSON.stringify({
                submissionId:'older-submission', payloadFingerprint:'older-payload', submissionKind:'full-application',
                createdAt:Date.now() - 3 * 24 * 60 * 60 * 1000
            }))
            """);
        await page.Locator("#continueButton").ClickAsync();
        await Assertions.Expect(page.Locator("#name")).ToBeVisibleAsync();
        Assert.EndsWith("/apply", page.Url);
        Assert.Equal(0, recoveries);
        Assert.True(await page.EvaluateAsync<bool>("sessionStorage.getItem('biomarkerData') !== null"));
        Assert.True(await page.EvaluateAsync<bool>("clock => sessionStorage.getItem(`bioageDraft:${clock}:v1`) !== null", clock));
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#name")).ToBeVisibleAsync();
        Assert.Equal(0, recoveries);
        Assert.NotEqual("older-submission", await page.EvaluateAsync<string>("window.createApplicationSubmissionId('older-payload')"));
    }

    [Fact]
    public async Task ApplicationReload_StillRecoversTheSubmissionFromItsOwnEntry()
    {
        await using var context = await NewContextAsync();
        await context.RouteAsync("**/api/application/submission-status", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """{"success":true,"paymentRequired":true,"checkoutLink":"/test-checkout","invoiceId":"current-invoice"}"""
        }));
        await context.RouteAsync("**/test-checkout", route => route.FulfillAsync(new() { Body = "Current checkout" }));
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, "pheno");
        await page.Locator("#continueButton").ClickAsync();
        await Assertions.Expect(page.Locator("#name")).ToBeVisibleAsync();
        await page.EvaluateAsync("""
            () => window.rememberPendingApplicationSubmission({
                submissionId:'current-submission', payloadFingerprint:'current-payload', submissionKind:'full-application'
            })
            """);
        await page.ReloadAsync();
        await page.WaitForURLAsync("**/test-checkout");
        Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('biomarkerData')"));
    }

    [Fact]
    public async Task RetiredCheckoutReview_ShowsUnavailableWithoutPollingTheWrongProvider()
    {
        await using var context = await NewContextAsync(width: 390);
        var checks = 0;
        await context.RouteAsync("**/api/application/payment-status", route =>
        {
            Interlocked.Increment(ref checks);
            return route.FulfillAsync(new() { Status = 503 });
        });
        await context.AddInitScriptAsync("sessionStorage.setItem('pendingPaymentInvoice', JSON.stringify({invoiceId:'stripe_old-order'}))");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/review?checkout=unavailable");
        await Assertions.Expect(page.Locator("#review-payment-status")).ToHaveTextAsync("This checkout is no longer available. Check your confirmation email.");
        await Assertions.Expect(page.Locator("[data-step='payment']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-failed"));
        Assert.Equal(0, checks);
    }

    [Theory]
    [InlineData("pheno", 390, ColorScheme.Light)]
    [InlineData("bortz", 390, ColorScheme.Dark)]
    [InlineData("pheno", 1280, ColorScheme.Dark)]
    [InlineData("bortz", 1280, ColorScheme.Light)]
    public async Task IncompleteReading_FillsWithoutInventingInputsOrAllowingSubmission(string clock, int width, ColorScheme scheme)
    {
        await using var context = await NewContextAsync(true, width, 844);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ColorScheme = scheme });
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync($"/{clock}-age");
        await FillBioageStepOneAsync(page, "2026-09-01");
        var readout = page.Locator(".bioage-live-readout");
        var value = page.Locator(".bioage-live-readout__value");
        await Assertions.Expect(value).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d+\.\d$"));
        await Assertions.Expect(readout).ToHaveAttributeAsync("data-complete", "false");
        await Assertions.Expect(page.Locator("#calculateBioageButton")).ToBeHiddenAsync();
        Assert.All(await page.Locator(".biomarker-card input").EvaluateAllAsync<string[]>("inputs => inputs.map(input => input.value)"), value => Assert.Equal("", value));
        await Assertions.Expect(page.Locator("#continueButton")).ToBeHiddenAsync();
        Assert.Equal("0%", await readout.EvaluateAsync<string>("element => element.style.getPropertyValue('--bioage-number-fill')"));
        await WaitForManagedActionStacksSettledAsync(page);
        var initialPosition = await readout.BoundingBoxAsync();
        var baseline = await value.InnerTextAsync();
        await CaptureAsync("empty");

        await page.Locator("#glucoseUnit").SelectOptionAsync("1");
        await page.Locator("#glucose").FillAsync("8");
        await Assertions.Expect(value).Not.ToHaveTextAsync(baseline);
        await Assertions.Expect(page.Locator("#glucose")).ToBeFocusedAsync();
        var partial = await value.InnerTextAsync();
        Assert.Equal(1, await page.Locator(".biomarker-card input").EvaluateAllAsync<int>("inputs => inputs.filter(input => input.value !== '').length"));
        Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('biomarkerData')"));
        await page.ReloadAsync();
        await Assertions.Expect(value).ToHaveTextAsync(partial);
        await Assertions.Expect(page.Locator("#glucose")).ToHaveValueAsync("8");
        Assert.Equal(1, await page.Locator(".biomarker-card input").EvaluateAllAsync<int>("inputs => inputs.filter(input => input.value !== '').length"));

        if (clock == "pheno") await FillPhenoBiomarkersAsync(page);
        else await FillBortzBiomarkersAsync(page);
        await Assertions.Expect(readout).ToHaveAttributeAsync("data-complete", "true");
        await Assertions.Expect(value).ToHaveTextAsync(await page.Locator("#animatedAge").InnerTextAsync());
        await Assertions.Expect(page.Locator("#continueButton")).ToBeEnabledAsync();
        await WaitForManagedActionStacksSettledAsync(page);
        var finalPosition = await readout.BoundingBoxAsync();
        Assert.NotNull(initialPosition);
        Assert.NotNull(finalPosition);
        Assert.InRange(finalPosition.X, initialPosition.X - 2, initialPosition.X + 2);
        Assert.InRange(finalPosition.Y, initialPosition.Y - 2, initialPosition.Y + 2);
        await CaptureAsync("complete");

        await page.Locator("#glucose").FillAsync("");
        await Assertions.Expect(readout).ToHaveAttributeAsync("data-complete", "false");
        await Assertions.Expect(page.Locator("#continueButton")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator($"#{clock}AgeResult")).ToBeHiddenAsync();
        await Assertions.Expect(value).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d+\.\d$"));
        Assert.False(await page.EvaluateAsync<bool>("() => { let sent = false; if (typeof storePhenoResultForNextStep === 'function') storePhenoResultForNextStep = () => { sent = true; }; else storeBortzResultForNextStep = () => { sent = true; }; proceedToNextPage(); return sent; }"));
        await Assertions.Expect(page.Locator("#glucose")).ToHaveValueAsync("");
        await CaptureAsync("partial");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        Assert.Empty(errors);

        async Task CaptureAsync(string state)
        {
            if (Environment.GetEnvironmentVariable("LWC_CAPTURE_BIOAGE_DESIGN") != "1") return;
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.artifacts/bioage-live"));
            Directory.CreateDirectory(directory);
            await page.EvaluateAsync("scrollTo(0, 0)");
            await WaitForManagedActionStacksSettledAsync(page);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{clock}-{width}-{state}.png") });
        }
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
        await page.EvaluateAsync("() => { document.querySelectorAll('.biomarker-card input').forEach(input => { if (input.id !== 'wbc') input.value = ''; }); document.getElementById('wbc').dispatchEvent(new Event('change', { bubbles: true })); }");
        await Assertions.Expect(page.Locator(".bioage-live-readout__value")).ToHaveTextAsync("—");
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
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".bioage-live-readout")).ToHaveAttributeAsync("data-complete", "true");
        Assert.Equal("0.1", await page.Locator("#albuminUnit").InputValueAsync());
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task IncompleteReading_AutomaticUnitCorrectionSurvivesReload(string clock)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/{clock}-age");
        await FillBioageStepOneAsync(page, "2026-09-01");
        await page.Locator("#albumin").FillAsync("45");
        await Assertions.Expect(page.Locator("#albuminUnit")).ToHaveValueAsync("1");
        await page.WaitForFunctionAsync("clock => JSON.parse(sessionStorage.getItem(`bioageDraft:${clock}:v1`))?.fields?.albuminUnit?.value === '1'", clock);
        var reading = await page.Locator(".bioage-live-readout__value").InnerTextAsync();
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".bioage-live-readout__value")).ToHaveTextAsync(reading);
        await Assertions.Expect(page.Locator("#albuminUnit")).ToHaveValueAsync("1");
        Assert.Equal(1, await page.Locator(".biomarker-card input").EvaluateAllAsync<int>("inputs => inputs.filter(input => input.value !== '').length"));
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task IncompleteReading_EquivalentUnitsKeepTheSameAge(string clock)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/{clock}-age");
        await FillBioageStepOneAsync(page, "2026-09-01");
        var value = page.Locator(".bioage-live-readout__value");
        await Assertions.Expect(value).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d+\.\d$"));
        var baseline = await value.InnerTextAsync();
        await page.Locator("#glucoseUnit").SelectOptionAsync("18.016");
        await page.WaitForTimeoutAsync(500);
        Assert.Equal(baseline, await value.InnerTextAsync());
        await SetFormValuesAsync(page, new() { ["glucose"] = "8", ["glucoseUnit"] = "1" });
        await Assertions.Expect(value).Not.ToHaveTextAsync(baseline);
        var metric = await value.InnerTextAsync();
        await SetFormValuesAsync(page, new() { ["glucose"] = "144.128", ["glucoseUnit"] = "18.016" });
        await page.WaitForTimeoutAsync(500);
        Assert.Equal(metric, await value.InnerTextAsync());
        await Assertions.Expect(page.Locator("#continueButton")).ToBeHiddenAsync();
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
        await FlowActionDockBrowserTests.RevealBioageResultAsync(page);
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
    public async Task ReadoutClick_ReturnsToTheResultAfterManualScroll(string clock)
    {
        await using var context = await NewContextAsync(true, 390, 844);
        var page = await context.NewPageAsync();
        await OpenPanelAsync(page, clock);
        await FlowActionDockBrowserTests.RevealBioageResultAsync(page);
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
