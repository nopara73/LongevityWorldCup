using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(711, 452)]
    [InlineData(390, 500)]
    public async Task CheckIn_KeyboardFocusScrollsTheWholeAnswerIntoView(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenCheckInWorkspaceAsync(context);
        var radios = page.Locator(".lmx-checkin-card .lmx-question input[value='2']");
        for (var index = 0; index < 4; index++)
        {
            var radio = radios.Nth(index);
            await radio.FocusAsync();
            await page.Keyboard.PressAsync("Space");
            var visible = await radio.EvaluateAsync<bool>("""
                input => {
                    const face = input.nextElementSibling;
                    const box = face.getBoundingClientRect();
                    const native = input.getBoundingClientRect();
                    return input === document.activeElement && input.checked &&
                        Math.abs(native.height - box.height) < 1 &&
                        box.top >= 0 && box.bottom <= innerHeight &&
                        getComputedStyle(face).outlineStyle !== 'none';
                }
                """);
            Assert.True(visible, $"Habit {index + 1} must keep its entire focused face visible.");
        }
    }

    [Theory]
    [InlineData("animate")]
    [InlineData("frame")]
    public async Task RitualMotion_AnimationFailureCannotTurnAConfirmedSaveIntoAnError(string failure)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = CheckInWorkspaceState();
        var page = await OpenCheckInWorkspaceAsync(context, state);
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.EvaluateAsync("""
            failure => {
                if (failure === 'animate') Element.prototype.animate = () => { throw Error('Animation unavailable'); };
                else {
                    const frame = window.requestAnimationFrame;
                    window.requestAnimationFrame = callback => {
                        if (new Error().stack.includes('rollNumber')) throw Error('Frame unavailable');
                        return frame(callback);
                    };
                }
            }
            """, failure);
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            state["eligibleDays"]![0]!["existing"] = SavedCheckIn("", 2);
            var row = state["public"]!["leaderboard"]![0]!;
            row["currentStreak"] = 24;
            row["totalPoints"] = 102;
            row["checkedInDays"] = 22;
            var cell = row["cells"]!.AsArray().Single(c => c!["challengeDay"]!.GetValue<int>() == 17)!;
            cell["checkedIn"] = true;
            cell["score"] = 8;
            foreach (var key in new[] { "sleep", "exercise", "nutrition", "vices" }) cell[key] = 2;
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var form = page.Locator(".lmx-checkin-card");
        await AnswerAllHabitsAsync(form);
        await form.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-checkin-switcher button[data-day='17'] em")).ToHaveTextAsync("Saved");
        await Assertions.Expect(page.Locator("#lmxHeroHighlights strong[data-ops-key='streak']")).ToHaveTextAsync("24");
        await Assertions.Expect(page.Locator("#lmxHeroHighlights strong[data-ops-key='score']")).ToHaveTextAsync("102");
        await Assertions.Expect(page.Locator(".lmx-board-row--self .lmx-cell[data-day='17'] .lmx-cell-score")).ToHaveTextAsync("8");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".lmx-checkin-switcher button[data-day='17']").ClickAsync();
        await Assertions.Expect(form.Locator("[data-checkin-progress]")).ToHaveTextAsync("Saved");
        await Assertions.Expect(form.Locator("[data-checkin-status]")).ToBeEmptyAsync();
        await Assertions.Expect(form.Locator("input[value='2']:checked")).ToHaveCountAsync(4);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckInResponse_ConcurrentTimezoneAndCheckInKeepTheNewestState(bool lateCheckIn)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var state = CheckInWorkspaceState();
        foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("");
        var page = await OpenCheckInWorkspaceAsync(context, state);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        await page.RouteAsync("**/api/longevitymaxxing/participant", route =>
        {
            reads++;
            return FulfillJsonAsync(route, state.ToJsonString());
        });
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            state["eligibleDays"]![0]!["existing"] = SavedCheckIn("", 2);
            var row = state["public"]!["leaderboard"]![0]!;
            row["totalPoints"] = 102;
            row["currentStreak"] = 24;
            row["checkedInDays"] = 22;
            var cell = row["cells"]!.AsArray().Single(c => c!["challengeDay"]!.GetValue<int>() == 17)!;
            cell["checkedIn"] = true;
            cell["score"] = 8;
            foreach (var key in new[] { "sleep", "exercise", "nutrition", "vices" }) cell[key] = 2;
            var snapshot = state.ToJsonString();
            if (lateCheckIn) { entered.TrySetResult(); await release.Task; }
            await FulfillJsonAsync(route, snapshot);
        });
        await page.RouteAsync("**/api/longevitymaxxing/edit", async route =>
        {
            state["participant"]!["timeZoneId"] = "Europe/London";
            var snapshot = state.ToJsonString();
            if (!lateCheckIn) { entered.TrySetResult(); await release.Task; }
            await FulfillJsonAsync(route, snapshot);
        });
        async Task SubmitCheckIn()
        {
            await page.Locator("#lmxCheckinTab").ClickAsync();
            await page.Locator(".lmx-checkin-switcher button[data-day='17']").ClickAsync();
            await AnswerAllHabitsAsync(page.Locator(".lmx-checkin-card"));
            await page.Locator(".lmx-checkin-card button[type='submit']").ClickAsync();
        }
        async Task SubmitTimeZone()
        {
            await page.Locator("#lmxProfileTab").ClickAsync();
            await ChooseProfileTimeZoneAsync(page, "London", "Europe/London");
            await page.Locator("#lmxSaveProfileButton").ClickAsync();
        }
        try
        {
            if (lateCheckIn) await SubmitCheckIn(); else await SubmitTimeZone();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (lateCheckIn)
            {
                await SubmitTimeZone();
                await WaitForTimeZoneSaveAsync(page);
            }
            else
            {
                await SubmitCheckIn();
                await Assertions.Expect(page.Locator(".lmx-checkin-switcher button[data-day='17'] em")).ToHaveTextAsync("Saved");
            }
        }
        finally { release.TrySetResult(); }
        await page.WaitForFunctionAsync("() => !document.querySelector('.lmx-checkin-card button[aria-busy=true]') && document.querySelector('#lmxSaveProfileButton')?.getAttribute('aria-busy') !== 'true'");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator("#lmxProfileTab").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxEditTimeZone")).ToHaveValueAsync("Europe/London");
        await Assertions.Expect(page.Locator(".lmx-board-row--self .lmx-cell[data-day='17'] .lmx-cell-score")).ToHaveTextAsync("8");
        await Assertions.Expect(page.Locator("#lmxHeroHighlights strong[data-ops-key='score']")).ToHaveTextAsync("102");
        await Assertions.Expect(page.Locator("#lmxHeroHighlights strong[data-ops-key='streak']")).ToHaveTextAsync("24");
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await page.Locator(".lmx-checkin-switcher button[data-day='17']").ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-checkin-card input[value='2']:checked")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator(".lmx-checkin-card [data-checkin-progress]")).ToHaveTextAsync("Saved");
        Assert.True(reads > 0, "A response overtaken by another accepted state must re-read authoritative state.");
    }

    [Theory]
    [InlineData(false, 390)]
    [InlineData(true, 1440)]
    public async Task RitualMotion_RestingFilterUsesParticipantIdentityAndPreservesRanks(bool signedIn, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }
        });
        var state = CheckInWorkspaceState();
        var rows = state["public"]!["leaderboard"]!.AsArray();
        var resting = rows[0]!.DeepClone().AsObject();
        resting["participantId"] = "resting";
        resting["challengeInactive"] = true;
        rows.Add(resting);
        await context.AddInitScriptAsync("""
            window.rowAnimations = [];
            const animate = Element.prototype.animate;
            Element.prototype.animate = function (frames, options) {
                if (this.matches('.lmx-board-row'))
                    window.rowAnimations.push({ id: this.dataset.participantId, frames });
                return animate.call(this, frames, options);
            };
            """);
        IPage page;
        if (signedIn) page = await OpenCheckInWorkspaceAsync(context, state);
        else
        {
            await RouteChallengeResourcesAsync(context);
            await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
            page = await context.NewPageAsync();
            await page.GotoAsync("/longevitymaxxing");
        }
        var toggle = page.Locator("#lmxInactiveToggle");
        await Assertions.Expect(page.Locator(".lmx-board-row[data-participant-id]")).ToHaveCountAsync(1);
        var meta = await page.Locator("#lmxBoardMeta").InnerTextAsync();
        await toggle.ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-board-row[data-participant-id]")).ToHaveCountAsync(2);
        Assert.Equal(new[] { "p1", "resting" }, await page.Locator(".lmx-board-row[data-participant-id]").EvaluateAllAsync<string[]>("rows => rows.map(r => r.dataset.participantId)"));
        Assert.Equal(new[] { "resting" }, await page.EvaluateAsync<string[]>("window.rowAnimations.map(a => a.id)"));
        await Assertions.Expect(page.Locator(".lmx-board-row[data-participant-id='resting'] .lmx-rank")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".lmx-board-row[data-participant-id='p1'] .lmx-rank")).ToHaveTextAsync("#1");
        await Assertions.Expect(page.Locator("#lmxBoardMeta")).ToHaveTextAsync(meta);
        await toggle.ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-board-row[data-participant-id]")).ToHaveCountAsync(1);
        await Assertions.Expect(toggle).ToBeFocusedAsync();
    }
}

