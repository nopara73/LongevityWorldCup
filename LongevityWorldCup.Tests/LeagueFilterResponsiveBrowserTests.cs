using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class LeagueFilterResponsiveBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, false, false)]
    [InlineData(320, true, false)]
    [InlineData(320, false, true)]
    [InlineData(320, true, true)]
    [InlineData(390, false, false)]
    [InlineData(390, true, false)]
    [InlineData(390, false, true)]
    [InlineData(390, true, true)]
    [InlineData(1440, false, false)]
    [InlineData(1440, true, false)]
    [InlineData(1440, false, true)]
    [InlineData(1440, true, true)]
    public async Task FullBoard_HeadersKeepWholeWordsWithEnlargedText(int width, bool dark, bool filtersOpen)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 900);
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        if (filtersOpen) await page.Locator(".sidebar-toggle").ClickAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var brokenWords = await page.Locator(".leaderboard thead th").EvaluateAllAsync<string[]>(
            """
            headers => headers.filter(header => header.getBoundingClientRect().width > 0).flatMap(header => {
                const broken = [], walker = document.createTreeWalker(header, NodeFilter.SHOW_TEXT);
                while (walker.nextNode()) {
                    const text = walker.currentNode;
                    for (const word of text.textContent.matchAll(/\p{L}+/gu)) {
                        const range = document.createRange();
                        range.setStart(text, word.index); range.setEnd(text, word.index + word[0].length);
                        const lines = new Set([...range.getClientRects()].filter(rect => rect.height > 0).map(rect => Math.round(rect.top)));
                        if (lines.size > 1) broken.push(word[0]);
                    }
                }
                return broken;
            })
            """);
        Assert.Empty(brokenWords);
    }

    [Theory]
    [InlineData(false, 100)]
    [InlineData(true, 100)]
    [InlineData(false, 200)]
    [InlineData(true, 200)]
    public async Task ShortBoard_HoverDoesNotMoveTheFooter(bool dark, int textPercent)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.GotoAsync("/leaderboard?filters=open,baby%20boomers", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var count = await page.Locator("#leaderboardResultCount").InnerTextAsync();
        var headingFits = await page.Locator(".sidebar-heading").EvaluateAsync<bool>(
            """
            heading => {
                const style = getComputedStyle(heading);
                const naturalHeight = heading.scrollHeight + parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
                return heading.getBoundingClientRect().height >= naturalHeight - 1;
            }
            """);
        Assert.True(headingFits, "The collapsed rail must not shrink its heading before measuring the title height.");
        var before = await ReadStableFooterTopAsync(page);
        await page.Locator(".sidebar").HoverAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var during = await ReadStableFooterTopAsync(page);
        Assert.InRange(Math.Abs(during - before), 0, 1);
        await page.Mouse.MoveAsync(0, 0);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var after = await ReadStableFooterTopAsync(page);
        Assert.InRange(Math.Abs(after - before), 0, 1);
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).ToHaveTextAsync(count);
    }

    private static Task<double> ReadStableFooterTopAsync(IPage page) =>
        page.Locator("footer").EvaluateAsync<double>("""
            element => new Promise((resolve, reject) => {
                // Text reflow feeds the rail's ResizeObserver height sync. Measure
                // its settled geometry, retaining the one-pixel hover assertion.
                const started = performance.now();
                let previous = NaN, stableFrames = 0;
                function sample() {
                    const top = element.getBoundingClientRect().top + scrollY;
                    stableFrames = Math.abs(top - previous) < .01 ? stableFrames + 1 : 0;
                    previous = top;
                    if (stableFrames >= 4) return resolve(top);
                    if (performance.now() - started > 5000) return reject(new Error('Footer layout did not settle'));
                    requestAnimationFrame(sample);
                }
                requestAnimationFrame(sample);
            })
            """);

    [Theory]
    [InlineData(320, false)]
    [InlineData(320, true)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    [InlineData(820, false)]
    [InlineData(820, true)]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    public async Task FilterPanel_KeepsHeadingActionsAndScoresReadableWithEnlargedText(int width, bool dark)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 900);
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.GotoAsync("/leaderboard?filters=open,baby%20boomers", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('link[rel=stylesheet]')].every(link => link.sheet)");
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await Assertions.Expect(page.Locator(".sidebar-panel")).Not.ToHaveAttributeAsync("inert", "");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const issues = [], panel = document.querySelector('.sidebar');
                const title = panel.querySelector('.sidebar-title-text').getBoundingClientRect();
                for (const button of panel.querySelectorAll('.sidebar-heading button')) {
                    const box = button.getBoundingClientRect();
                    if (title.left < box.right && title.right > box.left && title.top < box.bottom && title.bottom > box.top)
                        issues.push('Heading overlaps ' + button.id);
                    if (box.width < 44 || box.height < 44) issues.push('Small heading action');
                }
                const list = panel.querySelector('.sidebar-filter-list');
                if (list.clientWidth < panel.clientWidth / 2 || list.scrollWidth > list.clientWidth + 1)
                    issues.push('Filter list is squeezed or overflows');
                for (const header of document.querySelectorAll('.leaderboard > table th')) {
                    if (!header.getBoundingClientRect().width) continue;
                    const style = getComputedStyle(header), canvas = document.createElement('canvas').getContext('2d');
                    canvas.font = [style.fontStyle, style.fontWeight, style.fontSize, style.fontFamily].join(' ');
                    const word = header.textContent.trim().split(/\s+/)[0];
                    if (canvas.measureText(word).width > header.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight) + 1)
                        issues.push('Header word is broken: ' + word);
                }
                const score = [...document.querySelectorAll('.leaderboard tbody .age-reduction')].find(e => e.getBoundingClientRect().height > 0);
                if (score) {
                    const walker = document.createTreeWalker(score, NodeFilter.SHOW_TEXT);
                    while (walker.nextNode()) {
                        const text = walker.currentNode, match = /[-−]?\d+(?:\.\d+)?/.exec(text.textContent);
                        if (!match) continue;
                        const range = document.createRange();
                        range.setStart(text, match.index); range.setEnd(text, match.index + match[0].length);
                        if (new Set([...range.getClientRects()].map(r => Math.round(r.top))).size > 1)
                            issues.push('Score number breaks across lines');
                    }
                }
                if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('Page overflow');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / 200% / {(dark ? "dark" : "light")}: {string.Join(", ", problems)}");
        await page.Locator("#clearSidebarFiltersBtn").ClickAsync();
        await Assertions.Expect(page.Locator(".sidebar input:checked")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(320, false)]
    [InlineData(320, true)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public async Task LeaderboardControls_KeepSearchAndFilterReadableWithEnlargedText(int width, bool dark)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 844);
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('link[rel=stylesheet]')].every(link => link.sheet)");
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        await page.Locator("#athleteSearch").FillAsync("Michael");
        await Assertions.Expect(page.Locator(".clear-icon")).ToBeVisibleAsync();

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const button = document.querySelector('.sidebar-toggle').getBoundingClientRect();
                const icon = document.querySelector('.sidebar-toggle i').getBoundingClientRect();
                if (icon.left < button.left + 1 || icon.right > button.right - 1 || icon.top < button.top + 1 || icon.bottom > button.bottom - 1)
                    problems.push('Filter icon escapes its button');
                const input = document.querySelector('#athleteSearch');
                const style = getComputedStyle(input);
                const canvas = document.createElement('canvas').getContext('2d');
                canvas.font = style.font;
                const textWidth = input.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                // Long queries may scroll; a useful portion must remain readable beside Clear.
                if (textWidth < canvas.measureText('Mich').width)
                    problems.push(`Search has only ${textWidth}px for its text`);
                const clear = document.querySelector('.clear-icon').getBoundingClientRect();
                const field = input.getBoundingClientRect();
                if (clear.left < field.left || clear.right > field.right)
                    problems.push('Clear search escapes the field');
                if (clear.top < field.top || clear.bottom > field.bottom)
                    problems.push('Clear search escapes the field vertically');
                if (clear.width < 44 || clear.height < 44)
                    problems.push('Clear search target is too small');
                if (document.documentElement.scrollWidth > innerWidth + 1)
                    problems.push('Page overflows horizontally');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / 200% / {(dark ? "dark" : "light")}: {string.Join(", ", problems)}");

        await page.Locator(".clear-icon").ClickAsync();
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync("");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "true");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Theory]
    [InlineData(320, 100)]
    [InlineData(320, 200)]
    [InlineData(390, 200)]
    public async Task LeagueDrawer_KeepsEveryLabelAndCountReadableWithEnlargedText(int width, int textPercent)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 844);
        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.Locator(".sidebar-toggle").ClickAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var problems = await page.Locator(".filter-section label:visible").EvaluateAllAsync<string[]>(
            """
            labels => labels.flatMap(label => {
                const box = label.getBoundingClientRect();
                const range = document.createRange();
                range.selectNodeContents(label);
                const clipped = [...range.getClientRects()].some(rect => rect.left < box.left - 1 || rect.right > box.right + 1);
                return clipped ? [label.textContent.trim()] : [];
            })
            """);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}%: {string.Join(", ", problems)}");
        Assert.True(await page.Locator(".sidebar-filter-list").EvaluateAsync<bool>("list => list.scrollWidth <= list.clientWidth + 1"));

        var pro = page.Locator("input[name='leagueTrack'][value='professional' i]");
        await pro.CheckAsync();
        await Assertions.Expect(pro).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("label[data-league-track='professional' i]")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
        await page.Locator("#showLeaderboardResults").ClickAsync();
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
    }
}
