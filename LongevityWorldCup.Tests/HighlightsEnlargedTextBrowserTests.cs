using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HighlightsEnlargedTextBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/events", 390, 844, 200)]
    [InlineData("/events", 320, 640, 200)]
    [InlineData("/events", 360, 740, 200)]
    [InlineData("/events", 390, 844, 100)]
    [InlineData("/events", 1440, 900, 100)]
    public async Task Highlights_KeepRanksWholeAndMessagesWideAtEnlargedText(string path, int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await page.Locator(".events-board td.event-message .placement-link").First.WaitForAsync();
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.WaitForFunctionAsync(
            "percent => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 16 * percent / 100 - 0.5",
            textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");

        var result = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const cells = [...document.querySelectorAll('.events-board td.event-message .event-message-cell')]
                    .filter(cell => cell.getClientRects().length && cell.querySelector('.placement-link'));
                for (const cell of cells) {
                    const box = cell.getBoundingClientRect();
                    for (const rank of cell.querySelectorAll('.placement-link')) {
                        const lines = new Set([...rank.getClientRects()].filter(rect => rect.width > 0).map(rect => Math.round(rect.top)));
                        if (lines.size > 1) problems.push(`${rank.textContent.trim()} breaks across lines`);
                        const rankBox = rank.getBoundingClientRect();
                        if (rankBox.right > box.right + 1) problems.push(`${rank.textContent.trim()} runs past its message`);
                    }
                }
                const widest = Math.max(0, ...cells.map(cell => cell.getBoundingClientRect().width));
                return [String(cells.length), String(widest / innerWidth), ...problems];
            }
            """);
        var scenario = $"{path} {width}x{height} at {textPercent}%";
        Assert.True(int.Parse(result[0]) > 0, $"{scenario}: no highlight with a rank rendered.");
        Assert.True(result.Length == 2, $"{scenario}: {string.Join("; ", result.Skip(2).Distinct().Take(8))}");
        if (path == "/events" && width < 600)
            Assert.True(double.Parse(result[1], System.Globalization.CultureInfo.InvariantCulture) >= 0.6,
                $"{scenario}: the message only gets {result[1]} of the screen width.");
    }
}
