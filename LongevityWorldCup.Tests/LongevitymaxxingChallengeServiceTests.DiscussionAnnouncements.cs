using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    private const string AnnouncementText = "Earn your words.\n\nLongevitymaxxing notes and replies now start at 146 characters. Each current Challenge point adds one (up to 300), plus one for every athlete you outrank in the Ultimate League.\n\n[Join the Challenge](https://longevityworldcup.com/longevitymaxxing)";

    [Fact]
    public async Task DiscussionAnnouncements_MigrateWelcomeHistoryAndKeepSeparateReplyableAnnouncements()
    {
        using var fixture = TestChallengeFixture.Create();
        var author = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var replier = await fixture.ConfirmParticipantAsync("reply@example.com", "Reply Rae");
        var now = DateTimeOffset.Parse("2026-06-08T08:00:00Z");
        var state = fixture.Service.GetParticipantState(author, now);
        var participantId = state.Participant.Id;
        var welcome = state.Public.SystemDiscussionPosts.Single(post => post.ParticipantId == participantId);
        var welcomeReplyId = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(replier, participantId, 0, "Welcome!", welcomeReplyId, welcome.Id), now);
        var countsBefore = DiscussionAnnouncementHistoryCounts(fixture);

        // Restore the deployed pre-announcement parent schema while retaining its child rows.
        fixture.Db.Run(db =>
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                PRAGMA foreign_keys = OFF;
                CREATE TABLE LegacyPosts (
                    Id TEXT PRIMARY KEY, Kind TEXT NOT NULL CHECK (Kind IN ('participant-joined')),
                    ParticipantId TEXT NOT NULL, OccurredDate TEXT NOT NULL, OccurredAtUtc TEXT NOT NULL,
                    FOREIGN KEY (ParticipantId) REFERENCES LongevitymaxxingParticipants(Id) ON DELETE CASCADE);
                INSERT INTO LegacyPosts SELECT Id, Kind, ParticipantId, OccurredDate, OccurredAtUtc
                    FROM LongevitymaxxingDiscussionSystemPosts;
                DROP TABLE LongevitymaxxingDiscussionSystemPosts;
                ALTER TABLE LegacyPosts RENAME TO LongevitymaxxingDiscussionSystemPosts;
                CREATE UNIQUE INDEX UX_LongevitymaxxingDiscussionSystemPosts_ParticipantKind
                    ON LongevitymaxxingDiscussionSystemPosts(ParticipantId, Kind);
                PRAGMA foreign_keys = ON;
                """;
            cmd.ExecuteNonQuery();
        });
        var service = new LongevitymaxxingChallengeService(fixture.Db, fixture.Config, fixture.Http,
            fixture.Environment, fixture.Email, NullLogger<LongevitymaxxingChallengeService>.Instance, fixture.Athletes);
        Assert.Equal(countsBefore, DiscussionAnnouncementHistoryCounts(fixture));
        Assert.Equal("Welcome!", Assert.Single(service.GetDiscussionThread(null, 0, welcome.Id).SystemPost!.Replies).Body);

        var announcementId = Guid.NewGuid().ToString("N");
        foreach (var id in new[] { announcementId, Guid.NewGuid().ToString("N") })
            fixture.Db.Run(db =>
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO LongevitymaxxingDiscussionSystemPosts
                        (Id, Kind, ParticipantId, OccurredDate, OccurredAtUtc, SourceEventId, AnnouncementText)
                    VALUES (@id, 'announcement', @owner, '2026-06-08', '2026-06-08T08:01:00Z', @id, @text);
                    """;
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@owner", participantId);
                cmd.Parameters.AddWithValue("@text", AnnouncementText);
                cmd.ExecuteNonQuery();
            });
        var announcement = service.GetDiscussionThread(null, 0, announcementId).SystemPost!;
        Assert.Equal("announcement", announcement.Kind);
        Assert.Equal("Earn your words.", announcement.Announcement!.Title);
        Assert.Equal(announcementId, announcement.Announcement.EventId);
        Assert.Contains("[Join the Challenge](https://longevityworldcup.com/longevitymaxxing)", announcement.Announcement.Content);
        Assert.Equal(2, service.GetPublicState(now).SystemDiscussionPosts.Count(post => post.Kind == "announcement"));

        var replyId = Guid.NewGuid().ToString("N");
        var replied = service.SubmitDiscussionReply(new(replier, participantId, 0, "Earned words!", replyId, announcementId), now.AddMinutes(2));
        Assert.Equal("Earned words!", Assert.Single(replied.Public.SystemDiscussionPosts.Single(post => post.Id == announcementId).Replies).Body);
        Assert.Equal("Welcome!", Assert.Single(replied.Public.SystemDiscussionPosts.Single(post => post.Id == welcome.Id).Replies).Body);
        Assert.Empty(replied.Notes);
        var reminder = service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-09T09:00:00Z"))
            .Single(candidate => candidate.ParticipantId == participantId);
        var email = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(reminder,
            service.BuildAccessUrl(reminder.AccessToken), service.BuildStopUrl(reminder.StopToken));
        Assert.Contains("Your Challenge announcement (2026-06-08): 1 new reply from Reply Rae.", email.TextBody);
        Assert.DoesNotContain("Day 0", email.TextBody);
    }

    private static (long Posts, long Replies, long Notifications) DiscussionAnnouncementHistoryCounts(TestChallengeFixture fixture)
        => fixture.Db.Run(db =>
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                SELECT (SELECT COUNT(*) FROM LongevitymaxxingDiscussionSystemPosts),
                    (SELECT COUNT(*) FROM LongevitymaxxingDiscussionSystemPostReplies),
                    (SELECT COUNT(*) FROM LongevitymaxxingDiscussionSystemPostNotifications);
                """;
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            var result = (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
            reader.Close();
            cmd.CommandText = "PRAGMA foreign_keys;";
            Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
            cmd.CommandText = "PRAGMA foreign_key_check;";
            using var check = cmd.ExecuteReader();
            Assert.False(check.Read());
            return result;
        });
}
