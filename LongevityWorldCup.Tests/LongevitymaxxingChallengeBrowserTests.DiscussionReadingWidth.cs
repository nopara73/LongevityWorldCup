using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(390, false)]
    [InlineData(360, true)]
    public async Task DiscussionReplies_ReadUnderTheirAuthorRowOnPhones(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            IsMobile = true,
            HasTouch = true
        });
        var state = JsonSerializer.SerializeToNode(BuildParticipantState(noEligibleDays: true,
            includeJoinDiscussionPost: true, joinDiscussionHasReply: true))!.AsObject();
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        var reply = page.Locator("#lmxNotes .lmx-discussion-reply-item").First;
        await Assertions.Expect(reply).ToBeVisibleAsync();

        var layout = await reply.EvaluateAsync<double[]>(
            """
            item => {
                const avatar = item.querySelector('.lmx-discussion-reply-meta .lmx-discussion-author-avatar').getBoundingClientRect();
                const text = item.querySelector(':scope > p, p').getBoundingClientRect();
                return [avatar.left, text.left, text.width, document.documentElement.scrollWidth, innerWidth];
            }
            """);
        Assert.True(Math.Abs(layout[1] - layout[0]) <= 2,
            $"{width}px: a reply's text should start under its author's avatar ({layout[1]:F0}px vs {layout[0]:F0}px), not hang beside it.");
        Assert.True(layout[3] <= layout[4] + 1, $"{width}px: the page scrolls sideways.");
    }
}
