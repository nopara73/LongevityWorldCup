using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PodiumEnlargedTextBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390, 844, 200, true)]
    [InlineData(320, 640, 200, true)]
    [InlineData(390, 844, 100, false)]
    [InlineData(320, 640, 100, false)]
    public async Task PhonePodium_StacksFullWidthCardsWithWholeWordsAtEnlargedText(int width, int height, int textPercent, bool stacked)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/");
        await page.Locator(".podium-item.third .athlete-name").WaitForAsync();
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.WaitForFunctionAsync(
            "percent => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 16 * percent / 100 - 0.5",
            textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");

        var layout = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const podium = document.querySelector('.podium').getBoundingClientRect();
                const cards = ['first', 'second', 'third'].map(place => document.querySelector(`.podium-item.${place}`));
                const boxes = cards.map(card => card.getBoundingClientRect());
                for (const text of document.querySelectorAll('.podium-item :is(.athlete-name, .age-reduction, .prize-money)')) {
                    if (!text.getClientRects().length) continue;
                    const walker = document.createTreeWalker(text, NodeFilter.SHOW_TEXT);
                    for (let node; (node = walker.nextNode());) {
                        for (const word of node.textContent.matchAll(/[^\s ]+/g)) {
                            const range = document.createRange();
                            range.setStart(node, word.index);
                            range.setEnd(node, word.index + word[0].length);
                            if (new Set([...range.getClientRects()].filter(rect => rect.width > 0).map(rect => Math.round(rect.top))).size > 1)
                                problems.push(`"${word[0]}" breaks inside ${text.className}`);
                        }
                    }
                    const card = text.closest('.podium-item').getBoundingClientRect();
                    const box = text.getBoundingClientRect();
                    if (box.left < card.left - 1 || box.right > card.right + 1)
                        problems.push(`${text.className} leaves its card`);
                }
                const fullWidth = boxes.every(box => box.width >= podium.width - 2);
                const portrait = cards[0].querySelector('.podium-portrait').getBoundingClientRect();
                const name = cards[0].querySelector('.athlete-name').getBoundingClientRect();
                const nameBelowPortrait = name.top >= portrait.bottom - 1;
                return [String(fullWidth), String(nameBelowPortrait), ...problems];
            }
            """);
        var scenario = $"{width}x{height} at {textPercent}%";
        Assert.True(layout.Length == 2, $"{scenario}: {string.Join("; ", layout.Skip(2))}");
        Assert.True(layout[0] == stacked.ToString().ToLowerInvariant(), $"{scenario}: every card full width should be {stacked}.");
        Assert.True(layout[1] == stacked.ToString().ToLowerInvariant(), $"{scenario}: champion name under the portrait should be {stacked}.");
    }
}
