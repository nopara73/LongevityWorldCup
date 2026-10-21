using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BioageBiomarkerSheetBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", 1440, 900, 100)]
    [InlineData("/bortz-age", 1440, 900, 100)]
    [InlineData("/pheno-age", 1440, 900, 200)]
    [InlineData("/pheno-age", 768, 1024, 100)]
    [InlineData("/bortz-age", 390, 844, 100)]
    [InlineData("/pheno-age", 320, 844, 200)]
    [InlineData("/bortz-age", 320, 844, 200)]
    [InlineData("/bortz-age", 320, 844, 100)]
    [InlineData("/bortz-age", 195, 844, 100)]
    [InlineData("/bortz-age", 160, 844, 100)]
    [InlineData("/bortz-age", 240, 844, 100)]
    [InlineData("/bortz-age", 768, 1024, 200)]
    public async Task BiomarkerSheet_KeepsEveryValueOpenAndTypableInOrder(string path, int width, int height, int textPercent)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const ctx = document.createElement('canvas').getContext('2d');
                const fits = (element, text) => {
                    const style = getComputedStyle(element);
                    ctx.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                    const available = element.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                    return ctx.measureText(text).width <= available + 1;
                };
                for (const legend of document.querySelectorAll('#lwc-step-2 > fieldset > legend')) {
                    const title = [...legend.childNodes].find(node => node.nodeType === Node.TEXT_NODE && node.textContent.trim());
                    if (!title) continue;
                    const range = document.createRange();
                    range.setStart(title, title.textContent.search(/\S/));
                    range.setEnd(title, title.textContent.trimEnd().length);
                    const lines = new Set([...range.getClientRects()].filter(rect => rect.width > 1).map(rect => Math.round(rect.top)));
                    if (lines.size > 2) issues.push(`${title.textContent.trim()}: domain title is squeezed into ${lines.size} lines`);
                    for (const word of title.textContent.matchAll(/\S+/g)) {
                        range.setStart(title, word.index);
                        range.setEnd(title, word.index + word[0].length);
                        const wordLines = new Set([...range.getClientRects()].filter(rect => rect.width > 1).map(rect => Math.round(rect.top)));
                        if (wordLines.size > 1) issues.push(`${word[0]}: domain word is broken across lines`);
                    }
                }
                for (const card of document.querySelectorAll('#lwc-step-2 .biomarker-card')) {
                    const header = card.querySelector('.biomarker-card-header');
                    const name = header.childNodes[0].textContent.trim();
                    const input = card.querySelector('input[type="number"]');
                    const nameRect = (() => { const r = document.createRange(); r.selectNodeContents(header.childNodes[0]); return r.getBoundingClientRect(); })();
                    const headerRect = header.getBoundingClientRect();
                    const markerRect = header.querySelector('.toggle-icon').getBoundingClientRect();
                    const inputRect = input.getBoundingClientRect();
                    if (inputRect.height < 40 || inputRect.width < 60) issues.push(`${name}: value field is collapsed`);
                    if (nameRect.width < 20) issues.push(`${name}: name is squeezed away`);
                    if (nameRect.left < headerRect.left - 1 || nameRect.right > headerRect.right + 1) issues.push(`${name}: name is clipped`);
                    if (markerRect.left < headerRect.left - 1 || markerRect.right > headerRect.right + 1) issues.push(`${name}: completion marker is clipped`);
                    if (!fits(input, input.value || input.placeholder)) issues.push(`${name}: value text is clipped`);
                    const unit = card.querySelector('select');
                    if (unit.getBoundingClientRect().width) {
                        for (const option of unit.options) {
                            if (!fits(unit, option.textContent.trim())) issues.push(`${name}: unit ${option.textContent.trim()} is clipped`);
                        }
                    }
                    const fixedUnit = card.querySelector('.bioage-fixed-unit');
                    if (fixedUnit && !fits(fixedUnit, fixedUnit.textContent.trim())) issues.push(`${name}: fixed unit is clipped`);
                    if (header.getAttribute('role') === 'button' || header.hasAttribute('aria-expanded')) issues.push(`${name}: header still acts as a toggle`);
                }
                if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('page overflows');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{path} @ {width}px / {textPercent}%: {string.Join(", ", problems)}");

        // Keyboard entry runs value, unit, next value without stopping on headers.
        // Phones jump straight from value to value.
        await page.Locator("#wbc").FocusAsync();
        if (width > 600)
        {
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(page.Locator("#wbcUnit")).ToBeFocusedAsync();
        }
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#lwc-step-2 .biomarker-card").Nth(1).Locator("input[type=number]")).ToBeFocusedAsync();
        await AssertPlainBiomarkerHeadersAsync(page);

        // Focus, validation, and draft restoration must not revive accordion semantics.
        await page.Locator("#wbc").FillAsync("6.54");
        await page.Locator("#wbc").FillAsync("");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#wbc-entry-error")).ToBeVisibleAsync();
        await AssertPlainBiomarkerHeadersAsync(page);
        await page.Locator("#wbc").FillAsync("6.54");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#wbc-entry-error")).ToBeHiddenAsync();
        await AssertPlainBiomarkerHeadersAsync(page);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#wbc")).ToHaveValueAsync("6.54");
        await Assertions.Expect(page.Locator("#wbc")).ToBeVisibleAsync();
        await AssertPlainBiomarkerHeadersAsync(page);
        Assert.Empty(errors);
    }

    private static async Task AssertPlainBiomarkerHeadersAsync(IPage page)
    {
        await Assertions.Expect(page.Locator(".biomarker-card-header[aria-expanded], .biomarker-card-header[role=button], .biomarker-card-header[tabindex]"))
            .ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("/pheno-age", 1440, 900)]
    [InlineData("/bortz-age", 1366, 768)]
    public async Task DesktopDates_LineUpTheBloodDrawDateWithTheBirthDate(string path, int width, int height)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("() => document.querySelector('.bioage-biomarker-entry-ready')");

        var rows = await page.EvaluateAsync<double[]>(
            """
            () => [
                document.querySelector('#dobFieldset > legend').getBoundingClientRect().top,
                document.querySelector('#lwc-step-1 > fieldset:nth-of-type(2) > legend').getBoundingClientRect().top,
                document.querySelector('#dob-year').getBoundingClientRect().top,
                document.querySelector('#blood-draw-date').getBoundingClientRect().top,
                document.querySelector('#dob-year').getBoundingClientRect().bottom,
                document.querySelector('#blood-draw-date').getBoundingClientRect().bottom
            ]
            """);
        Assert.InRange(rows[1] - rows[0], -1, 1);
        Assert.InRange(rows[3] - rows[2], -1, 1);
        Assert.InRange(rows[5] - rows[4], -1, 1);
    }
}
