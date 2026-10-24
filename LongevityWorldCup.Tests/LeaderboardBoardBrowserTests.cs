using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

/// <summary>
/// The /leaderboard page reads as one board: header band with underline tabs
/// and tools, the ranking's meaning, then column heads that stay in view.
/// </summary>
[Collection(BrowserTestCollections.WorkloadD)]
public sealed class LeaderboardBoardBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<(IBrowserContext Context, IPage Page)> OpenAsync(int width, int height, bool dark, string path = "/leaderboard")
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        return (context, page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 768, true)]
    [InlineData(768, 1024, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task ControlsAndResults_AreOneCardWithoutASeam(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        var geometry = await page.EvaluateAsync<double[]>(
            """
            () => {
                const controls = document.querySelector('.leaderboard-controls').getBoundingClientRect();
                const board = document.querySelector('#athleteLeaderboard').getBoundingClientRect();
                const style = getComputedStyle(document.querySelector('.leaderboard-controls'));
                return [board.top - controls.bottom, parseFloat(style.borderBottomLeftRadius), parseFloat(getComputedStyle(document.querySelector('#athleteLeaderboard')).borderTopLeftRadius),
                        Math.abs(board.left - controls.left), Math.abs(board.right - controls.right)];
            }
            """);
        Assert.True(Math.Abs(geometry[0]) <= 1, $"Controls and results are {geometry[0]}px apart.");
        Assert.Equal(0, geometry[1]);
        Assert.Equal(0, geometry[2]);
        Assert.True(geometry[3] <= 1 && geometry[4] <= 1, "Controls and results must share both edges.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 768, false)]
    [InlineData(768, 1024, true)]
    [InlineData(390, 844, false)]
    public async Task RankingTabs_UnderlineOnTheBandEdge(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        var result = await page.EvaluateAsync<string[]>(
            """
            () => {
                const tab = document.querySelector('label[for="view-ultimate"]');
                const strip = document.querySelector('.view-switcher-chips');
                const style = getComputedStyle(tab);
                const stripBox = strip.getBoundingClientRect();
                return [style.backgroundColor, style.boxShadow, String(Math.abs(tab.getBoundingClientRect().bottom - stripBox.bottom)), getComputedStyle(strip).backgroundColor, getComputedStyle(strip).borderTopWidth];
            }
            """);
        Assert.Equal("rgba(0, 0, 0, 0)", result[0]);
        Assert.Contains("inset", result[1]);
        Assert.Contains("-3px", result[1]);
        Assert.DoesNotContain("0px 0px 0px 1px", result[1]);
        Assert.True(double.Parse(result[2], System.Globalization.CultureInfo.InvariantCulture) <= 1);
        Assert.Equal("rgba(0, 0, 0, 0)", result[3]);
        Assert.Equal("0px", result[4]);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1024, 768)]
    public async Task ToolButtons_ShareOneShapeAndEdge(int width, int height)
    {
        var (context, page) = await OpenAsync(width, height, false);
        await using var _ = context;
        var boxes = await page.EvaluateAsync<double[][]>(
            """
            () => ['.leaderboard-group-switcher .view-badge', '#leaderboardOrderToggle', '.leaderboard-controls .sidebar-toggle', '#athleteSearch'].map(selector => {
                const element = document.querySelector(selector);
                const box = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                return [box.height, parseFloat(style.borderTopLeftRadius), parseFloat(style.borderTopWidth), box.right, box.top];
            }).concat([[document.querySelector('.leaderboard-toolbar').getBoundingClientRect().right - parseFloat(getComputedStyle(document.querySelector('.leaderboard-toolbar')).paddingRight)]])
            """);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(44, boxes[i][0], 0.5);
            Assert.Equal(1, boxes[i][2], 0.1);
            Assert.True(Math.Abs(boxes[i][4] - boxes[3][4]) <= 1, "Search and its tools are one bar on one line.");
        }
        var bandEnd = boxes[4][0];
        Assert.True(Math.Abs(boxes[2][3] - bandEnd) <= 1, $"Filters end at {boxes[2][3]}, the band at {bandEnd}.");
        var tabBottom = await page.Locator("label[for='view-ultimate']").EvaluateAsync<double>("tab => tab.getBoundingClientRect().bottom");
        if (width <= 1100)
            Assert.True(boxes[3][4] >= tabBottom, "Search moves to its own row under the tabs.");
        else
            Assert.True(boxes[3][4] < tabBottom, "Search shares the tab row on wide screens.");
    }

    [Theory]
    [InlineData(1440, 900, false, 100)]
    [InlineData(1440, 900, true, 200)]
    [InlineData(1100, 900, false, 200)]
    [InlineData(901, 900, true, 200)]
    [InlineData(769, 900, false, 200)]
    [InlineData(768, 1024, true, 100)]
    [InlineData(481, 900, false, 200)]
    [InlineData(480, 900, true, 200)]
    [InlineData(390, 844, false, 100)]
    [InlineData(390, 844, true, 200)]
    public async Task ColumnHeads_StayUnderTheSiteHeaderWhileRowsScroll(int width, int height, bool dark, int textPercent)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync(
            "() => { document.documentElement.style.scrollBehavior = 'auto'; const row = document.querySelector('#athleteLeaderboard tbody tr:nth-child(40)'); scrollTo(0, row.getBoundingClientRect().top + scrollY - 300); }");
        // Text resizing updates the header through ResizeObserver. Wait for
        // that measured offset, then sample both edges in the same layout.
        await page.WaitForFunctionAsync("""
            () => {
                const header = document.getElementById('site-sticky-header');
                const height = header.getBoundingClientRect().height;
                const offset = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--site-sticky-height'));
                return header.classList.contains('visible') && height > 0 && Math.abs(offset - height) < 0.1;
            }
            """);
        var edges = await page.EvaluateAsync<double[]>("""
            () => [document.querySelectorAll('#athleteLeaderboard thead th')[1].getBoundingClientRect().top,
                document.getElementById('site-sticky-header').getBoundingClientRect().bottom]
            """);
        Assert.InRange(edges[0] - edges[1], -1, 1);
        var covered = await page.EvaluateAsync<bool>(
            "() => { const th = document.querySelectorAll('#athleteLeaderboard thead th')[1]; const box = th.getBoundingClientRect(); const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2); return !th.contains(hit); }");
        Assert.False(covered, "Rows must pass under the held column heads.");
        if (width > 768 && height > 480)
        {
            var headingHeight = await page.Locator("#athleteLeaderboard thead").EvaluateAsync<double>("head => head.getBoundingClientRect().height");
            var railHeight = await page.Locator(".sidebar-rail-icon").EvaluateAsync<double>("icon => icon.getBoundingClientRect().height");
            Assert.InRange(railHeight - headingHeight, -1, 1);
        }
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(901)]
    [InlineData(769)]
    [InlineData(390)]
    public async Task FontEnlargement_RefreshesTheStickyOffsetBeforeResizeObservers(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(
            """
            window.__holdHeaderResize = false;
            window.__heldHeaderResizes = [];
            const NativeResizeObserver = window.ResizeObserver;
            window.ResizeObserver = class extends NativeResizeObserver {
                constructor(callback) {
                    super((entries, observer) => {
                        const headerChanged = entries.some(entry => entry.target.matches(
                            'header[role="banner"], header[role="banner"] .header-link, #site-sticky-header, .join-game'));
                        if (window.__holdHeaderResize && headerChanged)
                            window.__heldHeaderResizes.push(() => callback(entries, observer));
                        else callback(entries, observer);
                    });
                }
            };
            """);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await page.EvaluateAsync(
            """
            () => {
                window.__holdHeaderResize = true;
                document.documentElement.style.fontSize = '200%';
                document.documentElement.style.scrollBehavior = 'auto';
                const row = document.querySelector('#athleteLeaderboard tbody tr:nth-child(40)');
                scrollTo(0, row.getBoundingClientRect().top + scrollY - 300);
            }
            """);
        await page.WaitForFunctionAsync(
            "() => window.__heldHeaderResizes.length > 0 && document.querySelector('#site-sticky-header').classList.contains('visible')");
        var gap = await page.EvaluateAsync<double>(
            "() => document.querySelectorAll('#athleteLeaderboard thead th')[1].getBoundingClientRect().top - document.querySelector('#site-sticky-header').getBoundingClientRect().bottom");
        Assert.InRange(gap, -1, 1);
    }

    [Fact]
    public async Task WideRows_ShowMoreAthletesPerScreen()
    {
        var (context, page) = await OpenAsync(1440, 900, false);
        await using var _ = context;
        var heights = await page.EvaluateAsync<double[]>(
            "() => [...document.querySelectorAll('#athleteLeaderboard tbody tr[data-athlete-name]')].slice(0, 20).map(row => row.getBoundingClientRect().height)");
        Assert.All(heights, height => Assert.InRange(height, 44, 60));
        var head = await page.Locator("#athleteLeaderboard thead th").Nth(3).EvaluateAsync<string[]>(
            "th => [getComputedStyle(th).textTransform, getComputedStyle(th).whiteSpace, String(th.getBoundingClientRect().height)]");
        Assert.Equal("uppercase", head[0]);
        Assert.True(await page.Locator(".leaderboard-metric-link").EvaluateAsync<bool>("link => link.getBoundingClientRect().height >= 44"));
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    public async Task FlagTeams_UseMedalsCompactRowsAndADrawnChevron(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark, "/leaderboard?group=flags");
        await using var _ = context;
        var teams = await page.EvaluateAsync<string[][]>(
            """
            () => [...document.querySelectorAll('.flag-leaderboard > .flag-team')].slice(0, 4).map(team => {
                const rank = getComputedStyle(team.querySelector('.flag-team-rank'));
                const chevron = team.querySelector('.flag-team-chevron');
                return [rank.backgroundColor, String(team.querySelector('summary').getBoundingClientRect().height),
                        getComputedStyle(chevron).fontSize, String(chevron.getBoundingClientRect().width), getComputedStyle(chevron, '::before').borderRightStyle];
            })
            """);
        Assert.Equal("rgb(245, 197, 66)", teams[0][0]);
        Assert.Equal("rgb(201, 210, 218)", teams[1][0]);
        Assert.Equal("rgb(219, 160, 109)", teams[2][0]);
        Assert.Equal("rgba(0, 0, 0, 0)", teams[3][0]);
        foreach (var team in teams)
        {
            Assert.True(double.Parse(team[1], System.Globalization.CultureInfo.InvariantCulture) <= 84, $"A flag row is {team[1]}px tall.");
            Assert.Equal("0px", team[2]);
            Assert.Equal("32", team[3]);
            Assert.Equal("solid", team[4]);
        }
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1024, 768, false)]
    [InlineData(390, 844, true)]
    public async Task NoResults_IsOneCalmCardWithItsWayBack(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        await page.Locator("#athleteSearch").FillAsync("zzqx");
        var state = page.Locator(".no-results-empty-state");
        await Assertions.Expect(state).ToBeVisibleAsync();
        var style = await state.EvaluateAsync<string[]>(
            "element => { const s = getComputedStyle(element); return [s.borderTopStyle, s.borderLeftWidth, s.borderRightWidth, getComputedStyle(element, '::before').content]; }");
        Assert.Equal("dashed", style[0]);
        Assert.Equal(style[2], style[1]);
        Assert.NotEqual("none", style[3]);
        Assert.DoesNotContain("\uf002", await state.AriaSnapshotAsync());
        await page.Locator(".show-all-athletes-btn").ClickAsync();
        await Assertions.Expect(state).ToHaveCountAsync(0);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task HomepageEmbed_KeepsItsOwnTableAndPortraitSizing()
    {
        var (context, page) = await OpenAsync(1440, 900, false, "/");
        await using var _ = context;
        var fullPageOffset = await page.Locator("main").EvaluateAsync<string>(
            "main => getComputedStyle(main).getPropertyValue('--board-sticky-top').trim()");
        Assert.Empty(fullPageOffset);
        var headerPosition = await page.Locator("#athleteLeaderboard thead th").First.EvaluateAsync<string>("head => getComputedStyle(head).position");
        Assert.DoesNotContain(headerPosition, new[] { "sticky", "fixed" });
        var portraitWidth = await page.Locator("#athleteLeaderboard img.portrait").First.EvaluateAsync<double>(
            "image => image.getBoundingClientRect().width");
        Assert.InRange(portraitWidth, 47, 49);
    }

    [Theory]
    [InlineData(320, 100)]
    [InlineData(320, 200)]
    [InlineData(390, 200)]
    [InlineData(769, 200)]
    public async Task ExpandedFlagTeams_KeepWholeNamesCircularMedalsAndLargeDisclosures(int width, int textPercent)
    {
        var (context, page) = await OpenAsync(width, 1000, false, "/leaderboard?group=flags");
        await using var _ = context;
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        var summary = page.Locator(".flag-team summary").First;
        await summary.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(page.Locator(".flag-team").First).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(summary).ToBeFocusedAsync();
        await page.Locator(".flag-team-name > span:last-child").First.EvaluateAsync("name => name.textContent = 'Liechtenstein'");
        await page.Locator(".flag-athlete-name").First.EvaluateAsync("name => name.firstChild.textContent = 'Alexandria Montgomery'");
        var brokenWords = await page.Locator(".flag-team[open] :is(.flag-team-name > span:last-child, .flag-athlete-name)").EvaluateAllAsync<string[]>("""
            names => names.flatMap(name => {
                const node = name.firstChild;
                return [...node.textContent.matchAll(/\p{Letter}+/gu)].flatMap(word => {
                    const range = document.createRange();
                    range.setStart(node, word.index);
                    range.setEnd(node, word.index + word[0].length);
                    return [...range.getClientRects()].filter(rect => rect.width && rect.height).length > 1 ? [word[0]] : [];
                });
            })
            """);
        Assert.Empty(brokenWords);
        var dimensions = await summary.EvaluateAsync<double[]>("""
            summary => {
                const box = summary.getBoundingClientRect();
                const medal = summary.querySelector('.flag-team-rank').getBoundingClientRect();
                return [box.width, box.height, Math.abs(medal.width - medal.height)];
            }
            """);
        Assert.True(dimensions[0] >= 44 && dimensions[1] >= 44, "The whole summary is the disclosure action.");
        Assert.InRange(dimensions[2], 0, 1);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(481)]
    [InlineData(769)]
    [InlineData(1024)]
    public async Task RankingTabs_EnlargedTextKeepsWholeWordsAndLargeActions(int width)
    {
        var (context, page) = await OpenAsync(width, 900, false);
        await using var _ = context;
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        foreach (var view in new[] { "ultimate", "pheno" })
        {
            await page.Locator($"label[for='view-{view}']").ClickAsync();
            var issues = await page.Locator(".leaderboard-view-switcher :is(.view-badge, .view-switcher-label)").EvaluateAllAsync<string[]>("""
                labels => labels.flatMap(label => {
                    const box = label.getBoundingClientRect();
                    // Phones keep the Ranking label for screen readers only.
                    if (label.matches('.view-switcher-label') && box.width <= 1) return [];
                    const issues = [];
                    if (label.matches('.view-badge') && (box.width < 44 || box.height < 44) || box.right > innerWidth + 1) issues.push(label.textContent);
                    const walker = document.createTreeWalker(label, NodeFilter.SHOW_TEXT);
                    while (walker.nextNode()) {
                        const node = walker.currentNode;
                        for (const word of node.textContent.matchAll(/\p{Letter}+/gu)) {
                            const range = document.createRange();
                            range.setStart(node, word.index);
                            range.setEnd(node, word.index + word[0].length);
                            const rects = [...range.getClientRects()].filter(rect => rect.width && rect.height);
                            if (rects.length > 1 || rects.some(rect => rect.left < box.left - 1 || rect.right > box.right + 1)) issues.push(word[0]);
                        }
                    }
                    return issues;
                })
                """);
            Assert.Empty(issues);
        }
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320)]
    [InlineData(481)]
    [InlineData(769)]
    [InlineData(1024)]
    public async Task Search_EnlargedTextKeepsItsLineBoxAndFocusCue(int width)
    {
        var (context, page) = await OpenAsync(width, 900, false);
        await using var _ = context;
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        var sizing = await page.Locator("#athleteSearch").EvaluateAsync<double[]>("""
            input => {
                const style = getComputedStyle(input);
                return [input.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom), parseFloat(style.lineHeight)];
            }
            """);
        Assert.True(sizing[0] >= sizing[1] - 1, $"Search has {sizing[0]}px for its {sizing[1]}px line box.");
        await page.Locator("#athleteSearch").FocusAsync();
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveCSSAsync("border-color", "rgb(8, 118, 133)");
        await Assertions.Expect(page.Locator(".search-icon")).ToHaveCSSAsync("color", "rgb(8, 118, 133)");
    }

    [Theory]
    [InlineData(481)]
    [InlineData(901)]
    [InlineData(1100)]
    public async Task EnlargedColumnLabels_KeepTheirWordsInsideTheirAssociatedColumns(int width)
    {
        var (context, page) = await OpenAsync(width, 900, false);
        await using var _ = context;
        await context.AddInitScriptAsync("document.addEventListener('DOMContentLoaded', () => document.documentElement.style.fontSize = '200%')");
        foreach (var view in new[] { "ultimate", "bortz", "pheno", "crowd", "improvement", "bortz-improvement" })
        {
            await NavigateAndSettleAsync(page, $"/leaderboard?view={view}");
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            var overflow = await page.Locator(".leaderboard table thead th").EvaluateAllAsync<string[]>("""
                heads => heads.flatMap(head => {
                    const box = head.getBoundingClientRect();
                    if (!box.width) return [];
                    const range = document.createRange();
                    range.selectNodeContents(head);
                    return [...range.getClientRects()].filter(rect => rect.width && (rect.left < box.left - 1 || rect.right > box.right + 1))
                        .map(() => head.textContent.trim());
                })
                """);
            Assert.Empty(overflow);
            Assert.True(await page.GetByRole(AriaRole.Columnheader).CountAsync() >= 3, "The ranking keeps its accessible column headers.");
            await ExpectNoHorizontalOverflowAsync(page);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlagMedals_FollowTheRankBeforeHydrationAndThroughSearch(bool javaScript)
    {
        await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = javaScript });
        var page = await context.NewPageAsync();
        foreach (var rank in new[] { "2", "4", "" })
        {
            await page.GotoAsync("/leaderboard?group=flags");
            if (javaScript) await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            var team = page.Locator($".flag-team:has(.flag-team-rank[data-rank='{rank}'])").First;
            var flag = (await team.GetAttributeAsync("data-flag"))!;
            var id = (await team.GetAttributeAsync("id"))!;
            if (javaScript)
                await page.Locator("#athleteSearch").FillAsync(flag);
            var medal = page.Locator($"#{id} .flag-team-rank");
            await Assertions.Expect(medal).ToHaveAttributeAsync("data-rank", rank);
            await Assertions.Expect(medal).ToHaveCSSAsync("background-color", rank == "2" ? "rgb(201, 210, 218)" : "rgba(0, 0, 0, 0)");
        }
    }
}
