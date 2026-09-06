using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(390, false)]
    [InlineData(1280, true)]
    public async Task ProfileTimeZone_ViewChangesKeepTheEditAndReturningToSavedValueClearsIt(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await OpenProfileWorkspaceAsync(context);
        var writes = 0;
        await page.RouteAsync("**/api/longevitymaxxing/edit", route =>
        {
            writes++;
            return FulfillJsonAsync(route, ProfileWorkspaceState().ToJsonString());
        });
        var save = page.Locator("#lmxSaveProfileButton");
        await Assertions.Expect(save).ToHaveAttributeAsync("aria-disabled", "true");
        await save.FocusAsync();
        await save.PressAsync("Enter");
        Assert.False(await ProfileExitGuardAsync(page));

        await ChooseProfileTimeZoneAsync(page, "London", "Europe/London");
        await Assertions.Expect(save).ToHaveAttributeAsync("aria-disabled", "false");
        Assert.True(await ProfileExitGuardAsync(page));
        await page.Locator("#lmxHomeTab").ClickAsync();
        await page.Locator("#lmxProfileTab").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/London");
        await Assertions.Expect(page.Locator("#lmxEditTimeZoneButton")).ToContainTextAsync("London");
        await Assertions.Expect(page.Locator("#lmxEditStatus")).ToBeEmptyAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        var dialogs = 0;
        if (width == 390)
        {
            page.Dialog += async (_, dialog) => { dialogs++; Assert.Equal("beforeunload", dialog.Type); await dialog.DismissAsync(); };
            await Assert.ThrowsAsync<PlaywrightException>(() => page.GotoAsync("/about"));
            Assert.Equal(1, dialogs);
            await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/London");
        }

        await ChooseProfileTimeZoneAsync(page, "UTC", "UTC");
        await Assertions.Expect(save).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.False(await ProfileExitGuardAsync(page));
        Assert.Equal(0, writes);
        if (width == 390) { await page.GotoAsync("/about"); Assert.Equal(1, dialogs); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileTimeZone_OtherStateUpdatesKeepAnEditButRefreshAnUnchangedField(bool edit)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = DiscussionWorkspaceState();
        state["eligibleDays"] = new JsonArray();
        var page = await OpenProfileWorkspaceAsync(context, state);
        if (edit) await ChooseProfileTimeZoneAsync(page, "London", "Europe/London");
        var updated = state.DeepClone().AsObject();
        updated["participant"]!["timeZoneId"] = "Asia/Tokyo";
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", route => FulfillJsonAsync(route, updated.ToJsonString()));
        await page.Locator("#lmxHomeTab").ClickAsync();
        var thread = DiscussionThread(page, "p2", 22);
        await thread.Locator("[data-discussion-reply]").ClickAsync();
        await thread.Locator("textarea").FillAsync("A separate discussion reply.");
        await thread.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(thread.Locator(".lmx-discussion-feedback")).ToHaveTextAsync("Reply posted.");
        await page.Locator("#lmxProfileTab").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync(edit ? "Europe/London" : "Asia/Tokyo");
        await Assertions.Expect(page.Locator("#lmxSaveProfileButton")).ToHaveAttributeAsync("aria-disabled", edit ? "false" : "true");
        if (edit) await ChooseProfileTimeZoneAsync(page, "Tokyo", "Asia/Tokyo");
        Assert.False(await ProfileExitGuardAsync(page));
    }

    [Fact]
    public async Task ProfileTimeZone_SaveResponseKeepsAnOpenSearchAndItsFocus()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenProfileWorkspaceAsync(context);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/edit", async route =>
        {
            started.TrySetResult(); await gate.Task;
            await FulfillJsonAsync(route, ProfileWorkspaceState(timeZone: "Europe/London").ToJsonString());
        });
        try
        {
            await ChooseProfileTimeZoneAsync(page, "London", "Europe/London");
            await page.Locator("#lmxSaveProfileButton").ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.Locator("#lmxEditTimeZoneButton").ClickAsync();
            var search = page.Locator("#lmxEditTimeZoneSearch");
            await search.FillAsync("Bangkok");
            gate.TrySetResult();
            await WaitForTimeZoneSaveAsync(page);
            await Assertions.Expect(search).ToHaveValueAsync("Bangkok");
            await Assertions.Expect(search).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#lmxEditTimeZoneButton")).ToHaveAttributeAsync("aria-expanded", "true");
            await search.PressAsync("ArrowDown");
            await search.PressAsync("Enter");
            await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Asia/Bangkok");
            await Assertions.Expect(page.Locator("#lmxSaveProfileButton")).ToHaveAttributeAsync("aria-disabled", "false");
        }
        finally { gate.TrySetResult(); }
    }

    [Theory]
    [InlineData("Bangkok", "Asia/Bangkok")]
    [InlineData("UTC", "UTC")]
    public async Task ProfileTimeZone_LateSaveKeepsTheLaterEditAndAcceptsAuthoritativeState(string query, string laterZone)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await OpenProfileWorkspaceAsync(context);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var submissions = new List<string>();
        await page.RouteAsync("**/api/longevitymaxxing/edit", async route =>
        {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            Assert.Equal("browser-token", payload["accessToken"]!.GetValue<string>());
            var zone = payload["timeZoneId"]!.GetValue<string>();
            submissions.Add(zone);
            if (submissions.Count == 1) { started.TrySetResult(); await gate.Task; }
            var response = ProfileWorkspaceState(timeZone: zone);
            response["participant"]!["displayName"] = "Updated participant";
            response["eligibleDays"] = new JsonArray(new JsonObject
            {
                ["challengeDay"] = 23, ["date"] = "2026-06-30", ["countsForScore"] = true,
                ["existing"] = JsonSerializer.SerializeToNode(SavedCheckIn(""))
            });
            await FulfillJsonAsync(route, response.ToJsonString());
        });
        try
        {
            await ChooseProfileTimeZoneAsync(page, "London", "Europe/London");
            var save = page.Locator("#lmxSaveProfileButton");
            await save.FocusAsync();
            await save.PressAsync("Enter");
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(save).ToBeFocusedAsync();
            await save.PressAsync("Enter");
            Assert.Single(submissions);
            await ChooseProfileTimeZoneAsync(page, query, laterZone);
            Assert.True(await ProfileExitGuardAsync(page));
            await page.Locator("#lmxProfilePictureButton").FocusAsync();
            gate.TrySetResult();
            await WaitForTimeZoneSaveAsync(page);
            await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync(laterZone);
            await Assertions.Expect(save).ToHaveAttributeAsync("aria-disabled", "false");
            await Assertions.Expect(page.Locator("#lmxProfilePictureButton")).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#lmxProfileIdentity")).ToContainTextAsync("Updated participant");
            await Assertions.Expect(page.Locator("#lmxCheckinList form")).ToHaveAttributeAsync("data-day", "23");
            Assert.True(await ProfileExitGuardAsync(page));
            await page.Locator("#lmxHomeTab").ClickAsync();
            await page.Locator("#lmxProfileTab").ClickAsync();
            await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync(laterZone);

            await save.ClickAsync();
            await WaitForTimeZoneSaveAsync(page);
            await Assertions.Expect(save).ToHaveAttributeAsync("aria-disabled", "true");
            await Assertions.Expect(save).ToBeFocusedAsync();
            Assert.False(await ProfileExitGuardAsync(page));
            Assert.Equal(new[] { "Europe/London", laterZone }, submissions);
        }
        finally { gate.TrySetResult(); }
    }

    [Theory]
    [InlineData("http", false)]
    [InlineData("network", true)]
    [InlineData("gateway", false)]
    public async Task ProfileTimeZone_FailureRetainsTheSelectionAndRetryUsesTheCurrentEdit(string failure, bool replace)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenProfileWorkspaceAsync(context);
        var submissions = new List<string>();
        await page.RouteAsync("**/api/longevitymaxxing/edit", async route =>
        {
            var zone = JsonNode.Parse(route.Request.PostData!)!["timeZoneId"]!.GetValue<string>();
            submissions.Add(zone);
            if (submissions.Count > 1) await FulfillJsonAsync(route, ProfileWorkspaceState(timeZone: zone).ToJsonString());
            else if (failure == "network") await route.AbortAsync();
            else await route.FulfillAsync(new()
            {
                Status = 503, ContentType = failure == "gateway" ? "text/html" : "application/json",
                Body = failure == "gateway" ? "<h1>Unavailable</h1>" : "{\"error\":\"Please try again.\"}"
            });
        });
        await ChooseProfileTimeZoneAsync(page, "London", "Europe/London");
        var save = page.Locator("#lmxSaveProfileButton");
        await save.ClickAsync();
        await Assertions.Expect(save).ToHaveTextAsync("Retry");
        await Assertions.Expect(save).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#lmxEditStatus")).Not.ToBeEmptyAsync();
        await page.Locator("#lmxHomeTab").ClickAsync();
        await page.Locator("#lmxProfileTab").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/London");
        await Assertions.Expect(save).ToHaveTextAsync("Retry");
        if (replace)
        {
            await ChooseProfileTimeZoneAsync(page, "Bangkok", "Asia/Bangkok");
            await Assertions.Expect(page.Locator("#lmxEditStatus")).ToBeEmptyAsync();
        }
        await save.ClickAsync();
        await WaitForTimeZoneSaveAsync(page);
        await Assertions.Expect(save).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.False(await ProfileExitGuardAsync(page));
        Assert.Equal(new[] { "Europe/London", replace ? "Asia/Bangkok" : "Europe/London" }, submissions);
    }

    [Fact]
    public async Task ProfileTimeZone_RevertingDuringAFailedSaveClearsObsoleteFeedback()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenProfileWorkspaceAsync(context);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/edit", async route =>
        {
            started.TrySetResult(); await gate.Task;
            await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"error\":\"Try again.\"}" });
        });
        try
        {
            await ChooseProfileTimeZoneAsync(page, "London", "Europe/London");
            await page.Locator("#lmxSaveProfileButton").ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await ChooseProfileTimeZoneAsync(page, "UTC", "UTC");
            Assert.True(await ProfileExitGuardAsync(page));
            await page.Locator("#lmxHomeTab").ClickAsync();
            gate.TrySetResult();
            await WaitForTimeZoneSaveAsync(page);
            await Assertions.Expect(page.Locator("#lmxHomeTab")).ToBeFocusedAsync();
            await page.Locator("#lmxProfileTab").ClickAsync();
            await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("UTC");
            await Assertions.Expect(page.Locator("#lmxSaveProfileButton")).ToHaveAttributeAsync("aria-disabled", "true");
            Assert.False(await ProfileExitGuardAsync(page));
        }
        finally { gate.TrySetResult(); }
    }

    [Fact]
    public async Task ProfileTimeZone_WrongParticipantResponseCannotReplaceTheCurrentProfile()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenProfileWorkspaceAsync(context);
        var wrong = ProfileWorkspaceState(timeZone: "Asia/Tokyo");
        wrong["participant"]!["id"] = "someone-else";
        wrong["participant"]!["displayName"] = "Different participant";
        await page.RouteAsync("**/api/longevitymaxxing/edit", route => FulfillJsonAsync(route, wrong.ToJsonString()));
        await ChooseProfileTimeZoneAsync(page, "London", "Europe/London");
        await page.Locator("#lmxSaveProfileButton").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxSaveProfileButton")).ToHaveTextAsync("Retry");
        await Assertions.Expect(page.Locator("#lmxProfileIdentity")).ToContainTextAsync("Browser Tester");
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/London");
        Assert.True(await ProfileExitGuardAsync(page));
    }

    [Fact]
    public async Task ProfileTimeZone_SavedAliasesDoNotCreateAFalseUnsavedEdit()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenProfileWorkspaceAsync(context, ProfileWorkspaceState(timeZone: "Asia/Calcutta"));
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Asia/Kolkata");
        await Assertions.Expect(page.Locator("#lmxSaveProfileButton")).ToHaveAttributeAsync("aria-disabled", "true");
        Assert.False(await ProfileExitGuardAsync(page));
    }

    private static async Task ChooseProfileTimeZoneAsync(IPage page, string query, string zone)
    {
        await page.Locator("#lmxEditTimeZoneButton").ClickAsync();
        await page.Locator("#lmxEditTimeZoneSearch").FillAsync(query);
        await page.Locator($"#lmxEditForm [data-time-zone='{zone}']").ClickAsync();
    }

    private static async Task WaitForTimeZoneSaveAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("#lmxSaveProfileButton")).Not.ToHaveAttributeAsync("aria-busy", "true");
        await Assertions.Expect(page.Locator("#lmxEditStatus")).ToBeEmptyAsync();
    }

    private static Task<bool> ProfileExitGuardAsync(IPage page) => page.EvaluateAsync<bool>("""
        () => { const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented; }
        """);
}
