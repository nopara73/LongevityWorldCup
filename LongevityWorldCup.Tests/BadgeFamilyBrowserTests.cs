using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

// Badges read as one family: every shape shares the theme's rim and sheen, the
// row's "+N" is a tile like its neighbours, an explanation opens as a popover
// that is not clipped by the badge, and the profile shelf never reflows.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed partial class BadgeFamilyBrowserTests(
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

        await hexagon.EvaluateAsync("el => el.nextElementSibling.focus({ preventScroll: true })");
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(hexagon).ToBeFocusedAsync();
        var tip = page.Locator(".badge-tip");
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await Assertions.Expect(tip.Locator(".badge-tip-title")).ToHaveTextAsync("Liver King");
        await Assertions.Expect(tip.Locator(".badge-tip-detail")).ToContainTextAsync("top liver profile");
        await AssertExplanationFitsAsync(tip);
        Assert.Null(await hexagon.GetAttributeAsync("aria-describedby"));
        Assert.Equal("true", await tip.GetAttributeAsync("aria-hidden"));
        Assert.Equal(title, await hexagon.GetAttributeAsync("aria-label"));
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

        // An outline outside a clip-path can be declared in CSS yet paint no
        // focus pixels. Check the rendered badge after dismissing its tooltip.
        var accent = await hexagon.EvaluateAsync<string>(
            "el => getComputedStyle(el).getPropertyValue('--lwc-accent-bright').trim() || '#19c3d1'");
        var expected = SixLabors.ImageSharp.Color.Parse(accent).ToPixel<SixLabors.ImageSharp.PixelFormats.Rgba32>();
        using var pixels = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(await hexagon.ScreenshotAsync());
        var focusPixels = 0;
        for (var y = 0; y < pixels.Height; y++)
        for (var x = 0; x < pixels.Width; x++)
        {
            var pixel = pixels[x, y];
            if (Math.Abs(pixel.R - expected.R) < 4 && Math.Abs(pixel.G - expected.G) < 4 && Math.Abs(pixel.B - expected.B) < 4)
                focusPixels++;
        }
        Assert.True(focusPixels >= 20, $"Only {focusPixels} accent pixels remain in the focused hexagon after Escape.");
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
        await AssertExplanationFitsAsync(tip);
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

        var toggle = item.Locator(".modal-badge-toggle");
        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        var panel = shelf.Locator(".modal-badge-explainer");
        await Assertions.Expect(panel).ToHaveCountAsync(1);
        await Assertions.Expect(panel.Locator(".modal-badge-explainer-title")).ToHaveTextAsync("Time Bender");
        await Assertions.Expect(panel.Locator(".modal-badge-explainer-detail")).ToHaveTextAsync("best pheno pace of aging");
        Assert.True(await panel.EvaluateAsync<bool>("el => el.scrollHeight === el.clientHeight && el.scrollWidth === el.clientWidth"));
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

        await toggle.ClickAsync();
        await Assertions.Expect(panel).ToHaveCountAsync(0);
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Null(await toggle.GetAttributeAsync("aria-controls"));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    private static ILocator MichaelRow(IPage page) =>
        page.Locator(".leaderboard tbody tr[data-athlete-name]")
            .Filter(new() { Has = page.Locator(".athlete-name", new() { HasText = "Michael Lustgarten" }) });

    [Theory]
    [InlineData(1, "Baby Boss", "#ffd700")]
    [InlineData(2, "Lullaby Lord", "#c0c0c0")]
    [InlineData(3, "Diaper Don", "#cd7f32")]
    public async Task CanonicalCrowdAge_UsesItsHumorousNameRankShapeMedalAndOrder(int place, string name, string colour)
    {
        await using var context = await NewBadgeContextAsync(1440, 900, false);
        var page = await OpenLeaderboardAsync(context);
        var result = await page.EvaluateAsync<string[]>("""
            place => {
                const badge = {BadgeLabel:'Crowd Age – lowest', Place:place};
                const host = document.createElement('div');
                host.className = 'badge-section';
                document.querySelector('#detailsModal').appendChild(host);
                window.setBadges({Badges:[badge, {BadgeLabel:'Crowd – most guessed', Place:place},
                    {BadgeLabel:'Age reduction', Place:place}, {BadgeLabel:'Best domain – liver', Place:1},
                    {BadgeLabel:'≥2 submissions'}]}, host);
                const lastTitle = host.lastElementChild.title;
                host.remove();
                return [window.makeTooltipFromServerBadge(badge, {CrowdAge:21.5}),
                    window.makeTooltipFromServerBadge(badge, {CrowdAge:21.5}, {suppressValues:true}),
                    window.getBadgeFamilyClass(badge), window.pickBackgroundForServerBadge(badge),
                    lastTitle, window.pickIconForServerBadge(badge)];
            }
            """, place);
        Assert.StartsWith(name + ":", result[0]);
        Assert.Contains("21.5 years", result[0]);
        Assert.StartsWith(name + ":", result[1]);
        Assert.DoesNotContain("21.5", result[1]);
        Assert.Equal("badge-family-rank", result[2]);
        Assert.Contains(colour, result[3]);
        Assert.StartsWith(name + ":", result[4]);
        Assert.Equal("fa-baby", result[5]);
    }

    [Fact]
    public async Task Tooltip_PermitsPointerTravelAndClosesWhenOwnerIsDetached()
    {
        await using var context = await NewBadgeContextAsync(1440, 900, false);
        var page = await OpenLeaderboardAsync(context);
        var chip = MichaelRow(page).Locator(".badge-overflow-count");
        await chip.HoverAsync();
        var tip = page.Locator(".badge-tip");
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await tip.HoverAsync();
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(200); // longer than the pointer-travel grace period
        await Assertions.Expect(tip).ToBeVisibleAsync();
        var owner = await chip.ElementHandleAsync();
        var title = await owner!.GetAttributeAsync("data-badge-tip");
        await owner.EvaluateAsync("el => el.remove()");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        Assert.Equal(title, await owner.GetAttributeAsync("title"));
        Assert.Null(await owner.GetAttributeAsync("aria-describedby"));
    }

    [Fact]
    public async Task Tooltip_RestoresExistingDescriptionsAndTitlesAndDismissesOnScrollResizeAndRerender()
    {
        await using var context = await NewBadgeContextAsync(1440, 900, false);
        var page = await OpenLeaderboardAsync(context);
        var chip = MichaelRow(page).Locator(".badge-overflow-count");
        await chip.ScrollIntoViewIfNeededAsync();
        await chip.EvaluateAsync("el => el.setAttribute('aria-describedby', 'leaderboardStatus')");
        var title = await chip.GetAttributeAsync("title");
        await chip.FocusAsync();
        var tip = page.Locator(".badge-tip");
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await Assertions.Expect(chip).ToHaveAttributeAsync("aria-describedby", "leaderboardStatus badge-tip");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        Assert.Equal(title, await chip.GetAttributeAsync("title"));
        Assert.Equal("leaderboardStatus", await chip.GetAttributeAsync("aria-describedby"));
        await Assertions.Expect(chip).ToBeFocusedAsync();

        await chip.HoverAsync();
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await page.EvaluateAsync("window.scrollBy(0, 100)");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        await chip.ClickAsync(new() { Trial = true });
        await chip.EvaluateAsync("el => el.blur()");
        await chip.FocusAsync();
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await page.SetViewportSizeAsync(1280, 800);
        await Assertions.Expect(tip).ToBeHiddenAsync();

        await chip.EvaluateAsync("el => el.blur()");
        await chip.FocusAsync();
        await Assertions.Expect(tip).ToBeVisibleAsync();
        // A layout update can move a focused owner without detaching it.
        await chip.EvaluateAsync("el => el.style.marginInlineStart = '18px'");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        Assert.Equal(title, await chip.GetAttributeAsync("title"));

        await chip.EvaluateAsync("el => el.blur()");
        await chip.FocusAsync();
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await chip.EvaluateAsync("el => el.title = 'New rendered title'");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        Assert.Equal("New rendered title", await chip.GetAttributeAsync("title"));
        await chip.EvaluateAsync("el => el.blur()");
        await chip.FocusAsync();
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await chip.EvaluateAsync("el => el.parentElement.replaceChildren()");
        await Assertions.Expect(tip).ToBeHiddenAsync();
    }

    [Fact]
    public async Task OverflowCard_PermitsPointerReadingScrollingAndEscapeFocusRestoration()
    {
        await using var context = await NewBadgeContextAsync(1440, 390, false);
        var page = await OpenLeaderboardAsync(context);
        var chip = MichaelRow(page).Locator(".badge-overflow-count");
        await chip.ScrollIntoViewIfNeededAsync();
        var title = await chip.GetAttributeAsync("title");
        await chip.FocusAsync();
        var tip = page.Locator(".badge-tip");
        await Assertions.Expect(tip).ToBeVisibleAsync();
        Assert.True(await tip.Locator(".badge-tip-content").EvaluateAsync<bool>("el => el.scrollHeight > el.clientHeight"));
        await tip.ClickAsync(new() { Position = new() { X = 20, Y = 20 } });
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await page.Mouse.WheelAsync(0, 1000);
        await Assertions.Expect(tip.Locator(".badge-tip-row").Last).ToBeInViewportAsync();
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        await Assertions.Expect(chip).ToBeFocusedAsync();
        Assert.Equal(title, await chip.GetAttributeAsync("title"));
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task Shelf_TreatsBadgeNamesAsTextAndKeepsOnlyTheCurrentDisclosureOwner(int width)
    {
        await using var context = await NewBadgeContextAsync(width, 900, false);
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            var michael = athletes.OfType<JsonObject>().Single(a => a["AthleteSlug"]!.GetValue<string>() == "michael_lustgarten");
            michael["Badges"] = new JsonArray(
                new JsonObject { ["BadgeLabel"] = "Badge <img src=x onerror=window.badgeMarkupExecuted=1>: Detail <svg onload=window.badgeMarkupExecuted=1>" },
                new JsonObject { ["BadgeLabel"] = "Crowd Age – lowest", ["Place"] = 1 });
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, $"/athlete/{Slug}");
        var shelf = page.Locator("#modalBadgeStrip");
        var toggles = shelf.Locator(".modal-badge-toggle");
        await Assertions.Expect(toggles).ToHaveCountAsync(2);
        Assert.Equal(0, await shelf.Locator("img, svg").CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("() => Boolean(window.badgeMarkupExecuted)"));
        var injected = toggles.Filter(new() { HasText = "onerror" });
        await injected.ClickAsync();
        await Assertions.Expect(shelf.Locator(".modal-badge-explainer")).ToContainTextAsync("<svg onload=");
        var other = toggles.Filter(new() { HasText = "Baby Boss" });
        await other.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Null(await injected.GetAttributeAsync("aria-controls"));
        await Assertions.Expect(injected).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(other).ToHaveAttributeAsync("aria-controls", "modalBadgeExplainer");
        await Assertions.Expect(shelf.Locator("[aria-controls]")).ToHaveCountAsync(1);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(shelf.Locator("[aria-controls]")).ToHaveCountAsync(0);
        await Assertions.Expect(shelf.Locator(".modal-badge-explainer")).ToHaveCountAsync(0);
        await Assertions.Expect(other).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Shelf_RepositionsForContainerAndFontChangesAndKeepsLinksSeparateFromButtons()
    {
        await using var context = await NewBadgeContextAsync(1440, 900, true);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, $"/athlete/{Slug}");
        var shelf = page.Locator("#modalBadgeStrip");
        var toggles = shelf.Locator(".modal-badge-toggle");
        await Assertions.Expect(toggles).ToHaveCountAsync(7);
        Assert.Equal(0, await shelf.Locator("button a, a button, [role=button] a").CountAsync());
        await Assertions.Expect(shelf.Locator("a[href='/']")).ToHaveCountAsync(1);
        await toggles.Nth(4).ClickAsync();
        foreach (var width in new[] { 280, 420, 180 })
        {
            await shelf.EvaluateAsync("(el, width) => { el.style.width = width+'px'; document.documentElement.style.fontSize='24px'; }", width);
            await page.WaitForFunctionAsync("""
                () => {
                    const shelf = document.querySelector('#modalBadgeStrip');
                    const item = shelf.querySelector('.modal-badge-item.expanded');
                    const panel = shelf.querySelector('.modal-badge-explainer');
                    if (!panel) return false;
                    const rect = item.getBoundingClientRect();
                    const next = panel.nextElementSibling?.getBoundingClientRect();
                    const p = panel.getBoundingClientRect();
                    return p.top >= rect.bottom && p.top - rect.bottom < 55 && (!next || next.top > p.bottom);
                }
                """);
        }
        await page.Keyboard.PressAsync("Escape");
        await shelf.EvaluateAsync("el => el.style.width='400px'");
        await Assertions.Expect(shelf.Locator(".modal-badge-explainer")).ToHaveCountAsync(0);
    }

    private static async Task<IPage> OpenLeaderboardAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        return page;
    }

    [Fact]
    public async Task TouchAndLongOverflow_KeepEveryExplanationReachableInsideTheViewport()
    {
        await using var context = await NewBadgeContextAsync(320, 480, true);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/events");
        await page.WaitForFunctionAsync("() => typeof window.setBadges === 'function'");
        await page.EvaluateAsync("""
            () => {
                const host = document.createElement('div');
                host.className = 'badge-section'; host.id = 'touch-badges';
                document.querySelector('.events-board').prepend(host);
                window.setBadges({Badges:Array.from({length:35}, (_,i) => ({
                    BadgeLabel:i ? 'Best domain – inflammation' : '≥2 submissions', Place:1
                }))}, host);
            }
            """);
        var badge = page.Locator("#touch-badges .badge-explained").First;
        await badge.TapAsync();
        var tip = page.Locator(".badge-tip");
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await Assertions.Expect(tip).ToContainTextAsync("The Regular");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        var chip = page.Locator("#touch-badges .badge-overflow-count");
        await chip.TapAsync();
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await Assertions.Expect(tip.Locator(".badge-tip-row")).ToHaveCountAsync(32);
        await tip.TapAsync(new() { Position = new() { X = 20, Y = 20 } });
        await Assertions.Expect(tip).ToBeVisibleAsync();
        Assert.True(await tip.EvaluateAsync<bool>("""
            el => {const r=el.getBoundingClientRect(),c=el.querySelector('.badge-tip-content');return r.left>=0 && r.right<=innerWidth && r.top>=0 && r.bottom<=innerHeight && c.scrollHeight>c.clientHeight;}
            """));
        await tip.Locator(".badge-tip-content").EvaluateAsync("el => el.scrollTop = el.scrollHeight");
        await Assertions.Expect(tip).ToBeVisibleAsync();
        await Assertions.Expect(tip.Locator(".badge-tip-row").Last).ToBeInViewportAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        await Assertions.Expect(chip).ToBeFocusedAsync();
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
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
        await RouteAthleteFixtureAsync(context, athletes =>
        {
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
