using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class OptionalBrowserFeaturesBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    public static IEnumerable<object[]> ProfileCases =>
        from failure in new[] { "localStorage-blocked", "sessionStorage-blocked", "localStorage-quota", "sessionStorage-quota" }
        from path in new[] { "/", "/leaderboard", "/athlete/michael-lustgarten" }
        from width in new[] { 390, 1280 }
        select new object[] { failure, path, width };

    [Theory]
    [MemberData(nameof(ProfileCases))]
    public async Task ProfileHydration_OptionalStorageCannotBecomeADataError(string failure, string path, int width)
    {
        await using var context = await NewContextAsync(width, failure);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        ILocator? opener = null;
        if (!path.StartsWith("/athlete/"))
        {
            await page.WaitForFunctionAsync("() => document.getElementById('leaderboardStatus')?.textContent === 'Leaderboard loaded.'");
            opener = page.Locator(".leaderboard tbody a.athlete-name").First;
            await opener.FocusAsync();
            await opener.PressAsync("Enter");
        }

        await AssertHydratedAsync(page);
        Assert.DoesNotContain("unavailable", await page.Locator("#athleteName").InnerTextAsync());
        if (path.StartsWith("/athlete/"))
        {
            await Assertions.Expect(page.Locator("#guessAgeContainer")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#athleteBio")).ToBeVisibleAsync();
        }
        else
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Skip", Exact = true }).ClickAsync();
            await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('guess-mode')");
            await Assertions.Expect(page.Locator("#closeAthleteDetailsModal")).ToBeFocusedAsync();
        }
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        if (opener is not null) await Assertions.Expect(opener).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("localStorage-blocked")]
    [InlineData("sessionStorage-blocked")]
    [InlineData("localStorage-quota")]
    [InlineData("sessionStorage-quota")]
    [InlineData("")]
    public async Task SkipAndSkipAll_PreserveCurrentPageChoicesAndImageHistory(string failure)
    {
        await using var context = await NewContextAsync(390, failure);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await page.WaitForFunctionAsync("() => document.getElementById('leaderboardStatus')?.textContent === 'Leaderboard loaded.'");
        var links = page.Locator(".leaderboard tbody a.athlete-name");
        await links.Nth(0).ClickAsync();
        await AssertHydratedAsync(page);
        var identity = new
        {
            slug = await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-athlete-slug"),
            profileImageId = await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-profile-image-id")
        };
        await page.GetByRole(AriaRole.Button, new() { Name = "Skip", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('guess-mode')");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await links.Nth(0).ClickAsync();
        await AssertHydratedAsync(page);
        await Assertions.Expect(page.Locator("#guessAgeContainer")).ToBeHiddenAsync();
        Assert.True(await page.EvaluateAsync<bool>("identity => window.LwcGuessState.get(identity)?.skipped === true", identity),
            await page.EvaluateAsync<string>("identity => JSON.stringify({ identity, saved: window.LwcGuessState.getAll() })", identity));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await links.Nth(1).ClickAsync();
        await AssertHydratedAsync(page);
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("e => e.classList.contains('gma-fast')"));
        await page.GetByRole(AriaRole.Button, new() { Name = "Skip all", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('guess-mode')");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await links.Nth(2).ClickAsync();
        await AssertHydratedAsync(page);
        await Assertions.Expect(page.Locator("#guessAgeContainer")).ToBeHiddenAsync();

        // The preference must not rewrite guesses or bind a different portrait to this history.
        Assert.True(await page.EvaluateAsync<bool>("""
            ({ imageA, imageB }) => {
                const identity = { slug: 'fictional-storage-athlete', profileImageId: imageA };
                window.LwcGuessState.set(identity, { value: 42, skipped: false, first: true, exact: true });
                window.LwcGuessState.ensureSkippedMany([identity, { ...identity, profileImageId: imageB }]);
                const saved = window.LwcGuessState.get(identity);
                return saved.value === 42 && saved.exact && saved.first && !saved.skipped
                    && window.LwcGuessState.get({ ...identity, profileImageId: imageB }).skipped
                    && window.LwcGuessState.get({ ...identity, profileImageId: 'c'.repeat(64) }) === null;
            }
            """, new { imageA = GuessMyAgeBrowserTests.ProfileImageA, imageB = GuessMyAgeBrowserTests.ProfileImageB }));
        if (failure is "" or "sessionStorage-blocked" or "sessionStorage-quota")
        {
            Assert.Equal("true", await page.EvaluateAsync<string>("localStorage.getItem('gmaSkipAll')"));
            await page.ReloadAsync();
            await AssertHydratedAsync(page);
            Assert.Equal(42, await page.EvaluateAsync<int>("imageId => window.LwcGuessState.get({ slug: 'fictional-storage-athlete', profileImageId: imageId }).value", GuessMyAgeBrowserTests.ProfileImageA));
        }
    }

    [Theory]
    [InlineData("localStorage-blocked")]
    [InlineData("sessionStorage-blocked")]
    [InlineData("localStorage-quota")]
    [InlineData("sessionStorage-quota")]
    public async Task SubmittedGuess_RemainsAvailableForItsPortraitWhenStorageFails(string failure)
    {
        await using var context = await NewContextAsync(390, failure);
        await context.RouteAsync("**/api/Guess/athlete-age**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """{"crowdAge":40,"crowdCount":12,"actualAge":41,"guessAccepted":true}"""
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/michael-lustgarten?guessmyage=1");
        await AssertHydratedAsync(page);
        var identity = new
        {
            slug = await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-athlete-slug"),
            profileImageId = await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-profile-image-id")
        };
        await page.Locator("#gmaRange").EvaluateAsync("e => { e.value = '54'; e.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('gmaStatus')?.textContent.includes('Actual age: 41')");
        Assert.True(await page.EvaluateAsync<bool>("identity => window.LwcGuessState.get(identity)?.value === 54", identity),
            await page.EvaluateAsync<string>("identity => JSON.stringify({ identity, saved: window.LwcGuessState.getAll() })", identity));
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content').classList.contains('gma-result-ready')");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await page.EvaluateAsync("identity => window.openAthleteModalBySlug(identity.slug)", identity);
        await AssertHydratedAsync(page);
        await Assertions.Expect(page.Locator("#guessAgeContainer")).ToBeHiddenAsync();
        Assert.Contains("54", await page.Locator("#yourGuess").InnerTextAsync());
        Assert.Null(await page.EvaluateAsync<int?>("identity => window.LwcGuessState.get({ ...identity, profileImageId: 'f'.repeat(64) })?.value ?? null", identity));
    }

    [Theory]
    [InlineData("missing", true, 1280)]
    [InlineData("rejected", true, 390)]
    [InlineData("throwing", true, 390)]
    [InlineData("missing", false, 390)]
    [InlineData("rejected", false, 1280)]
    [InlineData("throwing", false, 390)]
    [InlineData("missing", false, 320)]
    [InlineData("rejected", false, 844)]
    [InlineData("success", false, 1280)]
    public async Task DonationCopy_ConfirmsOnlySuccessfulCopiesAndOffersManualRecovery(string clipboard, bool legacySucceeds, int width)
    {
        await using var context = await NewContextAsync(width);
        await context.RouteAsync("**/api/bitcoin/donation-address", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = "{\"address\":\"bc1qfictionaladdressforbrowserfallbacktest000\"}"
        }));
        await context.AddInitScriptAsync($$"""
            window.copyAttempts = [];
            Object.defineProperty(navigator, 'clipboard', { configurable: true, value:
                '{{clipboard}}' === 'missing' ? undefined : { writeText(text) {
                    window.copyAttempts.push(text);
                    if ('{{clipboard}}' === 'throwing') throw new DOMException('Blocked', 'SecurityError');
                    return '{{clipboard}}' === 'success' ? Promise.resolve() : Promise.reject(new DOMException('Denied', 'NotAllowedError'));
                } }
            });
            document.execCommand = command => {
                if (command !== 'copy') throw new Error('Unexpected legacy command');
                window.copyAttempts.push(document.activeElement.value);
                return {{legacySucceeds.ToString().ToLowerInvariant()}};
            };
            """);
        var page = await context.NewPageAsync();
        if (width == 844) await page.SetViewportSizeAsync(844, 390);
        if (width == 320) await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/contribute");
        if (width == 320) await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        var button = page.Locator("#bitcoinCopyButton");
        await Assertions.Expect(button).ToBeEnabledAsync();
        await button.FocusAsync();
        await CaptureAsync(page, $"donation-{clipboard}-{legacySucceeds}-{width}-before");
        await button.PressAsync("Enter");
        if (legacySucceeds || clipboard == "success")
        {
            await Assertions.Expect(page.Locator("#copiedText")).ToHaveTextAsync("Copied");
            await Assertions.Expect(button).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#bitcoinAddressFallback")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#btcAddressLink")).ToBeVisibleAsync(new() { Timeout = 5000 });
        }
        else
        {
            var fallback = page.GetByRole(AriaRole.Textbox, new() { Name = "Bitcoin donation address", Exact = true });
            await Assertions.Expect(fallback).ToBeVisibleAsync();
            await Assertions.Expect(fallback).ToBeFocusedAsync();
            await Assertions.Expect(fallback).ToHaveValueAsync("bc1qfictionaladdressforbrowserfallbacktest000");
            Assert.True(await fallback.EvaluateAsync<bool>("e => e.readOnly && e.selectionStart === 0 && e.selectionEnd === e.value.length"));
            await Assertions.Expect(page.Locator("#copiedText")).ToBeHiddenAsync();
            Assert.Contains("Copy", await page.Locator("#bitcoinCopyStatus").InnerTextAsync());
            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
            await CaptureAsync(page, $"donation-{clipboard}-{legacySucceeds}-{width}-after");
            await fallback.PressAsync("Escape");
            await Assertions.Expect(button).ToBeFocusedAsync();
            await Assertions.Expect(fallback).ToBeHiddenAsync();
            // Recovery can retry without a stale failure or success message.
            await page.EvaluateAsync("() => Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.resolve() } })");
            await button.PressAsync("Enter");
            await Assertions.Expect(page.Locator("#copiedText")).ToHaveTextAsync("Copied");
        }
        Assert.All(await page.EvaluateAsync<string[]>("window.copyAttempts"), value => Assert.Equal("bc1qfictionaladdressforbrowserfallbacktest000", value));
        Assert.Empty(errors);
    }

    private async Task<IBrowserContext> NewContextAsync(int width, string failure = "")
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US", ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new() { Width = width, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        if (failure.Length > 0)
        {
            var store = failure.Split('-')[0];
            await context.AddInitScriptAsync(failure.EndsWith("blocked")
                ? $$"""Object.defineProperty(window, '{{store}}', { get() { throw new DOMException('Blocked', 'SecurityError'); } });"""
                : $$"""
                    const blockedStore = window.{{store}};
                    const originalSetItem = Storage.prototype.setItem;
                    Storage.prototype.setItem = function (...args) {
                        if (this === blockedStore) throw new DOMException('Full', 'QuotaExceededError');
                        return originalSetItem.apply(this, args);
                    };
                    """);
        }
        // Never send a real guess or any other write from this browser coverage.
        await context.RouteAsync("**/api/**", route => route.Request.Method is "GET" or "HEAD"
            ? route.FallbackAsync() : route.AbortAsync());
        return context;
    }

    private static async Task AssertHydratedAsync(IPage page)
    {
        await page.WaitForFunctionAsync("""
            () => document.querySelector('#detailsModal .modal-content')?.dataset.profileImageId
                && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')
            """);
        await Assertions.Expect(page.Locator("#athleteLoadError")).ToBeHiddenAsync();
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>("e => e.classList.contains('has-load-error') || e.hasAttribute('data-server-rendered-profile')"));
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        await page.EvaluateAsync("() => document.fonts.ready");
        var directory = Path.Combine(FindRepositoryRoot(), ".artifacts", "browser-fallbacks");
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png") });
    }
}
