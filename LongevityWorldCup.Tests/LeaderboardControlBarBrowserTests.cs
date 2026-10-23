using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

/// <summary>
/// On /leaderboard, search, By Flag, Order and Filters are one joined control
/// bar; the Order composer opens as a strip inside the card; phones lose the
/// title row; and phone and tablet column heads keep the board's title voice.
/// </summary>
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LeaderboardControlBarBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string ReadBar =
        """
        () => {
            const pick = s => document.querySelector(s);
            const segments = ['#athleteSearch', 'label[for="group-flags"]', '#leaderboardOrderToggle', '.leaderboard-controls .sidebar-toggle']
                .map(pick).filter(e => e && e.getClientRects().length);
            return segments.map(e => {
                const r = e.getBoundingClientRect(), s = getComputedStyle(e);
                return [r.left, r.right, r.top, r.bottom, parseFloat(s.borderTopLeftRadius), parseFloat(s.borderTopRightRadius)];
            });
        }
        """;

    private async Task<(IBrowserContext Context, IPage Page)> OpenAsync(int width, int height, bool dark)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator("#leaderboardOrderToggle")).ToBeEnabledAsync();
        return (context, page);
    }

    private static void AssertJoinedBar(double[][] segments, int expected, string state)
    {
        Assert.Equal(expected, segments.Length);
        for (var i = 1; i < segments.Length; i++)
        {
            Assert.True(Math.Abs(segments[i][0] - segments[i - 1][1]) <= 1.5, $"{state}: segment {i} is {segments[i][0] - segments[i - 1][1]}px from its neighbour.");
            Assert.True(Math.Abs(segments[i][2] - segments[0][2]) <= 1 && Math.Abs(segments[i][3] - segments[0][3]) <= 1, $"{state}: segment {i} is off the bar's line.");
        }
        Assert.True(segments[0][4] > 0, $"{state}: the bar's start must be rounded.");
        Assert.True(segments[^1][5] > 0, $"{state}: the bar's end must be rounded.");
        Assert.Equal(0, segments[0][5]);
        Assert.Equal(0, segments[^1][4]);
        for (var i = 1; i < segments.Length - 1; i++)
            Assert.True(segments[i][4] == 0 && segments[i][5] == 0, $"{state}: inner segment {i} must be square.");
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 768, true)]
    [InlineData(768, 1024, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task SearchFlagOrderAndFilters_AreOneJoinedBar(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;

        AssertJoinedBar(await page.EvaluateAsync<double[][]>(ReadBar), 4, "default");

        // By Flag hides Order; the bar closes up around the remaining segments.
        await page.Locator("label[for='group-flags']").ClickAsync();
        await Assertions.Expect(page.Locator("#group-flags")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#leaderboardOrderToggle")).ToBeHiddenAsync();
        AssertJoinedBar(await page.EvaluateAsync<double[][]>(ReadBar), 3, "by flag");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(320, 640, true)]
    [InlineData(768, 1024, true)]
    public async Task Phone_TabsOpenTheCardAndRunEdgeToEdge(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        var result = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.querySelector('.leaderboard-controls').getBoundingClientRect();
                const label = document.querySelector('.view-switcher-label').getBoundingClientRect();
                const tabs = document.querySelector('.leaderboard-view-switcher').getBoundingClientRect();
                const tab = document.querySelector('label[for="view-ultimate"]').getBoundingClientRect();
                const search = document.querySelector('#athleteSearch').getBoundingClientRect();
                return [label.width * label.height, tab.top - card.top, Math.abs(tabs.left - card.left), Math.abs(card.right - tabs.right), search.top - tabs.bottom];
            }
            """);
        Assert.True(result[0] <= 1, "The Ranking label is a redundant row on phones; it stays for screen readers only.");
        Assert.True(result[1] <= 2, $"The tabs start {result[1]}px below the card's top edge.");
        Assert.True(result[2] <= 2 && result[3] <= 2, $"The tab strip must reach both card edges ({result[2]}, {result[3]}).");
        Assert.InRange(result[4], 0, 20);
        await Assertions.Expect(page.Locator("#view-ultimate")).ToHaveAccessibleNameAsync("Ultimate ranking");
        await Assertions.Expect(page.Locator(".leaderboard-view-switcher")).ToHaveAccessibleNameAsync("Ranking");
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1024, 768, true)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task OrderComposer_OpensAsAStripInsideTheCard(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        var statusBefore = await ReadElementRectAsync(page, ".leaderboard-controls-status");

        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        await Assertions.Expect(page.Locator("#leaderboardOrderPanel")).ToBeVisibleAsync();
        var result = await page.EvaluateAsync<double[]>(
            """
            () => {
                const panel = document.querySelector('#leaderboardOrderPanel');
                const p = panel.getBoundingClientRect();
                const card = document.querySelector('.leaderboard-controls').getBoundingClientRect();
                const bar = document.querySelector('#athleteSearch').getBoundingClientRect();
                const status = document.querySelector('.leaderboard-controls-status').getBoundingClientRect();
                const rows = [...panel.querySelectorAll('.leaderboard-order-row')].map(r => r.getBoundingClientRect());
                const s = getComputedStyle(panel);
                return [Math.abs(p.left - card.left), Math.abs(card.right - p.right), p.top - bar.bottom, status.top - p.bottom,
                        s.position === 'static' ? 1 : 0, s.boxShadow === 'none' ? 1 : 0, Math.abs(rows[0].top - rows[1].top)];
            }
            """);
        Assert.True(result[0] <= 1.5 && result[1] <= 1.5, $"The strip must span the card ({result[0]}, {result[1]}).");
        Assert.InRange(result[2], 0, 20);
        Assert.True(Math.Abs(result[3]) <= 1, $"The status band must follow the strip, not hide under it ({result[3]}).");
        Assert.Equal(1, result[4]);
        Assert.Equal(1, result[5]);
        if (width > 768) Assert.True(result[6] <= 1, "On wide screens Order by and Then by read as one line.");
        else Assert.True(result[6] >= 40, "On phones Order by and Then by stack.");
        await ExpectNoHorizontalOverflowAsync(page);

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#leaderboardOrderPanel")).ToBeHiddenAsync();
        var statusAfter = await ReadElementRectAsync(page, ".leaderboard-controls-status");
        Assert.True(Math.Abs(statusAfter.Top - statusBefore.Top) <= 1, "Closing the strip returns the board to where it was.");
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(768, 1024, true)]
    public async Task PhoneAndTabletColumnHeads_KeepTheBoardTitleVoice(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        var result = await page.EvaluateAsync<string[]>(
            """
            () => {
                const heads = [...document.querySelectorAll('#athleteLeaderboard thead th')].filter(th => th.getClientRects().length);
                const rank = heads[0].getBoundingClientRect().width;
                return [[...new Set(heads.map(th => getComputedStyle(th).textTransform))].join(','), String(rank)];
            }
            """);
        Assert.Equal("uppercase", result[0]);
        Assert.True(double.Parse(result[1], System.Globalization.CultureInfo.InvariantCulture) <= 72, $"The rank column is {result[1]}px wide.");
    }
}
