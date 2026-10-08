using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class HomepagePanelsBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task HomepagePanels_ShareTheHighlightsHeadStrip(int width)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 900);
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync("#eventsTable thead th");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var strips = await page.EvaluateAsync<StripMeasurement[]>(
            """
            () => ['#eventsTable thead th', '.lwc-countdown-header-row', '#hall-of-fame h2', '#faq h2', '#contribute h2', '#newsletter h2']
                .map(selector => {
                    const element = document.querySelector(selector);
                    const style = getComputedStyle(element);
                    return {
                        Selector: selector,
                        Height: element.getBoundingClientRect().height,
                        Background: style.backgroundColor,
                        FontSize: style.fontSize,
                        Transform: style.textTransform
                    };
                })
            """);

        var highlights = strips[0];
        Assert.All(strips, strip =>
        {
            Assert.InRange(Math.Abs(strip.Height - highlights.Height), 0, 1);
            Assert.Equal(highlights.Background, strip.Background);
            Assert.Equal(highlights.FontSize, strip.FontSize);
            Assert.Equal(highlights.Transform, strip.Transform);
        });
    }

    private sealed class StripMeasurement
    {
        public string Selector { get; set; } = "";
        public double Height { get; set; }
        public string Background { get; set; } = "";
        public string FontSize { get; set; } = "";
        public string Transform { get; set; } = "";
    }
}
