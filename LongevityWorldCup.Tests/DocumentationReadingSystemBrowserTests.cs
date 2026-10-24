using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// About, History, the Ruleset and the Privacy policy are one long-form reading system:
// the same cover, contents rail, chapter tiles, picture plates and closing band on every page.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DocumentationReadingSystemBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static readonly string[] Paths = ["/about", "/history", "/ruleset", "/privacy"];

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 768, true)]
    [InlineData(768, 1024, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task EveryPage_OpensOnTheSameCoverAndClosesOnTheSameBand(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var signatures = new List<string>();
        foreach (var path in Paths)
        {
            await NavigateAndSettleAsync(page, path);
            var scenario = $"{path} at {width}px, dark={dark}";
            var result = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const issues = [];
                    const article = document.querySelector('.documentation-document');
                    const box = article.getBoundingClientRect();
                    const border = parseFloat(getComputedStyle(article).borderLeftWidth);
                    const spans = element => {
                        const b = element.getBoundingClientRect();
                        return Math.abs(b.left - box.left - border) <= 1.5 && Math.abs(b.right - box.right + border) <= 1.5;
                    };
                    const cover = article.firstElementChild;
                    if (!cover?.matches('.documentation-cover')) issues.push('the document does not open on a cover');
                    else {
                        if (!cover.querySelector(':scope > h1')) issues.push('the title is not on the cover');
                        if (!cover.querySelector(':scope > p')) issues.push('the lede is not on the cover');
                        if (!spans(cover)) issues.push('the cover does not span the document');
                        if (Math.abs(cover.getBoundingClientRect().top - box.top - border) > 1.5) issues.push('the cover does not start at the top');
                        if (getComputedStyle(cover).backgroundColor === getComputedStyle(article).backgroundColor) issues.push('the cover is not a band');
                    }
                    const bar = getComputedStyle(document.querySelector('.documentation-document h1'), '::after');
                    if (bar.content === 'none' || parseFloat(bar.height) < 3) issues.push('the title has no accent stroke');
                    const next = document.querySelector('#where-to-go-next, #contact')?.parentElement;
                    if (!next?.matches('.documentation-next')) issues.push('the closing section is not in a band');
                    else {
                        if (!spans(next)) issues.push('the closing band does not span the document');
                        if (Math.abs(next.getBoundingClientRect().bottom - box.bottom + border) > 1.5) issues.push('the closing band does not end the document');
                        const tile = next.querySelector('ul > li');
                        if (tile && getComputedStyle(tile).backgroundColor === getComputedStyle(next).backgroundColor) issues.push('tiles do not stand out from the band');
                    }
                    const h1 = getComputedStyle(document.querySelector('.documentation-document h1'));
                    const nav = getComputedStyle(document.querySelector('.documentation-nav'));
                    const signature = [h1.fontSize, h1.textAlign, getComputedStyle(article).fontSize, getComputedStyle(article).lineHeight,
                        nav.borderTopWidth, nav.borderRadius, nav.backgroundColor].join('|');
                    return [signature, ...issues];
                }
                """);
            signatures.Add(result[0]);
            Assert.True(result.Length == 1, $"{scenario}: {string.Join("; ", result.Skip(1))}");
            await ExpectNoHorizontalOverflowAsync(page);
        }

        Assert.True(signatures.Distinct().Count() == 1, $"{width}px: the pages differ: {string.Join(" / ", signatures)}");
    }

    [Theory]
    [InlineData(1280, "/history", "#2026-january-15---longevity-world-cup-season-2025-ended")]
    [InlineData(1280, "/ruleset", "#competition-mechanics")]
    [InlineData(390, "/history", "#special-mentions")]
    [InlineData(390, "/ruleset", "#faq")]
    public async Task ContentsRail_FillsUpToTheSectionBeingReadOnEveryPage(int width, string path, string href)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        if (width <= 900)
            await page.Locator(".documentation-nav-toggle").ClickAsync();
        await page.Locator($".documentation-nav a[href='{href}']").ClickAsync();
        await page.WaitForFunctionAsync(
            "href => document.querySelector('.documentation-nav a.is-active')?.getAttribute('href') === href", href);

        var problems = await page.EvaluateAsync<string[]>(
            """
            href => {
                const issues = [];
                const accent = getComputedStyle(document.querySelector('.documentation-source-link')).color;
                const links = [...document.querySelectorAll('.documentation-nav a[href^="#"]')];
                const active = links.findIndex(link => link.getAttribute('href') === href);
                links.forEach((link, index) => {
                    const style = getComputedStyle(link);
                    if (parseFloat(style.borderLeftWidth) < 2) issues.push(`${link.textContent}: not on the rail`);
                    if ((style.borderLeftColor === accent) !== (index <= active))
                        issues.push(`${link.textContent}: rail ${index <= active ? 'not filled' : 'filled past the section'}`);
                });
                const titles = links.filter(link => link.matches('.documentation-nav-level-2'))
                    .map(link => Math.round([...link.childNodes].find(node => node.nodeType === 3)
                        ? (() => { const r = document.createRange(); r.selectNodeContents([...link.childNodes].find(node => node.nodeType === 3)); return r.getBoundingClientRect().left; })()
                        : link.getBoundingClientRect().left));
                if (new Set(titles).size !== 1) issues.push(`top-level titles do not line up: ${titles.join(',')}`);
                return issues;
            }
            """, href);
        Assert.True(problems.Length == 0, $"{path} at {width}px: {string.Join("; ", problems)}");
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task ChaptersRulesAndPictures_ShareOneTileAndOnePlate(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var tiles = new List<string>();
        foreach (var (path, selector, count) in new[] { ("/about", ".documentation-document h2.documentation-chapter", 5), ("/ruleset", ".ruleset-rule > h2", 4), ("/privacy", ".documentation-document h2.documentation-chapter", 6) })
        {
            await NavigateAndSettleAsync(page, path);
            var styles = await page.Locator(selector).EvaluateAllAsync<string[]>(
                """
                headings => headings.map((heading, index) => {
                    const tile = getComputedStyle(heading, '::before');
                    return `${tile.width}|${tile.height}|${tile.backgroundColor}|${tile.fontFamily}|${tile.fontSize}|${index}`;
                })
                """);
            Assert.Equal(count, styles.Length);
            tiles.AddRange(styles.Select(style => style[..style.LastIndexOf('|')]));
            var numbers = await page.EvaluateAsync<string[]>(
                "selector => [...document.querySelectorAll(selector)].map(h => getComputedStyle(h, '::before').content)", selector);
            Assert.All(numbers, number => Assert.Contains("decimal-leading-zero", number));
        }
        Assert.Single(tiles.Distinct());

        foreach (var path in new[] { "/history", "/ruleset" })
        {
            await NavigateAndSettleAsync(page, path);
            var plates = await page.Locator(".documentation-document p:has(> img)").EvaluateAllAsync<string[]>(
                """
                plates => plates.map(plate => {
                    const style = getComputedStyle(plate);
                    return `${parseFloat(style.borderTopWidth) >= 1}|${style.backgroundColor !== 'rgba(0, 0, 0, 0)'}|${parseFloat(style.paddingTop) > 0}|${parseFloat(style.borderTopLeftRadius) > 0}`;
                })
                """);
            Assert.NotEmpty(plates);
            Assert.All(plates, plate => Assert.Equal("True|True|True|True", plate, ignoreCase: true));
        }
    }

    [Theory]
    [InlineData(1440, 100)]
    [InlineData(390, 100)]
    [InlineData(320, 100)]
    [InlineData(390, 200)]
    public async Task HistoryStandings_StayOneCompactTableThatFillsTheColumn(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        await page.EvaluateAsync("p => document.documentElement.style.fontSize = `${p}%`", textPercent);
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const table = document.querySelector('.documentation-document table');
                const frame = table.parentElement;
                if (!frame.matches('.documentation-table')) issues.push('the table has no frame');
                const column = frame.parentElement.getBoundingClientRect();
                const box = frame.getBoundingClientRect();
                if (Math.abs(box.left - column.left) > 1 || Math.abs(box.right - column.right) > 1) issues.push('the table does not fill its column');
                const rows = [...table.querySelectorAll('tbody tr')];
                if (rows.length !== 20) issues.push(`expected 20 rows, found ${rows.length}`);
                if (rows.some(row => [...row.cells].some(cell => getComputedStyle(cell, '::before').content !== 'none'))) issues.push('cells repeat their labels');
                const tabular = getComputedStyle(table.querySelector('thead')).position !== 'absolute';
                for (const row of rows) {
                    const [rank, athlete, years] = [...row.cells].map(cell => cell.getBoundingClientRect());
                    if (Math.abs(rank.left - box.left) > 2 + parseFloat(getComputedStyle(row).paddingLeft)) issues.push('rank does not lead the row');
                    if (tabular) {
                        if (getComputedStyle(row).display !== 'table-row') issues.push('rows became cards');
                        const range = document.createRange();
                        range.selectNodeContents(row.cells[2]);
                        if (Math.abs(range.getBoundingClientRect().right - (years.right - parseFloat(getComputedStyle(row.cells[2]).paddingRight))) > 1.5)
                            issues.push('age reduction is not right-aligned');
                    } else {
                        const oneLine = frame.clientWidth >= 18 * parseFloat(getComputedStyle(document.documentElement).fontSize);
                        if (athlete.left < rank.right || years.left < rank.right) issues.push('rank overlaps the athlete or age reduction');
                        if (oneLine) {
                            if (years.left < athlete.right - 1 || Math.abs(years.top + years.height / 2 - athlete.top - athlete.height / 2) > 1)
                                issues.push('compact columns overlap or do not share a center line');
                        } else if (years.top < athlete.bottom - 1) issues.push('narrow rows do not stack the athlete over the age reduction');
                        const range = document.createRange();
                        range.selectNodeContents(row.cells[1]);
                        if ([...range.getClientRects()].some(r => r.left < athlete.left - 1 || r.right > athlete.right + 1))
                            issues.push('athlete name escapes its column');
                    }
                }
                if (tabular !== (frame.clientWidth > 24 * parseFloat(getComputedStyle(document.documentElement).fontSize)))
                    issues.push(`a ${frame.clientWidth}px sheet uses the ${tabular ? 'table' : 'compact'} layout`);
                if (frame.scrollWidth > frame.clientWidth + 1) issues.push('the table scrolls sideways');
                const average = table.querySelector('tbody').getBoundingClientRect().height / rows.length;
                if (average > 5 * parseFloat(getComputedStyle(document.documentElement).fontSize)) issues.push(`rows average ${Math.round(average)}px`);
                return [...new Set(issues)];
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}%: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task MobileContents_IsOneCardWhoseHeaderRowOpensTheRail()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        foreach (var path in Paths)
        {
            await NavigateAndSettleAsync(page, path);
            await page.Locator(".documentation-nav-toggle").ClickAsync();
            var problems = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const issues = [];
                    const nav = document.querySelector('.documentation-nav'), toggle = nav.querySelector('.documentation-nav-toggle');
                    const navBox = nav.getBoundingClientRect(), toggleBox = toggle.getBoundingClientRect();
                    const navBorder = parseFloat(getComputedStyle(nav).borderLeftWidth);
                    if (Math.abs(toggleBox.left - navBox.left - navBorder) > 1 || Math.abs(toggleBox.right - navBox.right + navBorder) > 1
                        || Math.abs(toggleBox.top - navBox.top - navBorder) > 1)
                        issues.push('the toggle is not the card header row');
                    if (parseFloat(getComputedStyle(toggle).borderTopWidth) !== 0) issues.push('the toggle is a second box inside the card');
                    for (const link of nav.querySelectorAll('.documentation-nav-links a[href^="#"]')) {
                        const style = getComputedStyle(link);
                        if (style.backgroundColor !== 'rgba(0, 0, 0, 0)' && !link.matches('.is-active')) { issues.push(`${link.textContent}: boxed link`); break; }
                        if (parseFloat(style.borderLeftWidth) < 2) { issues.push(`${link.textContent}: off the rail`); break; }
                    }
                    return issues;
                }
                """);
            Assert.True(problems.Length == 0, $"{path}: {string.Join("; ", problems)}");
        }
    }
}
