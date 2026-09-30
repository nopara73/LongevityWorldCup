using Microsoft.Playwright;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class NewAthleteOnboardingBrowserTests
{
    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(631)]
    [InlineData(1280)]
    public async Task Application_DefaultsAndOptionalProfileFitOneFlatForm(int width)
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.SetViewportSizeAsync(width, 844);
            await page.GotoAsync("/apply");
            await page.Locator("#profilePicInput[data-listener='true']").WaitForAsync(new() { State = WaitForSelectorState.Attached });
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Enter the arena");
            await Assertions.Expect(page.Locator("#name")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#accountEmail")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#profileUploadSection")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#proofUploadSection")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#why")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#division")).ToHaveValueAsync("Open");
            await Assertions.Expect(page.Locator("#flag")).ToHaveValueAsync("Cyberspace");
            Assert.Equal(0, await page.Locator("fieldset, .sub-progress-container").CountAsync());
            Assert.Equal(0, await page.Locator("textarea[required], #mediaContact[required]").CountAsync());
            var backIcon = await page.Locator("#backButton > i").BoundingBoxAsync();
            var backLabel = await page.Locator("#backButton .flow-action__label").BoundingBoxAsync();
            Assert.NotNull(backIcon); Assert.NotNull(backLabel);
            Assert.True(backIcon.X + backIcon.Width <= backLabel.X);
            Assert.Contains("Declining means no spot", await page.Locator("#privacyDetails").InnerTextAsync());
            await Assertions.Expect(page.Locator("#privacyText")).ToBeHiddenAsync();
            await page.Locator(".application-privacy-story summary").PressAsync("Enter");
            await Assertions.Expect(page.Locator("#privacyText")).ToBeVisibleAsync();
            Assert.Contains("There’s no sport without spectators.", await page.Locator("#privacyText").InnerTextAsync());
            await page.Locator("#profileOptions > summary").ClickAsync();
            Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
            Assert.True(await page.Locator("#division, #flag, #why, #mediaContact, #personalLink").EvaluateAllAsync<bool>("fields => fields.every(field => { const bounds = field.getBoundingClientRect(); return bounds.left >= 0 && bounds.right <= innerWidth; })"));
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task Application_SubmitsWithTwoTypedFieldsAndUncroppedPhotoWithoutPublicContact()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await CompleteAmateurHandoffToApplicationAsync(page, DateTime.UtcNow.AddDays(-9).ToString("yyyy-MM-dd"));
            await page.Locator("#profilePicInput[data-listener='true']").WaitForAsync(new() { State = WaitForSelectorState.Attached });
            await page.Locator("#name").FillAsync("Synthetic Minimal Athlete");
            await page.Locator("#accountEmail").FillAsync("private@example.test");
            using var client = App.CreateClient();
            var image = await client.GetByteArrayAsync("/assets/logo.png");
            await page.Locator("#profilePicInput").SetInputFilesAsync(new FilePayload { Name = "portrait.png", MimeType = "image/png", Buffer = image });
            await Assertions.Expect(page.Locator("#profileImage")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#croppingPart")).ToBeHiddenAsync();
            await page.Locator("#proofPicInput").SetInputFilesAsync(new FilePayload { Name = "synthetic-proof.png", MimeType = "image/png", Buffer = image });
            await page.WaitForFunctionAsync("() => !applicationProfilePending && !applicationProofPending && proofPics.length > 0");
            var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            await page.RouteAsync("**/api/application/application", async route =>
            {
                using var json = JsonDocument.Parse(route.Request.PostData!);
                received.SetResult(json.RootElement.Clone());
                await route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"success\":true,\"paymentRequired\":false}" });
            });
            await page.Locator("#nextButton").ClickAsync();
            var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal("Open", payload.GetProperty("division").GetString());
            Assert.Equal("Cyberspace", payload.GetProperty("flag").GetString());
            Assert.Equal("", payload.GetProperty("why").GetString());
            Assert.Equal("", payload.GetProperty("mediaContact").GetString());
            Assert.Equal("private@example.test", payload.GetProperty("accountEmail").GetString());
            Assert.StartsWith("data:image/", payload.GetProperty("profilePic").GetString());
            Assert.Single(payload.GetProperty("proofPics").EnumerateArray());
            AssertSubmittedPhenoBiomarkers(payload, DateTime.UtcNow.AddDays(-9).ToString("yyyy-MM-dd"));
            await page.GetByText("Your application was received", new() { Exact = true }).WaitForAsync();
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task Application_ValidationOpensOptionalFieldAndNeverCopiesPrivateEmailToPublicContact()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await GoToFakeApplicationFinalStageAsync(page);
            await page.Locator("#accountEmail").FillAsync("private@example.com");
            await page.Locator("#mediaContact").FillAsync("");
            await page.Locator("#personalLink").FillAsync("invalid link");
            await page.Locator("#profileOptions > summary").ClickAsync();
            await page.Locator("#nextButton").ClickAsync();
            await Assertions.Expect(page.Locator("#personalLink")).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#personalLinkError")).ToHaveTextAsync("Please enter a valid URL for your personal link.");
            await page.Locator("#personalLink").FillAsync("");
            await Assertions.Expect(page.Locator("#personalLinkError")).ToBeEmptyAsync();
            await page.Locator("#mediaContact").FillAsync("public@example.com");
            await Assertions.Expect(page.Locator("#accountEmail")).ToHaveValueAsync("private@example.com");
            await page.Locator("#mediaContact").FillAsync("");
            await Assertions.Expect(page.Locator("#mediaContact")).ToHaveValueAsync("");
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task Application_PhotoReplacementIgnoresLatePreparationAndPreservesTypingFocus()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply");
            await page.Locator("#profilePicInput[data-listener='true']").WaitForAsync(new() { State = WaitForSelectorState.Attached });
            await page.EvaluateAsync("""
                () => {
                    let count = 0;
                    const optimize = window.optimizeImageClient;
                    window.optimizeImageClient = async (...args) => {
                        const invocation = ++count;
                        if (invocation === 1) await new Promise(resolve => window.releaseOldPhoto = resolve);
                        const prepared = await optimize(...args);
                        if (invocation === 1) window.oldPhotoFinished = true;
                        else window.newPhoto = prepared.dataUrl;
                        return prepared;
                    };
                }
                """);
            using var client = App.CreateClient();
            var photo = new FilePayload { Name = "portrait.png", MimeType = "image/png", Buffer = await client.GetByteArrayAsync("/assets/logo.png") };
            await page.Locator("#profilePicInput").SetInputFilesAsync(photo);
            await page.WaitForFunctionAsync("() => typeof window.releaseOldPhoto === 'function'");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
            await page.Locator("#profilePicInput").SetInputFilesAsync(new FilePayload
            {
                Name = "replacement.jpg", MimeType = "image/jpeg",
                Buffer = await client.GetByteArrayAsync("/assets/content-images/play-athlete-placeholder.jpg")
            });
            await page.WaitForFunctionAsync("() => !applicationProfilePending && !!profilePic");
            await page.Locator("#accountEmail").FillAsync("still-typing@example.com");
            var selected = await page.EvaluateAsync<string>("profilePic");
            await page.EvaluateAsync("window.releaseOldPhoto()");
            await page.WaitForFunctionAsync("() => window.oldPhotoFinished === true");
            Assert.Equal(selected, await page.EvaluateAsync<string>("profilePic"));
            await Assertions.Expect(page.Locator("#accountEmail")).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#accountEmail")).ToHaveValueAsync("still-typing@example.com");
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task Application_RejectedPhotoPreservesPreviousSelectionAndAllowsRetry()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply?fake=1");
            await page.WaitForFunctionAsync("() => applicationReady");
            var selected = await page.Locator("#profileImage").GetAttributeAsync("src");
            await page.Locator("#profilePicInput").SetInputFilesAsync(new FilePayload
            {
                Name = "broken-photo.png", MimeType = "image/png", Buffer = [0, 1, 2, 3]
            });
            await Assertions.Expect(page.Locator("#profileUploadStatus")).ToHaveTextAsync("Profile picture upload failed. Please try another image.");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
            Assert.Equal(selected, await page.Locator("#profileImage").GetAttributeAsync("src"));
            await Assertions.Expect(page.Locator("#uploadButton")).ToBeEnabledAsync();
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task Application_MissingResultHasAnEditDestinationBeforeSubmission()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply?fake=1");
            await page.Locator("#profilePicInput[data-listener='true']").WaitForAsync(new() { State = WaitForSelectorState.Attached });
            var requests = 0;
            await page.RouteAsync("**/api/application/application", _ => { requests++; return Task.CompletedTask; });
            await Assertions.Expect(page.Locator("#calculatorLink")).ToHaveTextAsync("Calculate your result");
            await page.Locator("#nextButton").ClickAsync();
            await Assertions.Expect(page.Locator("#calculatorLink")).ToBeFocusedAsync();
            Assert.Equal(0, requests);
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task Application_PlaceholderRemainsVersioned()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply");
            Assert.Contains("/play-athlete-placeholder.jpg?v=", await page.Locator("#illustrationImage").GetAttributeAsync("src"));
            Assert.Contains("/play-athlete-placeholder.webp?v=", await page.Locator("#illustrationPicture source").GetAttributeAsync("srcset"));
            Assert.Empty(errors);
        });
    }
}
