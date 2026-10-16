using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayStartTrackBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1280, 720)]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task PlayStart_SetsTheEmblemOnATrackThatClearsTheActions(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await Assertions.Expect(page.Locator("#newGameBtn")).ToBeEnabledAsync();
        await page.EvaluateAsync("async () => { window.LwcFlowActionDock?.refreshNow?.(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const panel = document.getElementById('playStartPanel');
                const track = getComputedStyle(panel, '::after');
                const logo = document.querySelector('.play-logo-watermark').getBoundingClientRect();
                const actions = document.querySelector('.play-menu-actions').getBoundingClientRect();
                const trackWidth = parseFloat(track.width), trackHeight = parseFloat(track.height);
                const top = parseFloat(track.top);
                const hasLanes = track.content !== 'none' && track.content !== 'normal' && (track.maskImage || track.webkitMaskImage || '').includes('svg');
                return [hasLanes ? 1 : 0, trackWidth, top - (logo.top + logo.bottom) / 2, top + trackHeight / 2, actions.top,
                    track.pointerEvents === 'none' ? 1 : 0, trackWidth - logo.width];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(layout[0] == 1, $"{scenario}: the start screen has no track behind its emblem.");
        Assert.True(Math.Abs(layout[2]) <= 2, $"{scenario}: the track is {layout[2]}px off the emblem's centre.");
        Assert.True(layout[3] <= layout[4] + 1, $"{scenario}: the track ({layout[3]}) runs into the actions ({layout[4]}).");
        Assert.True(layout[5] == 1, $"{scenario}: the track must not catch taps.");
        Assert.True(layout[6] >= 0, $"{scenario}: the track should be at least as wide as the emblem.");
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));

        await page.Locator("#newGameBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => !document.body.classList.contains('play-start-active')");
        Assert.Equal("none", await page.EvaluateAsync<string>("() => getComputedStyle(document.getElementById('playStartPanel'), '::after').content"));
    }
}
