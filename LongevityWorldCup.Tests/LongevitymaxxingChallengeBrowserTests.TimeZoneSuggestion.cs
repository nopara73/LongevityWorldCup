using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320, false)]
    [InlineData(390, false)]
    [InlineData(1280, true)]
    public async Task TimeZoneSuggestion_KeepPreservesTheSavedZoneAndSurvivesReload(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            TimezoneId = "Asia/Bangkok", ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var state = ProfileWorkspaceState(timeZone: "Europe/London");
        var page = await OpenProfileWorkspaceAsync(context, state);
        var writes = 0;
        await page.RouteAsync("**/api/longevitymaxxing/edit", route => { writes++; return route.AbortAsync(); });
        var prompt = page.Locator("#lmxTimeZoneSuggestion");
        await Assertions.Expect(prompt).ToBeVisibleAsync();
        await Assertions.Expect(prompt).ToContainTextAsync("Bangkok, Thailand");
        await Assertions.Expect(prompt).ToContainTextAsync("London, United Kingdom");
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/London");
        Assert.False(await ProfileExitGuardAsync(page));
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        await page.Locator("#lmxKeepTimeZoneButton").PressAsync("Enter");
        await Assertions.Expect(prompt).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lmxProfileTab")).ToBeFocusedAsync();
        await page.ReloadAsync();
        await page.Locator("#lmxProfileTab").ClickAsync();
        await Assertions.Expect(prompt).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/London");
        Assert.Equal(0, writes);

        // The same browser's choice must not silence a different participant.
        state["participant"]!["id"] = "p2";
        await page.ReloadAsync();
        await page.Locator("#lmxProfileTab").ClickAsync();
        await Assertions.Expect(prompt).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeZoneSuggestion_ReviewUsesTheExistingExplicitSaveAndRetry(bool failFirst)
    {
        await using var context = await NewContextAsync(Browser, App, new() { TimezoneId = "Asia/Bangkok" });
        var state = ProfileWorkspaceState(timeZone: "Europe/London");
        var page = await OpenProfileWorkspaceAsync(context, state);
        var submissions = new List<string>();
        await page.RouteAsync("**/api/longevitymaxxing/edit", route =>
        {
            submissions.Add(JsonNode.Parse(route.Request.PostData!)!["timeZoneId"]!.GetValue<string>());
            if (failFirst && submissions.Count == 1) return route.AbortAsync();
            state["participant"]!["timeZoneId"] = "Asia/Bangkok";
            return FulfillJsonAsync(route, state.ToJsonString());
        });
        await page.Locator("#lmxHomeTab").ClickAsync();
        await page.Locator("#lmxReviewTimeZoneButton").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#lmxProfileTab")).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(page.Locator("#lmxEditTimeZoneButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Asia/Bangkok");
        await Assertions.Expect(page.Locator("#lmxTimeZoneSuggestion")).ToBeHiddenAsync();
        Assert.Equal("Europe/London", state["participant"]!["timeZoneId"]!.GetValue<string>());
        Assert.Empty(submissions);
        Assert.True(await ProfileExitGuardAsync(page));
        var save = page.Locator("#lmxSaveProfileButton");
        await save.ClickAsync();
        if (failFirst)
        {
            await Assertions.Expect(save).ToHaveTextAsync("Retry");
            await Assertions.Expect(save).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Asia/Bangkok");
            Assert.True(await ProfileExitGuardAsync(page));
            await save.PressAsync("Enter");
        }
        await WaitForTimeZoneSaveAsync(page);
        await Assertions.Expect(save).ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(save).ToBeFocusedAsync();
        Assert.False(await ProfileExitGuardAsync(page));
        Assert.Equal(Enumerable.Repeat("Asia/Bangkok", failFirst ? 2 : 1), submissions);
        await page.ReloadAsync();
        await page.Locator("#lmxProfileTab").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxTimeZoneSuggestion")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("Asia/Calcutta", "Asia/Kolkata")]
    [InlineData("US/Eastern", "America/New_York")]
    [InlineData("Etc/UTC", "UTC")]
    [InlineData("Europe/London", "Europe/London")]
    public async Task TimeZoneSuggestion_EquivalentZonesAndSeasonalClockChangesStayQuiet(string savedZone, string deviceZone)
    {
        await using var context = await NewContextAsync(Browser, App, new() { TimezoneId = deviceZone });
        var page = await OpenProfileWorkspaceAsync(context, ProfileWorkspaceState(timeZone: savedZone));
        await Assertions.Expect(page.Locator("#lmxTimeZoneSuggestion")).ToBeHiddenAsync();
        foreach (var month in new[] { 1, 7 })
        {
            await page.Clock.SetFixedTimeAsync(new DateTime(2026, month, 15, 12, 0, 0, DateTimeKind.Utc));
            await page.EvaluateAsync("window.dispatchEvent(new Event('focus'))");
            await Assertions.Expect(page.Locator("#lmxTimeZoneSuggestion")).ToBeHiddenAsync();
        }
    }

    [Fact]
    public async Task TimeZoneSuggestion_DeviceChangesRefreshOnlyThePromptAndRespectUnfinishedEdits()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenProfileWorkspaceAsync(context, ProfileWorkspaceState(timeZone: "Europe/London"));
        var prompt = page.Locator("#lmxTimeZoneSuggestion");
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setTimezoneOverride", new Dictionary<string, object> { ["timezoneId"] = "Europe/London" });
        await page.EvaluateAsync("window.dispatchEvent(new Event('focus'))");
        await Assertions.Expect(prompt).ToBeHiddenAsync();
        await cdp.SendAsync("Emulation.setTimezoneOverride", new Dictionary<string, object> { ["timezoneId"] = "Asia/Bangkok" });
        await page.Locator("#lmxEditTimeZoneButton").FocusAsync();
        await page.EvaluateAsync("document.dispatchEvent(new Event('visibilitychange'))");
        await Assertions.Expect(prompt).ToContainTextAsync("Bangkok, Thailand");
        await Assertions.Expect(prompt).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#lmxEditTimeZoneButton")).ToBeFocusedAsync();
        await page.Locator("#lmxKeepTimeZoneButton").ClickAsync();
        await Assertions.Expect(prompt).ToBeHiddenAsync();
        await cdp.SendAsync("Emulation.setTimezoneOverride", new Dictionary<string, object> { ["timezoneId"] = "Asia/Tokyo" });
        await page.EvaluateAsync("window.dispatchEvent(new Event('focus'))");
        await Assertions.Expect(prompt).ToBeVisibleAsync();
        await Assertions.Expect(prompt).ToContainTextAsync("Tokyo, Japan");
        await ChooseProfileTimeZoneAsync(page, "Paris", "Europe/Paris");
        await Assertions.Expect(prompt).ToBeHiddenAsync();
        await page.EvaluateAsync("window.dispatchEvent(new Event('focus'))");
        await page.Locator("#lmxHomeTab").ClickAsync();
        await page.Locator("#lmxProfileTab").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/Paris");
        await Assertions.Expect(prompt).ToBeHiddenAsync();
        Assert.True(await ProfileExitGuardAsync(page));
    }

    [Fact]
    public async Task TimeZoneSuggestion_KeepSynchronizesAcrossTabsButADifferentSavedZoneCanPromptAgain()
    {
        await using var context = await NewContextAsync(Browser, App, new() { TimezoneId = "Asia/Bangkok" });
        var state = ProfileWorkspaceState(timeZone: "Europe/London");
        var first = await OpenProfileWorkspaceAsync(context, state);
        var second = await context.NewPageAsync();
        await second.GotoAsync("/longevitymaxxing");
        await second.Locator("#lmxProfileTab").ClickAsync();
        await Assertions.Expect(second.Locator("#lmxTimeZoneSuggestion")).ToBeVisibleAsync();
        await second.Locator("#lmxKeepTimeZoneButton").FocusAsync();
        await first.Locator("#lmxKeepTimeZoneButton").ClickAsync();
        await Assertions.Expect(second.Locator("#lmxTimeZoneSuggestion")).ToBeHiddenAsync();
        await Assertions.Expect(second.Locator("#lmxProfileTab")).ToBeFocusedAsync();
        state["participant"]!["timeZoneId"] = "Asia/Tokyo";
        await second.ReloadAsync();
        await second.Locator("#lmxProfileTab").ClickAsync();
        await Assertions.Expect(second.Locator("#lmxTimeZoneSuggestion")).ToBeVisibleAsync();
        await Assertions.Expect(second.Locator("#lmxKeepTimeZoneButton")).ToHaveTextAsync("Keep Tokyo");
    }

    [Fact]
    public async Task TimeZoneSuggestion_StorageFailureStillKeepsTheDecisionForTheOpenPage()
    {
        await using var context = await NewContextAsync(Browser, App, new() { TimezoneId = "Asia/Bangkok" });
        var page = await OpenProfileWorkspaceAsync(context, ProfileWorkspaceState(timeZone: "Europe/London"));
        await page.EvaluateAsync("() => { Storage.prototype.setItem = () => { throw new Error('Unavailable'); }; }");
        await page.Locator("#lmxKeepTimeZoneButton").ClickAsync();
        await page.Locator("#lmxHomeTab").ClickAsync();
        await page.Locator("#lmxProfileTab").ClickAsync();
        await page.EvaluateAsync("window.dispatchEvent(new Event('focus'))");
        await Assertions.Expect(page.Locator("#lmxTimeZoneSuggestion")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/London");
    }

    [Fact]
    public async Task TimeZoneSuggestion_UnknownDeviceZoneDoesNotGuessAMismatch()
    {
        await using var context = await NewContextAsync(Browser, App, new() { TimezoneId = "Europe/London" });
        await context.AddInitScriptAsync("""
            const original = Intl.DateTimeFormat.prototype.resolvedOptions;
            Intl.DateTimeFormat.prototype.resolvedOptions = function() { return { ...original.call(this), timeZone: undefined }; };
            """);
        var page = await OpenProfileWorkspaceAsync(context, ProfileWorkspaceState(timeZone: "Europe/London"));
        await Assertions.Expect(page.Locator("#lmxTimeZoneSuggestion")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task TimeZoneSuggestion_WaitsUntilTheProfileIsAvailableAfterDueCheckIns()
    {
        await using var context = await NewContextAsync(Browser, App, new() { TimezoneId = "Asia/Bangkok" });
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await context.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState(timeZoneId: "Europe/London"))));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("#lmxProfileTab")).ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(page.Locator("#lmxTimeZoneSuggestion")).ToBeHiddenAsync();
    }
}
