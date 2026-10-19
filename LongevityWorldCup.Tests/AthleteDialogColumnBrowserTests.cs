using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteDialogColumnBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, 844, false, 100)]
    [InlineData(360, 844, true, 100)]
    [InlineData(390, 844, false, 100)]
    [InlineData(390, 844, true, 100)]
    [InlineData(600, 900, false, 100)]
    [InlineData(768, 1024, true, 100)]
    [InlineData(844, 390, false, 100)]
    [InlineData(932, 480, true, 100)]
    [InlineData(1440, 900, false, 100)]
    [InlineData(1440, 900, true, 100)]
    [InlineData(320, 844, true, 200)]
    [InlineData(390, 844, false, 200)]
    [InlineData(768, 1024, false, 200)]
    [InlineData(769, 900, true, 200)]
    [InlineData(844, 390, true, 200)]
    [InlineData(933, 480, false, 200)]
    public async Task ProfileRankTiles_AlignAndKeepTextInsideEachTile(
        int width, int height, bool dark, int textPercent)
    {
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width <= 932,
            HasTouch = width <= 932
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/michael-lustgarten", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("""
            () => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'michael-lustgarten'
                && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')
                && document.querySelectorAll('#athleteRankings a').length >= 2
            """);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        await AssertRankLayoutAsync(page, $"{width}x{height}, dark={dark}, {textPercent}% text, real profile");

        // Exercise the shared layout with one, two and several views, including
        // the longest current labels and longer placements as the field grows.
        // Rows may wrap: readability and the section edges are the invariant.
        foreach (var count in new[] { 1, 2, 4 })
        {
            await page.Locator("#athleteRankings").EvaluateAsync("""
                (rankings, count) => {
                    const template = rankings.querySelector('a').cloneNode(true);
                    const views = [
                        ['Ultimate League', '#1234'],
                        ['Pheno Improvement', '#199'],
                        ['Bortz Improvement', '#299'],
                        ['Crowd Age', '#198']
                    ];
                    rankings.replaceChildren(...views.slice(0, count).map(([label, rank]) => {
                        const link = template.cloneNode(true);
                        link.querySelector('span').textContent = label;
                        link.querySelector('strong').textContent = rank;
                        link.setAttribute('aria-label', `${label} rank ${rank.slice(1)}`);
                        return link;
                    }));
                }
                """, count);
            await AssertRankLayoutAsync(page, $"{width}x{height}, dark={dark}, {textPercent}% text, {count} tiles");
        }

        var links = page.Locator("#athleteRankings a");
        await links.First.FocusAsync();
        for (var i = 0; i < await links.CountAsync(); i++)
        {
            var link = links.Nth(i);
            await Assertions.Expect(link).ToBeFocusedAsync();
            Assert.True(await link.EvaluateAsync<bool>("""
                link => {
                    const style = getComputedStyle(link);
                    return style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) >= 2;
                }
                """));
            await page.Keyboard.PressAsync("Tab");
        }

        var modal = page.Locator("#detailsModal .modal-content");
        await modal.EvaluateAsync("element => element.scrollTop = element.scrollHeight");
        await Assertions.Expect(page.Locator("#modalStickyHeader")).ToBeVisibleAsync();
        Assert.True(await modal.EvaluateAsync<bool>("element => element.scrollTop > 0 && element.scrollWidth <= element.clientWidth + 1"),
            $"{width}x{height}, {textPercent}% text: the dialog must scroll vertically without horizontal overflow.");
    }

    private static async Task AssertRankLayoutAsync(IPage page, string scenario)
    {
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const ranks = document.getElementById('athleteRankings').getBoundingClientRect();
                const section = document.querySelector('#detailsModal .athlete-info').getBoundingClientRect();
                return [ranks.left, ranks.right, section.left, section.right];
            }
            """);
        Assert.True(Math.Abs(layout[0] - layout[2]) <= 1 && Math.Abs(layout[1] - layout[3]) <= 1,
            $"{scenario}: the rank tiles span {layout[0]:F0}-{layout[1]:F0}px but the profile sections {layout[2]:F0}-{layout[3]:F0}px.");
        var problems = await page.Locator("#athleteRankings").EvaluateAsync<string[]>("""
            rankings => {
                const problems = [];
                const column = rankings.getBoundingClientRect();
                const tiles = [...rankings.querySelectorAll('a')];
                const boxes = tiles.map(tile => tile.getBoundingClientRect());
                tiles.forEach((tile, index) => {
                    const box = boxes[index];
                    if (box.width < 44 || box.height < 44) problems.push('small touch target');
                    if (box.left < column.left - 1 || box.right > column.right + 1)
                        problems.push('tile leaves the section column');
                    if (tile.scrollWidth > tile.clientWidth + 1) problems.push('tile overflows');
                    const textBounds = [...tile.querySelectorAll('span, strong')].map(element => {
                        const range = document.createRange();
                        range.selectNodeContents(element);
                        const rects = [...range.getClientRects()];
                        if (rects.some(rect => rect.left < box.left - 1 || rect.right > box.right + 1
                            || rect.top < box.top - 1 || rect.bottom > box.bottom + 1))
                            problems.push(`${element.textContent}: text leaves the tile`);
                        return range.getBoundingClientRect();
                    });
                    if (textBounds[0].bottom > textBounds[1].top + 1) problems.push('label overlaps rank');
                    if (boxes.slice(index + 1).some(other => box.left < other.right - 1
                        && box.right > other.left + 1 && box.top < other.bottom - 1 && box.bottom > other.top + 1))
                        problems.push('tiles overlap');
                });
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{scenario}: {string.Join(", ", problems)}");
    }
}
