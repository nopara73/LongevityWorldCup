using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(1280, ColorScheme.Light)]
    public async Task CheckInFeedback_UsesOneVisibleStateAndRetainsRetryAndSaveAnnouncements(int width, ColorScheme theme)
    {
        var state = CheckInWorkspaceState();
        foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("");
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme
        });
        var page = await OpenCheckInWorkspaceAsync(context, state);
        await page.Locator("#lmxCheckinTab").ClickAsync();
        var form = page.Locator(".lmx-checkin-card");
        var save = form.Locator("button[type='submit']");
        var progress = form.Locator("[data-checkin-progress]");
        var status = form.Locator("[data-checkin-status]");

        await Assertions.Expect(save).ToHaveTextAsync("Saved");
        await Assertions.Expect(save).ToBeDisabledAsync();
        await Assertions.Expect(status).ToBeEmptyAsync();
        await Assertions.Expect(progress).ToHaveTextAsync("Saved");
        Assert.True(await progress.EvaluateAsync<bool>("e => getComputedStyle(e).clip === 'rect(0px, 0px, 0px, 0px)'"));
        Assert.Equal("polite", await progress.GetAttributeAsync("aria-live"));
        await Assertions.Expect(form.Locator(":scope > h3")).ToBeHiddenAsync();
        var selectedDay = page.Locator(".lmx-checkin-switcher button[aria-pressed='true']");
        Assert.Contains("Check-in for", await form.GetAttributeAsync("aria-label"));
        Assert.True(await selectedDay.Locator("em").EvaluateAsync<bool>("e => getComputedStyle(e).clip === 'rect(0px, 0px, 0px, 0px)'"));

        var note = form.Locator("textarea");
        await note.FillAsync("Keep this edit through failure.");
        await Assertions.Expect(save).ToHaveTextAsync("Save");
        await Assertions.Expect(save).ToBeEnabledAsync();
        await Assertions.Expect(progress).ToBeEmptyAsync();
        await Assertions.Expect(status).ToBeEmptyAsync();

        var releaseSave = new TaskCompletionSource();
        var submitted = new TaskCompletionSource();
        var requests = 0;
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            requests++;
            if (requests == 1)
            {
                submitted.SetResult();
                await releaseSave.Task;
                await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Connection interrupted. Try again.\"}" });
            }
            else
            {
                state["eligibleDays"]!.AsArray().Last()!["existing"] = SavedCheckIn("");
                await FulfillJsonAsync(route, state.ToJsonString());
            }
        });
        try
        {
            await save.ClickAsync();
            await submitted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(save).ToHaveTextAsync("Saving…");
            await Assertions.Expect(save).ToBeDisabledAsync();
            await Assertions.Expect(save).ToHaveAttributeAsync("aria-busy", "true");
            await Assertions.Expect(progress).ToBeEmptyAsync();
            await Assertions.Expect(status).ToBeEmptyAsync();
        }
        finally { releaseSave.TrySetResult(); }

        await Assertions.Expect(save).ToHaveTextAsync("Retry");
        await Assertions.Expect(status).ToContainTextAsync("Not saved. Connection interrupted");
        await Assertions.Expect(status).ToBeVisibleAsync();
        await Assertions.Expect(note).ToHaveValueAsync("Keep this edit through failure.");
        await note.FillAsync("");
        await Assertions.Expect(save).ToHaveTextAsync("Retry");
        await Assertions.Expect(save).ToBeEnabledAsync();
        await Assertions.Expect(progress).ToBeEmptyAsync();
        await Assertions.Expect(status).ToContainTextAsync("Not saved. Connection interrupted");
        await Assertions.Expect(selectedDay.Locator("em")).ToHaveTextAsync("Not saved");
        await save.ClickAsync();
        await Assertions.Expect(save).ToHaveTextAsync("Saved");
        await Assertions.Expect(save).ToBeDisabledAsync();
        await Assertions.Expect(progress).ToHaveTextAsync("Saved");
        await Assertions.Expect(status).ToBeEmptyAsync();
        await Assertions.Expect(note).ToHaveValueAsync("");
        Assert.Equal(2, requests);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }
}
