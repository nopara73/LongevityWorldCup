using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1280, ColorScheme.Light)]
    [InlineData(1280, ColorScheme.Dark)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(320, ColorScheme.Dark)]
    public async Task EmptyLeaderboard_ShowsOneMessageInsteadOfBlankDays(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        var state = JsonSerializer.SerializeToNode(BuildPublicState())!.AsObject();
        state["leaderboard"] = new JsonArray();
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");

        var rows = page.Locator("#lmxBoard .lmx-board-row:not(.header)");
        await Assertions.Expect(rows).ToHaveCountAsync(1);
        await Assertions.Expect(rows.First).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("\\blmx-empty-row\\b"));
        await Assertions.Expect(rows.First).ToHaveTextAsync("No one has joined yet");
        Assert.Equal(0, await rows.First.Locator(".lmx-cell, .lmx-number").CountAsync());
        var message = Assert.IsType<LocatorBoundingBoxResult>(await rows.First.Locator(".lmx-empty-name").BoundingBoxAsync());
        Assert.True(message.Width > 0 && message.Height > 0);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    }

    [Theory]
    [InlineData(390, 100)]
    [InlineData(320, 100)]
    [InlineData(390, 200)]
    public async Task PhoneLeaderboard_KeepsScoreBesideTheNameUntilEnlargedTextNeedsTheRoom(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, IsMobile = true, HasTouch = true
        });
        await context.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState(includeMentionParticipants: true))));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-board-row:not(.header) .lmx-number").First).ToBeVisibleAsync();
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var problems = await page.EvaluateAsync<string[]>(
            """
            enlarged => {
                const issues = [];
                if (getComputedStyle(document.querySelector('#lmxBoard .lmx-board-row.header')).display !== 'none')
                    issues.push('column header is shown on a phone');
                for (const row of document.querySelectorAll('#lmxBoard .lmx-board-row:not(.header)')) {
                    const label = row.querySelector('.lmx-participant-label');
                    const name = row.querySelector('.lmx-name').getBoundingClientRect();
                    const score = row.querySelector('.lmx-number').getBoundingClientRect();
                    const days = row.querySelector('.lmx-cell-strip').getBoundingClientRect();
                    const who = label.textContent.trim();
                    const besideName = score.left >= name.right - 1 && Math.abs((score.top + score.bottom) / 2 - (name.top + name.bottom) / 2) < 12;
                    const underName = score.top >= name.bottom - 1;
                    if (!enlarged && !besideName) issues.push(`${who}: score is not beside the name`);
                    if (!besideName && !underName) issues.push(`${who}: score overlaps the name`);
                    if (days.top < Math.max(name.bottom, score.bottom) - 1) issues.push(`${who}: days overlap the name or score`);
                    // A name may wrap between words, but never one letter per line.
                    const text = label.querySelector('a, span') ?? label;
                    const words = text.textContent.trim().split(/\s+/).length;
                    const range = document.createRange();
                    range.selectNodeContents(text);
                    const lines = new Set([...range.getClientRects()].map(rect => Math.round(rect.top))).size;
                    if (lines > Math.max(2, words + 1)) issues.push(`${who}: name breaks into ${lines} lines`);
                }
                if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('page overflows');
                return issues;
            }
            """, textPercent > 100);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}%: {string.Join(", ", problems)}");
    }
}
