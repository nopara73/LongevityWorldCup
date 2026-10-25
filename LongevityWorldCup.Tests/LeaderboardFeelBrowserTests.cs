using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardFeelBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Rows = "#athleteLeaderboard > table > tbody > tr[data-athlete-name]";

    private async Task<(IBrowserContext Context, IPage Page)> OpenAsync(int width = 1440, bool reduce = false, bool crowdFixture = false)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = reduce ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        if (crowdFixture) await RouteAthleteFixtureAsync(context, athletes =>
        {
            foreach (var athlete in athletes.Take(4))
            {
                athlete!["CrowdAge"] = 30;
                athlete["CrowdCount"] = 150;
            }
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        return (context, page);
    }

    [Fact]
    public async Task CondensedSponsorHeading_DropsTheHiddenLinkTargetAndCentersItsLabel()
    {
        var (context, page) = await OpenAsync(reduce: true);
        await using var _ = context;
        await page.Locator(Rows).Nth(20).ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#athleteLeaderboard').dataset.headStuck === 'true'");
        await Assertions.Expect(page.Locator("#athleteLeaderboard .sponsor-coming-soon")).ToBeHiddenAsync();
        Assert.True(await page.Locator("#athleteLeaderboard .sponsor-th").EvaluateAsync<bool>("""
            th => {
                const link = th.querySelector('.sponsor-coming-soon');
                const node = [...th.childNodes].find(n => n.nodeType === Node.TEXT_NODE && n.textContent.includes('Sponsor'));
                const range = document.createRange();
                const start = node.textContent.indexOf('Sponsor');
                range.setStart(node, start); range.setEnd(node, start + 'Sponsor'.length);
                const label = range.getBoundingClientRect(), cell = th.getBoundingClientRect();
                return link.getBoundingClientRect().height === 0 && Math.abs(label.top + label.height / 2 - cell.top - cell.height / 2) < 2;
            }
            """));
    }

    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(768, true)]
    [InlineData(390, true)]
    public async Task CondensingHead_KeepsRowsStationaryInTheDocument(int width, bool reduce)
    {
        var (context, page) = await OpenAsync(width, reduce: reduce);
        await using var _ = context;
        await page.Locator("#athleteSearch").ClickAsync();
        const string rowDocumentTop = "() => document.querySelector('#athleteLeaderboard > table > tbody').getBoundingClientRect().top + scrollY";
        var before = await page.EvaluateAsync<double>(rowDocumentTop);
        await page.Locator(Rows).Nth(20).ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#athleteLeaderboard').dataset.headStuck === 'true'");
        await page.WaitForTimeoutAsync(300); // include the normal-motion folding transition
        Assert.InRange(Math.Abs(await page.EvaluateAsync<double>(rowDocumentTop) - before), 0, 0.5);
        await page.EvaluateAsync("scrollTo(0, 0)");
        await page.WaitForFunctionAsync("() => document.querySelector('#athleteLeaderboard').dataset.headStuck === 'false'");
        await page.WaitForTimeoutAsync(300);
        Assert.InRange(Math.Abs(await page.EvaluateAsync<double>(rowDocumentTop) - before), 0, 0.5);
    }

    [Fact]
    public async Task ScrollIndicators_RespectInitialAndReducedMotionGates()
    {
        var (context, page) = await OpenAsync(390);
        await using var _ = context;
        const string noTransitions = """
            () => [...document.querySelectorAll('.lb-head-progress, #athleteLeaderboard > table > thead, .lb-rail, .lb-tab-ink')]
                .every(el => getComputedStyle(el).transitionDuration.split(',').every(value => parseFloat(value) === 0))
            """;
        Assert.True(await page.EvaluateAsync<bool>(noTransitions));
        await page.Locator("#athleteSearch").ClickAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        Assert.True(await page.EvaluateAsync<bool>(noTransitions));
    }

    [Fact]
    public async Task DeferredMotion_FilterGhostsKeepVisibleContentAndAlwaysCleanUp()
    {
        var (context, page) = await OpenAsync();
        await using var _ = context;
        await page.Locator("#athleteSearch").ClickAsync();
        await page.EvaluateAsync("""
            () => {
                const row = document.querySelector('#athleteLeaderboard > table > tbody > tr[data-athlete-name]');
                scrollTo(0, row.getBoundingClientRect().top + scrollY - 240);
                window.ghostReceipt = [];
                new MutationObserver(() => {
                    for (const ghost of document.querySelectorAll('.lb-ghost')) {
                        window.ghostReceipt.push({ visible: ghost.querySelector('tr').getBoundingClientRect().height > 0,
                            ids: ghost.querySelectorAll('[id], [data-athlete-name]').length });
                    }
                }).observe(document.querySelector('.lb-ghosts'), { childList: true });
            }
            """);
        await page.Locator("#athleteSearch").EvaluateAsync("input => { input.value = 'zz-no-athlete'; input.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.WaitForFunctionAsync("() => window.ghostReceipt.length > 0");
        Assert.True(await page.EvaluateAsync<bool>("() => window.ghostReceipt.every(g => g.visible && g.ids === 0)"));
        await Assertions.Expect(page.Locator(".lb-ghost")).ToHaveCountAsync(0);
        await page.Locator("#athleteSearch").FillAsync("");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.Locator("#athleteSearch").FillAsync("zz-no-athlete");
        await Assertions.Expect(page.Locator(".lb-ghost")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(390)]
    [InlineData(768)]
    public async Task Rail_UsesVisibleCanonicalRanksAndDisappearsForSmallOrGroupedResults(int width)
    {
        var (context, page) = await OpenAsync(width, true);
        await using var _ = context;
        await page.Locator("#athleteSearch").FillAsync("a");
        // The initial rows also satisfy the rail predicate while search is debounced.
        await page.WaitForFunctionAsync("() => new URLSearchParams(location.search).get('search') === 'a'");
        await page.WaitForFunctionAsync("""
            () => {
                const rows = [...document.querySelectorAll('#athleteLeaderboard > table > tbody > tr[data-athlete-name]')].filter(r => r.getClientRects().length);
                const ticks = [...document.querySelectorAll('button.lb-rail-tick:not(.is-section)')];
                return rows.length >= 30 && ticks.length === 5 && ticks.every(t => t.textContent === rows[+t.dataset.index].querySelector('.rank').textContent.trim());
            }
            """);
        await page.EvaluateAsync("""
            () => { const row = document.querySelector('#athleteLeaderboard > table > tbody > tr[data-athlete-name]:not([style*="display: none"])'); scrollTo(0, row.getBoundingClientRect().top + scrollY - 220); }
            """);
        await Assertions.Expect(page.Locator(".lb-rail")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-visible"));
        var tick = page.Locator("button.lb-rail-tick").Nth(2);
        await tick.FocusAsync();
        await tick.PressAsync("Enter");
        // Keyboard activation does not wait for the scroll/layout callbacks.
        await page.WaitForFunctionAsync("""
            () => {
                const tick = document.querySelectorAll('button.lb-rail-tick')[2];
                const rows = [...document.querySelectorAll('#athleteLeaderboard > table > tbody > tr[data-athlete-name]')].filter(r => r.getClientRects().length);
                const row = rows[+tick.dataset.index];
                const head = document.querySelector('#athleteLeaderboard thead');
                const header = document.getElementById('site-sticky-header');
                const clearance = row.getBoundingClientRect().top - header.getBoundingClientRect().bottom - head.getBoundingClientRect().height;
                return clearance >= 0 && clearance <= 10 && document.querySelector('#athleteLeaderboard > table').getBoundingClientRect().right < document.querySelector('.lb-rail').getBoundingClientRect().left;
            }
            """);
        var name = await page.Locator(Rows + ":visible").First.GetAttributeAsync("data-athlete-name");
        await page.Locator("#athleteSearch").FillAsync(name!);
        await page.WaitForFunctionAsync("() => !document.querySelector('.lb-rail').classList.contains('is-visible') && document.querySelector('.lb-rail').inert");
        await page.Locator("#athleteSearch").FillAsync("no-matching-athlete-ever");
        await Assertions.Expect(page.Locator(Rows + ":visible")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("button.lb-rail-tick")).ToHaveCountAsync(0);
        await page.Locator("#athleteSearch").FillAsync("");
        await page.Locator("#group-flags").CheckAsync(new() { Force = true });
        await page.WaitForFunctionAsync("() => !document.querySelector('#athleteLeaderboard').classList.contains('has-rank-rail')");
    }

    [Theory]
    [InlineData("chip")]
    [InlineData("clear")]
    public async Task Rail_ReturnsWhenGroupingIsRemovedWithoutAChangeEvent(string mode)
    {
        var (context, page) = await OpenAsync(390, true);
        await using var _ = context;
        await page.Locator("#group-flags").CheckAsync(new() { Force = true });
        await page.WaitForFunctionAsync("() => document.querySelector('#athleteLeaderboard').classList.contains('leaderboard--flags')");
        await page.Locator(mode == "chip" ? "button[aria-label='Remove By Flag filter']" : "#clearLeaderboardSelection").ClickAsync();
        await Assertions.Expect(page.Locator("#group-flags")).Not.ToBeCheckedAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#athleteLeaderboard').classList.contains('has-rank-rail')");
        await page.Locator(Rows + ":visible").Nth(10).ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(page.Locator(".lb-rail")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-visible"));
    }

    [Theory]
    [InlineData("ultimate")]
    [InlineData("bortz")]
    [InlineData("pheno")]
    [InlineData("crowd")]
    public async Task Peek_UsesComputedAgesAndTheDisplayedMetricWithoutParsing(string view)
    {
        var (context, page) = await OpenAsync(reduce: true, crowdFixture: view == "crowd");
        await using var _ = context;
        await page.Locator($"label[for='view-{view}']").ClickAsync();
        var row = page.Locator(Rows + ":visible").First;
        await row.ScrollIntoViewIfNeededAsync();
        await page.Keyboard.PressAsync("Tab");
        await row.Locator(".athlete-name").FocusAsync();
        await Assertions.Expect(page.Locator(".lb-peek")).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const row = document.activeElement.closest('tr');
                const peek = document.querySelector('.lb-peek');
                const athlete = window.getLeaderboardPeek(row.dataset.athleteName);
                const view = document.querySelector('[name=leaderboardView]:checked').value;
                const bortz = view === 'bortz' || (view === 'ultimate' && athlete.lowestBortzAge != null);
                const bio = peek.querySelector('.is-bio');
                return peek.querySelector('.lb-peek-metric').textContent === row.querySelector('.age-reduction').textContent.trim()
                    && (view === 'crowd' ? bio === null : bio.textContent === (bortz ? athlete.lowestBortzAge : athlete.lowestPhenoAge).toFixed(1))
                    && peek.scrollHeight <= peek.clientHeight + 1 && peek.scrollWidth <= peek.clientWidth + 1;
            }
            """));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".lb-peek")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(false, "normal")]
    [InlineData(false, "missing")]
    [InlineData(false, "throw")]
    [InlineData(true, "normal")]
    public async Task FlagTransition_AppliesEachInputExactlyOnce(bool reduce, string mode)
    {
        var (context, page) = await OpenAsync(reduce: reduce);
        await using var _ = context;
        await page.Locator("#athleteSearch").ClickAsync();
        await page.EvaluateAsync("""
            mode => {
                window.flagChanges = 0; window.flagTransitions = 0;
                document.getElementById('group-flags').addEventListener('change', () => window.flagChanges++);
                const start = document.startViewTransition?.bind(document);
                document.startViewTransition = mode === 'missing' ? undefined : update => {
                    window.flagTransitions++;
                    if (mode === 'throw') throw new Error('unavailable');
                    return start(update);
                };
            }
            """, mode);
        for (var i = 1; i <= 4; i++)
        {
            await page.Locator("#group-flags").EvaluateAsync("input => input.click()");
            await page.WaitForFunctionAsync("count => window.flagChanges === count && !document.querySelector('.lb-crossfade')", i);
            Assert.Equal(i % 2 == 1, await page.Locator("#group-flags").IsCheckedAsync());
            Assert.Equal(i % 2 == 1, page.Url.Contains("group=flags", StringComparison.Ordinal));
            Assert.Equal(i % 2 == 1, await page.Locator("#athleteLeaderboard").EvaluateAsync<bool>("board => board.classList.contains('leaderboard--flags')"));
        }
        Assert.Equal(reduce || mode == "missing" ? 0 : 4, await page.EvaluateAsync<int>("() => window.flagTransitions"));
    }
}
