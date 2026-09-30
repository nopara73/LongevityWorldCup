using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class HomepageLeaderboardUxBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    public async Task HomepageEntrance_ClosedFiltersDoNotWidenThePage(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.NoPreference
        });
        await context.AddInitScriptAsync("""
            window.uxOpeningLayout = { drawerWidth: 0, overflow: 0, samples: 0, done: false };
            const sample = () => {
                const drawer = document.getElementById('leaderboardFilters');
                const root = document.documentElement;
                if (drawer && getComputedStyle(drawer).position === 'fixed') {
                    const state = window.uxOpeningLayout;
                    state.samples++;
                    state.drawerWidth = Math.max(state.drawerWidth, drawer.getBoundingClientRect().width);
                    state.overflow = Math.max(state.overflow, root.scrollWidth - root.clientWidth);
                }
                if (document.readyState !== 'complete' || root?.classList.contains('homepage-arriving')) requestAnimationFrame(sample);
                else window.uxOpeningLayout.done = true;
            };
            requestAnimationFrame(sample);
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.uxOpeningLayout.done");
        Assert.True(await page.EvaluateAsync<bool>("uxOpeningLayout.samples > 0"));
        Assert.Equal(0, await page.EvaluateAsync<double>("uxOpeningLayout.drawerWidth"));
        Assert.InRange(await page.EvaluateAsync<double>("uxOpeningLayout.overflow"), 0, 1);
    }

    [Theory]
    [InlineData(320, 720, true)]
    [InlineData(390, 844, true)]
    [InlineData(844, 390, true)]
    [InlineData(1280, 844, true)]
    [InlineData(320, 720, false)]
    [InlineData(1280, 844, false)]
    public async Task Filters_PreserveReadingPositionAndTableWidthAndContainFocus(int width, int height, bool reducedMotion)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("() => document.fonts.ready");
        var toggle = page.Locator(".sidebar-toggle");
        await toggle.ScrollIntoViewIfNeededAsync();
        var scroll = await page.EvaluateAsync<double>("scrollY");
        var tableWidth = (await page.Locator(".leaderboard > table").BoundingBoxAsync())!.Width;
        await toggle.ClickAsync();
        var drawer = page.Locator("#leaderboardFilters");
        await Assertions.Expect(drawer).ToHaveAttributeAsync("aria-modal", "true");
        await Assertions.Expect(page.Locator(".sidebar-close")).ToBeFocusedAsync();
        Assert.True(await page.Locator(".leaderboard > table").EvaluateAsync<bool>("element => element.inert"));
        Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("scrollY") - scroll), 0, 1);
        Assert.InRange(Math.Abs((await page.Locator(".leaderboard > table").BoundingBoxAsync())!.Width - tableWidth), 0, 1);

        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(page.Locator("#showLeaderboardResults")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".sidebar-close")).ToBeFocusedAsync();
        await page.Locator("input[name='division'][value=\"Women's\"]").CheckAsync();
        Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("scrollY") - scroll), 0, 1);
        await CaptureAsync(page, $"filters-{width}");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        await Assertions.Expect(drawer).ToBeHiddenAsync();
        Assert.False(await page.Locator(".leaderboard > table").EvaluateAsync<bool>("element => element.inert"));
        Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("scrollY") - scroll), 0, 1);
        Assert.InRange(Math.Abs((await page.Locator(".leaderboard > table").BoundingBoxAsync())!.Width - tableWidth), 0, 1);
        await toggle.HoverAsync();
        await Assertions.Expect(drawer).ToBeHiddenAsync();
        await page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name").First.ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(548)]
    [InlineData(683)]
    public async Task PhoneHomepage_FilterDrawerPreservesTheRefreshedPodium(int width)
    {
        await using var staticContext = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, JavaScriptEnabled = false
        });
        var staticPage = await staticContext.NewPageAsync();
        await staticPage.GotoAsync("/");
        await staticPage.EvaluateAsync("() => document.fonts.ready");
        await Assertions.Expect(staticPage.Locator(".podium-score")).ToHaveCountAsync(3);
        var initialName = (await staticPage.Locator(".podium-item.first .athlete-name").BoundingBoxAsync())!;
        var initialScore = (await staticPage.Locator(".podium-item.first .podium-score").BoundingBoxAsync())!;
        Assert.InRange(Math.Abs(initialName.X - initialScore.X), 0, 1);
        await CaptureAsync(staticPage, $"homepage-{width}-initial");

        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("() => document.fonts.ready");
        var first = (await page.Locator(".podium-item.first").BoundingBoxAsync())!;
        var second = (await page.Locator(".podium-item.second").BoundingBoxAsync())!;
        var third = (await page.Locator(".podium-item.third").BoundingBoxAsync())!;
        Assert.True(first.Y + first.Height <= Math.Min(second.Y, third.Y));
        Assert.True(second.X + second.Width <= third.X);
        var nameLeft = (await page.Locator(".podium-item.first .athlete-name").BoundingBoxAsync())!.X;
        var scoreLeft = (await page.Locator(".podium-item.first .podium-score").BoundingBoxAsync())!.X;
        var links = page.Locator(".podium-item.first .podium-link-row");
        await Assertions.Expect(links).ToBeVisibleAsync();
        Assert.InRange(Math.Abs(nameLeft - scoreLeft), 0, 1);
        Assert.InRange(Math.Abs(nameLeft - (await links.BoundingBoxAsync())!.X), 0, 1);
        var podiumBottom = Math.Max(second.Y + second.Height, third.Y + third.Height);
        Assert.InRange((await page.Locator(".leaderboard-toolbar").BoundingBoxAsync())!.Y - podiumBottom, 0, 40);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await CaptureAsync(page, $"homepage-{width}");

        var toggle = page.Locator(".sidebar-toggle");
        await toggle.ScrollIntoViewIfNeededAsync();
        var beforeOpening = (await page.Locator(".podium-item.first").BoundingBoxAsync())!;
        await toggle.ClickAsync();
        await Assertions.Expect(page.Locator(".sidebar-close")).ToBeFocusedAsync();
        var openFirst = (await page.Locator(".podium-item.first").BoundingBoxAsync())!;
        Assert.InRange(Math.Abs(openFirst.X - beforeOpening.X), 0, 1);
        Assert.InRange(Math.Abs(openFirst.Y - beforeOpening.Y), 0, 1);
        Assert.InRange(Math.Abs(openFirst.Width - beforeOpening.Width), 0, 1);
        await page.Locator("#showLeaderboardResults").ClickAsync();
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#leaderboardFilters")).ToBeHiddenAsync();

        foreach (var card in await page.Locator(".podium-item").AllAsync())
        {
            await card.Locator(".athlete-name").EvaluateAsync("element => element.textContent='Alexandria-Cassandra von Hohenlohe Longevity Research'");
        }
        foreach (var card in await page.Locator(".podium-item").AllAsync())
        {
            var name = (await card.Locator(".athlete-name").BoundingBoxAsync())!;
            var metric = (await card.Locator("div:has(> .age-reduction)").BoundingBoxAsync())!;
            var prize = (await card.Locator(".podium-item-lower").BoundingBoxAsync())!;
            Assert.True(name.Y + name.Height <= metric.Y + 1);
            Assert.True(metric.Y + metric.Height <= prize.Y + 1);
        }
    }

    [Fact]
    public async Task DirectRankAnchor_RemainsInViewAfterLeaderboardHydration()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.NoPreference });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard#rank-20");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        // Allow the former delayed title-scroll timer to run.
        await page.WaitForTimeoutAsync(700);
        var rank = (await page.Locator("#rank-20").BoundingBoxAsync())!;
        Assert.InRange(rank.Y, -1, 200);
        Assert.EndsWith("#rank-20", page.Url);
        await CaptureAsync(page, "rank-anchor");
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        if (Environment.GetEnvironmentVariable("LWC_UX_SCREENSHOTS") is not { Length: > 0 } destination) return;
        Directory.CreateDirectory(destination);
        await page.ScreenshotAsync(new() { Path = Path.Combine(destination, name + ".png") });
    }
}
