using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Fact]
    public async Task DiscussionFeed_PagesPastTheInitialWindowWithoutDuplicatesAsTimePasses()
    {
        using var fixture = TestChallengeFixture.Create();
        var token = await fixture.ConfirmParticipantAsync("feed@example.com", "Feed Tester");
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00.1234567Z");
        var participantId = fixture.Service.GetParticipantState(token, now).Participant.Id;
        SeedDiscussionFeed(fixture, participantId);
        Assert.Equal(100, fixture.Service.GetPublicState(now).Notes.Count);

        LongevitymaxxingDiscussionFeedCursor? cursor = null;
        var threads = new List<LongevitymaxxingDiscussionThread>();
        var pages = 0;
        do
        {
            var page = fixture.Service.GetDiscussionFeed(new(null, cursor), now.AddDays(pages++));
            Assert.InRange(page.Threads.Count, 1, 10);
            threads.AddRange(page.Threads);
            cursor = page.NextCursor;
            Assert.True(pages < 20, "Feed history must make forward progress.");
        } while (cursor is not null);

        Assert.Equal(125, threads.Count(thread => thread.Note is not null));
        Assert.Single(threads, thread => thread.SystemPost is not null);
        Assert.Equal(threads.Count, threads.Select(thread => thread.Note is { } note
            ? $"{note.ParticipantId}/{note.ChallengeDay}" : thread.SystemPost!.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 125).Reverse(), threads.Where(thread => thread.Note is not null).Select(thread => thread.Note!.ChallengeDay));
    }

    [Fact]
    public async Task DiscussionFeed_AcceptsTheBrowserWindowCursorAndPreservesPublicVisibility()
    {
        using var fixture = TestChallengeFixture.Create();
        var token = await fixture.ConfirmParticipantAsync("feed@example.com", "Feed Tester");
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        var participantId = fixture.Service.GetParticipantState(token, now).Participant.Id;
        SeedDiscussionFeed(fixture, participantId);
        fixture.Db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            // Reverse the sub-millisecond order: the browser sees these as simultaneous.
            command.CommandText = "UPDATE LongevitymaxxingCheckIns SET DiscussionUpdatedAtUtc = '2026-06-21T08:00:00.123' || printf('%04d', 126 - ChallengeDay) || '+00:00';";
            command.ExecuteNonQuery();
        });
        var state = fixture.Service.GetPublicState(now);
        var last = state.Notes[^1];
        Assert.Equal(26, last.ChallengeDay);
        var activity = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.Parse(last.LastActivityAtUtc).ToUnixTimeMilliseconds());
        var score = Math.Round(Math.Log2(last.ReplyCount + 1) - 4 * (now - activity).TotalDays, 9);
        var cursor = state.DiscussionSnapshot!.NextCursor!;
        Assert.Equal(score, cursor.Score, 8);
        Assert.Equal(activity.ToUnixTimeMilliseconds(), cursor.ActivityAtUnixMs);
        var older = fixture.Service.GetDiscussionFeed(new(null, cursor), now);
        Assert.Equal(Enumerable.Range(16, 10).Reverse(), older.Threads.Select(thread => thread.Note!.ChallengeDay));

        fixture.Db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "UPDATE LongevitymaxxingCheckIns SET CheckedInAtUtc = '2026-06-10T08:00:00.0000000+00:00' WHERE ChallengeDay > 1;";
            command.ExecuteNonQuery();
        });
        Assert.Single(fixture.Service.GetDiscussionFeed(new(null), now).Threads, thread => thread.Note is not null);
        Assert.Equal(10, fixture.Service.GetDiscussionFeed(new(token), now).Threads.Count);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Service.GetDiscussionFeed(new("invalid-token"), now));
    }

    [Fact]
    public void DiscussionFeed_RejectsMalformedCursors()
    {
        using var fixture = TestChallengeFixture.Create();
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        var valid = new LongevitymaxxingDiscussionFeedCursor(now.ToUnixTimeMilliseconds(), 0, now.ToUnixTimeMilliseconds(), 1, Guid.NewGuid().ToString("N"), null);
        foreach (var cursor in new[] {
            valid with { RankedAtUnixMs = -1 }, valid with { RankedAtUnixMs = long.MaxValue },
            valid with { ActivityAtUnixMs = -1 }, valid with { ActivityAtUnixMs = long.MaxValue },
            valid with { Score = double.NaN }, valid with { ChallengeDay = -1 },
            valid with { ParticipantId = "invalid" }, valid with { SystemPostId = "invalid" }
        }) Assert.Throws<InvalidOperationException>(() => fixture.Service.GetDiscussionFeed(new(null, cursor), now));
    }

    [Fact]
    public async Task DiscussionFeed_UnansweredRequestsLeadBothStateAndEveryPageWithoutLosingNormalHistory()
    {
        using var fixture = TestChallengeFixture.Create();
        var token = await fixture.ConfirmParticipantAsync("needs-advice@example.com", "Needs Advice");
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        var id = fixture.Service.GetParticipantState(token, now).Participant.Id;
        SeedDiscussionFeed(fixture, id);
        fixture.Db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = """
                INSERT INTO LongevitymaxxingAdviceRequests
                    (Id, ParticipantId, ChallengeDay, Status, TriggerRawScore, CharacterLimit, CreatedAtUtc,
                     Story, PublishedAtUtc, UpdatedAtUtc)
                SELECT lower(hex(randomblob(16))), ParticipantId, ChallengeDay, 'published', 4, 240,
                       UpdatedAtUtc, 'What would help me restart?', UpdatedAtUtc, UpdatedAtUtc
                FROM LongevitymaxxingCheckIns WHERE ChallengeDay <= 12;
                """;
            command.ExecuteNonQuery();
        });
        Assert.Equal(Enumerable.Range(1, 12).Reverse(), fixture.Service.GetPublicState(now).Notes.Take(12).Select(note => note.ChallengeDay));
        var first = fixture.Service.GetDiscussionFeed(new(null), now);
        Assert.True(first.NextCursor!.NeedsAdvice);
        Assert.All(first.Threads, thread => Assert.NotNull(thread.Note!.Advice));
        var all = first.Threads.ToList();
        var cursor = first.NextCursor;
        do
        {
            var page = fixture.Service.GetDiscussionFeed(new(null, cursor), now.AddHours(1));
            all.AddRange(page.Threads);
            cursor = page.NextCursor;
            Assert.True(all.Count <= 126);
        } while (cursor is not null);
        Assert.Equal(126, all.Count);
        Assert.Equal(126, all.Select(thread => thread.Note is { } note ? $"{note.ParticipantId}/{note.ChallengeDay}" : thread.SystemPost!.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 12).Reverse().Concat(Enumerable.Range(13, 113).Reverse()),
            all.Where(thread => thread.Note is not null).Select(thread => thread.Note!.ChallengeDay));
    }

    [Fact]
    public async Task DiscussionFeed_FirstAdviceUnpinsDeletingTheLeadRepinsAndWithdrawalClearsThePin()
    {
        using var fixture = TestChallengeFixture.Create();
        fixture.Config.LongevitymaxxingChallenge!.StartDate = "2026-06-20";
        var (owner, lower, higher, post, now) = await AdviceScenarioAsync(fixture);
        var rankedAt = now.AddDays(5);
        fixture.Db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = """
                WITH RECURSIVE days(day) AS (SELECT 8 UNION ALL SELECT day + 1 FROM days WHERE day < 109)
                INSERT INTO LongevitymaxxingCheckIns
                    (ParticipantId, ChallengeDay, ChallengeDate, Sleep, Exercise, Nutrition, Vices, Note,
                     DiscussionUpdatedAtUtc, CheckedInAtUtc, UpdatedAtUtc)
                SELECT @id, day, '2026-06-21', 2, 2, 2, 2, 'Newer ordinary discussion.', @time, @time, @time FROM days;
                INSERT INTO LongevitymaxxingDiscussionReplies
                    (Id, PostParticipantId, PostChallengeDay, AuthorParticipantId, Body, CreatedAtUtc)
                VALUES (@legacy, @id, 7, @id, 'A comment from before publication.', @legacyTime);
                """;
            command.Parameters.AddWithValue("@id", post.ParticipantId);
            command.Parameters.AddWithValue("@time", now.AddDays(3).ToString("o"));
            command.Parameters.AddWithValue("@legacyTime", now.AddDays(-3).ToString("o"));
            command.Parameters.AddWithValue("@legacy", Guid.NewGuid().ToString("N"));
            command.ExecuteNonQuery();
        });
        Assert.Equal(7, fixture.Service.GetPublicState(rankedAt).Notes[0].ChallengeDay);
        Assert.Equal(7, fixture.Service.GetDiscussionFeed(new(null), rankedAt).Threads[0].Note!.ChallengeDay);
        var first = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(lower, post.ParticipantId, 7, "Start with one walk.", first, IsAdvice: true), now);
        Assert.DoesNotContain(fixture.Service.GetPublicState(rankedAt).Notes, note => note.ChallengeDay == 7);
        Assert.All(fixture.Service.GetDiscussionFeed(new(null), rankedAt).Threads, thread => Assert.NotEqual(7, thread.Note?.ChallengeDay));
        var lead = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(higher, post.ParticipantId, 7, "Keep a single daily anchor.", lead, IsAdvice: true), now.AddMinutes(1));
        var deleted = fixture.Service.DeleteDiscussionReply(new(higher, lead), now.AddMinutes(2));
        Assert.Equal(7, deleted.Public.Notes[0].ChallengeDay);
        Assert.Null(deleted.Public.Notes[0].Advice!.ActiveAdviceReplyId);
        Assert.Contains(deleted.Public.Notes[0].Advice!.Replies, reply => reply.Id == first && reply.IsArchived);
        var advice = deleted.Public.Notes[0].Advice!;
        fixture.Service.WithdrawAdviceStory(new(owner, advice.Id, advice.Version), now.AddMinutes(3));
        Assert.DoesNotContain(fixture.Service.GetPublicState(rankedAt).Notes, note => note.ChallengeDay == 7);
    }

    private static void SeedDiscussionFeed(TestChallengeFixture fixture, string participantId)
    {
        fixture.Db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = """
                WITH RECURSIVE days(day) AS (SELECT 1 UNION ALL SELECT day + 1 FROM days WHERE day < 125)
                INSERT INTO LongevitymaxxingCheckIns
                    (ParticipantId, ChallengeDay, ChallengeDate, Sleep, Exercise, Nutrition, Vices, Note,
                     DiscussionUpdatedAtUtc, CheckedInAtUtc, UpdatedAtUtc)
                SELECT @participantId, day, '2026-06-21', 2, 2, 2, 2, 'A feed discussion.',
                       '2026-06-21T08:00:00.1230000+00:00', '2026-06-21T08:00:00.1230000+00:00', '2026-06-21T08:00:00.1230000+00:00'
                FROM days;
                """;
            command.Parameters.AddWithValue("@participantId", participantId);
            command.ExecuteNonQuery();
        });
    }
}
