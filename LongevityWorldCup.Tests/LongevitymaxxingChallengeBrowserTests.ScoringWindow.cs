using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1280, ColorScheme.Light, "Pacific/Kiritimati")]
    [InlineData(1280, ColorScheme.Dark, "America/Los_Angeles")]
    [InlineData(390, ColorScheme.Light, "America/Los_Angeles")]
    [InlineData(320, ColorScheme.Dark, "Pacific/Kiritimati")]
    public async Task ScoringWindowSeparatesPendingDaysAndKeepsTheServerCutoffAcrossViews(
        int width, ColorScheme theme, string timeZone)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600, TimezoneId = timeZone
        });
        var state = JsonSerializer.SerializeToNode(BuildPublicState(includeMentionParticipants: true))!.AsObject();
        state["scoringWindow"] = new JsonObject
        {
            ["startDay"] = 7, ["endDay"] = 20, ["nextClosesAtUtc"] = "2026-06-30T12:00:00Z"
        };
        foreach (var row in state["leaderboard"]!.AsArray())
        {
            row!["cells"] = new JsonArray(Enumerable.Range(1, 22).Select(day => (JsonNode)new JsonObject
            {
                ["challengeDay"] = day, ["checkedIn"] = day != 21, ["countsForScore"] = day != 1,
                ["score"] = day == 1 || day == 21 ? null : 11,
                ["sleep"] = day == 21 ? null : 2, ["exercise"] = day == 21 ? null : 2,
                ["nutrition"] = day == 21 ? null : 2, ["vices"] = day == 21 ? null : 2
            }).ToArray());
            row["totalPoints"] = 154;
            row["checkedInDays"] = 14;
            row["currentStreak"] = 14;
        }
        var participant = JsonSerializer.SerializeToNode(BuildParticipantState(includeUpcomingCall: true))!.AsObject();
        participant["public"] = state.DeepClone();
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, participant.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(DateTime.Parse("2026-06-29T13:00:00Z").ToUniversalTime());
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("#lmxBoardMeta")).ToContainTextAsync("through Saturday, Jun 27");
        var firstRow = page.Locator("#lmxBoard .lmx-board-row:not(.header)").First;
        await Assertions.Expect(firstRow.Locator(".lmx-number")).ToHaveTextAsync("154");
        await Assertions.Expect(firstRow.Locator(".lmx-reporting-open")).ToHaveCountAsync(2);
        await Assertions.Expect(firstRow.Locator("[data-day='21']")).ToHaveAttributeAsync("aria-label", "Day 21: pending · Reporting open; not yet included in standings");
        await Assertions.Expect(firstRow.Locator("[data-day='22']")).ToContainTextAsync("11");
        Assert.Equal("dashed", await firstRow.Locator("[data-day='22']").EvaluateAsync<string>("e => getComputedStyle(e).borderTopStyle"));
        await page.Locator("#lmxBoardSection").ScrollIntoViewIfNeededAsync();
        if (width < 600)
        {
            await Assertions.Expect(firstRow.Locator(".lmx-open-divider")).ToBeVisibleAsync();
            var closedBox = await firstRow.Locator("[data-day='20']").BoundingBoxAsync();
            var openBox = await firstRow.Locator("[data-day='21']").BoundingBoxAsync();
            Assert.True(openBox!.Y > closedBox!.Y + closedBox.Height);
            Assert.True(await firstRow.Locator("[data-day='22']").EvaluateAsync<bool>("""
                cell => {
                    const day = getComputedStyle(cell, '::before');
                    const score = cell.querySelector('.lmx-cell-score').getBoundingClientRect();
                    return score.top - cell.getBoundingClientRect().top > parseFloat(day.top) + parseFloat(day.lineHeight);
                }
                """), "The day number and points must not overlap on a narrow screen.");
        }
        else
        {
            Assert.Equal("3px", await firstRow.Locator("[data-day='21']").EvaluateAsync<string>("e => getComputedStyle(e, '::after').width"));
        }
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        var captures = Environment.GetEnvironmentVariable("LWC_SCORING_CAPTURE_DIR");
        if (!string.IsNullOrEmpty(captures))
        {
            Directory.CreateDirectory(captures);
            await page.Locator("#lmxBoardSection").ScreenshotAsync(new() { Path = Path.Combine(captures, $"leaderboard-{width}-{theme}.png") });
        }
        if (width < 600)
        {
            await page.Locator("#lmxWeekOlder").ClickAsync();
            await Assertions.Expect(firstRow.Locator(".lmx-reporting-open")).ToHaveCountAsync(0);
            await Assertions.Expect(firstRow.Locator(".lmx-open-divider")).ToHaveCountAsync(0);
        }

        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await Assertions.Expect(page.Locator(".lmx-habit-summary .lmx-dashboard-category strong").First).ToHaveTextAsync("28/28");
        await page.Locator(".lmx-habit-history > summary").ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-dashboard-day.lmx-reporting-start")).ToHaveTextAsync("21");
        await Assertions.Expect(page.Locator(".lmx-category-day.lmx-reporting-open")).ToHaveCountAsync(8);
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-board-row:not(.header)").First.Locator(".lmx-number")).ToHaveTextAsync("154");
    }
}
