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
}
