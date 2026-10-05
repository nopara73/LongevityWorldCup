using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class InitialViewBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string SeedTask = """
        sessionStorage.setItem('selectedAthlete', JSON.stringify({
            Name: 'Initial View Athlete', DisplayName: 'Initial View Athlete',
            AccountEmail: 'firstpaint@example.test', ProfilePic: '/assets/content-images/play-athlete-placeholder.jpg',
            Division: "Women's", Flag: 'United Kingdom', Why: 'More healthy years.',
            PersonalLink: 'https://example.test/athlete', MediaContact: 'firstpaint@example.test',
            DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: []
        }));
        sessionStorage.setItem('applicationDetailsDraft:v1', JSON.stringify({ fields: {
            name: 'Saved Application', division: "Women's", flag: 'United Kingdom',
            why: 'A saved motivation.', personalLink: '', mediaContact: '', accountEmail: 'firstpaint@example.test'
        }}));
        sessionStorage.setItem('contactEmail', 'firstpaint@example.test');
        localStorage.setItem('hasApplication', 'true');
        """;

    [Theory]
    [InlineData("/edit-profile", "#character-title", "Initial View Athlete", 390)]
    [InlineData("/proofs", "#character-title", "Initial View Athlete", 1280)]
    [InlineData("/review?from=proof-upload", "#appReviewText", "Result review", 390)]
    [InlineData("/review?from=edit-profile", "#appReviewText", "Edit review", 1280)]
    public async Task TaskPages_DoNotPaintGenericContentBeforeResolvingTheirTask(string path, string selector, string expected, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(SeedTask);
        var page = await context.NewPageAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/site-footer.js*", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.Commit });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await page.WaitForSelectorAsync(selector, new() { State = WaitForSelectorState.Attached });
            await NextPaintAsync(page);
            var title = page.Locator(selector);
            Assert.True(!await title.IsVisibleAsync() || (await title.TextContentAsync())?.Trim() == expected,
                "The first painted title must belong to the requested task.");
            await CaptureAsync(page, path, width, "loading");
            release.TrySetResult();
            await Assertions.Expect(title).ToHaveTextAsync(expected);
            await Assertions.Expect(title).ToBeVisibleAsync();
            if (path == "/edit-profile")
            {
                await Assertions.Expect(page.Locator("#divisionDisplaySelect")).ToHaveValueAsync("Women's");
                await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("More healthy years.");
            }
            if (path.StartsWith("/review"))
                await Assertions.Expect(page.Locator("#contactEmailPlaceholder")).ToHaveTextAsync("firstpaint@example.test");
            await CaptureAsync(page, path, width, "ready");
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task SavedApplication_RestoresAllFieldsBeforeTheyBecomeVisible()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync(SeedTask);
        var page = await context.NewPageAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/site-footer.js*", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        // Optional directory requests must not gate restoration of saved fields.
        await context.RouteAsync("**/api/data/divisions", route => route.AbortAsync());
        try
        {
            await page.GotoAsync("/apply", new() { WaitUntil = WaitUntilState.Commit });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var name = page.Locator("#name");
            await page.WaitForSelectorAsync("#name", new() { State = WaitForSelectorState.Attached });
            await NextPaintAsync(page);
            Assert.True(!await name.IsVisibleAsync() || await name.InputValueAsync() == "Saved Application",
                "An empty applicant form must not paint on the way to a saved draft.");
            release.TrySetResult();
            await Assertions.Expect(name).ToBeVisibleAsync();
            await Assertions.Expect(name).ToHaveValueAsync("Saved Application");
            await Assertions.Expect(page.Locator("#division")).ToHaveValueAsync("Women's");
            await Assertions.Expect(page.Locator("#accountEmail")).ToHaveValueAsync("firstpaint@example.test");
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("/join?freepass=1", "#joinTrackPanel", 390)]
    [InlineData("/play", "#playStartPanel", 1280)]
    [InlineData("/select-athlete", "#athleteSelectionPanel", 390)]
    [InlineData("/dashboard", "#athleteDashboardPanel", 1280)]
    public async Task PlayRoutes_ResolveTheirPanelPricingAndReturningAthleteBeforePainting(string path, string panel, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(SeedTask + """
            const savedAthlete = JSON.parse(sessionStorage.getItem('selectedAthlete'));
            savedAthlete.Name = savedAthlete.DisplayName = 'Michael Lustgarten';
            sessionStorage.setItem('selectedAthlete', JSON.stringify(savedAthlete));
            """);
        var page = await context.NewPageAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/site-footer.js*", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.Commit });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await NextPaintAsync(page);
            if (panel != "#playStartPanel") await Assertions.Expect(page.Locator("#playStartPanel")).ToBeHiddenAsync();
            if (path.StartsWith("/join") && await page.Locator("#joinTrackPanel").IsVisibleAsync())
                await Assertions.Expect(page.Locator(".join-amateur-entry-cell")).ToContainTextAsync("free");
            if (path == "/play" && await page.Locator("#playStartPanel").IsVisibleAsync())
                await Assertions.Expect(page.Locator(".play-menu-actions button").First).ToContainTextAsync("I'm already an athlete");
            await CaptureAsync(page, path, width, "loading");
            release.TrySetResult();
            await Assertions.Expect(page.Locator(panel)).ToBeVisibleAsync();
            if (path.StartsWith("/join"))
            {
                await Assertions.Expect(page.Locator(".join-amateur-entry-cell")).ToContainTextAsync("free");
                await Assertions.Expect(page.Locator("#joinProEntryPrice")).ToContainTextAsync("free");
            }
            if (path == "/play")
                await Assertions.Expect(page.Locator(".play-menu-actions button").First).ToContainTextAsync("I'm already an athlete");
            if (path == "/dashboard")
                await Assertions.Expect(page.Locator("#athleteDashboardTitle")).ToHaveTextAsync("Michael Lustgarten");
            await CaptureAsync(page, path, width, "ready");
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("pheno", 390)]
    [InlineData("bortz", 1280)]
    public async Task SharedCalculatorLinks_RestoreTheirValuesBeforePainting(string clock, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/site-statistics-tracking.js*", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            await page.GotoAsync($"/{clock}-age?Year=1980&Month=5&Day=20&Date=2026-09-01&Wbc1000cellsuL=6.3", new() { WaitUntil = WaitUntilState.Commit });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await page.WaitForSelectorAsync("#dob-year", new() { State = WaitForSelectorState.Attached });
            await NextPaintAsync(page);
            Assert.True(!await page.Locator("#dob-year").IsVisibleAsync() || await page.Locator("#dob-year").InputValueAsync() == "1980");
            release.TrySetResult();
            await Assertions.Expect(page.Locator("#dob-year")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#dob-year")).ToHaveValueAsync("1980");
            await Assertions.Expect(page.Locator("#blood-draw-date")).ToHaveValueAsync("2026-09-01");
            await Assertions.Expect(page.Locator("#wbc")).ToHaveValueAsync("6.3");
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task StatisticsLink_DoesNotPaintDefaultFiltersBeforeRestoringTheUrl()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/site-statistics.js*", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            await page.GotoAsync("/internal/site-statistics.html?tab=Source%20Quality&range=30d&flow=challenge&device=mobile&source=ai", new() { WaitUntil = WaitUntilState.Commit });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await page.WaitForSelectorAsync("#statsRange", new() { State = WaitForSelectorState.Attached });
            await NextPaintAsync(page);
            Assert.True(!await page.Locator("#statsRange").IsVisibleAsync() || await page.Locator("#statsRange").InputValueAsync() == "30d");
            release.TrySetResult();
            await Assertions.Expect(page.Locator("#statsRange")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#statsRange")).ToHaveValueAsync("30d");
            await Assertions.Expect(page.Locator("#statsFlow")).ToHaveValueAsync("challenge");
            await Assertions.Expect(page.Locator("#statsDevice")).ToHaveValueAsync("mobile");
            await Assertions.Expect(page.Locator("#statsSource")).ToHaveValueAsync("ai");
            await Assertions.Expect(page.Locator(".stats-tab.active")).ToHaveTextAsync("Source Quality");
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task SocialPostDestinations_RestoreBeforeTheOptionalDirectory_AndKeepSubsequentEdits()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync("""
            localStorage.setItem('customEventDesigner.sendTo', JSON.stringify({
                sendWebpage: false, sendSlack: true, sendX: false, sendThreads: true, sendFacebook: true
            }));
            """);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/internal/custom-event-designer.html", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assertions.Expect(page.Locator("#sendWebpage")).Not.ToBeCheckedAsync();
            await Assertions.Expect(page.Locator("#sendX")).Not.ToBeCheckedAsync();
            await Assertions.Expect(page.Locator("#sendThreads")).ToBeCheckedAsync();
            await page.Locator("#sendX").CheckAsync();
            var directoryResponse = page.WaitForResponseAsync("**/api/data/athletes");
            release.TrySetResult();
            await directoryResponse;
            await NextPaintAsync(page);
            await Assertions.Expect(page.Locator("#sendX")).ToBeCheckedAsync();
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("/leaderboard?search=Michael&view=pheno", "pheno", "Michael", "PHENO AGE LEAGUE")]
    [InlineData("/league/bortz?search=Michael", "bortz", "Michael", "BORTZ AGE LEAGUE")]
    [InlineData("/contribute?search=Michael&view=pheno", "pheno", "Michael", "PHENO AGE LEAGUE")]
    public async Task SearchLinks_NeverPaintDefaultRankingOrAnEmptyQuery(string path, string view, string query, string rail)
    {
        await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#view-" + view)).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync(query);
        await Assertions.Expect(page.Locator(".collapsed-title")).ToHaveTextAsync(rail);
        await Assertions.Expect(page.Locator(".leaderboard table tbody")).ToHaveAttributeAsync("aria-busy", "true");
    }

    [Fact]
    public async Task DirectGuessLink_StartsWithItsDialog_WithoutPublishingProfileAnswers()
    {
        await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/michael-lustgarten?guessmyage=1");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#athleteBio")).ToBeEmptyAsync();
        await Assertions.Expect(page.Locator("#chronologicalAge")).ToBeEmptyAsync();
        await Assertions.Expect(page.Locator("#guessAgeContainer")).ToBeHiddenAsync();
        Assert.Null(await page.Locator("#detailsModal .modal-content").GetAttributeAsync("data-server-rendered-profile"));
    }

    [Theory]
    [InlineData("/?view=pheno", 390)]
    [InlineData("/?filters=amateur", 1280)]
    [InlineData("/?search=Michael", 390)]
    public async Task SelectedHomepage_DoesNotPaintTheDefaultChampionsWhileHydrating(string path, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        await context.AddInitScriptAsync("""
            window.paintedDefaultChampions = false;
            function observePodium() {
                const podium = document.querySelector('.podium');
                if (podium?.checkVisibility({ checkOpacity: true })) window.paintedDefaultChampions = true;
                requestAnimationFrame(observePodium);
            }
            requestAnimationFrame(observePodium);
            """);
        try
        {
            await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await NextPaintAsync(page);
            await Assertions.Expect(page.Locator(".podium")).ToBeHiddenAsync();
            release.TrySetResult();
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            await NextPaintAsync(page);
            Assert.False(await page.EvaluateAsync<bool>("window.paintedDefaultChampions"));
            await Assertions.Expect(page.Locator(".podium")).ToBeHiddenAsync();
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task DirectGuessLoadingDialog_CanCloseBeforeTheAthleteRequestReturns()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/athlete/michael-lustgarten?guessmyage=1", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#guessAgeContainer")).ToBeHiddenAsync();
            await page.Locator("#closeAthleteDetailsModal").ClickAsync();
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
            release.TrySetResult();
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            await NextPaintAsync(page);
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExpiredHeadGate_OffersRecoveryWhenTheDocumentArrivesAfterFailure(bool abortModule)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync(SeedTask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseModule = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/field-validation.js*", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        await context.RouteAsync("**/js/flags.js*", async route =>
        {
            if (abortModule) await route.AbortAsync();
            else
            {
                await releaseModule.Task;
                await route.ContinueAsync();
            }
        });
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        try
        {
            await page.GotoAsync("/edit-profile", new() { WaitUntil = WaitUntilState.Commit });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-initial-view", "pending");
            await page.Clock.FastForwardAsync(15000);
            await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-initial-view", "failed");
            release.TrySetResult();
            await Assertions.Expect(page.Locator(".initial-view-recovery")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".initial-view-recovery")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("#whyDisplayInput")).ToBeHiddenAsync();
        }
        finally
        {
            release.TrySetResult();
            releaseModule.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationDivisionLookup_PreservesFieldsEditedWhileTheDirectoryIsPending(bool fake)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync(SeedTask);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/divisions", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = "[\"Men's\",\"Women's\",\"Open\",\"New division\"]" });
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(fake ? "/apply?fake=1" : "/apply", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await page.Locator("#name").FillAsync("Edited while loading");
            await page.Locator("#division").SelectOptionAsync("Open");
            release.TrySetResult();
            await Assertions.Expect(page.Locator("#division option")).ToHaveCountAsync(5);
            await Assertions.Expect(page.Locator("#division")).ToHaveValueAsync("Open");
            await Assertions.Expect(page.Locator("#name")).ToHaveValueAsync("Edited while loading");
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task FailedProfileInitialization_OffersRecoveryWithoutRevealingAnEmptyEditor()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync(SeedTask);
        var unavailable = true;
        await context.RouteAsync("**/js/flags.js*", route => unavailable ? route.AbortAsync() : route.ContinueAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/edit-profile");
        await Assertions.Expect(page.Locator(".initial-view-recovery")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToBeHiddenAsync();
        unavailable = false;
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#character-title")).ToHaveTextAsync("Initial View Athlete");
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("More healthy years.");
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToBeVisibleAsync();
    }

    private static Task NextPaintAsync(IPage page)
        => page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

    private static async Task CaptureAsync(IPage page, string path, int width, string state)
    {
        var directory = Path.Combine(FindRepositoryRoot(), ".artifacts", "initial-view-audit");
        Directory.CreateDirectory(directory);
        var name = path.Trim('/').Replace('?', '-').Replace('=', '-');
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}-{width}-{state}.png"), FullPage = true });
    }
}
