using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PrivacyPolicyBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(1280, 720, "dark")]
    [InlineData(820, 1180, "light")]
    [InlineData(390, 844, "dark")]
    [InlineData(320, 640, "light")]
    public async Task Policy_ReadsAsOneFullWidthColumnWithPhoneEdgeToEdgeCard(int width, int height, string theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/privacy");
        await page.EvaluateAsync("() => document.fonts.ready");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.querySelector('.card').getBoundingClientRect();
                let narrowest = Infinity;
                for (const paragraph of document.querySelectorAll('.card p')) {
                    const parent = paragraph.parentElement, style = getComputedStyle(parent);
                    const column = parent.getBoundingClientRect().width - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                    narrowest = Math.min(narrowest, paragraph.getBoundingClientRect().width / column);
                }
                return [narrowest, card.left, card.right, innerWidth];
            }
            """);
        var scenario = $"{width}x{height} {theme}";
        Assert.True(layout[0] >= 0.97, $"{scenario}: policy paragraphs should fill their column instead of leaving a ragged empty gutter ({layout[0]:0.00}).");
        if (width <= 560)
            Assert.True(layout[1] <= 0.5 && layout[2] >= layout[3] - 0.5, $"{scenario}: the policy card should run edge to edge on phones ({layout[1]:0.0}-{layout[2]:0.0} of {layout[3]}).");
        await ExpectNoHorizontalOverflowAsync(page);
    }
}
