using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class FlowAccessibilityBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", 320)]
    [InlineData("/pheno-age", 390)]
    [InlineData("/pheno-age", 1280)]
    [InlineData("/bortz-age", 320)]
    [InlineData("/bortz-age", 390)]
    [InlineData("/bortz-age", 1280)]
    [InlineData("/apply?fake=1", 320)]
    [InlineData("/apply?fake=1", 390)]
    [InlineData("/apply?fake=1", 1280)]
    public async Task JourneyProgress_EnlargedTextKeepsEveryLabelInsideItsStage(string route, int width)
    {
        await using var context = await NewContextAsync(width);
        var page = await NavigateAsync(context, route);
        await SetTextScaleAsync(page);

        var labels = await page.Locator("#mainProgressBar .stage-label").CountAsync();
        Assert.Equal(3, labels);
        for (var index = 0; index < labels; index++)
        {
            var geometry = await page.Locator("#mainProgressBar .stage-label").Nth(index)
                .EvaluateAsync<ContainedText>(
                    """
                    element => {
                        const range = document.createRange();
                        range.selectNodeContents(element);
                        const text = range.getBoundingClientRect();
                        const stage = element.parentElement.getBoundingClientRect();
                        const progress = element.closest('.progress-container').getBoundingClientRect();
                        return {
                            Text: element.textContent.trim(),
                            Fits: text.left >= stage.left - 1 && text.right <= stage.right + 1
                                && text.top >= stage.top - 1 && text.bottom <= stage.bottom + 1
                                && text.bottom <= progress.bottom + 1
                                && stage.left >= 0 && stage.right <= innerWidth,
                            Detail: `text=${JSON.stringify(text)}, stage=${JSON.stringify(stage)}`
                        };
                    }
                    """);
            Assert.True(geometry.Fits, $"{route} at {width}px: {geometry.Text} escapes its stage. {geometry.Detail}");
        }
    }

    [Theory]
    [InlineData("/pheno-age", 320, 100)]
    [InlineData("/pheno-age", 390, 100)]
    [InlineData("/bortz-age", 320, 100)]
    [InlineData("/bortz-age", 390, 100)]
    [InlineData("/pheno-age", 320, 200)]
    [InlineData("/pheno-age", 390, 200)]
    [InlineData("/pheno-age", 1280, 200)]
    [InlineData("/bortz-age", 320, 200)]
    [InlineData("/bortz-age", 390, 200)]
    [InlineData("/bortz-age", 1280, 200)]
    public async Task DateHints_StayWithinTheirOwnLabel(string route, int width, int textScale)
    {
        await using var context = await NewContextAsync(width);
        var page = await NavigateAsync(context, route);
        await SetTextScaleAsync(page, textScale);

        var hints = page.Locator("#dobFieldset label span");
        Assert.Equal(3, await hints.CountAsync());
        for (var index = 0; index < 3; index++)
        {
            var geometry = await hints.Nth(index).EvaluateAsync<ContainedText>(
                """
                element => {
                    const label = element.closest('label').getBoundingClientRect();
                    const range = document.createRange();
                    range.selectNodeContents(element);
                    const fragments = [...range.getClientRects()];
                    return {
                        Text: element.textContent.trim(),
                        Fits: fragments.length > 0 && fragments.every(text =>
                            text.left >= label.left - 1 && text.right <= label.right + 1),
                        Detail: `fragments=${JSON.stringify(fragments)}, label=${JSON.stringify(label)}`
                    };
                }
                """);
            Assert.True(geometry.Fits, $"{route} at {width}px: {geometry.Text} overlaps another date field. {geometry.Detail}");
        }
    }

    [Theory]
    [InlineData("/pheno-age", ColorScheme.Light, false)]
    [InlineData("/pheno-age", ColorScheme.Dark, false)]
    [InlineData("/pheno-age", ColorScheme.Light, true)]
    [InlineData("/bortz-age", ColorScheme.Light, false)]
    [InlineData("/bortz-age", ColorScheme.Dark, false)]
    [InlineData("/bortz-age", ColorScheme.Light, true)]
    public async Task CalculatorStepNavigation_RemainsVisibleAcrossThemes(
        string route, ColorScheme theme, bool forcedColors)
    {
        await using var context = await NewContextAsync(390, theme, forcedColors);
        var page = await NavigateAsync(context, route);
        await page.WaitForFunctionAsync("() => document.getElementById('dob-year')?.options.length > 1");

        await AssertStepMarkersVisibleAsync(page, route, theme, forcedColors);
        await page.Locator("#dob-year").SelectOptionAsync("1980");
        await page.Locator("#blood-draw-date").FillAsync("2026-09-29");
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('lwcDot1').getAttribute('aria-disabled') === 'false'");
        await AssertStepMarkersVisibleAsync(page, route, theme, forcedColors);
    }

    private async Task<IBrowserContext> NewContextAsync(
        int width, ColorScheme theme = ColorScheme.Light, bool forcedColors = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.Reduce,
            ColorScheme = theme,
            ForcedColors = forcedColors ? ForcedColors.Active : ForcedColors.None,
            ViewportSize = new ViewportSize { Width = width, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }

    private static async Task<IPage> NavigateAsync(IBrowserContext context, string route)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(route, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.fonts.status === 'loaded' && !document.documentElement.classList.contains('play-panel-transitioning')");
        return page;
    }

    private static async Task SetTextScaleAsync(IPage page, int percent = 200)
    {
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", percent);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }

    private static async Task AssertStepMarkersVisibleAsync(
        IPage page, string route, ColorScheme theme, bool forcedColors)
    {
        var markers = await page.Locator(".lwc-wizard-nav .lwc-dot").EvaluateAllAsync<MarkerContrast[]>(
            """
            elements => {
                const rgb = value => value.match(/[\d.]+/g).map(Number);
                const luminance = color => color.slice(0, 3).reduce((sum, channel, index) => {
                    const normalized = channel / 255;
                    const linear = normalized <= 0.04045
                        ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4;
                    return sum + linear * [0.2126, 0.7152, 0.0722][index];
                }, 0);
                const ratio = (color, background) => {
                    const first = luminance(color), second = luminance(background);
                    return (Math.max(first, second) + 0.05) / (Math.min(first, second) + 0.05);
                };
                return elements.map(element => {
                    const marker = getComputedStyle(element, '::before');
                    const surface = rgb(getComputedStyle(element.closest('.bioageform')).backgroundColor);
                    const opacity = Number(marker.opacity) * Number(getComputedStyle(element).opacity);
                    const effective = value => {
                        const color = rgb(value), alpha = (color[3] ?? 1) * opacity;
                        return color.slice(0, 3).map((channel, index) =>
                            channel * alpha + surface[index] * (1 - alpha));
                    };
                    const backgroundRatio = ratio(effective(marker.backgroundColor), surface);
                    const borderRatio = parseFloat(marker.borderTopWidth) > 0
                        && marker.borderTopStyle !== 'none'
                        ? ratio(effective(marker.borderTopColor), surface) : 1;
                    return {
                        Id: element.id,
                        Ratio: Math.max(backgroundRatio, borderRatio),
                        Width: parseFloat(marker.width),
                        Height: parseFloat(marker.height)
                    };
                });
            }
            """);
        Assert.Equal(2, markers.Length);
        foreach (var marker in markers)
        {
            Assert.True(marker.Width > 0 && marker.Height > 0 && marker.Ratio >= 3,
                $"{route} {theme} forcedColors={forcedColors}: {marker.Id} contrast is {marker.Ratio:F2}:1.");
        }
    }

    private sealed class ContainedText
    {
        public string Text { get; set; } = "";
        public bool Fits { get; set; }
        public string Detail { get; set; } = "";
    }

    private sealed class MarkerContrast
    {
        public string Id { get; set; } = "";
        public double Ratio { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
