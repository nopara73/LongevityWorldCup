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
        Assert.Equal("3", await rows.First.GetByRole(AriaRole.Cell).GetAttributeAsync("aria-colspan"));
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
                    // A row with nothing in this window drops its days on phones.
                    const quiet = row.classList.contains('lmx-board-row--quiet');
                    if (!quiet && days.top < Math.max(name.bottom, score.bottom) - 1) issues.push(`${who}: days overlap the name or score`);
                    if (quiet && days.height !== 0) issues.push(`${who}: empty days still show`);
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

    [Theory]
    [InlineData(320, 100, ColorScheme.Light)]
    [InlineData(390, 100, ColorScheme.Dark)]
    [InlineData(320, 200, ColorScheme.Dark)]
    [InlineData(390, 200, ColorScheme.Light)]
    public async Task PhoneLeaderboard_WrapsWideScoresBeforeSqueezingLongNames(int width, int textPercent, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = true, HasTouch = true
        });
        var state = JsonSerializer.SerializeToNode(BuildPublicState(includeMentionParticipants: true))!.AsObject();
        var participants = state["leaderboard"]!.AsArray();
        participants[0]!["displayName"] = "Vishwamithra Shashishekara";
        participants[0]!["hasFullMarks"] = true;
        participants[1]!["displayName"] = "Alexandria Chandrasekharan";
        participants[1]!["hasFullMarks"] = true;
        participants[1]!["totalPoints"] = 168;
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-full-marks-icon").First).ToBeVisibleAsync();
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const canvas = document.createElement('canvas').getContext('2d');
                for (const row of document.querySelectorAll('#lmxBoard .lmx-board-row:not(.header)')) {
                    const text = row.querySelector('.lmx-participant-label > a, .lmx-participant-label > span');
                    const label = text.closest('.lmx-participant-label').getBoundingClientRect();
                    const rowBox = row.getBoundingClientRect();
                    const available = rowBox.right - parseFloat(getComputedStyle(row).paddingRight) - label.left;
                    const font = getComputedStyle(text);
                    canvas.font = `${font.fontWeight} ${font.fontSize} ${font.fontFamily}`;
                    const longestWord = Math.max(...text.textContent.trim().split(/\s+/).map(word => canvas.measureText(word).width));
                    // Break an oversized token only after giving the name the whole card's space.
                    if (longestWord <= available + 1 && label.width < longestWord - 1)
                        issues.push(`${text.textContent}: label squeezed to ${label.width}px for a ${longestWord}px word`);
                    const name = row.querySelector('.lmx-name').getBoundingClientRect();
                    const score = row.querySelector('.lmx-number').getBoundingClientRect();
                    const days = row.querySelector('.lmx-cell-strip').getBoundingClientRect();
                    const beside = score.left >= name.right - 1;
                    if (!beside && score.top < name.bottom - 1) issues.push('score overlaps the name');
                    if (!row.classList.contains('lmx-board-row--quiet') && days.top < Math.max(name.bottom, score.bottom) - 1)
                        issues.push('days overlap the name or score');
                }
                if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('horizontal overflow');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}%: {string.Join(", ", problems)}");
    }

    [Theory]
    [InlineData(1280, 100)]
    [InlineData(320, 200)]
    [InlineData(390, 200)]
    public async Task LeaderboardHistoryControls_KeepLabelsSeparateAndTouchTargetsUsable(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            IsMobile = width < 600, HasTouch = width < 600
        });
        var state = JsonSerializer.SerializeToNode(BuildPublicState(includeMentionParticipants: true))!.AsObject();
        state["leaderboard"]!.AsArray()[2]!["challengeInactive"] = true;
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        var toggle = page.Locator("#lmxInactiveToggle");
        await Assertions.Expect(toggle).ToBeVisibleAsync();
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");
        var toggleBox = Assert.IsType<LocatorBoundingBoxResult>(await toggle.BoundingBoxAsync());
        Assert.True(toggleBox.Height >= 44, $"Resting toggle is only {toggleBox.Height}px tall.");
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(toggle).ToHaveTextAsync("Hide resting");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        if (width > 760) return;

        var older = page.Locator("#lmxWeekOlder");
        var newer = page.Locator("#lmxWeekNewer");
        foreach (var control in new[] { older, newer })
        {
            var box = Assert.IsType<LocatorBoundingBoxResult>(await control.BoundingBoxAsync());
            Assert.True(box.Height >= 44 && box.Width >= 44);
        }
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const label = document.querySelector('#lmxWeekLabel');
                const labelRange = document.createRange();
                labelRange.selectNodeContents(label);
                for (const button of document.querySelectorAll('#lmxWeekPager button')) {
                    const box = button.getBoundingClientRect();
                    for (const line of labelRange.getClientRects())
                        if (line.left < box.right - 1 && line.right > box.left + 1 && line.top < box.bottom - 1 && line.bottom > box.top + 1)
                            issues.push('day range overlaps a pager button');
                    const range = document.createRange();
                    range.selectNodeContents(button.querySelector('span'));
                    for (const line of range.getClientRects())
                        if (line.left < box.left - 1 || line.right > box.right + 1) issues.push('pager text escapes its button');
                }
                if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('horizontal overflow');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, string.Join(", ", problems));
        await older.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#lmxWeekLabel")).ToHaveTextAsync("Days 1–8");
        await newer.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#lmxWeekLabel")).ToHaveTextAsync("Days 9–22");
    }
}
