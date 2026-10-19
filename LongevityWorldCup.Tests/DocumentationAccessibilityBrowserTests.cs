using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DocumentationAccessibilityBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/about")]
    [InlineData("/history")]
    [InlineData("/ruleset")]
    public async Task Contents_KeepEveryGlyphInsideThePanelAcrossTheDesktopTransition(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 901, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        foreach (var percent in new[] { 100, 200 })
        foreach (var width in new[] { 900, 901, 902, 960, 1024, 1440, 901 })
        {
            await page.SetViewportSizeAndWaitForLayoutAsync(width, 900);
            await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", percent);
            if (width <= 900 && await page.Locator(".documentation-nav-toggle").GetAttributeAsync("aria-expanded") == "false")
                await page.Locator(".documentation-nav-toggle").ClickAsync();
            await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
            var problems = await page.EvaluateAsync<string[]>("""
                () => {
                    const nav = document.querySelector('.documentation-nav'), bounds = nav.getBoundingClientRect();
                    const issues = [];
                    for (const link of nav.querySelectorAll('a')) {
                        const walker = document.createTreeWalker(link, NodeFilter.SHOW_TEXT);
                        for (let node; (node = walker.nextNode());) {
                            if (!node.textContent.trim() || node.parentElement.closest('.documentation-timeline-separator')) continue;
                            const range = document.createRange();
                            range.selectNodeContents(node);
                            for (const box of range.getClientRects())
                                if (box.left < bounds.left - 1 || box.right > bounds.right + 1)
                                    issues.push(link.textContent + ': contents clips its text');
                        }
                    }
                    return [...new Set(issues)];
                }
                """);
            Assert.True(problems.Length == 0, $"{path} at {width}px / {percent}%: {string.Join("; ", problems)}");
        }
    }

    [Theory]
    [InlineData(100)]
    [InlineData(200)]
    public async Task HistoryStandings_KeepAccessibleRowsAndLongNamesOnBothSidesOfTheContainerBoundary(int percent)
    {
        var markdown = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "LongevityWorldCup.Documentation", "LongevitySportHistory.md"));
        var expected = Regex.Matches(markdown, @"^\| (?<rank>\d+) \| \[(?<name>.*?)\]\((?<url>.*?)\) \| (?<score>.*?) \|\r?$", RegexOptions.Multiline)
            .Select(m => new[] { m.Groups["rank"].Value, m.Groups["name"].Value, m.Groups["score"].Value, m.Groups["url"].Value })
            .ToArray();
        Assert.Equal(20, expected.Length);
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        var table = page.GetByRole(AriaRole.Table);
        Assert.Equal(1, await table.CountAsync());
        Assert.Equal(expected.Select(row => $"{row[0]}|{row[1]}|{row[2]}|{row[3]}"), await table.Locator("tbody tr").EvaluateAllAsync<string[]>(
            "rows => rows.map(r => [...r.cells].map(c=>c.textContent).join('|') + '|' + r.querySelector('a').getAttribute('href'))"));
        const string longName = "Dr. Alexandermichaelpeterjonathanrobertschmidt von Extraordinary Longevity Research";
        await table.Locator("tbody a").First.EvaluateAsync("(a, name) => a.textContent = name", longName);
        expected[0][1] = longName;
        var rootFont = await page.EvaluateAsync<double>("percent => { document.documentElement.style.fontSize=percent+'%'; return parseFloat(getComputedStyle(document.documentElement).fontSize); }", percent);
        foreach (var delta in new[] { -1, 0, 1, 0, -1 })
        {
            var width = 24 * rootFont + delta;
            await page.Locator(".documentation-table").EvaluateAsync(
                "(frame, width) => { frame.style.boxSizing='content-box'; frame.style.width=width+'px'; }", width);
            await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r))); }");
            Assert.Equal(new[] { "Rank", "Athlete", "Age reduction" }, await table.GetByRole(AriaRole.Columnheader).AllTextContentsAsync());
            Assert.Equal(21, await table.GetByRole(AriaRole.Row).CountAsync());
            Assert.Equal(60, await table.GetByRole(AriaRole.Cell).CountAsync());
            var snapshot = await table.AriaSnapshotAsync();
            foreach (var row in expected)
                Assert.Contains($"- row \"{row[0]} {row[1]} {row[2]}\":", snapshot);
            var problems = await table.EvaluateAsync<string[]>("""
                table => {
                    const issues = [], frame = table.parentElement;
                    if (frame.scrollWidth > frame.clientWidth + 1) issues.push('table scrolls sideways');
                    for (const cell of table.querySelectorAll('tbody td')) {
                        const bounds = cell.getBoundingClientRect();
                        const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT);
                        for(let node; (node=walker.nextNode());) {
                            const range=document.createRange();
                            range.selectNodeContents(node);
                            for(const box of range.getClientRects())
                                if(box.left<bounds.left-1 || box.right>bounds.right+1)
                                    issues.push(cell.textContent + ': text escapes its cell');
                        }
                    }
                    return [...new Set(issues)];
                }
                """);
            Assert.True(problems.Length == 0, $"{width}px / {percent}%: {string.Join("; ", problems)}");
        }
    }
}
