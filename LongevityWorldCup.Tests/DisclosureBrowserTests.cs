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
    [InlineData("/?group=flags")]
    [InlineData("/leaderboard?group=flags")]
    public async Task OpeningFlight_ShowsLoadedPortraitsWhileRosterImagesArePending(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var team = page.Locator(".flag-team").First;
        await team.Locator("summary").ScrollIntoViewIfNeededAsync();
        await team.EvaluateAsync("team => Promise.all([...team.querySelectorAll('.flag-team-portraits img')].map(i => i.decode()))");
        var releaseImages = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/disclosure-cold.png", async route =>
        {
            await releaseImages.Task;
            await route.ContinueAsync();
        });
        try
        {
            await team.EvaluateAsync("""
                team => {
                    team.querySelectorAll('.flag-athlete-link > img').forEach(image => {
                        // A fresh element has no previous decoded bitmap to reuse.
                        const cold = document.createElement('img');
                        cold.alt = '';
                        cold.loading = 'lazy';
                        cold.src = '/disclosure-cold.png';
                        cold.srcset = '/disclosure-cold.png 96w';
                        cold.sizes = '60px';
                        cold.dataset.lwcImageRole = 'athlete';
                        cold.dataset.lwcPortrait = '';
                        cold.dataset.lwcPortraitState = 'loading';
                        image.replaceWith(cold);
                    });
                }
                """);
            foreach (var open in new[] { true, false })
            {
                await team.EvaluateAsync("""
                    team => {
                        team.querySelector('summary').click();
                        document.getAnimations().filter(animation => {
                            const target = animation.effect?.target;
                            return target instanceof Element && (team.contains(target) || target.matches('.lwc-disclosure-ghost'));
                        }).forEach(animation => animation.pause());
                    }
                    """);
                await Assertions.Expect(page.Locator(".lwc-disclosure-ghost")).ToHaveCountAsync(3);
                await page.WaitForFunctionAsync("""
                    () => {
                        const images = [...document.querySelectorAll('.lwc-disclosure-ghost')];
                        return images.length === 3 && images.every(image => image.complete && image.naturalWidth > 0
                            && getComputedStyle(image).content === 'normal');
                    }
                    """, null, new() { Timeout = 3000 });
                Assert.Equal(open, await team.EvaluateAsync<bool>("d => d.open"));
            }
        }
        finally
        {
            releaseImages.TrySetResult();
            await context.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.Wait });
        }
    }

    [Theory]
    [InlineData("/leaderboard?group=flags", "preference")]
    [InlineData("/leaderboard?group=flags", "scroll")]
    [InlineData("/leaderboard?group=flags", "resize")]
    [InlineData("/?group=flags", "preference")]
    [InlineData("/?group=flags", "scroll")]
    [InlineData("/?group=flags", "resize")]
    public async Task InterruptedFlight_RestoresPortraitsAndNativeState(string path, string interrupt)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
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
        Assert.Equal("-999px -999px", await team.Locator(".flag-team-portraits img").First.EvaluateAsync<string>("image => getComputedStyle(image).objectPosition"));
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
                window.disclosureAnimationCounts = [];
                // Native details toggle after click dispatch; sample on the next
                // frame so both native and enhanced disclosures have opened.
                document.addEventListener('click', () => requestAnimationFrame(() => {
                    const rows = document.querySelectorAll('.play-join-card--pro .play-join-biomarkers li');
                    for (const row of rows) {
                        // Observe CSS and Web Animations together, excluding the
                        // decorative plus. Every row gets one entrance owner.
                        const animations = row.getAnimations().filter(a => a.effect.target === row && !a.effect.pseudoElement);
                        window.disclosureAnimationCounts.push(animations.length);
                        for (const animation of animations) {
                            const item = { state: 'running' };
                            window.disclosureItems.push(item);
                            animation.finished.then(() => item.state = 'finished', () => item.state = 'cancelled');
                        }
                    }
                }), { once: true });
            }
            """);
        await page.Locator(".play-join-biomarkers summary").Last.ClickAsync();
        await page.WaitForFunctionAsync("() => window.disclosureAnimationCounts.length === 22");
        var counts = await page.EvaluateAsync<int[]>("window.disclosureAnimationCounts");
        Assert.Equal(22, counts.Length);
        Assert.All(counts, count => Assert.Equal(1, count));
        await page.WaitForFunctionAsync("() => window.disclosureItems.length > 0 && window.disclosureItems.every(i => i.state !== 'running')");
        Assert.All(await page.EvaluateAsync<string[]>("window.disclosureItems.map(i => i.state)"), state => Assert.Equal("finished", state));
        await Assertions.Expect(page.Locator(".play-join-biomarkers details").Last).Not.ToHaveAttributeAsync("data-disclosure", "opening");
    }

    [Theory]
    [InlineData("/leaderboard?group=flags", 1440, 100)]
    [InlineData("/leaderboard?group=flags", 390, 100)]
    [InlineData("/leaderboard?group=flags", 600, 100)]
    [InlineData("/leaderboard?group=flags", 390, 200)]
    [InlineData("/?group=flags", 1440, 100)]
    [InlineData("/?group=flags", 390, 100)]
    [InlineData("/?group=flags", 600, 100)]
    [InlineData("/?group=flags", 390, 200)]
    public async Task ContributionBar_FollowsTheRenderedNameColumn(string path, int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
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
