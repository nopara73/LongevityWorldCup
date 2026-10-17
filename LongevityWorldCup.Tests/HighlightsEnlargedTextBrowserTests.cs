using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HighlightsEnlargedTextBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/events", 390, 844, 200)]
    [InlineData("/events", 320, 640, 200)]
    [InlineData("/events", 360, 740, 200)]
    [InlineData("/events", 390, 844, 100)]
    [InlineData("/events", 1440, 900, 100)]
    public async Task Highlights_KeepRanksWholeAndMessagesWideAtEnlargedText(string path, int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await page.Locator(".events-board td.event-message .placement-link").First.WaitForAsync();
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.WaitForFunctionAsync(
            "percent => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 16 * percent / 100 - 0.5",
            textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");

        var result = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const cells = [...document.querySelectorAll('.events-board td.event-message .event-message-cell')]
                    .filter(cell => cell.getClientRects().length && cell.querySelector('.placement-link'));
                for (const cell of cells) {
                    const box = cell.getBoundingClientRect();
                    for (const rank of cell.querySelectorAll('.placement-link')) {
                        const range = document.createRange();
                        range.selectNodeContents(rank);
                        const fragments = [...range.getClientRects()].filter(rect => rect.width > 0);
                        const lines = new Set(fragments.map(rect => Math.round(rect.top)));
                        if (lines.size > 1) problems.push(`${rank.textContent.trim()} breaks across lines`);
                        if (fragments.some(rect => rect.left < box.left - 1 || rect.right > box.right + 1))
                            problems.push(`${rank.textContent.trim()} runs past its message`);
                    }
                }
                const narrowest = Math.min(...cells.map(cell => cell.getBoundingClientRect().width));
                return [String(cells.length), String(narrowest / innerWidth), ...problems];
            }
            """);
        var scenario = $"{path} {width}x{height} at {textPercent}%";
        Assert.True(int.Parse(result[0]) > 0, $"{scenario}: no highlight with a rank rendered.");
        Assert.True(result.Length == 2, $"{scenario}: {string.Join("; ", result.Skip(2).Distinct().Take(8))}");
        if (path == "/events" && width < 600)
            Assert.True(double.Parse(result[1], System.Globalization.CultureInfo.InvariantCulture) >= 0.6,
                $"{scenario}: the message only gets {result[1]} of the screen width.");
    }

    [Theory]
    [InlineData("/events")]
    [InlineData("/")]
    public async Task RankedHighlights_ReflowNamesAndRanksThroughTextAndViewportChanges(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = true
        });
        // Exercise the real renderer and homepage selection with known ranked rows,
        // rather than depending on whichever Events happen to lead the shared fixture.
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """
                [
                  {"Key":"ranked-multiword","Name":"Nóra Marosvölgyi","CurrentPlacement":232},
                  {"Key":"ranked-long","Name":"Alexandertheexceptionallylongunbrokentokenforreflow","CurrentPlacement":75},
                  {"Key":"ranked-pro","Name":"Melian Attila","CurrentPlacement":224},
                  {"Key":"ranked-crowd","Name":"Bertalan Fazekas","CurrentPlacement":9}
                ]
                """
        }));
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """
                [
                  {"Id":"ranked-joined","Type":1,"Text":"slug[ranked-multiword]","OccurredAt":"2026-10-16T08:00:00Z","Relevance":10},
                  {"Id":"ranked-long","Type":9,"Text":"slug[ranked-long]","OccurredAt":"2026-10-15T08:00:00Z","Relevance":10},
                  {"Id":"ranked-pro","Type":9,"Text":"slug[ranked-pro]","OccurredAt":"2026-10-14T08:00:00Z","Relevance":10},
                  {"Id":"ranked-crowd","Type":11,"Text":"slug[ranked-crowd] place[3] previousPlace[6] crowdAge[31.2] crowdCount[123]","OccurredAt":"2026-10-13T08:00:00Z","Relevance":10},
                  {"Id":"ranked-announcement","Type":6,"Text":"Season update\n\nThe full announcement remains available. [Read the rules](https://longevityworldcup.com/ruleset)","OccurredAt":"2026-10-12T08:00:00Z","Relevance":10}
                ]
                """
        }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        var firstRow = page.Locator("tr.main-row[data-event-id='ranked-joined']");
        await Assertions.Expect(firstRow.Locator(".event-message .event-athlete-link")).ToHaveAccessibleNameAsync("Nóra Marosvölgyi");
        await Assertions.Expect(firstRow.Locator(".placement-link")).ToHaveAccessibleNameAsync("(#232)");
        Assert.EndsWith("#rank-232", await firstRow.Locator(".placement-link").GetAttributeAsync("href"));
        foreach (var percent in new[] { 100, 125, 150, 175, 200 })
        {
            await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", percent);
            await page.WaitForFunctionAsync("percent => Math.abs(parseFloat(getComputedStyle(document.documentElement).fontSize) - 16 * percent / 100) < 0.1", percent);
            Assert.InRange(await page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.documentElement).fontSize)"), 16 * percent / 100.0 - 0.1, 16 * percent / 100.0 + 0.1);
            foreach (var width in new[] { 320, 390, 600, 601, 768, 844, 1440, 390 })
            {
                await page.SetViewportSizeAsync(width, width == 844 ? 390 : 844);
                await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
                var problems = await page.EvaluateAsync<string[]>(
                    """
                    () => {
                        const issues = [];
                        if (![...document.fonts].some(font => font.family.replaceAll('"', '') === 'Roboto' && font.status === 'loaded'))
                            issues.push('Roboto is not loaded');
                        const cells = [...document.querySelectorAll('tr.main-row .event-message-cell')].filter(cell => cell.getClientRects().length);
                        if (!cells.some(cell => cell.querySelector('.placement-link'))) issues.push('no ranked highlight rendered');
                        const heading = document.querySelector('#events-root-index .header-bar > span');
                        if (heading) {
                            const range = document.createRange(); range.selectNodeContents(heading);
                            if (new Set([...range.getClientRects()].filter(r => r.width > 0).map(r => Math.round(r.top))).size !== 1)
                                issues.push('Highlights heading splits inside its word');
                        }
                        for (const cell of cells) {
                            const box = cell.getBoundingClientRect();
                            for (const rank of cell.querySelectorAll('.placement-link')) {
                                const range = document.createRange(); range.selectNodeContents(rank);
                                const lines = new Set([...range.getClientRects()].filter(r => r.width > 0).map(r => Math.round(r.top)));
                                if (lines.size !== 1) issues.push(`${rank.textContent} splits across lines`);
                            }
                            const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT);
                            for (let node; (node = walker.nextNode());) {
                                const range = document.createRange(); range.selectNodeContents(node);
                                for (const r of range.getClientRects()) {
                                    if (!r.width) continue;
                                    if (r.left < box.left - 1 || r.right > box.right + 1)
                                        issues.push(`${node.textContent.trim()} escapes its message`);
                                    for (let ancestor = node.parentElement; ancestor && ancestor !== cell.parentElement; ancestor = ancestor.parentElement) {
                                        const style = getComputedStyle(ancestor), clip = ancestor.getBoundingClientRect();
                                        if (/hidden|clip/.test(style.overflowX) && (r.left < clip.left - 1 || r.right > clip.right + 1))
                                            issues.push(`${node.textContent.trim()} is clipped`);
                                    }
                                }
                                // Fitting words stay whole; only an oversized token needs an emergency break.
                                for (const word of node.textContent.matchAll(/[^\s]+/g)) {
                                    range.setStart(node, word.index); range.setEnd(node, word.index + word[0].length);
                                    const rects = [...range.getClientRects()].filter(r => r.width > 0);
                                    const canvas = document.createElement('canvas').getContext('2d');
                                    canvas.font = getComputedStyle(node.parentElement).font;
                                    if (canvas.measureText(word[0]).width <= box.width && new Set(rects.map(r => Math.round(r.top))).size > 1)
                                        issues.push(`${word[0]} breaks despite fitting its message`);
                                }
                            }
                        }
                        return [...new Set(issues)];
                    }
                    """);
                Assert.True(problems.Length == 0, $"{path}, {width}px / {percent}%: {string.Join("; ", problems)}");
            }
        }
        var announcement = page.Locator("tr.main-row[data-event-id='ranked-announcement']");
        var expander = announcement.Locator(".custom-event-expander");
        await expander.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "true");
        var details = announcement.Locator("+ tr.custom-event-details");
        await Assertions.Expect(details.GetByRole(AriaRole.Link, new() { Name = "Read the rules" })).ToHaveAttributeAsync("href", "https://longevityworldcup.com/ruleset");
        await expander.TapAsync();
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "false");
    }
}
