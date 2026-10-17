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
        var last = fixture.Service.GetPublicState(now).Notes[^1];
        var activity = DateTimeOffset.Parse(last.LastActivityAtUtc);
        var score = Math.Round(Math.Log2(last.ReplyCount + 1) - 4 * (now - activity).TotalDays, 9);
        var cursor = new LongevitymaxxingDiscussionFeedCursor(now.ToUnixTimeMilliseconds(), score,
            activity.ToUnixTimeMilliseconds(), last.ChallengeDay, participantId, null);
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
