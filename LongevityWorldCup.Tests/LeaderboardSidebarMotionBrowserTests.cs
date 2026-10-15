using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LeaderboardSidebarMotionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/leaderboard", false)]
    [InlineData("/leaderboard?group=flags", true)]
    public async Task Expansion_RevealsFiltersWithoutResizingTheirContents(string path, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 844 },
            ReducedMotion = ReducedMotion.NoPreference,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var rail = page.Locator(".sidebar");
        var collapsedWidth = (await rail.BoundingBoxAsync())!.Width;
        await page.Locator(".sidebar-toggle").HoverAsync();
        await rail.EvaluateAsync("e => Promise.all(e.getAnimations().map(animation => animation.finished))");
        Assert.InRange(Math.Abs((await rail.BoundingBoxAsync())!.Width - collapsedWidth), 0, 1);

        // Start and pause in one browser operation so even a busy runner inspects
        // the actual intermediate layout instead of missing a short transition.
        var widths = await page.EvaluateAsync<double[]>("""
            () => {
                const rail = document.querySelector('.sidebar');
                document.querySelector('.sidebar-toggle').click();
                rail.getBoundingClientRect();
                const transition = rail.getAnimations().find(a => a.transitionProperty === 'width');
                if (!transition) throw new Error('Expected a sidebar width transition');
                transition.pause();
                const duration = transition.effect.getTiming().duration;
                const measure = () => [rail, rail.querySelector('.sidebar-heading'), rail.querySelector('.sidebar-filter-list'), rail.querySelector('.filter-section label')]
                    .map(element => element.getBoundingClientRect().width);
                transition.currentTime = duration / 3;
                const during = measure();
                transition.finish();
                return [...during, ...measure()];
            }
            """);
        Assert.True(widths[0] > collapsedWidth + 1 && widths[0] < widths[4] - 1,
            $"Expected an intermediate rail width, got {widths[0]} between {collapsedWidth} and {widths[4]}.");
        for (var index = 1; index <= 3; index++)
            Assert.InRange(Math.Abs(widths[index] - widths[index + 4]), 0, 1);
        Assert.InRange(Math.Abs(widths[1] - widths[4]), 0, 2);
        Assert.True(widths[3] > 200, "The filter labels must retain their full readable width during expansion.");
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    [Fact]
    public async Task Close_UnderThePointerStaysClosedUntilThePointerLeaves()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 844 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var rail = page.Locator(".sidebar");
        var collapsedWidth = (await rail.BoundingBoxAsync())!.Width;
        await rail.HoverAsync(new() { Position = new() { X = 20, Y = 20 } });
        await Assertions.Expect(page.Locator(".sidebar-filter-list")).ToBeVisibleAsync();
        await rail.EvaluateAsync("e => Promise.all(e.getAnimations().map(animation => animation.finished))");

        // Close while the pointer remains over the part of the rail that survives
        // collapse; clicking the distant Close button would miss this regression.
        await page.Locator(".sidebar-close").EvaluateAsync("button => button.click()");
        await rail.EvaluateAsync("e => Promise.all(e.getAnimations().map(animation => animation.finished))");
        await Assertions.Expect(page.Locator(".filter-section").First).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".collapsed-title")).ToBeVisibleAsync();
        Assert.InRange(Math.Abs((await rail.BoundingBoxAsync())!.Width - collapsedWidth), 0, 1);
        await page.Mouse.MoveAsync(0, 0);
        await rail.HoverAsync(new() { Position = new() { X = 20, Y = 20 } });
        await Assertions.Expect(page.Locator(".filter-section").First).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(1280, 844)]
    [InlineData(800, 844)]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    public async Task ReducedMotion_OpensImmediatelyAndKeepsFiltersUsable(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await Assertions.Expect(page.Locator(".filter-section").First).ToBeVisibleAsync();
        Assert.True(await page.Locator(".sidebar").EvaluateAsync<bool>(
            "e => e.getAnimations().every(a => a.effect.getTiming().duration <= 1)"));
        var pheno = page.Locator(".sidebar input[name=agingClockView][value=pheno]");
        await pheno.CheckAsync();
        await Assertions.Expect(page.Locator("#view-pheno")).ToBeCheckedAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }
}
