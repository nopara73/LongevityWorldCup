using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Fact]
    public async Task CheckInResponse_ClosedDayClearsOnlyItsRemovedRemarkFromDiscussion()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = CheckInWorkspaceState();
        state["eligibleDays"]![0]!["existing"] = SavedCheckIn("Old remark.", 0);
        var otherDay = SavedCheckIn("Keep the other day's remark.", 2);
        otherDay["exercise"] = 1;
        otherDay["vices"] = 1;
        state["eligibleDays"]![2]!["existing"] = otherDay;
        var notes = JsonSerializer.SerializeToNode(new[]
        {
            Note("p1", "Browser Tester", 17, "2026-06-24", "Old remark."),
            Note("p1", "Browser Tester", 22, "2026-06-29", "Keep the other day's remark.")
        })!;
        state["notes"] = notes.DeepClone();
        state["public"]!["notes"] = notes.DeepClone();
        var cell = state["public"]!["leaderboard"]![0]!["cells"]!.AsArray().Single(cell => (int?)cell!["challengeDay"] == 17)!;
        cell["checkedIn"] = true;
        cell["score"] = 0;
        foreach (var habit in new[] { "sleep", "exercise", "nutrition", "vices" }) cell[habit] = 0;
        var page = await OpenCheckInWorkspaceAsync(context, state);
        await page.Locator(".lmx-checkin-switcher button[data-day='17']").ClickAsync();
        var removed = page.Locator("#lmxNotes > article[data-discussion-post-participant-id='p1'][data-discussion-post-challenge-day='17']");
        var retained = page.Locator("#lmxNotes > article[data-discussion-post-participant-id='p1'][data-discussion-post-challenge-day='22']");
        await Assertions.Expect(removed).ToHaveCountAsync(1);
        await Assertions.Expect(retained).ToHaveCountAsync(1);
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            state["eligibleDays"]!.AsArray().RemoveAt(0);
            foreach (var feed in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() })
                feed.Remove(feed.Single(note => (int?)note!["challengeDay"] == 17));
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await page.Locator(".lmx-checkin-card textarea").FillAsync("");
        await page.Locator(".lmx-checkin-card button[type='submit']").ClickAsync();
        await Assertions.Expect(removed).ToHaveCountAsync(0);
        await Assertions.Expect(retained).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".lmx-checkin-card")).ToHaveAttributeAsync("data-day", "21");
    }

    [Fact]
    public async Task CheckInResponse_AClosedDayCanConfirmThroughItsPersistedHistory()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = CheckInWorkspaceState();
        var page = await OpenCheckInWorkspaceAsync(context, state);
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            state["eligibleDays"]!.AsArray().RemoveAt(0);
            var cell = state["public"]!["leaderboard"]![0]!["cells"]!.AsArray().Single(cell => (int?)cell!["challengeDay"] == 17)!;
            cell["checkedIn"] = true;
            cell["score"] = 0;
            foreach (var habit in new[] { "sleep", "exercise", "nutrition", "vices" }) cell[habit] = 0;
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var form = page.Locator(".lmx-checkin-card");
        foreach (var habit in new[] { "sleep", "exercise", "nutrition", "vices" }) await AnswerHabitAsync(form, habit, "no");
        await form.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(form).ToHaveAttributeAsync("data-day", "21");
        await Assertions.Expect(form.Locator("[data-checkin-status]")).ToBeEmptyAsync();
        await Assertions.Expect(form.Locator(".lmx-answer-input:checked")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(320, 640, true, true)]
    [InlineData(390, 844, false, true)]
    [InlineData(844, 390, true, true)]
    [InlineData(160, 320, true, false)]
    public async Task CheckIn_LargeTextKeepsEachAnsweredCountAndSaveControlReadable(int width, int height, bool direct, bool largeText)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenCheckInWorkspaceAsync(context, direct: direct);
        if (largeText) await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        await page.EvaluateAsync("document.fonts.ready");
        var form = page.Locator(".lmx-checkin-card");
        Assert.True(await form.EvaluateAsync<bool>("""
            form => {
                const bounds = form.querySelector('.lmx-checkin-fields').getBoundingClientRect();
                return [...form.querySelectorAll('.lmx-answer-face')].every(face => {
                    const box = face.getBoundingClientRect();
                    return box.width >= 43.99 && box.height >= 43.99 && box.left >= bounds.left - 1 && box.right <= bounds.right + 1;
                });
            }
            """));
        foreach (var key in new[] { "sleep", "exercise", "nutrition", "vices" })
        {
            Assert.True(await form.Locator(".lmx-checkin-actions").EvaluateAsync<bool>("""
                actions => {
                    const count = actions.querySelector('[data-checkin-progress]');
                    const bounds = count.getBoundingClientRect();
                    const walker = document.createTreeWalker(count, NodeFilter.SHOW_TEXT);
                    let node;
                    while (node = walker.nextNode()) {
                        const range = document.createRange();
                        range.selectNodeContents(node);
                        if ([...range.getClientRects()].some(r => r.left < bounds.left - 1 || r.right > bounds.right + 1 || r.top < bounds.top - 1 || r.bottom > bounds.bottom + 1)) return false;
                    }
                    return actions.scrollWidth <= actions.clientWidth + 1;
                }
                """));
            await AnswerHabitAsync(form, key, "no");
        }
        await Assertions.Expect(form.Locator(".lmx-answer-input:checked")).ToHaveCountAsync(4);
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeEnabledAsync();
        var save = form.Locator("button[type='submit']");
        await save.ScrollIntoViewIfNeededAsync();
        var box = (await save.BoundingBoxAsync())!;
        Assert.InRange(box.X, 0, width - box.Width + 1);
        Assert.InRange(box.Y, 0, height - box.Height + 1);
        Assert.True(box.Width >= 44 && box.Height >= 44);
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task HabitHistory_TodayRetainsItsPracticeMarkerAndPerHabitAnswers(ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, ColorScheme = theme, TimezoneId = "UTC"
        });
        var state = JsonSerializer.SerializeToNode(BuildParticipantState(noEligibleDays: true))!.AsObject();
        var cells = state["public"]!["leaderboard"]![0]!["cells"]!.AsArray();
        var today = cells.Last()!;
        today["countsForScore"] = false;
        today["sleep"] = 2;
        today["exercise"] = 1;
        today["nutrition"] = 0;
        today["vices"] = 2;
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(DateTime.Parse("2026-06-29T09:00:00Z", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        await page.GotoAsync("/longevitymaxxing");
        await page.Locator(".lmx-habit-history > summary").ClickAsync();
        foreach (var (habit, answer) in new[] { ("sleep", "full"), ("exercise", "partial"), ("nutrition", "missed"), ("vices", "full") })
        {
            var cell = page.Locator($".lmx-dashboard-row:has(.lmx-dashboard-category.{habit}) .lmx-category-day[data-day='22']");
            await Assertions.Expect(cell).ToHaveClassAsync(new System.Text.RegularExpressions.Regex($@"\b{answer}\b"));
            await Assertions.Expect(cell).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bpractice\b"));
            Assert.Contains("inset", await cell.EvaluateAsync<string>("e => getComputedStyle(e).boxShadow"));
            Assert.Contains("3.5px", await cell.EvaluateAsync<string>("e => getComputedStyle(e).boxShadow"));
            await Assertions.Expect(cell).ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex("practice"));
        }
        var zero = page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.nutrition) .lmx-category-day[data-day='22']");
        await Assertions.Expect(zero).ToHaveAttributeAsync("aria-label", new System.Text.RegularExpressions.Regex("0 of 2"));
        var missing = page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.nutrition) .lmx-category-day[data-day='21']");
        await Assertions.Expect(missing).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bempty\b"));
        Assert.DoesNotContain("0 of 2", await missing.GetAttributeAsync("aria-label"));
    }

    [Theory]
    [InlineData("participant")]
    [InlineData("missing")]
    [InlineData("answers")]
    [InlineData("date")]
    [InlineData("photos")]
    public async Task CheckInResponse_MustConfirmTheParticipantDayAndSubmittedWorkBeforeClearingTheDraft(string mismatch)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = CheckInWorkspaceState();
        var page = await OpenCheckInWorkspaceAsync(context, state);
        var requests = 0;
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            requests++;
            var result = state.DeepClone().AsObject();
            var saved = SavedCheckIn("Keep this draft.", 0);
            saved["images"] = JsonSerializer.SerializeToNode(new[] { CheckInImage("/generated/longevitymaxxing/check-in-photos/walk.webp") });
            result["eligibleDays"]![0]!["existing"] = saved;
            if (requests == 1)
            {
                if (mismatch == "participant") result["participant"]!["id"] = "p-other";
                if (mismatch == "missing") result["eligibleDays"]![0]!["existing"] = null;
                if (mismatch == "answers") saved["sleep"] = 2;
                if (mismatch == "date") result["eligibleDays"]![0]!["date"] = "2026-06-25";
                if (mismatch == "photos") saved["images"] = new JsonArray();
            }
            await FulfillJsonAsync(route, result.ToJsonString());
        });
        var form = page.Locator(".lmx-checkin-card");
        foreach (var key in new[] { "sleep", "exercise", "nutrition", "vices" }) await AnswerHabitAsync(form, key, "no");
        await form.Locator("textarea").FillAsync("Keep this draft.");
        await AddCheckInPhotosAsync(form, "walk.png");
        var save = form.Locator("button[type='submit']");
        await Assertions.Expect(save).ToBeEnabledAsync();
        await save.ClickAsync();
        await Assertions.Expect(save).ToHaveTextAsync("Retry");
        await Assertions.Expect(form.Locator("[data-checkin-status]")).ToContainTextAsync("Not saved.");
        await Assertions.Expect(form.Locator("textarea")).ToHaveValueAsync("Keep this draft.");
        await Assertions.Expect(form.Locator("input[value='0']:checked")).ToHaveCountAsync(4);
        await Assertions.Expect(form.Locator("[data-photo-previews] img")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".lmx-checkin-switcher button[data-day='17']")).ToHaveAttributeAsync("aria-pressed", "true");
        await save.ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-checkin-switcher button[data-day='17'] em")).ToHaveTextAsync("Saved");
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData(320, 640, ColorScheme.Light, true)]
    [InlineData(390, 844, ColorScheme.Dark, true)]
    [InlineData(1280, 800, ColorScheme.Light, true)]
    [InlineData(1440, 900, ColorScheme.Dark, false)]
    public async Task CheckIn_FillsEachHabitAndLightsItsSaveRailSegment(int width, int height, ColorScheme theme, bool direct)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenCheckInWorkspaceAsync(context, direct: direct);
        var form = page.Locator(".lmx-checkin-card");
        var rail = form.Locator(".lmx-checkin-actions");
        const string railImage = "e => getComputedStyle(e, '::before').backgroundImage";

        // The four answers share one inset track; unchosen faces stay quiet.
        foreach (var key in new[] { "sleep", "exercise", "nutrition", "vices" })
        {
            var track = form.Locator($".lmx-question[data-key='{key}'] .lmx-answer-options");
            Assert.Equal("1px", await track.EvaluateAsync<string>("e => getComputedStyle(e).borderTopWidth"));
            Assert.NotEqual("rgba(0, 0, 0, 0)", await track.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
            Assert.All(
                await track.Locator(".lmx-answer-face").EvaluateAllAsync<string[]>("faces => faces.map(f => getComputedStyle(f).backgroundColor)"),
                color => Assert.Equal("rgba(0, 0, 0, 0)", color));
            Assert.Equal("rgba(0, 0, 0, 0)", await form.Locator($".lmx-question[data-key='{key}'] .lmx-question-icon")
                .EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
        }
        Assert.Equal("4px", await rail.EvaluateAsync<string>("e => getComputedStyle(e, '::before').height"));
        Assert.DoesNotContain("rgb(37, 99, 235)", await rail.EvaluateAsync<string>(railImage));

        var expected = new[]
        {
            (Key: "sleep", Accent: "rgb(37, 99, 235)"),
            (Key: "exercise", Accent: "rgb(220, 38, 38)"),
            (Key: "nutrition", Accent: "rgb(22, 163, 74)"),
            (Key: "vices", Accent: "rgb(124, 58, 237)")
        };
        foreach (var (key, accent) in expected)
        {
            Assert.DoesNotContain(accent, await rail.EvaluateAsync<string>(railImage));
            await AnswerHabitAsync(form, key, key == "exercise" ? "somewhat" : "yes");
            await Assertions.Expect(form.Locator($".lmx-question[data-key='{key}'] .lmx-question-icon"))
                .ToHaveCSSAsync("background-color", accent);
            Assert.Contains(accent, await rail.EvaluateAsync<string>(railImage));
        }

        Assert.All(
            await form.Locator(".lmx-answer-input:checked + .lmx-answer-face").EvaluateAllAsync<string[]>("faces => faces.map(f => getComputedStyle(f).backgroundColor)"),
            color => Assert.NotEqual("rgba(0, 0, 0, 0)", color));
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeEnabledAsync();
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(1440, ColorScheme.Light)]
    public async Task HabitHistory_FillsDaysWithEachHabitColour(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = theme,
            TimezoneId = "UTC"
        });
        await RouteChallengeResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(DateTime.Parse("2026-06-29T09:00:00Z", CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        await page.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await page.RouteAsync("**/api/longevitymaxxing/participant",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState(noEligibleDays: true))));
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await page.Locator(".lmx-habit-history > summary").ClickAsync();
        await page.Locator(".lmx-category-day.today").First.WaitForAsync();

        // Day 22: sleep and nutrition answered yes, exercise and vices somewhat.
        await Assertions.Expect(page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.sleep) .lmx-category-day[data-day='22']"))
            .ToHaveCSSAsync("background-color", "rgb(37, 99, 235)");
        await Assertions.Expect(page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.nutrition) .lmx-category-day[data-day='22']"))
            .ToHaveCSSAsync("background-color", "rgb(22, 163, 74)");
        Assert.Contains("rgb(220, 38, 38)", await page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.exercise) .lmx-category-day[data-day='22']")
            .EvaluateAsync<string>("e => getComputedStyle(e).backgroundImage"));
        Assert.Contains("rgb(124, 58, 237)", await page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.vices) .lmx-category-day[data-day='22']")
            .EvaluateAsync<string>("e => getComputedStyle(e).backgroundImage"));
        Assert.Equal("dashed", await page.Locator(".lmx-dashboard-row:has(.lmx-dashboard-category.sleep) .lmx-category-day.empty").First
            .EvaluateAsync<string>("e => getComputedStyle(e).borderTopStyle"));

        // The history head reads like the hero's other ruled section headings.
        var head = page.Locator(".lmx-title-panel .lmx-dashboard-head");
        Assert.Equal("0px", await head.EvaluateAsync<string>("e => getComputedStyle(e).marginTop"));
        Assert.Equal("1px", await head.Locator("h2").EvaluateAsync<string>("e => getComputedStyle(e, '::after').height"));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, ColorScheme.Light, "p1", "Browser Tester")]
    [InlineData(1440, ColorScheme.Dark, "p2", "Ari Able")]
    public async Task Leaderboard_MarksTheSignedInParticipantsOwnRow(int width, ColorScheme theme, string participantId, string name)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = theme,
            TimezoneId = "UTC"
        });
        await RouteChallengeResourcesAsync(context);
        var page = await context.NewPageAsync();
        var state = JsonSerializer.SerializeToNode(BuildParticipantState(noEligibleDays: true, includeMentionParticipants: true))!;
        state["participant"]!["id"] = participantId;
        state["participant"]!["displayName"] = name;
        await page.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState(includeMentionParticipants: true))));
        await page.RouteAsync("**/api/longevitymaxxing/participant",
            route => FulfillJsonAsync(route, state.ToJsonString()));
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await page.Locator("#lmxParticipantPanel:not(.lmx-hidden)").WaitForAsync();

        var self = page.Locator("#lmxBoard .lmx-board-row.lmx-board-row--self");
        await Assertions.Expect(self).ToHaveCountAsync(1);
        await Assertions.Expect(self.Locator(".lmx-name")).ToContainTextAsync(name);
        var colors = await page.Locator("#lmxBoard .lmx-board-row:not(.header)").EvaluateAllAsync<string[]>(
            "rows => rows.map(r => getComputedStyle(r).backgroundColor)");
        var selfColor = await self.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor");
        Assert.Single(colors, color => color == selfColor);
        var accentSurface = await LmxTokenColorAsync(page, "--lmx-surface-accent");
        Assert.Equal(accentSurface, selfColor);
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(1280, ColorScheme.Dark)]
    public async Task CatchUpSwitcher_RulesTheOpenDayWithTheAccentInsteadOfAnInverseTile(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenCheckInWorkspaceAsync(context);
        var switcher = page.Locator(".lmx-checkin-switcher");
        var open = switcher.Locator("button[aria-pressed='true']");
        var closed = switcher.Locator("button[aria-pressed='false']").First;
        Assert.Equal("rgba(0, 0, 0, 0)", await switcher.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
        var accent = await LmxTokenColorAsync(page, "--lmx-accent");
        await Assertions.Expect(open).ToHaveCSSAsync("border-top-color", accent);
        Assert.Contains("inset", await open.EvaluateAsync<string>("e => getComputedStyle(e).boxShadow"));
        await Assertions.Expect(open).ToHaveCSSAsync("background-color", await LmxTokenColorAsync(page, "--lmx-surface-accent"));
        Assert.NotEqual("rgba(0, 0, 0, 0)", await closed.EvaluateAsync<string>("e => getComputedStyle(e).borderTopColor"));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    private static Task<string> LmxTokenColorAsync(IPage page, string token) => page.EvaluateAsync<string>("""
        token => {
            const probe = document.createElement('span');
            probe.style.color = `var(${token})`;
            document.querySelector('.lmx-page').append(probe);
            const color = getComputedStyle(probe).color;
            probe.remove();
            return color;
        }
        """, token);
}
