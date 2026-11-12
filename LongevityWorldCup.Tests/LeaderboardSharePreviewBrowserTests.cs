using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class LeaderboardSharePreviewBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task HydratedRowsAndActualDivisionLinksMatchTheShareCard(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true'); Object.defineProperty(navigator, 'share', {value: undefined, configurable: true});");
        var page = await context.NewPageAsync();
        var images = App.Services.GetRequiredService<LeagueOgImageService>();
        foreach (var route in new[] { "/leaderboard", "/league/pheno", "/league/bortz", "/league/improvement", "/league/bortz-improvement", "/league/gen-alpha",
            "/league/crowd", "/flag/hungary?filters=Women%27s", "/flag/hungary?filters=Hungary,Women%27s&view=pheno",
            "/league/bortz?filters=Men%27s", "/leaderboard?group=flags", "/leaderboard?group=flags&view=pheno&filters=Women%27s" })
        {
            await page.GotoAsync(route);
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            var uri = new Uri(page.Url);
            Assert.True(images.TryGetLeaderboardPayload(uri.AbsolutePath, new QueryString(uri.Query).ToQuery(), out var payload));
            await AssertRowsAsync(page, payload);
            Assert.Equal(await page.TitleAsync(), payload.PreviewTitle + " | Longevity World Cup");
            Assert.Equal(await page.TitleAsync(), await page.Locator("meta[property='og:title']").GetAttributeAsync("content"));
        }

        foreach (var division in new[] { "Women's", "Men's" })
        {
            await page.GotoAsync("/flag/hungary");
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            await page.Locator(".sidebar-toggle").ClickAsync();
            await page.Locator($"input[name=division][value=\"{division}\"]").CheckAsync();
            await Assertions.Expect(page.Locator("#leaderboardLeagueTitle")).ToHaveTextAsync("Hungary " + division + " League");
            var uri = new Uri(page.Url);
            Assert.Equal("/leaderboard", uri.AbsolutePath);
            var filters = new QueryString(uri.Query).ToQuery()["filters"].ToString();
            Assert.Contains("hungary", filters.ToLowerInvariant());
            Assert.Contains(division.ToLowerInvariant(), filters.ToLowerInvariant());
            Assert.True(images.TryGetLeaderboardPayload(uri.AbsolutePath, new QueryString(uri.Query).ToQuery(), out var payload));
            await AssertRowsAsync(page, payload);
            await page.Keyboard.PressAsync("Escape");
            await page.Locator("#leaderboardTitle [data-lwc-share]").ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('.lwc-share__preview img')?.naturalWidth === 1200");
            var src = await page.Locator(".lwc-share__preview img").GetAttributeAsync("src");
            using var client = App.CreateClient();
            Assert.Equal(await File.ReadAllBytesAsync((await images.EnsureRenderedImageAsync(payload))!), await client.GetByteArrayAsync(src));
            await page.Keyboard.PressAsync("Escape");
            await page.ReloadAsync();
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            await AssertRowsAsync(page, payload);
            var ogTitle = await page.Locator("meta[property='og:title']").GetAttributeAsync("content");
            Assert.Equal(payload.PreviewTitle + " | Longevity World Cup", ogTitle);
        }
    }

    private static async Task AssertRowsAsync(IPage page, LeagueOgImageService.LeagueOgPayload payload)
    {
        string[] names;
        if (payload.ByFlag)
            names = await page.Locator(".flag-team:visible").EvaluateAllAsync<string[]>("rows => rows.slice(0,3).map(row => row.dataset.flag)");
        else if (await page.Locator(".podium-item.first:visible").CountAsync() > 0)
            names = await page.EvaluateAsync<string[]>("() => ['first','second','third'].map(rank => document.querySelector('.podium-item.' + rank + ' .athlete-name')?.textContent.trim()).filter(Boolean)");
        else
            names = (await page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name").AllTextContentsAsync()).Take(3).Select(name => name.Trim()).ToArray();
        Assert.Equal(payload.Top3Names, names);
    }
}
