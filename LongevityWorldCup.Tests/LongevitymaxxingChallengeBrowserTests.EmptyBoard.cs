using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1440, ColorScheme.Light, false, "active")]
    [InlineData(1280, ColorScheme.Dark, true, "active")]
    [InlineData(390, ColorScheme.Light, true, "active")]
    [InlineData(320, ColorScheme.Dark, false, "active")]
    [InlineData(1440, ColorScheme.Dark, false, "signup")]
    [InlineData(820, ColorScheme.Light, true, "signup")]
    [InlineData(390, ColorScheme.Dark, false, "signup")]
    [InlineData(320, ColorScheme.Light, true, "signup")]
    public async Task EmptyLeaderboard_DropsTheDayColumnsAndCentersTheMessage(
        int width, ColorScheme theme, bool resting, string phase)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        var state = LongLeaderboardState(resting, phase);
        if (!resting) state["leaderboard"] = new JsonArray();
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-empty-row")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-empty-name"))
            .ToHaveTextAsync(resting ? "No active participants" : "No one has joined yet");
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-empty-row").GetByRole(AriaRole.Cell))
            .ToHaveAttributeAsync("aria-colspan", phase == "signup" ? "2" : "3");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const header = document.querySelector('#lmxBoard .lmx-board-row.header');
                const pager = document.getElementById('lmxWeekPager');
                const scroller = document.querySelector('.lmx-board-scroll');
                const wrap = document.getElementById('lmxBoardSection').getBoundingClientRect();
                const row = document.querySelector('#lmxBoard .lmx-empty-row').getBoundingClientRect();
                const message = document.querySelector('#lmxBoard .lmx-empty-name').getBoundingClientRect();
                return [header.getBoundingClientRect().height, pager.getBoundingClientRect().height,
                    scroller.scrollWidth - scroller.clientWidth, row.left - wrap.left, wrap.right - row.right,
                    (message.left + message.right) / 2 - (row.left + row.right) / 2, message.width];
            }
            """);
        var scenario = $"{width}px {theme}, {phase}, resting={resting}";
        Assert.True(layout[0] == 0, $"{scenario}: the day header still shows over an empty board.");
        Assert.True(layout[1] == 0, $"{scenario}: the day pager still shows over an empty board.");
        Assert.True(layout[2] <= 1, $"{scenario}: the empty board still scrolls sideways by {layout[2]}px.");
        Assert.True(layout[3] >= 0 && layout[4] >= 0, $"{scenario}: the message box escapes the card.");
        Assert.True(Math.Abs(layout[5]) <= 4 && layout[6] > 0, $"{scenario}: the message is {layout[5]}px off centre.");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    }

    [Theory]
    [InlineData(1440, ColorScheme.Light, "active")]
    [InlineData(820, ColorScheme.Dark, "signup")]
    [InlineData(390, ColorScheme.Dark, "active")]
    [InlineData(320, ColorScheme.Light, "signup")]
    public async Task EmptyLeaderboard_RestoresRowsHistoryAndFocusAcrossRestingToggles(
        int width, ColorScheme theme, string phase)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        var state = LongLeaderboardState(resting: true, phase);
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        var board = page.Locator("#lmxBoard");
        var toggle = page.Locator("#lmxInactiveToggle");
        var pager = page.Locator("#lmxWeekPager");
        await Assertions.Expect(board.Locator(".lmx-empty-name")).ToHaveTextAsync("No active participants");
        // Outlive the initial scroll retries: restoring a board must work later, too.
        await page.EvaluateAsync("() => new Promise(resolve => setTimeout(resolve, 1300))");

        for (var cycle = 0; cycle < 2; cycle++)
        {
            await toggle.FocusAsync();
            await page.Keyboard.PressAsync("Space");
            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true");
            await Assertions.Expect(toggle).ToBeFocusedAsync();
            await Assertions.Expect(board).ToHaveAttributeAsync("role", "table");
            await Assertions.Expect(board.GetByRole(AriaRole.Row)).ToHaveCountAsync(width > 760 ? 2 : 1);
            var row = board.Locator(".lmx-board-row:not(.header)");
            // A phone row with nothing in the window keeps only its name and score cells.
            var quietPhoneRow = width <= 760 && phase != "signup";
            await Assertions.Expect(row.GetByRole(AriaRole.Cell)).ToHaveCountAsync(phase == "signup" || quietPhoneRow ? 2 : 3);
            await Assertions.Expect(row.Locator(".lmx-participant-label")).ToContainTextAsync("Browser Tester");
            await Assertions.Expect(board.Locator(".lmx-empty-row")).ToHaveCountAsync(0);
            Assert.False(await page.Locator("#lmxBoardSection").EvaluateAsync<bool>(
                "section => section.classList.contains('lmx-board-wrap--empty')"));

            if (width > 760)
            {
                await Assertions.Expect(board.GetByRole(AriaRole.Columnheader).First).ToBeVisibleAsync();
                await page.WaitForFunctionAsync("""
                    () => {
                        const scroller = document.querySelector('#lmxBoardSection .lmx-board-scroll');
                        const last = document.querySelector('#lmxBoard .lmx-board-row:not(.header) [data-day="128"]').getBoundingClientRect();
                        const viewport = scroller.getBoundingClientRect();
                        // scrollbar-gutter can make scrollWidth - clientWidth exceed
                        // the browser's maximum scrollLeft. Verify the day itself.
                        return scroller.scrollLeft > 100 && last.left >= viewport.left
                            && last.right <= viewport.right + 1 && viewport.right - last.right < 24;
                    }
                    """, null, new() { Timeout = 3000 });
                await page.EvaluateAsync("""
                    () => {
                        const scroller = document.querySelector('#lmxBoardSection .lmx-board-scroll');
                        scroller.scrollLeft = (scroller.scrollWidth - scroller.clientWidth) / 2;
                    }
                    """);
            }
            else
            {
                await Assertions.Expect(pager).ToBeVisibleAsync();
                await Assertions.Expect(page.Locator("#lmxWeekLabel"))
                    .ToHaveTextAsync(cycle == 0 ? "Days 115–128" : "Days 101–114");
                if (cycle == 0)
                {
                    await page.Locator("#lmxWeekOlder").TapAsync();
                    await Assertions.Expect(page.Locator("#lmxWeekLabel")).ToHaveTextAsync("Days 101–114");
                    if (quietPhoneRow) await Assertions.Expect(row.Locator("[data-day='114']")).ToBeAttachedAsync();
                    else await Assertions.Expect(row.Locator("[data-day='114']")).ToBeVisibleAsync();
                }
                else
                {
                    await page.Locator("#lmxWeekNewer").TapAsync();
                    await Assertions.Expect(page.Locator("#lmxWeekLabel")).ToHaveTextAsync("Days 115–128");
                }
            }

            await toggle.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(toggle).ToBeFocusedAsync();
            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-pressed", "false");
            await Assertions.Expect(board.Locator(".lmx-empty-name")).ToHaveTextAsync("No active participants");
            await Assertions.Expect(pager).ToBeHiddenAsync();
            Assert.True(await page.EvaluateAsync<bool>("""
                () => {
                    const scroller = document.querySelector('#lmxBoardSection .lmx-board-scroll');
                    return scroller.scrollWidth <= scroller.clientWidth + 1
                        && document.documentElement.scrollWidth <= innerWidth + 1
                        && document.activeElement.getClientRects().length > 0;
                }
                """));
        }
    }

    [Fact]
    public async Task PopulatedLeaderboard_RestingTogglePreservesManualHistoryPosition()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }
        });
        var state = LongLeaderboardState(resting: false);
        var resting = state["leaderboard"]![0]!.DeepClone();
        resting["participantId"] = "resting";
        resting["displayName"] = "Resting Tester";
        resting["challengeInactive"] = true;
        state["leaderboard"]!.AsArray().Add(resting);
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-board-row:not(.header)")).ToHaveCountAsync(1);
        await page.EvaluateAsync("() => document.fonts.ready");
        var position = await page.EvaluateAsync<double>("""
            () => {
                const scroller = document.querySelector('#lmxBoardSection .lmx-board-scroll');
                scroller.scrollLeft = 1000;
                return scroller.scrollLeft;
            }
            """);
        Assert.True(position > 100);
        for (var cycle = 0; cycle < 4; cycle++)
        {
            await page.Locator("#lmxInactiveToggle").ClickAsync();
            await Assertions.Expect(page.Locator("#lmxBoard .lmx-board-row:not(.header)"))
                .ToHaveCountAsync(cycle % 2 == 0 ? 2 : 1);
            var actual = await page.Locator("#lmxBoardSection .lmx-board-scroll")
                .EvaluateAsync<double>("e => e.scrollLeft");
            Assert.True(Math.Abs(actual - position) <= 1, $"Toggling resting rows moved history from {position} to {actual}.");
        }
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task EmptyLeaderboard_EnlargedTextWrapsBetweenWords(ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 320, Height = 844 }, ColorScheme = theme,
            IsMobile = true, HasTouch = true
        });
        var state = LongLeaderboardState(resting: true);
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-empty-name")).ToHaveTextAsync("No active participants");
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.EvaluateAsync("() => document.fonts.ready");
        var size = await page.Locator("#lmxBoard .lmx-empty-name").EvaluateAsync<double[]>("""
            label => {
                const font = getComputedStyle(label);
                const canvas = document.createElement('canvas').getContext('2d');
                canvas.font = `${font.fontWeight} ${font.fontSize} ${font.fontFamily}`;
                const word = Math.max(...label.textContent.trim().split(/\s+/).map(w => canvas.measureText(w).width));
                return [label.getBoundingClientRect().width, word];
            }
            """);
        Assert.True(size[0] >= size[1] - 1, $"The empty label has {size[0]}px for its {size[1]}px longest word.");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    private static JsonObject LongLeaderboardState(bool resting, string phase = "active")
    {
        var state = JsonSerializer.SerializeToNode(BuildPublicState())!.AsObject();
        state["phase"] = phase;
        state["days"] = new JsonArray(Enumerable.Range(1, 128).Select(day => (JsonNode)new JsonObject
        {
            ["challengeDay"] = day,
            ["date"] = new DateOnly(2026, 6, 8).AddDays(day - 1).ToString("yyyy-MM-dd")
        }).ToArray());
        state["leaderboard"]![0]!["challengeInactive"] = resting;
        state["notes"] = new JsonArray();
        return state;
    }
}
