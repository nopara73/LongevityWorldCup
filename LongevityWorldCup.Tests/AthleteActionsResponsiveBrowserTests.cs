using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteActionsResponsiveBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/athlete/michael-lustgarten", 390, 100)]
    [InlineData("/athlete/michael-lustgarten", 320, 200)]
    [InlineData("/athlete/michael-lustgarten", 390, 200)]
    [InlineData("/about", 390, 200)]
    public async Task ProfileActions_ContainTheirLabelsAndKeepTheShareMenuAccessible(string path, int width, int textPercent)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 844);
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (path == "/about")
        {
            await page.Locator("main.documentation-page a[href='/athlete/michael-lustgarten']").First.ClickAsync();
        }
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'michael-lustgarten' && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var controls = page.Locator(".athlete-links > a:visible, #shareAthleteProfile");
        Assert.Equal(3, await controls.CountAsync());
        var problems = await controls.EvaluateAllAsync<string[]>(
            """
            controls => controls.flatMap(control => {
                const box = control.getBoundingClientRect();
                const range = document.createRange();
                range.selectNodeContents(control);
                const clipped = [...range.getClientRects()].some(rect =>
                    rect.left < box.left - 1 || rect.right > box.right + 1);
                return clipped || box.width < 44 || box.height < 44
                    ? [control.textContent.trim()] : [];
            })
            """);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}%: {string.Join(", ", problems)}");

        var share = page.Locator("#shareAthleteProfile");
        await share.ScrollIntoViewIfNeededAsync();
        await share.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.Locator("#athleteShareMenu").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        var menu = Assert.IsType<LocatorBoundingBoxResult>(await page.Locator("#athleteShareMenu").BoundingBoxAsync());
        Assert.True(menu.X >= 0 && menu.X + menu.Width <= width);
        await page.Keyboard.PressAsync("Escape");
        Assert.True(await page.Locator("#athleteShareMenu").IsHiddenAsync());
        Assert.True(await share.EvaluateAsync<bool>("element => document.activeElement === element"));
    }
}
