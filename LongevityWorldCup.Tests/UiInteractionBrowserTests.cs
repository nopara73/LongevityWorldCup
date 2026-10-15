using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class UiInteractionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IBrowserContext> NewContextAsync(int width = 390, int height = 844, bool reduced = true)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true')");
        return context;
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Profile_OwnsFocusDuringLoadingHydrationAndNestedNavigation(int width, int height)
    {
        await using var context = await NewContextAsync(width, height);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/athlete/michael-lustgarten", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Assertions.Expect(page.Locator("#closeAthleteDetailsModal")).ToBeFocusedAsync();
            Assert.True(await page.Locator("header").EvaluateAsync<bool>("e => e.inert"));
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await page.EvaluateAsync<bool>("!!document.activeElement.closest('#detailsModal')"));
            release.TrySetResult();
            await Assertions.Expect(page.Locator("#detailsModal .modal-content")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-loading"));
            Assert.True(await page.EvaluateAsync<bool>("!!document.activeElement.closest('#detailsModal')"));

            var frame = page.FrameLocator("#events-frame");
            var frameLinks = frame.Locator("a[href]:visible");
            await Assertions.Expect(frameLinks.First).ToBeVisibleAsync();
            await page.Locator("#closeAthleteDetailsModal").FocusAsync();
            await page.Keyboard.PressAsync("Shift+Tab");
            Assert.True(await page.EvaluateAsync<bool>("!!document.activeElement.closest('#detailsModal')"));
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await page.EvaluateAsync<bool>("document.activeElement.matches('#closeAthleteDetailsModal, #stickyCloseBtn')"),
                await FocusDiagnosticsAsync(page));

            await frameLinks.Last.FocusAsync();
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await page.EvaluateAsync<bool>("document.activeElement.matches('#closeAthleteDetailsModal, #stickyCloseBtn')"),
                await FocusDiagnosticsAsync(page));
            await page.Locator("#closeAthleteDetailsModal").FocusAsync();
            await CaptureAsync(page, $"profile-{width}");
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
            Assert.False(await page.Locator("header").EvaluateAsync<bool>("e => e.inert"));
            Assert.False(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
            Assert.True(await page.EvaluateAsync<bool>("document.activeElement.matches('a.header-link')"), await FocusDiagnosticsAsync(page));
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("/", 390, 100, true)]
    [InlineData("/events", 320, 200, false)]
    [InlineData("/event-board-embed.html?athlete=michael-lustgarten&embed=1", 1440, 100, true)]
    public async Task EventDisclosure_HidesClosedLinksAndFitsLongContent(string path, int width, int textPercent, bool reduced)
    {
        await using var context = await NewContextAsync(width, 900, reduced);
        await context.RouteAsync("**/api/events**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new[] { new {
                Id = "interaction-event", Type = 6,
                Text = "Announcement slug[michael-lustgarten]\n\n" + string.Join("\n\n", Enumerable.Repeat("Long details keep every line readable at enlarged text sizes.", 45)) + "\n\n[Read more](https://example.test/details)",
                OccurredAt = DateTimeOffset.UtcNow, Relevance = 10
            } })
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var toggle = page.Locator(".custom-event-expander").First;
        await toggle.WaitForAsync();
        await page.EvaluateAsync("p => document.documentElement.style.fontSize = p + '%'", textPercent);
        if (path == "/events")
        {
            await page.EvaluateAsync("() => document.fonts.ready");
            Assert.Equal(1, await page.Locator("#eventBoardTitle").EvaluateAsync<int>("""
                heading => {
                    const range = document.createRange(); range.selectNodeContents(heading);
                    return new Set([...range.getClientRects()].map(rect => Math.round(rect.top))).size;
                }
                """));
        }
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.False(await page.EvaluateAsync<bool>("!!document.activeElement.closest('.custom-event-content')"));
        var content = page.Locator(".custom-event-content").First;
        Assert.True(await content.EvaluateAsync<bool>("e => e.inert && e.getAttribute('aria-hidden') === 'true'"));
        if (!reduced)
        {
            Assert.True(await toggle.EvaluateAsync<bool>("""
                button => {
                    button.click();
                    const panel = document.querySelector('.custom-event-content');
                    for (const animation of panel.getAnimations()) {
                        animation.pause();
                        animation.currentTime = animation.effect.getComputedTiming().endTime / 2;
                    }
                    return panel.clientHeight > 0 && panel.clientHeight < panel.firstElementChild.scrollHeight
                        && !panel.inert && panel.getAttribute('aria-hidden') === 'false';
                }
                """));
            await CaptureAsync(page, "disclosure-intermediate");
            await content.EvaluateAsync("e => e.getAnimations().forEach(animation => animation.play())");
        }
        else await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await page.WaitForFunctionAsync("() => { const e = document.querySelector('.custom-event-content'); return e.clientHeight > 1400 && e.clientHeight >= e.firstElementChild.scrollHeight - 1; }");
        Assert.True(await content.EvaluateAsync<bool>("e => e.scrollHeight > 1400"));
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.EvaluateAsync<bool>("!!document.activeElement.closest('.custom-event-content')"));
        await CaptureAsync(page, $"disclosure-{width}-{textPercent}");
        await toggle.ClickAsync();
        await Assertions.Expect(content).ToHaveAttributeAsync("aria-hidden", "true");
        // Interrupt both directions before their animations settle.
        await page.EvaluateAsync("() => { const b = document.querySelector('.custom-event-expander'); b.click(); b.click(); b.click(); }");
        await page.WaitForFunctionAsync("() => { const e = document.querySelector('.custom-event-content'); return e.clientHeight > 1400 && e.clientHeight >= e.firstElementChild.scrollHeight - 1 && !e.inert; }");
        await toggle.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.custom-event-content').clientHeight === 0");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RankAndEventDeepLinks_RespectMotionPreference(bool reduced)
    {
        await using var context = await NewContextAsync(390, 844, reduced);
        await context.AddInitScriptAsync("""
            window.uiScrollRequests = [];
            const original = Element.prototype.scrollIntoView;
            Element.prototype.scrollIntoView = function(options) {
                if (this.id === 'rank-4' || this.dataset.eventId?.startsWith('motion-'))
                    window.uiScrollRequests.push(options.behavior);
                return original.call(this, options);
            };
            """);
        await context.RouteAsync("**/api/events**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new[] { new {
                Id = "motion-event", Type = 6, Text = "Motion test\n\n[Read more](https://example.test/story)",
                OccurredAt = DateTimeOffset.UtcNow, Relevance = 10
            }, new {
                Id = "motion-note", Type = 6, Text = "A short announcement without extra details.",
                OccurredAt = DateTimeOffset.UtcNow, Relevance = 10
            } })
        }));
        var page = await context.NewPageAsync();
        foreach (var path in new[] { "/leaderboard#rank-4", "/events?event=motion-event", "/events?event=motion-note" })
        {
            await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.WaitForFunctionAsync("() => window.uiScrollRequests.length > 0");
            Assert.Equal(reduced ? "instant" : "smooth", await page.EvaluateAsync<string>("window.uiScrollRequests[0]"));
            if (path.EndsWith("event=motion-event", StringComparison.Ordinal))
            {
                await Assertions.Expect(page.Locator(".custom-event-expander")).ToHaveAttributeAsync("aria-expanded", "true");
                Assert.False(await page.Locator(".custom-event-content").EvaluateAsync<bool>("e => e.inert"));
            }
        }
    }

    [Fact]
    public async Task StickyHeader_RecalculatesAfterResizeAndTextChangesWithoutScrolling()
    {
        await using var context = await NewContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/about");
        await page.EvaluateAsync("scrollTo(0, 45)");
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForFunctionAsync("""
            () => {
                const header = document.querySelector('header'), sticky = document.querySelector('#site-sticky-header');
                return sticky.classList.contains('visible') === (scrollY >= Math.max(0, header.offsetHeight - (sticky.offsetHeight || 52)));
            }
            """);
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.WaitForFunctionAsync("""
            () => {
                const header = document.querySelector('header'), sticky = document.querySelector('#site-sticky-header');
                return sticky.classList.contains('visible') === (scrollY >= Math.max(0, header.offsetHeight - (sticky.offsetHeight || 52)));
            }
            """);
        await page.SetViewportSizeAsync(844, 390);
        await CaptureAsync(page, "header-landscape-enlarged");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(150)]
    public async Task ProfileAndViewer_ReopenDuringClosingKeepTheirNewestState(int reversalMs)
    {
        await using var context = await NewContextAsync(844, 390, false);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/michael-lustgarten");
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.Clock.InstallAsync();
        await page.EvaluateAsync("openEnlargedView(document.querySelector('#modalProfilePic'), { pushHistory: false })");
        await page.Clock.RunForAsync(32);
        await page.EvaluateAsync("closeEnlargedView(document.querySelector('#athleteImageViewer'))");
        Assert.True(await page.Locator("#athleteImageViewer").EvaluateAsync<bool>("e => e.inert"));
        await page.Clock.RunForAsync(reversalMs);
        await page.EvaluateAsync("openEnlargedView(document.querySelector('#modalProfilePic'), { pushHistory: false })");
        await page.Clock.RunForAsync(300);
        await Assertions.Expect(page.Locator("#athleteImageViewer")).ToHaveAttributeAsync("aria-hidden", "false");
        Assert.False(await page.Locator("#athleteImageViewer").EvaluateAsync<bool>("e => e.inert"));
        Assert.True(await page.Locator(".modal-content").EvaluateAsync<bool>("e => e.inert"));
        await page.EvaluateAsync("closeEnlargedView(document.querySelector('#athleteImageViewer'))");
        await page.Clock.RunForAsync(300);
        await Assertions.Expect(page.Locator("#modalProfilePic")).ToBeFocusedAsync();
        await page.EvaluateAsync("closeModal({ fromHistory: true })");
        await page.Clock.RunForAsync(reversalMs);
        await page.EvaluateAsync("window.openAthleteModalBySlug('michael-lustgarten', { suppressGuessMyAge: true })");
        await page.Clock.RunForAsync(300);
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        Assert.True(await page.Locator("header").EvaluateAsync<bool>("e => e.inert"));
        await page.Clock.ResumeAsync();
    }

    [Fact]
    public async Task Profile_OverlappingCloseRequestsKeepTheOriginalOpener()
    {
        await using var context = await NewContextAsync(1280, 900, false);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?search=michael");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var opener = page.Locator(".leaderboard tbody .athlete-name").Filter(new() { HasText = "Michael Lustgarten" }).First;
        await opener.PressAsync("Enter");
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal').style.display === 'block' && !document.querySelector('.modal-content').classList.contains('is-loading')");
        await page.EvaluateAsync("() => { closeModal({ fromHistory: true }); closeModal({ fromHistory: true }); }");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await Assertions.Expect(opener).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("/about")]
    [InlineData("/history")]
    [InlineData("/ruleset")]
    [InlineData("/media")]
    [InlineData("/rejuvenation-olympics")]
    [InlineData("/privacy")]
    [InlineData("/unsubscribe")]
    [InlineData("/error/404.html")]
    [InlineData("/error/502.html")]
    [InlineData("/error/503.html")]
    [InlineData("/error/504.html")]
    public async Task PublicDocumentsAndRecovery_ReflowWithEnlargedText(string path)
    {
        await using var context = await NewContextAsync(320, 740);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.EvaluateAsync("() => document.fonts.ready");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"), path);
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await page.EvaluateAsync<bool>("document.activeElement !== document.body && document.activeElement.getClientRects().length > 0"), path);
        await CaptureAsync(page, "reflow-" + path.Trim('/').Replace('/', '-'));
    }

    [Fact]
    public async Task LoadingToAlert_RestoresFocusAndReleasesScrollOnlyAfterDismissal()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/pheno-age");
        await page.Locator("#dob-year").FocusAsync();
        await page.EvaluateAsync("showLoading()");
        await page.EvaluateAsync("() => { hideLoading(); void customAlert('Verification error'); }");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        Assert.True(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#dob-year")).ToBeFocusedAsync();
        Assert.False(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('no-scroll')"));
    }

    private static Task<string> FocusDiagnosticsAsync(IPage page) => page.EvaluateAsync<string>("""
        () => JSON.stringify({ active:document.activeElement.outerHTML.slice(0,200),
            frame:document.querySelector('#events-frame')?.contentDocument?.activeElement?.outerHTML.slice(0,200),
            controls:[...document.querySelectorAll('#detailsModal a[href], #detailsModal button')]
                .filter(e => e.getClientRects().length && !e.closest('[hidden], [inert]'))
                .map(e => e.id || e.tagName + ':' + e.textContent.slice(0,25)) })
        """);

    private static async Task CaptureAsync(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("LWC_UI_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png") });
    }
}
