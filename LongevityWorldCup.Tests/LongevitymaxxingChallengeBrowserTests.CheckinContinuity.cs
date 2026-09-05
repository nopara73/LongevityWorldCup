using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task CheckInDrafts_KeepSeparateDaysAndRestoreAReset(ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 }, ColorScheme = theme });
        var page = await OpenCheckInWorkspaceAsync(context);
        var form = page.Locator(".lmx-checkin-card");
        await AnswerHabitAsync(form, "sleep", "no");
        await AnswerHabitAsync(form, "exercise", "yes");
        const string note = "  A walk with @Ari tomorrow.  ";
        await form.Locator("textarea").First.FillAsync(note);
        await AddCheckInPhotosAsync(form, "walk.png");
        await page.Locator(".lmx-checkin-switcher button[data-day='21']").ClickAsync();
        Assert.Empty(await form.Locator(".lmx-answer-input:checked").AllAsync());
        Assert.Equal("", await form.Locator("textarea").First.InputValueAsync());
        await AnswerHabitAsync(form, "nutrition", "somewhat");
        await form.Locator("textarea").First.FillAsync("Another day's note.");
        await page.Locator(".lmx-checkin-switcher button[data-day='17']").ClickAsync();
        Assert.Equal(note, await form.Locator("textarea").First.InputValueAsync());
        Assert.True(await form.Locator("[data-key='sleep'] input[value='0']").IsCheckedAsync());
        Assert.True(await form.Locator("[data-key='exercise'] input[value='2']").IsCheckedAsync());
        Assert.Equal(2, await form.Locator(".lmx-answer-input:checked").CountAsync());
        Assert.Equal("walk.png", await form.Locator("[data-photo-previews] figcaption").InnerTextAsync());
        await Assertions.Expect(page.Locator(".lmx-checkin-switcher button[data-day='21'] em")).ToHaveTextAsync("In progress");
        Assert.True(await page.Locator(".lmx-checkin-switcher button[data-day='17']").EvaluateAsync<bool>("e => e === document.activeElement"));

        await form.GetByRole(AriaRole.Button, new() { Name = "Reset this check-in", Exact = true }).ClickAsync();
        Assert.Equal(0, await form.Locator(".lmx-answer-input:checked").CountAsync());
        Assert.Equal(0, await form.Locator("[data-photo-previews] img").CountAsync());
        Assert.Equal("", await form.Locator("textarea").First.InputValueAsync());
        Assert.All(await form.Locator(".lmx-plant").EvaluateAllAsync<string[]>("plants => plants.map(p => p.dataset.preview)"), value => Assert.Equal("", value));
        await form.GetByRole(AriaRole.Button, new() { Name = "Undo reset", Exact = true }).ClickAsync();
        Assert.Equal(note, await form.Locator("textarea").First.InputValueAsync());
        Assert.Equal(2, await form.Locator(".lmx-answer-input:checked").CountAsync());
        Assert.Equal(1, await form.Locator("[data-photo-previews] img").CountAsync());
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeDisabledAsync();
        await page.Locator(".lmx-checkin-switcher button[data-day='21']").ClickAsync();
        Assert.Equal("Another day's note.", await form.Locator("textarea").First.InputValueAsync());
        Assert.Equal(0, await form.Locator("[data-photo-previews] img").CountAsync());
    }

    [Fact]
    public async Task SavedCheckInEdits_SurviveTabsAndResetToPublishedValues()
    {
        var state = CheckInWorkspaceState();
        foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("Published note.");
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenCheckInWorkspaceAsync(context, state);
        await page.Locator("#lmxCheckinTab").ClickAsync();
        var form = page.Locator(".lmx-checkin-card");
        await AnswerHabitAsync(form, "sleep", "no");
        await form.Locator("textarea").First.FillAsync("Unpublished edit.");
        await page.Locator("#lmxProfileTab").ClickAsync();
        await page.Locator("#lmxHomeTab").ClickAsync();
        await page.Locator("#lmxCheckinTab").ClickAsync();
        Assert.True(await form.Locator("[data-key='sleep'] input[value='0']").IsCheckedAsync());
        Assert.Equal("Unpublished edit.", await form.Locator("textarea").First.InputValueAsync());
        await form.GetByRole(AriaRole.Button, new() { Name = "Reset this check-in", Exact = true }).ClickAsync();
        Assert.True(await form.Locator("[data-key='sleep'] input[value='1']").IsCheckedAsync());
        Assert.Equal("Published note.", await form.Locator("textarea").First.InputValueAsync());
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeDisabledAsync();
        await Assertions.Expect(form.Locator("[data-checkin-progress]")).ToHaveTextAsync("Saved");
    }

    [Fact]
    public async Task CheckInPhotoPicker_AppendsDeduplicatesAndExplainsRejectedFiles()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenCheckInWorkspaceAsync(context);
        var form = page.Locator(".lmx-checkin-card");
        await AddCheckInPhotosAsync(form, "walk.png");
        await AddCheckInPhotosAsync(form, "lunch.png", "walk.png", "report.txt");
        Assert.Equal(new[] { "walk.png", "lunch.png" }, await form.Locator("figcaption").AllTextContentsAsync());
        await Assertions.Expect(form.Locator("[data-photo-feedback]")).ToContainTextAsync("1 unsupported file skipped");
        await Assertions.Expect(form.Locator("[data-photo-feedback]")).ToContainTextAsync("1 photo already selected");
        await AddCheckInPhotosAsync(form, "three.png", "four.png", "five.png");
        Assert.Equal(4, await form.Locator("[data-photo-previews] img").CountAsync());
        await Assertions.Expect(form.Locator("[data-photo-feedback]")).ToContainTextAsync("4 photos maximum. 1 photo not added");
        await Assertions.Expect(form.Locator("[data-photo-button]")).ToBeDisabledAsync();
        await form.GetByRole(AriaRole.Button, new() { Name = "Remove lunch.png", Exact = true }).ClickAsync();
        await Assertions.Expect(form.Locator("[data-photo-button]")).ToBeEnabledAsync();
        Assert.True(await form.GetByRole(AriaRole.Button, new() { Name = "Remove three.png", Exact = true }).EvaluateAsync<bool>("e => e === document.activeElement"));
        await AddCheckInPhotosAsync(form, "five.png");
        Assert.Equal(new[] { "walk.png", "three.png", "four.png", "five.png" }, await form.Locator("figcaption").AllTextContentsAsync());
    }

    [Fact]
    public async Task CheckInSaveFailure_KeepsFilesAndAnswersForOneRetry()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = CheckInWorkspaceState();
        var page = await OpenCheckInWorkspaceAsync(context, state);
        var requests = new List<string>();
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route => {
            requests.Add(route.Request.PostData ?? "");
            if (requests.Count == 1) await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Connection interrupted. Try again.\"}" });
            else {
                state["eligibleDays"]![0]!["existing"] = SavedCheckIn("Keep this note.", 2);
                await FulfillJsonAsync(route, state.ToJsonString());
            }
        });
        var form = page.Locator(".lmx-checkin-card");
        await AnswerAllHabitsAsync(form);
        await form.Locator("textarea").First.FillAsync("Keep this note.");
        await AddCheckInPhotosAsync(form, "walk.png", "lunch.png");
        await form.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(form.Locator("[data-checkin-status]")).ToContainTextAsync("Not saved. Connection interrupted");
        Assert.Equal(2, await form.Locator("[data-photo-previews] img").CountAsync());
        Assert.Equal("Keep this note.", await form.Locator("textarea").First.InputValueAsync());
        await page.Locator(".lmx-checkin-switcher button[data-day='21']").ClickAsync();
        await page.Locator(".lmx-checkin-switcher button[data-day='17']").ClickAsync();
        await form.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await Assertions.Expect(form).ToHaveAttributeAsync("data-day", "21");
        Assert.Equal(2, requests.Count);
        var submissionIds = requests.Select(request => Regex.Match(request, "name=\"submissionId\"\\r\\n\\r\\n([^\\r\\n]+)").Groups[1].Value).ToArray();
        Assert.All(submissionIds, id => Assert.True(Guid.TryParse(id, out _)));
        Assert.Equal(submissionIds[0], submissionIds[1]);
        foreach (var request in requests) {
            Assert.Contains("Keep this note.", request);
            Assert.Contains("walk.webp", request);
            Assert.Contains("lunch.webp", request);
        }
        await page.Locator(".lmx-checkin-switcher button[data-day='17']").ClickAsync();
        Assert.Equal(0, await form.Locator("[data-photo-previews] img").CountAsync());
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task SlowCheckInSave_LocksTheSubmittedDayAndPreservesAnotherDaysWork()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = CheckInWorkspaceState();
        var page = await OpenCheckInWorkspaceAsync(context, state);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route => {
            requests++;
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            state["eligibleDays"]![0]!["existing"] = SavedCheckIn("Submitted day.", 2);
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var form = page.Locator(".lmx-checkin-card");
        await AnswerAllHabitsAsync(form);
        await form.Locator("textarea").First.FillAsync("Submitted day.");
        try {
            await form.Locator("button[type='submit']").ClickAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.All(await form.Locator(".lmx-checkin-entry input, .lmx-checkin-entry textarea, .lmx-checkin-entry button").EvaluateAllAsync<bool[]>("controls => controls.map(c => c.disabled)"), Assert.True);
            await form.EvaluateAsync("f => f.dispatchEvent(new Event('submit', {bubbles:true, cancelable:true}))");
            await page.Locator(".lmx-checkin-switcher button[data-day='21']").ClickAsync();
            await AnswerHabitAsync(form, "sleep", "yes");
            await form.Locator("textarea").First.FillAsync("Still writing this day.");
            await AddCheckInPhotosAsync(form, "another-day.png");
            await Assertions.Expect(form.Locator("[data-checkin-progress]")).ToHaveTextAsync("Saving Day 17…");
        } finally { release.TrySetResult(); }
        await Assertions.Expect(page.Locator(".lmx-checkin-switcher button[data-day='17'] em")).ToHaveTextAsync("Saved");
        Assert.Equal(1, requests);
        Assert.Equal("21", await form.GetAttributeAsync("data-day"));
        Assert.Equal("Still writing this day.", await form.Locator("textarea").First.InputValueAsync());
        Assert.True(await form.Locator("[data-key='sleep'] input[value='2']").IsCheckedAsync());
        Assert.Equal("another-day.png", await form.Locator("figcaption").InnerTextAsync());
    }

    [Theory]
    [InlineData(320, 720, ColorScheme.Light, false)]
    [InlineData(390, 844, ColorScheme.Dark, true)]
    [InlineData(844, 390, ColorScheme.Dark, true)]
    [InlineData(1280, 900, ColorScheme.Light, false)]
    public async Task CheckInSaveBar_StaysVisibleWhileAnsweringAndStopsBeforeDiscussion(int width, int height, ColorScheme theme, bool dialog)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = height }, ColorScheme = theme });
        var page = await OpenCheckInWorkspaceAsync(context, direct: dialog);
        var form = page.Locator(".lmx-checkin-card");
        await form.Locator(".lmx-question[data-key='nutrition']").EvaluateAsync("e => e.scrollIntoView({block:'center',behavior:'instant'})");
        var save = form.Locator("button[type='submit']");
        var box = await save.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.InRange(box.Y, 0, height - box.Height + 1);
        Assert.InRange(box.X, 0, width - box.Width + 1);
        Assert.True(box.Width >= 44 && box.Height >= 44);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        await AnswerAllHabitsAsync(form);
        await Assertions.Expect(save).ToBeEnabledAsync();
        await Assertions.Expect(form.Locator("[data-checkin-progress]")).ToHaveTextAsync("Ready to save");
        await form.Locator(".lmx-recent-remarks").EvaluateAsync("e => e.scrollIntoView({block:'start',behavior:'instant'})");
        var bar = (await form.Locator(".lmx-checkin-actions").BoundingBoxAsync())!;
        var discussion = (await form.Locator(".lmx-recent-remarks").BoundingBoxAsync())!;
        Assert.True(bar.Y + bar.Height <= discussion.Y + 1);
    }

    [Fact]
    public async Task UnpublishedCheckIn_PromptsBeforeLeavingAndResetClearsThePrompt()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenCheckInWorkspaceAsync(context);
        var form = page.Locator(".lmx-checkin-card");
        await AnswerHabitAsync(form, "sleep", "yes");
        await form.Locator("textarea").First.FillAsync("Do not lose this.");
        var dialogs = 0;
        page.Dialog += async (_, dialog) => { dialogs++; Assert.Equal("beforeunload", dialog.Type); await dialog.DismissAsync(); };
        await Assert.ThrowsAsync<PlaywrightException>(() => page.GotoAsync("/about"));
        Assert.Equal(1, dialogs);
        Assert.Equal("Do not lose this.", await form.Locator("textarea").First.InputValueAsync());
        await form.GetByRole(AriaRole.Button, new() { Name = "Reset this check-in", Exact = true }).ClickAsync();
        await page.GotoAsync("/about");
        Assert.Equal(1, dialogs);
    }

    [Fact]
    public async Task CheckInPhotoCapacity_IncludesPublishedPhotos()
    {
        var state = CheckInWorkspaceState();
        var existing = SavedCheckIn("Published note.");
        existing["images"] = JsonSerializer.SerializeToNode(new[] { CheckInImage("/generated/longevitymaxxing/check-in-photos/one.webp"), CheckInImage("/generated/longevitymaxxing/check-in-photos/two.webp"), CheckInImage("/generated/longevitymaxxing/check-in-photos/three.webp") });
        state["eligibleDays"]![0]!["existing"] = existing;
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenCheckInWorkspaceAsync(context, state);
        await page.Locator(".lmx-checkin-switcher button[data-day='17']").ClickAsync();
        var form = page.Locator(".lmx-checkin-card");
        await AddCheckInPhotosAsync(form, "four.png", "five.png");
        Assert.Equal(3, await form.Locator(".lmx-note-photo-grid.saved img").CountAsync());
        Assert.Equal(1, await form.Locator("[data-photo-previews] img").CountAsync());
        await Assertions.Expect(form.Locator("[data-photo-feedback]")).ToContainTextAsync("1 photo not added");
        await form.GetByRole(AriaRole.Button, new() { Name = "Reset this check-in", Exact = true }).ClickAsync();
        Assert.Equal(3, await form.Locator(".lmx-note-photo-grid.saved img").CountAsync());
        Assert.Equal(0, await form.Locator("[data-photo-previews] img").CountAsync());
        await Assertions.Expect(form.Locator("[data-photo-count]")).ToHaveTextAsync("1 slot left");
    }

    [Fact]
    public async Task ChangingAFailedCheckIn_StartsANewSubmission()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenCheckInWorkspaceAsync(context);
        var requests = new List<JsonObject>();
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route => {
            requests.Add(JsonNode.Parse(route.Request.PostData!)!.AsObject());
            await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Try again.\"}" });
        });
        var form = page.Locator(".lmx-checkin-card");
        await AnswerAllHabitsAsync(form);
        await form.Locator("textarea").First.FillAsync("First attempt.");
        await form.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(form.Locator("button[type='submit']")).ToHaveTextAsync("Retry");
        await form.Locator("textarea").First.FillAsync("Changed before retry.");
        await form.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(form.Locator("button[type='submit']")).ToHaveTextAsync("Retry");
        Assert.Equal(2, requests.Count);
        Assert.NotEqual(requests[0]["submissionId"]!.GetValue<string>(), requests[1]["submissionId"]!.GetValue<string>());
        Assert.Equal("Changed before retry.", requests[1]["note"]!.GetValue<string>());
    }

    private static JsonObject CheckInWorkspaceState() => JsonSerializer.SerializeToNode(BuildParticipantState(includeMissedCatchUpDay: true))!.AsObject();
    private static JsonNode SavedCheckIn(string note, int value = 1) => JsonSerializer.SerializeToNode(new { sleep = value, exercise = value, nutrition = value, vices = value, note, images = Array.Empty<object>() })!;

    private static async Task<IPage> OpenCheckInWorkspaceAsync(IBrowserContext context, JsonObject? state = null, bool direct = false)
    {
        state ??= CheckInWorkspaceState();
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, JsonSerializer.Serialize(BuildPublicState())));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync(direct ? "/longevitymaxxing?token=browser-token&checkin=1" : "/longevitymaxxing");
        await page.Locator(".lmx-checkin-card").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        return page;
    }

    private static Task AnswerHabitAsync(ILocator form, string key, string answer) => form.Locator($".lmx-question[data-key='{key}'] .lmx-answer-option[data-answer='{answer}']").ClickAsync();
    private static async Task AnswerAllHabitsAsync(ILocator form) {
        foreach (var key in new[] { "sleep", "exercise", "nutrition", "vices" }) await AnswerHabitAsync(form, key, "yes");
    }

    private static Task AddCheckInPhotosAsync(ILocator form, params string[] names) => form.Locator("[data-note-photos]").EvaluateAsync("""
        (input, names) => {
            const bytes = Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQAAAABJRU5ErkJggg=='), c => c.charCodeAt(0));
            const transfer = new DataTransfer();
            for (const name of names) transfer.items.add(new File([bytes], name, {type: name.endsWith('.png') ? 'image/png' : 'text/plain', lastModified: 123}));
            input.files = transfer.files;
            input.dispatchEvent(new Event('change', {bubbles:true}));
        }
        """, names);
}
