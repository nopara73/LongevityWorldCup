using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BioageDateSheetBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", 1440, ColorScheme.Light)]
    [InlineData("/bortz-age", 390, ColorScheme.Dark)]
    public async Task DateFeedback_FollowsNativeValidityAndRespectsReducedMotion(string path, int width, ColorScheme scheme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = scheme,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc));
        await page.GotoAsync(path);
        await page.WaitForSelectorAsync(".bioage-biomarker-entry-ready");
        await Assertions.Expect(page.Locator("#dobFieldset")).ToHaveAccessibleNameAsync("Date of birth:");
        await Assertions.Expect(page.Locator("#lwc-step-1 > fieldset").Nth(1)).ToHaveAccessibleNameAsync("Blood draw date:");
        var year = page.Locator("#dob-year");
        var date = page.Locator("#blood-draw-date");
        var drawBand = page.Locator("#lwc-step-1 > fieldset:nth-of-type(2) > legend");
        var readyColor = await drawBand.EvaluateAsync<string>("e => getComputedStyle(e, '::after').backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", readyColor);

        await year.SelectOptionAsync("2000");
        await year.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#dob-month")).ToBeFocusedAsync();
        await page.SelectOptionAsync("#dob-month", "2");
        await Assertions.Expect(page.Locator("#dob-day")).ToHaveValueAsync("29");
        await year.SelectOptionAsync("2001");
        await Assertions.Expect(page.Locator("#dob-day")).ToHaveValueAsync("28");
        await Assertions.Expect(page.Locator("#dob-day option[value='29']")).ToHaveCountAsync(0);

        foreach (var invalid in new[] { "", "2026-11-04" })
        {
            await date.FillAsync(invalid);
            Assert.False(await date.EvaluateAsync<bool>("e => e.validity.valid"));
            Assert.Equal("rgba(0, 0, 0, 0)", await drawBand.EvaluateAsync<string>("e => getComputedStyle(e, '::after').backgroundColor"));
            // An invalid draw date must not arm Continue even when the year is set.
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            await Assertions.Expect(page.Locator("#lwcToStep2Btn")).Not.ToHaveCSSAsync("animation-name", "bioage-date-arm");
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        }
        await date.FillAsync("2024-02-29");
        Assert.True(await date.EvaluateAsync<bool>("e => e.validity.valid"));
        Assert.Equal(readyColor, await drawBand.EvaluateAsync<string>("e => getComputedStyle(e, '::after').backgroundColor"));
        await page.SelectOptionAsync("#dob-month", "12");
        var locked = await page.Locator("#privacyNote").EvaluateAsync<string>("e => getComputedStyle(e, '::before').content");
        await page.SelectOptionAsync("#dob-day", "30");
        Assert.NotEqual(locked, await page.Locator("#privacyNote").EvaluateAsync<string>("e => getComputedStyle(e, '::before').content"));

        var motion = await page.EvaluateAsync<string[]>(
            """
            () => [
                getComputedStyle(document.querySelector('#lwc-step-1 > fieldset:nth-of-type(2) > legend'), '::after'),
                getComputedStyle(document.querySelector('#privacyNote')),
                getComputedStyle(document.querySelector('#lwcToStep2Btn'))
            ].map(style => style.transitionDuration + '|' + style.animationName)
            """);
        Assert.All(motion, value => Assert.Equal("0s|none", value));
        await Assertions.Expect(date).ToHaveValueAsync("2024-02-29");
        await ExpectNoHorizontalOverflowAsync(page);
    }
}
