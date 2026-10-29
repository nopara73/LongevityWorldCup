using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Fact]
    public async Task DiscussionRead_BoundedContextIncludesVisibleNewRepliesBeyondTheOldestHundred()
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("read-history-owner@example.com", "History Owner");
        var actor = await fixture.ConfirmParticipantAsync("read-history-actor@example.com", "History Actor");
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        var ownerId = fixture.Service.GetParticipantState(owner, now).Participant.Id;
        var actorId = fixture.Service.GetParticipantState(actor, now).Participant.Id;
        SeedDiscussionFeed(fixture, ownerId);
        var replyIds = Enumerable.Range(0, 105).Select(_ => Guid.NewGuid().ToString("N")).ToArray();
        fixture.Db.Run(sqlite => {
            using var transaction = sqlite.BeginTransaction();
            for (var index = 0; index < replyIds.Length; index++) {
                using var command = sqlite.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO LongevitymaxxingDiscussionReplies
                        (Id, PostParticipantId, PostChallengeDay, AuthorParticipantId, Body, CreatedAtUtc)
                    VALUES (@reply, @owner, 1, @actor, 'Another small step.', @now);
                    INSERT INTO LongevitymaxxingDiscussionNotifications
                        (Id, RecipientParticipantId, ActorParticipantId, PostParticipantId, PostChallengeDay, Kind, SourceReplyId, CreatedAtUtc)
                    VALUES (@notice, @owner, @actor, @owner, 1, 'reply', @reply, @now);
                    """;
                command.Parameters.AddWithValue("@reply", replyIds[index]);
                command.Parameters.AddWithValue("@notice", Guid.NewGuid().ToString("N"));
                command.Parameters.AddWithValue("@owner", ownerId);
                command.Parameters.AddWithValue("@actor", actorId);
                command.Parameters.AddWithValue("@now", now.AddSeconds(index).ToString("o"));
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        });
        var state = fixture.Service.GetParticipantState(owner, now.AddMinutes(5));
        var note = state.Notes.Single(note => note.ParticipantId == ownerId && note.ChallengeDay == 1);
        var unread = state.ReadContext!.Threads.Single(thread => thread.ParticipantId == ownerId && thread.ChallengeDay == 1);
        Assert.Equal(105, unread.UnreadCount);
        Assert.Equal(replyIds[0], unread.FirstUnreadReplyId);
        Assert.InRange(unread.Notifications.Count, 1, 100 + note.Replies.Count);
        Assert.Contains(unread.Notifications, notification => notification.SourceReplyId == replyIds[0]);
        Assert.NotEmpty(note.Replies);
        foreach (var reply in note.Replies)
            Assert.Contains(unread.Notifications, notification => notification.SourceReplyId == reply.Id);
        var oldestVisible = note.Replies.First();
        var earlier = fixture.Service.GetDiscussionReplyPage(new(owner, ownerId, 1, oldestVisible.CreatedAtUtc, oldestVisible.Id));
        var earlierUnread = Assert.Single(earlier.ReadContext!.Threads);
        Assert.InRange(earlierUnread.Notifications.Count, 1, 100 + note.Replies.Count + earlier.Replies.Count);
        foreach (var reply in earlier.Replies)
            Assert.Contains(earlierUnread.Notifications, notification => notification.SourceReplyId == reply.Id);
        var newest = unread.Notifications.Single(notification => notification.SourceReplyId == replyIds[^1]);
        var receipt = fixture.Service.MarkDiscussionRead(new(owner, ownerId, 1, null,
            state.ReadContext.ActivityRevision, [newest.Id]));
        Assert.Equal(104, Assert.Single(receipt.ReadContext.Threads).UnreadCount);
        Assert.Equal(replyIds[0], Assert.Single(receipt.ReadContext.Threads).FirstUnreadReplyId);
    }

    [Fact]
    public async Task DiscussionRead_OnlyObservedOwnedIdsAreAcknowledgedAndNewerRepliesStayUnread()
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("read-owner@example.com", "Read Owner");
        var actor = await fixture.ConfirmParticipantAsync("read-actor@example.com", "Read Actor");
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        var id = fixture.Service.GetParticipantState(owner, now).Participant.Id;
        SeedDiscussionFeed(fixture, id);
        fixture.Service.SubmitDiscussionReply(new(actor, id, 1, "Try a smaller step.", Guid.NewGuid().ToString("N")), now);
        var state = fixture.Service.GetParticipantState(owner, now);
        var unread = state.ReadContext!.Threads.Single(thread => thread.ChallengeDay == 1);
        Assert.Equal(1, unread.UnreadCount);
        var observed = state.ReadContext.ActivityRevision;
        fixture.Service.SubmitDiscussionReply(new(actor, id, 1, "And keep it easy.", Guid.NewGuid().ToString("N")), now.AddSeconds(1));
        var newer = fixture.Service.GetParticipantState(owner, now).ReadContext!.Threads.Single(thread => thread.ChallengeDay == 1);
        var allIds = newer.Notifications.Select(notification => notification.Id).Append(Guid.NewGuid().ToString("N")).ToList();
        var receipt = fixture.Service.MarkDiscussionRead(new(owner, id, 1, null, observed, allIds));
        Assert.Equal(unread.Notifications.Select(notification => notification.Id), receipt.AcknowledgedIds);
        Assert.Equal(1, Assert.Single(receipt.ReadContext.Threads).UnreadCount);
        var replay = fixture.Service.MarkDiscussionRead(new(owner, id, 1, null, observed, allIds));
        Assert.Equal(receipt.ReadContext.ActivityRevision, replay.ReadContext.ActivityRevision);
        Assert.Equal(receipt.AcknowledgedIds, replay.AcknowledgedIds);
        Assert.Empty(fixture.Service.MarkDiscussionRead(new(actor, id, 1, null, long.MaxValue, allIds)).AcknowledgedIds);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Service.MarkDiscussionRead(new("bad-token", id, 1, null, observed, allIds)));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.MarkDiscussionRead(new(owner, id, 1, null, observed, Enumerable.Repeat(allIds[0], 101).ToList())));
    }

    [Fact]
    public async Task DiscussionRead_PersonalUnreadPrecedesAdviceBeyondTheInitialWindowAndReadsPreservePaging()
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("read-feed@example.com", "Read Feed");
        var actor = await fixture.ConfirmParticipantAsync("read-feed-actor@example.com", "Feed Actor");
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        var id = fixture.Service.GetParticipantState(owner, now).Participant.Id;
        SeedDiscussionFeed(fixture, id);
        fixture.Service.SubmitDiscussionReply(new(actor, id, 1, "An old discussion still matters.", Guid.NewGuid().ToString("N")), now.AddDays(-40));
        fixture.Db.Run(sqlite => {
            using var command = sqlite.CreateCommand();
            command.CommandText = """
                INSERT INTO LongevitymaxxingAdviceRequests (Id, ParticipantId, ChallengeDay, Status, TriggerRawScore,
                    CharacterLimit, CreatedAtUtc, Story, PublishedAtUtc, UpdatedAtUtc)
                VALUES (@advice, @id, 2, 'published', 4, 240, @now, 'What helps?', @now, @now);
                """;
            command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@advice", Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("@now", now.ToString("o")); command.ExecuteNonQuery();
        });
        var state = fixture.Service.GetParticipantState(owner, now);
        Assert.Equal(1, state.Notes[0].ChallengeDay);
        Assert.Equal(1, state.DiscussionSnapshot!.Keys[0].ChallengeDay);
        Assert.Null(state.Public.Notes[0].Advice?.ActiveAdviceReplyId);
        Assert.Equal(2, state.Public.Notes[0].ChallengeDay);
        var first = fixture.Service.GetDiscussionFeed(new(owner), now);
        Assert.Equal(1, first.Threads[0].Note!.ChallengeDay);
        var notice = first.ReadContext!.Threads.Single(thread => thread.ChallengeDay == 1);
        fixture.Service.MarkDiscussionRead(new(owner, id, 1, null, first.ReadContext.ActivityRevision,
            notice.Notifications.Select(item => item.Id).ToList()));
        var all = first.Threads.ToList();
        var cursor = first.NextCursor;
        while (cursor is not null) {
            var page = fixture.Service.GetDiscussionFeed(new(owner, cursor), now.AddMinutes(1));
            all.AddRange(page.Threads); cursor = page.NextCursor;
            Assert.True(all.Count <= 128);
        }
        Assert.Equal(125, all.Count(thread => thread.Note is not null));
        Assert.Equal(all.Count, all.Select(thread => thread.Note is { } note ? $"{note.ParticipantId}/{note.ChallengeDay}" : thread.SystemPost!.Id).Distinct().Count());
        fixture.Service.SubmitDiscussionReply(new(actor, id, 3, "A newer message.", Guid.NewGuid().ToString("N")), now);
        Assert.Throws<LongevitymaxxingDiscussionFeedChangedException>(() => fixture.Service.GetDiscussionFeed(new(owner, first.NextCursor), now));
    }

    [Fact]
    public async Task DiscussionRead_ContributorAndDirectReplyRelevanceSurviveMentionRemovalWithoutExtraEmails()
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("read-thread@example.com", "Thread Owner");
        var contributor = await fixture.ConfirmParticipantAsync("read-member@example.com", "Thread Member");
        var actor = await fixture.ConfirmParticipantAsync("read-other@example.com", "Other Member");
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        var id = fixture.Service.GetParticipantState(owner, now).Participant.Id;
        SeedDiscussionFeed(fixture, id);
        var parent = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(contributor, id, 1, "My first reply.", parent), now);
        var reply = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(actor, id, 1, "@Thread Member thanks.", reply, ReplyToId: parent), now.AddSeconds(1));
        var state = fixture.Service.GetParticipantState(contributor, now);
        var unread = state.ReadContext!.Threads.Single(thread => thread.ChallengeDay == 1);
        Assert.Equal(1, unread.UnreadCount);
        Assert.Equal(2, unread.Notifications.Count);
        fixture.Service.EditDiscussionReply(new(actor, reply, "Thanks for that."), now.AddSeconds(2));
        unread = fixture.Service.GetParticipantState(contributor, now).ReadContext!.Threads.Single(thread => thread.ChallengeDay == 1);
        Assert.Equal(1, unread.UnreadCount);
        Assert.Single(unread.Notifications);
        var flags = fixture.Db.Run(sqlite => {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT InAppOnly FROM LongevitymaxxingDiscussionNotifications WHERE SourceReplyId = @id AND Kind = 'reply' AND RecipientParticipantId <> @owner;";
            command.Parameters.AddWithValue("@id", reply); command.Parameters.AddWithValue("@owner", id);
            return Convert.ToInt32(command.ExecuteScalar());
        });
        Assert.Equal(1, flags);
        Assert.All(fixture.Service.GetParticipantState(actor, now).ReadContext!.Threads, thread => Assert.Equal(0, thread.UnreadCount));
    }

    [Fact]
    public async Task DiscussionRead_AdviceParticipationIsScopedToItsBranch()
    {
        using var fixture = TestChallengeFixture.Create();
        fixture.Config.LongevitymaxxingChallenge!.StartDate = "2026-06-20";
        var (owner, lower, higher, post, now) = await AdviceScenarioAsync(fixture);
        fixture.Service.SubmitDiscussionReply(new(lower, post.ParticipantId, 7, "Keep one walk.", Guid.NewGuid().ToString("N"), IsAdvice: true), now);
        fixture.Service.SubmitDiscussionReply(new(higher, post.ParticipantId, 7, "Use one daily anchor.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddSeconds(1));
        var state = fixture.Service.GetParticipantState(lower, now);
        Assert.All(state.ReadContext!.Threads, thread => Assert.Equal(0, thread.UnreadCount));
        Assert.Equal(2, fixture.Service.GetParticipantState(owner, now).ReadContext!.Threads.Single(thread => thread.ChallengeDay == 7).UnreadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionRead_ReceiptsWorkForNotesAndWelcomeThreadsAndHiddenAuthorsDoNotPin(bool welcome)
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("read-channel-owner@example.com", "Channel Owner");
        var actor = await fixture.ConfirmParticipantAsync("read-channel-actor@example.com", "Channel Actor");
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        var state = fixture.Service.GetParticipantState(owner, now);
        var id = state.Participant.Id;
        SeedDiscussionFeed(fixture, id);
        var systemId = welcome ? state.Public.SystemDiscussionPosts.Single(post => post.ParticipantId == id).Id : null;
        var day = welcome ? 0 : 1;
        fixture.Service.SubmitDiscussionReply(new(actor, id, day, "A relevant reply.", Guid.NewGuid().ToString("N"), SystemPostId: systemId), now);
        state = fixture.Service.GetParticipantState(owner, now);
        var unread = state.ReadContext!.Threads.Single(thread => thread.ParticipantId == id && thread.ChallengeDay == day && thread.SystemPostId == systemId);
        Assert.Equal(1, unread.UnreadCount);
        var receipt = fixture.Service.MarkDiscussionRead(new(owner, id, day, systemId, state.ReadContext.ActivityRevision, unread.Notifications.Select(item => item.Id).ToList()));
        Assert.Equal(0, Assert.Single(receipt.ReadContext.Threads).UnreadCount);
        fixture.Service.SubmitDiscussionReply(new(actor, id, day, "Another relevant reply.", Guid.NewGuid().ToString("N"), SystemPostId: systemId), now.AddSeconds(1));
        var actorId = fixture.Service.GetParticipantState(actor, now).Participant.Id;
        fixture.Db.Run(sqlite => {
            using var command = sqlite.CreateCommand(); command.CommandText = "UPDATE LongevitymaxxingParticipants SET ConfirmedAtUtc = NULL WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", actorId); command.ExecuteNonQuery();
        });
        Assert.All(fixture.Service.GetParticipantState(owner, now).ReadContext!.Threads, thread => Assert.Equal(0, thread.UnreadCount));
    }

    [Fact]
    public async Task DiscussionRead_RemovingAnAlreadyEmailedOpeningMentionClearsUnreadWithoutRepeatedEditNotifications()
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("read-mention-owner@example.com", "Mention Owner");
        var recipient = await fixture.ConfirmParticipantAsync("read-mention-recipient@example.com", "Mention Recipient");
        var now = DateTimeOffset.Parse("2026-06-09T08:00:00Z");
        fixture.Service.SubmitCheckIn(new(owner, 1, 2, 2, 2, 2, "A question for @Mention Recipient."), now);
        Assert.Contains(fixture.Service.GetParticipantState(recipient, now).ReadContext!.Threads, thread => thread.HasOpeningMention);
        fixture.Db.Run(sqlite => {
            using var command = sqlite.CreateCommand(); command.CommandText = "UPDATE LongevitymaxxingDiscussionNotifications SET NotifiedAtUtc = 'sent';"; command.ExecuteNonQuery();
        });
        fixture.Service.SubmitCheckIn(new(owner, 1, 2, 2, 2, 2, "Reworded for @Mention Recipient."), now.AddMinutes(1));
        Assert.Equal(1, fixture.Service.GetParticipantState(recipient, now).ReadContext!.Threads.Sum(thread => thread.UnreadCount));
        fixture.Service.SubmitCheckIn(new(owner, 1, 2, 2, 2, 2, "I found the answer."), now.AddMinutes(2));
        Assert.All(fixture.Service.GetParticipantState(recipient, now).ReadContext!.Threads, thread => Assert.Equal(0, thread.UnreadCount));
    }

    [Fact]
    public async Task DiscussionRead_MigrationAndRestartKeepEmailDeliverySeparateFromReadReceipts()
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("read-migrate@example.com", "Migration Owner");
        var actor = await fixture.ConfirmParticipantAsync("read-migrate-actor@example.com", "Migration Actor");
        var now = DateTimeOffset.Parse("2026-06-30T12:00:00Z");
        var id = fixture.Service.GetParticipantState(owner, now).Participant.Id;
        SeedDiscussionFeed(fixture, id);
        fixture.Service.SubmitDiscussionReply(new(actor, id, 1, "Before migration.", Guid.NewGuid().ToString("N")), now);
        fixture.Db.Run(sqlite => {
            using var command = sqlite.CreateCommand();
            command.CommandText = "UPDATE LongevitymaxxingDiscussionNotifications SET NotifiedAtUtc = 'sent'; DELETE FROM LongevitymaxxingDiscussionVersions;";
            command.ExecuteNonQuery();
        });
        var restarted = new LongevitymaxxingChallengeService(fixture.Db, fixture.Config, fixture.Http, fixture.Environment,
            fixture.Email, NullLogger<LongevitymaxxingChallengeService>.Instance, fixture.Athletes, fixture.Statistics);
        Assert.All(restarted.GetParticipantState(owner, now).ReadContext!.Threads, thread => Assert.Equal(0, thread.UnreadCount));
        restarted.SubmitDiscussionReply(new(actor, id, 1, "After migration.", Guid.NewGuid().ToString("N")), now.AddSeconds(1));
        var context = restarted.GetParticipantState(owner, now).ReadContext!;
        var unread = context.Threads.Single(thread => thread.ChallengeDay == 1);
        Assert.Equal(1, unread.UnreadCount);
        restarted.MarkDiscussionRead(new(owner, id, 1, null, context.ActivityRevision, unread.Notifications.Select(item => item.Id).ToList()));
        var again = new LongevitymaxxingChallengeService(fixture.Db, fixture.Config, fixture.Http, fixture.Environment,
            fixture.Email, NullLogger<LongevitymaxxingChallengeService>.Instance, fixture.Athletes, fixture.Statistics);
        Assert.All(again.GetParticipantState(owner, now).ReadContext!.Threads, thread => Assert.Equal(0, thread.UnreadCount));
        Assert.Equal("sent", fixture.Db.Run(sqlite => {
            using var command = sqlite.CreateCommand(); command.CommandText = "SELECT NotifiedAtUtc FROM LongevitymaxxingDiscussionNotifications WHERE ReadRevision = 0 LIMIT 1;";
            return command.ExecuteScalar() as string;
        }));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DiscussionRead_NewOwnerMentionOnEditCreatesOnlyInAppUnreadAndRetainedMentionsStayRead(bool welcome, bool mentionedOriginally)
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("read-edit-owner@example.com", "Edit Owner");
        var actor = await fixture.ConfirmParticipantAsync("read-edit-actor@example.com", "Edit Actor");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var state = fixture.Service.SubmitCheckIn(new(owner, 12, 2, 2, 2, 2, "A routine update."), now);
        var id = state.Participant.Id;
        var system = welcome ? state.Public.SystemDiscussionPosts.Single(post => post.ParticipantId == id).Id : null;
        var day = welcome ? 0 : 12;
        var reply = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(actor, id, day, mentionedOriginally ? "A first reply for @Edit Owner." : "A first reply.", reply, SystemPostId: system), now.AddMinutes(1));
        void Acknowledge()
        {
            var context = fixture.Service.GetParticipantState(owner, now).ReadContext!;
            var unread = context.Threads.Single(thread => thread.ParticipantId == id && thread.ChallengeDay == day && thread.SystemPostId == system);
            fixture.Service.MarkDiscussionRead(new(owner, id, day, system, context.ActivityRevision, unread.Notifications.Select(item => item.Id).ToList()));
        }
        int Unread() => fixture.Service.GetParticipantState(owner, now).ReadContext!.Threads.Sum(thread => thread.UnreadCount);
        Acknowledge();
        fixture.Service.EditDiscussionReply(new(actor, reply, mentionedOriginally ? "An ordinary edit for @Edit Owner." : "An ordinary edit."), now.AddMinutes(2));
        Assert.Equal(0, Unread());
        if (mentionedOriginally) fixture.Service.EditDiscussionReply(new(actor, reply, "The mention was removed."), now.AddMinutes(3));
        fixture.Service.EditDiscussionReply(new(actor, reply, "A new question for @Edit Owner."), now.AddMinutes(4));
        Assert.Equal(1, Unread());
        Acknowledge();
        fixture.Service.EditDiscussionReply(new(actor, reply, "A clearer question for @Edit Owner."), now.AddMinutes(5));
        Assert.Equal(0, Unread());
        var digest = fixture.Service.GetDailyReminderCandidates(now.AddDays(1)).Single(candidate => candidate.ParticipantId == id).DiscussionDigest;
        Assert.Equal(1, digest.ReplyCount);
        Assert.Equal(0, digest.MentionCount);
        Assert.Single(digest.NotificationIds);
    }
}
