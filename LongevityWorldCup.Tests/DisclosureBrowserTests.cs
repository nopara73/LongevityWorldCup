using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DisclosureBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("preference")]
    [InlineData("scroll")]
    [InlineData("resize")]
    public async Task InterruptedFlight_RestoresPortraitsAndNativeState(string interrupt)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var team = page.Locator(".flag-team").First;
        // Hold real effects so runner speed cannot let the flight finish before interruption.
        await team.EvaluateAsync("""
            team => {
                team.querySelector('summary').click();
                document.getAnimations().forEach(a => a.pause());
            }
            """);
        await Assertions.Expect(page.Locator(".lwc-disclosure-ghost")).ToHaveCountAsync(3);
        if (interrupt == "preference")
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        else if (interrupt == "resize")
            await page.SetViewportSizeAsync(1280, 800);
        else
            await page.EvaluateAsync("window.scrollBy(0, 80)");
        await Assertions.Expect(page.Locator(".lwc-disclosure-ghost")).ToHaveCountAsync(0);
        await Assertions.Expect(team).Not.ToHaveAttributeAsync("data-disclosure", "opening");
        Assert.True(await team.EvaluateAsync<bool>("d => d.open"));
        Assert.True(await team.EvaluateAsync<bool>("d => [...d.querySelectorAll('.flag-athlete-link img')].every(i => getComputedStyle(i).opacity === '1')"));
        await team.Locator("summary").FocusAsync();
        await page.Keyboard.PressAsync("Space");
        Assert.False(await team.EvaluateAsync<bool>("d => d.open"));
        await page.Keyboard.PressAsync("Enter");
        Assert.True(await team.EvaluateAsync<bool>("d => d.open"));
    }

    [Fact]
    public async Task LongBiomarkerList_LetsEveryStaggeredItemFinish()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/join");
        await page.EvaluateAsync("""
            () => {
                window.disclosureItems = [];
                const animate = Element.prototype.animate;
                Element.prototype.animate = function(...args) {
                    const animation = animate.apply(this, args);
                    if (this.matches('.play-join-biomarkers li')) {
                        const item = { state: 'running' };
                        window.disclosureItems.push(item);
                        animation.finished.then(() => item.state = 'finished', () => item.state = 'cancelled');
                    }
                    return animation;
                };
            }
            """);
        await page.Locator(".play-join-biomarkers summary").Last.ClickAsync();
        await page.WaitForFunctionAsync("() => window.disclosureItems.length > 0 && window.disclosureItems.every(i => i.state !== 'running')");
        Assert.All(await page.EvaluateAsync<string[]>("window.disclosureItems.map(i => i.state)"), state => Assert.Equal("finished", state));
        await Assertions.Expect(page.Locator(".play-join-biomarkers details").Last).Not.ToHaveAttributeAsync("data-disclosure", "opening");
    }

    [Theory]
    [InlineData(390, 100)]
    [InlineData(600, 100)]
    [InlineData(390, 200)]
    public async Task ContributionBar_FollowsTheRenderedNameColumn(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + '%'", textPercent);
        await page.Locator(".flag-team summary").First.ClickAsync();
        var delta = await page.Locator(".flag-team .flag-athlete-link").First.EvaluateAsync<double>("""
            link => Math.abs(link.querySelector('.flag-athlete-name').getBoundingClientRect().left
                - link.getBoundingClientRect().left - parseFloat(getComputedStyle(link, '::before').left))
            """);
        Assert.InRange(delta, 0, 1);
    }

    [Fact]
    public async Task PlayBiomarkers_KeepOneMarkerAndAllRequiredItems()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/join");
        var disclosures = page.Locator(".play-join-biomarkers details");
        for (var i = 0; i < 2; i++)
        {
            var details = disclosures.Nth(i);
            var summary = details.Locator("summary");
            var markers = await summary.EvaluateAsync<int>("s => ['::before','::after'].filter(p => !['none','normal'].includes(getComputedStyle(s,p).content)).length");
            Assert.Equal(1, markers);
            await summary.ClickAsync();
            await Assertions.Expect(details.Locator("li")).ToHaveCountAsync(i == 0 ? 9 : 22);
            await Assertions.Expect(details.Locator("li").Last).ToBeVisibleAsync();
            Assert.True(await details.EvaluateAsync<bool>("d => d.scrollWidth <= d.clientWidth + 1"));
        }
    }
}
