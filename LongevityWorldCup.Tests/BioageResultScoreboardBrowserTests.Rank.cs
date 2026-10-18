using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class BioageResultScoreboardBrowserTests
{
    [Theory]
    [InlineData("/pheno-age", 320, 200)]
    [InlineData("/bortz-age", 390, 200)]
    [InlineData("/pheno-age", 820, 200)]
    [InlineData("/bortz-age", 1440, 200)]
    [InlineData("/pheno-age", 390, 100)]
    [InlineData("/bortz-age", 320, 100)]
    [InlineData("/bortz-age", 844, 100)]
    public async Task Rank_LongPlacesNamesAndScoresRemainReadable(string path, int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await FillAndCalculateAsync(page, path);
        await page.WaitForFunctionAsync("() => document.querySelector('.bioage-rank-row')");
        await page.EvaluateAsync(
            """
            () => {
                // Presentation fixtures retain the generated rows and controls.
                document.querySelector('.bioage-rank-number').textContent = '#10003';
                document.querySelectorAll('.bioage-rank-row').forEach((row, index) => {
                    const link = row.querySelector('.bioage-rank-row-name a');
                    if (link) link.textContent = index === 0
                        ? 'AlexandertheGreatVeryLongUnbrokenSurname'
                        : 'Vishwamithra Shashishekara';
                    row.querySelector('.bioage-rank-row-place').textContent = `#${10001 + index}`;
                    row.querySelector('.bioage-rank-row-score').textContent = '-123.456789y';
                });
            }
            """);
        await page.EvaluateAsync("() => document.fonts.ready");
        await AssertReadableRankRowsAsync(page);
        var brokenCtaWords = await page.EvaluateAsync<string[]>(
            """
            () => {
                const label = document.querySelector('#continueButton .flow-action__label');
                const walker = document.createTreeWalker(label, NodeFilter.SHOW_TEXT);
                const broken = [];
                while (walker.nextNode()) {
                    const node = walker.currentNode;
                    for (const word of node.textContent.matchAll(/\S+/g)) {
                        const range = document.createRange();
                        range.setStart(node, word.index);
                        range.setEnd(node, word.index + word[0].length);
                        const lines = [...range.getClientRects()].filter(rect => rect.width > 0);
                        if (new Set(lines.map(rect => Math.round(rect.top))).size > 1) broken.push(word[0]);
                    }
                }
                return broken;
            }
            """);
        Assert.Empty(brokenCtaWords);
        await page.EvaluateAsync("window.scrollTo(0, document.documentElement.scrollHeight)");
        var clearance = await page.EvaluateAsync<double>(
            """
            () => document.querySelector('.phenoage-result-actions, .bioage-result-actions').getBoundingClientRect().top
                - document.querySelector('[data-bioage-result-rank]').getBoundingClientRect().bottom
            """);
        Assert.True(clearance >= 8, $"{path} {width}px/{textPercent}%: rank ends behind the dock ({clearance}px).");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/pheno-age", "pheno")]
    [InlineData("/bortz-age", "bortz")]
    public async Task Rank_PreservesFieldTiesNeighboursLinksAndMissingData(string path, string clock)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 320, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("() => window.LwcBioAgeRankPreview && window.PhenoAge && window.BortzAge");
        await page.EvaluateAsync(
            """
            () => {
                // A deterministic clock isolates ranking/display contracts from model arithmetic.
                window.PhenoAge.calculatePhenoAge = values => values[0] + values[1];
                window.BortzAge.calculateBortzAge = (age, values) => age + values[1];
                const entry = reduction => ({ Date: '2026-01-01', AlbGL: reduction,
                    Wbc1000cellsuL: 6, LymPc: 30, McvFL: 90, RdwPc: 13, AlpUL: 80,
                    CreatUmolL: 70, GluMmolL: 5, CrpMgL: 1, UreaMmolL: 5,
                    CholesterolMmolL: 5, CystatinCMgL: 1, Hba1cMmolMol: 35, GgtUL: 20,
                    Rbc10e12L: 4, MonocytePc: 6, NeutrophilPc: 60, AltUL: 20,
                    ShbgNmolL: 40, VitaminDNmolL: 70, MchPg: 30, ApoA1GL: 1.5 });
                const athlete = (name, slug, reduction, year) => ({ Name: name, DisplayName: name,
                    AthleteSlug: slug, DateOfBirth: { Year: year, Month: 1, Day: 1 },
                    Biomarkers: [entry(reduction)] });
                window.__rankFixture = [athlete('Leader', 'rank_leader', -10, 1980),
                    athlete('Older', 'rank_older', -5, 1960),
                    athlete('Aaron & Co', 'rank_aaron', -5, 1980),
                    athlete('Zoe', 'rank_zoe', -5, 1980),
                    athlete('Last', 'rank_last', 5, 1980), null, {},
                    { Name: 'Incomplete', DateOfBirth: {Year: 1980, Month: 1, Day: 1}, Biomarkers: [] }];
                window.__rankFixture[0].ProfilePicThumb = 'data:image/webp;base64,broken';
                window.getSharedAthletes = () => Promise.resolve(window.__rankFixture);
                document.querySelector('.bioageform[id$="AgeResult"]').classList.add('show');
            }
            """);
        var target = path == "/pheno-age" ? "phenoAgeRankPreview" : "bortzAgeRankPreview";
        var preview = page.Locator($"#{target}");
        foreach (var (reduction, rank, percent, names, places) in new[]
        {
            (-5, 4, 67, new[] { "Older", "Aaron & Co", "You", "Zoe", "Last" }, new[] { "#2", "#3", "#4", "#5", "#6" }),
            (-100, 1, 17, new[] { "You", "Leader", "Older" }, new[] { "#1", "#2", "#3" }),
            (100, 6, 100, new[] { "Zoe", "Last", "You" }, new[] { "#4", "#5", "#6" })
        })
        {
            await page.EvaluateAsync(
                """
                ({target, clock, reduction}) => window.LwcBioAgeRankPreview.render(target,
                    {clock, ageReduction: reduction, dateOfBirth: new Date(1980, 0, 1)})
                """, new { target, clock, reduction });
            await Assertions.Expect(preview.Locator(".bioage-rank-number")).ToHaveTextAsync($"#{rank}");
            await Assertions.Expect(preview.Locator(".bioage-rank-meta")).ToHaveTextAsync($"{(clock == "pheno" ? "Pheno" : "Bortz")} Age · {rank} of 6");
            await Assertions.Expect(preview.Locator(".bioage-rank-percentile")).ToHaveTextAsync($"Top {percent}%");
            Assert.Equal(names, await preview.Locator(".bioage-rank-row-name").AllTextContentsAsync());
            Assert.Equal(places, await preview.Locator(".bioage-rank-row-place").AllTextContentsAsync());
            foreach (var link in await preview.Locator(".bioage-rank-row-name a").AllAsync())
                Assert.StartsWith("/athlete/rank-", await link.GetAttributeAsync("href"));
            Assert.Equal("You", await preview.Locator(".current .bioage-rank-row-name").TextContentAsync());
            Assert.Equal(0, await preview.Locator(".current a").CountAsync());
            // A failed portrait decodes after rank rendering completes.
            await Assertions.Expect(preview.Locator(".bioage-rank-row-avatar-fallback")).ToHaveCountAsync(names.Length - 1);
            if (names.Contains("Older"))
            {
                var older = preview.Locator(".bioage-rank-row").Filter(new()
                {
                    Has = page.Locator(".bioage-rank-row-name", new() { HasText = "Older" })
                });
                Assert.True(await older.EvaluateAsync<bool>(
                    """
                    row => {
                        const name = row.querySelector('.bioage-rank-row-name').getBoundingClientRect();
                        const score = row.querySelector('.bioage-rank-row-score').getBoundingClientRect();
                        return score.left >= name.right && score.top < name.bottom;
                    }
                    """), "A short name and score fit on one row at 320px without sacrificing name space.");
            }
            if (names.Contains("Leader"))
            {
                var leader = preview.Locator(".bioage-rank-row").Filter(new()
                {
                    Has = page.Locator(".bioage-rank-row-name", new() { HasText = "Leader" })
                });
                await Assertions.Expect(leader.Locator(".bioage-rank-row-avatar-fallback")).ToHaveTextAsync("L");
                Assert.Equal(0, await leader.Locator("img").CountAsync());
            }
        }
        await page.EvaluateAsync("window.__rankFixture = []");
        await page.EvaluateAsync(
            """
            ({target, clock}) => window.LwcBioAgeRankPreview.render(target,
                {clock, ageReduction: 0, dateOfBirth: new Date(1980, 0, 1)})
            """, new { target, clock });
        await Assertions.Expect(preview.Locator(".bioage-rank-meta")).ToContainTextAsync("1 of 1");
        await Assertions.Expect(preview.Locator(".bioage-rank-percentile")).ToHaveTextAsync("Top 100%");
        Assert.Equal(1, await preview.Locator(".bioage-rank-row").CountAsync());
        Assert.Equal(0, await preview.Locator("a").CountAsync());
    }

    private static async Task AssertReadableRankRowsAsync(IPage page)
    {
        var defects = await page.EvaluateAsync<string[]>(
            """
            () => {
                const defects = [];
                const rects = element => {
                    const range = document.createRange();
                    range.selectNodeContents(element);
                    return [...range.getClientRects()].filter(rect => rect.width > 0 && rect.height > 0);
                };
                const preview = document.querySelector('[data-bioage-result-rank]').getBoundingClientRect();
                for (const summary of document.querySelectorAll('.bioage-rank-label, .bioage-rank-number, .bioage-rank-meta, .bioage-rank-percentile')) {
                    if (rects(summary).some(rect => rect.left < preview.left - 1 || rect.right > preview.right + 1))
                        defects.push(`clipped summary: ${summary.textContent}`);
                }
                for (const row of document.querySelectorAll('.bioage-rank-row')) {
                    const parts = [...row.querySelectorAll('.bioage-rank-row-place, .bioage-rank-row-name, .bioage-rank-row-score')];
                    const rowBox = row.getBoundingClientRect();
                    for (const part of parts) {
                        const box = part.getBoundingClientRect();
                        if (part.scrollWidth > part.clientWidth + 1 || box.left < rowBox.left || box.right > rowBox.right)
                            defects.push(`overflow: ${part.textContent}`);
                        if (rects(part).some(rect => rect.left < box.left - 1 || rect.right > box.right + 1
                            || rect.top < rowBox.top || rect.bottom > rowBox.bottom))
                            defects.push(`clipped glyphs: ${part.textContent}`);
                    }
                    for (let first = 0; first < parts.length; first++) {
                        for (let second = first + 1; second < parts.length; second++) {
                            if (rects(parts[first]).some(a => rects(parts[second]).some(b =>
                                Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1
                                && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1)))
                                defects.push(`collision: ${parts[first].textContent}/${parts[second].textContent}`);
                        }
                    }
                    const name = row.querySelector('.bioage-rank-row-name');
                    const style = getComputedStyle(name);
                    const canvas = document.createElement('canvas');
                    const measure = canvas.getContext('2d');
                    measure.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                    const words = name.textContent.trim().split(/\s+/);
                    const longestWord = Math.max(...words.map(word => measure.measureText(word).width));
                    const rowStyle = getComputedStyle(row);
                    const available = row.clientWidth - parseFloat(rowStyle.paddingLeft) - parseFloat(rowStyle.paddingRight);
                    // Unbroken identifiers can wrap anywhere. Normal full names
                    // must have room for their words when those words fit the row.
                    if (words.length > 1 && name.getBoundingClientRect().width < Math.min(longestWord, available) - 1)
                        defects.push(`unnecessarily broken name: ${name.textContent}`);
                }
                return defects;
            }
            """);
        Assert.Empty(defects);
    }
}
