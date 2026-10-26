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
            if (mobile)
            {
                var imageBox = Assert.IsType<LocatorBoundingBoxResult>(await card.Locator("img").BoundingBoxAsync());
                Assert.True(imageBox.Height >= 100, "Product photos must remain identifiable when text is enlarged.");
            }
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
    [Fact]
    public async Task Swag_ForcedColorsKeepsEveryProductSelectorVisible()
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 850);
        await page.EmulateMediaAsync(new() { ForcedColors = ForcedColors.Active });
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var dots = page.Locator(".lwc-merch-mobile-dot");
        await dots.First.ScrollIntoViewIfNeededAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var visible = new byte[3][];
        for (var index = 0; index < visible.Length; index++)
        {
            visible[index] = await dots.Nth(index).ScreenshotAsync();
        }
        var suppression = await page.AddStyleTagAsync(new() { Content = ".lwc-merch-mobile-dot::before { visibility: hidden !important; }" });
        for (var index = 0; index < visible.Length; index++)
        {
            var empty = await dots.Nth(index).ScreenshotAsync();
            Assert.False(visible[index].SequenceEqual(empty), "Every selector must have a visible mark in forced colors.");
        }
        await suppression.EvaluateAsync("style => style.remove()");
        await dots.Nth(1).FocusAsync();
        await dots.Nth(1).PressAsync("Space");
        await Assertions.Expect(dots.Nth(1)).ToHaveAttributeAsync("aria-pressed", "true");
        var selected = await dots.Nth(1).ScreenshotAsync();
        Assert.False(visible[1].SequenceEqual(selected), "The selected product must remain visually distinguishable in forced colors.");
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

    [Theory]
    [InlineData(390, false)]
    [InlineData(320, true)]
    public async Task Swag_PhoneCarouselSwipesWithTheNextProductPeeking(int width, bool dark)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new() { Width = width, Height = 850 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(
            """
            window.swagTouchTrace = [];
            for (const type of ['touchstart', 'touchend', 'touchcancel', 'scrollend']) document.addEventListener(type, event => {
                const track = document.querySelector('.lwc-merch-mobile-track');
                const point = event.changedTouches?.[0];
                window.swagTouchTrace.push({ type, target: event.target.className, x: point?.clientX, y: point?.clientY,
                    scroll: track?.scrollLeft, focus: document.activeElement.className, time: performance.now() });
            }, { capture: true, passive: true });
            """);
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.SetViewportSizeAsync(width, 850);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var carousel = page.Locator("#lwc-merch-mobile-carousel");
        await Assertions.Expect(carousel).ToBeVisibleAsync();

        var peek = await page.EvaluateAsync<double[]>(
            """
            () => {
                const track = document.querySelector('.lwc-merch-mobile-track');
                const slides = [...track.querySelectorAll('.lwc-merch-mobile-slide')];
                const box = track.getBoundingClientRect();
                const next = slides[1].getBoundingClientRect();
                return [box.right - next.left, track.scrollWidth - track.clientWidth];
            }
            """);
        Assert.True(peek[0] >= 16, $"{width}px: the next product should peek into view ({peek[0]:F0}px visible) to show the carousel can be swiped.");
        Assert.True(peek[1] > 0, $"{width}px: the products should be a swipeable strip, not stacked slides.");

        // Send real touch input through Chromium; assigning scrollLeft cannot
        // verify gesture handling, snap stops, or vertical page scrolling.
        await carousel.ScrollIntoViewIfNeededAsync();
        await SwipeAsync(page, page.Locator(".lwc-merch-mobile-track"), forward: true);
        var dots = carousel.Locator(".lwc-merch-mobile-dot");
        var cards = carousel.Locator(".lwc-merch-mobile-slide");
        await Assertions.Expect(dots.Nth(1)).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(cards.Nth(1)).ToHaveAttributeAsync("aria-hidden", "false");
        await Assertions.Expect(cards.Nth(0)).ToHaveAttributeAsync("aria-hidden", "true");
        await WaitForProductPositionAsync(page, 1);

        await cards.Nth(1).FocusAsync();
        await SwipeAsync(page, page.Locator(".lwc-merch-mobile-track"), forward: true, fling: true);
        await Assertions.Expect(dots.Nth(2)).ToHaveAttributeAsync("aria-pressed", "true");
        await WaitForProductPositionAsync(page, 2);
        Assert.False(await page.EvaluateAsync<bool>("!!document.activeElement.closest('[aria-hidden=true], [inert]')"),
            "Swiping must never leave focus in a hidden product.");
        await SwipeAsync(page, page.Locator(".lwc-merch-mobile-track"), forward: false);
        await Assertions.Expect(dots.Nth(1)).ToHaveAttributeAsync("aria-pressed", "true");
        await WaitForProductPositionAsync(page, 1);
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
        await SwipeAsync(page, page.Locator(".lwc-merch-mobile-track"), forward: false, fling: true);
        await Assertions.Expect(dots.Nth(0)).ToHaveAttributeAsync("aria-pressed", "true");
        await WaitForProductPositionAsync(page, 0);

        await page.Locator(".lwc-merch-mobile-track").ScrollIntoViewIfNeededAsync();
        var scrollBefore = await page.EvaluateAsync<double>("scrollY");
        await SwipeAsync(page, page.Locator(".lwc-merch-mobile-track"), forward: true, vertical: true);
        await page.WaitForFunctionAsync("before => scrollY > before + 30", scrollBefore);
        await Assertions.Expect(dots.Nth(0)).ToHaveAttributeAsync("aria-pressed", "true");

        // A dot brings its product back into place.
        await dots.Nth(2).ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const track = document.querySelector('.lwc-merch-mobile-track');
                const slide = track.querySelectorAll('.lwc-merch-mobile-slide')[2];
                return Math.abs(slide.getBoundingClientRect().right - track.getBoundingClientRect().right) <= 2
                    || Math.abs(slide.getBoundingClientRect().left - track.getBoundingClientRect().left) <= 2;
            }
            """);
        await Assertions.Expect(cards.Nth(2)).ToHaveAttributeAsync("aria-hidden", "false");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));

        // The page gesture still opens filters when it starts outside a scroller.
        await SwipeAsync(page, page.Locator(".lwc-merch-copy"), forward: false);
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "true");
    }

    [Fact]
    public async Task Swag_PeekingProductKeepsItsDestinationAndExposesItsFocus()
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        await context.RouteAsync("https://merch.longevityworldcup.com/**", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "text/html",
            Body = "<title>Product navigation intercepted</title>"
        }));
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var track = page.Locator(".lwc-merch-mobile-track");
        await track.ScrollIntoViewIfNeededAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var box = Assert.IsType<LocatorBoundingBoxResult>(await track.BoundingBoxAsync());
        var popupTask = page.WaitForPopupAsync();
        await page.Mouse.ClickAsync(box.X + box.Width - 8, box.Y + 60);
        var popup = await popupTask;
        await popup.WaitForDocumentLoadedAsync();
        Assert.Equal("https://merch.longevityworldcup.com/product/organic-cotton-hoodie/", popup.Url);
        await popup.CloseAsync();
        await WaitForProductPositionAsync(page, 1);
        await Assertions.Expect(page.Locator(".lwc-merch-mobile-slide").Nth(1)).ToHaveAttributeAsync("aria-hidden", "false");
        Assert.False(await page.EvaluateAsync<bool>("!!document.activeElement.closest('[aria-hidden=true], [inert]')"));
    }

    [Fact]
    public async Task Swag_SmoothKeyboardSelectionNeverHidesFocusedProductOrReversesItsDot()
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.NoPreference);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var dot = page.Locator(".lwc-merch-mobile-dot").Nth(2);
        await dot.FocusAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        await page.EvaluateAsync(
            """
            () => {
                window.merchScrollSamples = [];
                const track = document.querySelector('.lwc-merch-mobile-track');
                const record = () => window.merchScrollSamples.push({
                    hiddenFocus: !!document.activeElement.closest('[aria-hidden=true], [inert]'),
                    selected: document.querySelectorAll('.lwc-merch-mobile-dot')[2].getAttribute('aria-pressed')
                });
                track.addEventListener('scroll', () => requestAnimationFrame(record));
                new MutationObserver(record).observe(track, { subtree: true, attributes: true });
            }
            """);
        await dot.PressAsync("Enter");
        await WaitForProductPositionAsync(page, 2);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        Assert.True(await page.EvaluateAsync<bool>(
            "window.merchScrollSamples.length > 1 && window.merchScrollSamples.every(sample => !sample.hiddenFocus && sample.selected === 'true')"),
            "Every smooth-scroll frame must keep the keyboard-selected product exposed and its dot selected.");
        await Assertions.Expect(page.Locator(".lwc-merch-mobile-slide").Nth(2)).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Swag_ResizeTransfersFocusAndKeepsTheSelectedProductAlignedAfterTextChanges(bool blurHiddenFocusBeforeMediaChange)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        if (blurHiddenFocusBeforeMediaChange)
        {
            await context.AddInitScriptAsync("""
                // A browser may blur display:none content before dispatching the
                // media-query change. Exercise that ordering deterministically.
                const originalMatchMedia = window.matchMedia.bind(window);
                window.matchMedia = query => {
                    const media = originalMatchMedia(query);
                    if (query !== '(max-width: 576px)') return media;
                    const addListener = media.addEventListener.bind(media);
                    media.addEventListener = (type, listener, options) => addListener(type, event => {
                        const focused = document.activeElement;
                        if (focused?.closest('.lwc-merch-body') && !focused.getClientRects().length)
                            focused.blur();
                        listener(event);
                    }, options);
                    return media;
                };
                """);
        }
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var slides = page.Locator(".lwc-merch-mobile-slide");
        var grid = page.Locator(".lwc-merch-grid .lwc-merch-item");
        var dots = page.Locator(".lwc-merch-mobile-dot");
        await dots.Nth(2).FocusAsync();
        await dots.Nth(2).PressAsync("Space");
        await WaitForProductPositionAsync(page, 2);
        await Assertions.Expect(slides.Nth(2)).ToBeFocusedAsync();
        await page.SetViewportSizeAsync(768, 844);
        await Assertions.Expect(grid.Nth(2)).ToBeFocusedAsync();
        for (var index = 0; index < 3; index++)
        {
            await Assertions.Expect(grid.Nth(index)).ToBeVisibleAsync();
            Assert.False(await grid.Nth(index).EvaluateAsync<bool>("link => !!link.closest('[inert], [aria-hidden=true]')"));
        }
        await grid.Nth(1).FocusAsync();
        await page.SetViewportSizeAsync(390, 844);
        await Assertions.Expect(slides.Nth(1)).ToBeFocusedAsync();
        await WaitForProductPositionAsync(page, 1);
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await WaitForProductPositionAsync(page, 1);
        await page.SetViewportSizeAsync(320, 844);
        await WaitForProductPositionAsync(page, 1);
        await Assertions.Expect(dots.Nth(1)).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(slides.Nth(1)).ToHaveAttributeAsync("aria-hidden", "false");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));

        // A deliberate blur or focus moved elsewhere must not be restored by
        // a later breakpoint change.
        await slides.Nth(1).EvaluateAsync("link => link.blur()");
        await page.SetViewportSizeAsync(768, 844);
        await Assertions.Expect(page.Locator(".lwc-merch-grid")).ToHaveJSPropertyAsync("inert", false);
        await Assertions.Expect(page.Locator("#lwc-merch-mobile-carousel")).ToHaveJSPropertyAsync("inert", true);
        Assert.True(await page.EvaluateAsync<bool>("document.activeElement === document.body"));
        await grid.Nth(0).FocusAsync();
        await page.Locator("#emailInput").FocusAsync();
        await page.SetViewportSizeAsync(390, 844);
        await Assertions.Expect(page.Locator(".lwc-merch-grid")).ToHaveJSPropertyAsync("inert", true);
        await Assertions.Expect(page.Locator("#lwc-merch-mobile-carousel")).ToHaveJSPropertyAsync("inert", false);
        await Assertions.Expect(page.Locator("#emailInput")).ToBeFocusedAsync();
    }

    private static async Task WaitForProductPositionAsync(IPage page, int index)
    {
        try
        {
            await page.WaitForFunctionAsync(
            """
            index => {
                const track = document.querySelector('.lwc-merch-mobile-track');
                const slide = track.querySelectorAll('.lwc-merch-mobile-slide')[index];
                const target = Math.min(slide.offsetLeft, track.scrollWidth - track.clientWidth);
                return Math.abs(track.scrollLeft - target) <= 1;
            }
            """, index, new() { Timeout = 5000 });
            await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        }
        catch (TimeoutException exception)
        {
            var evidence = await page.EvaluateAsync<string>(
                """
                () => JSON.stringify({ scroll: document.querySelector('.lwc-merch-mobile-track').scrollLeft,
                    snap: getComputedStyle(document.querySelector('.lwc-merch-mobile-track')).scrollSnapType,
                    visible: document.visibilityState, hasFocus: document.hasFocus(),
                    slides: [...document.querySelectorAll('.lwc-merch-mobile-slide')].map(slide => ({ offset: slide.offsetLeft, active: slide.classList.contains('is-active') })),
                    focus: document.activeElement.outerHTML.slice(0, 250), trace: window.swagTouchTrace })
                """);
            throw new InvalidOperationException($"Product {index} did not snap into place: {evidence}", exception);
        }
    }

    private static async Task SwipeAsync(IPage page, ILocator track, bool forward, bool fling = false, bool vertical = false)
    {
        await track.ScrollIntoViewIfNeededAsync();
        var box = Assert.IsType<LocatorBoundingBoxResult>(await track.BoundingBoxAsync());
        var startX = box.X + box.Width * (forward && !vertical ? 0.9f : 0.1f);
        var startY = box.Y + Math.Min(box.Height * 0.7f, 200);
        // The final snap is clamped, so a long reverse drag can already pass
        // the middle snap before release. Keep single-step drags short; flings
        // still use a longer, fast gesture to exercise native momentum.
        var distance = vertical ? Math.Min(box.Height * 0.6f, 150) : box.Width * (fling ? 0.8f : 0.6f);
        var steps = fling ? 4 : 12;
        var session = await page.Context.NewCDPSessionAsync(page);
        await session.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x = startX, y = startY } } });
        for (var step = 1; step <= steps; step++)
        {
            var x = vertical ? startX : startX + (forward ? -distance : distance) * step / steps;
            var y = vertical ? startY - distance * step / steps : startY;
            await session.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x, y } } });
            await Task.Delay(fling ? 10 : 20);
        }
        // Stop before lifting so the slow drag ends with the finger at rest.
        // Fling cases deliberately retain their release velocity.
        if (!fling) await Task.Delay(150);
        await session.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        await session.DetachAsync();
    }
}
