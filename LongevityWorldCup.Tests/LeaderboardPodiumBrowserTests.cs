using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LeaderboardPodiumBrowserTests
{
    [Fact]
    public async Task PodiumContent_RemainsAboveThePrizePanelAcrossTheDesktopBoundary()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1026, Height = 505 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/bitcoin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            var body = path.EndsWith("/donation-address", StringComparison.OrdinalIgnoreCase)
                ? """{"address":""}"""
                : path.EndsWith("/btcusd", StringComparison.OrdinalIgnoreCase)
                    ? """{"btcToUsdRate":0}"""
                    : """{"totalReceivedSatoshis":0}""";

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = body
            });
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.podium-item:not(.podium-skeleton-item)').length === 3");

        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 1026, Height = 505 },
                     new ViewportSize { Width = 769, Height = 481 },
                     new ViewportSize { Width = 768, Height = 481 }
                 })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await SettleLayoutAsync(page);

            var layouts = await MeasurePodiumAsync(page);
            Assert.Equal(3, layouts.Length);
            AssertPodiumContentDoesNotOverlapPrizePanel(layouts, viewport);

            var first = Assert.Single(layouts, layout => layout.Rank == "first");
            var second = Assert.Single(layouts, layout => layout.Rank == "second");
            var third = Assert.Single(layouts, layout => layout.Rank == "third");
            Assert.True(first.CardHeight > second.CardHeight);
            Assert.True(second.CardHeight > third.CardHeight);

            if (viewport.Width > 768)
            {
                var cardBottoms = layouts.Select(layout => layout.CardBottom).ToArray();
                Assert.True(
                    cardBottoms.Max() - cardBottoms.Min() <= 1,
                    $"Podium cards stopped sharing a baseline at {viewport.Width}x{viewport.Height}.");
            }
        }

        await page.SetViewportSizeAsync(1026, 505);
        await SettleLayoutAsync(page);
        foreach (var rank in new[] { "first", "second", "third" })
        {
            var beforeStress = await MeasurePodiumAsync(page);
            var cardHeightBeforeStress = beforeStress.Single(layout => layout.Rank == rank).CardHeight;
            var athleteName = page.Locator($".podium-item.{rank} .athlete-name");
            var originalName = await athleteName.TextContentAsync() ?? "Athlete";
            await athleteName.EvaluateAsync(
                "(element, value) => element.textContent = value",
                $"Alexandria-Cassandra von Hohenlohe-{rank}-Longevity-Research-Collective");
            await SettleLayoutAsync(page);

            var stressedLayouts = await MeasurePodiumAsync(page);
            AssertPodiumContentDoesNotOverlapPrizePanel(
                stressedLayouts,
                new ViewportSize { Width = 1026, Height = 505 });
            AssertDesktopPodiumGeometry(stressedLayouts, $"while stressing {rank} place");
            Assert.True(
                stressedLayouts.Single(layout => layout.Rank == rank).CardHeight > cardHeightBeforeStress,
                $"The {rank}-place card did not grow to accommodate a wrapped athlete name.");

            await athleteName.EvaluateAsync("(element, value) => element.textContent = value", originalName);
            await SettleLayoutAsync(page);
        }
    }

    private static async Task SettleLayoutAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.fonts?.ready || Promise.resolve()");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
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
                const contentBottom = Math.max(...[...card.children]
                    .filter(child => !child.matches('.podium-rank, .podium-item-lower'))
                    .map(child => child.getBoundingClientRect().bottom));
                return {
                    Rank: ['first', 'second', 'third'].find(rank => card.classList.contains(rank)),
                    Athlete: card.getAttribute('data-athlete-name'),
                    CardHeight: cardRect.height,
                    CardBottom: cardRect.bottom,
                    ContentBottom: contentBottom,
                    MetricBottom: metricRect.bottom,
                    PrizePanelTop: panelRect.top
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
        Assert.InRange(first.CardHeight - second.CardHeight, 17, 19);
        Assert.InRange(second.CardHeight - third.CardHeight, 15, 17);
    }

    private sealed class PodiumLayout
    {
        public string Rank { get; set; } = "";
        public string Athlete { get; set; } = "";
        public double CardHeight { get; set; }
        public double CardBottom { get; set; }
        public double ContentBottom { get; set; }
        public double MetricBottom { get; set; }
        public double PrizePanelTop { get; set; }
    }
}
