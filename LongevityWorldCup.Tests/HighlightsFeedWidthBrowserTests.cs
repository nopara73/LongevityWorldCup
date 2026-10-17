using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HighlightsFeedWidthBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1280, 720)]
    [InlineData(1024, 768)]
    public async Task DesktopHighlights_ReadAsAColumnWithRecognisableFaces(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/events");
        await page.Locator(".event-board-page .events-board tr.main-row").First.WaitForAsync();

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const board = document.querySelector('.event-board-page .events-board').getBoundingClientRect();
                const heading = document.querySelector('.event-board-heading').getBoundingClientRect();
                const avatar = document.querySelector('.event-board-page .events-board tr.main-row .avatar, .event-board-page .events-board tr.main-row .avatar-fallback').getBoundingClientRect();
                return [board.width, (board.left + board.right) / 2, innerWidth / 2, avatar.width, heading.width];
            }
            """);
        Assert.True(layout[0] <= 862, $"{width}px: the feed is {layout[0]}px wide; it should read at a column's width.");
        Assert.InRange(Math.Abs(layout[1] - layout[2]), 0, 2);
        Assert.True(layout[3] >= 31, $"{width}px: faces are {layout[3]}px; they should be recognisable at 32px.");
        Assert.InRange(Math.Abs(layout[4] - layout[0]), 0, 2);
    }
}
