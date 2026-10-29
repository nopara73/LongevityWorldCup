using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

// The athlete dialog's frame: the chrome row, the hero with its actions and
// flag, and stepping from one athlete to the next without leaving the dialog.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed partial class AthleteDialogFrameBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Profile = "/athlete/michael-lustgarten";

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task Actions_FollowTheNameAndShareTheFirstView(int width, int height)
    {
        await using var context = await NewContextAsync(width, height, dark: width < 400);
        var page = await OpenProfileAsync(context, Profile, "michael-lustgarten");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const failures = [];
                const name = document.getElementById('athleteName').getBoundingClientRect();
                const links = document.querySelector('#athlete-profile > .athlete-links');
                const box = links.getBoundingClientRect();
                const ranks = document.getElementById('athleteRankings').getBoundingClientRect();
                if (box.top < name.bottom - 1) failures.push('actions should follow the name');
                if (box.bottom > ranks.top + 1) failures.push('actions should come before the ranks');
                if (box.top - name.bottom > 32) failures.push(`actions drift ${Math.round(box.top - name.bottom)}px below the name`);
                if (box.bottom > innerHeight) failures.push(`actions end at ${Math.round(box.bottom)}px, below the first view`);

                const actions = [...links.querySelectorAll(':scope > a, :scope > .athlete-share-wrapper > button')]
                    .filter(action => getComputedStyle(action).display !== 'none');
                if (actions.length < 3) failures.push(`only ${actions.length} actions shown`);
                const tops = actions.map(action => Math.round(action.getBoundingClientRect().top));
                if (tops.some(top => Math.abs(top - tops[0]) > 1)) failures.push(`actions break into rows ${tops.join(', ')}`);
                for (const action of actions) {
                    const ring = action.querySelector('i').getBoundingClientRect();
                    const label = action.getBoundingClientRect();
                    if (Math.abs(ring.width - ring.height) > 1 || ring.width < 44) failures.push(`${action.id}: ring ${Math.round(ring.width)}x${Math.round(ring.height)}`);
                    if (parseFloat(getComputedStyle(action.querySelector('i')).borderTopLeftRadius) < ring.width / 2 - 1) failures.push(`${action.id}: icon is not a ring`);
                    if (Math.abs(ring.left + ring.width / 2 - (label.left + label.width / 2)) > 1) failures.push(`${action.id}: caption is off the ring's centre`);
                }

                // The stats card closes itself now that the actions have left it.
                const stats = getComputedStyle(document.getElementById('athlete-stats'));
                if (parseFloat(stats.borderBottomLeftRadius) === 0) failures.push('stats card lost its bottom corners');
                return failures;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px: {string.Join("; ", problems)}");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    public async Task ChromeRow_PairsCloseWithTheStepsAndClearsThePortrait(int width, int height)
    {
        await using var context = await NewContextAsync(width, height, dark: true);
        var page = await OpenProfileAsync(context, Profile, "michael-lustgarten");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const failures = [];
                const content = document.querySelector('#detailsModal .modal-content').getBoundingClientRect();
                const close = document.getElementById('closeAthleteDetailsModal').getBoundingClientRect();
                const steps = document.querySelector('.modal-content > .athlete-steps');
                if (!steps || steps.hidden) return ['the dialog has no athlete steps'];
                const buttons = [...steps.querySelectorAll('.athlete-step')].map(button => button.getBoundingClientRect());
                if (buttons.length !== 2) failures.push(`${buttons.length} step buttons`);
                for (const button of buttons) {
                    if (Math.abs(button.top - close.top) > 1) failures.push(`step at y=${Math.round(button.top)}, close at y=${Math.round(close.top)}`);
                    if (Math.abs(button.width - close.width) > 1 || Math.abs(button.height - close.height) > 1) failures.push('steps and close differ in size');
                }
                const leftInset = close.left - content.left;
                const rightInset = content.right - buttons[buttons.length - 1].right;
                if (Math.abs(leftInset - rightInset) > 2) failures.push(`insets differ: ${Math.round(leftInset)} vs ${Math.round(rightInset)}`);

                const portrait = document.querySelector('#athlete-profile .portrait-wrapper').getBoundingClientRect();
                const overlaps = rect => rect.left < portrait.right && rect.right > portrait.left && rect.top < portrait.bottom && rect.bottom > portrait.top;
                if (overlaps(close) || buttons.some(overlaps)) failures.push('the chrome covers the portrait');
                return failures;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
    }

    [Fact]
    public async Task Steps_SwapTheProfileInPlaceAndCloseReturnsToTheBoard()
    {
        await using var context = await NewContextAsync(1280, 860, dark: true);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/leaderboard");
        var rows = page.Locator(".leaderboard table tbody tr[data-athlete-name]");
        await Assertions.Expect(rows.Nth(2)).ToBeVisibleAsync();
        var names = await rows.EvaluateAllAsync<string[]>("rows => rows.slice(0, 3).map(row => row.dataset.athleteName)");

        await rows.Nth(0).Locator("a.athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        var steps = page.Locator(".modal-content > .athlete-steps");
        await Assertions.Expect(steps.Locator("[data-step=previous]")).ToBeDisabledAsync();
        await Assertions.Expect(steps.Locator("[data-step=next]")).ToBeEnabledAsync();
        var openedLength = await page.EvaluateAsync<int>("() => history.length");

        await steps.Locator("[data-step=next]").ClickAsync();
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync(names[1]);
        await WaitForProfileAsync(page);
        await Assertions.Expect(steps.Locator("[data-step=previous]")).ToBeEnabledAsync();
        Assert.Equal(openedLength, await page.EvaluateAsync<int>("() => history.length"));
        var slug = await page.EvaluateAsync<string>("() => document.querySelector('#detailsModal .modal-content').dataset.athleteSlug");
        Assert.Equal($"/athlete/{slug}", new Uri(page.Url).AbsolutePath);

        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync(names[2]);
        await WaitForProfileAsync(page);
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync(names[1]);
        await WaitForProfileAsync(page);
        await Assertions.Expect(page.Locator("#stickyAthleteName")).ToContainTextAsync(names[1]);

        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal("/leaderboard", new Uri(page.Url).AbsolutePath);
        Assert.Null(await page.EvaluateAsync<string?>("() => history.state?.modal ?? null"));
    }

    [Fact]
    public async Task Steps_StayOutOfTheGuessingGame()
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 860 },
            ColorScheme = ColorScheme.Dark,
            ReducedMotion = ReducedMotion.Reduce
        });
        await using var _ = context;
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/patrick-ruff?guessmyage=1", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#detailsModal .modal-content.guess-mode")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".modal-content > .athlete-steps")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#modalFlagMark")).ToBeHiddenAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForTimeoutAsync(300);
        Assert.Equal("/athlete/patrick-ruff", new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator("#detailsModal .modal-content.guess-mode")).ToHaveCountAsync(1);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task FlagMark_RidesOnThePortraitRimAndLeadsToTheTeam(int width, int height)
    {
        await using var context = await NewContextAsync(width, height, dark: false);
        var page = await OpenProfileAsync(context, "/athlete/benjamin-garden", "benjamin-garden");
        var mark = page.Locator("#modalFlagMark");
        await Assertions.Expect(mark).ToBeVisibleAsync();
        await Assertions.Expect(mark).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("^/flag/"));
        await Assertions.Expect(mark).ToHaveAttributeAsync("aria-label", new System.Text.RegularExpressions.Regex(".+"));

        var placement = await page.EvaluateAsync<double[]>(
            """
            () => {
                const portrait = document.getElementById('modalProfilePic').getBoundingClientRect();
                const flag = document.getElementById('modalFlagMark').getBoundingClientRect();
                const cx = portrait.left + portrait.width / 2, cy = portrait.top + portrait.height / 2;
                const fx = flag.left + flag.width / 2, fy = flag.top + flag.height / 2;
                return [Math.hypot(fx - cx, fy - cy) / (portrait.width / 2), fx - cx, fy - cy, flag.width / portrait.width];
            }
            """);
        Assert.True(placement[0] is > 0.75 and < 1.15, $"{width}px: the flag should sit on the rim (distance {placement[0]:0.00} radii).");
        Assert.True(placement[1] < 0 && placement[2] > 0, $"{width}px: the flag belongs on the lower left, opposite the PRO badge.");
        Assert.True(placement[3] is > 0.12 and < 0.3, $"{width}px: the flag is {placement[3]:0.00} of the portrait's width.");
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task StickyHeader_CarriesThePortraitAndTheSteps(int width, int height)
    {
        await using var context = await NewContextAsync(width, height, dark: true);
        var page = await OpenProfileAsync(context, Profile, "michael-lustgarten");
        await page.Locator("#detailsModal .modal-content").EvaluateAsync("content => { content.style.scrollBehavior = 'auto'; content.scrollTop = content.scrollHeight / 2; content.dispatchEvent(new Event('scroll')); }");
        await Assertions.Expect(page.Locator("#modalStickyHeader")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#stickyAthletePortrait")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#modalStickyHeader .athlete-steps")).ToBeVisibleAsync();

        var row = await page.EvaluateAsync<double[]>(
            """
            () => {
                const header = document.getElementById('modalStickyHeader').getBoundingClientRect();
                const close = document.getElementById('stickyCloseBtn').getBoundingClientRect();
                const portrait = document.getElementById('stickyAthletePortrait').getBoundingClientRect();
                const steps = document.querySelector('#modalStickyHeader .athlete-steps').getBoundingClientRect();
                const name = document.getElementById('stickyAthleteName').getBoundingClientRect();
                return [portrait.left - close.right, name.left - portrait.right, header.right - steps.right,
                    Math.abs((close.top + close.bottom) / 2 - (steps.top + steps.bottom) / 2), name.right - steps.left];
            }
            """);
        Assert.True(row[0] > 0 && row[1] > 0, $"{width}px: close, portrait and name should read left to right.");
        Assert.True(row[2] is >= 0 and < 32, $"{width}px: the steps should end the header ({row[2]:0}px from its edge).");
        Assert.True(row[3] <= 1, $"{width}px: close and steps should share the header's centre line.");
        Assert.True(row[4] <= 0, $"{width}px: the name should not run under the steps.");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    private static async Task<IPage> OpenProfileAsync(IBrowserContext context, string path, string slug)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            slug => {
                const content = document.querySelector('#detailsModal .modal-content');
                return content?.dataset.athleteSlug === slug
                    && !content.classList.contains('is-loading')
                    && !content.hasAttribute('data-server-rendered-profile')
                    && [...document.querySelectorAll('link[rel="stylesheet"]')].every(link => !!link.sheet);
            }
            """, slug);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        return page;
    }

    private static Task WaitForProfileAsync(IPage page) => page.WaitForFunctionAsync(
        "() => { const content = document.querySelector('#detailsModal .modal-content'); return content && !content.classList.contains('is-loading'); }");

    private async Task<IBrowserContext> NewContextAsync(int width, int height, bool dark)
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        return context;
    }
}
