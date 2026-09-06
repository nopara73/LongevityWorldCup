using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProfileDraftBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, true)]
    [InlineData(1280, false)]
    public async Task ReturningToDraft_RestoresEditsWithoutRoutineStatusMessages(int width, bool dark)
    {
        await using var context = await CreateContextAsync(width, dark);
        var page = await OpenEditorAsync(context);
        await page.Locator("#personalLinkInput").FillAsync("example.com/alex");
        await page.Locator("#whyDisplayInput").FillAsync("More healthy years with my family.");
        await page.Locator(".back-button").ClickAsync();
        await page.WaitForURLAsync("**/dashboard");
        await page.GotoAsync("/edit-profile");
        await Assertions.Expect(page.Locator("#personalLinkInput")).ToHaveValueAsync("https://example.com/alex");
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("More healthy years with my family.");
        await Assertions.Expect(page.Locator("#profileDraftStatus")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#resetProfileDraftButton")).ToBeVisibleAsync();
        await page.Locator("#restorePersonalLinkBtn").ClickAsync();
        await Assertions.Expect(page.Locator("#personalLinkInput")).ToHaveValueAsync("https://example.com");
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("More healthy years with my family.");
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    [Fact]
    public async Task ResetAndUndo_RestoreAllDraftValuesAndValidationWithoutSubmitting()
    {
        await using var context = await CreateContextAsync(390);
        var page = await OpenEditorAsync(context);
        // Seed the next document so the current editor's late division response
        // cannot overwrite the fixture with its still-unchanged profile.
        await page.AddInitScriptAsync("""
            { const draft = JSON.parse(sessionStorage.getItem('selectedAthlete'));
                draft.ProfilePic = '/assets/content-images/play-athlete-placeholder.webp';
                sessionStorage.setItem('tempAthlete', JSON.stringify(draft)); }
            """);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#resetProfileDraftButton")).ToBeVisibleAsync();
        await page.Locator("#divisionDisplaySelect").SelectOptionAsync("Open");
        await page.Locator("#flagDisplayInput").FillAsync("?");
        await page.Locator("#personalLinkInput").FillAsync("");
        await page.Locator("#mediaContactInput").FillAsync("@alex");
        await page.Locator("#whyDisplayInput").FillAsync("A different motivation.");
        await page.Locator("#resetProfileDraftButton").ClickAsync();
        await Assertions.Expect(page.Locator("#undoProfileDraftButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator("#flagDisplayInput")).ToHaveValueAsync("United Kingdom");
        await Assertions.Expect(page.Locator(".illustration")).ToHaveAttributeAsync("src", "/assets/content-images/play-athlete-placeholder.jpg");
        Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('tempAthlete')"));
        await page.Locator("#undoProfileDraftButton").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#resetProfileDraftButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#flagDisplayInput")).ToHaveValueAsync("?");
        await Assertions.Expect(page.Locator("#flagDisplayInput")).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator("#personalLinkInput")).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator("#divisionDisplaySelect")).ToHaveValueAsync("Open");
        await Assertions.Expect(page.Locator("#mediaContactInput")).ToHaveValueAsync("@alex");
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("A different motivation.");
        await Assertions.Expect(page.Locator(".illustration")).ToHaveAttributeAsync("src", "/assets/content-images/play-athlete-placeholder.webp");
        await page.Locator("#resetProfileDraftButton").ClickAsync();
        await page.Locator("#whyDisplayInput").FillAsync("Start a new draft.");
        await Assertions.Expect(page.Locator("#undoProfileDraftButton")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#submitButton")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task DirectEntry_IgnoresAnotherAthletesDraft()
    {
        await using var context = await CreateContextAsync(390);
        var page = await OpenEditorAsync(context);
        var recoveries = 0;
        await context.RouteAsync("**/api/application/submission-status", async route =>
        {
            recoveries++;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"success\":true}" });
        });
        await page.EvaluateAsync("""
            () => { const draft = JSON.parse(sessionStorage.getItem('selectedAthlete'));
                draft.Name = 'Another Athlete'; draft.DisplayName = 'Another Athlete'; draft.Why = 'Someone else';
                sessionStorage.setItem('tempAthlete', JSON.stringify(draft));
                window.rememberPendingApplicationSubmission({submissionId:'another-athlete-request', payloadFingerprint:'other-draft',
                    submissionKind:'edit-request', applicantName:draft.Name}); }
            """);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#character-title")).ToHaveTextAsync("Alex Morgan");
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("Training for a longer, healthier life.");
        await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
        Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('tempAthlete')"));
        Assert.Equal(0, recoveries);
    }

    [Fact]
    public async Task ResetBeforeBlurValidation_UndoRestoresTheInvalidFieldFeedback()
    {
        await using var context = await CreateContextAsync(390);
        var page = await OpenEditorAsync(context);
        await page.Locator("#whyDisplayInput").FillAsync("");
        await page.Locator("#resetProfileDraftButton").ClickAsync();
        await page.Locator("#undoProfileDraftButton").ClickAsync();
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("");
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator("#whyDisplayInputError")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailure_DoesNotOfferAnAcceptedDraftForResubmission(bool blockReplacement)
    {
        await using var context = await CreateContextAsync(390);
        var page = await OpenEditorAsync(context);
        var submissions = 0;
        var recoveries = 0;
        await context.RouteAsync("**/api/application/application", async route =>
        {
            submissions++;
            await page.EvaluateAsync("""
                blockReplacement => {
                    const remove = Storage.prototype.removeItem, set = Storage.prototype.setItem;
                    Storage.prototype.removeItem = function(key) {
                        if (key === 'tempAthlete') throw new DOMException('Storage blocked', 'SecurityError');
                        return remove.call(this, key);
                    };
                    Storage.prototype.setItem = function(key, value) {
                        if (blockReplacement && key === 'tempAthlete') throw new DOMException('Storage blocked', 'SecurityError');
                        return set.call(this, key, value);
                    };
                }
                """, blockReplacement);
            await route.FulfillAsync(new() { ContentType = "application/json", Body = "{}" });
        });
        await context.RouteAsync("**/api/application/submission-status", async route =>
        {
            recoveries++;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"success\":true}" });
        });
        await page.Locator("#whyDisplayInput").FillAsync("This change was accepted.");
        await page.Locator("#submitButton").ClickAsync();
        await Assertions.Expect(page.Locator("#custom-alert")).ToContainTextAsync("Change request submitted!");
        if (blockReplacement)
            Assert.True(await page.EvaluateAsync<bool>("Boolean(window.getPendingApplicationSubmission('edit-request'))"));
        else
            Assert.True(await page.EvaluateAsync<bool>("JSON.parse(sessionStorage.getItem('tempAthlete')) === null"));
        await page.ReloadAsync();
        if (blockReplacement)
            await Assertions.Expect(page.Locator("#custom-alert")).ToContainTextAsync("Change request submitted!");
        else
            await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("Training for a longer, healthier life.");
        Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('tempAthlete')"));
        Assert.False(await page.EvaluateAsync<bool>("Boolean(window.getPendingApplicationSubmission('edit-request'))"));
        Assert.Equal(blockReplacement ? 1 : 0, recoveries);
        Assert.Equal(1, submissions);
    }

    [Fact]
    public async Task FailedDraftStorage_ExplainsTheFailureAndProtectsNavigation()
    {
        await using var context = await CreateContextAsync(390);
        var page = await OpenEditorAsync(context);
        await page.EvaluateAsync("""
            () => { const originalSet = Storage.prototype.setItem;
                Storage.prototype.setItem = function(key, value) {
                    if (key === 'tempAthlete') throw new DOMException('Storage full', 'QuotaExceededError');
                    return originalSet.call(this, key, value); }; }
            """);
        await page.Locator("#whyDisplayInput").ClickAsync();
        await page.Locator("#whyDisplayInput").FillAsync("Do not lose this draft.");
        await Assertions.Expect(page.Locator("#profileDraftStatus")).ToContainTextAsync("could not be saved");
        var dialogs = 0;
        page.Dialog += async (_, dialog) => { dialogs++; Assert.Equal("beforeunload", dialog.Type); await dialog.DismissAsync(); };
        await Assert.ThrowsAsync<PlaywrightException>(() => page.GotoAsync("/dashboard"));
        Assert.Equal(1, dialogs);
        await Assertions.Expect(page.Locator("#whyDisplayInput")).ToHaveValueAsync("Do not lose this draft.");
        await page.Locator("#resetProfileDraftButton").ClickAsync();
        await page.GotoAsync("/dashboard");
        Assert.Equal(1, dialogs);
    }

    [Fact]
    public async Task AcceptedSubmission_ClearsTheDraftWithoutChangingThePublishedProfile()
    {
        await using var context = await CreateContextAsync(390);
        var divisionsGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/divisions", async route =>
        {
            await divisionsGate.Task;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = "[\"Men's\",\"Women's\",\"Open\",\"Masters\"]" });
        });
        await context.RouteAsync("**/api/application/application", route => route.FulfillAsync(new()
        {
            Status = 200, ContentType = "application/json", Body = "{}"
        }));
        try
        {
            var page = await OpenEditorAsync(context);
            await page.Locator("#whyDisplayInput").FillAsync("Please review my new motivation.");
            await page.Locator("#submitButton").ClickAsync();
            await Assertions.Expect(page.Locator("#custom-alert")).ToContainTextAsync("Change request submitted!");
            Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('tempAthlete')"));
            divisionsGate.TrySetResult();
            await Assertions.Expect(page.Locator("#divisionDisplaySelect option")).ToHaveCountAsync(4);
            Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('tempAthlete')"));
            Assert.Equal("Training for a longer, healthier life.", await page.EvaluateAsync<string>("JSON.parse(sessionStorage.getItem('selectedAthlete')).Why"));
        }
        finally { divisionsGate.TrySetResult(); }
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, bool dark = false)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("""
            if (!sessionStorage.getItem('selectedAthlete')) {
                const athlete = {Name:'Alex Morgan', DisplayName:'Alex Morgan', ProfilePic:'/assets/content-images/play-athlete-placeholder.jpg',
                    Division:"Men's", Flag:'United Kingdom', PersonalLink:'https://example.com',
                    MediaContact:'press@example.com', Why:'Training for a longer, healthier life.', Biomarkers:[]};
                sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
                localStorage.setItem('selectedAthleteName', athlete.Name); localStorage.setItem('hasApplication', 'true');
            }
            """);
        await context.RouteAsync("**/api/application/submission-report", route => route.FulfillAsync(new() { Status = 204 }));
        return context;
    }

    private static async Task<IPage> OpenEditorAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/edit-profile");
        await page.WaitForFunctionAsync("() => document.querySelector('#divisionDisplaySelect')?.value === \"Men's\" && document.querySelector('#whyDisplayInput')?.value === 'Training for a longer, healthier life.'");
        await page.EvaluateAsync("async()=>{await window.modulesReady;await document.fonts.ready}");
        return page;
    }
}
