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
    [InlineData(505, 844, 200, true)]
    [InlineData(520, 844, 200, true)]
    [InlineData(544, 844, 200, true)]
    [InlineData(545, 844, 200, true)]
    [InlineData(576, 844, 200, true)]
    [InlineData(577, 844, 200, false)]
    [InlineData(390, 844, 150, true)]
    [InlineData(390, 844, 175, true)]
    [InlineData(320, 640, 125, true)]
    [InlineData(390, 844, 125, false)]
    [InlineData(390, 844, 100, false)]
    [InlineData(320, 640, 100, false)]
    [InlineData(768, 1024, 100, false)]
    [InlineData(844, 390, 100, false)]
    [InlineData(1440, 900, 100, false)]
    [InlineData(844, 844, 175, true)]
    [InlineData(844, 844, 200, true)]
    [InlineData(1024, 900, 200, true)]
    [InlineData(1440, 900, 200, false)]
    public async Task Podium_StacksFullWidthCardsWithWholeWordsAtEnlargedText(int width, int height, int textPercent, bool stacked)
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
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".podium-item.third .athlete-name").WaitForAsync();
        var originalSizes = await MeasureFontSizesAsync(page);
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.WaitForFunctionAsync(
            "percent => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 16 * percent / 100 - 0.5",
            textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");
        await WaitForFiniteAnimationsAsync(page);
        var enlargedSizes = await MeasureFontSizesAsync(page);
        for (var i = 0; i < originalSizes.Length; i++)
            Assert.InRange(enlargedSizes[i], originalSizes[i] * textPercent / 100 - 0.1, originalSizes[i] * textPercent / 100 + 0.1);

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
                const mobile = matchMedia('(max-width: 768px), (max-width: 932px) and (max-height: 480px) and (orientation: landscape)').matches;
                const nameBelowPortrait = (mobile || fullWidth) && cards[0].querySelector('.athlete-name').getBoundingClientRect().top
                    >= cards[0].querySelector('.podium-portrait').getBoundingClientRect().bottom - 1;
                if (fullWidth && !(boxes[0].bottom <= boxes[1].top && boxes[1].bottom <= boxes[2].top))
                    problems.push('stacked visual order is not first, second, third');
                for (const card of cards) {
                    const portrait = card.querySelector('.podium-portrait').getBoundingClientRect();
                    const medal = card.querySelector('.podium-rank').getBoundingClientRect();
                    if (Math.abs((medal.left + medal.right) / 2 - (portrait.left + portrait.right) / 2) > 1
                        || medal.top >= portrait.bottom || medal.bottom <= portrait.bottom)
                        problems.push('medal no longer follows its portrait');
                }
                return [String(fullWidth), String(nameBelowPortrait), ...problems];
            }
            """);
        var scenario = $"{width}x{height} at {textPercent}%";
        Assert.True(layout.Length == 2, $"{scenario}: {string.Join("; ", layout.Skip(2))}");
        Assert.True(layout[0] == stacked.ToString().ToLowerInvariant(), $"{scenario}: every card full width should be {stacked}.");
        Assert.True(layout[1] == stacked.ToString().ToLowerInvariant(), $"{scenario}: champion name under the portrait should be {stacked}.");
    }

    [Theory]
    [InlineData(320, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task StackedPodium_LongNamesAndLatePrizesPreserveGlyphsAndKeyboardOrder(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = true
        });
        var releasePrize = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/bitcoin/total-received", async route =>
        {
            await releasePrize.Task;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = """{"totalReceivedSatoshis":123456789}""" });
        });
        var page = await context.NewPageAsync();
        try
        {
            await NavigateAndSettleAsync(page, "/");
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
            await page.EvaluateAsync(
                """
                () => {
                    const names = ['Alexandria Cassandra von Hohenlohe', 'Alexandra Maria de la Cruz', 'Maximilian Alexander van der Meer'];
                    document.querySelectorAll('.podium-item .athlete-name').forEach((name, i) => name.textContent = names[i]);
                    window.podiumBeforePrize = [...document.querySelectorAll('.podium-item')];
                }
                """);
            var links = page.Locator(".podium-item a[href]");
            await links.First.FocusAsync();
            for (var i = 0; i < await links.CountAsync(); i++)
            {
                await Assertions.Expect(links.Nth(i)).ToBeFocusedAsync();
                await Assertions.Expect(links.Nth(i)).ToBeInViewportAsync();
                Assert.True(await links.Nth(i).EvaluateAsync<bool>("link => getComputedStyle(link).outlineStyle !== 'none'"));
                if (i == 0)
                {
                    releasePrize.TrySetResult();
                    await Assertions.Expect(page.Locator(".podium-item-lower[aria-busy=true]")).ToHaveCountAsync(0);
                    Assert.True(await page.EvaluateAsync<bool>("() => window.podiumBeforePrize.every((card, i) => card === document.querySelectorAll('.podium-item')[i])"));
                    await Assertions.Expect(links.Nth(i)).ToBeFocusedAsync();
                }
                await page.Keyboard.PressAsync("Tab");
            }
            var issues = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const issues = [];
                    for (const element of document.querySelectorAll('.podium-item :is(.athlete-name, .podium-score, .prize-money, .btc-amount)')) {
                        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                        for (let node; (node = walker.nextNode());) {
                            for (let i = 0; i < node.length; i++) {
                                if (/\s/.test(node.data[i])) continue;
                                const range = document.createRange(); range.setStart(node, i); range.setEnd(node, i + 1);
                                for (const glyph of range.getClientRects()) {
                                    if (!glyph.width) continue;
                                    if (glyph.left < -1 || glyph.right > innerWidth + 1) issues.push('glyph leaves viewport');
                                    for (let ancestor = node.parentElement; ancestor && ancestor !== document.body; ancestor = ancestor.parentElement) {
                                        const style = getComputedStyle(ancestor), box = ancestor.getBoundingClientRect();
                                        if (/hidden|clip/.test(style.overflowX) && (glyph.left < box.left - 1 || glyph.right > box.right + 1)) issues.push('glyph clipped horizontally');
                                        if (/hidden|clip/.test(style.overflowY) && (glyph.top < box.top - 1 || glyph.bottom > box.bottom + 1)) issues.push('glyph clipped vertically');
                                    }
                                }
                            }
                        }
                    }
                    return [...new Set(issues)];
                }
                """);
            Assert.True(issues.Length == 0, string.Join("; ", issues));
        }
        finally { releasePrize.TrySetResult(); }
    }

    [Fact]
    public async Task PodiumQuery_UsesAvailableContainerWidthAndItsFontSize()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 768, Height = 1024 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".podium-item")).ToHaveCountAsync(3);
        foreach (var percent in new[] { 100, 125, 150, 175, 200 })
        {
            await page.EvaluateAsync("p => document.documentElement.style.fontSize = `${p}%`", percent);
            await page.EvaluateAsync("() => document.fonts.ready");
            foreach (var (em, stacked) in new[] { (16.01, false), (15.99, true), (16.01, false) })
            {
                var widths = await page.Locator(".podium").EvaluateAsync<double[]>("""
                    async (podium, em) => {
                        podium.style.width = `${em}em`;
                        podium.style.marginInline = 'auto';
                        await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                        await Promise.all(podium.getAnimations({ subtree: true })
                            .filter(animation => Number.isFinite(animation.effect.getComputedTiming().endTime))
                            .map(animation => animation.finished.catch(() => {})));
                        await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                        return [parseFloat(getComputedStyle(podium).fontSize), podium.getBoundingClientRect().width,
                            ...[...podium.querySelectorAll('.podium-item')].map(card => card.getBoundingClientRect().width)];
                    }
                    """, em);
                Assert.Equal(5, widths.Length);
                Assert.True(Math.Abs(em * widths[0] - widths[1]) <= .1,
                    $"Podium width was {widths[1]}px; expected {em * widths[0]}px at {percent}% text.");
                var fullWidth = widths.Skip(2).All(width => Math.Abs(width - widths[1]) < 1);
                Assert.True(stacked == fullWidth,
                    $"Podium at {percent}% text and {em}em: container={widths[1]}, cards={string.Join(", ", widths.Skip(2))}.");
            }
        }
    }

    private static Task<double[]> MeasureFontSizesAsync(IPage page)
        => page.EvaluateAsync<double[]>("() => [...document.querySelectorAll('.podium-item :is(.athlete-name, .age-reduction, .prize-money)')].map(text => parseFloat(getComputedStyle(text).fontSize))");
}
