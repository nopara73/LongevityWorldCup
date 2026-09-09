using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Fact]
    public async Task DiscussionLinks_ReadThreadsOutsideTheFeedWithoutChangingPublicVisibility()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var postedAt = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var state = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access, 12, 2, 2, 2, 2, "The linked discussion."), postedAt);
        var participantId = state.Participant.Id;
        fixture.Db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = """
                WITH RECURSIVE days(day) AS (SELECT 13 UNION ALL SELECT day + 1 FROM days WHERE day < 113)
                INSERT INTO LongevitymaxxingCheckIns
                    (ParticipantId, ChallengeDay, ChallengeDate, Sleep, Exercise, Nutrition, Vices, Note,
                     DiscussionUpdatedAtUtc, CheckedInAtUtc, UpdatedAtUtc)
                SELECT @participantId, day, '2026-06-21', 2, 2, 2, 2, 'A more recent discussion.',
                       '2026-06-21T08:00:00.0000000+00:00', '2026-06-21T08:00:00.0000000+00:00', '2026-06-21T08:00:00.0000000+00:00'
                FROM days;
                """;
            cmd.Parameters.AddWithValue("@participantId", participantId);
            cmd.ExecuteNonQuery();
        });
        var feed = fixture.Service.GetPublicState(postedAt.AddDays(2));
        Assert.Equal(100, feed.Notes.Count);
        Assert.DoesNotContain(feed.Notes, note => note.ChallengeDay == 12);
        var linked = fixture.Service.GetDiscussionThread(participantId, 12);
        Assert.Equal("The linked discussion.", linked.Note!.Note);
        Assert.Null(linked.SystemPost);

        fixture.Db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "UPDATE LongevitymaxxingParticipants SET ConfirmedAtUtc = NULL WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", participantId);
            cmd.ExecuteNonQuery();
        });
        Assert.Throws<InvalidOperationException>(() => fixture.Service.GetDiscussionThread(participantId, 12));
    }

    [Fact]
    public async Task DiscussionLinks_IncludeWelcomePostsAndRejectPrivateOrMissingPosts()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var state = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access, 1, 2, 2, 2, 2, "Private historical discussion."),
            DateTimeOffset.Parse("2026-06-09T08:00:00Z"));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.GetDiscussionThread(state.Participant.Id, 1));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.GetDiscussionThread(state.Participant.Id, 999));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.GetDiscussionThread("invalid", 1));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.GetDiscussionThread(null, 0, "invalid"));
        var welcome = Assert.Single(state.Public.SystemDiscussionPosts);
        var linked = fixture.Service.GetDiscussionThread(null, 0, welcome.Id);
        Assert.Null(linked.Note);
        Assert.Equal(welcome.Id, linked.SystemPost!.Id);
        Assert.Equal(welcome.DisplayName, linked.SystemPost.DisplayName);
    }
}
