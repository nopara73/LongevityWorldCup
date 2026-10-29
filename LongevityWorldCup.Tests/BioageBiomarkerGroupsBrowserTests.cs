using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BioageBiomarkerGroupsBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", 1440, 900, false)]
    [InlineData("/bortz-age", 1440, 900, true)]
    [InlineData("/pheno-age", 390, 844, true)]
    [InlineData("/bortz-age", 320, 640, false)]
    public async Task BiomarkerEntry_GroupsEachDomainIntoRuledRows(string path, int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        await page.WaitForFunctionAsync("() => document.querySelector('.bioageform')?.classList.contains('bioage-biomarker-entry-ready')");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const groups = [...document.querySelectorAll('#lwc-step-2 > fieldset')].filter(group => group.querySelector('.biomarker-card'));
                if (groups.length < 3) problems.push(`only ${groups.length} domains found`);
                for (const group of groups) {
                    const name = group.querySelector('legend')?.textContent.trim();
                    const cards = [...group.querySelectorAll(':scope > .biomarker-card')];
                    const radius = card => parseFloat(getComputedStyle(card).borderTopLeftRadius);
                    // Each domain is a ruled block of rows: its title band rules off the first row,
                    // and the last row is closed by its own rule or by the next domain's band.
                    const band = group.querySelector(':scope > legend');
                    if (parseFloat(getComputedStyle(band).borderBottomWidth) < 1) problems.push(`${name}: the title band should rule off the first row`);
                    const nextBand = group.nextElementSibling?.matches('fieldset') ? group.nextElementSibling.querySelector(':scope > legend') : null;
                    const closing = Math.max(
                        parseFloat(getComputedStyle(cards[cards.length - 1]).borderBottomWidth),
                        nextBand ? parseFloat(getComputedStyle(nextBand).borderTopWidth) : 0);
                    if (closing < 1) problems.push(`${name}: the last row should close the domain with a rule`);
                    for (let i = 0; i < cards.length; i++) {
                        if (radius(cards[i]) > 0.5) problems.push(`${name}: rows should not be rounded boxes`);
                    }
                    for (let i = 1; i < cards.length; i++) {
                        const gap = cards[i].getBoundingClientRect().top - cards[i - 1].getBoundingClientRect().bottom;
                        if (gap > 0.5) problems.push(`${name}: a ${gap.toFixed(0)}px gap splits its rows into separate boxes`);
                    }
                }
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page scrolls sideways');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{path} {width}x{height}: {string.Join("; ", problems)}");
    }

    [Theory]
    [InlineData("/pheno-age", 1440, 900, 200)]
    [InlineData("/pheno-age", 320, 844, 200)]
    [InlineData("/bortz-age", 320, 844, 200)]
    [InlineData("/pheno-age", 390, 844, 100)]
    [InlineData("/bortz-age", 844, 390, 100)]
    public async Task GroupedRows_KeepTextControlsAndValidationInsideTheRow(
        string path, int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 960
        });
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);
        await page.GotoAsync(path);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");
        if (path == "/pheno-age") await FillPhenoBiomarkersAsync(page);
        else await FillBortzBiomarkersAsync(page);
        var total = path == "/pheno-age" ? 9 : 22;
        await Assertions.Expect(page.Locator(".biomarker-card--complete")).ToHaveCountAsync(total);
        await Assertions.Expect(page.Locator(".bioage-biomarker-progress")).ToHaveTextAsync($"{total} of {total} biomarkers entered");
        await Assertions.Expect(page.Locator("#calculateBioageButton")).ToBeEnabledAsync();

        // Exercise every actual label/error, including long Bortz names. Empty
        // geometry alone cannot detect a clipped error or a lost completion mark.
        var inputs = page.Locator("#lwc-step-2 .biomarker-card input[type=number]");
        for (var index = 0; index < total; index++)
        {
            var input = inputs.Nth(index);
            var id = await input.GetAttributeAsync("id");
            var value = await input.InputValueAsync();
            var fontSize = await input.EvaluateAsync<double>("input => parseFloat(getComputedStyle(input).fontSize)");
            Assert.InRange(fontSize, 16 * textPercent / 100d - 0.5, double.MaxValue);
            Assert.False(string.IsNullOrWhiteSpace(await input.GetAttributeAsync("aria-label")));
            await input.FillAsync("");
            await input.PressAsync("Tab");
            var error = page.Locator($"#{id}-entry-error");
            await Assertions.Expect(error).ToBeVisibleAsync();
            await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "true");
            await Assertions.Expect(page.Locator(".biomarker-card--complete")).ToHaveCountAsync(total - 1);
            await Assertions.Expect(page.Locator(".bioage-biomarker-progress")).ToHaveTextAsync($"{total - 1} of {total} biomarkers entered");
            await Assertions.Expect(page.Locator("#calculateBioageButton")).ToBeDisabledAsync();

            var problems = await input.EvaluateAsync<string[]>(
                """
                input => {
                    const issues = [];
                    const card = input.closest('.biomarker-card');
                    const bounds = card.getBoundingClientRect();
                    const inside = (r, name) => {
                        if (r.left < bounds.left - 1 || r.right > bounds.right + 1 ||
                            r.top < bounds.top - 1 || r.bottom > bounds.bottom + 1) issues.push(`${name} is clipped`);
                    };
                    for (const element of card.querySelectorAll('.biomarker-card-header, .bioage-biomarker-error:not([hidden]), .bioage-fixed-unit')) {
                        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                        let node;
                        while (node = walker.nextNode()) {
                            if (!node.textContent.trim()) continue;
                            const range = document.createRange();
                            range.selectNodeContents(node);
                            for (const rect of range.getClientRects()) if (rect.width && rect.height) inside(rect, node.textContent.trim());
                        }
                    }
                    for (const element of card.querySelectorAll('input, select, .toggle-icon')) {
                        const rect = element.getBoundingClientRect();
                        if (!rect.width) continue;
                        inside(rect, element.id || 'completion badge');
                        if (element.matches('input, select') && rect.height < 44) issues.push('control target is too small');
                    }
                    if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('page scrolls sideways');
                    return issues;
                }
                """);
            Assert.True(problems.Length == 0, $"{path} {width}px {textPercent}% {id}: {string.Join("; ", problems)}");
            await input.FillAsync(value);
            await input.PressAsync("Tab");
            await Assertions.Expect(error).ToBeHiddenAsync();
        }
        await Assertions.Expect(page.Locator(".biomarker-card--complete")).ToHaveCountAsync(total);
        await Assertions.Expect(page.Locator("#calculateBioageButton")).ToBeEnabledAsync();
        Assert.Empty(errors);
    }
}
