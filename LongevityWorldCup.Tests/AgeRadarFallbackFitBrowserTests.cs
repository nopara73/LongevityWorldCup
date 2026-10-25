using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class AgeRadarFallbackFitBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(160, 16, false)]
    [InlineData(195, 16, true)]
    [InlineData(320, 32, false)]
    [InlineData(320, 32, true)]
    public async Task SingleReadingSummary_FitsTheProfileWithoutClippingOrHorizontalScrolling(int width, int rootSize, bool dark)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 640 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/benjamin-garden");
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading') && document.querySelector('#ageRadarFallbackValue').textContent.includes('years')");
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {rootSize}px !important; }}" });
        await page.EvaluateAsync("""
            async () => {
                await document.fonts.ready;
                // Exercise the real summary and its populated text in the
                // single-reading state, independent of chart-load timing.
                document.getElementById('ageRadarWrapper').style.display = 'none';
                document.getElementById('ageRadarFallback').style.display = 'flex';
            }
            """);
        var issues = await page.EvaluateAsync<string[]>("""
            () => {
                const issues = [], card = document.querySelector('#detailsModal .modal-content');
                const summary = document.getElementById('ageRadarFallback'), parent = summary.parentElement;
                const style = getComputedStyle(parent);
                const available = parent.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                if (summary.getBoundingClientRect().width > available + 1) issues.push('summary exceeds its card content width');
                if (summary.scrollWidth > summary.clientWidth + 1 || summary.scrollHeight > summary.clientHeight + 1) issues.push('summary text overflows');
                const box = summary.getBoundingClientRect();
                for (const label of summary.querySelectorAll('span')) {
                    const r = label.getBoundingClientRect();
                    if (r.top < box.top || r.bottom > box.bottom || r.left < box.left || r.right > box.right) issues.push('summary label is outside its surface');
                }
                if (card.scrollWidth > card.clientWidth + 1) issues.push('profile requires horizontal scrolling');
                return issues;
            }
            """);
        Assert.Empty(issues);
    }
}
