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
    public async Task LeaderboardSharesRanksHighlightsFullMarksAndRevealsOnlySubmittedOpenDays(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        var state = JsonSerializer.SerializeToNode(BuildPublicState(includeMentionParticipants: true))!.AsObject();
        state["scoringWindow"] = new JsonObject
        {
            ["startDay"] = 7, ["endDay"] = 20, ["nextClosesAtUtc"] = "2026-06-30T12:00:00Z"
        };
        var rows = state["leaderboard"]!.AsArray();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]!;
            row["totalPoints"] = index < 2 ? 154 : 143;
            row["rank"] = index < 2 ? 1 : 3;
            row["hasFullMarks"] = index < 2;
            row["checkedInDays"] = 14;
            row["currentStreak"] = 14;
            row["cells"] = new JsonArray(Enumerable.Range(1, 22).Select(day => (JsonNode)new JsonObject
            {
                ["challengeDay"] = day, ["checkedIn"] = day <= 20, ["countsForScore"] = day != 1,
                ["score"] = day == 1 || day > 20 ? null : index == 2 && day == 20 ? 0 : 11,
                ["sleep"] = day > 20 ? null : 2, ["exercise"] = day > 20 ? null : 2,
                ["nutrition"] = day > 20 ? null : 2, ["vices"] = day > 20 ? null : 2
            }).ToArray());
        }
        // A zero-point submission still makes its open date worth displaying.
        var day21 = rows[0]!["cells"]![20]!;
        day21["checkedIn"] = true;
        day21["score"] = 0;
        var participant = JsonSerializer.SerializeToNode(BuildParticipantState(includeUpcomingCall: true))!.AsObject();
        participant["participant"]!["id"] = "p2";
        participant["participant"]!["displayName"] = "Ari Able";
        participant["public"] = state.DeepClone();
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", route =>
        {
            participant["public"] = state.DeepClone();
            return FulfillJsonAsync(route, participant.ToJsonString());
        });
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(DateTime.Parse("2026-06-29T13:00:00Z").ToUniversalTime());
        await page.GotoAsync("/longevitymaxxing");
        var board = page.Locator("#lmxBoard");
        await Assertions.Expect(board.Locator(".lmx-rank")).ToHaveTextAsync(new[] { "#1", "#1", "#3" });
        await Assertions.Expect(board.Locator(".lmx-full-marks")).ToHaveCountAsync(2);
        await Assertions.Expect(board.Locator(".lmx-full-marks-end")).ToHaveCountAsync(1);
        await Assertions.Expect(board.GetByRole(AriaRole.Img, new() { Name = "Full marks for the 14-day scoring window" })).ToHaveCountAsync(2);
        await Assertions.Expect(board.Locator("[data-day='21']")).ToHaveCountAsync(3);
        await Assertions.Expect(board.Locator("[data-day='22']")).ToHaveCountAsync(0);
        var boardRows = board.Locator(".lmx-board-row:not(.header)");
        var colors = await boardRows.EvaluateAllAsync<string[]>("rows => rows.map(row => getComputedStyle(row).backgroundColor)");
        Assert.Equal(colors[0], colors[1]);
        Assert.NotEqual(colors[1], colors[2]);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        await page.Locator("#lmxBoardSection").ScrollIntoViewIfNeededAsync();
        var captures = Environment.GetEnvironmentVariable("LWC_SCORING_CAPTURE_DIR");
        if (!string.IsNullOrEmpty(captures))
        {
            Directory.CreateDirectory(captures);
            await page.Locator("#lmxBoardSection").ScreenshotAsync(new() { Path = Path.Combine(captures, $"shared-ranks-{width}-{theme}.png") });
        }

        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await Assertions.Expect(page.Locator(".lmx-ops-tile").Filter(new() { HasText = "Rank" }).Locator("strong")).ToHaveTextAsync("#1");
        await Assertions.Expect(board.Locator(".lmx-rank")).ToHaveTextAsync(new[] { "#1", "#1", "#3" });

        // A newly submitted date appears on refresh, retaining an unsubmitted gap before it.
        day21["checkedIn"] = false;
        day21["score"] = null;
        rows[1]!["cells"]![21]!["checkedIn"] = true;
        rows[1]!["cells"]![21]!["score"] = 0;
        await page.ReloadAsync();
        await Assertions.Expect(board.Locator("[data-day='21']")).ToHaveCountAsync(3);
        await Assertions.Expect(board.Locator("[data-day='22']")).ToHaveCountAsync(3);
        await Assertions.Expect(board.Locator(".lmx-rank")).ToHaveTextAsync(new[] { "#1", "#1", "#3" });

        // With no open submissions, closed empty dates remain and the open divider disappears.
        rows[1]!["cells"]![21]!["checkedIn"] = false;
        rows[1]!["cells"]![21]!["score"] = null;
        foreach (var row in rows)
        {
            row!["cells"]![19]!["checkedIn"] = false;
            row["hasFullMarks"] = false;
        }
        await page.ReloadAsync();
        await Assertions.Expect(board.Locator("[data-day='20']")).ToHaveCountAsync(3);
        await Assertions.Expect(board.Locator(".lmx-reporting-open")).ToHaveCountAsync(0);
        await Assertions.Expect(board.Locator(".lmx-open-divider")).ToHaveCountAsync(0);
        await Assertions.Expect(board.Locator(".lmx-full-marks")).ToHaveCountAsync(0);
        if (width < 600)
            await Assertions.Expect(page.Locator("#lmxWeekLabel")).ToHaveTextAsync("Days 7–20");
    }
}
