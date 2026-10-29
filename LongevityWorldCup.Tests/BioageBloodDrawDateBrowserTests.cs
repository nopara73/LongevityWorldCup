using System.Globalization;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BioageBloodDrawDateBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("pheno", "Asia/Singapore", "2026-10-14T16:30:00Z", "2026-10-15", "2026-10-14", 390)]
    [InlineData("bortz", "Asia/Singapore", "2026-10-14T16:30:00Z", "2026-10-15", "2026-10-14", 1440)]
    [InlineData("pheno", "America/Los_Angeles", "2026-03-09T06:30:00Z", "2026-03-08", "2026-03-07", 1440)]
    [InlineData("bortz", "America/Los_Angeles", "2026-03-09T06:30:00Z", "2026-03-08", "2026-03-07", 320)]
    public async Task RecentDates_UseLocalCalendarDaysAndPreserveEdits(
        string clock, string timeZone, string instant, string today, string yesterday, int width)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            TimezoneId = timeZone,
            ViewportSize = new() { Width = width, Height = 900 },
            IsMobile = width < 600,
            HasTouch = width < 600,
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture).UtcDateTime);
        await page.GotoAsync($"/{clock}-age");
        await page.WaitForSelectorAsync(".bioage-biomarker-entry-ready");

        var date = page.Locator("#blood-draw-date");
        var relativeLabel = page.Locator(".blood-draw-relative-label");
        await Assertions.Expect(date).ToHaveValueAsync(today);
        await Assertions.Expect(date).ToBeVisibleAsync();
        await Assertions.Expect(relativeLabel).ToHaveTextAsync("Today");
        await Assertions.Expect(relativeLabel).ToBeVisibleAsync();
        await AssertRelativeLabelCenteredAsync(page);
        await date.FocusAsync();
        await Assertions.Expect(relativeLabel).ToBeHiddenAsync();
        Assert.NotEqual("rgba(0, 0, 0, 0)", await date.EvaluateAsync<string>("input => getComputedStyle(input).color"));
        await FlowActionDockBrowserTests.FillBloodDrawDateAsync(page, yesterday);
        await page.Locator("#dob-year").FocusAsync();
        await Assertions.Expect(date).ToHaveValueAsync(yesterday);
        await Assertions.Expect(relativeLabel).ToHaveTextAsync("Yesterday");
        await Assertions.Expect(relativeLabel).ToBeVisibleAsync();
        await AssertRelativeLabelCenteredAsync(page);
        await page.ReloadAsync();
        await Assertions.Expect(date).ToHaveValueAsync(yesterday);
        await Assertions.Expect(relativeLabel).ToHaveTextAsync("Yesterday");
        await Assertions.Expect(relativeLabel).ToBeVisibleAsync();

        await FlowActionDockBrowserTests.FillBloodDrawDateAsync(page, "2026-01-02");
        await page.ReloadAsync();
        await Assertions.Expect(date).ToHaveValueAsync("2026-01-02");
        await Assertions.Expect(date).ToBeVisibleAsync();
        await Assertions.Expect(relativeLabel).ToBeHiddenAsync();

        await FlowActionDockBrowserTests.FillBloodDrawDateAsync(page, "");
        await page.ReloadAsync();
        await Assertions.Expect(date).ToHaveValueAsync("");
        await Assertions.Expect(date).ToBeVisibleAsync();

        await date.FillAsync(DateOnly.Parse(today, CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd"));
        Assert.True(await date.EvaluateAsync<bool>("input => input.validity.rangeOverflow"));
        await FlowActionDockBrowserTests.FillBloodDrawDateAsync(page, today);
        await page.Locator("#dob-year").FocusAsync();
        await Assertions.Expect(date).ToHaveValueAsync(today);
        await Assertions.Expect(relativeLabel).ToHaveTextAsync("Today");
        await Assertions.Expect(relativeLabel).ToBeVisibleAsync();
        Assert.True(await date.EvaluateAsync<bool>("input => input.validity.valid"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    }

    private static async Task AssertRelativeLabelCenteredAsync(IPage page)
    {
        var offset = await page.Locator(".blood-draw-relative-label").EvaluateAsync<double>(
            """
            label => {
                const field = document.querySelector('#blood-draw-date').getBoundingClientRect();
                const text = label.getBoundingClientRect();
                return text.top + text.height / 2 - field.top - field.height / 2;
            }
            """);
        Assert.InRange(offset, -0.5, 0.5);
    }

    [Theory]
    [InlineData("pheno", "2026-01-02")]
    [InlineData("bortz", "2026-01-02")]
    [InlineData("pheno", "")]
    [InlineData("bortz", "")]
    public async Task SharedDateAndLegacyCrpDraft_KeepTheExplicitValues(string clock, string legacyDate)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            $$"""
            (() => {
                const clock = location.pathname.startsWith('/bortz') ? 'bortz' : 'pheno';
                sessionStorage.setItem(`bioageDraft:${clock}:v1`, JSON.stringify({
                    version: 1, clock, step: 1, fields: {
                        'dob-year': { value: '1980' },
                        'blood-draw-date': { value: '{{legacyDate}}' },
                        'crp': { value: '2.5' },
                        'crp-negative': { value: 'on', checked: true }
                    }
                }));
            })();
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/{clock}-age");
        await page.WaitForSelectorAsync(".bioage-biomarker-entry-ready");
        if (legacyDate.Length > 0)
        {
            await Assertions.Expect(page.Locator("#blood-draw-date")).ToHaveValueAsync(legacyDate);
            await Assertions.Expect(page.Locator("#blood-draw-date")).ToBeVisibleAsync();
        }
        else
        {
            await Assertions.Expect(page.Locator(".blood-draw-relative-label")).ToHaveTextAsync("Today");
        }
        await Assertions.Expect(page.Locator("#crp-negative")).ToHaveCountAsync(0);
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await Assertions.Expect(page.Locator("#crp")).ToHaveValueAsync("2.5");
        await Assertions.Expect(page.Locator("#crp")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#crpUnit")).ToBeEnabledAsync();
        await page.Locator("#crp").FillAsync("0.7");

        await page.GotoAsync($"/{clock}-age?Date=2026-02-03&Year=1980");
        await Assertions.Expect(page.Locator("#blood-draw-date")).ToHaveValueAsync("2026-02-03");
        await Assertions.Expect(page.Locator("#blood-draw-date")).ToBeVisibleAsync();
    }
}
