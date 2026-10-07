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
    [InlineData(576, 100, false)]
    [InlineData(577, 100, false)]
    [InlineData(320, 200, false)]
    [InlineData(320, 200, true)]
    [InlineData(390, 200, false)]
    [InlineData(1280, 100, false)]
    [InlineData(1280, 200, false)]
    public async Task Swag_ProductsRemainReadableAndKeyboardAccessible(int width, int textPercent, bool dark)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.SetViewportSizeAsync(width, 850);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var mobile = width <= 576;
        var carousel = page.Locator("#lwc-merch-mobile-carousel");
        var cards = mobile ? carousel.Locator(".lwc-merch-mobile-slide") : page.Locator(".lwc-merch-grid .lwc-merch-item");
        if (mobile)
        {
            await Assertions.Expect(carousel).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".lwc-merch-grid")).ToBeHiddenAsync();
        }
        else
        {
            await Assertions.Expect(carousel).ToBeHiddenAsync();
        }
        await Assertions.Expect(cards).ToHaveCountAsync(3);
        string[] products = ["white-glossy-mug", "organic-cotton-hoodie", "organic-cotton-cap"];
        var positions = await cards.EvaluateAllAsync<float[]>("cards => cards.map(card => card.offsetTop)");
        for (var index = 0; index < products.Length; index++)
        {
            if (mobile)
            {
                var dot = carousel.Locator(".lwc-merch-mobile-dot").Nth(index);
                await dot.FocusAsync();
                await dot.PressAsync("Enter");
                Assert.True(await cards.Nth(index).EvaluateAsync<bool>("card => document.activeElement === card"),
                    "Keyboard selection must move focus to the chosen product so Enter can open it.");
                await Assertions.Expect(dot).ToHaveAttributeAsync("aria-pressed", "true");
                var dotBox = Assert.IsType<LocatorBoundingBoxResult>(await dot.BoundingBoxAsync());
                Assert.True(dotBox.Width >= 44 && dotBox.Height >= 44, "Product selectors must retain usable touch targets.");
                await Assertions.Expect(carousel.Locator(".is-active.lwc-merch-mobile-slide")).ToHaveCountAsync(1);
                await Assertions.Expect(cards.Nth(index)).ToHaveAttributeAsync("aria-hidden", "false");
                for (var other = 0; other < products.Length; other++)
                {
                    if (other != index)
                    {
                        await Assertions.Expect(cards.Nth(other)).ToHaveAttributeAsync("aria-hidden", "true");
                        await Assertions.Expect(cards.Nth(other)).ToHaveAttributeAsync("tabindex", "-1");
                    }
                }
            }
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
                        return [...range.getClientRects()].every(rect => rect.left >= bounds.left - 1 && rect.right <= bounds.right + 1
                            && rect.top >= bounds.top - 1 && rect.bottom <= bounds.bottom + 1);
                    });
                }
                """), "Product titles and prices must stay inside their card, without overlapping another product.");
        }
        if (!mobile && textPercent == 100)
        {
            Assert.True(positions.Max() - positions.Min() <= 1, "The three products should share one row at normal text size.");
        }
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(320)]
    [InlineData(1280)]
    public async Task Swag_WithoutJavaScriptKeepsAllProductLinksAvailable(int width)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            JavaScriptEnabled = false,
            ViewportSize = new() { Width = width, Height = 850 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#lwc-merch-mobile-carousel")).ToBeHiddenAsync();
        var cards = page.Locator(".lwc-merch-grid .lwc-merch-item");
        await Assertions.Expect(cards).ToHaveCountAsync(3);
        for (var index = 0; index < 3; index++)
        {
            await Assertions.Expect(cards.Nth(index)).ToBeVisibleAsync();
            await cards.Nth(index).FocusAsync();
            Assert.True(await cards.Nth(index).EvaluateAsync<bool>("card => document.activeElement === card"));
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Swag_MobileProductKeyboardFocusIsVisible(bool forcedColors)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 850);
        await page.EmulateMediaAsync(new() { ForcedColors = forcedColors ? ForcedColors.Active : ForcedColors.None });
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var track = page.Locator(".lwc-merch-mobile-track");
        var product = track.Locator(".lwc-merch-mobile-slide.is-active");
        await track.ScrollIntoViewIfNeededAsync();
        await product.Locator("img").EvaluateAsync("img => img.decode()");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var before = await track.ScreenshotAsync();
        await product.FocusAsync();
        Assert.True(await product.EvaluateAsync<bool>("link => link.matches(':focus-visible')"));
        var after = await track.ScreenshotAsync();
        Assert.False(before.SequenceEqual(after), "The focused product must have a visible indicator inside the clipped carousel track.");
    }
}
