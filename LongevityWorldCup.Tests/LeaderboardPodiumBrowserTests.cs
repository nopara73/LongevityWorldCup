using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class LeaderboardPodiumBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task LeaderboardRowsAndPodiumCards_PreserveFeedbackAndTheirBroadDetailsHitAreas(ColorScheme colorScheme)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ColorScheme = colorScheme,
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = 1026, Height = 720 }
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            var separator = route.Request.Url.Contains('?') ? '&' : '?';
            await route.ContinueAsync(new RouteContinueOptions
            {
                Url = $"{route.Request.Url}{separator}browserFullFixture=1"
            });
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.podium-item:not(.podium-skeleton-item)').length === 3");
        await page.WaitForSelectorAsync(
            ".leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name]:visible");
        var fullAthleteCount = await page.EvaluateAsync<int>(
            "() => window.__sharedAthletesRequest.then(athletes => athletes.length)");
        Assert.True(fullAthleteCount >= 200, $"Expected the full-scale athlete fixture; received {fullAthleteCount} rows.");

        var proRows = page.Locator(".leaderboard tbody:not(.loading-skeleton) tr.tier-pro:visible");
        // Both striped and unstriped athletes must show hover and rank-link feedback.
        foreach (var index in new[] { 0, 1 })
        {
            var row = proRows.Nth(index);
            await page.Mouse.MoveAsync(0, 0);
            var restingBackground = await row.EvaluateAsync<string>("row => getComputedStyle(row).backgroundColor");
            await row.HoverAsync();
            await Assertions.Expect(row).Not.ToHaveCSSAsync("background-color", restingBackground);
            await page.Mouse.MoveAsync(0, 0);
            await Assertions.Expect(row).ToHaveCSSAsync("background-color", restingBackground);
            var rankAnchor = await row.GetAttributeAsync("id");
            await page.EvaluateAsync("anchor => location.hash = anchor", rankAnchor);
            Assert.True(await row.EvaluateAsync<bool>("row => row.matches(':target')"));
            await Assertions.Expect(row).Not.ToHaveCSSAsync("background-color", restingBackground);
            await page.EvaluateAsync("location.hash = ''");
            await Assertions.Expect(row).ToHaveCSSAsync("background-color", restingBackground);
        }

        var visibleRows = page.Locator(".leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name]:visible");
        var visibleRowCount = await visibleRows.CountAsync();
        Assert.True(visibleRowCount > 0);
        var finalVisibleRow = visibleRows.Nth(visibleRowCount - 1);
        var expectedName = (await finalVisibleRow.Locator(".athlete-name").InnerTextAsync()).Trim();

        // The final cloned row used to miss its listeners because wiring happened before append.
        await finalVisibleRow.Locator(".athlete-name").ClickAsync();
        await AssertDetailsModalShowsAthleteAsync(page, expectedName);
        await CloseDetailsModalAsync(page);

        // Master treated neutral row chrome as a large hit area, not only the name control.
        await finalVisibleRow.Locator(".rank").ClickAsync();
        await AssertDetailsModalShowsAthleteAsync(page, expectedName);
        await CloseDetailsModalAsync(page);

        var podiumCards = page.Locator(".podium-item:not(.podium-skeleton-item)");
        var podiumCount = await podiumCards.CountAsync();
        Assert.Equal(3, podiumCount);
        var firstPodiumCard = podiumCards.Nth(0);
        var podiumName = (await firstPodiumCard.Locator(".athlete-name").InnerTextAsync()).Trim();

        await firstPodiumCard.Locator(".podium-rank").ClickAsync();
        await AssertDetailsModalShowsAthleteAsync(page, podiumName);
        await CloseDetailsModalAsync(page);

        // The prize panel remains a donation link and must not be swallowed by the card handler.
        await firstPodiumCard.Locator(".podium-item-lower").ClickAsync();
        await page.WaitForDomContentLoadedUrlAsync("**/#contribute");
        Assert.False(await page.Locator("#detailsModal").IsVisibleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PodiumContent_RemainsAboveThePrizePanelAcrossTheDesktopBoundary(bool javaScriptEnabled)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            JavaScriptEnabled = javaScriptEnabled,
            ViewportSize = new ViewportSize { Width = 1026, Height = 505 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.podium-item:not(.podium-skeleton-item)').length === 3");
        if (javaScriptEnabled)
        {
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        }
        // Measure the resting baseline after the deliberately staggered entrance.
        await page.WaitForFunctionAsync(
            "() => !document.documentElement.classList.contains('homepage-arriving')");

        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 1026, Height = 505 },
                     new ViewportSize { Width = 769, Height = 481 },
                     new ViewportSize { Width = 768, Height = 481 },
                     new ViewportSize { Width = 683, Height = 900 },
                     new ViewportSize { Width = 548, Height = 844 },
                     new ViewportSize { Width = 390, Height = 844 },
                     new ViewportSize { Width = 320, Height = 700 }
                 })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await SettleLayoutAsync(page, javaScriptEnabled);

            var layouts = await MeasurePodiumAsync(page);
            Assert.Equal(3, layouts.Length);
            AssertPodiumContentDoesNotOverlapPrizePanel(layouts, viewport);
            AssertMedalsFollowPortraits(layouts, $"at {viewport.Width}x{viewport.Height}");

            var first = Assert.Single(layouts, layout => layout.Rank == "first");
            var second = Assert.Single(layouts, layout => layout.Rank == "second");
            var third = Assert.Single(layouts, layout => layout.Rank == "third");
            if (viewport.Width > 768)
            {
                AssertDesktopPodiumGeometry(layouts, $"at {viewport.Width}x{viewport.Height}");
            }
            else
            {
                Assert.True(first.CardBottom <= Math.Min(second.CardTop, third.CardTop),
                    "The champion must remain above the other two athletes on mobile.");
                Assert.InRange(Math.Abs(second.CardTop - third.CardTop), 0, 1);
                Assert.True(second.CardRight <= third.CardLeft,
                    "Second and third place must fit beside each other without overlapping.");
                Assert.InRange(first.CardLeft, 0, viewport.Width);
                Assert.InRange(third.CardRight, 0, viewport.Width);
                Assert.InRange(Math.Abs(first.NameLeft - first.MetricLeft), 0, 1);
                Assert.InRange(Math.Abs(first.NameLeft - first.LinkRowLeft), 0, 1);
                Assert.True(Math.Max(second.CardBottom, third.CardBottom) - first.CardTop < 600,
                    "The mobile podium must leave room for the standings below it.");
            }
            Assert.InRange(await page.EvaluateAsync<int>("document.documentElement.scrollWidth"), 0, viewport.Width);
        }

        await page.SetViewportSizeAsync(1026, 505);
        await SettleLayoutAsync(page, javaScriptEnabled);
        foreach (var rank in new[] { "first", "second", "third" })
        {
            var beforeStress = await MeasurePodiumAsync(page);
            var cardHeightBeforeStress = beforeStress.Single(layout => layout.Rank == rank).CardHeight;
            var athleteName = page.Locator($".podium-item.{rank} .athlete-name");
            var originalName = await athleteName.TextContentAsync() ?? "Athlete";
            await athleteName.EvaluateAsync(
                "(element, value) => element.textContent = value",
                $"Alexandria-Cassandra von Hohenlohe-{rank}-Longevity-Research-Collective");
            await SettleLayoutAsync(page, javaScriptEnabled);

            var stressedLayouts = await MeasurePodiumAsync(page);
            AssertPodiumContentDoesNotOverlapPrizePanel(
                stressedLayouts,
                new ViewportSize { Width = 1026, Height = 505 });
            AssertDesktopPodiumGeometry(stressedLayouts, $"while stressing {rank} place");
            Assert.True(
                stressedLayouts.Single(layout => layout.Rank == rank).CardHeight > cardHeightBeforeStress,
                $"The {rank}-place card did not grow to accommodate a wrapped athlete name.");

            await athleteName.EvaluateAsync("(element, value) => element.textContent = value", originalName);
            await SettleLayoutAsync(page, javaScriptEnabled);
        }

        await page.SetViewportSizeAsync(320, 700);
        await page.Locator(".podium-item.first .athlete-name").EvaluateAsync(
            "element => element.textContent = 'Alexandria-Cassandra von Hohenlohe-Longevity-Research-Collective'");
        await SettleLayoutAsync(page, javaScriptEnabled);
        AssertPodiumContentDoesNotOverlapPrizePanel(
            await MeasurePodiumAsync(page), new ViewportSize { Width = 320, Height = 700 });
        AssertMedalsFollowPortraits(await MeasurePodiumAsync(page), "with a wrapped champion name on mobile");
        Assert.InRange(await page.EvaluateAsync<int>("document.documentElement.scrollWidth"), 0, 320);
    }

    private static async Task SettleLayoutAsync(IPage page, bool javaScriptEnabled)
    {
        if (javaScriptEnabled)
        {
            await page.EvaluateAsync("() => document.fonts?.ready || Promise.resolve()");
            await page.EvaluateAsync(
                "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        }
        else
        {
            await page.WaitForLoadStateAsync(LoadState.Load);
        }
    }

    [Theory]
    [InlineData(ForcedColors.None)]
    [InlineData(ForcedColors.Active)]
    public async Task MobilePrizeLinks_KeepLargeAmountsAndKeyboardFocusInsideTheirCards(ForcedColors forcedColors)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ForcedColors = forcedColors,
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new() { Width = 320, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.style.fontSize = '32px';
                document.querySelectorAll('.podium-item-lower').forEach(panel => {
                    panel.querySelector('.prize-money').textContent = '$27000.00';
                    panel.querySelector('.btc-amount').textContent = '(0.54000000 BTC)';
                });
            }
            """);
        await SettleLayoutAsync(page, true);
        foreach (var panel in await page.Locator(".podium-item-lower").AllAsync())
        {
            Assert.True(await panel.EvaluateAsync<bool>(
                """
                panel => {
                    const box = panel.getBoundingClientRect();
                    return [...panel.children].every(child => {
                        const range = document.createRange();
                        range.selectNodeContents(child);
                        return [...range.getClientRects()].every(rect =>
                            rect.left >= box.left - 1 && rect.right <= box.right + 1);
                    });
                }
                """), "Prize amounts must wrap inside their card rather than be clipped.");
            await panel.FocusAsync();
            Assert.True(await panel.EvaluateAsync<bool>(
                """
                panel => {
                    const style = getComputedStyle(panel);
                    return panel.matches(':focus-visible') && style.outlineStyle !== 'none'
                        && parseFloat(style.outlineWidth) >= 2
                        && parseFloat(style.outlineOffset) <= -parseFloat(style.outlineWidth);
                }
                """), "The focus ring must remain inside the clipped mobile card.");
        }
    }

    private static async Task AssertDetailsModalShowsAthleteAsync(IPage page, string expectedName)
    {
        await page.WaitForFunctionAsync(
            "name => document.getElementById('detailsModal')?.style.display === 'block' && document.getElementById('athleteName')?.textContent?.includes(name)",
            expectedName);
    }

    private static async Task CloseDetailsModalAsync(IPage page)
    {
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.getElementById('detailsModal')?.style.display !== 'block'");
    }

    private static Task<PodiumLayout[]> MeasurePodiumAsync(IPage page) =>
        page.Locator(".podium-item:not(.podium-skeleton-item)").EvaluateAllAsync<PodiumLayout[]>(
            """
            cards => cards.map(card => {
                const panel = card.querySelector('.podium-item-lower');
                const metric = card.querySelector('.age-reduction')?.parentElement;
                if (!panel || !metric) throw new Error('Podium content is incomplete.');

                const cardRect = card.getBoundingClientRect();
                const panelRect = panel.getBoundingClientRect();
                const metricRect = metric.getBoundingClientRect();
                const portraitRect = card.querySelector('.podium-portrait').getBoundingClientRect();
                const medalRect = card.querySelector('.podium-rank').getBoundingClientRect();
                const contentBottom = Math.max(...[...card.children]
                    .filter(child => !child.matches('.podium-rank, .podium-item-lower'))
                    .map(child => child.getBoundingClientRect().bottom));
                return {
                    Rank: ['first', 'second', 'third'].find(rank => card.classList.contains(rank)),
                    Athlete: card.getAttribute('data-athlete-name'),
                    CardHeight: cardRect.height,
                    CardTop: cardRect.top,
                    CardLeft: cardRect.left,
                    CardRight: cardRect.right,
                    CardBottom: cardRect.bottom,
                    ContentBottom: contentBottom,
                    MetricBottom: metricRect.bottom,
                    MetricLeft: metricRect.left,
                    NameLeft: card.querySelector('.athlete-name').getBoundingClientRect().left,
                    LinkRowLeft: card.querySelector('.podium-link-row').getBoundingClientRect().left,
                    PrizePanelTop: panelRect.top,
                    PortraitBottom: portraitRect.bottom,
                    PortraitCenterX: (portraitRect.left + portraitRect.right) / 2,
                    MedalCenterX: (medalRect.left + medalRect.right) / 2,
                    MedalCenterY: (medalRect.top + medalRect.bottom) / 2
                };
            })
            """);

    private static void AssertPodiumContentDoesNotOverlapPrizePanel(
        IEnumerable<PodiumLayout> layouts,
        ViewportSize viewport)
    {
        const double renderingTolerance = 0.5;
        foreach (var layout in layouts)
        {
            Assert.True(
                layout.MetricBottom <= layout.PrizePanelTop + renderingTolerance,
                $"{layout.Athlete}'s metric overlapped the prize panel at {viewport.Width}x{viewport.Height}: " +
                $"metric bottom {layout.MetricBottom:F2}px, panel top {layout.PrizePanelTop:F2}px.");
            Assert.True(
                layout.ContentBottom <= layout.PrizePanelTop + renderingTolerance,
                $"{layout.Athlete}'s podium content overlapped the prize panel at " +
                $"{viewport.Width}x{viewport.Height}.");
        }
    }

    private static void AssertDesktopPodiumGeometry(IEnumerable<PodiumLayout> layouts, string context)
    {
        var measured = layouts.ToArray();
        var first = Assert.Single(measured, layout => layout.Rank == "first");
        var second = Assert.Single(measured, layout => layout.Rank == "second");
        var third = Assert.Single(measured, layout => layout.Rank == "third");
        var cardBottoms = measured.Select(layout => layout.CardBottom).ToArray();

        Assert.True(cardBottoms.Max() - cardBottoms.Min() <= 1,
            $"Podium cards stopped sharing a baseline {context}.");
        // Each placing stands on a visibly higher step than the one below it.
        Assert.True(first.CardHeight - second.CardHeight >= 16,
            $"First place no longer stands clearly above second {context}.");
        Assert.True(second.CardHeight - third.CardHeight >= 16,
            $"Second place no longer stands clearly above third {context}.");
        Assert.True(second.PrizePanelTop - first.PrizePanelTop >= 16,
            $"The gold prize step is not clearly above silver {context}.");
        Assert.True(third.PrizePanelTop - second.PrizePanelTop >= 16,
            $"The silver prize step is not clearly above bronze {context}.");
    }

    private static void AssertMedalsFollowPortraits(IEnumerable<PodiumLayout> layouts, string context)
    {
        foreach (var layout in layouts)
        {
            Assert.InRange(Math.Abs(layout.MedalCenterX - layout.PortraitCenterX), 0, 1);
            Assert.True(Math.Abs(layout.MedalCenterY - layout.PortraitBottom) <= 6,
                $"The {layout.Rank}-place medal moved away from its portrait {context}.");
        }
    }

    private sealed class PodiumLayout
    {
        public string Rank { get; set; } = "";
        public string Athlete { get; set; } = "";
        public double CardHeight { get; set; }
        public double CardTop { get; set; }
        public double CardLeft { get; set; }
        public double CardRight { get; set; }
        public double CardBottom { get; set; }
        public double ContentBottom { get; set; }
        public double MetricBottom { get; set; }
        public double MetricLeft { get; set; }
        public double NameLeft { get; set; }
        public double LinkRowLeft { get; set; }
        public double PrizePanelTop { get; set; }
        public double PortraitBottom { get; set; }
        public double PortraitCenterX { get; set; }
        public double MedalCenterX { get; set; }
        public double MedalCenterY { get; set; }
    }
}
