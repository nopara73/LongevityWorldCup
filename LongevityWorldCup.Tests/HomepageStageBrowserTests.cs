using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class HomepageStageBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1, 1280)]
    [InlineData(1, 390)]
    [InlineData(5, 1280)]
    [InlineData(5, 390)]
    public async Task Stage_RunsInTheHeaderGraphiteFromTheHeaderToThePodiumFloor(int visits, int width)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        // The visit counter increments on load, so seed one less than the visit being simulated.
        await context.AddInitScriptAsync($"localStorage.setItem('lwcHomepageVisitCount:v1', '{visits - 1}');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 900);
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.podium-item:not(.podium-skeleton-item)').length === 3 && document.querySelector('main').dataset.homepageVisitLayout");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var stage = await page.EvaluateAsync<StageMeasurement>(
            """
            () => {
                const backdrop = element => {
                    const style = getComputedStyle(element, '::before');
                    return { color: style.backgroundColor, width: parseFloat(style.width) };
                };
                const main = document.querySelector('main');
                const pieces = [backdrop(document.querySelector('.game-description')), backdrop(document.querySelector('.podium'))];
                if (main.dataset.homepageVisitLayout === 'repeat') {
                    pieces.push(backdrop(document.querySelector('.lwc-highlights-countdown-wrapper')));
                }
                return {
                    Layout: main.dataset.homepageVisitLayout,
                    Header: getComputedStyle(document.querySelector('header[role="banner"]')).backgroundColor,
                    Colors: pieces.map(piece => piece.color),
                    Widths: pieces.map(piece => piece.width),
                    ViewportWidth: document.documentElement.clientWidth,
                    ScrollWidth: document.documentElement.scrollWidth
                };
            }
            """);

        Assert.Equal(visits > 3 ? "repeat" : "default", stage.Layout);
        Assert.All(stage.Colors, color => Assert.Equal(stage.Header, color));
        Assert.All(stage.Widths, backdropWidth => Assert.True(backdropWidth >= stage.ViewportWidth,
            $"A stage backdrop is {backdropWidth}px wide in a {stage.ViewportWidth}px viewport."));
        Assert.InRange(stage.ScrollWidth, 0, stage.ViewportWidth);
    }

    [Fact]
    public async Task Stage_GivesWayToTheLightCanvasWhenThePodiumIsHidden()
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/?search=max", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".podium")).ToBeHiddenAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var description = page.Locator(".game-description");
        Assert.Equal("none", await description.EvaluateAsync<string>("element => getComputedStyle(element, '::before').content"));
        var toolbarGap = await page.EvaluateAsync<double>(
            "() => document.querySelector('.leaderboard-toolbar').getBoundingClientRect().top - document.querySelector('.game-description').getBoundingClientRect().bottom");
        Assert.True(toolbarGap >= 16, $"The ranking controls sit only {toolbarGap}px below the tagline.");
    }

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

    private sealed class StageMeasurement
    {
        public string Layout { get; set; } = "";
        public string Header { get; set; } = "";
        public string[] Colors { get; set; } = [];
        public double[] Widths { get; set; } = [];
        public double ViewportWidth { get; set; }
        public double ScrollWidth { get; set; }
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
