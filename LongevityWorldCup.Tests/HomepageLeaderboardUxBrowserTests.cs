using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class HomepageLeaderboardUxBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
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
    [InlineData(600)]
    public async Task PhonePodium_KeepsAllThreePlacesAndRankingControlsTogether(int width)
    {
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
        Assert.True(second.X + second.Width <= first.X && first.X + first.Width <= third.X);
        Assert.InRange(Math.Abs(first.Y + first.Height - second.Y - second.Height), 0, 1);
        Assert.InRange(Math.Abs(first.Y + first.Height - third.Y - third.Height), 0, 1);
        Assert.True((await page.Locator(".search-wrapper").BoundingBoxAsync())!.Y < 844,
            "Phone search should be available in the opening viewport.");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await CaptureAsync(page, $"homepage-{width}");

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
