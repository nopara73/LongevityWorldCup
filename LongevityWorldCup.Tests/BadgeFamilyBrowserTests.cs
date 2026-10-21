using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

// Badges read as one family: every shape shares the theme's rim and sheen, the
// row's "+N" is a tile like its neighbours, an explanation opens as a popover
// that is not clipped by the badge, and the profile shelf never reflows.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BadgeFamilyBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Slug = "michael-lustgarten";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RowBadges_ShareOneRimSheenAndTileFootprint(bool dark)
    {
        await using var context = await NewBadgeContextAsync(1440, 900, dark);
        var page = await OpenLeaderboardAsync(context);
        var row = MichaelRow(page);
        await Assertions.Expect(row.Locator(".badge-overflow-count")).ToHaveTextAsync("+4");

        var badges = await row.Locator(".badge-section > .badge-class").EvaluateAllAsync<RowBadge[]>("""
            badges => badges.map(badge => {
                const style = getComputedStyle(badge);
                const rect = badge.getBoundingClientRect();
                return {
                    Overflow: badge.classList.contains('badge-overflow-count'),
                    Domain: badge.classList.contains('badge-family-domain'),
                    Width: rect.width,
                    Height: rect.height,
                    Rim: badge.classList.contains('badge-family-domain') ? style.backgroundColor : style.borderTopColor,
                    BorderWidth: style.borderTopWidth,
                    Sheen: getComputedStyle(badge, '::after').backgroundImage
                };
            })
            """);
        Assert.Equal(4, badges.Length);
        var expectedRim = dark ? "rgba(255, 255, 255, 0.26)" : "rgba(0, 0, 0, 0.48)";
        Assert.Contains(badges, b => b.Domain);
        foreach (var badge in badges.Where(b => !b.Overflow))
        {
            Assert.Equal(expectedRim, badge.Rim);
            Assert.Contains("linear-gradient", badge.Sheen);
        }

        var chip = Assert.Single(badges, b => b.Overflow);
        Assert.Equal("1px", chip.BorderWidth);
        Assert.All(badges, b => Assert.InRange(b.Height, chip.Height - 0.5, chip.Height + 0.5));
        Assert.All(badges, b => Assert.InRange(b.Width, chip.Width - 0.5, chip.Width + 0.5));
    }

    [Theory]
    [InlineData(1440, false)]
    [InlineData(1440, true)]
    [InlineData(1100, false)]
    public async Task FocusedBadge_ExplainsItselfInAPopoverOutsideTheBadge(int width, bool dark)
    {
        await using var context = await NewBadgeContextAsync(width, 900, dark);
        var page = await OpenLeaderboardAsync(context);
        var hexagon = MichaelRow(page).Locator(".badge-section .badge-family-domain");
        await hexagon.ScrollIntoViewIfNeededAsync();
        var title = await hexagon.GetAttributeAsync("title");
        Assert.StartsWith("Liver King:", title);

        await hexagon.FocusAsync();
        var tip = page.Locator(".badge-tip");
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await Assertions.Expect(tip.Locator(".badge-tip-title")).ToHaveTextAsync("Liver King");
        await Assertions.Expect(tip.Locator(".badge-tip-detail")).ToContainTextAsync("top liver profile");
        Assert.Equal("badge-tip", await hexagon.GetAttributeAsync("aria-describedby"));
        // The native title is lifted while the popover shows, so two tooltips never stack.
        Assert.Null(await hexagon.GetAttributeAsync("title"));

        var geometry = await page.EvaluateAsync<TipGeometry>("""
            () => {
                const tip = document.querySelector('.badge-tip').getBoundingClientRect();
                const badge = document.activeElement.getBoundingClientRect();
                return {
                    TipBottom: tip.bottom, TipTop: tip.top, TipLeft: tip.left, TipRight: tip.right, TipWidth: tip.width,
                    BadgeTop: badge.top, BadgeBottom: badge.bottom, Viewport: innerWidth
                };
            }
            """);
        Assert.True(geometry.TipWidth > 120, $"popover is {geometry.TipWidth}px wide");
        Assert.True(geometry.TipBottom <= geometry.BadgeTop || geometry.TipTop >= geometry.BadgeBottom,
            "the popover must sit beside the badge, not inside its clipped shape");
        Assert.True(geometry.TipLeft >= 0 && geometry.TipRight <= geometry.Viewport);

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        Assert.Equal(title, await hexagon.GetAttributeAsync("title"));
        Assert.Null(await hexagon.GetAttributeAsync("aria-describedby"));
    }

    [Fact]
    public async Task OverflowChip_ListsEveryHiddenBadgeOnHover()
    {
        await using var context = await NewBadgeContextAsync(1440, 900, dark: false);
        var page = await OpenLeaderboardAsync(context);
        await MichaelRow(page).Locator(".badge-overflow-count").HoverAsync();
        var tip = page.Locator(".badge-tip");
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await Assertions.Expect(tip.Locator(".badge-tip-row")).ToHaveCountAsync(4);
        await page.Mouse.MoveAsync(5, 5);
        await Assertions.Expect(tip).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task ProfileShelf_ExplainsABadgeUnderItsRowWithoutMovingAnyBadge(int width, int height)
    {
        await using var context = await NewBadgeContextAsync(width, height, dark: false);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, $"/athlete/{Slug}");
        await page.WaitForFunctionAsync(
            """
            slug => {
                const content = document.querySelector('#detailsModal .modal-content');
                return content?.dataset.athleteSlug === slug && !content.classList.contains('is-loading')
                    && document.querySelectorAll('#modalBadgeStrip .modal-badge-item').length >= 7;
            }
            """, Slug);
        var shelf = page.Locator("#modalBadgeStrip");
        await shelf.ScrollIntoViewIfNeededAsync();
        Assert.Equal("grid", await shelf.EvaluateAsync<string>("el => getComputedStyle(el).display"));

        const string positions = "shelf => [...shelf.querySelectorAll('.modal-badge-item .badge-class')].map(b => { const r = b.getBoundingClientRect(); return [Math.round(r.left), Math.round(r.width)]; })";
        var before = await shelf.EvaluateAsync<int[][]>(positions);

        var item = shelf.Locator(".modal-badge-item").Nth(1);
        // Hovering must not open the detail in place and shove the grid.
        await item.HoverAsync();
        Assert.Equal(before, await shelf.EvaluateAsync<int[][]>(positions));

        await item.ClickAsync();
        await Assertions.Expect(item).ToHaveAttributeAsync("aria-expanded", "true");
        var panel = shelf.Locator(".modal-badge-explainer");
        await Assertions.Expect(panel).ToHaveCountAsync(1);
        await Assertions.Expect(panel.Locator(".modal-badge-explainer-title")).ToHaveTextAsync("Time Bender");
        await Assertions.Expect(panel.Locator(".modal-badge-explainer-detail")).ToHaveTextAsync("best pheno pace of aging");
        Assert.Equal(before, await shelf.EvaluateAsync<int[][]>(positions));

        var layout = await shelf.EvaluateAsync<PanelLayout>("""
            shelf => {
                const panel = shelf.querySelector('.modal-badge-explainer');
                const item = shelf.querySelector('.modal-badge-item.expanded');
                const p = panel.getBoundingClientRect();
                const i = item.getBoundingClientRect();
                const s = shelf.getBoundingClientRect();
                const caret = parseFloat(getComputedStyle(panel).getPropertyValue('--explainer-caret'));
                return {
                    PanelTop: p.top, ItemBottom: i.bottom, PanelWidth: p.width, ShelfWidth: s.width,
                    CaretX: p.left + caret, ItemCenter: i.left + i.width / 2
                };
            }
            """);
        Assert.True(layout.PanelTop >= layout.ItemBottom, "the explanation opens under the chosen badge's row");
        Assert.True(layout.PanelTop - layout.ItemBottom < 40, $"panel gap {layout.PanelTop - layout.ItemBottom}px");
        Assert.True(layout.PanelWidth > layout.ShelfWidth * 0.8);
        Assert.InRange(layout.CaretX, layout.ItemCenter - 2, layout.ItemCenter + 2);

        await item.ClickAsync();
        await Assertions.Expect(panel).ToHaveCountAsync(0);
        await Assertions.Expect(item).ToHaveAttributeAsync("aria-expanded", "false");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    private static ILocator MichaelRow(IPage page) =>
        page.Locator(".leaderboard tbody tr[data-athlete-name]")
            .Filter(new() { Has = page.Locator(".athlete-name", new() { HasText = "Michael Lustgarten" }) });

    private static async Task<IPage> OpenLeaderboardAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        return page;
    }

    private async Task<IBrowserContext> NewBadgeContextAsync(int width, int height, bool dark)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            var response = await route.FetchAsync();
            var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
            var michael = athletes.OfType<JsonObject>().Single(athlete =>
                athlete["AthleteSlug"]!.GetValue<string>() == "michael_lustgarten");
            michael["PersonalLink"] = null;
            michael["PodcastLink"] = null;
            // Sorted by the renderer: the row shows submissions, pace and the liver hexagon, then "+4".
            michael["Badges"] = new JsonArray(
                new JsonObject { ["BadgeLabel"] = "Age reduction", ["LeagueCategory"] = "Global", ["Place"] = 1 },
                new JsonObject { ["BadgeLabel"] = "≥2 submissions" },
                new JsonObject { ["BadgeLabel"] = "Best domain – liver", ["Place"] = 1 },
                new JsonObject { ["BadgeLabel"] = "Best domain – metabolic", ["Place"] = 1 },
                new JsonObject { ["BadgeLabel"] = "Best domain – immune", ["Place"] = 1 },
                new JsonObject { ["BadgeLabel"] = "Best domain – vitamin D", ["Place"] = 1 },
                new JsonObject { ["BadgeLabel"] = "Pheno pace of aging", ["Place"] = 1 });
            await route.FulfillAsync(new() { ContentType = "application/json", Body = athletes.ToJsonString() });
        });
        return context;
    }

    private sealed class RowBadge
    {
        public bool Overflow { get; set; }
        public bool Domain { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Rim { get; set; } = "";
        public string BorderWidth { get; set; } = "";
        public string Sheen { get; set; } = "";
    }

    private sealed class TipGeometry
    {
        public double TipBottom { get; set; }
        public double TipTop { get; set; }
        public double TipLeft { get; set; }
        public double TipRight { get; set; }
        public double TipWidth { get; set; }
        public double BadgeTop { get; set; }
        public double BadgeBottom { get; set; }
        public double Viewport { get; set; }
    }

    private sealed class PanelLayout
    {
        public double PanelTop { get; set; }
        public double ItemBottom { get; set; }
        public double PanelWidth { get; set; }
        public double ShelfWidth { get; set; }
        public double CaretX { get; set; }
        public double ItemCenter { get; set; }
    }
}
