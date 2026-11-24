using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed partial class LeaderboardFeelBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Rows = "#athleteLeaderboard > table > tbody > tr[data-athlete-name]";

    private async Task<(IBrowserContext Context, IPage Page)> OpenAsync(int width = 1440, bool reduce = false, bool crowdFixture = false, string path = "/leaderboard")
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
        await NavigateAndSettleAsync(page, path);
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
        await WaitForHeadingMotionAsync(page);
        Assert.InRange(Math.Abs(await page.EvaluateAsync<double>(rowDocumentTop) - before), 0, 0.5);
        await page.EvaluateAsync("scrollTo(0, 0)");
        await page.WaitForFunctionAsync("() => document.querySelector('#athleteLeaderboard').dataset.headStuck === 'false'");
        await WaitForHeadingMotionAsync(page);
        Assert.InRange(Math.Abs(await page.EvaluateAsync<double>(rowDocumentTop) - before), 0, 0.5);
    }

    private static Task WaitForHeadingMotionAsync(IPage page) => WaitForFiniteAnimationsAsync(
        page, "#athleteLeaderboard > table > thead, #athleteLeaderboard > table > thead *");

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
    public async Task FilteringToEmpty_DoesNotPaintDepartedAthletesOverRecovery()
    {
        var (context, page) = await OpenAsync();
        await using var _ = context;
        await page.Locator("#athleteSearch").ClickAsync();
        await page.Locator("#athleteSearch").EvaluateAsync("input => { input.value = 'zz-no-athlete'; input.dispatchEvent(new Event('input', { bubbles: true })); }");
        await Assertions.Expect(page.Locator(".no-results")).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => [...document.querySelectorAll('#athleteLeaderboard .athlete-name')]
                .every(name => !name.getClientRects().length)
            """), "Departing athlete copies obscure the empty result while recovery is already available.");
        await Assertions.Expect(page.Locator(".lb-ghost")).ToHaveCountAsync(0);
        await page.Locator("#athleteSearch").FillAsync("");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.Locator("#athleteSearch").FillAsync("zz-no-athlete");
        await Assertions.Expect(page.Locator(".lb-ghost")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Press_TintsTheWholeRowWithoutCellBoxesOrScaling()
    {
        var (context, page) = await OpenAsync();
        await using var _ = context;
        var row = page.Locator(Rows).Nth(20);
        await row.ScrollIntoViewIfNeededAsync();
        await row.Locator(".rank").HoverAsync();
        await WaitForFiniteAnimationsAsync(page, Rows + ":hover");
        var hoverColor = await row.EvaluateAsync<string>("row => getComputedStyle(row).backgroundColor");
        await page.Mouse.DownAsync();
        try
        {
            await page.WaitForFunctionAsync("""
                hoverColor => {
                    const row = document.querySelector('#athleteLeaderboard tr[data-athlete-name]:active');
                    return row && getComputedStyle(row).scale === 'none'
                        && getComputedStyle(row).backgroundColor !== hoverColor
                        && [...row.cells].filter(cell => cell.offsetWidth)
                            .every(cell => getComputedStyle(cell).backgroundColor === 'rgba(0, 0, 0, 0)');
                }
                """, hoverColor);
        }
        finally
        {
            await page.Mouse.MoveAsync(0, 0);
            await page.Mouse.UpAsync();
        }
    }

    [Fact]
    public async Task TouchPress_TintsStripedAmateurRowsWithReducedMotion()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var row = page.Locator(Rows + ".tier-amateur:nth-child(even)").First;
        var rank = row.Locator(".rank");
        await rank.ScrollIntoViewIfNeededAsync();
        var box = await rank.BoundingBoxAsync() ?? throw new InvalidOperationException("Rank must be visible");
        var session = await context.NewCDPSessionAsync(page);
        await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = "touchStart",
            ["touchPoints"] = new[] { new { x = box.X + box.Width / 2, y = box.Y + box.Height / 2 } }
        });
        try
        {
            await Assertions.Expect(row).ToHaveAttributeAsync("data-lwc-pressed", "row");
            await Assertions.Expect(row).ToHaveCSSAsync("box-shadow", new System.Text.RegularExpressions.Regex("inset"));
            await Assertions.Expect(row.Locator(".portrait")).ToHaveCSSAsync("scale", "none");
        }
        finally
        {
            await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchCancel", ["touchPoints"] = Array.Empty<object>()
            });
            await session.DetachAsync();
        }
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
        await Assertions.Expect(page.Locator(".lb-rail")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-visible"));
        await page.Locator(Rows + ":visible").Nth(20).ScrollIntoViewIfNeededAsync();
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
                const board = document.querySelector('#athleteLeaderboard').getBoundingClientRect();
                const table = head.closest('table').getBoundingClientRect();
                const rail = document.querySelector('.lb-rail').getBoundingClientRect();
                const score = document.createRange();
                score.selectNodeContents(row.querySelector('.age-reduction'));
                return clearance >= 0 && clearance <= 10 && Math.abs(table.right - board.right) <= 2
                    && rail.right <= board.right && rail.right >= board.right - 3
                    && score.getBoundingClientRect().right <= rail.left;
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

    [Fact]
    public async Task RowPeek_FollowsTheHoveredRowAndYieldsToContactLinks()
    {
        var (context, page) = await OpenAsync(reduce: true);
        await using var _ = context;
        await page.Clock.InstallAsync();
        var rows = page.Locator(Rows + ":visible").Filter(new() { Has = page.Locator("a.media-contact") });
        var first = rows.First;
        var second = rows.Nth(1);
        var peek = page.Locator("#leaderboard-peek");
        var name = peek.Locator(".lb-peek-name");

        // Layout can send a boundary event when scrolling a row under a stationary mouse.
        await first.Locator(".rank").DispatchEventAsync("pointerover", new { pointerType = "mouse" });
        await page.Clock.RunForAsync(500);
        await Assertions.Expect(peek).ToBeHiddenAsync();

        // Deliver the scroll notification after the pointer is already at its new position.
        await first.EvaluateAsync("""
            row => row.addEventListener('pointerover', () => requestAnimationFrame(() => window.dispatchEvent(new Event('scroll'))), { once: true })
            """);
        await first.Locator(".rank").HoverAsync();
        await Assertions.Expect(name).ToHaveTextAsync(await first.Locator(".athlete-name").InnerTextAsync());
        var score = second.Locator(".age-reduction");
        await score.HoverAsync();
        await Assertions.Expect(name).ToHaveTextAsync(await second.Locator(".athlete-name").InnerTextAsync());
        await Assertions.Expect(peek).ToBeVisibleAsync();
        var scoreBox = (await score.BoundingBoxAsync())!;
        var cardBox = (await peek.BoundingBoxAsync())!;
        var pointerX = scoreBox.X + scoreBox.Width / 2;
        var gap = Math.Max(cardBox.X - pointerX, pointerX - (cardBox.X + cardBox.Width));
        Assert.InRange(gap, 10, 14);
        var scanX = pointerX + (cardBox.X > pointerX ? 3 : -3);
        Assert.False(await page.EvaluateAsync<bool>(
            "([x, y]) => !!document.elementFromPoint(x, y)?.closest('.lb-peek-region')",
            new double[] { scanX, cardBox.Y + cardBox.Height / 2 }));
        await Assertions.Expect(first.Locator(".athlete-name")).Not.ToHaveAttributeAsync("aria-details", "leaderboard-peek");

        // Pause between the row and the card for longer than its dismissal delay.
        var rowBox = (await second.BoundingBoxAsync())!;
        var edgeX = cardBox.X > pointerX ? cardBox.X : cardBox.X + cardBox.Width;
        var gapY = cardBox.Y < rowBox.Y ? rowBox.Y - 4 : rowBox.Y + rowBox.Height + 4;
        await page.Mouse.MoveAsync((pointerX + edgeX) / 2, gapY);
        await page.Clock.RunForAsync(500);
        await Assertions.Expect(peek).ToBeVisibleAsync();
        await Assertions.Expect(name).ToHaveTextAsync(await second.Locator(".athlete-name").InnerTextAsync());
        await peek.HoverAsync();
        await first.Locator(".age-reduction").HoverAsync();
        await Assertions.Expect(name).ToHaveTextAsync(await first.Locator(".athlete-name").InnerTextAsync());

        await second.Locator("a.media-contact").HoverAsync();
        await Assertions.Expect(page.Locator(".lwc-tip")).ToBeVisibleAsync();
        await Assertions.Expect(peek).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("/", 1440, false)]
    [InlineData("/", 390, true)]
    [InlineData("/leaderboard", 700, false)]
    public async Task FirstMouseHover_IdentifiesTheAthleteAcrossTheRow(string path, int width, bool reduce)
    {
        var (context, page) = await OpenAsync(width, reduce, path: path);
        await using var _ = context;
        var rows = page.Locator(Rows + ":visible");
        var row = rows.Nth(1);
        var name = row.Locator(".athlete-name");
        var face = row.Locator(".portrait-wrapper");
        await row.ScrollIntoViewIfNeededAsync();
        await page.Mouse.MoveAsync(1, 1);
        var restingColor = await name.EvaluateAsync<string>("el => getComputedStyle(el).color");
        // Late table content can change its height while the pointer rests on a score.
        await row.EvaluateAsync("""
            row => row.addEventListener('pointermove', () => {
                const sibling = document.createElement('tr');
                sibling.id = 'late-preview-sibling';
                sibling.hidden = true;
                row.parentElement.append(sibling);
                row.closest('table').createTFoot().insertRow().insertCell().style.height = '40px';
            }, { once: true })
            """);
        await row.Locator(".age-reduction").HoverAsync();
        await Assertions.Expect(name).Not.ToHaveCSSAsync("color", restingColor);
        await Assertions.Expect(face).ToHaveCSSAsync("translate", reduce ? "none" : "2px -2px");
        var card = page.Locator("#leaderboard-peek");
        await Assertions.Expect(card.Locator(".lb-peek-name")).ToHaveTextAsync(await name.InnerTextAsync());
        var bounds = (await card.BoundingBoxAsync())!;
        Assert.InRange(bounds.X, 0, width - bounds.Width + 1);
        await card.HoverAsync();
        var cardLink = card.Locator("a.lb-peek-top");
        await cardLink.FocusAsync();
        await row.EvaluateAsync("""
            async row => {
                document.getElementById('late-preview-sibling').remove();
                document.dispatchEvent(new Event('lwc:standings-ready'));
                row.closest('table').tFoot.rows[0].cells[0].style.height = '80px';
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            }
            """);
        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(cardLink).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(card).ToBeHiddenAsync();
        await Assertions.Expect(name).ToBeFocusedAsync();
        await name.BlurAsync();
        var next = rows.Nth(2);
        await next.Locator(".rank").HoverAsync();
        await Assertions.Expect(card.Locator(".lb-peek-name")).ToHaveTextAsync(await next.Locator(".athlete-name").InnerTextAsync());
        await Assertions.Expect(name).ToHaveCSSAsync("color", restingColor);
        await next.EvaluateAsync("row => row.remove()");
        await Assertions.Expect(card).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("ultimate")]
    [InlineData("bortz")]
    [InlineData("pheno")]
    [InlineData("crowd")]
    [InlineData("improvement")]
    [InlineData("bortz-improvement")]
    public async Task Peek_UsesComputedAgesAndTheDisplayedMetricWithoutParsing(string view)
    {
        var (context, page) = await OpenAsync(reduce: true, crowdFixture: view == "crowd",
            path: view == "ultimate" ? "/leaderboard" : $"/league/{view}");
        await using var _ = context;
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
                const bortz = view === 'bortz' || (view === 'ultimate' && athlete.bortzRankResult != null);
                const bio = peek.querySelector('.is-bio');
                return peek.querySelector('.lb-peek-metric').textContent === row.querySelector('.age-reduction').textContent.trim()
                    && (view === 'crowd' || view.endsWith('improvement') ? bio === null : bio.textContent === (bortz ? athlete.bortzRankResult : athlete.phenoRankResult).bio.toFixed(1))
                    && peek.scrollHeight <= peek.clientHeight + 1 && peek.scrollWidth <= peek.clientWidth + 1;
            }
            """));
        var card = page.Locator("#leaderboard-peek");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(card.GetByRole(AriaRole.Link)).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(card.Locator(".lb-peek-metric")).ToBeFocusedAsync();
        var reference = view switch
        {
            "crowd" => "age today",
            "improvement" => "highest recorded pheno age",
            "bortz-improvement" => "highest recorded bortz age",
            _ => "age at the blood draw"
        };
        await Assertions.Expect(page.GetByRole(AriaRole.Tooltip)).ToContainTextAsync(reference);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(card).ToBeHiddenAsync();
        await Assertions.Expect(row.Locator(".athlete-name")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("/leaderboard?search=markus", false)]
    [InlineData("/?search=markus", true)]
    public async Task Peek_ExplainsTheAthletesNumbersAndOpensTheirProfile(string path, bool touch)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = touch ? 390 : 1440, Height = 844 },
            HasTouch = touch, IsMobile = touch, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            var athlete = athletes.Single(a => (string?)a!["Name"] == "Markus Mattiasson")!;
            athlete["Placements"] = new System.Text.Json.Nodes.JsonArray(9, null, 13, 21);
            athlete["CrowdAge"] = 46;
            athlete["CrowdCount"] = 123;
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var row = page.Locator(Rows + "[data-athlete-name='Markus Mattiasson']:visible");
        await page.Clock.InstallAsync();
        var card = page.Locator("#leaderboard-peek");
        var tip = page.GetByRole(AriaRole.Tooltip);
        var score = row.Locator(".age-reduction");
        if (touch) await score.TapAsync(); else await score.HoverAsync();
        await page.Clock.RunForAsync(800);
        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(card.GetByRole(AriaRole.Link)).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("/athlete/markus-mattiasson$"));

        async Task ReadFact(string selector, string text)
        {
            var fact = card.Locator(selector);
            if (touch) await fact.TapAsync(); else await fact.HoverAsync();
            await Assertions.Expect(tip).ToContainTextAsync(text);
            await Assertions.Expect(tip).ToBeVisibleAsync();
        }
        var magnitude = (await score.InnerTextAsync()).TrimStart('-');
        await ReadFact(".lb-peek-metric", magnitude + " below");
        await ReadFact(".lb-peek-ages", "bortz clock estimated a biological age of " + await card.Locator(".is-bio").InnerTextAsync());
        await ReadFact(".lb-peek-crowd", "median of 123 accepted guesses is 46.0 years");
        await ReadFact(".lb-peek-history", "A year ago #21 · A month ago #13 · A week ago (no record) · Yesterday #9 · Today #");
        if (!touch)
        {
            await tip.HoverAsync();
            await page.Clock.RunForAsync(800);
            await Assertions.Expect(card).ToBeVisibleAsync();
        }

        var identity = card.Locator(touch ? ".lb-peek-top img" : ".lb-peek-name");
        if (touch) await identity.TapAsync(); else await identity.ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal .modal-content")).ToHaveAttributeAsync("data-athlete-slug", "markus-mattiasson");
        await Assertions.Expect(card).ToBeHiddenAsync();
        await page.GoBackAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await Assertions.Expect(row.Locator(".athlete-name")).ToBeFocusedAsync();
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
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.flagTransitions"));
    }
}
