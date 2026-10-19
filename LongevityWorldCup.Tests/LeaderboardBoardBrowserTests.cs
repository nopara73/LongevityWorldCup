using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

/// <summary>
/// The /leaderboard page reads as one board: header band with underline tabs
/// and tools, the ranking's meaning, then column heads that stay in view.
/// </summary>
[Collection(BrowserTestCollections.WorkloadB)]
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
            Assert.Equal(boxes[0][1], boxes[i][1], 0.5);
            Assert.Equal(1, boxes[i][2], 0.1);
        }
        var bandEnd = boxes[4][0];
        Assert.True(Math.Abs(boxes[2][3] - bandEnd) <= 1, $"Filters end at {boxes[2][3]}, the band at {bandEnd}.");
        Assert.True(Math.Abs(boxes[0][3] - bandEnd) <= 1 || width > 1100, "On laptops By Flag ends the first row.");
        if (width <= 1100)
            Assert.True(boxes[3][4] > boxes[0][4] + 20, "Search moves to its own row under the tabs.");
        else
            Assert.True(Math.Abs(boxes[3][4] - boxes[0][4]) <= 1, "Search shares the tab row on wide screens.");
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
        await page.WaitForTimeoutAsync(200);
        var top = await page.Locator("#athleteLeaderboard thead th").Nth(1).EvaluateAsync<double>("th => th.getBoundingClientRect().top");
        var headerBottom = await page.Locator("#site-sticky-header").EvaluateAsync<double>("header => header.getBoundingClientRect().bottom");
        Assert.InRange(top - headerBottom, -1, 1);
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
        await page.Locator(".show-all-athletes-btn").ClickAsync();
        await Assertions.Expect(state).ToHaveCountAsync(0);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task HomepageEmbed_KeepsItsOwnComposition()
    {
        var (context, page) = await OpenAsync(1440, 900, false, "/");
        await using var _ = context;
        var gap = await page.EvaluateAsync<double>(
            "() => document.querySelector('#athleteLeaderboard').getBoundingClientRect().top - document.querySelector('.leaderboard-controls').getBoundingClientRect().bottom");
        Assert.True(gap > 4, "The homepage board is outside this page's redesign.");
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
