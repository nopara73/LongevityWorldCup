using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(1280, ColorScheme.Dark)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(320, ColorScheme.Dark)]
    public async Task EmptyLeaderboard_DropsTheDayColumnsAndCentersTheMessage(int width, ColorScheme theme)
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
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-empty-row")).ToBeVisibleAsync();

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
        var scenario = $"{width}px {theme}";
        Assert.True(layout[0] == 0, $"{scenario}: the day header still shows over an empty board.");
        Assert.True(layout[1] == 0, $"{scenario}: the day pager still shows over an empty board.");
        Assert.True(layout[2] <= 1, $"{scenario}: the empty board still scrolls sideways by {layout[2]}px.");
        Assert.True(layout[3] >= 0 && layout[4] >= 0, $"{scenario}: the message box escapes the card.");
        Assert.True(Math.Abs(layout[5]) <= 4 && layout[6] > 0, $"{scenario}: the message is {layout[5]}px off centre.");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    }
}
