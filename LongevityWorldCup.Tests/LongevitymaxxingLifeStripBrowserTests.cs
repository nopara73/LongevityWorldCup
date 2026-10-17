using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LongevitymaxxingLifeStripBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390, 844, 200, 1)]
    [InlineData(320, 640, 200, 1)]
    [InlineData(390, 844, 100, 2)]
    [InlineData(320, 640, 100, 2)]
    [InlineData(768, 1024, 200, 2)]
    [InlineData(1440, 900, 100, 4)]
    public async Task LifeStrip_KeepsEachLabelInsideItsOwnTile(int width, int height, int textPercent, int expectedColumns)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await page.Locator("#lmxLifeStrip span").First.WaitForAsync();
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.WaitForFunctionAsync(
            "percent => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 16 * percent / 100 - 0.5",
            textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");

        var layout = await page.EvaluateAsync<string[]>(
            """
            () => {
                const strip = document.getElementById('lmxLifeStrip');
                const tiles = [...strip.querySelectorAll('span')];
                const problems = [];
                const boxes = tiles.map(tile => tile.getBoundingClientRect());
                tiles.forEach((tile, index) => {
                    if (tile.scrollWidth > tile.clientWidth + 1)
                        problems.push(`${tile.textContent.trim()} spills out of its tile (${tile.scrollWidth}/${tile.clientWidth}px)`);
                    const range = document.createRange();
                    range.selectNodeContents(tile);
                    const text = range.getBoundingClientRect();
                    if (text.left < boxes[index].left - 1 || text.right > boxes[index].right + 1)
                        problems.push(`${tile.textContent.trim()} text runs past its tile`);
                });
                for (let a = 0; a < boxes.length; a++)
                    for (let b = a + 1; b < boxes.length; b++)
                        if (boxes[a].left < boxes[b].right - 1 && boxes[b].left < boxes[a].right - 1 && boxes[a].top < boxes[b].bottom - 1 && boxes[b].top < boxes[a].bottom - 1)
                            problems.push(`${tiles[a].textContent.trim()} overlaps ${tiles[b].textContent.trim()}`);
                const columns = new Set(boxes.map(box => Math.round(box.left))).size;
                return [String(columns), ...problems];
            }
            """);
        var scenario = $"{width}x{height} at {textPercent}%";
        Assert.True(layout.Length == 1, $"{scenario}: {string.Join("; ", layout.Skip(1))}");
        Assert.Equal(expectedColumns.ToString(), layout[0]);
    }
}
