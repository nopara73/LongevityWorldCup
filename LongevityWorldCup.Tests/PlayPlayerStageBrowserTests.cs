using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayPlayerStageBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string SavedAthlete = """
        const athlete = {Name:'Stage Test Athlete',DisplayName:'Stage Test Athlete',
            DateOfBirth:{Year:1980,Month:5,Day:20},Biomarkers:[]};
        sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
        localStorage.setItem('selectedAthleteName', athlete.Name);
        """;

    private Task<IBrowserContext> ContextAsync() => NewContextAsync(Browser, App, new()
    {
        ViewportSize = new() { Width = 1440, Height = 900 },
        ReducedMotion = ReducedMotion.NoPreference
    });

    private static async Task ReadyAsync(IPage page, string route)
    {
        await page.GotoAsync(route);
        await page.WaitForFunctionAsync("() => document.documentElement.classList.contains('play-route-ready') && !document.body.classList.contains('play-route-hydrating')");
        await page.EvaluateAsync("() => document.fonts.ready");
    }

    [Fact]
    public async Task Caption_OptionalAnimationFailureDoesNotThrow()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await ReadyAsync(page, "/select-athlete");
        await page.EvaluateAsync("""
            () => { Element.prototype.animate = undefined;
                document.getElementById('athleteSelectionTitle').textContent = 'Chosen athlete'; }
            """);
        await page.EvaluateAsync("() => new Promise(requestAnimationFrame)");
        Assert.Empty(errors);
        await Assertions.Expect(page.Locator("#athleteSelectionTitle")).ToHaveTextAsync("Chosen athlete");
    }

    [Fact]
    public async Task Caption_ReducedMotionCancelsAnAlreadyRunningAnimation()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await ReadyAsync(page, "/select-athlete");
        await page.EvaluateAsync("""
            () => {
                const animate = Element.prototype.animate;
                Element.prototype.animate = function(frames, options) {
                    return animate.call(this, frames, {...options, duration:5000});
                };
                document.getElementById('athleteSelectionTitle').textContent = 'Chosen athlete';
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('athleteSelectionTitle').getAnimations().some(a => a.playState === 'running')");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => !document.getElementById('athleteSelectionTitle').getAnimations().some(a => a.playState === 'running')",
            null, new() { Timeout = 1000 });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Spotlight_DoesNotKeepARejectedLazyThumbnail(bool broken)
    {
        await using var context = await ContextAsync();
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """[{"Name":"Stage Test Athlete","DateOfBirth":{"Year":1980,"Month":5,"Day":20},"Biomarkers":[],"ProfilePicLeaderboardThumb":"/stage-tiny.svg"}]"""
        }));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/stage-tiny.svg", async route =>
        {
            await release.Task;
            await route.FulfillAsync(new()
            {
                Status = broken ? 404 : 200, ContentType = "image/svg+xml",
                Body = """<svg xmlns="http://www.w3.org/2000/svg" width="8" height="8"><rect width="8" height="8" fill="red"/></svg>"""
            });
        });
        var page = await context.NewPageAsync();
        await ReadyAsync(page, "/select-athlete");
        await page.Locator("#playAthleteInput").FillAsync("Stage");
        await Assertions.Expect(page.Locator("[role=option]").First).ToBeVisibleAsync();
        await page.Locator("#playAthleteInput").PressAsync("ArrowDown");
        release.SetResult();
        await page.WaitForFunctionAsync("() => document.querySelector('.athlete-option-thumb')?.dataset.lwcImageRole === 'illustration'");
        await page.EvaluateAsync("() => new Promise(requestAnimationFrame)");
        Assert.False(await page.Locator(".play-athlete-card").EvaluateAsync<bool>("c => c.classList.contains('is-spotlit')"));
        Assert.Equal("", await page.Locator(".athlete-stage-spotlight").EvaluateAsync<string>("s => s.style.getPropertyValue('--stage-spotlight')"));
    }

    [Fact]
    public async Task BadgeFreeDiscount_HasAVisibleDecorativeChip()
    {
        await using var context = await ContextAsync();
        await context.AddInitScriptAsync(SavedAthlete);
        var page = await context.NewPageAsync();
        await ReadyAsync(page, "/dashboard");
        var slot = page.Locator(".play-dashboard-discounts .pro-discount-badge-slot").First;
        await Assertions.Expect(slot).ToBeVisibleAsync();
        Assert.DoesNotContain("\uf295", await page.Locator(".play-dashboard-discounts").AriaSnapshotAsync());
        Assert.Contains("10%", await page.Locator(".play-dashboard-discounts").InnerTextAsync());
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(1440, 900)]
    public async Task ThreePerks_KeepDesktopCommandsInline(int width, int height)
    {
        await using var context = await ContextAsync();
        await context.AddInitScriptAsync(SavedAthlete + """
            athlete.PersonalLink='https://example.test/stage-test-athlete';
            sessionStorage.setItem('selectedAthlete',JSON.stringify(athlete));
            localStorage.setItem('gmaHasPerfectGuess','1');
            """);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await ReadyAsync(page, "/dashboard");
        await WaitForFiniteAnimationsAsync(page);
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await Assertions.Expect(page.Locator(".play-dashboard-discounts .pro-discount-line")).ToHaveCountAsync(3);
        Assert.True(await page.Locator("#athleteDashboardActions").EvaluateAsync<bool>("e => !e.classList.contains('flow-action-stack--docked') && e.getBoundingClientRect().bottom <= innerHeight"));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1366, 640)]
    [InlineData(1280, 600)]
    public async Task ShortProStage_KeepsFittingActionsInline(int width, int height)
    {
        await using var context = await ContextAsync();
        await context.AddInitScriptAsync(SavedAthlete + """
            athlete.Biomarkers=[{Date:'2026-06-19',Hba1cMmolMol:35}];
            sessionStorage.setItem('selectedAthlete',JSON.stringify(athlete));
            """);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await ReadyAsync(page, "/dashboard");
        await WaitForFiniteAnimationsAsync(page);
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        Assert.True(await page.Locator("#athleteDashboardActions").EvaluateAsync<bool>("e => !e.classList.contains('flow-action-stack--docked') && e.getBoundingClientRect().bottom <= innerHeight"));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }
}
