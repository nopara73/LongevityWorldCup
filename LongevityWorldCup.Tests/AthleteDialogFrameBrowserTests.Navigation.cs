using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class AthleteDialogFrameBrowserTests
{
    [Theory]
    [InlineData(160)]
    [InlineData(195)]
    [InlineData(240)]
    public async Task StickyHeader_NarrowZoomWidthKeepsIdentityAndControlsVisible(int width)
    {
        await using var context = await NewContextAsync(width, 450, dark: true);
        var page = await OpenProfileAsync(context, Profile, "michael-lustgarten");
        await Assertions.Expect(page.Locator("#modalStickyHeader")).ToBeHiddenAsync();
        await page.Locator("#detailsModal .modal-content").EvaluateAsync("content => content.scrollTop = content.scrollHeight / 2");
        await Assertions.Expect(page.Locator("#modalStickyHeader")).ToBeVisibleAsync();
        var issues = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [];
                const header = document.getElementById('modalStickyHeader');
                for (const element of header.querySelectorAll('button, img, #stickyAthleteName')) {
                    if (!element.checkVisibility()) continue;
                    const box = element.getBoundingClientRect();
                    if (box.left < 0 || box.right > innerWidth) failures.push(`${element.id || element.className}: clipped`);
                    if (element.tagName === 'BUTTON' && (box.width < 44 || box.height < 44)) failures.push('small control');
                    if (element.tagName === 'IMG' && Math.abs(box.width - box.height) > 1) failures.push('squashed portrait');
                    if (element.id === 'stickyAthleteName' && box.width < 60) failures.push('name disappeared');
                }
                return failures;
            }
            """);
        Assert.Empty(issues);
    }

    [Fact]
    public async Task Steps_FollowTheExpandedFlagTeamAndStopAtItsBoundary()
    {
        await using var context = await NewContextAsync(1280, 860, dark: true);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/leaderboard?group=flags&search=United%20States");
        var team = page.Locator("#flagLeaderboard .flag-team").First;
        await team.Locator("summary").ClickAsync();
        var links = team.Locator(".flag-athlete-link");
        var paths = await links.EvaluateAllAsync<string[]>("links => links.map(link => new URL(link.href).pathname)");
        Assert.True(paths.Length >= 2);
        await links.First.ClickAsync();
        await WaitForProfileAsync(page);
        var steps = page.Locator(".modal-content > .athlete-steps");
        await Assertions.Expect(steps.Locator("[data-step=previous]")).ToBeDisabledAsync();
        foreach (var path in paths.Skip(1))
        {
            await steps.Locator("[data-step=next]").ClickAsync();
            await page.WaitForURLAsync($"**{path}");
            await WaitForProfileAsync(page);
        }
        await Assertions.Expect(steps.Locator("[data-step=next]")).ToBeDisabledAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await Assertions.Expect(links.First).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/?sort=age&dir=desc")]
    public async Task Steps_ContinuePastTheShortBoardAndReturnToItsActualOpener(string path)
    {
        await using var context = await NewContextAsync(390, 844, dark: true);
        var fullPage = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(fullPage, path.Replace("/", "/leaderboard"));
        var fullPaths = await fullPage.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name")
            .EvaluateAllAsync<string[]>("links => links.map(link => new URL(link.href).pathname)");
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, path);
        var opener = page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name").Last;
        var openedPath = await opener.EvaluateAsync<string>("link => new URL(link.href).pathname");
        var next = fullPaths[Array.IndexOf(fullPaths, openedPath) + 1];
        await opener.ScrollIntoViewIfNeededAsync();
        var scroll = await page.EvaluateAsync<double>("scrollY");
        await opener.ClickAsync();
        await WaitForProfileAsync(page);
        var historyLength = await page.EvaluateAsync<int>("history.length");
        await Assertions.Expect(page.Locator(".modal-content > .athlete-steps [data-step=next]")).ToBeEnabledAsync();
        await page.Locator(".modal-content > .athlete-steps [data-step=next]").ClickAsync();
        await page.WaitForURLAsync($"**{next}");
        await WaitForProfileAsync(page);
        Assert.Equal(historyLength, await page.EvaluateAsync<int>("history.length"));
        await page.GoBackAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal(path, new Uri(page.Url).PathAndQuery);
        await Assertions.Expect(opener).ToBeFocusedAsync();
        Assert.InRange(await page.EvaluateAsync<double>("scrollY"), scroll - 2, scroll + 2);
    }

    [Fact]
    public async Task Steps_AdvancePendingIntentAndClosedRequestsCannotReopenTheDialog()
    {
        await using var context = await NewContextAsync(1280, 860, dark: true);
        var page = await OpenProfileAsync(context, Profile, "michael-lustgarten");
        await page.EvaluateAsync("""
            () => {
                const original = window.getSharedAthletes;
                window.pendingProfiles = [];
                window.getSharedAthletes = () => new Promise(resolve => window.pendingProfiles.push(resolve));
                window.releaseProfile = async () => {
                    const athletes = await original();
                    window.pendingProfiles.reverse().forEach(resolve => resolve(athletes));
                };
            }
            """);
        await page.Locator(".modal-content > .athlete-steps [data-step=next]").ClickAsync();
        var pendingSlug = await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-athlete-slug");
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.NotEqual(pendingSlug, await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-athlete-slug"));
        Assert.Equal(2, await page.EvaluateAsync<int>("window.pendingProfiles.length"));
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await page.EvaluateAsync("window.releaseProfile()");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
    }

    [Theory]
    [InlineData("[role=tablist] button")]
    [InlineData("#shareAthleteProfile")]
    public async Task Steps_DoNotConsumeKeysOwnedByClockTabsOrTheShareSheet(string selector)
    {
        await using var context = await NewContextAsync(1280, 860, dark: true);
        await context.AddInitScriptAsync("Object.defineProperty(navigator, 'share', {value:undefined, configurable:true});");
        const string path = "/athlete/benjamin-garden";
        var page = await OpenProfileAsync(context, path, "benjamin-garden");
        if (selector == "#shareAthleteProfile") await page.Locator(selector).ClickAsync();
        else await page.Locator(selector).First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal(path, new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync("Benjamin Garden");
    }
}
