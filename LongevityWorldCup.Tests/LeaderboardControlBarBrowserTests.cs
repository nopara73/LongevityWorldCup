using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

/// <summary>
/// On the homepage and /leaderboard, search, By Flag, Order and Filters are one joined control
/// bar; the Order composer opens as a strip inside the card; phones lose the
/// title row; and tablet rows keep the rank column narrow.
/// </summary>
[Collection(BrowserTestCollections.WorkloadD)]
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
    [InlineData("/leaderboard", 1440, 900, false)]
    [InlineData("/", 1440, 900, false)]
    [InlineData("/leaderboard", 1024, 768, true)]
    [InlineData("/", 1024, 768, true)]
    [InlineData("/leaderboard", 768, 1024, false)]
    [InlineData("/", 768, 1024, false)]
    [InlineData("/leaderboard", 390, 844, true)]
    [InlineData("/", 390, 844, true)]
    [InlineData("/leaderboard", 320, 640, false)]
    [InlineData("/", 320, 640, false)]
    public async Task SearchFlagOrderAndFilters_AreOneJoinedBar(string path, int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark, path);
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
    [InlineData("/leaderboard", 390, 844, false)]
    [InlineData("/", 390, 844, false)]
    [InlineData("/leaderboard", 320, 640, true)]
    [InlineData("/", 320, 640, true)]
    [InlineData("/leaderboard", 768, 1024, true)]
    [InlineData("/", 768, 1024, true)]
    public async Task Phone_TabsOpenTheCardAndRunEdgeToEdge(string path, int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark, path);
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
    [InlineData("/leaderboard", 1440, 900, false)]
    [InlineData("/", 1440, 900, false)]
    [InlineData("/leaderboard", 1024, 768, true)]
    [InlineData("/", 1024, 768, true)]
    [InlineData("/leaderboard", 390, 844, true)]
    [InlineData("/", 390, 844, true)]
    [InlineData("/leaderboard", 320, 640, false)]
    [InlineData("/", 320, 640, false)]
    public async Task OrderComposer_OpensAsAStripInsideTheCard(string path, int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark, path);
        await using var _ = context;
        var statusBefore = await ReadElementRectAsync(page, ".leaderboard-controls-status");
        var documentTopBefore = statusBefore.Top + await page.EvaluateAsync<double>("scrollY");

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
        var documentTopAfter = statusAfter.Top + await page.EvaluateAsync<double>("scrollY");
        Assert.True(Math.Abs(documentTopAfter - documentTopBefore) <= 1,
            "Closing the strip restores the board's document position, including when the homepage scrolls to its controls.");
    }

    [Theory]
    [InlineData(768, 1024, true)]
    [InlineData(600, 900, false)]
    public async Task TabletRows_KeepTheRankColumnNarrow(int width, int height, bool dark)
    {
        var (context, page) = await OpenAsync(width, height, dark);
        await using var _ = context;
        var widths = await page.EvaluateAsync<double[]>(
            """
            () => {
                const row = document.querySelector('#athleteLeaderboard tbody tr[data-athlete-name]:not([style*="none"])');
                return [document.querySelector('#athleteLeaderboard thead th').getBoundingClientRect().width, row.querySelector('.rank-td').getBoundingClientRect().width];
            }
            """);
        Assert.True(widths[0] <= 72 && widths[1] <= 72, $"The rank column is {widths[0]}px / {widths[1]}px wide.");
    }

    [Theory]
    [InlineData("/leaderboard", 320, 100)]
    [InlineData("/", 320, 100)]
    [InlineData("/leaderboard", 320, 200)]
    [InlineData("/", 320, 200)]
    [InlineData("/leaderboard", 390, 200)]
    [InlineData("/", 390, 200)]
    [InlineData("/leaderboard", 768, 200)]
    [InlineData("/", 768, 200)]
    public async Task NativeSelections_KeepEveryCatalogLabelReadableAtEnlargedText(string path, int width, int textPercent)
    {
        var (context, page) = await OpenAsync(width, 844, true, path);
        await using var _ = context;
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = `${size}%`", textPercent);
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        var primary = page.Locator("#leaderboardOrderPrimary");
        await Assertions.Expect(primary).ToHaveAccessibleNameAsync("Order by");
        var values = await primary.Locator("option").EvaluateAllAsync<string[]>("options => options.map(option => option.value)");
        foreach (var value in values)
        {
            await primary.SelectOptionAsync(value);
            var diagnostics = await primary.EvaluateAsync<string[]>("""
                select => {
                    const label = select.parentElement.querySelector('.order-selected-value');
                    const bounds = select.getBoundingClientRect();
                    const chevron = select.parentElement.querySelector('.order-select-chevron').getBoundingClientRect();
                    const range = document.createRange(); range.selectNodeContents(label);
                    const clipped = [...range.getClientRects()].some(r => r.width > 0 &&
                        (r.left < bounds.left - 1 || r.right > bounds.right + 1 || r.top < bounds.top - 1 || r.bottom > bounds.bottom + 1));
                    const overlapsChevron = [...range.getClientRects()].some(r => r.width > 0 && r.right > chevron.left + 1);
                    return [select.selectedOptions[0].textContent, label.textContent, String(clipped), label.getAttribute('aria-hidden'), String(overlapsChevron)];
                }
                """);
            Assert.Equal(diagnostics[0], diagnostics[1]);
            Assert.Equal("false", diagnostics[2]);
            Assert.Equal("true", diagnostics[3]);
            Assert.False(bool.Parse(diagnostics[4]), $"'{diagnostics[0]}' overlaps its dropdown arrow at {width}px and {textPercent}% text.");
        }
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/leaderboard", 390)]
    [InlineData("/", 390)]
    [InlineData("/leaderboard", 1440)]
    [InlineData("/", 1440)]
    public async Task JoinedBar_KeyboardOrderMatchesTheVisibleSegments(string path, int width)
    {
        var (context, page) = await OpenAsync(width, 900, false, path);
        await using var _ = context;
        await page.Locator("#view-ultimate").FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#athleteSearch")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#group-flags")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("#leaderboardOrderToggle")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".leaderboard-controls .sidebar-toggle")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("/leaderboard", 844, 390)]
    [InlineData("/", 844, 390)]
    [InlineData("/leaderboard", 640, 240)]
    [InlineData("/", 640, 240)]
    public async Task ShortLandscape_TabTraversalKeepsEveryNativeControlVisible(string path, int width, int height)
    {
        var (context, page) = await OpenAsync(width, height, false, path + "?sort=pheno&then=age");
        await using var _ = context;
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        await page.Keyboard.PressAsync("Shift+Tab");

        async Task ExpectVisibleFocusAsync(string id)
        {
            var control = page.Locator("#" + id);
            await Assertions.Expect(control).ToBeFocusedAsync();
            await Assertions.Expect(control).ToBeInViewportAsync(new() { Ratio = 1 });
            var bounds = (await control.BoundingBoxAsync())!;
            var sticky = await page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--site-sticky-height')) || 0");
            Assert.True(bounds.Y >= sticky - 1, $"{id} begins at {bounds.Y}px behind the {sticky}px header.");
        }

        var sequence = new[]
        {
            "leaderboardOrderSwap", "leaderboardOrderPrimary", "leaderboardOrderDirection",
            "leaderboardOrderPrimaryClear", "leaderboardOrderSecondary", "leaderboardThenDirection",
            "leaderboardOrderSecondaryClear"
        };
        for (var i = 0; i < sequence.Length; i++)
        {
            await ExpectVisibleFocusAsync(sequence[i]);
            if (i + 1 < sequence.Length) await page.Keyboard.PressAsync("Tab");
        }
        await page.Keyboard.PressAsync("Escape");
        await ExpectVisibleFocusAsync("leaderboardOrderToggle");
    }

    [Theory]
    [InlineData("/leaderboard", 844, 390, 100)]
    [InlineData("/", 844, 390, 100)]
    [InlineData("/leaderboard", 844, 390, 200)]
    [InlineData("/", 844, 390, 200)]
    [InlineData("/leaderboard", 640, 320, 200)]
    [InlineData("/", 640, 320, 200)]
    public async Task ShortLandscape_OpeningTallStripKeepsNativeFocusBelowStickyHeader(string path, int width, int height, int textPercent)
    {
        var (context, page) = await OpenAsync(width, height, false, path);
        await using var _ = context;
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = `${size}%`", textPercent);
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        var primary = page.Locator("#leaderboardOrderPrimary");
        await Assertions.Expect(primary).ToBeFocusedAsync();
        var position = await primary.EvaluateAsync<double[]>("""
            select => {
                const rect = select.getBoundingClientRect();
                const sticky = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--site-sticky-height')) || 0;
                return [rect.top, rect.bottom, sticky, innerHeight];
            }
            """);
        Assert.True(position[0] >= position[2], $"Focused field begins at {position[0]}px behind the {position[2]}px header.");
        Assert.True(position[1] <= position[3], $"Focused field ends at {position[1]}px below the {position[3]}px viewport.");
        await page.Keyboard.PressAsync("Escape");
        var toggle = page.Locator("#leaderboardOrderToggle");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        var trigger = (await toggle.BoundingBoxAsync())!;
        Assert.True(trigger.Y >= position[2], $"Returned focus is at {trigger.Y}px behind the {position[2]}px header.");
        Assert.True(trigger.Y + trigger.Height <= height, "Returned focus must fit in the viewport.");
    }
}
