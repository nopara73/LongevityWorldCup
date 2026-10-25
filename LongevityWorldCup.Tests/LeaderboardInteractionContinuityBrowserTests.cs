using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardInteractionContinuityBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/", 2, false)]
    [InlineData("/", 3, false)]
    [InlineData("/", 3, true)]
    [InlineData("/leaderboard", 0, false)]
    public async Task BrowserBack_RestoresTheReadingPositionAndTheSameHomepageLayout(string path, int visits, bool search)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ReducedMotion = search ? ReducedMotion.NoPreference : ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync($"if (!localStorage.getItem('lwcHomepageVisitCount:v1')) localStorage.setItem('lwcHomepageVisitCount:v1', '{visits}');");
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        if (search)
        {
            await page.Locator("#athleteSearch").FillAsync("michael");
            await page.WaitForFunctionAsync("() => new URL(location.href).searchParams.get('search') === 'michael'");
        }
        var target = path == "/" ? page.Locator("#events-root-index") : page.Locator(".leaderboard tbody tr[data-athlete-name]:visible").Nth(20);
        if (path == "/") await Assertions.Expect(page.Locator("#eventsStatus")).ToHaveTextAsync("Events loaded.");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await target.ScrollIntoViewIfNeededAsync();
        var position = await page.EvaluateAsync<double>("scrollY");
        var layout = await page.Locator("main").GetAttributeAsync("data-homepage-visit-layout");
        await page.GotoAsync("/about");
        await page.GoBackAsync();
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        Assert.Equal(layout, await page.Locator("main").GetAttributeAsync("data-homepage-visit-layout"));
        await page.WaitForFunctionAsync("y => Math.abs(scrollY - y) < 3", position);
        if (search)
        {
            // Incoming shared links have a one-second auto-scroll; it must not
            // replay after the browser has already restored a history entry.
            await page.WaitForTimeoutAsync(1300);
            Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("scrollY") - position), 0, 2);
        }
    }

    [Theory]
    [InlineData("/", 390)]
    [InlineData("/leaderboard", 1440)]
    public async Task Search_UpdatesOnTheNextFrame_AndClearCancelsPendingInput(string path, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var result = await page.EvaluateAsync<bool>("""
            async () => {
                const input = document.getElementById('athleteSearch');
                input.value = 'zzzz-no-athlete';
                input.dispatchEvent(new Event('input', { bubbles: true }));
                await new Promise(requestAnimationFrame);
                return [...document.querySelectorAll('.leaderboard tbody tr[data-athlete-name]')]
                    .every(row => row.style.display === 'none')
                    && document.querySelector('.no-results')?.textContent.includes('No athletes');
            }
            """);
        Assert.True(result, "Search retained the old athletes after the next rendering opportunity.");
        await page.EvaluateAsync("""
            () => {
                const input = document.getElementById('athleteSearch');
                for (const value of ['michael', '']) {
                    input.value = value;
                    input.dispatchEvent(new Event('input', { bubbles: true }));
                }
            }
            """);
        await Assertions.Expect(page.Locator(".no-results")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync("");
        Assert.DoesNotContain("search=", new Uri(page.Url).Query);
    }

    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    public async Task RankingChanges_KeepNamesReadableDuringReordering_AndRespectMountedReducedMotion(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Mouse.ClickAsync(5, 100);
        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                document.querySelector('label[for=view-pheno]').click();
                const rows = [...document.querySelectorAll('.leaderboard tbody tr[data-athlete-name]')]
                    .filter(row => row.style.display !== 'none' && row.getBoundingClientRect().top < innerHeight);
                const problems = [];
                for (const progress of [0, .25, .5, .75]) {
                    rows.flatMap(row => row.getAnimations()).forEach(animation => {
                        animation.pause();
                        animation.currentTime = animation.effect.getTiming().duration * progress;
                    });
                    const names = rows.map(row => row.querySelector('.athlete-name').getBoundingClientRect());
                    if (rows.some(row => Number(getComputedStyle(row).opacity) < .75))
                        problems.push('New results become unreadable before their delayed arrival.');
                    for (let i = 0; i < names.length; i++) for (let j = i + 1; j < names.length; j++) {
                        const a = names[i], b = names[j];
                        if (a.top < b.bottom && a.bottom > b.top && a.left < b.right && a.right > b.left)
                            problems.push('Athlete names cross and overlap during a ranking change.');
                    }
                }
                return problems;
            }
            """);
        Assert.Empty(problems);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Assertions.Expect(page.Locator(".lb-ghost")).ToHaveCountAsync(0);
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('.leaderboard tbody tr')].every(row => !row.getAnimations().length)");
    }

    [Theory]
    [InlineData("/", 390)]
    [InlineData("/leaderboard", 1440)]
    public async Task SearchWithTheSameResultCount_DoesNotRepeatTheLiveAnnouncement(string path, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator("#athleteSearch").FillAsync("zzzz-no-athlete");
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).ToHaveTextAsync("0 athletes");
        await page.EvaluateAsync("""
            () => {
                window.countAnnouncements = [];
                const count = document.getElementById('leaderboardResultCount');
                new MutationObserver(() => countAnnouncements.push(count.textContent))
                    .observe(count, { childList: true, characterData: true, subtree: true });
            }
            """);
        await page.Locator("#athleteSearch").FillAsync("zzzz-no-athlete-again");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        Assert.Empty(await page.EvaluateAsync<string[]>("countAnnouncements"));

        await page.Locator("#athleteSearch").FillAsync("");
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).Not.ToHaveTextAsync("0 athletes");
        var announcements = await page.EvaluateAsync<string[]>("countAnnouncements");
        Assert.Equal(await page.Locator("#leaderboardResultCount").TextContentAsync(), Assert.Single(announcements));
    }

    [Fact]
    public async Task ByFlag_AppliesImmediately_AndTheNextRankingInputIsNotReplayed()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Mouse.ClickAsync(5, 100);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                document.getElementById('group-flags').click();
                return document.getElementById('group-flags').checked
                    && document.querySelector('.leaderboard').classList.contains('leaderboard--flags');
            }
            """), "By Flag deferred its native selection until a document snapshot completed.");
        await page.Locator("label[for=view-pheno]").ClickAsync();
        await page.Locator("label[for=group-flags]").ClickAsync();
        await Assertions.Expect(page.Locator("#group-flags")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#view-pheno")).ToBeCheckedAsync();
        await page.EvaluateAsync("() => Promise.allSettled(document.getElementById('athleteLeaderboard').getAnimations().map(animation => animation.finished))");
        await Assertions.Expect(page.Locator("#group-flags")).Not.ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#view-pheno")).ToBeCheckedAsync();
    }
}
