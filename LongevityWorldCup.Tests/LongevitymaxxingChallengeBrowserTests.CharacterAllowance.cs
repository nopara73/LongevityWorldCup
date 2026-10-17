using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task CharacterAllowance_PreservesSavedNotesAndRepliesAboveTheCurrentAllowance(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 900 } });
        var state = ConversationWorkspaceState();
        var row = state["public"]!["leaderboard"]!.AsArray().Single(row => (string?)row!["participantId"] == "p1")!;
        row["totalPoints"] = 0;
        row["characterAllowance"] = new JsonObject { ["challengePoints"] = 0, ["ultimateLeagueAthletesBehind"] = 0, ["limit"] = 146 };
        foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn(new string('n', 300));
        foreach (var collection in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() })
            collection.Single(post => (string?)post!["participantId"] == "p7")!["replies"]!.AsArray()
                .Single(reply => (string?)reply!["id"] == "r6")!["body"] = new string('r', 300);
        var page = await OpenDiscussionWorkspaceAsync(context, state, dialog: true);
        var form = page.Locator("#lmxCheckinList form");
        await Assertions.Expect(form.Locator("[data-note-character-count]")).ToHaveTextAsync("300/300");
        await form.Locator(".lmx-question[data-key='nutrition'] .lmx-answer-input[value='1']").CheckAsync();
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeEnabledAsync();
        await form.Locator("textarea[id^='lmx-note-']").FillAsync(new string('n', 301));
        await Assertions.Expect(form.Locator("[data-note-character-count]")).ToHaveTextAsync("1 over");
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeDisabledAsync();
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-reply-id='r6'] [data-discussion-reply-edit]").ClickAsync();
        await thread.Locator("textarea").FillAsync(new string('e', 300));
        await Assertions.Expect(thread.Locator("[data-reply-count]")).ToHaveTextAsync("300/300");
        await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToBeEnabledAsync();
        await thread.Locator("textarea").FillAsync(new string('e', 301));
        await Assertions.Expect(thread.Locator("[data-reply-count]")).ToHaveTextAsync("1 over");
        await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToBeDisabledAsync();
    }

    [Theory]
    [InlineData(390, 0, 0, 146)]
    [InlineData(1440, 154, 0, 300)]
    [InlineData(390, 100, 23, 269)]
    [InlineData(1440, 154, 23, 323)]
    public async Task CharacterAllowance_UsesTheEarnedLimitForNotesRepliesAndMentions(int width, int points, int behind, int limit)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 900 } });
        var state = ConversationWorkspaceState();
        var row = state["public"]!["leaderboard"]!.AsArray().Single(row => (string?)row!["participantId"] == "p1")!;
        row["totalPoints"] = points;
        row["characterAllowance"] = new JsonObject { ["challengePoints"] = points, ["ultimateLeagueAthletesBehind"] = behind, ["limit"] = limit };
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var form = page.Locator("#lmxCheckinList form");
        var note = form.Locator("textarea[id^='lmx-note-']");
        await note.FillAsync(new string('n', limit));
        await Assertions.Expect(form.Locator("[data-note-character-count]")).ToHaveTextAsync($"{limit}/{limit}");
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeEnabledAsync();
        await note.FillAsync(new string('n', limit + 1));
        await Assertions.Expect(form.Locator("[data-note-character-count]")).ToHaveTextAsync("1 over");
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeDisabledAsync();
        await note.FillAsync(new string('n', limit));

        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        var reply = thread.Locator("textarea");
        await reply.FillAsync(new string('r', limit));
        await Assertions.Expect(thread.Locator("[data-reply-count]")).ToHaveTextAsync($"{limit}/{limit}");
        await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToBeEnabledAsync();
        Assert.Contains($"{behind} athletes behind you", await thread.Locator("[data-reply-count]").GetAttributeAsync("title"));
        await thread.Locator("[data-discussion-reply-id='r4'] [data-discussion-reply-to]").ClickAsync();
        await Assertions.Expect(reply).ToHaveValueAsync(new string('r', limit));
        await Assertions.Expect(thread.Locator("[data-discussion-draft] .lmx-status")).ToContainTextAsync("Make room");
        await reply.FillAsync(new string('r', limit + 1));
        await Assertions.Expect(thread.Locator("[data-reply-count]")).ToHaveTextAsync("1 over");
        await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToBeDisabledAsync();
        const string prefix = "A little more room for the conversation. ";
        await reply.FillAsync(prefix + new string('r', limit - prefix.Length));
        await Assertions.Expect(thread.Locator("[data-reply-count]")).ToHaveTextAsync($"{limit}/{limit}");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        if (Environment.GetEnvironmentVariable("LWC_CHARACTER_ARTIFACTS") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await thread.ScreenshotAsync(new() { Path = Path.Combine(directory, $"characters-{width}-{limit}.png") });
        }
    }
}
