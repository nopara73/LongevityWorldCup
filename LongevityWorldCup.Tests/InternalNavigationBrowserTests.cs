using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class InternalNavigationBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task InitialHtml_ContainsPublicNavigationAndValidRulesetAnchor()
    {
        using var client = App.CreateClient();
        var home = await client.GetStringAsync("/");
        Assert.Contains("<a id=\"viewAllAthletesBtn\" href=\"/leaderboard\"", home);
        foreach (var view in new[] { "bortz", "pheno", "improvement", "bortz-improvement", "crowd" })
        {
            Assert.Contains($"class=\"filter-league-link\" href=\"/league/{view}\"", home);
            using var response = await client.GetAsync($"/league/{view}");
            Assert.True(response.IsSuccessStatusCode);
        }
        Assert.Contains("href=\"/ruleset#point-system-ranking\"", home);
        Assert.Contains("id=\"point-system-ranking\"", await client.GetStringAsync("/ruleset"));
        var leaderboard = await client.GetStringAsync("/leaderboard");
        Assert.Contains("class=\"athlete-name\" href=\"/athlete/", leaderboard);
        Assert.Contains("class=\"portrait-wrapper athlete-profile-link\" href=\"/athlete/", leaderboard);
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task HomepageLink_PreservesStateForNewTabsKeyboardAndBack(int width)
    {
        await using var context = await CreateContextAsync(width);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/?source=internal-links&view=pheno&search=michael#rank-1");
        await WaitForLeaderboardAsync(page);
        var originalUrl = page.Url;
        var link = page.Locator("a#viewAllAthletesBtn");
        var href = (await link.GetAttributeAsync("href"))!;
        Assert.StartsWith("/league/pheno?", href);
        Assert.Contains("source=internal-links", href);
        Assert.Contains("search=michael", href);
        Assert.EndsWith("#rank-1", href);

        var newPage = await context.RunAndWaitForPageAsync(() => link.ClickAsync(new() { Button = MouseButton.Middle }));
        await WaitForLeaderboardAsync(newPage);
        Assert.EndsWith(href, newPage.Url);
        Assert.Equal(originalUrl, page.Url);
        await Assertions.Expect(newPage.Locator("#view-pheno")).ToBeCheckedAsync();
        await Assertions.Expect(newPage.Locator("#athleteSearch")).ToHaveValueAsync("michael");
        await newPage.CloseAsync();

        await link.FocusAsync();
        await link.PressAsync("Enter");
        await WaitForLeaderboardAsync(page);
        await Assertions.Expect(page.Locator("[data-leaderboard-page='full']")).ToBeVisibleAsync();
        await page.GoBackAsync();
        await WaitForLeaderboardAsync(page);
        Assert.Equal(originalUrl, page.Url);
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync("michael");
    }

    [Theory]
    [InlineData(390, ".podium .athlete-name")]
    [InlineData(1280, ".leaderboard tbody .athlete-name")]
    [InlineData(390, ".leaderboard tbody .athlete-profile-link")]
    [InlineData(1280, ".podium .athlete-profile-link")]
    public async Task AthleteLinks_KeepNativeActivationAndEnhancedProfileHistory(int width, string selector)
    {
        await using var context = await CreateContextAsync(width);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/?source=profile-links");
        await WaitForLeaderboardAsync(page);
        var originalUrl = page.Url;
        var link = page.Locator(selector + ":visible").First;
        var href = (await link.GetAttributeAsync("href"))!;
        Assert.StartsWith("/athlete/", href);

        // Observe cancellation at the end of propagation while suppressing actual
        // navigation, so every modifier can be checked on every supported platform.
        Assert.True(await link.EvaluateAsync<bool>("""
            link => [ {ctrlKey:true}, {metaKey:true}, {shiftKey:true}, {altKey:true}, {button:1} ].every(init => {
                let native = false;
                window.addEventListener('click', e => { native = !e.defaultPrevented; e.preventDefault(); }, {once:true});
                link.dispatchEvent(new MouseEvent('click', {bubbles:true, cancelable:true, button:0, ...init}));
                return native;
            })
            """));
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        var newPage = await context.RunAndWaitForPageAsync(() => link.ClickAsync(new() { Modifiers = [KeyboardModifier.ControlOrMeta] }));
        await Assertions.Expect(newPage.Locator("#detailsModal")).ToBeVisibleAsync();
        Assert.Equal(href, new Uri(newPage.Url).AbsolutePath);
        Assert.Equal(originalUrl, page.Url);
        await newPage.CloseAsync();

        await link.FocusAsync();
        await link.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        await page.GoBackAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal(originalUrl, page.Url);
        await page.GoForwardAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal(originalUrl, page.Url);
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task LeagueLinks_PreserveOtherSelectionsAndConnectToCalculatorsAndRules(int width)
    {
        await using var context = await CreateContextAsync(width);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/?source=league-links&view=pheno&filters=women%27s&search=an");
        await WaitForLeaderboardAsync(page);
        await page.Locator(".sidebar-toggle").ClickAsync();
        var clockLink = page.Locator("li:has([data-aging-clock-view='bortz']) > a");
        var href = (await clockLink.GetAttributeAsync("href"))!;
        Assert.Contains("view=bortz", href);
        Assert.Contains("source=league-links", href);
        Assert.Contains("search=an", href);
        Assert.Contains("filters=women", href);
        await clockLink.ClickAsync();
        await WaitForLeaderboardAsync(page);
        await Assertions.Expect(page.Locator("#view-bortz")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("input[name='division']:checked")).ToHaveValueAsync("Women's");
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync("an");
        await Assertions.Expect(page.Locator("#rankingExplanation a")).ToHaveAttributeAsync("href", "/bortz-age");
        await page.Locator("#rankingExplanation a").ClickAsync();
        await page.WaitForURLAsync("**/bortz-age");
        await page.GoBackAsync();
        await WaitForLeaderboardAsync(page);
        if (width < 480)
        {
            await page.Locator(".view-badge-ultimate").ClickAsync();
            await page.Locator("#rankingExplanation a").ClickAsync();
        }
        else
        {
            await page.Locator(".leaderboard-metric-link").ClickAsync();
        }
        await Assertions.Expect(page.Locator("#point-system-ranking")).ToBeVisibleAsync();
        Assert.Equal("/ruleset#point-system-ranking", new Uri(page.Url).PathAndQuery + new Uri(page.Url).Fragment);

        await page.GotoAsync("/");
        await WaitForLeaderboardAsync(page);
        await page.Locator(".sidebar-toggle").ClickAsync();
        var flag = page.Locator("li:has(label[data-flag='Hungary']) > a");
        await Assertions.Expect(flag).ToHaveAttributeAsync("href", "/flag/hungary");
        await flag.ClickAsync();
        await WaitForLeaderboardAsync(page);
        await Assertions.Expect(page.Locator("input[name='flag'][value='Hungary']")).ToBeCheckedAsync();
        Assert.Equal("/flag/hungary", new Uri(page.Url).AbsolutePath);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    private async Task<IBrowserContext> CreateContextAsync(int width)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        return context;
    }

    private static Task WaitForLeaderboardAsync(IPage page) =>
        Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
}
