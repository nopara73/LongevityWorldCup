using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Fact]
    public async Task LandingBoard_EmailShapedUsernameStaysIncompleteAndCannotBeSubmitted()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        await context.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        var submissions = 0;
        await context.RouteAsync("**/api/longevitymaxxing/signup", route =>
        {
            submissions++;
            return FulfillJsonAsync(route, "{}");
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/longevitymaxxing");
        await Assertions.Expect(page.Locator(".lmx-page")).ToHaveAttributeAsync("aria-busy", "false");
        await page.Locator("#lmxSignupEmail").FillAsync("contact@example.com");
        var name = page.Locator("#lmxSignupName");
        await name.FillAsync("private@example.com");
        Assert.False(await name.EvaluateAsync<bool>("input => input.validity.valid"));
        await Assertions.Expect(name).ToHaveAttributeAsync("data-complete", "false");
        await page.Locator("#lmxSignupForm button[type=submit]").ClickAsync();
        Assert.Equal(0, submissions);
        await name.FillAsync("Public Person");
        Assert.True(await name.EvaluateAsync<bool>("input => input.validity.valid"));
        await Assertions.Expect(name).ToHaveAttributeAsync("data-complete", "true");
    }

    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task LandingBoard_SignedInDailyQuestionsFollowPendingCheckIns(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ColorScheme = theme
        });
        var noEligibleDays = false;
        await context.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        await context.RouteAsync("**/api/longevitymaxxing/participant",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildParticipantState(noEligibleDays: noEligibleDays))));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await Assertions.Expect(page.Locator(".lmx-page")).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(page.Locator("#lmxHabitGrid")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lmxQuestionPreview")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#lmxQuestionPreviewList .lmx-question-preview-item")).ToHaveCountAsync(4);
        noEligibleDays = true;
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".lmx-page")).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(page.Locator("#lmxQuestionPreview")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(1440, ColorScheme.Light, 2)]
    [InlineData(1024, ColorScheme.Dark, 2)]
    [InlineData(390, ColorScheme.Dark, 1)]
    [InlineData(320, ColorScheme.Light, 1)]
    public async Task LandingBoard_SignedOutHabitTilesCarryTheirOwnDailyQuestion(int width, ColorScheme theme, int columns)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        await context.RouteAsync("**/api/longevitymaxxing/state",
            route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/longevitymaxxing");
        await Assertions.Expect(page.Locator(".lmx-page")).ToHaveAttributeAsync("aria-busy", "false");

        // The four habits are named once: the separate question list stays hidden signed out.
        await Assertions.Expect(page.Locator("#lmxQuestionPreview")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lmxHabitGrid .lmx-habit-question")).ToHaveTextAsync(new[]
        {
            "Did you set yourself up for good sleep last night?",
            "Did you challenge or intentionally rest your body yesterday?",
            "Did you do what you reasonably could yesterday to eat in a way you consider healthy?",
            "Were your vices under control yesterday?"
        });

        var problems = await page.Locator("#lmxHabitGrid").EvaluateAsync<string[]>("""
            (grid, columns) => {
                const problems = [];
                const actual = getComputedStyle(grid).gridTemplateColumns.split(' ').length;
                if (actual !== columns) problems.push(`${actual} habit columns`);
                for (const card of grid.querySelectorAll('.lmx-habit-card')) {
                    const box = card.getBoundingClientRect();
                    const name = card.querySelector('span').getBoundingClientRect();
                    const question = card.querySelector('.lmx-habit-question');
                    const text = question.getBoundingClientRect();
                    if (text.top < name.bottom - 1) problems.push(`${card.className}: question is not under the name`);
                    if (Math.abs(text.left - name.left) > 1) problems.push(`${card.className}: question and name start apart`);
                    if (text.right > box.right + 1 || text.bottom > box.bottom + 1) problems.push(`${card.className}: question escapes the tile`);
                    if (getComputedStyle(question).textAlign === 'center') problems.push(`${card.className}: centred question`);
                }
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page overflows');
                return problems;
            }
            """, columns);
        Assert.True(problems.Length == 0, $"{width}px / {theme}: {string.Join("; ", problems)}");
    }

    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(1280, ColorScheme.Dark)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(320, ColorScheme.Dark)]
    public async Task LandingBoard_RestingRowsDropRanksAndMissedDaysAreQuiet(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        var state = JsonSerializer.SerializeToNode(BuildPublicState(includeMentionParticipants: true))!.AsObject();
        var rows = state["leaderboard"]!.AsArray();
        Assert.True(rows.Count >= 3);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]!;
            var checkedIn = index < 2;
            row["challengeInactive"] = index > 0;
            row["totalPoints"] = index == 0 ? 40 : 0;
            row["rank"] = 1;
            row["cells"] = new JsonArray(Enumerable.Range(1, 14).Select(day => (JsonNode)new JsonObject
            {
                ["challengeDay"] = day, ["checkedIn"] = checkedIn && day % 2 == 0, ["countsForScore"] = day != 1,
                ["score"] = checkedIn && day % 2 == 0 ? 8 : null,
                ["sleep"] = 2, ["exercise"] = 2, ["nutrition"] = 2, ["vices"] = 2
            }).ToArray());
        }
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await page.Locator("#lmxInactiveToggle").ClickAsync();
        var board = page.Locator("#lmxBoard");
        await Assertions.Expect(board.Locator(".lmx-board-row.inactive")).ToHaveCountAsync(rows.Count - 1);

        // Only the racing row has a rank; resting rows keep the slot so names stay aligned.
        await Assertions.Expect(board.Locator(".lmx-rank")).ToHaveTextAsync(new[] { "#1" });
        await Assertions.Expect(board.Locator(".lmx-board-row.inactive .lmx-rank")).ToHaveCountAsync(0);

        var result = await page.EvaluateAsync<JsonElement>("""
            () => {
                const rows = [...document.querySelectorAll('#lmxBoard .lmx-board-row:not(.header)')];
                const empty = document.querySelector('#lmxBoard .lmx-board-row:not(.header) .lmx-cell.empty');
                const style = getComputedStyle(empty);
                const labels = rows.map(row => row.querySelector('.lmx-participant-label').getBoundingClientRect().left);
                return {
                    emptyBorder: style.borderTopColor,
                    emptyBackground: style.backgroundColor,
                    labelSpread: Math.max(...labels) - Math.min(...labels),
                    quiet: rows.map(row => row.classList.contains('lmx-board-row--quiet')),
                    stripHeights: rows.map(row => row.querySelector('.lmx-cell-strip').getBoundingClientRect().height),
                    overflow: document.documentElement.scrollWidth - innerWidth
                };
            }
            """);
        var scenario = $"{width}px {theme}";
        Assert.Equal("rgba(0, 0, 0, 0)", result.GetProperty("emptyBorder").GetString());
        Assert.Equal("rgba(0, 0, 0, 0)", result.GetProperty("emptyBackground").GetString());
        Assert.True(result.GetProperty("labelSpread").GetDouble() <= 1, $"{scenario}: resting names start at a different edge.");
        var quiet = result.GetProperty("quiet").EnumerateArray().Select(value => value.GetBoolean()).ToArray();
        Assert.Equal(new[] { false, false }, quiet[..2]);
        Assert.All(quiet[2..], Assert.True);
        if (width <= 760)
        {
            var heights = result.GetProperty("stripHeights").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            Assert.True(heights[0] > 0, $"{scenario}: a row with check-ins lost its days.");
            Assert.All(heights[2..], height => Assert.Equal(0, height));
        }
        Assert.True(result.GetProperty("overflow").GetDouble() <= 0, $"{scenario}: the page scrolls sideways.");
    }

    [Theory]
    [InlineData(1440, ColorScheme.Light, "active")]
    [InlineData(390, ColorScheme.Dark, "active")]
    [InlineData(820, ColorScheme.Dark, "signup")]
    public async Task LandingBoard_EmptyBoardShowsTheRestingCrewUnderItsMessage(int width, ColorScheme theme, string phase)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        var state = JsonSerializer.SerializeToNode(BuildPublicState(includeMentionParticipants: true))!.AsObject();
        state["phase"] = phase;
        var template = state["leaderboard"]!.AsArray()[0]!;
        state["leaderboard"] = new JsonArray(Enumerable.Range(1, 10).Select(index =>
        {
            var row = template.DeepClone().AsObject();
            row["participantId"] = $"resting-{index}";
            row["displayName"] = $"Resting {index}";
            row["challengeInactive"] = true;
            return (JsonNode)row;
        }).ToArray());
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        var faces = page.Locator("#lmxBoard .lmx-empty-row .lmx-resting-faces");
        await Assertions.Expect(faces).ToBeVisibleAsync();
        await Assertions.Expect(faces.Locator(".lmx-participant-avatar")).ToHaveCountAsync(7);
        await Assertions.Expect(faces.Locator(".lmx-resting-more")).ToHaveTextAsync("+3");

        var layout = await page.EvaluateAsync<double[]>("""
            () => {
                const row = document.querySelector('#lmxBoard .lmx-empty-row').getBoundingClientRect();
                const message = document.querySelector('#lmxBoard .lmx-empty-name').getBoundingClientRect();
                const faces = document.querySelector('#lmxBoard .lmx-resting-faces').getBoundingClientRect();
                const style = getComputedStyle(document.querySelector('#lmxBoard .lmx-empty-row'));
                return [faces.top - message.bottom, (faces.left + faces.right) / 2 - (row.left + row.right) / 2,
                    parseFloat(style.borderTopWidth), document.documentElement.scrollWidth - innerWidth];
            }
            """);
        Assert.True(layout[0] >= 0, $"{width}px: the faces overlap the message.");
        Assert.True(Math.Abs(layout[1]) <= 4, $"{width}px: the faces are {layout[1]}px off centre.");
        Assert.Equal(0, layout[2]);
        Assert.True(layout[3] <= 0);

        // Showing the resting rows replaces the faces with the rows themselves.
        await page.Locator("#lmxInactiveToggle").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-resting-faces")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#lmxBoard .lmx-board-row.inactive")).ToHaveCountAsync(10);
    }
}
