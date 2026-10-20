using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320, 640, ColorScheme.Light, true)]
    [InlineData(390, 844, ColorScheme.Dark, true)]
    [InlineData(1280, 800, ColorScheme.Light, true)]
    [InlineData(1440, 900, ColorScheme.Dark, false)]
    public async Task CheckIn_FillsEachHabitAndLightsItsSaveRailSegment(int width, int height, ColorScheme theme, bool direct)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenCheckInWorkspaceAsync(context, direct: direct);
        var form = page.Locator(".lmx-checkin-card");
        var rail = form.Locator(".lmx-checkin-actions");
        const string railImage = "e => getComputedStyle(e, '::before').backgroundImage";

        // The four answers share one inset track; unchosen faces stay quiet.
        foreach (var key in new[] { "sleep", "exercise", "nutrition", "vices" })
        {
            var track = form.Locator($".lmx-question[data-key='{key}'] .lmx-answer-options");
            Assert.Equal("1px", await track.EvaluateAsync<string>("e => getComputedStyle(e).borderTopWidth"));
            Assert.NotEqual("rgba(0, 0, 0, 0)", await track.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
            Assert.All(
                await track.Locator(".lmx-answer-face").EvaluateAllAsync<string[]>("faces => faces.map(f => getComputedStyle(f).backgroundColor)"),
                color => Assert.Equal("rgba(0, 0, 0, 0)", color));
            Assert.Equal("rgba(0, 0, 0, 0)", await form.Locator($".lmx-question[data-key='{key}'] .lmx-question-icon")
                .EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
        }
        Assert.Equal("4px", await rail.EvaluateAsync<string>("e => getComputedStyle(e, '::before').height"));
        Assert.DoesNotContain("rgb(37, 99, 235)", await rail.EvaluateAsync<string>(railImage));

        var expected = new[]
        {
            (Key: "sleep", Accent: "rgb(37, 99, 235)"),
            (Key: "exercise", Accent: "rgb(220, 38, 38)"),
            (Key: "nutrition", Accent: "rgb(22, 163, 74)"),
            (Key: "vices", Accent: "rgb(124, 58, 237)")
        };
        foreach (var (key, accent) in expected)
        {
            Assert.DoesNotContain(accent, await rail.EvaluateAsync<string>(railImage));
            await AnswerHabitAsync(form, key, key == "exercise" ? "somewhat" : "yes");
            await Assertions.Expect(form.Locator($".lmx-question[data-key='{key}'] .lmx-question-icon"))
                .ToHaveCSSAsync("background-color", accent);
            Assert.Contains(accent, await rail.EvaluateAsync<string>(railImage));
        }

        Assert.All(
            await form.Locator(".lmx-answer-input:checked + .lmx-answer-face").EvaluateAllAsync<string[]>("faces => faces.map(f => getComputedStyle(f).backgroundColor)"),
            color => Assert.NotEqual("rgba(0, 0, 0, 0)", color));
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeEnabledAsync();
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(1440, ColorScheme.Light)]
    public async Task HabitHistory_FillsDaysWithEachHabitColour(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = theme,
            TimezoneId = "UTC"
        });
        await RouteChallengeResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(DateTime.Parse("2026-06-29T09:00:00Z", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        await page.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await page.RouteAsync("**/api/longevitymaxxing/participant",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState(noEligibleDays: true))));
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await page.Locator(".lmx-habit-history > summary").ClickAsync();
        await page.Locator(".lmx-category-day.today").First.WaitForAsync();

        // Day 22: sleep and nutrition answered yes, exercise and vices somewhat.
        await Assertions.Expect(page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.sleep) .lmx-category-day[data-day='22']"))
            .ToHaveCSSAsync("background-color", "rgb(37, 99, 235)");
        await Assertions.Expect(page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.nutrition) .lmx-category-day[data-day='22']"))
            .ToHaveCSSAsync("background-color", "rgb(22, 163, 74)");
        Assert.Contains("rgb(220, 38, 38)", await page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.exercise) .lmx-category-day[data-day='22']")
            .EvaluateAsync<string>("e => getComputedStyle(e).backgroundImage"));
        Assert.Contains("rgb(124, 58, 237)", await page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.vices) .lmx-category-day[data-day='22']")
            .EvaluateAsync<string>("e => getComputedStyle(e).backgroundImage"));
        Assert.Equal("dashed", await page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.sleep) .lmx-category-day.empty").First
            .EvaluateAsync<string>("e => getComputedStyle(e).borderTopStyle"));

        // The history head reads like the hero's other ruled section headings.
        var head = page.Locator(".lmx-title-panel .lmx-dashboard-head");
        Assert.Equal("0px", await head.EvaluateAsync<string>("e => getComputedStyle(e).marginTop"));
        Assert.Equal("1px", await head.Locator("h2").EvaluateAsync<string>("e => getComputedStyle(e, '::after').height"));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(1440, ColorScheme.Dark)]
    public async Task Leaderboard_MarksTheSignedInParticipantsOwnRow(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = theme,
            TimezoneId = "UTC"
        });
        await RouteChallengeResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState(includeMentionParticipants: true))));
        await page.RouteAsync("**/api/longevitymaxxing/participant",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState(noEligibleDays: true, includeMentionParticipants: true))));
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await page.Locator("#lmxParticipantPanel:not(.lmx-hidden)").WaitForAsync();

        var self = page.Locator("#lmxBoard .lmx-board-row.lmx-board-row--self");
        await Assertions.Expect(self).ToHaveCountAsync(1);
        await Assertions.Expect(self.Locator(".lmx-name")).ToContainTextAsync("Browser Tester");
        var colors = await page.Locator("#lmxBoard .lmx-board-row:not(.header)").EvaluateAllAsync<string[]>(
            "rows => rows.map(r => getComputedStyle(r).backgroundColor)");
        var selfColor = await self.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
        Assert.Single(colors, color => color == selfColor);
        var accentSurface = await LmxTokenColorAsync(page, "--lmx-surface-accent");
        Assert.Equal(accentSurface, selfColor);
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(1280, ColorScheme.Dark)]
    public async Task CatchUpSwitcher_RulesTheOpenDayWithTheAccentInsteadOfAnInverseTile(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenCheckInWorkspaceAsync(context);
        var switcher = page.Locator(".lmx-checkin-switcher");
        var open = switcher.Locator("button[aria-pressed='true']");
        var closed = switcher.Locator("button[aria-pressed='false']").First;
        Assert.Equal("rgba(0, 0, 0, 0)", await switcher.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
        var accent = await LmxTokenColorAsync(page, "--lmx-accent");
        await Assertions.Expect(open).ToHaveCSSAsync("border-top-color", accent);
        Assert.Contains("inset", await open.EvaluateAsync<string>("e => getComputedStyle(e).boxShadow"));
        await Assertions.Expect(open).ToHaveCSSAsync("background-color", await LmxTokenColorAsync(page, "--lmx-surface-accent"));
        Assert.NotEqual("rgba(0, 0, 0, 0)", await closed.EvaluateAsync<string>("e => getComputedStyle(e).borderTopColor"));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    private static Task<string> LmxTokenColorAsync(IPage page, string token) => page.EvaluateAsync<string>("""
        token => {
            const probe = document.createElement('span');
            probe.style.color = `var(${token})`;
            document.querySelector('.lmx-page').append(probe);
            const color = getComputedStyle(probe).color;
            probe.remove();
            return color;
        }
        """, token);
}
