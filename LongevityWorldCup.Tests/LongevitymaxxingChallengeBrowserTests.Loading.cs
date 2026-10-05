using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1280, "token")]
    [InlineData(390, "stored")]
    [InlineData(390, "confirm")]
    public async Task InitialCheckIn_ShowsOnlyLoadingUntilTheIntendedViewIsReady(int width, string access)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 844 }
        });
        await RouteChallengeResourcesAsync(context);
        if (access == "stored")
            await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        var scriptRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseScript = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var participantRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseParticipant = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/longevitymaxxing.js*", async route =>
        {
            scriptRequested.TrySetResult();
            await releaseScript.Task;
            await route.ContinueAsync();
        });
        var state = BuildParticipantState();
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, JsonSerializer.Serialize(BuildPublicState())));
        await context.RouteAsync(access == "confirm" ? "**/api/longevitymaxxing/confirm" : "**/api/longevitymaxxing/participant", async route =>
        {
            participantRequested.TrySetResult();
            await releaseParticipant.Task;
            await FulfillJsonAsync(route, access == "confirm"
                ? JsonSerializer.Serialize(new { accessToken = "browser-token", state })
                : JsonSerializer.Serialize(state));
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(access == "stored" ? "/longevitymaxxing" : $"/longevitymaxxing?{access}=browser-token", new() { WaitUntil = WaitUntilState.Commit });
            await scriptRequested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assertions.Expect(page.Locator("#lmx-title")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("#lmxSignupPanel")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#lmxHeroHighlights")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#lmxAccessLoadingPanel")).ToBeVisibleAsync();
            var screenshots = Path.Combine(FindRepositoryRoot(), ".artifacts", "initial-views");
            Directory.CreateDirectory(screenshots);
            await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"checkin-{width}-{access}-loading.png") });
            releaseScript.TrySetResult();
            await participantRequested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assertions.Expect(page.Locator("#lmxSignupPanel")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#lmxHeroHighlights")).ToBeHiddenAsync();
            await Assertions.Expect(page.Locator("#lmxAccessLoadingPanel")).ToBeVisibleAsync();
            releaseParticipant.TrySetResult();
            await Assertions.Expect(page.Locator("#lmxParticipantPanel.lmx-checkin-dialog-panel")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("main")).ToHaveAttributeAsync("aria-busy", "false");
            await Assertions.Expect(page.Locator("#lmxSignupPanel")).ToBeHiddenAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"checkin-{width}-{access}-ready.png") });
        }
        finally
        {
            releaseScript.TrySetResult();
            releaseParticipant.TrySetResult();
        }
    }

    [Fact]
    public async Task InitialCheckIn_DoesNotWaitForThePublicStateRequest()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        await RouteChallengeResourcesAsync(context);
        await context.RouteAsync("**/api/longevitymaxxing/state", r => r.AbortAsync());
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, JsonSerializer.Serialize(BuildParticipantState())));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await Assertions.Expect(page.Locator("#lmxParticipantPanel.lmx-checkin-dialog-panel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#lmxSignupPanel")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(401, true)]
    [InlineData(503, true)]
    [InlineData(503, false)]
    public async Task InitialAccessFailure_ExposesRecoveryInsteadOfLeavingThePageLoading(int status, bool publicAvailable)
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        await RouteChallengeResourcesAsync(context);
        await context.RouteAsync("**/api/longevitymaxxing/state", r => publicAvailable
            ? FulfillJsonAsync(r, JsonSerializer.Serialize(BuildPublicState()))
            : r.AbortAsync());
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => r.FulfillAsync(new()
        {
            Status = status,
            ContentType = "application/json",
            Body = "{\"message\":\"Could not load participant.\"}"
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await Assertions.Expect(page.Locator("main")).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(page.Locator("#lmxAccessLoadingPanel")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lmxResendPanel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#lmxResendEmail")).ToBeEditableAsync();
    }
}
