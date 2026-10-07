using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class HomepageMerchBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, 100, false)]
    [InlineData(390, 100, false)]
    [InlineData(320, 200, false)]
    [InlineData(320, 200, true)]
    [InlineData(390, 200, false)]
    [InlineData(1280, 100, false)]
    [InlineData(1280, 200, false)]
    public async Task Swag_AllProductsRemainReadableAndKeyboardAccessible(int width, int textPercent, bool dark)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.SetViewportSizeAsync(width, 850);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var cards = page.Locator(".lwc-merch-grid .lwc-merch-item");
        await Assertions.Expect(cards).ToHaveCountAsync(3);
        string[] products = ["white-glossy-mug", "organic-cotton-hoodie", "organic-cotton-cap"];
        var positions = await cards.EvaluateAllAsync<float[]>("cards => cards.map(card => card.offsetTop)");
        for (var index = 0; index < products.Length; index++)
        {
            var card = cards.Nth(index);
            await Assertions.Expect(card).ToBeVisibleAsync();
            Assert.Equal($"https://merch.longevityworldcup.com/product/{products[index]}/", await card.GetAttributeAsync("href"));
            await card.FocusAsync();
            Assert.True(await card.EvaluateAsync<bool>("card => document.activeElement === card"));
            var box = Assert.IsType<LocatorBoundingBoxResult>(await card.BoundingBoxAsync());
            Assert.True(box.Width >= 44 && box.Height >= 44, "Every product must remain a usable touch target.");
            Assert.True(box.X >= 0 && box.X + box.Width <= width, "Product cards must fit the viewport.");
            Assert.True(await card.EvaluateAsync<bool>(
                """
                card => {
                    const bounds = card.getBoundingClientRect();
                    return [...card.querySelectorAll('.lwc-merch-item-title, .lwc-merch-item-price')].every(label => {
                        const range = document.createRange();
                        range.selectNodeContents(label);
                        return [...range.getClientRects()].every(rect => rect.left >= bounds.left - 1 && rect.right <= bounds.right + 1);
                    });
                }
                """), "Product titles and prices must stay inside their card, without overlapping another product.");
        }
        if (textPercent == 100)
        {
            Assert.True(positions.Max() - positions.Min() <= 1, "The three products should share one row at normal text size.");
        }
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }
}
