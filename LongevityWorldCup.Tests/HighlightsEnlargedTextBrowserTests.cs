using System.Text.Json.Nodes;
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
            Body = WithAthletePageFields("""
                [
                  {"Key":"ranked-multiword","Name":"Nóra Marosvölgyi","CurrentPlacement":232},
                  {"Key":"ranked-long","Name":"Alexandertheexceptionallylongunbrokentokenforreflow","CurrentPlacement":75},
                  {"Key":"ranked-pro","Name":"Melian Attila","CurrentPlacement":224},
                  {"Key":"ranked-crowd","Name":"Bertalan Fazekas","CurrentPlacement":9}
                ]
                """)
        }));
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """
                [
                  {"Id":"ranked-joined","Type":1,"Text":"slug[ranked-multiword]","OccurredAt":"2026-10-16T08:00:00Z","Relevance":10},
                  {"Id":"ranked-long","Type":9,"Text":"slug[ranked-long]","OccurredAt":"2026-10-15T08:00:00Z","Relevance":10},
                  {"Id":"ranked-donation","Type":3,"Text":"sats[8455]","OccurredAt":"2026-10-14T08:00:00Z","Relevance":10},
                  {"Id":"ranked-crowd","Type":11,"Text":"slug[ranked-crowd] place[3] previousPlace[6] crowdAge[31.2] crowdCount[123]","OccurredAt":"2026-10-13T08:00:00Z","Relevance":10},
                  {"Id":"ranked-announcement","Type":6,"Text":"Season update\n\nThe full announcement remains available. [Read the rules](https://longevityworldcup.com/ruleset)","OccurredAt":"2026-10-12T08:00:00Z","Relevance":10}
                ]
                """
        }));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        page.Console += (_, message) => { if (message.Type == "error") errors.Add(message.Text); };
        await NavigateAndSettleAsync(page, path);
        var firstRow = page.Locator("tr.main-row[data-event-id='ranked-joined']");
        await Assertions.Expect(firstRow.Locator(".event-message .event-athlete-link")).ToHaveAccessibleNameAsync("Nóra Marosvölgyi");
        await Assertions.Expect(firstRow.Locator(".placement-link")).ToHaveAccessibleNameAsync("(#232)");
        Assert.EndsWith("#rank-232", await firstRow.Locator(".placement-link").GetAttributeAsync("href"));
        await Assertions.Expect(page.Locator(".donation-amount")).ToHaveTextAsync("0.00008455 BTC");
        foreach (var percent in new[] { 100, 125, 150, 175, 200 })
        {
            await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", percent);
            await page.WaitForFunctionAsync("percent => Math.abs(parseFloat(getComputedStyle(document.documentElement).fontSize) - 16 * percent / 100) < 0.1", percent);
            Assert.InRange(await page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.documentElement).fontSize)"), 16 * percent / 100.0 - 0.1, 16 * percent / 100.0 + 0.1);
            foreach (var width in new[] { 320, 390, 600, 601, 768, 844, 1440, 390 })
            {
                await page.SetViewportSizeAsync(width, width == 844 ? 390 : 844);
                await page.WaitForFunctionAsync("width => innerWidth === width", width);
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
                        for (const date of document.querySelectorAll('tr.main-row .col-date')) {
                            if (!date.getClientRects().length) continue;
                            const box = date.getBoundingClientRect(), style = getComputedStyle(date);
                            const range = document.createRange(); range.selectNodeContents(date);
                            if ([...range.getClientRects()].some(r => r.width > 0 && (r.left < box.left + parseFloat(style.paddingLeft) - 1 || r.right > box.right - parseFloat(style.paddingRight) + 1)))
                                issues.push(`${date.textContent} escapes its date column (${box.width}px with ${style.paddingLeft}/${style.paddingRight} padding)`);
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
                                for (const word of node.textContent.matchAll(/[^\s\-\u00ad]+/g)) {
                                    range.setStart(node, word.index); range.setEnd(node, word.index + word[0].length);
                                    const rects = [...range.getClientRects()].filter(r => r.width > 0);
                                    if (new Set(rects.map(r => Math.round(r.top))).size > 1) {
                                        // Numeric variants cannot be serialized into the CSS font shorthand.
                                        // Measuring that empty shorthand on canvas silently uses 10px sans-serif.
                                        // Inherit the actual rendered type and features for the unbroken word.
                                        const probe = document.createElement('span');
                                        probe.textContent = word[0];
                                        probe.style.cssText = 'position:absolute;visibility:hidden;display:inline-block;width:max-content;max-width:none;min-width:0;padding:0;border:0;margin:0;white-space:nowrap;font:inherit;font-variant:inherit;font-feature-settings:inherit;letter-spacing:inherit';
                                        node.parentElement.append(probe);
                                        const wordWidth = probe.getBoundingClientRect().width;
                                        probe.remove();
                                        if (wordWidth <= box.width)
                                            issues.push(`${word[0]} breaks despite fitting its message`);
                                    }
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
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task HomepageHighlights_KeepAnnouncementStateAndFocusAcrossResizes(int announcementIndex)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = true
        });
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """
                [
                  {"Id":"resize-announcement-0","Type":6,"Text":"Season update\n\n[Read the rules](https://longevityworldcup.com/ruleset)","OccurredAt":"2026-10-17T07:00:00Z","Relevance":10},
                  {"Id":"resize-announcement-1","Type":6,"Text":"Another update\n\nMore details.","OccurredAt":"2026-10-16T07:00:00Z","Relevance":10},
                  {"Id":"resize-announcement-2","Type":6,"Text":"Earlier update\n\n[Read the rules](https://longevityworldcup.com/ruleset)","OccurredAt":"2026-10-15T07:00:00Z","Relevance":10}
                ]
                """
        }));
        var page = await context.NewPageAsync();
        const string pausedTime = "2026-10-17T08:00:01Z";
        await page.Clock.SetFixedTimeAsync(pausedTime);
        await page.Clock.PauseAtAsync(pausedTime);
        await NavigateAndSettleAsync(page, "/");
        await page.Clock.RunForAsync(500);
        var announcement = page.Locator($"tr.main-row[data-event-id='resize-announcement-{announcementIndex}']");
        var expander = announcement.Locator(".custom-event-expander");
        await expander.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "true");

        foreach (var width in new[] { 600, 1440, 390 })
        {
            await page.SetViewportSizeAsync(width, 844);
            // Advance the pending responsive layout work before checking the interaction.
            await page.Clock.RunForAsync(600);
            await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "true");
            await Assertions.Expect(expander).ToBeFocusedAsync();
        }
        var rules = announcement.Locator("+ tr.custom-event-details").GetByRole(AriaRole.Link, new() { Name = "Read the rules" });
        await rules.FocusAsync();
        await page.SetViewportSizeAsync(844, 390);
        await page.Clock.RunForAsync(600);
        await Assertions.Expect(rules).ToBeFocusedAsync();
        await expander.TapAsync();
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "false");
    }

    private static string WithAthletePageFields(string json)
    {
        // The homepage also renders the leaderboard; its required fields must
        // be present so the ranked fixture cannot pass alongside a console error.
        var athletes = JsonNode.Parse(json)!.AsArray();
        foreach (var athlete in athletes)
        {
            athlete!["DateOfBirth"] = new JsonObject { ["Year"] = 1980, ["Month"] = 1, ["Day"] = 1 };
            athlete["Biomarkers"] = new JsonArray();
            athlete["Placements"] = new JsonArray();
            athlete["Badges"] = new JsonArray();
            athlete["Proofs"] = new JsonArray();
            athlete["Division"] = "Women's";
            athlete["Flag"] = "Hungary";
            athlete["AthleteSlug"] = athlete["Key"]!.GetValue<string>();
        }
        return athletes.ToJsonString();
    }
}
