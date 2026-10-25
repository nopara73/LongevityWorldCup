using System.Globalization;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

/// <summary>
/// On /leaderboard, By Flag reads as a nations table beside the athlete table:
/// the same row rhythm and inline scores, open teams as one edged block with
/// members on a thread, empty seats for incomplete teams, and the table's empty card.
/// </summary>
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FlagNationsTableBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<(IBrowserContext Context, IPage Page)> OpenAsync(int width, int height, bool dark, string path = "/leaderboard?group=flags", bool touch = false, bool motion = false)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = motion ? ReducedMotion.NoPreference : ReducedMotion.Reduce,
            HasTouch = touch,
            IsMobile = touch
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true')");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        return (context, page);
    }

    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(390, false)]
    [InlineData(320, true)]
    public async Task HomepageFlagPreview_TouchDisclosureLeavesNoStuckHover(int width, bool dark)
    {
        var (context, page) = await OpenAsync(width, 844, dark, "/?group=flags", touch: true);
        await using var _ = context;
        var team = page.Locator(".flag-team").Nth(3);
        await team.Locator("summary").TapAsync();
        await Assertions.Expect(team.Locator(".flag-team-athletes")).ToBeVisibleAsync();
        await team.Locator("summary").TapAsync();
        await Assertions.Expect(team.Locator(".flag-team-athletes")).ToBeHiddenAsync();
        Assert.Equal("rgba(0, 0, 0, 0)", await team.Locator("summary").EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
    }

    [Theory]
    [InlineData("/league/bortz?group=flags")]
    [InlineData("/league/improvement?group=flags")]
    [InlineData("/league/bortz-improvement?group=flags")]
    public async Task WithoutJavaScript_ServerTeamsAreImmediatelyStableAndExpandable(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            JavaScriptEnabled = false, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        Assert.Equal("none", await page.Locator("#flagLeaderboard").EvaluateAsync<string>("e => getComputedStyle(e).animationName"));
        var team = page.Locator(".flag-team").First;
        await team.Locator("summary").ClickAsync();
        await Assertions.Expect(team.Locator(".flag-team-athletes")).ToBeVisibleAsync();
        Assert.Equal("none", await team.Locator(".flag-team-athletes").EvaluateAsync<string>("e => getComputedStyle(e).animationName"));
    }

    [Theory]
    [InlineData(320, 100)]
    [InlineData(390, 100)]
    [InlineData(390, 200)]
    public async Task PhoneSelection_ChipsWrapBesideClearAllInReadingOrder(int width, int textPercent)
    {
        var (context, page) = await OpenAsync(width, 844, false, "/leaderboard?group=flags&search=zzzzqq");
        await using var _ = context;
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + '%'", textPercent);
        var result = await page.EvaluateAsync<bool>(
            """
            () => {
                const summary = document.getElementById('leaderboardSelectionSummary');
                const count = document.getElementById('leaderboardResultCount').getBoundingClientRect();
                const chips = document.getElementById('leaderboardSelectionChips').getBoundingClientRect();
                const clear = document.getElementById('clearLeaderboardSelection').getBoundingClientRect();
                return getComputedStyle(summary).display === 'grid'
                    && count.bottom <= chips.top + 1 && chips.right <= clear.left + 1
                    && Math.abs(chips.bottom - clear.bottom) < 2 && clear.width >= 44 && clear.height >= 44;
            }
            """);
        Assert.True(result, "Keep the count above the wrapping chips and Clear all beside their final line.");
        await page.Locator(".leaderboard-selection-chip").Last.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#clearLeaderboardSelection")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator(".leaderboard > table")).ToBeVisibleAsync();
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(160, 100)]
    [InlineData(320, 200)]
    [InlineData(390, 200)]
    [InlineData(768, 200)]
    public async Task NarrowAndEnlargedTeams_KeepActualScoreAndNameTextInsideTheirRows(int width, int textPercent)
    {
        var (context, page) = await OpenAsync(width, 1000, false);
        await using var _ = context;
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + '%'", textPercent);
        await page.Locator(".flag-team summary").First.ClickAsync();
        var failures = await page.EvaluateAsync<string[]>(
            """
            () => {
                const failures = [];
                for (const team of document.querySelectorAll('.flag-team')) {
                    const row = team.getBoundingClientRect();
                    for (const element of team.querySelectorAll('.flag-team-name > span:last-child, .flag-team-score, .flag-athlete-name, .flag-athlete-metric')) {
                        if (!element.checkVisibility()) continue;
                        const cell = element.getBoundingClientRect();
                        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                        for (let node; (node = walker.nextNode());) {
                            if (!node.textContent.trim()) continue;
                            const range = document.createRange(); range.selectNodeContents(node);
                            for (const glyphs of range.getClientRects())
                                if (glyphs.width && (glyphs.left < row.left - 1 || glyphs.right > row.right + 1
                                    || glyphs.left < cell.left - 1 || glyphs.right > cell.right + 1))
                                    failures.push(`${team.dataset.flag}: ${node.textContent.trim()} leaves its cell`);
                        }
                    }
                }
                return failures;
            }
            """);
        Assert.Empty(failures);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task StickyBandAndFilters_ReceivePointerInputAboveExpandedMembers()
    {
        var (context, page) = await OpenAsync(1440, 900, false);
        await using var _ = context;
        await page.Locator(".flag-team.tier-amateur summary").First.ClickAsync();
        await page.Locator(".sidebar-toggle").ClickAsync();
        await page.EvaluateAsync(
            """
            () => {
                const band = document.querySelector('.flag-section-heading.tier-separator');
                scrollTo({ top: scrollY + band.getBoundingClientRect().top + 100, behavior: 'instant' });
            }
            """);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var clear = page.Locator(".sidebar-close");
        Assert.True(await clear.EvaluateAsync<bool>(
            """
            e => {
                const b = e.getBoundingClientRect();
                return [.2, .5, .8].every(x => [.2, .5, .8].every(y =>
                    e.contains(document.elementFromPoint(b.left + b.width * x, b.top + b.height * y))));
            }
            """), "The entire filter close target must stay above the sticky band and member layers.");
        await clear.ClickAsync(new() { Position = new() { X = 22, Y = 8 } });
        await page.Mouse.MoveAsync(0, 0);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var bandLink = page.Locator(".flag-section-heading.tier-separator a");
        Assert.True(await bandLink.EvaluateAsync<bool>(
            "e => { const b = e.getBoundingClientRect(); return e.contains(document.elementFromPoint(b.left + b.width / 2, b.top + b.height / 2)); }"),
            "An expanded member's place disc must stay below the track link.");
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1024, 768, false)]
    public async Task TeamRows_ShareTheAthleteTablesRhythmZebraAndInlineScores(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        var result = await page.EvaluateAsync<string[]>(
            """
            () => {
                const teams = [...document.querySelectorAll('.flag-team:not(.tier-amateur)')];
                const summary = teams[0].querySelector('summary');
                const score = teams[0].querySelector('.flag-team-score');
                const unit = score.querySelector('small');
                const portrait = teams[0].querySelector('.flag-team-portraits img');
                return [
                    String(summary.getBoundingClientRect().height),
                    getComputedStyle(teams[0]).backgroundColor,
                    getComputedStyle(teams[1]).backgroundColor,
                    getComputedStyle(unit).display,
                    getComputedStyle(portrait).borderTopColor,
                    getComputedStyle(score).color,
                    getComputedStyle(teams[0].querySelector('.flag-team-rank')).fontFamily
                ];
            }
            """);
        Assert.InRange(Number(result[0]), 56, 60);
        Assert.NotEqual(result[1], result[2]);
        Assert.Equal("inline", result[3]);
        Assert.Equal(result[5], result[4]);
        Assert.Contains("Orbitron", result[6]);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 768, true)]
    [InlineData(768, 1024, false)]
    [InlineData(390, 844, true)]
    public async Task OpenTeam_IsOneEdgedBlockWithMembersOnAThread(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        await page.Locator(".flag-team summary").First.ClickAsync();
        await Assertions.Expect(page.Locator(".flag-team-athletes").First).ToBeVisibleAsync();
        var result = await page.EvaluateAsync<string[]>(
            """
            () => {
                const team = document.querySelector('.flag-team[open]');
                const list = team.querySelector('.flag-team-athletes');
                const thread = getComputedStyle(list, '::before');
                const places = [...team.querySelectorAll('.flag-athlete-rank')];
                const threadLeft = list.getBoundingClientRect().left + parseFloat(thread.left) + parseFloat(thread.width) / 2;
                const placeCentres = places.map(place => { const box = place.getBoundingClientRect(); return box.left + box.width / 2; });
                const score = team.querySelector('.flag-team-score').getBoundingClientRect();
                const metrics = [...team.querySelectorAll('.flag-athlete-metric')].map(metric => metric.getBoundingClientRect().right);
                const icon = team.querySelector('.flag-team-icon').getBoundingClientRect();
                return [
                    getComputedStyle(team).boxShadow,
                    getComputedStyle(team.querySelector('summary')).boxShadow,
                    thread.content,
                    String(Math.max(...placeCentres.map(centre => Math.abs(centre - threadLeft)))),
                    String(Math.abs(threadLeft - (icon.left + icon.width / 2))),
                    getComputedStyle(places[0]).borderTopLeftRadius,
                    String(innerWidth > 440 ? Math.max(...metrics.map(right => Math.abs(right - score.right))) : 0),
                    getComputedStyle(team.querySelector('.flag-athlete-link img')).borderTopWidth
                ];
            }
            """);
        Assert.Contains("inset", result[0]);
        Assert.Contains("3px 0px 0px 0px", result[0]);
        Assert.Contains("3px 0px 0px 0px", result[1]);
        Assert.Equal("\"\"", result[2]);
        Assert.True(Number(result[3]) <= 1.5, $"Member places sit {result[3]}px off the thread.");
        Assert.True(Number(result[4]) <= 1.5, $"The thread hangs {result[4]}px off the flag.");
        Assert.Equal("50%", result[5]);
        Assert.True(Number(result[6]) <= 1.5, $"Member scores end {result[6]}px away from the team score.");
        Assert.Equal("3px", result[7]);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1280, 720, true)]
    public async Task IncompleteTeams_ShowTheirEmptySeatsAndAQuieterCount(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        var result = await page.EvaluateAsync<string[]>(
            """
            () => {
                const seats = portraits => ['::before', '::after']
                    .map(pseudo => getComputedStyle(portraits, pseudo))
                    .filter(style => style.content !== 'none' && style.borderTopStyle === 'dashed').length;
                const unranked = [...document.querySelectorAll('.flag-team')].filter(team => team.querySelector('.flag-team-rank').dataset.rank === '');
                const two = unranked.find(team => team.querySelectorAll('.flag-team-portraits img').length === 2);
                const one = unranked.find(team => team.querySelectorAll('.flag-team-portraits img').length === 1);
                const full = document.querySelector('.flag-team .flag-team-portraits');
                const ranked = document.querySelector('.flag-team .flag-team-score');
                return [String(seats(two.querySelector('.flag-team-portraits'))), String(seats(one.querySelector('.flag-team-portraits'))), String(seats(full)),
                        getComputedStyle(two.querySelector('.flag-team-score')).color, getComputedStyle(ranked).color];
            }
            """);
        Assert.Equal("1", result[0]);
        Assert.Equal("2", result[1]);
        Assert.Equal("0", result[2]);
        Assert.NotEqual(result[4], result[3]);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    public async Task SectionBands_StayInViewUnderTheSiteHeader(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        var result = await page.EvaluateAsync<double[]>(
            """
            async () => {
                const band = document.querySelector('.flag-section-heading.tier-separator');
                const next = band.nextElementSibling.nextElementSibling.nextElementSibling;
                document.documentElement.style.scrollBehavior = 'auto';
                scrollTo(0, next.getBoundingClientRect().top + scrollY - 20);
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                const stickyTop = parseFloat(getComputedStyle(document.querySelector('main[data-leaderboard-page="full"]')).getPropertyValue('--board-sticky-top'));
                return [band.getBoundingClientRect().top, stickyTop, getComputedStyle(band).position === 'sticky' ? 1 : 0];
            }
            """);
        Assert.Equal(1, result[2]);
        Assert.True(result[0] >= 0 && Math.Abs(result[0] - result[1]) <= 2, $"The Amateur band sits at {result[0]}px, under a header ending at {result[1]}px.");
        if (width > 768)
        {
            // A stuck band must stay beneath the filter panel that opens over the results.
            await page.Locator(".sidebar-toggle").ClickAsync();
            await page.Locator(".sidebar-close").ClickAsync(new() { Timeout = 5000 });
            await Assertions.Expect(page.Locator(".sidebar")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("expanded"));
        }
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task EmptySearch_UsesTheAthleteTablesEmptyCard(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark, "/leaderboard?group=flags&search=zzzzqq");
        await using var _ = context;
        var empty = page.Locator(".flag-empty");
        await Assertions.Expect(empty).ToHaveTextAsync("No flags match this leaderboard.");
        var art = empty.Locator(":scope > svg.lwc-empty__art");
        await Assertions.Expect(art).ToHaveCountAsync(1);
        await Assertions.Expect(art).ToHaveAttributeAsync("aria-hidden", "true");
        await Assertions.Expect(art).ToHaveAttributeAsync("focusable", "false");
        var result = await empty.EvaluateAsync<string[]>(
            """
            card => {
                const board = document.getElementById('flagLeaderboard').getBoundingClientRect();
                const box = card.getBoundingClientRect();
                return [getComputedStyle(card).borderTopStyle, getComputedStyle(card, '::before').content, getComputedStyle(card).fontWeight,
                        String(Math.abs((box.left - board.left) - (board.right - box.right)))];
            }
            """);
        Assert.Equal("dashed", result[0]);
        Assert.Equal("none", result[1]);
        Assert.Equal("700", result[2]);
        Assert.True(Number(result[3]) <= 2, "The empty card is centred in the board.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(320, 640, true)]
    public async Task Phones_KeepMembersReadableAndTapsLeaveNoStuckTint(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark, touch: true);
        await using var _ = context;
        var team = page.Locator(".flag-team").Nth(3);
        await team.Locator("summary").TapAsync();
        await Assertions.Expect(team.Locator(".flag-team-athletes")).ToBeVisibleAsync();
        await team.Locator("summary").TapAsync();
        await Assertions.Expect(team.Locator(".flag-team-athletes")).ToBeHiddenAsync();
        Assert.Equal("rgba(0, 0, 0, 0)", await team.Locator("summary").EvaluateAsync<string>("summary => getComputedStyle(summary).backgroundColor"));
        await page.Locator(".flag-team summary").First.TapAsync();
        var failures = await page.EvaluateAsync<string[]>(
            """
            () => {
                const failures = [];
                const team = document.querySelector('.flag-team[open]');
                const row = team.getBoundingClientRect();
                for (const link of team.querySelectorAll('.flag-athlete-link')) {
                    const name = link.querySelector('.flag-athlete-name').getBoundingClientRect();
                    const metric = link.querySelector('.flag-athlete-metric').getBoundingClientRect();
                    if (name.width < 80) failures.push(`name squeezed to ${name.width}px`);
                    if (metric.right > row.right + 1) failures.push('score leaves the row');
                    if (name.right > metric.left + 1 && name.bottom > metric.top + 1 && name.top < metric.bottom - 1) failures.push('name overlaps score');
                }
                return failures;
            }
            """);
        Assert.True(failures.Length == 0, string.Join("; ", failures));
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task SwitchingToFlags_SettlesTheNationsTableIn_UnlessMotionIsReduced()
    {
        var (context, page) = await OpenAsync(1280, 900, false, "/leaderboard", motion: true);
        await using (context)
        {
            await page.Locator("label[for=group-flags]").ClickAsync();
            await Assertions.Expect(page.Locator(".flag-team").First).ToBeVisibleAsync();
            Assert.Equal("lwc-flag-board-in", await page.Locator("#flagLeaderboard").EvaluateAsync<string>("board => getComputedStyle(board).animationName"));
            await page.Locator(".flag-team summary").First.ClickAsync();
            Assert.Equal("lwc-flag-members-in", await page.Locator(".flag-team-athletes").First.EvaluateAsync<string>("list => getComputedStyle(list).animationName"));
        }

        var (reduced, reducedPage) = await OpenAsync(1280, 900, false, "/leaderboard");
        await using (reduced)
        {
            await reducedPage.Locator("label[for=group-flags]").ClickAsync();
            await Assertions.Expect(reducedPage.Locator(".flag-team").First).ToBeVisibleAsync();
            Assert.Equal("none", await reducedPage.Locator("#flagLeaderboard").EvaluateAsync<string>("board => getComputedStyle(board).animationName"));
        }
    }
}
