using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.HomepageChromeRegressionBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HomepagePrizePoolBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, false)]
    [InlineData(390, true)]
    [InlineData(640, false)]
    [InlineData(1440, true)]
    public async Task EnlargedCopyConfirmation_KeepsTheFocusedControlInsideTheTicket(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, ReducedMotion.Reduce);
        await context.AddInitScriptAsync("Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async () => {} } });");
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.SetViewportSizeAsync(width, 844);
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.GotoAsync("/contribute", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '32px'");
        await SettleLayoutAsync(page);
        var button = page.Locator("#bitcoinCopyButton");
        await Assertions.Expect(button).ToBeEnabledAsync();
        await GuessMyAgeBrowserTests.PauseClockAsync(page);
        await button.FocusAsync();
        await button.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#copiedText")).ToHaveTextAsync("Copied");
        Assert.True(await page.Locator(".btc-address").EvaluateAsync<bool>("""
            e => {
                const box = e.getBoundingClientRect(), button = e.querySelector('button');
                const b = button.getBoundingClientRect();
                const range = document.createRange(); range.selectNodeContents(e.querySelector('#copiedText'));
                return e.scrollWidth <= e.clientWidth + 1 && e.scrollHeight <= e.clientHeight + 1
                    && document.activeElement === button && b.left >= box.left - 1 && b.right <= box.right + 1
                    && b.top >= box.top - 1 && b.bottom <= box.bottom + 1
                    && [...range.getClientRects()].every(r => r.left >= box.left && r.right <= box.right);
            }
            """), $"The confirmation and focused copy button must fit at {width}px with enlarged text.");
    }

    [Theory]
    [InlineData(305)]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(640)]
    [InlineData(768)]
    [InlineData(1440)]
    public async Task EnlargedPrizeAmountAndUnit_KeepTheirCharactersTogether(int width)
    {
        await using var context = await NewContextAsync(Browser, App, ReducedMotion.Reduce);
        await context.RouteAsync("**/api/bitcoin/total-received", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = "{\"totalReceivedSatoshis\":831766}"
        }));
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 844);
        await page.GotoAsync("/contribute", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#current-btc")).ToHaveTextAsync("0.0075");
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '32px'");
        await SettleLayoutAsync(page);
        foreach (var selector in new[] { "#current-btc", ".btc-status-unit" })
        {
            Assert.True(await page.Locator(selector).EvaluateAsync<bool>("""
                e => {
                    const range = document.createRange(); range.selectNodeContents(e);
                    const rects = [...range.getClientRects()];
                    const panel = e.closest('.btc-status').getBoundingClientRect();
                    return rects.length === 1 && rects[0].left >= panel.left
                        && rects[0].right <= panel.right;
                }
                """), $"{selector} must remain a readable whole at {width}px with enlarged text.");
        }
    }

    [Theory]
    [InlineData(831766, "0.0075", "0.0100")]
    [InlineData(0, "0.000000", "0.010000")]
    [InlineData(100000000, "0.90", "1.00")]
    [InlineData(200000000, "1.8", "10.0")]
    [InlineData(10000000000, "90", "100")]
    public async Task LoadedTotal_RemainsAvailableBeforeTheMeterIsViewed(long satoshis, string amount, string goal)
    {
        await using var context = await NewContextAsync(Browser, App);
        await context.RouteAsync("**/api/bitcoin/total-received", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = $"{{\"totalReceivedSatoshis\":{satoshis}}}"
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator(".btc-status-track")).ToHaveAttributeAsync("aria-valuetext", $"{amount} of {goal} BTC");
        await Assertions.Expect(page.Locator("#current-btc")).ToHaveTextAsync(amount);
        await Assertions.Expect(page.Locator("#goal-btc")).ToHaveTextAsync(goal);
        Assert.True(await page.Locator(".btc-status").EvaluateAsync<bool>("e => e.getBoundingClientRect().top > innerHeight"));
        if (satoshis == 0)
            Assert.Equal(0, await page.Locator(".btc-status-fill").EvaluateAsync<double>("e => e.getBoundingClientRect().width"));
    }

    [Theory]
    [InlineData(503, "unavailable")]
    [InlineData(200, "{\"totalReceivedSatoshis\":null}")]
    [InlineData(200, "{\"totalReceivedSatoshis\":-1}")]
    public async Task UnavailableFund_NeverInventsAnAmountOrProgress(int status, string body)
    {
        await using var context = await NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var release = new TaskCompletionSource();
        await context.RouteAsync("**/api/bitcoin/total-received", async route =>
        {
            await release.Task;
            await route.FulfillAsync(new() { Status = status, ContentType = "application/json", Body = body });
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/contribute", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Assertions.Expect(page.Locator("#current-btc")).ToHaveTextAsync("—");
            Assert.Null(await page.Locator(".btc-status-track").GetAttributeAsync("aria-valuenow"));
        }
        finally { release.TrySetResult(); }
        await Assertions.Expect(page.Locator(".btc-status-track")).ToHaveAttributeAsync("aria-valuetext", "Prize pool unavailable");
        await Assertions.Expect(page.Locator("#current-btc")).ToHaveTextAsync("—");
        Assert.Equal(0, await page.Locator(".btc-status-fill").EvaluateAsync<double>("e => e.getBoundingClientRect().width"));
    }

    [Theory]
    [InlineData("pointer")]
    [InlineData("focus")]
    [InlineData("reduced")]
    [InlineData("pagehide")]
    public async Task InterruptedCount_KeepsTheAccessibleTotalAndReleasesMotion(string interruption)
    {
        await using var context = await NewContextAsync(Browser, App);
        await context.RouteAsync("**/api/bitcoin/total-received", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = "{\"totalReceivedSatoshis\":831766}"
        }));
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator(".btc-status-track")).ToHaveAttributeAsync("aria-valuetext", "0.0075 of 0.0100 BTC");
        // Hold the rAF counter until the intended interruption reaches the page.
        await GuessMyAgeBrowserTests.PauseClockAsync(page);
        await page.Locator(".btc-status").ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.btc-status').classList.contains('is-counting')");
        // The changing visual is decorative; the real DOM/accessibility value is stable.
        await Assertions.Expect(page.Locator("#current-btc")).ToHaveTextAsync("0.0075");
        if (interruption == "reduced") await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        else if (interruption == "focus") await page.Locator("#bitcoinCopyButton").FocusAsync();
        else if (interruption == "pointer") await page.Mouse.ClickAsync(2, 400);
        else await page.EvaluateAsync("() => dispatchEvent(new PageTransitionEvent('pagehide'))");
        await page.WaitForFunctionAsync("() => !document.querySelector('.btc-status').classList.contains('is-counting')");
        Assert.Null(await page.Locator("#current-btc").GetAttributeAsync("data-pool-count"));
        Assert.False(await page.Locator(".btc-status").EvaluateAsync<bool>("e => e.classList.contains('is-landed')"));
        Assert.InRange(await page.Locator(".btc-status-fill").EvaluateAsync<double>("e => parseFloat(e.style.width)"), 74.85, 74.87);
    }

    [Fact]
    public async Task ReducedMotionBeforeViewing_DoesNotRestartTheCountOnArrival()
    {
        await using var context = await NewContextAsync(Browser, App);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.querySelector('.btc-status').dataset.poolPlayed === 'true'");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => cancelPrizePoolFill === null");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.Locator(".btc-status").ScrollIntoViewIfNeededAsync();
        await SettleLayoutAsync(page);
        Assert.False(await page.Locator(".btc-status").EvaluateAsync<bool>("e => e.classList.contains('is-counting')"));
    }

    [Fact]
    public async Task FailedCopyAfterSuccess_ClearsTheSuccessStampAndPreservesManualCopy()
    {
        await using var context = await NewContextAsync(Browser, App, ReducedMotion.Reduce);
        await context.AddInitScriptAsync("""
            Object.defineProperty(navigator, 'clipboard', { configurable: true, value: {
                writeText: async () => { if (window.failCopy) throw new Error('denied'); }
            }});
            document.execCommand = () => false;
            """);
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync("/contribute", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var button = page.Locator("#bitcoinCopyButton");
        await Assertions.Expect(button).ToBeEnabledAsync();
        await GuessMyAgeBrowserTests.PauseClockAsync(page);
        await button.ClickAsync();
        await Assertions.Expect(page.Locator("#copiedText")).ToHaveTextAsync("Copied");
        Assert.True(await page.Locator(".btc-address").EvaluateAsync<bool>("e => e.classList.contains('is-copied')"));
        await page.EvaluateAsync("() => window.failCopy = true");
        await button.ClickAsync();
        await Assertions.Expect(page.Locator("#bitcoinAddressFallback")).ToBeFocusedAsync();
        Assert.False(await page.Locator(".btc-address").EvaluateAsync<bool>("e => e.classList.contains('is-copied')"));
        await Assertions.Expect(button.Locator("i")).ToHaveClassAsync("fas fa-copy");
        await Assertions.Expect(page.Locator("#bitcoinCopyHelp")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ReducedMotionSubscription_DoesNotLeaveAPendingPlaneAnimation()
    {
        await using var context = await NewContextAsync(Browser, App, ReducedMotion.Reduce);
        await context.RouteAsync("**/api/home/subscribe", route => route.FulfillAsync(new()
        {
            Status = 200, ContentType = "text/plain", Body = "Subscription successful."
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#emailInput").FillAsync("test@example.test");
        await page.Locator("#newsletter-form button").ClickAsync();
        await Assertions.Expect(page.Locator("#newsletterStatus")).ToHaveTextAsync("Subscribed with test@example.test.");
        Assert.False(await page.Locator("#newsletter-form").EvaluateAsync<bool>("e => e.classList.contains('is-sent')"));
    }
}
