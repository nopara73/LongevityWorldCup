using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LeaderboardStandingMotionBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullBoards_KeepTheirTableFirstAndThePodiumOnTheHomepage(bool script)
    {
        await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = script, ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        foreach (var path in new[] { "/leaderboard", "/league/womens", "/flag/hungary" })
        {
            await page.GotoAsync(path);
            if (script) await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            Assert.Equal(0, await page.Locator(".lb-stage").CountAsync());
            await Assertions.Expect(page.Locator(".podium")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator(".leaderboard tbody tr[data-athlete-name]").First).ToBeVisibleAsync();
        }
        await page.GotoAsync("/");
        if (script) await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".podium")).ToBeVisibleAsync();
        Assert.Equal(3, await page.Locator(".podium-item:not(.podium-skeleton-item)").CountAsync());
    }

    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    public async Task RankRolls_UseComparableHistoryAndStopOnLiveReducedMotion(int width, bool down)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.NoPreference });
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            foreach (var athlete in athletes.OfType<JsonObject>())
            {
                athlete["IsNew"] = false;
                athlete["Placements"] = new JsonArray(999, 999, down ? 1 : 9, 999);
            }
        });
        var page = await context.NewPageAsync();
        // Own the cleanup clock before page code schedules timers. This lets
        // a slow runner inspect the real mounted effect before it expires.
        await page.Clock.InstallAsync(new() { TimeDate = DateTime.UtcNow.AddMinutes(-5) });
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var row = page.Locator(".leaderboard tbody tr[data-athlete-name]:visible").Nth(down ? 1 : 0);
        await row.ScrollIntoViewIfNeededAsync();
        var rank = row.Locator(".rank");
        var current = await rank.InnerTextAsync();
        Assert.Equal(0, await page.Locator(".lb-rolling").CountAsync());
        await page.Clock.PauseAtAsync(DateTime.UtcNow.AddMinutes(5));
        await rank.EvaluateAsync("""
            rank => {
                const observer = new MutationObserver(() => {
                    if (!rank.classList.contains('lb-rolling')) return;
                    for (const animation of rank.getAnimations({subtree:true})) {
                        animation.pause();
                        animation.currentTime = Number(animation.effect.getTiming().duration) / 2;
                    }
                    observer.disconnect();
                });
                observer.observe(rank, {attributes:true, attributeFilter:['class']});
            }
            """);
        await page.Keyboard.PressAsync("Shift");
        await page.Clock.RunForAsync(32);
        await Assertions.Expect(rank).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lb-rolling"));
        Assert.Equal(down ? "1" : "9", await rank.GetAttributeAsync("data-was"));
        Assert.Equal(current, await rank.InnerTextAsync());
        Assert.Equal(down ? "down" : "up", await row.GetAttributeAsync("data-lb-moved"));
        var animations = await rank.EvaluateAsync<string[]>("rank => rank.getAnimations({subtree:true}).map(animation => animation.animationName)");
        Assert.Contains(down ? "lb-roll-out-down" : "lb-roll-out-up", animations);
        Assert.Contains(down ? "lb-roll-in-down" : "lb-roll-in-up", animations);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Assertions.Expect(page.Locator(".lb-rolling")).ToHaveCountAsync(0);
        Assert.Null(await rank.GetAttributeAsync("data-was"));
        Assert.Equal(current, await rank.InnerTextAsync());
        Assert.Empty(await rank.EvaluateAsync<string[]>("rank => rank.getAnimations({subtree:true}).map(animation => animation.animationName).filter(name => name?.startsWith('lb-roll-'))"));
        await page.Clock.ResumeAsync();

        await page.Locator("label[for=view-pheno]").ClickAsync();
        Assert.Equal(0, await page.Locator("tr[data-lb-moved]").CountAsync());
        await page.Locator("label[for=view-ultimate]").ClickAsync();
        await page.Locator("#athleteSearch").FillAsync("Valerie");
        await Assertions.Expect(page.Locator("tr[data-lb-moved]")).ToHaveCountAsync(0);
        await page.Locator("#athleteSearch").FillAsync("");
        await Assertions.Expect(page.Locator("tr[data-lb-moved]").First).ToBeAttachedAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        Assert.Equal(0, await page.Locator(".lb-rolling").CountAsync());
        await page.GotoAsync("/league/womens");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        Assert.Equal(0, await page.Locator("tr[data-lb-moved]").CountAsync());
    }

    [Fact]
    public async Task NewAthletes_DoNotReceiveHistoricalMovement()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            foreach (var athlete in athletes.OfType<JsonObject>())
            {
                athlete["IsNew"] = true;
                athlete["Placements"] = new JsonArray(999, 999, 999, 999);
            }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Keyboard.PressAsync("Shift");
        Assert.Equal(0, await page.Locator("tr[data-lb-moved],.lb-rolling").CountAsync());
    }
}
