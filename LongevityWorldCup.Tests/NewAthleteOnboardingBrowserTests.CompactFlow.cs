using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class NewAthleteOnboardingBrowserTests
{
    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task CompactApplication_ShortMotivationKeepsFourScreensAndVisibleTerms(int width)
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.SetViewportSizeAsync(width, 844);
            await page.GotoAsync("/apply?fake=1");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("#personalDetails")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#whyField")).ToBeVisibleAsync();
            await page.Locator("#why").FillAsync("   ");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
            await page.Locator("#why").FillAsync("Live well.");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();

            var headings = new[] { "1. Enter the arena", "2. Almost there", "3. Don't trust, verify", "4. Final details" };
            for (var stage = 1; stage <= headings.Length; stage++)
            {
                await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync(headings[stage - 1]);
                await Assertions.Expect(page.Locator(".sub-progress-container .stage:visible")).ToHaveCountAsync(4);
                await Assertions.Expect(page.Locator($"#subStage{stage}")).ToHaveClassAsync("stage active");
                Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
                if (stage < headings.Length) await page.Locator("#nextButton").ClickAsync();
            }

            await Assertions.Expect(page.Locator("#nextButton")).ToHaveTextAsync("Apply");
            await Assertions.Expect(page.Locator("#finalDetails")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#applyDetails")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#privacyDetails")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByText("Declining means no spot on the leaderboard.", new() { Exact = false })).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#privacyText")).ToBeHiddenAsync();
            await page.Locator(".application-privacy-story summary").PressAsync("Enter");
            await Assertions.Expect(page.Locator("#privacyText")).ToBeVisibleAsync();
            Assert.Contains("There’s no sport without spectators.", await page.Locator("#privacyText").InnerTextAsync());
            await page.Locator("#backButton").PressAsync("Enter");
            await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("3. Don't trust, verify");
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task CompactApplication_ContactFieldsValidateTogetherAndKeepPrivateEmailSeparate()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await GoToFakeApplicationFinalStageAsync(page);
            await page.Locator("#accountEmail").FillAsync("private@example.com");
            await page.Locator("#mediaContact").FillAsync("@alex");
            await Assertions.Expect(page.Locator("#accountEmail")).ToHaveValueAsync("private@example.com");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();

            await page.Locator("#mediaContact").FillAsync("");
            await page.Locator("#accountEmail").FillAsync("still-private@example.com");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
            await page.Locator("#mediaContact").FillAsync("public@example.com");
            await Assertions.Expect(page.Locator("#accountEmail")).ToHaveValueAsync("still-private@example.com");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();

            await page.Locator("#personalLink").FillAsync("invalid link");
            await page.Locator("#accountEmail").FillAsync("private@example.com");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
            await page.Locator("#personalLink").FillAsync("");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
            await page.Locator("#accountEmail").FillAsync("invalid-email");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
            await page.Locator("#accountEmail").FillAsync("private@example.com");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task CompactApplication_PendingPhotoCropCannotAdvanceOrChangeAnotherScreensValidation()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply?fake=1");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
            await page.Locator("#nextButton").ClickAsync();
            await page.EvaluateAsync("""
                () => {
                    window.Cropper = class {
                        destroy() {}
                        getCroppedCanvas() {
                            const canvas = document.createElement('canvas');
                            canvas.width = 20; canvas.height = 20;
                            return canvas;
                        }
                    };
                    const optimize = window.optimizeImageClient;
                    window.optimizeImageClient = async (...args) => {
                        await new Promise(resolve => window.releaseProfilePreparation = resolve);
                        return optimize(...args);
                    };
                }
                """);
            using var client = App.CreateClient();
            await page.Locator("#profilePicInput").SetInputFilesAsync(new FilePayload
            {
                Name = "portrait.png", MimeType = "image/png", Buffer = await client.GetByteArrayAsync("/assets/logo.png")
            });
            await Assertions.Expect(page.Locator("#croppingPart")).ToBeVisibleAsync();
            await page.Locator("#backButton").ClickAsync();
            await page.Locator("#nextButton").ClickAsync();
            await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
            await page.Locator("#cropButton").ClickAsync();
            await page.WaitForFunctionAsync("() => typeof window.releaseProfilePreparation === 'function'");
            await page.Locator("#backButton").ClickAsync();
            await page.Locator("#why").FillAsync("");
            await page.EvaluateAsync("window.releaseProfilePreparation()");
            await page.WaitForFunctionAsync("() => !applicationProfilePending");
            await Assertions.Expect(page.Locator("#nextButton")).ToBeDisabledAsync();
            await Assertions.Expect(page.Locator("#profileImage")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#why")).ToBeFocusedAsync();
            await page.Locator("#why").FillAsync("Live well.");
            await page.Locator("#nextButton").ClickAsync();
            await Assertions.Expect(page.Locator("#profileImage")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#nextButton")).ToBeEnabledAsync();
            Assert.Empty(errors);
        });
    }

    [Fact]
    public async Task CompactApplication_IllustrationsStayVersionedThroughStepChanges()
    {
        await RunOnboardingBrowserAsync(async (page, errors) =>
        {
            await page.GotoAsync("/apply");
            await FillApplicationIdentityDraftAsync(page);
            Assert.Contains("/enter-arena.jpg?v=", await page.Locator("#illustrationImage").GetAttributeAsync("src"));
            await page.Locator("#nextButton").ClickAsync();
            Assert.Contains("/headshot.jpg?v=", await page.Locator("#illustrationImage").GetAttributeAsync("src"));
            Assert.Contains("/headshot.webp?v=", await page.Locator("#illustrationPicture source[type='image/webp']").GetAttributeAsync("srcset"));
            await page.Locator("#backButton").ClickAsync();
            Assert.Contains("/enter-arena.jpg?v=", await page.Locator("#illustrationImage").GetAttributeAsync("src"));
            Assert.Empty(errors);
        });
    }
}
