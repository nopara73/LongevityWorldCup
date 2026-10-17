using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LeaderboardNameWrapBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, 640, 100)]
    [InlineData(390, 844, 200)]
    [InlineData(320, 640, 200)]
    [InlineData(390, 844, 100)]
    [InlineData(1440, 900, 100)]
    public async Task PhoneRows_KeepEveryNameWordWhole(int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.WaitForFunctionAsync(
            "percent => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 16 * percent / 100 - 0.5",
            textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");

        var broken = await page.EvaluateAsync<string[]>(
            """
            () => {
                const broken = [];
                const names = [...document.querySelectorAll('.leaderboard tbody tr[data-athlete-name] .athlete-name')]
                    .filter(name => name.getClientRects().length);
                for (const name of names) {
                    const walker = document.createTreeWalker(name, NodeFilter.SHOW_TEXT);
                    for (let node; (node = walker.nextNode());) {
                        // A hyphen is a legitimate break point; letters between breaks must stay on one line.
                        for (const part of node.textContent.matchAll(/[^\s -]+-?/g)) {
                            const range = document.createRange();
                            range.setStart(node, part.index);
                            range.setEnd(node, part.index + part[0].length);
                            const lines = new Set([...range.getClientRects()].filter(rect => rect.width > 0).map(rect => Math.round(rect.top)));
                            if (lines.size > 1) broken.push(part[0]);
                        }
                    }
                }
                return names.length ? broken : ['no names rendered'];
            }
            """);
        Assert.True(broken.Length == 0, $"{width}x{height} at {textPercent}%: names break inside {string.Join(", ", broken.Distinct().Take(12))}.");
    }
}
