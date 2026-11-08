using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BioageDateCardsBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", "America/Los_Angeles")]
    [InlineData("/bortz-age", "Pacific/Apia")]
    public async Task Calendar_RestoresImmediatelyAndCancelsSupersededFlips(string path, string timezone)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), TimezoneId = timezone,
            ViewportSize = new() { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync(path);
        await page.WaitForSelectorAsync(".bioage-biomarker-entry-ready");
        await page.Clock.RunForAsync(4000); // Restores can arrive after the old polling window.
        await page.EvaluateAsync("""
            () => {
                document.querySelector('#dob-year').value = '1980';
                document.querySelector('#blood-draw-date').value = '2011-12-30';
                window.LwcBioageFlow.completeInitialView();
            }
            """);
        // This civil date was skipped by Apia's time-zone change; it must still echo exactly.
        await Assertions.Expect(page.Locator(".bioage-cal-day")).ToHaveTextAsync("30");
        await Assertions.Expect(page.Locator(".bioage-cal-month")).ToHaveTextAsync("Dec");
        await page.EvaluateAsync("""
            () => {
                const field = document.querySelector('#blood-draw-date');
                field.value = '2024-02-29'; field.dispatchEvent(new Event('input', {bubbles:true}));
                field.value = '2024-03-01'; window.LwcBioageFlow.completeInitialView();
            }
            """);
        await Assertions.Expect(page.Locator(".bioage-cal-day")).ToHaveTextAsync("1");
        Assert.Equal(0, await page.Locator(".bioage-cal").EvaluateAsync<int>("e => e.getAnimations().length"));
        await page.Clock.RunForAsync(1000);
        await Assertions.Expect(page.Locator(".bioage-cal-day")).ToHaveTextAsync("1");
        await page.Locator("#blood-draw-date").FillAsync("");
        await Assertions.Expect(page.Locator(".bioage-cal")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".bioage-cal-day")).ToHaveTextAsync("");
        await page.Locator("#blood-draw-date").FillAsync("2024-02-29");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Assertions.Expect(page.Locator(".bioage-cal-day")).ToHaveTextAsync("29");
        Assert.Equal(0, await page.Locator(".bioage-cal").EvaluateAsync<int>("e => e.getAnimations().length"));
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task ContinueGlow_UsesTheSameDateBoundsAsTheCalculator(string path)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForSelectorAsync(".bioage-biomarker-entry-ready");
        await page.SelectOptionAsync("#dob-year", "2000");
        var date = page.Locator("#blood-draw-date");
        foreach (var value in new[] { "1999-12-31", "2999-01-01", "", "2024-02-29" })
        {
            await date.FillAsync(value);
            var states = await page.EvaluateAsync<bool[]>("""
                () => [document.querySelector('#lwcToStep2Btn').classList.contains('is-armed'),
                    window.LwcBioageFlow.hasValidDates(document.querySelector('form.bioageform'))]
                """);
            Assert.Equal(value == "2024-02-29", states[0]);
            Assert.Equal(states[1], states[0]);
        }
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await Assertions.Expect(page.Locator("#lwc-step-2")).ToBeVisibleAsync();
        await page.Locator("#lwcToStep1Btn").ClickAsync();
        await Assertions.Expect(date).ToHaveValueAsync("2024-02-29");
    }
}
