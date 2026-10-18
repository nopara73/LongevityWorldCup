using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DashboardPhoneDockBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390, 844, false, false)]
    [InlineData(360, 780, true, false)]
    [InlineData(360, 780, false, true)]
    [InlineData(320, 640, true, true)]
    public async Task PhoneDock_ShowsEveryCommandWholeAndReadable(int width, int height, bool dark, bool isPro)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        await context.AddInitScriptAsync(
            """
            (isPro => {
                const athlete = {
                    Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete', PersonalLink: 'https://example.test/browser-test-athlete',
                    DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: isPro ? [{ Date: '2026-06-19', Hba1cMmolMol: 35 }] : []
                };
                sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
                localStorage.setItem('selectedAthleteName', athlete.Name);
                localStorage.setItem('hasApplication', 'true');
                localStorage.setItem('gmaHasPerfectGuess', '1');
            })(
            """ + (isPro ? "true" : "false") + ")");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/dashboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('athleteDashboardPanel')?.hidden === false && document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        await Assertions.Expect(page.Locator("#athleteDashboardActions")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("flow-action-stack--docked"));

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                for (const action of document.querySelectorAll('#athleteDashboardActions .flow-action')) {
                    if (!action.offsetParent || action.id === 'playDashboardBackBtn') continue;
                    const box = action.getBoundingClientRect();
                    const label = action.querySelector('.flow-action__label') || action;
                    const name = action.textContent.trim().replace(/\s+/g, ' ');
                    const range = document.createRange();
                    range.selectNodeContents(label);
                    const text = range.getBoundingClientRect();
                    if (text.left < box.left || text.right > box.right) problems.push(`${name}: label clipped by its button`);
                    const icon = action.querySelector(':scope > i');
                    if (icon && icon.getBoundingClientRect().width > 0) {
                        const glyph = icon.getBoundingClientRect();
                        if (glyph.left < text.right && glyph.right > text.left) problems.push(`${name}: label runs into its icon`);
                    }
                    if (parseFloat(getComputedStyle(label).fontSize) < 13.5) problems.push(`${name}: ${getComputedStyle(label).fontSize} label is too small to read`);
                }
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page scrolls sideways');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{(isPro ? "pro" : "amateur")} {width}x{height}: {string.Join("; ", problems)}");
        await Assertions.Expect(page.Locator("#playDashboardBackBtn")).ToHaveAccessibleNameAsync("Change athlete");
    }
}
