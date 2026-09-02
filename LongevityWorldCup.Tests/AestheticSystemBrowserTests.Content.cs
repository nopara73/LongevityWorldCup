using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class AestheticSystemBrowserTests
{
    [Fact]
    public async Task RepresentativePages_ReflowAtFourHundredPercentZoomApproximation()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                // A 320 CSS-pixel viewport approximates viewing a 1280px desktop
                // page at 400% browser zoom, where the layout viewport reflows.
                ViewportSize = new ViewportSize { Width = 320, Height = 720 }
            });
        var page = await context.NewPageAsync();
        foreach (var path in RepresentativePaths)
        {
            await NavigateAndSettleAsync(page, path);
            var layout = await MeasureLayoutAsync(page);
            Assert.True(layout.RootFontSize >= 16, $"{path} reduced the root font below 16px during reflow.");
            Assert.True(
                layout.HorizontalOverflow <= 1,
                $"{path} did not reflow at the 400% zoom approximation: " +
                $"overflow={layout.HorizontalOverflow}px, scrollWidth={layout.ScrollWidth}, clientWidth={layout.ClientWidth}.");
        }
    }

    [Fact]
    public async Task LongLocalizedContent_ReflowsWithoutClippingAtThreeHundredTwentyPixels()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 320, Height = 720 }
            });
        await AddRouteStressStateAsync(context);
        var page = await context.NewPageAsync();

        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.WaitForSelectorAsync(
            ".leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name] .athlete-name",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible });
        await page.WaitForSelectorAsync(
            ".ranking-explanation",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible });
        var leaderboard = await InjectAndMeasureLongContentAsync(
            page,
            new Dictionary<string, string>
            {
                [".leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name] .athlete-name"] =
                    "Alexandria-Cassandra von Hohenlohe-Longevity-Research-Collective",
                [".leaderboard-view-switcher .view-badge"] =
                    "Classement général de toutes les catégories biologiques",
                [".ranking-explanation"] =
                    "Classement international calculé à partir de la réduction de l’âge biologique, " +
                    "avec départage transparent selon les mesures admissibles les plus récentes."
            },
            [
                ".leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name] .athlete-name",
                ".leaderboard-view-switcher .view-badge",
                ".ranking-explanation"
            ]);
        AssertLongContentLayout("/leaderboard", leaderboard);

        await NavigateAndSettleAsync(page, "/apply");
        var form = await InjectAndMeasureLongContentAsync(
            page,
            new Dictionary<string, string>
            {
                ["label[for=\"name\"]"] =
                    "Nom complet ou pseudonyme public international de l’athlète (obligatoire)",
                ["#nextButton .flow-action__label"] =
                    "Continuer vers l’étape suivante de la candidature internationale"
            },
            ["label[for=\"name\"]", "#nextButton"]);
        AssertLongContentLayout("/apply", form);

        await NavigateAndSettleAsync(page, "/play");
        var actions = await InjectAndMeasureLongContentAsync(
            page,
            new Dictionary<string, string>
            {
                ["#newGameBtn .flow-action__label"] =
                    "Je souhaite participer pour la toute première fois à cette compétition",
                ["#continueGameBtn .flow-action__label"] =
                    "Je participe déjà en tant qu’athlète international enregistré"
            },
            ["#newGameBtn", "#continueGameBtn"]);
        AssertLongContentLayout("/play", actions);

        var canonicalRoutes = GetCanonicalFirstPartyRoutes();
        Assert.NotEmpty(canonicalRoutes);

        var routeDiagnostics = new ExtremeContentDiagnostics?[canonicalRoutes.Length];
        var workerCount = Math.Min(2, canonicalRoutes.Length);
        await Task.WhenAll(
            Enumerable.Range(0, workerCount).Select(async workerIndex =>
            {
                var workerPage = workerIndex == 0 ? page : await context.NewPageAsync();
                try
                {
                    for (var routeIndex = workerIndex; routeIndex < canonicalRoutes.Length; routeIndex += workerCount)
                    {
                        await NavigateAndSettleAsync(workerPage, canonicalRoutes[routeIndex]);
                        routeDiagnostics[routeIndex] = await InjectAndMeasureExtremeContentAsync(workerPage);
                    }
                }
                finally
                {
                    if (workerIndex != 0)
                        await workerPage.CloseAsync();
                }
            }));

        for (var routeIndex = 0; routeIndex < canonicalRoutes.Length; routeIndex++)
        {
            var stress = Assert.IsType<ExtremeContentDiagnostics>(routeDiagnostics[routeIndex]);
            AssertExtremeContentLayout(canonicalRoutes[routeIndex], stress);
        }
    }

}
