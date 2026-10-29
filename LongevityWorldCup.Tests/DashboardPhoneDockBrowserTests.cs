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
    [InlineData(390, 844, false, false, 100)]
    [InlineData(390, 844, true, true, 100)]
    [InlineData(360, 780, true, false, 100)]
    [InlineData(360, 780, false, true, 100)]
    [InlineData(320, 640, false, false, 100)]
    [InlineData(320, 640, true, true, 100)]
    [InlineData(390, 844, false, false, 200)]
    [InlineData(390, 844, true, true, 200)]
    [InlineData(320, 640, true, false, 200)]
    [InlineData(320, 640, false, true, 200)]
    [InlineData(844, 390, true, false, 100)]
    [InlineData(844, 390, false, true, 200)]
    [InlineData(820, 1180, false, true, 200)]
    [InlineData(1440, 900, true, false, 200)]
    public async Task DashboardCommands_KeepIconsAndFullLabelsAtEveryTextSize(
        int width, int height, bool dark, bool isPro, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 960,
            HasTouch = width < 960
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
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && [...document.querySelectorAll('link[rel=stylesheet]')].every(link => link.sheet)");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        var problems = await page.EvaluateAsync<string[]>(
            $$"""
            percent => {
                const problems = [];
                const ink = {{BrowserIconAssertions.Ink}};
                let visibleIcons = 0;
                for (const action of document.querySelectorAll('#athleteDashboardActions .flow-action')) {
                    const box = action.getBoundingClientRect();
                    if (!box.width || !box.height) { problems.push('hidden dashboard command'); continue; }
                    const icon = action.querySelector(':scope > i');
                    const glyph = ink(icon);
                    const style = icon && getComputedStyle(icon);
                    if (!glyph?.width || !glyph.height || style.visibility === 'hidden' || Number(style.opacity) === 0)
                        problems.push(`${action.textContent.trim()}: missing visible command icon`);
                    else {
                        visibleIcons++;
                        if (glyph.left < box.left || glyph.right > box.right || glyph.top < box.top || glyph.bottom > box.bottom)
                            problems.push(`${action.textContent.trim()}: icon clipped by its button`);
                    }
                    if (box.width < 44 || box.height < 44) problems.push('command target is smaller than 44px');
                    if (action.id === 'playDashboardBackBtn') continue;
                    const label = action.querySelector('.flow-action__label') || action;
                    const name = action.textContent.trim().replace(/\s+/g, ' ');
                    const range = document.createRange();
                    range.selectNodeContents(label);
                    for (const text of range.getClientRects()) {
                        if (!text.width || !text.height) continue;
                        if (text.left < box.left - 1 || text.right > box.right + 1 || text.top < box.top - 1 || text.bottom > box.bottom + 1)
                            problems.push(`${name}: label clipped by its button`);
                        if (glyph && glyph.left < text.right && glyph.right > text.left && glyph.top < text.bottom && glyph.bottom > text.top)
                            problems.push(`${name}: label runs into its icon`);
                    }
                    if (parseFloat(getComputedStyle(label).fontSize) < 13.5 * percent / 100)
                        problems.push(`${name}: text enlargement was suppressed`);
                }
                if (visibleIcons !== 5) problems.push(`expected five real command icons, found ${visibleIcons}`);
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page scrolls sideways');
                return problems;
            }
            """, textPercent);
        Assert.True(problems.Length == 0, $"{(isPro ? "pro" : "amateur")} {width}x{height}, {textPercent}% text: {string.Join("; ", problems)}");
        await Assertions.Expect(page.Locator("#playDashboardBackBtn")).ToHaveAccessibleNameAsync("Change athlete");
        var labels = (await page.Locator("#athleteDashboardActions .flow-action__label").AllTextContentsAsync())
            .Select(label => label.Replace('\u00a0', ' ')).ToArray();
        Assert.Contains("Edit profile", labels);
        Assert.Contains("Longevitymaxxing", labels);
        Assert.Contains(isPro ? "Update Pheno Age" : "Submit new results", labels);
        if (isPro) Assert.Contains("Update Bortz Age", labels);
    }
}
