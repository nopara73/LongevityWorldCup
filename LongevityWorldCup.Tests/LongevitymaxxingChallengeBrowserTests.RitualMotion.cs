using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    // Running animations (CSS transitions, CSS animations and Web Animations) inside the check-in card.
    private const string RunningCheckInMotion = """
        () => {
            const form = document.querySelector('.lmx-checkin-card');
            return document.getAnimations().filter(a => {
                const target = a.effect && a.effect.target;
                return a.playState === 'running' && target && form.contains(target);
            }).map(a => a.transitionProperty || a.animationName || 'web-animation');
        }
        """;

    // Logs every animation and transition that starts, so short motion is caught however slow the machine is.
    private const string MotionLogScript = """
        window.__lmxMotion = [];
        for (const type of ['animationstart', 'transitionrun'])
            document.addEventListener(type, e => window.__lmxMotion.push(e.animationName || e.propertyName), true);
        const animate = Element.prototype.animate;
        Element.prototype.animate = function (...args) {
            const name = this.getAttribute('class') || '';
            window.__lmxMotion.push('web-animation:' + name);
            return animate.apply(this, args);
        };
        """;

    private static async Task<string[]> TakeMotionLogAsync(IPage page) =>
        await page.EvaluateAsync<string[]>("() => window.__lmxMotion.splice(0)");

    [Theory]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(1280, ColorScheme.Light)]
    public async Task RitualMotion_HabitTapPressesFillsAndFillsTheRailSegment(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme
        });
        await context.AddInitScriptAsync(MotionLogScript);
        var page = await OpenCheckInWorkspaceAsync(context);
        var form = page.Locator(".lmx-checkin-card");
        var option = form.Locator(".lmx-question[data-key='exercise'] .lmx-answer-option[data-answer='somewhat']");
        var face = option.Locator(".lmx-answer-face");

        // Press: the face gives way under the pointer before the tap completes.
        await option.ScrollIntoViewIfNeededAsync();
        var box = (await option.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        await page.Mouse.DownAsync();
        await page.WaitForTimeoutAsync(150);
        var pressed = await face.EvaluateAsync<string>("e => getComputedStyle(e).scale");
        await page.Mouse.UpAsync();
        Assert.True(pressed == "0.9", $"Pressed face scale was {pressed}");

        // Fill: the chosen face washes in, its icon pops, and the rail segment grows in its habit colour.
        await page.WaitForFunctionAsync("() => window.__lmxMotion.includes('--lmx-fill-exercise')");
        var motion = await TakeMotionLogAsync(page);
        Assert.Contains("--lmx-rail-exercise", motion);
        Assert.Contains("transform", motion);
        Assert.Contains(motion, name => name.StartsWith("web-animation:lmx-question-icon"));
        Assert.Contains(motion, name => name.StartsWith("web-animation:lmx-answer-face"));
        await page.WaitForFunctionAsync("() => getComputedStyle(document.querySelector('.lmx-checkin-actions')).getPropertyValue('--lmx-fill-exercise').trim() === '1'");
        Assert.Equal("1", await face.EvaluateAsync<string>("e => getComputedStyle(e, '::before').opacity"));
        Assert.Equal("0", await form.Locator(".lmx-checkin-actions").EvaluateAsync<string>("e => getComputedStyle(e).getPropertyValue('--lmx-fill-sleep').trim()"));
    }

    [Fact]
    public async Task RitualMotion_FourthAnswerPlaysOneShortCompletionMoment()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        await context.AddInitScriptAsync(MotionLogScript);
        var page = await OpenCheckInWorkspaceAsync(context);
        var form = page.Locator(".lmx-checkin-card");
        foreach (var key in new[] { "sleep", "exercise", "nutrition" }) await AnswerHabitAsync(form, key, "yes");
        Assert.Null(await form.GetAttributeAsync("data-lmx-moment"));
        Assert.DoesNotContain("lmx-rail-ready", await TakeMotionLogAsync(page));

        await AnswerHabitAsync(form, "vices", "yes");
        Assert.Equal("complete", await form.GetAttributeAsync("data-lmx-moment"));
        await page.WaitForFunctionAsync("() => ['lmx-rail-ready', 'lmx-save-ready'].every(name => window.__lmxMotion.includes(name))");
        Assert.True(await page.EvaluateAsync<bool>("() => document.getAnimations().filter(a => a.animationName === 'lmx-rail-ready').every(a => a.effect.getComputedTiming().duration <= 800)"));

        // The moment is short and plays once: it clears itself and changing an answer does not replay it.
        await Assertions.Expect(form).Not.ToHaveAttributeAsync("data-lmx-moment", "complete", new() { Timeout = 2000 });
        await TakeMotionLogAsync(page);
        await AnswerHabitAsync(form, "vices", "somewhat");
        Assert.Null(await form.GetAttributeAsync("data-lmx-moment"));
        Assert.DoesNotContain("lmx-rail-ready", await TakeMotionLogAsync(page));
    }

    [Theory]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(1280, ColorScheme.Dark)]
    public async Task RitualMotion_SaveIsOptimisticAndRollsBackGracefully(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme
        });
        var state = CheckInWorkspaceState();
        await context.AddInitScriptAsync(MotionLogScript);
        var page = await OpenCheckInWorkspaceAsync(context, state);
        var form = page.Locator(".lmx-checkin-card");
        var save = form.Locator("button[type='submit']");
        var boardDay = page.Locator(".lmx-board-row--self .lmx-cell[data-day='17']");
        var historyDay = page.Locator("#lmxTrack .lmx-category-day[data-day='17']").First;
        await Assertions.Expect(boardDay).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bempty\b"));
        await AnswerAllHabitsAsync(form);

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Connection interrupted. Try again.\"}" });
        });
        try
        {
            await save.ClickAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // The tap never waits on the network: the day reads saved and lands on the board and history at once.
            await Assertions.Expect(save).ToHaveTextAsync("Saved");
            await Assertions.Expect(save).ToHaveAttributeAsync("aria-busy", "true");
            await Assertions.Expect(form.Locator("[data-checkin-progress]")).ToBeEmptyAsync();
            await Assertions.Expect(boardDay).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bempty\b"));
            await Assertions.Expect(boardDay).ToHaveAttributeAsync("data-score-pending", "true");
            await Assertions.Expect(historyDay).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bfull\b"));
            Assert.Equal("saved", await form.GetAttributeAsync("data-lmx-moment"));
        }
        finally { release.TrySetResult(); }

        // Rollback: the optimistic day leaves again, Retry nudges once, and the answers stay.
        await Assertions.Expect(save).ToHaveTextAsync("Retry");
        await Assertions.Expect(form).ToHaveAttributeAsync("data-lmx-moment", "rollback");
        await Assertions.Expect(boardDay).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bempty\b"));
        await Assertions.Expect(historyDay).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bempty\b"));
        await Assertions.Expect(form.Locator("[data-checkin-status]")).ToContainTextAsync("Not saved. Connection interrupted");
        Assert.True(await form.Locator(".lmx-question[data-key='vices'] input[value='2']").IsCheckedAsync());
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    [Fact]
    public async Task RitualMotion_ConfirmedSaveRollsTheStreakAndMovesTheBoardRow()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        var state = CheckInWorkspaceState();
        var leaderboard = state["public"]!["leaderboard"]!.AsArray();
        var self = leaderboard[0]!.AsObject();
        var rival = self.DeepClone().AsObject();
        rival["participantId"] = "rival";
        rival["displayName"] = "Rival Tester";
        rival["athleteUrl"] = null;
        rival["totalPoints"] = self["totalPoints"]!.GetValue<int>() + 4;
        leaderboard.Insert(0, rival);
        rival["rank"] = 1;
        self["rank"] = 2;
        await context.AddInitScriptAsync(MotionLogScript);
        var page = await OpenCheckInWorkspaceAsync(context, state);
        var form = page.Locator(".lmx-checkin-card");
        var streak = page.Locator("#lmxHeroHighlights strong[data-ops-key='streak']");
        var startStreak = int.Parse(await streak.InnerTextAsync());
        await AnswerAllHabitsAsync(form);

        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            state["eligibleDays"]![0]!["existing"] = SavedCheckIn("", 2);
            var row = state["public"]!["leaderboard"]!.AsArray().First(r => r!["participantId"]!.GetValue<string>() == "p1")!;
            row["currentStreak"] = startStreak + 3;
            row["totalPoints"] = row["totalPoints"]!.GetValue<int>() + 8;
            var cell = row["cells"]!.AsArray().FirstOrDefault(c => c!["challengeDay"]!.GetValue<int>() == 17);
            if (cell is not null) { cell["checkedIn"] = true; cell["score"] = 8; cell["sleep"] = 2; cell["exercise"] = 2; cell["nutrition"] = 2; cell["vices"] = 2; }
            var rows = state["public"]!["leaderboard"]!.AsArray();
            var moved = rows[1]!;
            rows.RemoveAt(1);
            rows.Insert(0, moved);
            rows[0]!["rank"] = 1;
            rows[1]!["rank"] = 2;
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await form.Locator("button[type='submit']").ClickAsync();

        // The streak counts up instead of jumping, and the participant's row glides above the rival.
        await page.WaitForFunctionAsync("() => document.querySelector(\"#lmxHeroHighlights strong[data-ops-key='streak']\")?.dataset.lmxRolling === 'up'");
        await page.WaitForFunctionAsync("() => document.querySelector(\".lmx-board-row[data-participant-id='p1']\")?.dataset.lmxMoving === 'up'");
        await Assertions.Expect(streak).ToHaveTextAsync((startStreak + 3).ToString());
        await Assertions.Expect(streak).Not.ToHaveAttributeAsync("data-lmx-rolling", new System.Text.RegularExpressions.Regex(".*"));
        Assert.Equal("p1", await page.Locator(".lmx-board-row[data-participant-id]").First.GetAttributeAsync("data-participant-id"));

        // Opening the habit history staggers its days in.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#lmxQuoteDialog")).ToBeHiddenAsync();
        await page.Locator(".lmx-habit-history summary").ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-dashboard-grid")).ToHaveAttributeAsync("data-lmx-entering", "true");
        Assert.True(await page.EvaluateAsync<int>("() => document.getAnimations().filter(a => a.effect?.target?.classList?.contains('lmx-category-day')).length") > 10);
    }

    [Theory]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(1280, ColorScheme.Light)]
    public async Task RitualMotion_ReducedMotionKeepsEveryStateWithoutMovement(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(MotionLogScript);
        var page = await OpenCheckInWorkspaceAsync(context);
        var form = page.Locator(".lmx-checkin-card");
        await AnswerAllHabitsAsync(form);
        // Every state lands at once: the rail is full, nothing animates, and no moment plays.
        Assert.Empty(await page.EvaluateAsync<string[]>(RunningCheckInMotion));
        Assert.DoesNotContain(await TakeMotionLogAsync(page), name => name.StartsWith("web-animation") || name.StartsWith("lmx-") || name.StartsWith("--lmx-"));
        Assert.Null(await form.GetAttributeAsync("data-lmx-moment"));
        Assert.Equal("1", await form.Locator(".lmx-checkin-actions").EvaluateAsync<string>("e => getComputedStyle(e).getPropertyValue('--lmx-fill-vices').trim()"));

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Connection interrupted. Try again.\"}" });
        });
        try
        {
            await form.Locator("button[type='submit']").ClickAsync();
            // Optimistic feedback still arrives instantly, just without motion.
            await Assertions.Expect(form.Locator("button[type='submit']")).ToHaveTextAsync("Saved");
            Assert.Null(await form.GetAttributeAsync("data-lmx-moment"));
            Assert.Equal(0, await page.EvaluateAsync<int>("() => document.getAnimations().filter(a => a.playState === 'running' && a.effect?.target?.closest?.('.lmx-board, #lmxTrack, #lmxHeroHighlights, .lmx-checkin-card')).length"));
        }
        finally { release.TrySetResult(); }
        await Assertions.Expect(form.Locator("button[type='submit']")).ToHaveTextAsync("Retry");
        Assert.Null(await form.GetAttributeAsync("data-lmx-moment"));
    }

    [Fact]
    public async Task RitualMotion_UndoRewindsTheAnswersItChanges()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 900 } });
        var state = CheckInWorkspaceState();
        foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("");
        await context.AddInitScriptAsync(MotionLogScript);
        var page = await OpenCheckInWorkspaceAsync(context, state);
        await page.Locator("#lmxCheckinTab").ClickAsync();
        var form = page.Locator(".lmx-checkin-card");
        await AnswerHabitAsync(form, "sleep", "yes");
        await page.WaitForTimeoutAsync(400);
        await TakeMotionLogAsync(page);
        await form.Locator("[data-checkin-reset]").ClickAsync();

        // Reset walks the changed answer back: its question rewinds and its old face washes in again.
        Assert.Equal("true", await form.Locator(".lmx-question[data-key='sleep']").GetAttributeAsync("data-lmx-rewind"));
        Assert.Null(await form.Locator(".lmx-question[data-key='exercise']").GetAttributeAsync("data-lmx-rewind"));
        var motion = await TakeMotionLogAsync(page);
        Assert.Contains(motion, name => name.StartsWith("web-animation:lmx-button secondary lmx-checkin-reset"));
        Assert.Contains(motion, name => name.StartsWith("web-animation:lmx-question-icon"));
        // The two sleep faces trade their colour wash instead of swapping instantly.
        Assert.Contains("opacity", motion);
        Assert.Contains("transform", motion);
        Assert.True(await form.Locator(".lmx-question[data-key='sleep'] input[value='1']").IsCheckedAsync());
        await Assertions.Expect(form.Locator("[data-checkin-reset]")).ToHaveTextAsync("Undo");
    }
}
