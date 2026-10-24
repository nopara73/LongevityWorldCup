using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteProfileDocumentBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Profile = "/athlete/michael-lustgarten";

    [Fact]
    public async Task HydrationAfterPageShow_KeepsTheCurrentDocumentsStylesheetsLoaded()
    {
        await using var context = await NewContextAsync(390, 844, true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Func<IRoute, Task> hold = async route => { await release.Task; await route.ContinueAsync(); };
        await context.RouteAsync("**/api/data/athletes", hold);
        await context.AddInitScriptAsync("""
            window.__profileNavigation = [];
            navigation.addEventListener('navigate', event => window.__profileNavigation.push({
                type: event.navigationType, sameDocument: event.destination.sameDocument, url: event.destination.url
            }));
            """);
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(Profile, new() { WaitUntil = WaitUntilState.Load });
            release.TrySetResult();
            await page.WaitForFunctionAsync("""
                () => {
                    const content = document.querySelector('#detailsModal .modal-content');
                    return content?.dataset.athleteSlug === 'michael-lustgarten'
                        && !content.classList.contains('is-loading')
                        && !content.hasAttribute('data-server-rendered-profile');
                }
                """);
            var state = await page.EvaluateAsync<string>("""
                () => JSON.stringify({ navigation: window.__profileNavigation, styles:
                    [...document.querySelectorAll('link[rel="stylesheet"]')]
                        .map(link => ({ href: link.href, disabled: link.disabled, loaded: !!link.sheet })) })
                """);
            Assert.True(await page.EvaluateAsync<bool>("() => window.__profileNavigation.some(event => event.type === 'replace' && event.sameDocument)"), state);
            Assert.True(await page.EvaluateAsync<bool>("() => [...document.querySelectorAll('link[rel=stylesheet]')].every(link => !!link.sheet && !link.disabled)"), state);
        }
        finally
        {
            release.TrySetResult();
            await context.UnrouteAsync("**/api/data/athletes", hold);
        }
    }

    [Theory]
    [InlineData(1440, 900, true)]
    [InlineData(1024, 768, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task ProfileSections_ShareOneColumnAndAttachTheirBands(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(width, height, dark);
        var page = await OpenProfileAsync(context);

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const failures = [];
                const visible = el => el && getComputedStyle(el).display !== 'none' && el.getBoundingClientRect().height > 0;
                const cards = ['#athleteRankings', '#modalBadgeStrip', '#ageVisualization', '#athlete-stats',
                    '#athlete-biomarkers', '#athlete-proofs', '#detailsModal .events-embed']
                    .map(selector => [selector, document.querySelector(selector)])
                    .filter(([, el]) => visible(el));
                if (cards.length < 6) failures.push(`only ${cards.length} sections rendered`);
                const first = cards[0][1].getBoundingClientRect();
                const firstStyle = getComputedStyle(cards[0][1]);
                for (const [selector, el] of cards) {
                    const box = el.getBoundingClientRect();
                    const style = getComputedStyle(el);
                    if (Math.abs(box.left - first.left) > 1 || Math.abs(box.width - first.width) > 1)
                        failures.push(`${selector}: ${Math.round(box.left)}+${Math.round(box.width)} is off the column ${Math.round(first.left)}+${Math.round(first.width)}`);
                    if (style.borderLeftColor !== firstStyle.borderLeftColor || style.borderLeftWidth !== firstStyle.borderLeftWidth)
                        failures.push(`${selector}: frame ${style.borderLeftWidth} ${style.borderLeftColor} differs`);
                    if (style.boxShadow !== 'none') failures.push(`${selector}: extra shadow`);
                }

                const attached = (upper, lower) => {
                    const a = document.querySelector(upper), b = document.querySelector(lower);
                    if (!visible(a) || !visible(b)) return;
                    const gap = b.getBoundingClientRect().top - a.getBoundingClientRect().bottom;
                    if (Math.abs(gap) > 1) failures.push(`${lower} should continue ${upper} (gap ${Math.round(gap)})`);
                    if (parseFloat(getComputedStyle(a).borderBottomLeftRadius) !== 0 || parseFloat(getComputedStyle(b).borderTopLeftRadius) !== 0)
                        failures.push(`${upper} and ${lower} should share one card`);
                };
                attached('#athleteRankings', '#modalBadgeStrip');

                const ranks = [...document.querySelectorAll('#athleteRankings a')];
                if (ranks.length > 1 && ranks.some(link => Math.abs(link.getBoundingClientRect().top - ranks[0].getBoundingClientRect().top) > 1))
                    failures.push('rank cells should share the card band');

                const gaps = [['#ageVisualization', '#athlete-stats'], ['#athlete-stats', '#athlete-biomarkers'],
                    ['#athlete-biomarkers', '#athlete-proofs'], ['#athlete-proofs', '#detailsModal .events-embed']]
                    .map(([a, b]) => [document.querySelector(a), document.querySelector(b)])
                    .filter(([a, b]) => visible(a) && visible(b))
                    .map(([a, b]) => Math.round(b.getBoundingClientRect().top - a.getBoundingClientRect().bottom));
                if (gaps.some(gap => Math.abs(gap - gaps[0]) > 1)) failures.push(`uneven section gaps ${gaps.join(', ')}`);

                const modal = document.querySelector('#detailsModal .modal-content');
                if (modal.scrollWidth > modal.clientWidth + 1) failures.push('dialog overflows sideways');
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
    [InlineData(667, 375)]
    [InlineData(932, 480)]
    public async Task StickyHeader_KeepsTheCloseButtonInPlaceAndTracksProgressOnItsEdge(int width, int height)
    {
        await using var context = await NewContextAsync(width, height, true);
        var page = await OpenProfileAsync(context);
        var top = await CloseButtonBoxAsync(page, "#closeAthleteDetailsModal");

        await page.Locator("#detailsModal .modal-content").EvaluateAsync("content => { content.style.scrollBehavior = 'auto'; content.scrollTop = content.scrollHeight / 2; content.dispatchEvent(new Event('scroll')); }");
        await Assertions.Expect(page.Locator("#modalStickyHeader")).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(300);
        var sticky = await CloseButtonBoxAsync(page, "#stickyCloseBtn");
        Assert.True(Math.Abs(sticky.X - top.X) <= 2,
            $"{width}px: the close button jumps from x={top.X:0} to x={sticky.X:0} when the header sticks.");

        var edge = await page.EvaluateAsync<double[]>(
            """
            () => {
                const header = document.getElementById('modalStickyHeader').getBoundingClientRect();
                const bar = document.getElementById('modalScrollProgress').getBoundingClientRect();
                const name = document.getElementById('stickyAthleteName').getBoundingClientRect();
                return [header.bottom - bar.bottom, bar.left - header.left, name.left];
            }
            """);
        Assert.True(Math.Abs(edge[0]) <= 2 && Math.Abs(edge[1]) <= 1,
            $"{width}px: progress should run along the header's bottom edge (offset {edge[0]:0.#}, inset {edge[1]:0.#}).");
        Assert.True(edge[2] > sticky.X + sticky.Width - 1, $"{width}px: the name should follow the close button.");
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task LoadingProfile_ShowsARadarSkeletonInsteadOfAnEmptyFrame(int width, int height)
    {
        await using var context = await NewContextAsync(width, height, true);
        await context.RouteAsync("**/api/data/athletes", _ => Task.CompletedTask);
        var page = await context.NewPageAsync();
        await page.GotoAsync(Profile, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#detailsModal .modal-content.is-loading")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#ageVisualization")).ToBeVisibleAsync();

        var skeleton = await page.Locator("#ageVisualization").EvaluateAsync<string[]>(
            """
            card => {
                const after = getComputedStyle(card, '::after');
                const before = getComputedStyle(card, '::before');
                return [after.content, after.backgroundImage, before.content, String(card.getBoundingClientRect().height)];
            }
            """);
        Assert.True(skeleton[0] != "none" && skeleton[0] != "normal" && skeleton[1].Contains("gradient"),
            $"{width}px: the radar card should show a skeleton while the profile loads ({skeleton[0]}, {skeleton[1]}).");
        Assert.True(skeleton[2] != "none" && skeleton[2] != "normal", $"{width}px: the skeleton should keep the card's band.");
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task FailedProfile_ShowsTheMessageAsACardWithoutAnEmptyRadar(int width, int height)
    {
        await using var context = await NewContextAsync(width, height, true);
        await context.RouteAsync("**/api/data/athletes", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync(Profile, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#athleteLoadError")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#retryAthleteLoad")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#ageVisualization")).ToBeHiddenAsync();

        var aligned = await page.EvaluateAsync<double[]>(
            """
            () => {
                const error = document.getElementById('athleteLoadError').getBoundingClientRect();
                const ranks = document.getElementById('athleteRankings');
                const stats = document.getElementById('athlete-stats');
                const reference = (!ranks.hidden ? ranks : stats).getBoundingClientRect();
                return [error.left - reference.left, error.width - reference.width];
            }
            """);
        Assert.True(Math.Abs(aligned[0]) <= 1 && Math.Abs(aligned[1]) <= 1,
            $"{width}px: the message should sit in the profile's column (offset {aligned[0]:0.#}, width {aligned[1]:0.#}).");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    private static async Task<IPage> OpenProfileAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        var failures = new List<string>();
        page.PageError += (_, error) => failures.Add(error);
        page.RequestFailed += (_, request) => failures.Add($"{request.Method} {request.Url}: {request.Failure}");
        page.Console += (_, message) => { if (message.Type == "error") failures.Add(message.Text); };
        await page.GotoAsync(Profile, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        try
        {
            await page.WaitForFunctionAsync(
            """
            () => {
                const content = document.querySelector('#detailsModal .modal-content');
                return content?.dataset.athleteSlug === 'michael-lustgarten'
                    && !content.classList.contains('is-loading')
                    && !content.hasAttribute('data-server-rendered-profile')
                    && document.getElementById('athlete-proofs')
                    && [...document.querySelectorAll('link[rel="stylesheet"]')].every(link => !!link.sheet);
            }
            """);
        }
        catch (TimeoutException error)
        {
            var state = await page.EvaluateAsync<string>("""
                async () => {
                    const content = document.querySelector('#detailsModal .modal-content');
                    const settle = promise => promise === undefined ? 'absent' : Promise.race([
                        Promise.resolve(promise).then(() => 'fulfilled', error => 'rejected: ' + error),
                        new Promise(resolve => setTimeout(() => resolve('pending'), 20))
                    ]);
                    return JSON.stringify({
                        ready: document.readyState,
                        visibility: document.visibilityState,
                        contentClass: content?.className,
                        contentData: content ? { ...content.dataset } : null,
                        errorVisible: !document.getElementById('athleteLoadError')?.hidden,
                        proofsPresent: !!document.getElementById('athlete-proofs'),
                        modules: await settle(window.modulesReady),
                        dialogModules: await settle(window.athleteDialogModulesReady),
                        athletes: await settle(window.__sharedAthletesRequest),
                        styles: [...document.querySelectorAll('link[rel="stylesheet"]')]
                            .map(link => ({ href: link.href, media: link.media, disabled: link.disabled, loaded: !!link.sheet }))
                    });
                }
                """);
            throw new Xunit.Sdk.XunitException($"Direct profile hydration timed out: {state}\nBrowser failures: {string.Join("; ", failures)}\n{error.Message}");
        }
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        return page;
    }

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

    private static async Task<LocatorBoundingBoxResult> CloseButtonBoxAsync(IPage page, string selector) =>
        Assert.IsType<LocatorBoundingBoxResult>(await page.Locator(selector).BoundingBoxAsync());
}
