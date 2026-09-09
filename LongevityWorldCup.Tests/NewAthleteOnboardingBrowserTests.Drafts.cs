using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class NewAthleteOnboardingBrowserTests
{
    private const string ApplicationDraftKey = "applicationDetailsDraft:v1";
    private const string DraftMotivation = "More healthy years exploring the world with my family.";
    private const string DraftImage = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z1ZsAAAAASUVORK5CYII=";

    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task ApplicationDraft_ReloadAndCalculatorReturnKeepEnteredDetails(int width)
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.SetViewportSizeAsync(width, 844);
            await page.EmulateMediaAsync(new() { ColorScheme = width == 390 ? ColorScheme.Dark : ColorScheme.Light });
            await CompleteAmateurHandoffToApplicationAsync(page, DateTime.UtcNow.AddDays(-9).ToString("yyyy-MM-dd"));
            await FillApplicationIdentityDraftAsync(page);
            await page.Locator("#name").PressAsync("Enter");
            await page.Locator("#why").FillAsync(DraftMotivation);
            await page.ReloadAsync();
            await AssertApplicationIdentityDraftAsync(page);
            await Assertions.Expect(page.Locator("#applicationDraftStatus")).ToBeHiddenAsync();
            await page.Locator("#nextButton").ClickAsync();
            await Assertions.Expect(page.Locator("#why")).ToHaveValueAsync(DraftMotivation);
            await Assertions.Expect(page.Locator("#whyCharCounter")).ToHaveTextAsync($"{DraftMotivation.Length}/250");
            await page.Locator("#backButton").ClickAsync();
            await page.Locator("#backButton").ClickAsync();
            await page.WaitForDomContentLoadedUrlAsync("**/pheno-age");
            await page.Locator("#calculateBioageButton").ClickAsync();
            await page.Locator("#continueButton.show").ClickAsync();
            await page.WaitForDomContentLoadedUrlAsync("**/apply");
            await AssertApplicationIdentityDraftAsync(page);
            await Assertions.Expect(page.Locator("#why")).ToHaveValueAsync(DraftMotivation);
            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
            Assert.Empty(errors);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationDraft_ContactDetailsKeepExplicitEmailOrFollowMediaPrefill(bool explicitEmail)
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply");
            await FillApplicationIdentityDraftAsync(page);
            await page.EvaluateAsync("() => { currentStage = 6; goToStage(6); }");
            await page.Locator("#personalLink").FillAsync("example.com/alex");
            await page.Locator("#mediaContact").FillAsync("media@example.com");
            await page.Locator("#nextButton").ClickAsync();
            if (explicitEmail) await page.Locator("#accountEmail").FillAsync("private@example.com");
            await page.ReloadAsync();
            await AssertApplicationIdentityDraftAsync(page);
            await Assertions.Expect(page.Locator("#personalLink")).ToHaveValueAsync("https://example.com/alex");
            await page.EvaluateAsync("() => { currentStage = 6; goToStage(6); }");
            await page.Locator("#mediaContact").FillAsync("new-media@example.com");
            await page.Locator("#nextButton").ClickAsync();
            await Assertions.Expect(page.Locator("#accountEmail")).ToHaveValueAsync(explicitEmail ? "private@example.com" : "new-media@example.com");
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task ApplicationDraft_DelayedDivisionOptionsKeepRestoredSelectionAndLaterTyping()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply");
            await FillApplicationIdentityDraftAsync(page);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await page.RouteAsync("**/api/data/divisions", async route =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "[\"Men's\",\"Women's\",\"Open\"]" });
            });
            try
            {
                await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await page.Locator("#name").FillAsync("Avery River");
                release.TrySetResult();
                await Assertions.Expect(page.Locator("#division")).ToHaveValueAsync("Open");
                await Assertions.Expect(page.Locator("#name")).ToBeFocusedAsync();
                await Assertions.Expect(page.Locator("#name")).ToHaveValueAsync("Avery River");
                await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
                await page.ReloadAsync();
                await Assertions.Expect(page.Locator("#name")).ToHaveValueAsync("Avery River");
                await Assertions.Expect(page.Locator("#division")).ToHaveValueAsync("Open");
                Assert.Empty(errors);
            }
            finally { release.TrySetResult(); }
        });
    }

    [Fact]
    public async Task ApplicationDraft_StorageFailureProtectsTypingAndClearsFeedbackAfterRecovery()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply");
            await page.EvaluateAsync("""
                () => { const original = Storage.prototype.setItem;
                    Storage.prototype.setItem = function(key, value) {
                        if (key === 'applicationDetailsDraft:v1') throw new DOMException('Full', 'QuotaExceededError');
                        return original.call(this, key, value);
                    };
                    window.restoreDraftStorage = () => { Storage.prototype.setItem = original; };
                }
                """);
            await page.Locator("#name").FillAsync("Alex River");
            await Assertions.Expect(page.Locator("#applicationDraftStatus")).ToHaveTextAsync("Your details could not be saved. Keep this page open.");
            await Assertions.Expect(page.Locator("#name")).ToBeFocusedAsync();
            Assert.True(await ApplicationExitIsProtectedAsync(page));
            await page.EvaluateAsync("window.restoreDraftStorage()");
            await page.Locator("#name").PressAsync("End");
            await page.Locator("#name").PressAsync("s");
            await Assertions.Expect(page.Locator("#applicationDraftStatus")).ToBeHiddenAsync();
            Assert.False(await ApplicationExitIsProtectedAsync(page));
            await page.ReloadAsync();
            await Assertions.Expect(page.Locator("#name")).ToHaveValueAsync("Alex Rivers");
            await page.EvaluateAsync("""
                () => { const original = Storage.prototype.removeItem;
                    Storage.prototype.removeItem = function(key) {
                        if (key === 'applicationDetailsDraft:v1') throw new DOMException('Blocked', 'SecurityError');
                        return original.call(this, key);
                    };
                }
                """);
            await page.Locator("#name").FillAsync("");
            await Assertions.Expect(page.Locator("#applicationDraftStatus")).ToBeVisibleAsync();
            Assert.True(await ApplicationExitIsProtectedAsync(page));
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task ApplicationDraft_AcceptedSubmissionClearsDetailsWithoutRecreatingThemOnExit()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await CompleteAmateurHandoffToApplicationAsync(page, DateTime.UtcNow.AddDays(-9).ToString("yyyy-MM-dd"));
            await FillApplicationIdentityDraftAsync(page);
            await page.Locator("#nextButton").ClickAsync();
            await page.Locator("#why").FillAsync(DraftMotivation);
            // Only media is fixture data. The fields, validation and submission use the real form.
            await page.EvaluateAsync("image => { profilePic = image; proofPics.push(image); currentStage = 6; goToStage(6); }", DraftImage);
            await page.Locator("#mediaContact").FillAsync("media@example.com");
            await page.Locator("#nextButton").ClickAsync();
            await page.Locator("#accountEmail").FillAsync("private@example.com");
            Assert.True(await ApplicationExitIsProtectedAsync(page));
            var requests = 0;
            await page.RouteAsync("**/api/application/application", async route =>
            {
                requests++;
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"success\":true,\"paymentRequired\":false}" });
            });
            await page.Locator("#nextButton").ClickAsync();
            await page.GetByText("Your application was received", new() { Exact = true }).WaitForAsync();
            Assert.Null(await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", ApplicationDraftKey));
            Assert.False(await ApplicationExitIsProtectedAsync(page));
            await page.EvaluateAsync("window.dispatchEvent(new PageTransitionEvent('pagehide'))");
            Assert.Null(await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", ApplicationDraftKey));
            await page.ReloadAsync();
            await Assertions.Expect(page.Locator("#name")).ToHaveValueAsync("");
            await Assertions.Expect(page.Locator("#accountEmail")).ToHaveValueAsync("");
            Assert.Equal(1, requests);
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task ApplicationDraft_ProfileSelectionWarnsBeforeExitAndCancelClearsTheWarning()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply");
            await FillApplicationIdentityDraftAsync(page);
            await page.EvaluateAsync("""
                () => {
                    window.Cropper = class {
                        destroy() {}
                        getCroppedCanvas() { const canvas = document.createElement('canvas'); canvas.width = 20; canvas.height = 20; return canvas; }
                    };
                    currentStage = 4; goToStage(4);
                }
                """);
            Assert.False(await ApplicationExitIsProtectedAsync(page));
            using var client = App.CreateClient();
            var photo = new FilePayload { Name = "portrait.png", MimeType = "image/png", Buffer = await client.GetByteArrayAsync("/assets/logo.png") };
            await page.Locator("#profilePicInput").SetInputFilesAsync(photo);
            await Assertions.Expect(page.Locator("#croppingPart")).ToBeVisibleAsync();
            Assert.True(await ApplicationExitIsProtectedAsync(page));
            await page.Locator("#cancelProfileCropButton").ClickAsync();
            Assert.False(await ApplicationExitIsProtectedAsync(page));
            await page.Locator("#profilePicInput").SetInputFilesAsync(photo);
            await Assertions.Expect(page.Locator("#croppingPart")).ToBeVisibleAsync();
            await page.Locator("#cropButton").ClickAsync();
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
            Assert.True(await ApplicationExitIsProtectedAsync(page));
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task ApplicationDraft_FakePreviewDoesNotReplaceARealDraft()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply");
            await FillApplicationIdentityDraftAsync(page);
            await page.GotoAsync("/apply?fake=1");
            await Assertions.Expect(page.Locator("#name")).Not.ToHaveValueAsync("Alex River");
            await page.GotoAsync("/apply");
            await AssertApplicationIdentityDraftAsync(page);
            Assert.Empty(errors);
        });
    }

    private static async Task FillApplicationIdentityDraftAsync(IPage page)
    {
        await page.Locator("#name[data-stage1-validity-listener='true']").WaitForAsync();
        await page.Locator("#name").FillAsync("Alex River");
        await page.Locator("#division").SelectOptionAsync("Open");
        await page.Locator("#flag").FillAsync("United Kingdom");
        await page.Locator("#flag").PressAsync("Escape");
    }

    private static async Task AssertApplicationIdentityDraftAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("#name")).ToHaveValueAsync("Alex River");
        await Assertions.Expect(page.Locator("#division")).ToHaveValueAsync("Open");
        await Assertions.Expect(page.Locator("#flag")).ToHaveValueAsync("United Kingdom");
        await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
    }

    private static Task<bool> ApplicationExitIsProtectedAsync(IPage page) => page.EvaluateAsync<bool>("""
        () => { const event = new Event('beforeunload', {cancelable:true}); window.dispatchEvent(event); return event.defaultPrevented; }
        """);
}
