using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LongevitymaxxingChallengeServiceTests
{
    [Fact]
    public async Task DiscussionReplyIsStoredUnderItsPostWithoutEditingEitherCheckInPost()
    {
        using var fixture = TestChallengeFixture.Create();
        var ariAccess = await fixture.ConfirmParticipantAsync("ari@example.com", "Ari Author");
        var beaAccess = await fixture.ConfirmParticipantAsync("bea@example.com", "Bea Builder");
        var postedAt = DateTimeOffset.Parse("2026-06-09T08:05:00Z");

        var ariState = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(ariAccess, 1, 2, 2, 2, 2, "Ari's discussion post."),
            postedAt);
        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(beaAccess, 1, 2, 2, 2, 2, "Bea's separate post."),
            postedAt.AddMinutes(1));
        var ariPost = ariState.Notes.Single(note => note.DisplayName == "Ari Author");

        var result = fixture.Service.SubmitDiscussionReply(
            new LongevitymaxxingDiscussionReplyRequest(
                beaAccess,
                ariPost.ParticipantId,
                ariPost.ChallengeDay,
                "This is a real child reply.",
                Guid.NewGuid().ToString("D")),
            postedAt.AddMinutes(2));

        var thread = result.Notes.Single(note => note.ParticipantId == ariPost.ParticipantId && note.ChallengeDay == 1);
        Assert.Equal("Ari's discussion post.", thread.Note);
        Assert.Equal(1, thread.ReplyCount);
        var reply = Assert.Single(thread.Replies);
        Assert.Equal("Bea Builder", reply.DisplayName);
        Assert.Equal("This is a real child reply.", reply.Body);
        Assert.Equal(postedAt.AddMinutes(2), DateTimeOffset.Parse(thread.LastActivityAtUtc));
        Assert.Equal("Bea's separate post.", result.Notes.Single(note => note.DisplayName == "Bea Builder").Note);
    }

    [Fact]
    public async Task DiscussionReplyRequiresAnExistingPostAndAThreadWithRepliesCannotBeRemoved()
    {
        using var fixture = TestChallengeFixture.Create();
        var authorAccess = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var replierAccess = await fixture.ConfirmParticipantAsync("reply@example.com", "Reply Rae");
        var now = DateTimeOffset.Parse("2026-06-09T08:05:00Z");

        var noPostState = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 1, 2, 2, 2, 2, null),
            now);
        var authorId = noPostState.Participant.Id;
        var missingPost = Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(
            new LongevitymaxxingDiscussionReplyRequest(replierAccess, authorId, 1, "Nowhere to go.", Guid.NewGuid().ToString("D")),
            now.AddMinutes(1)));
        Assert.Equal("That discussion post is no longer available.", missingPost.Message);

        var withPost = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 1, 2, 2, 2, 2, "Keep this thread."),
            now.AddMinutes(2));
        fixture.Service.SubmitDiscussionReply(
            new LongevitymaxxingDiscussionReplyRequest(replierAccess, withPost.Participant.Id, 1, "A saved reply.", Guid.NewGuid().ToString("D")),
            now.AddMinutes(3));

        var removal = Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 1, 2, 2, 2, 2, null),
            now.AddMinutes(4)));
        Assert.Equal("A discussion post with replies cannot be removed.", removal.Message);
        Assert.Equal("Keep this thread.", fixture.Service.GetParticipantState(authorAccess, now.AddMinutes(5)).Notes.Single().Note);
    }

    [Fact]
    public async Task DiscussionThreadsUseReplyCountAndLatestActivityForVoteFreeHotOrder()
    {
        using var fixture = TestChallengeFixture.Create();
        var oldAccess = await fixture.ConfirmParticipantAsync("old@example.com", "Old Olivia");
        var newAccess = await fixture.ConfirmParticipantAsync("new@example.com", "New Nia");
        var replierAccess = await fixture.ConfirmParticipantAsync("reply@example.com", "Reply Rae");

        var oldState = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(oldAccess, 10, 2, 2, 2, 2, "Older active thread."),
            DateTimeOffset.Parse("2026-06-18T08:00:00Z"));
        var oldPost = oldState.Notes.Single(note => note.DisplayName == "Old Olivia");
        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(oldAccess, 10, 2, 1, 2, 2, "Older active thread."),
            DateTimeOffset.Parse("2026-06-19T08:30:00Z"));
        for (var index = 0; index < 7; index++)
        {
            fixture.Service.SubmitDiscussionReply(
                new LongevitymaxxingDiscussionReplyRequest(replierAccess, oldPost.ParticipantId, 10, $"Reply {index + 1}", Guid.NewGuid().ToString("D")),
                DateTimeOffset.Parse("2026-06-19T09:00:00Z").AddSeconds(index));
        }

        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(newAccess, 12, 2, 2, 2, 2, "New thread without replies."),
            DateTimeOffset.Parse("2026-06-20T08:00:00Z"));

        var state = fixture.Service.GetParticipantState(newAccess, DateTimeOffset.Parse("2026-06-20T09:00:00Z"));
        Assert.Equal("Old Olivia", state.Notes[0].DisplayName);
        Assert.Equal(7, state.Notes[0].ReplyCount);
        Assert.Equal(DateTimeOffset.Parse("2026-06-18T08:00:00Z"), DateTimeOffset.Parse(state.Notes[0].UpdatedAtUtc));
        Assert.Equal(DateTimeOffset.Parse("2026-06-19T09:00:06Z"), DateTimeOffset.Parse(state.Notes[0].LastActivityAtUtc));
        Assert.Equal("New Nia", state.Notes[1].DisplayName);

        var popularOlder = LongevitymaxxingChallengeService.CalculateDiscussionHotScore(
            7,
            DateTimeOffset.Parse("2026-06-19T09:00:00Z"),
            DateTimeOffset.Parse("2026-06-20T09:00:00Z"));
        var newWithoutReplies = LongevitymaxxingChallengeService.CalculateDiscussionHotScore(
            0,
            DateTimeOffset.Parse("2026-06-20T09:00:00Z"),
            DateTimeOffset.Parse("2026-06-20T09:00:00Z"));
        Assert.True(popularOlder > newWithoutReplies);
    }

    [Fact]
    public async Task DiscussionRepliesAreBoundedInitiallyAndKeysetPagedWithoutGaps()
    {
        using var fixture = TestChallengeFixture.Create();
        var authorAccess = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var replierAccess = await fixture.ConfirmParticipantAsync("reply@example.com", "Reply Rae");
        var postedAt = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var post = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 12, 2, 2, 2, 2, "A deliberately busy discussion."),
            postedAt);
        var repliedAt = postedAt.AddMinutes(1);

        LongevitymaxxingParticipantState? latest = null;
        for (var index = 1; index <= 24; index++)
        {
            latest = fixture.Service.SubmitDiscussionReply(
                new LongevitymaxxingDiscussionReplyRequest(
                    replierAccess,
                    post.Participant.Id,
                    12,
                    $"Reply {index}",
                    $"00000000-0000-0000-0000-{index:000000000000}"),
                repliedAt);
        }

        var authenticatedThread = Assert.Single(latest!.Notes);
        Assert.Equal(24, authenticatedThread.ReplyCount);
        Assert.Equal(["Reply 22", "Reply 23", "Reply 24"], authenticatedThread.Replies.Select(reply => reply.Body));
        Assert.Equal(repliedAt, DateTimeOffset.Parse(authenticatedThread.LastActivityAtUtc));
        var embeddedPublicThread = Assert.Single(latest.Public.Notes);
        Assert.Equal(24, embeddedPublicThread.ReplyCount);
        Assert.Equal(["Reply 22", "Reply 23", "Reply 24"], embeddedPublicThread.Replies.Select(reply => reply.Body));
        var publicThread = Assert.Single(fixture.Service.GetPublicState(postedAt.AddMinutes(1)).Notes);
        Assert.Equal(["Reply 22", "Reply 23", "Reply 24"], publicThread.Replies.Select(reply => reply.Body));

        var firstPage = fixture.Service.GetDiscussionReplyPage(new LongevitymaxxingDiscussionReplyPageRequest(
            null,
            post.Participant.Id,
            12,
            authenticatedThread.Replies[0].CreatedAtUtc,
            authenticatedThread.Replies[0].Id));
        Assert.Equal(24, firstPage.TotalCount);
        Assert.Equal(20, firstPage.Replies.Count);
        Assert.Equal("Reply 2", firstPage.Replies[0].Body);
        Assert.Equal("Reply 21", firstPage.Replies[^1].Body);
        Assert.Equal(1, firstPage.RemainingEarlierReplyCount);
        Assert.True(firstPage.HasEarlier);

        var secondPage = fixture.Service.GetDiscussionReplyPage(new LongevitymaxxingDiscussionReplyPageRequest(
            null,
            post.Participant.Id,
            12,
            firstPage.NextBeforeCreatedAtUtc,
            firstPage.NextBeforeReplyId));
        Assert.Equal("Reply 1", Assert.Single(secondPage.Replies).Body);
        Assert.Equal(0, secondPage.RemainingEarlierReplyCount);
        Assert.False(secondPage.HasEarlier);
        Assert.Null(secondPage.NextBeforeCreatedAtUtc);
        Assert.Null(secondPage.NextBeforeReplyId);

        var everyReply = secondPage.Replies
            .Concat(firstPage.Replies)
            .Concat(authenticatedThread.Replies)
            .ToList();
        Assert.Equal(24, everyReply.Count);
        Assert.Equal(24, everyReply.Select(reply => reply.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(1, 24).Select(index => $"Reply {index}"), everyReply.Select(reply => reply.Body));
    }

    [Fact]
    public async Task DiscussionStateCapsRankedThreadsBeforeReplyBodiesAreEmbedded()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var participantId = fixture.Service.GetParticipantState(access).Participant.Id;
        var start = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        fixture.Db.Run(sqlite =>
        {
            using var insert = sqlite.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO LongevitymaxxingCheckIns
                (ParticipantId, ChallengeDay, ChallengeDate, Sleep, Exercise, Nutrition, Vices, Note, DiscussionUpdatedAtUtc, CheckedInAtUtc, UpdatedAtUtc)
                VALUES (@participantId, @day, @date, 2, 2, 2, 2, @note, @updated, @updated, @updated);
                """;
            insert.Parameters.AddWithValue("@participantId", participantId);
            var dayParameter = insert.Parameters.Add("@day", Microsoft.Data.Sqlite.SqliteType.Integer);
            var dateParameter = insert.Parameters.Add("@date", Microsoft.Data.Sqlite.SqliteType.Text);
            var noteParameter = insert.Parameters.Add("@note", Microsoft.Data.Sqlite.SqliteType.Text);
            var updatedParameter = insert.Parameters.Add("@updated", Microsoft.Data.Sqlite.SqliteType.Text);
            for (var day = 1; day <= 101; day++)
            {
                var updated = start.AddMinutes(day);
                dayParameter.Value = day;
                dateParameter.Value = DateOnly.FromDateTime(updated.UtcDateTime).ToString("yyyy-MM-dd");
                noteParameter.Value = $"Discussion {day}";
                updatedParameter.Value = updated.ToString("o");
                insert.ExecuteNonQuery();
            }
        });

        var state = fixture.Service.GetParticipantState(access, start.AddDays(10));
        Assert.Equal(100, state.Notes.Count);
        Assert.Equal(100, state.Public.Notes.Count);
        Assert.DoesNotContain(state.Notes, note => note.ChallengeDay == 1);
        Assert.DoesNotContain(state.Public.Notes, note => note.ChallengeDay == 1);
        Assert.All(state.Notes, note => Assert.InRange(note.Replies.Count, 0, 3));
    }

    [Fact]
    public async Task DiscussionReplyReplayIsIdempotentAndReplyIdCannotChangePayload()
    {
        using var fixture = TestChallengeFixture.Create();
        var authorAccess = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var replierAccess = await fixture.ConfirmParticipantAsync("reply@example.com", "Reply Rae");
        var mentionedAccess = await fixture.ConfirmParticipantAsync("mia@example.com", "Mention Mia");
        var postedAt = DateTimeOffset.Parse("2026-06-09T08:00:00Z");
        var post = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 1, 2, 2, 2, 2, "Retry replies here."),
            postedAt);
        var replyId = "10101010-2020-3030-4040-505050505050";
        var request = new LongevitymaxxingDiscussionReplyRequest(
            replierAccess,
            post.Participant.Id,
            1,
            "One durable reply for @Mention Mia.",
            replyId);

        fixture.Service.SubmitDiscussionReply(request, postedAt.AddMinutes(1));
        var replay = fixture.Service.SubmitDiscussionReply(request, postedAt.AddHours(1));
        var thread = Assert.Single(replay.Notes);
        Assert.Equal(1, thread.ReplyCount);
        Assert.Equal(postedAt.AddMinutes(1), DateTimeOffset.Parse(thread.LastActivityAtUtc));
        Assert.Equal(Guid.Parse(replyId).ToString("N"), Assert.Single(thread.Replies).Id);

        var notificationCount = fixture.Db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT COUNT(*)
                FROM LongevitymaxxingDiscussionNotifications
                WHERE SourceReplyId = @replyId;
                """;
            cmd.Parameters.AddWithValue("@replyId", Guid.Parse(replyId).ToString("N"));
            return Convert.ToInt32(cmd.ExecuteScalar());
        });
        Assert.Equal(2, notificationCount);
        Assert.Equal(1, fixture.Service.GetDailyReminderCandidates(postedAt.AddDays(1).AddMinutes(5))
            .Single(candidate => candidate.ParticipantId == post.Participant.Id)
            .DiscussionDigest.ReplyCount);
        var mentionedId = fixture.Service.GetParticipantState(mentionedAccess).Participant.Id;
        Assert.Equal(1, fixture.Service.GetDailyReminderCandidates(postedAt.AddDays(1).AddMinutes(5))
            .Single(candidate => candidate.ParticipantId == mentionedId)
            .DiscussionDigest.MentionCount);

        var conflict = Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(
            request with { Body = "A different body." },
            postedAt.AddHours(2)));
        Assert.Contains("conflicts with an earlier reply", conflict.Message);
        Assert.Equal(1, fixture.Service.GetParticipantState(replierAccess, postedAt.AddHours(2)).Notes.Single().ReplyCount);
    }

    [Fact]
    public async Task TokenlessReplyPagingHonorsThePublicCutoffWhileAuthenticatedPagingCanReadHistory()
    {
        using var fixture = TestChallengeFixture.Create();
        var authorAccess = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var readerAccess = await fixture.ConfirmParticipantAsync("reader@example.com", "Reader Rae");
        var oldPost = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 1, 2, 2, 2, 2, "Historical participant discussion."),
            DateTimeOffset.Parse("2026-06-09T08:00:00Z"));

        var publicError = Assert.Throws<InvalidOperationException>(() => fixture.Service.GetDiscussionReplyPage(
            new LongevitymaxxingDiscussionReplyPageRequest(null, oldPost.Participant.Id, 1, null, null)));
        Assert.Equal("That discussion post is no longer available.", publicError.Message);

        var authenticated = fixture.Service.GetDiscussionReplyPage(
            new LongevitymaxxingDiscussionReplyPageRequest(readerAccess, oldPost.Participant.Id, 1, null, null));
        Assert.Equal(0, authenticated.TotalCount);
        Assert.Empty(authenticated.Replies);

        var cursorError = Assert.Throws<InvalidOperationException>(() => fixture.Service.GetDiscussionReplyPage(
            new LongevitymaxxingDiscussionReplyPageRequest(readerAccess, oldPost.Participant.Id, 1, "2026-06-09T08:00:00Z", null)));
        Assert.Equal("That reply page cursor is invalid.", cursorError.Message);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Service.GetDiscussionReplyPage(
            new LongevitymaxxingDiscussionReplyPageRequest("not-a-valid-token", oldPost.Participant.Id, 1, null, null)));
    }

    [Fact]
    public async Task MentionsAndRepliesAreBundledIntoTheNextDailyEmailAndOnlyItsSnapshotIsMarked()
    {
        using var fixture = TestChallengeFixture.Create();
        var authorAccess = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var replierAccess = await fixture.ConfirmParticipantAsync("reply@example.com", "Reply Rae");
        var mentionerAccess = await fixture.ConfirmParticipantAsync("mentioner@example.com", "Mention Max");
        var postedAt = DateTimeOffset.Parse("2026-06-09T07:00:00Z");
        var authorState = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 1, 2, 2, 2, 2, "Please discuss."),
            postedAt);
        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(mentionerAccess, 1, 2, 2, 2, 2, "A useful point from @Author Ana."),
            postedAt.AddMinutes(5));

        fixture.Service.SubmitDiscussionReply(
            new LongevitymaxxingDiscussionReplyRequest(replierAccess, authorState.Participant.Id, 1, "One reply.", Guid.NewGuid().ToString("D")),
            postedAt.AddMinutes(15));
        fixture.Service.SubmitDiscussionReply(
            new LongevitymaxxingDiscussionReplyRequest(authorAccess, authorState.Participant.Id, 1, "A self reply.", Guid.NewGuid().ToString("D")),
            postedAt.AddMinutes(16));

        var firstReminder = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-10T08:05:00Z"))
            .Single(candidate => candidate.ParticipantId == authorState.Participant.Id);
        Assert.Equal(1, firstReminder.DiscussionDigest.MentionCount);
        Assert.Equal(1, firstReminder.DiscussionDigest.ReplyCount);
        Assert.Equal(2, firstReminder.DiscussionDigest.TotalCount);
        var mentionItem = Assert.Single(firstReminder.DiscussionDigest.Items, item =>
            item.Kind == LongevitymaxxingDiscussionActivityKind.Mention);
        Assert.Equal(1, mentionItem.ChallengeDay);
        Assert.Equal("2026-06-08", mentionItem.Date);
        Assert.Equal(["Mention Max"], mentionItem.ActorDisplayNames);
        var replyItem = Assert.Single(firstReminder.DiscussionDigest.Items, item =>
            item.Kind == LongevitymaxxingDiscussionActivityKind.Reply);
        Assert.Equal(["Reply Rae"], replyItem.ActorDisplayNames);

        var email = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(
            firstReminder,
            fixture.Service.BuildAccessUrl(firstReminder.AccessToken),
            fixture.Service.BuildStopUrl(firstReminder.StopToken));
        Assert.Contains("Discussion activity: 1 new mention and 1 new reply", email.TextBody);
        Assert.Contains("- Mention Max mentioned you in a Day 1 post (2026-06-08).", email.TextBody);
        Assert.Contains("- Your Day 1 post (2026-06-08): 1 new reply from Reply Rae.", email.TextBody);
        Assert.Contains("Open the check-in link above to read and reply.", email.TextBody);

        fixture.Service.SubmitDiscussionReply(
            new LongevitymaxxingDiscussionReplyRequest(replierAccess, authorState.Participant.Id, 1, "Arrived after the email snapshot.", Guid.NewGuid().ToString("D")),
            DateTimeOffset.Parse("2026-06-10T08:05:30Z"));
        fixture.Service.MarkDailyReminderSent(firstReminder, DateTimeOffset.Parse("2026-06-10T08:06:00Z"));
        var nextReminder = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-11T08:05:00Z"))
            .Single(candidate => candidate.ParticipantId == authorState.Participant.Id);
        Assert.Equal(0, nextReminder.DiscussionDigest.MentionCount);
        Assert.Equal(1, nextReminder.DiscussionDigest.ReplyCount);
        Assert.Single(nextReminder.DiscussionDigest.NotificationIds);
    }

    [Fact]
    public async Task FailedDailyEmailKeepsMentionsAndRepliesPendingForTheSuccessfulRetry()
    {
        using var fixture = TestChallengeFixture.Create();
        var authorAccess = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var replierAccess = await fixture.ConfirmParticipantAsync("reply@example.com", "Reply Rae");
        var mentionerAccess = await fixture.ConfirmParticipantAsync("mentioner@example.com", "Mention Max");
        var post = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 1, 2, 2, 2, 2, "Retry this digest."),
            DateTimeOffset.Parse("2026-06-09T07:00:00Z"));
        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(mentionerAccess, 1, 2, 2, 2, 2, "Retry with @Author Ana."),
            DateTimeOffset.Parse("2026-06-09T07:10:00Z"));
        fixture.Service.SubmitDiscussionReply(
            new LongevitymaxxingDiscussionReplyRequest(replierAccess, post.Participant.Id, 1, "Do not lose this.", Guid.NewGuid().ToString("D")),
            DateTimeOffset.Parse("2026-06-09T07:15:00Z"));

        using var events = CreateEventDataService(fixture);
        var job = new LongevitymaxxingReminderJob(
            fixture.Service,
            events,
            fixture.Email,
            NullLogger<LongevitymaxxingReminderJob>.Instance);

        fixture.Email.ThrowOnDailyReminder = true;
        await job.ExecuteAtAsync(DateTimeOffset.Parse("2026-06-10T08:05:00Z"));
        var failedAttempt = Assert.Single(fixture.Email.DailyReminders, reminder => reminder.ParticipantId == post.Participant.Id);
        Assert.Equal(1, failedAttempt.DiscussionDigest.MentionCount);
        Assert.Equal(1, failedAttempt.DiscussionDigest.ReplyCount);

        fixture.Email.ThrowOnDailyReminder = false;
        await job.ExecuteAtAsync(DateTimeOffset.Parse("2026-06-10T08:06:00Z"));
        var authorAttempts = fixture.Email.DailyReminders.Where(reminder => reminder.ParticipantId == post.Participant.Id).ToList();
        Assert.Equal(2, authorAttempts.Count);
        Assert.Equal(1, authorAttempts[1].DiscussionDigest.MentionCount);
        Assert.Equal(1, authorAttempts[1].DiscussionDigest.ReplyCount);

        await job.ExecuteAtAsync(DateTimeOffset.Parse("2026-06-10T08:07:00Z"));
        Assert.Equal(2, fixture.Email.DailyReminders.Count(reminder => reminder.ParticipantId == post.Participant.Id));
    }

    [Fact]
    public void GardenVitality_StartsAsSeedlingAndNoDamageScalesWithEstablishedGrowth()
    {
        static double ApplyRepeatedly(double vitality, int answer, int count)
            => Enumerable.Range(0, count).Aggregate(
                vitality,
                (current, _) => LongevitymaxxingGardenHabitState.ApplyAnswer(current, answer));

        var firstYes = LongevitymaxxingGardenHabitState.ApplyAnswer(0d, 2);
        var ninetyNinthYes = ApplyRepeatedly(0d, 2, 99);
        var hundredthYes = LongevitymaxxingGardenHabitState.ApplyAnswer(ninetyNinthYes, 2);
        var mature = ApplyRepeatedly(0d, 2, 100);
        var firstNoAfterMaturity = LongevitymaxxingGardenHabitState.ApplyAnswer(mature, 0);
        var secondNoAfterMaturity = LongevitymaxxingGardenHabitState.ApplyAnswer(firstNoAfterMaturity, 0);
        var recovered = ApplyRepeatedly(firstNoAfterMaturity, 2, 20);

        Assert.Equal(0.025d, firstYes, 10);
        Assert.Equal(0d, LongevitymaxxingGardenHabitState.ApplyAnswer(0d, 0), 10);
        Assert.True(mature > 0.9d);
        Assert.True(firstYes > hundredthYes - ninetyNinthYes);
        Assert.True(mature - firstNoAfterMaturity > firstYes);
        Assert.True(firstNoAfterMaturity - secondNoAfterMaturity < mature - firstNoAfterMaturity);
        Assert.True(recovered > firstNoAfterMaturity);
        Assert.Equal(mature, LongevitymaxxingGardenHabitState.ApplyAnswer(mature, 1), 10);
    }

    [Fact]
    public async Task ParticipantGardenAggregatesLifetimeYesAndNoEvidenceAcrossCheckIns()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("garden@example.com", "Garden Gia");

        var first = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access, 1, 2, 1, 0, 2, null),
            DateTimeOffset.Parse("2026-06-09T08:05:00Z"));

        Assert.Equal(
            new LongevitymaxxingGardenState(
                1,
                new LongevitymaxxingGardenHabitState(1, 0, 0.025d),
                new LongevitymaxxingGardenHabitState(0, 0, 0d),
                new LongevitymaxxingGardenHabitState(0, 1, 0d),
                new LongevitymaxxingGardenHabitState(1, 0, 0.025d)),
            first.Garden);
        Assert.Equal(0.025d, first.Garden.Sleep.Vitality, 10);
        Assert.Equal(0d, first.Garden.Exercise.Vitality, 10);
        Assert.Equal(0d, first.Garden.Nutrition.Vitality, 10);

        var second = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access, 2, 1, 2, 2, 0, null),
            DateTimeOffset.Parse("2026-06-10T08:05:00Z"));

        Assert.Equal(
            new LongevitymaxxingGardenState(
                2,
                new LongevitymaxxingGardenHabitState(1, 0, 0.025d),
                new LongevitymaxxingGardenHabitState(1, 0, 0.025d),
                new LongevitymaxxingGardenHabitState(1, 1, 0.025d),
                new LongevitymaxxingGardenHabitState(1, 1, 0.01625d)),
            second.Garden);
    }

    [Fact]
    public async Task CheckInMentionEditsQueueOnlyNewNamesAndAvoidPartialOrSelfMatches()
    {
        using var fixture = TestChallengeFixture.Create();
        var senderAccess = await fixture.ConfirmParticipantAsync("sender@example.com", "Sender Sam");
        var bobAccess = await fixture.ConfirmParticipantAsync("bob@example.com", "Bob");
        var bobSmithAccess = await fixture.ConfirmParticipantAsync("bob-smith@example.com", "Bob Smith");
        var now = DateTimeOffset.Parse("2026-06-09T08:05:00Z");

        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(
                senderAccess,
                1,
                2,
                2,
                2,
                2,
                "Email test@Bob.com; thanks @Bob Smith and @Sender Sam."),
            now);

        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(
                senderAccess,
                1,
                2,
                2,
                2,
                2,
                "Email test@Bob.com; thanks @Bob Smith and @Sender Sam."),
            now.AddMinutes(1));

        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(
                senderAccess,
                1,
                2,
                2,
                2,
                2,
                "Thanks again @Bob Smith, and welcome @Bob."),
            now.AddMinutes(2));
        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(
                senderAccess,
                1,
                2,
                2,
                2,
                2,
                "Welcome @Bob; the earlier mention was removed before delivery."),
            now.AddMinutes(3));

        var candidates = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-10T08:05:00Z"));
        var bobId = fixture.Service.GetParticipantState(bobAccess).Participant.Id;
        var bobSmithId = fixture.Service.GetParticipantState(bobSmithAccess).Participant.Id;
        var bobDigest = candidates.Single(candidate => candidate.ParticipantId == bobId).DiscussionDigest;
        var bobSmithDigest = candidates.Single(candidate => candidate.ParticipantId == bobSmithId).DiscussionDigest;
        Assert.Equal(1, bobDigest.MentionCount);
        Assert.Equal(0, bobSmithDigest.MentionCount);
        Assert.Equal(["Sender Sam"], Assert.Single(bobDigest.Items).ActorDisplayNames);
        Assert.Empty(bobSmithDigest.Items);
    }

    [Fact]
    public async Task ConcurrentIdenticalMentionEditsLeaveOnePendingOpeningPostNotification()
    {
        using var fixture = TestChallengeFixture.Create();
        var senderAccess = await fixture.ConfirmParticipantAsync("sender@example.com", "Sender Sam");
        var bobAccess = await fixture.ConfirmParticipantAsync("bob@example.com", "Bob Builder");
        var bobId = fixture.Service.GetParticipantState(bobAccess).Participant.Id;
        var now = DateTimeOffset.Parse("2026-06-09T08:00:00Z");
        var request = new LongevitymaxxingCheckInRequest(
            senderAccess,
            1,
            2,
            2,
            2,
            2,
            "A concurrent hello to @Bob Builder.");

        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
            fixture.Service.SubmitCheckIn(request, now.AddMilliseconds(index)))));

        var pending = fixture.Db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT COUNT(*)
                FROM LongevitymaxxingDiscussionNotifications
                WHERE RecipientParticipantId = @recipient
                  AND Kind = 'mention'
                  AND SourceReplyId IS NULL
                  AND NotifiedAtUtc IS NULL;
                """;
            cmd.Parameters.AddWithValue("@recipient", bobId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        });
        Assert.Equal(1, pending);
        Assert.Equal(1, fixture.Service.GetDailyReminderCandidates(now.AddDays(1).AddMinutes(5))
            .Single(candidate => candidate.ParticipantId == bobId)
            .DiscussionDigest.MentionCount);

        var senderId = fixture.Service.GetParticipantState(senderAccess).Participant.Id;
        var duplicate = Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => fixture.Db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO LongevitymaxxingDiscussionNotifications
                (Id, RecipientParticipantId, ActorParticipantId, PostParticipantId, PostChallengeDay, Kind, SourceReplyId, CreatedAtUtc, NotifiedAtUtc)
                VALUES (@id, @recipient, @sender, @sender, 1, 'mention', NULL, @created, NULL);
                """;
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@recipient", bobId);
            cmd.Parameters.AddWithValue("@sender", senderId);
            cmd.Parameters.AddWithValue("@created", now.ToString("o"));
            cmd.ExecuteNonQuery();
        }));
        Assert.Equal(19, duplicate.SqliteErrorCode);
    }

    [Fact]
    public async Task RemovingOpeningPostMentionDoesNotDeleteReplyMentionFromTheSameAuthor()
    {
        using var fixture = TestChallengeFixture.Create();
        var authorAccess = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var mentionedAccess = await fixture.ConfirmParticipantAsync("mia@example.com", "Mention Mia");
        var miaId = fixture.Service.GetParticipantState(mentionedAccess).Participant.Id;
        var now = DateTimeOffset.Parse("2026-06-09T08:00:00Z");
        var post = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 1, 2, 2, 2, 2, "Opening mention for @Mention Mia."),
            now);
        fixture.Service.SubmitDiscussionReply(
            new LongevitymaxxingDiscussionReplyRequest(
                authorAccess,
                post.Participant.Id,
                1,
                "Reply mention for @Mention Mia.",
                Guid.NewGuid().ToString("D")),
            now.AddMinutes(1));

        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 1, 2, 2, 2, 2, "Opening mention removed."),
            now.AddMinutes(2));

        var pendingSources = fixture.Db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT SourceReplyId
                FROM LongevitymaxxingDiscussionNotifications
                WHERE RecipientParticipantId = @recipient
                  AND Kind = 'mention'
                  AND NotifiedAtUtc IS NULL;
                """;
            cmd.Parameters.AddWithValue("@recipient", miaId);
            var values = new List<string?>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                values.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
            return values;
        });
        Assert.NotNull(Assert.Single(pendingSources));
        var digest = fixture.Service.GetDailyReminderCandidates(now.AddDays(1).AddMinutes(5))
            .Single(candidate => candidate.ParticipantId == miaId)
            .DiscussionDigest;
        Assert.Equal(1, digest.MentionCount);
        Assert.Equal(["Author Ana"], Assert.Single(digest.Items).ActorDisplayNames);
    }

    [Fact]
    public async Task ChallengeEmailOptOutSuppressesTheOnlyDiscussionDeliveryPath()
    {
        using var fixture = TestChallengeFixture.Create();
        var senderAccess = await fixture.ConfirmParticipantAsync("sender@example.com", "Sender Sam");
        var quietAccess = await fixture.ConfirmParticipantAsync("quiet@example.com", "Quiet Quinn");
        var quietId = fixture.Service.GetParticipantState(quietAccess).Participant.Id;
        fixture.Service.StopChallengeEmails(quietAccess, DateTimeOffset.Parse("2026-06-08T12:00:00Z"));

        var unsubscribed = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-09T08:04:00Z"))
            .Leaderboard
            .Single(row => row.DisplayName == "Quiet Quinn");
        Assert.True(unsubscribed.ChallengeEmailsStopped);

        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(senderAccess, 1, 2, 2, 2, 2, "Still visible to @Quiet Quinn."),
            DateTimeOffset.Parse("2026-06-09T08:05:00Z"));

        Assert.DoesNotContain(
            fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-10T08:05:00Z")),
            candidate => candidate.ParticipantId == quietId);
        var pending = fixture.Db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT COUNT(*)
                FROM LongevitymaxxingDiscussionNotifications
                WHERE RecipientParticipantId = @participantId
                  AND Kind = 'mention'
                  AND NotifiedAtUtc IS NULL;
                """;
            cmd.Parameters.AddWithValue("@participantId", quietId);
            return Convert.ToInt32(cmd.ExecuteScalar());
        });
        Assert.Equal(1, pending);
    }

    [Fact]
    public async Task ReplyMentionsShareTheDigestWithoutDuplicatingThePostAuthor()
    {
        using var fixture = TestChallengeFixture.Create();
        var authorAccess = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var replierAccess = await fixture.ConfirmParticipantAsync("reply@example.com", "Reply Rae");
        var mentionedAccess = await fixture.ConfirmParticipantAsync("mentioned@example.com", "Mention Mia");
        var post = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(authorAccess, 1, 2, 2, 2, 2, "A post to answer."),
            DateTimeOffset.Parse("2026-06-09T08:00:00Z"));

        fixture.Service.SubmitDiscussionReply(
            new LongevitymaxxingDiscussionReplyRequest(
                replierAccess,
                post.Participant.Id,
                1,
                "Thanks @Author Ana; this may help @Mention Mia too.",
                Guid.NewGuid().ToString("D")),
            DateTimeOffset.Parse("2026-06-09T08:05:00Z"));

        var candidates = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-10T08:05:00Z"));
        var authorDigest = candidates.Single(candidate => candidate.ParticipantId == post.Participant.Id).DiscussionDigest;
        var mentionedId = fixture.Service.GetParticipantState(mentionedAccess).Participant.Id;
        var mentionedDigest = candidates.Single(candidate => candidate.ParticipantId == mentionedId).DiscussionDigest;
        Assert.Equal(0, authorDigest.MentionCount);
        Assert.Equal(1, authorDigest.ReplyCount);
        Assert.Single(authorDigest.NotificationIds);
        Assert.Equal(1, mentionedDigest.MentionCount);
        Assert.Equal(0, mentionedDigest.ReplyCount);
        Assert.Equal(["Reply Rae"], Assert.Single(mentionedDigest.Items).ActorDisplayNames);
    }

    [Fact]
    public async Task DiscussionPostsAndRepliesLimitMentionFanout()
    {
        using var fixture = TestChallengeFixture.Create();
        var senderAccess = await fixture.ConfirmParticipantAsync("sender@example.com", "Sender Sam");
        var targetAccess = await fixture.ConfirmParticipantAsync("target@example.com", "Target Tina");
        var names = Enumerable.Range(1, 6).Select(index => $"Person {index}").ToArray();
        foreach (var name in names)
            fixture.InsertConfirmedParticipant($"{name.Replace(' ', '-').ToLowerInvariant()}@example.com", name);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(
                senderAccess,
                1,
                2,
                2,
                2,
                2,
                string.Join(" ", names.Select(name => $"@{name}"))),
            DateTimeOffset.Parse("2026-06-09T08:07:00Z")));

        Assert.Equal("Each discussion post can mention up to 5 participants.", error.Message);
        var target = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(targetAccess, 1, 2, 2, 2, 2, "Reply here."),
            DateTimeOffset.Parse("2026-06-09T08:08:00Z"));
        var replyError = Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(
            new LongevitymaxxingDiscussionReplyRequest(
                senderAccess,
                target.Participant.Id,
                1,
                string.Join(" ", names.Select(name => $"@{name}")),
                Guid.NewGuid().ToString("D")),
            DateTimeOffset.Parse("2026-06-09T08:09:00Z")));
        Assert.Equal("Each reply can mention up to 5 participants.", replyError.Message);
    }

    [Fact]
    public async Task SignupRequiresConfirmationBeforePublicRosterAndSubscribesNewsletterOnConfirm()
    {
        using var fixture = TestChallengeFixture.Create();
        var now = DateTimeOffset.Parse("2026-06-07T12:00:00Z");

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "athlete@example.com",
            "Momentum Alice",
            "UTC",
            "/athlete/momentum-alice"), now);

        Assert.Empty(fixture.Service.GetPublicState(now).Leaderboard);
        Assert.Single(fixture.Email.Confirmations);
        Assert.StartsWith("https://example.test/longevitymaxxing?confirm=", fixture.Email.Confirmations[0].Url);

        var confirmToken = ReadQueryToken(fixture.Email.Confirmations[0].Url, "confirm");
        var access = await fixture.Service.ConfirmAsync(confirmToken, now.AddMinutes(1));

        var publicState = fixture.Service.GetPublicState(now.AddMinutes(2));
        var row = Assert.Single(publicState.Leaderboard);
        Assert.Equal("Momentum Alice", row.DisplayName);
        Assert.Equal("/athlete/momentum-alice", row.AthleteUrl);
        Assert.False(string.IsNullOrWhiteSpace(access.AccessToken));

        var subscriptions = File.ReadAllText(Path.Combine(fixture.ContentRoot, "AppData", "subscriptions.txt"));
        Assert.Contains("athlete@example.com", subscriptions);
    }

    [Fact]
    public async Task SignupAndResendNormalizeCopiedEmailLinks()
    {
        using var fixture = TestChallengeFixture.Create();
        var now = DateTimeOffset.Parse("2026-06-07T12:00:00Z");

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "Linked Lee <linked@example.com>",
            "Linked Lee",
            "UTC",
            null), now);

        var confirmation = Assert.Single(fixture.Email.Confirmations);
        Assert.Equal("linked@example.com", confirmation.Email);

        await fixture.Service.ResendAccessLinkAsync(
            " Linked Lee <mailto:linked@example.com?subject=Challenge> ",
            now.AddMinutes(1));

        Assert.Equal(2, fixture.Email.Confirmations.Count);
        Assert.Equal("linked@example.com", fixture.Email.Confirmations.Last().Email);
    }

    [Fact]
    public async Task SignupNormalizesWindowsTimeZoneIdsToIana()
    {
        using var fixture = TestChallengeFixture.Create();
        var now = DateTimeOffset.Parse("2026-06-07T12:00:00Z");

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "india@example.com",
            "India Iris",
            "India Standard Time",
            null), now);
        var access = await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            now.AddMinutes(1));

        Assert.NotEqual("India Standard Time", access.State.Participant.TimeZoneId);
        Assert.True(TimeZoneInfo.TryConvertIanaIdToWindowsId(access.State.Participant.TimeZoneId, out var windowsId));
        Assert.Equal("India Standard Time", windowsId);
    }

    [Fact]
    public async Task ServerStatisticsCaptureSignupAndCheckInOutcomes()
    {
        using var fixture = TestChallengeFixture.Create();
        var signupAt = DateTimeOffset.Parse("2026-06-07T12:00:00Z");

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "stats@example.com",
            "Stats Sam",
            "UTC",
            null), signupAt);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "bad-email",
            "Bad Email",
            "UTC",
            null), signupAt));

        var access = await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Single().Url, "confirm"),
            signupAt.AddMinutes(1));

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            1,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-09T08:00:00Z"));

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            2,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-10T08:00:00Z"));

        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            99,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-10T08:00:00Z")));

        var dashboard = await fixture.Statistics.GetDashboardAsync(new SiteStatisticsDashboardQuery
        {
            Range = "30d",
            Flow = "challenge",
            Limit = 100
        });
        var eventNames = dashboard.Events.Select(ev => ev.EventName).ToList();

        Assert.Contains("challenge_signup_succeeded", eventNames);
        Assert.Contains("challenge_signup_failed", eventNames);
        Assert.Contains("challenge_practice_checkin_submitted", eventNames);
        Assert.Contains("challenge_scored_checkin_submitted", eventNames);
        Assert.Contains("challenge_checkin_failed", eventNames);

        var practice = Assert.Single(dashboard.Events, ev => ev.EventName == "challenge_practice_checkin_submitted");
        Assert.Equal("practice", practice.Metadata["checkInKind"]);
        Assert.Equal("1", practice.Metadata["challengeDay"]);

        var scored = Assert.Single(dashboard.Events, ev => ev.EventName == "challenge_scored_checkin_submitted");
        Assert.Equal("scored", scored.Metadata["checkInKind"]);
        Assert.Equal("2", scored.Metadata["challengeDay"]);
    }

    [Fact]
    public async Task ServerStatisticsUseParticipantSessionFallbackWithoutHttpContext()
    {
        using var fixture = TestChallengeFixture.Create(signupClosesAtUtc: "2026-06-09T22:00:00Z");
        var signupAt = DateTimeOffset.Parse("2026-06-08T08:00:00Z");

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "one@example.test",
            "One Participant",
            "UTC",
            null), signupAt);
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "two@example.test",
            "Two Participant",
            "UTC",
            null), signupAt.AddMinutes(1));

        var dashboard = await fixture.Statistics.GetDashboardAsync(new SiteStatisticsDashboardQuery
        {
            Range = "30d",
            Flow = "challenge",
            Limit = 100
        });
        var signups = dashboard.Events
            .Where(ev => ev.EventName == "challenge_signup_succeeded")
            .ToList();

        Assert.Equal(2, signups.Count);
        Assert.Equal(2, signups.Select(ev => ev.SessionHash).Distinct().Count());
        Assert.Equal(2, signups.Select(ev => ev.ActorHash).Distinct().Count());
    }

    [Fact]
    public async Task SignupStaysOpenDuringActiveChallengeWithoutBackfillingBeforeSignup()
    {
        using var fixture = TestChallengeFixture.Create(signupClosesAtUtc: "2026-06-09T22:00:00Z");
        var signup = DateTimeOffset.Parse("2026-06-09T12:00:00Z");

        var publicState = fixture.Service.GetPublicState(signup);
        Assert.Equal("active", publicState.Phase);
        Assert.True(publicState.SignupOpen);

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "signup@example.com",
            "Signup Sue",
            "UTC",
            null), signup);
        var access = await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), signup.AddMinutes(1));

        Assert.Empty(access.State.EligibleDays);
        var nextDay = fixture.Service.GetParticipantState(access.AccessToken, DateTimeOffset.Parse("2026-06-10T08:05:00Z"));
        var practice = Assert.Single(nextDay.EligibleDays);
        Assert.Equal(2, practice.ChallengeDay);
        Assert.False(practice.CountsForScore);
        Assert.DoesNotContain(access.State.EligibleDays, day => day.ChallengeDay == 2);
        Assert.True(fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-09T22:01:00Z")).SignupOpen);
    }

    [Fact]
    public async Task SignupStaysOpenBeforeChallengeStartAfterConfiguredSignupClose()
    {
        using var fixture = TestChallengeFixture.Create(signupClosesAtUtc: "2026-06-01T00:00:00Z");
        var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");

        var publicState = fixture.Service.GetPublicState(now);
        Assert.Equal("signup", publicState.Phase);
        Assert.True(publicState.SignupOpen);
        Assert.Empty(fixture.Service.GetChallengeStartCandidates(now));

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "prestart@example.com",
            "Prestart Pat",
            "UTC",
            null), now);
        var access = await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), now.AddMinutes(1));

        Assert.Empty(access.State.EligibleDays);
        Assert.True(access.State.Public.SignupOpen);
    }


    [Fact]
    public async Task SignupReservesAthleteNamesForSelectedAthleteProfiles()
    {
        using var fixture = TestChallengeFixture.Create();
        var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");
        await fixture.ConfirmParticipantAsync("desktop@example.com", "Desktop Dana");
        fixture.Athletes.Snapshot.Add(new JsonObject
        {
            ["AthleteSlug"] = "athlete_alex",
            ["Name"] = "Athlete Alex",
            ["DisplayName"] = "Athlete Display"
        });

        var duplicateParticipant = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("duplicate@example.com", "desktop   dana", "UTC", null),
            now));
        Assert.Equal("That username is already taken.", duplicateParticipant.Message);

        var duplicateAthleteName = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("athlete-name@example.com", "athlete alex", "UTC", null),
            now));
        Assert.Equal("That username is already used by a Longevity athlete.", duplicateAthleteName.Message);

        var duplicateAthleteDisplay = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("athlete-display@example.com", "Athlete Display", "UTC", null),
            now));
        Assert.Equal("That username is already used by a Longevity athlete.", duplicateAthleteDisplay.Message);

        await fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("real-athlete@example.com", "Ignored Name", "UTC", "/athlete/athlete-alex"),
            now);
        var athleteAccess = await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            now.AddMinutes(1));
        Assert.Equal("Athlete Display", athleteAccess.State.Participant.DisplayName);
        Assert.Equal("athlete-alex", athleteAccess.State.Participant.AthleteSlug);

        var duplicateAthleteProfile = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("same-athlete@example.com", "Athlete Display", "UTC", "/athlete/athlete-alex"),
            now));
        Assert.Equal("That athlete profile is already in the challenge.", duplicateAthleteProfile.Message);
    }

    [Fact]
    public async Task EditRejectsDisplayNameChangesAfterSignup()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync("taken@example.com", "Taken Tina");
        var access = await fixture.ConfirmParticipantAsync("editor@example.com", "Editor Eli");

        var rename = Assert.Throws<InvalidOperationException>(() => fixture.Service.EditParticipant(
            new LongevitymaxxingParticipantEditRequest(access, "UTC", "taken tina")));
        Assert.Equal("Identity cannot be changed after signup.", rename.Message);

        var profile = fixture.Service.EditParticipant(
            new LongevitymaxxingParticipantEditRequest(access, "Europe/Budapest"));
        Assert.Equal("Editor Eli", profile.Participant.DisplayName);
        Assert.Equal("Europe/Budapest", profile.Participant.TimeZoneId);
    }

    [Fact]
    public async Task EditRejectsAthleteProfileChangesAfterSignup()
    {
        using var fixture = TestChallengeFixture.Create();
        fixture.Athletes.Snapshot.Add(new JsonObject
        {
            ["AthleteSlug"] = "athlete_bea",
            ["Name"] = "Athlete Bea",
            ["DisplayName"] = "Bea Baseline"
        });
        var access = await fixture.ConfirmParticipantAsync("bea@example.com", "Bea User");

        var link = Assert.Throws<InvalidOperationException>(() => fixture.Service.EditParticipant(
            new LongevitymaxxingParticipantEditRequest(access, "UTC", AthleteLink: "/athlete/athlete-bea")));
        Assert.Equal("Identity cannot be changed after signup.", link.Message);

        var state = fixture.Service.GetParticipantState(access);
        Assert.Equal("Bea User", state.Participant.DisplayName);
        Assert.Null(state.Participant.AthleteSlug);
    }

    [Fact]
    public async Task EmptyPostDayFourteenLeaderboardStillRendersVisibleDayCells()
    {
        using var fixture = TestChallengeFixture.Create();

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-25T09:00:00Z"));

        Assert.Empty(state.Leaderboard);
        Assert.Contains(state.Days, day => day.ChallengeDay == 18);
        Assert.Equal(18, state.Days.Count);
    }

    [Fact]
    public async Task ParticipantWithoutAthleteProfileCanUploadChallengeProfilePicture()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("pic@example.com", "Picture Pat");
        using var stream = CreatePngStream();
        var file = CreatePngFormFile(stream);

        var state = await fixture.Service.UploadParticipantProfilePictureAsync(access, file);

        Assert.NotNull(state.Participant.ProfileImageUrl);
        Assert.Contains("/generated/longevitymaxxing/profile-pictures/", state.Participant.ProfileImageUrl);
        Assert.Contains("?v=", state.Participant.ProfileImageUrl);
        var row = Assert.Single(state.Public.Leaderboard);
        Assert.Equal(state.Participant.ProfileImageUrl, row.ProfileImageUrl);
        Assert.Null(row.AthleteUrl);

        var storedPath = Path.Combine(fixture.ContentRoot, "generated", "longevitymaxxing", "profile-pictures", $"{state.Participant.Id}.webp");
        Assert.True(File.Exists(storedPath));
    }

    [Fact]
    public async Task UnsupportedChallengeProfilePictureFormatGivesSpecificGuidance()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("format@example.com", "Format Fran");
        using var stream = new MemoryStream("not an image"u8.ToArray());
        var file = new FormFile(stream, 0, stream.Length, "profilePicture", "profile.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UploadParticipantProfilePictureAsync(access, file));

        Assert.Contains("format is not supported", ex.Message);
        Assert.Contains("JPG, PNG, or WebP", ex.Message);
    }

    [Fact]
    public async Task CheckInCanAttachWebReadyPhotosToParticipantNotes()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync(
            "notes@example.com",
            "Notes Nora",
            nowUtc: DateTimeOffset.Parse("2026-06-19T12:00:00Z"));
        using var stream = CreatePngStream(width: 2400, height: 1200);
        var file = CreatePngFormFile(stream, formName: "notePhotos", fileName: "kitchen.png");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");

        var state = await fixture.Service.SubmitCheckInAsync(
            new LongevitymaxxingCheckInRequest(access, 12, 2, 2, 2, 2, "Good breakfast prep.\nps.: kept line break"),
            [file],
            now);

        var note = Assert.Single(state.Notes);
        Assert.Equal("Good breakfast prep.\nps.: kept line break", note.Note);
        var image = Assert.Single(note.Images);
        Assert.Contains("/generated/longevitymaxxing/check-in-photos/", image.Url);
        Assert.Contains("?v=", image.Url);
        Assert.Equal(1600, image.Width);
        Assert.Equal(800, image.Height);

        var publicNote = Assert.Single(state.Public.Notes);
        Assert.Equal(note.ParticipantId, publicNote.ParticipantId);
        Assert.Equal("Notes Nora", publicNote.DisplayName);
        Assert.Equal("Good breakfast prep.\nps.: kept line break", publicNote.Note);
        Assert.Single(publicNote.Images);

        var draft = state.EligibleDays.Single(day => day.ChallengeDay == 12).Existing;
        Assert.NotNull(draft);
        Assert.Single(draft.Images);

        var storedFileName = Path.GetFileName(new Uri($"https://example.test{image.Url}").AbsolutePath);
        var storedPath = Path.Combine(fixture.ContentRoot, "generated", "longevitymaxxing", "check-in-photos", storedFileName);
        Assert.True(File.Exists(storedPath));
        var storedInfo = Image.Identify(storedPath);
        Assert.NotNull(storedInfo);
        Assert.Equal(1600, storedInfo.Width);
        Assert.Equal(800, storedInfo.Height);

        var edited = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access, 12, 2, 1, 2, 2, "Edited note."),
            now.AddMinutes(5));

        var editedNote = Assert.Single(edited.Notes);
        Assert.Equal("Edited note.", editedNote.Note);
        Assert.Single(editedNote.Images);
        Assert.Equal("Edited note.", Assert.Single(edited.Public.Notes).Note);
    }

    [Fact]
    public async Task PublicNotesOnlyExposeCheckInsAfterPublicNotesCutoff()
    {
        using var fixture = TestChallengeFixture.Create();
        var oldAccess = await fixture.ConfirmParticipantAsync("old-note@example.com", "Old Note");
        var newAccess = await fixture.ConfirmParticipantAsync(
            "new-note@example.com",
            "New Note",
            nowUtc: DateTimeOffset.Parse("2026-06-19T12:00:00Z"));

        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(oldAccess, 1, 2, 2, 2, 2, "legacy note"),
            DateTimeOffset.Parse("2026-06-09T08:00:00Z"));

        var afterCutoff = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(newAccess, 12, 2, 2, 2, 2, "public note"),
            afterCutoff);

        var publicState = fixture.Service.GetPublicState(afterCutoff.AddMinutes(1));

        var publicNote = Assert.Single(publicState.Notes);
        Assert.Equal("public note", publicNote.Note);
        Assert.Equal("New Note", publicNote.DisplayName);
        Assert.DoesNotContain(publicState.Notes, note => note.Note == "legacy note");

        var participantState = fixture.Service.GetParticipantState(newAccess, afterCutoff.AddMinutes(1));
        Assert.Contains(participantState.Notes, note => note.Note == "legacy note" && note.DisplayName == "Old Note");
        Assert.Contains(participantState.Notes, note => note.Note == "public note" && note.DisplayName == "New Note");
    }

    [Fact]
    public async Task ParticipantWithoutUploadedPictureWarmsCachedGravatarWithoutBlocking()
    {
        using var gravatar = CreatePngStream();
        using var gravatarGate = new ManualResetEventSlim(false);
        using var fixture = TestChallengeFixture.Create(gravatarResponse: gravatar.ToArray(), gravatarGate: gravatarGate);
        var access = await fixture.ConfirmParticipantAsync("gravatar@example.com", "Gravatar Gail");

        var state = fixture.Service.GetParticipantState(access);

        Assert.Null(state.Participant.ProfileImageUrl);
        var row = Assert.Single(state.Public.Leaderboard);
        Assert.Equal(state.Participant.ProfileImageUrl, row.ProfileImageUrl);

        Assert.True(SpinWait.SpinUntil(() => fixture.Http.Requests.Count > 0, TimeSpan.FromSeconds(8)));
        gravatarGate.Set();
        Assert.True(SpinWait.SpinUntil(() =>
            fixture.Service.GetParticipantState(access).Participant.ProfileImageUrl is not null,
            TimeSpan.FromSeconds(8)));
        var cached = fixture.Service.GetParticipantState(access);

        Assert.Contains(".gravatar.webp?v=", cached.Participant.ProfileImageUrl);
        Assert.DoesNotContain("gravatar.com", cached.Participant.ProfileImageUrl);
        Assert.Equal(cached.Participant.ProfileImageUrl, cached.Public.Leaderboard.Single().ProfileImageUrl);
        Assert.Single(fixture.Http.Requests);
    }

    [Fact]
    public void PublicStateWarmsUncachedGravatarWithoutBlockingLeaderboard()
    {
        using var gravatar = CreatePngStream();
        using var gravatarGate = new ManualResetEventSlim(false);
        using var fixture = TestChallengeFixture.Create(gravatarResponse: gravatar.ToArray(), gravatarGate: gravatarGate);
        fixture.InsertConfirmedParticipant("uncached@example.com", "Uncached Uma");

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-09T09:00:00Z"));

        var row = Assert.Single(state.Leaderboard);
        Assert.Equal("Uncached Uma", row.DisplayName);
        Assert.Null(row.ProfileImageUrl);

        Assert.True(SpinWait.SpinUntil(() => fixture.Http.Requests.Count > 0, TimeSpan.FromSeconds(8)));
        gravatarGate.Set();
        Assert.True(SpinWait.SpinUntil(() =>
            fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-09T09:00:01Z")).Leaderboard.Single().ProfileImageUrl is not null,
            TimeSpan.FromSeconds(8)));

        var warmed = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-09T09:00:02Z")).Leaderboard.Single();
        Assert.Contains(".gravatar.webp?v=", warmed.ProfileImageUrl);
        Assert.Single(fixture.Http.Requests);
    }

    [Fact]
    public async Task ParticipantWithoutEmailGravatarFallsBackToDisplayNameProfileSlug()
    {
        using var profileImage = CreatePngStream();
        using var fixture = TestChallengeFixture.Create(
            profileJson: "{\"entry\":[{\"thumbnailUrl\":\"https://0.gravatar.com/avatar/profile-hash\"}]}",
            profileImageResponse: profileImage.ToArray());
        var access = await fixture.ConfirmParticipantAsync("plain@example.com", "molnard");

        Assert.True(SpinWait.SpinUntil(() =>
            fixture.Service.GetParticipantState(access).Participant.ProfileImageUrl is not null,
            TimeSpan.FromSeconds(8)));
        var state = fixture.Service.GetParticipantState(access);

        Assert.NotNull(state.Participant.ProfileImageUrl);
        Assert.Contains(".gravatar.webp?v=", state.Participant.ProfileImageUrl);
        Assert.Contains(fixture.Http.Requests, uri => uri.AbsoluteUri.Contains("/avatar/") && uri.AbsoluteUri.Contains("d=404"));
        Assert.Contains(fixture.Http.Requests, uri => uri.AbsoluteUri == "https://gravatar.com/molnard.json");
        Assert.Contains(fixture.Http.Requests, uri => uri.AbsoluteUri == "https://0.gravatar.com/avatar/profile-hash?s=512&r=pg");
        Assert.All(fixture.Http.UserAgents, userAgent => Assert.Contains("LongevityWorldCup/1.0", userAgent));
    }

    [Fact]
    public async Task LinkedAthleteParticipantCanUseChallengeGravatarFallback()
    {
        using var gravatar = CreatePngStream();
        using var fixture = TestChallengeFixture.Create(gravatarResponse: gravatar.ToArray());
        var access = await fixture.ConfirmParticipantAsync("linked-gravatar@example.com", "Linked Gail", athleteLink: "/athlete/linked-gail");

        Assert.True(SpinWait.SpinUntil(() =>
            fixture.Service.GetParticipantState(access).Participant.ProfileImageUrl is not null,
            TimeSpan.FromSeconds(8)));
        var state = fixture.Service.GetParticipantState(access);

        Assert.Equal("/athlete/linked-gail", state.Participant.AthleteUrl);
        Assert.NotNull(state.Participant.ProfileImageUrl);
        Assert.Contains(".gravatar.webp?v=", state.Participant.ProfileImageUrl);
        var row = Assert.Single(state.Public.Leaderboard);
        Assert.Equal("/athlete/linked-gail", row.AthleteUrl);
        Assert.Equal(state.Participant.ProfileImageUrl, row.ProfileImageUrl);
    }

    [Fact]
    public async Task UploadedChallengeProfilePictureTakesPriorityOverGravatar()
    {
        using var gravatar = CreatePngStream();
        using var gravatarGate = new ManualResetEventSlim(false);
        using var fixture = TestChallengeFixture.Create(gravatarResponse: gravatar.ToArray(), gravatarGate: gravatarGate);
        var access = await fixture.ConfirmParticipantAsync("priority@example.com", "Priority Pat");
        Assert.True(SpinWait.SpinUntil(() => fixture.Http.Requests.Count > 0, TimeSpan.FromSeconds(8)));
        gravatarGate.Set();
        Assert.True(SpinWait.SpinUntil(() =>
            fixture.Service.GetParticipantState(access).Participant.ProfileImageUrl is not null,
            TimeSpan.FromSeconds(8)));
        fixture.Http.Requests.Clear();
        using var upload = CreatePngStream();
        var file = CreatePngFormFile(upload);

        var state = await fixture.Service.UploadParticipantProfilePictureAsync(access, file);

        Assert.NotNull(state.Participant.ProfileImageUrl);
        Assert.DoesNotContain(".gravatar.webp", state.Participant.ProfileImageUrl);
        Assert.Empty(fixture.Http.Requests);
    }

    [Fact]
    public async Task LinkedAthleteProfileKeepsChallengeProfilePictureUploadUnavailable()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("linked@example.com", "Linked Lou", athleteLink: "/athlete/linked-lou");
        using var stream = CreatePngStream();
        var file = CreatePngFormFile(stream);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UploadParticipantProfilePictureAsync(access, file));

        Assert.Contains("without a linked Longevity athlete profile", ex.Message);
    }

    [Fact]
    public async Task LeaderboardRanksScoreBeforeConsistencyAndKeepsLegacyNotesOutOfPublicState()
    {
        using var fixture = TestChallengeFixture.Create();
        var alice = await fixture.ConfirmParticipantAsync("alice@example.com", "Alice");
        var bob = await fixture.ConfirmParticipantAsync("bob@example.com", "Bob");

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            alice,
            1,
            2,
            2,
            2,
            2,
            "perfect start"), DateTimeOffset.Parse("2026-06-09T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            alice,
            2,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-10T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            bob,
            1,
            0,
            0,
            0,
            0,
            null), DateTimeOffset.Parse("2026-06-09T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            bob,
            2,
            0,
            0,
            0,
            0,
            "still returned"), DateTimeOffset.Parse("2026-06-10T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            bob,
            3,
            0,
            0,
            0,
            0,
            null), DateTimeOffset.Parse("2026-06-11T08:00:00Z"));

        var publicState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-11T09:00:00Z"));

        Assert.Equal("Alice", publicState.Leaderboard[0].DisplayName);
        Assert.Equal(8, publicState.Leaderboard[0].TotalPoints);
        Assert.Equal(2, publicState.Leaderboard[0].CheckedInDays);
        Assert.Equal("Bob", publicState.Leaderboard[1].DisplayName);
        Assert.Equal(0, publicState.Leaderboard[1].TotalPoints);
        Assert.Equal(3, publicState.Leaderboard[1].CheckedInDays);
        Assert.DoesNotContain(publicState.Leaderboard[0].Badges, badge => badge.Contains("perfect start", StringComparison.OrdinalIgnoreCase));

        var participantState = fixture.Service.GetParticipantState(alice, DateTimeOffset.Parse("2026-06-11T09:00:00Z"));
        Assert.DoesNotContain(publicState.Notes, note => note.Note == "perfect start");
        Assert.DoesNotContain(publicState.Notes, note => note.Note == "still returned");
        Assert.Contains(participantState.Notes, note => note.Note == "perfect start");
        Assert.Contains(participantState.Notes, note => note.Note == "still returned");
    }

    [Fact]
    public async Task LeaderboardBreaksPerformanceTiesByMainLeaderboardRankThenOlderAthlete()
    {
        using var fixture = TestChallengeFixture.Create();
        fixture.AddAthleteTieBreak("young_ranked", currentPlacement: 2, birthYear: 1990, birthMonth: 1, birthDay: 1);
        fixture.AddAthleteTieBreak("older_ranked", currentPlacement: 3, birthYear: 1970, birthMonth: 1, birthDay: 1);
        fixture.AddAthleteTieBreak("same_rank_old", currentPlacement: 5, birthYear: 1975, birthMonth: 1, birthDay: 1);
        fixture.AddAthleteTieBreak("same_rank_young", currentPlacement: 5, birthYear: 1985, birthMonth: 1, birthDay: 1);

        var plain = await fixture.ConfirmParticipantAsync("plain@example.com", "Aaron Plain");
        var young = await fixture.ConfirmParticipantAsync("young@example.com", "Young Ranked", athleteLink: "/athlete/young-ranked");
        var older = await fixture.ConfirmParticipantAsync("older@example.com", "Zelda Older", athleteLink: "/athlete/older-ranked");
        var sameRankOld = await fixture.ConfirmParticipantAsync("same-old@example.com", "Same Rank Old", athleteLink: "/athlete/same-rank-old");
        var sameRankYoung = await fixture.ConfirmParticipantAsync("same-young@example.com", "Same Rank Young", athleteLink: "/athlete/same-rank-young");

        var checkInAt = DateTimeOffset.Parse("2026-06-09T08:00:00Z");
        foreach (var access in new[] { plain, young, older, sameRankOld, sameRankYoung })
        {
            fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
                access,
                1,
                2,
                2,
                2,
                2,
                null), checkInAt);
        }

        var publicState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-09T09:00:00Z"));

        Assert.Equal(
            ["Young Ranked", "Older Ranked", "Same Rank Old", "Same Rank Young", "Aaron Plain"],
            publicState.Leaderboard.Select(row => row.DisplayName).ToArray());
    }

    [Fact]
    public async Task FirstCheckInCountsForConsistencyButNotHabitPoints()
    {
        using var fixture = TestChallengeFixture.Create();
        var alice = await fixture.ConfirmParticipantAsync("alice@example.com", "Alice");
        var bob = await fixture.ConfirmParticipantAsync("bob@example.com", "Bob");

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            alice,
            1,
            2,
            2,
            2,
            2,
            "practice"), DateTimeOffset.Parse("2026-06-09T08:00:00Z"));

        var practiceState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-09T09:00:00Z"));
        var alicePractice = practiceState.Leaderboard.Single(row => row.DisplayName == "Alice");
        var aliceDay1 = alicePractice.Cells.Single(cell => cell.ChallengeDay == 1);
        Assert.Equal(1, alicePractice.CheckedInDays);
        Assert.Equal(1, alicePractice.CurrentStreak);
        Assert.Equal(0, alicePractice.TotalPoints);
        Assert.True(aliceDay1.CheckedIn);
        Assert.False(aliceDay1.CountsForScore);
        Assert.Null(aliceDay1.Score);
        Assert.Equal(2, aliceDay1.Sleep);
        Assert.Equal(2, aliceDay1.Exercise);
        Assert.Equal(2, aliceDay1.Nutrition);
        Assert.Equal(2, aliceDay1.Vices);
        Assert.DoesNotContain("Sleep", alicePractice.Badges);

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            bob,
            2,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-10T08:00:00Z"));

        var scoredState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-10T09:00:00Z"));
        Assert.Equal("Bob", scoredState.Leaderboard[0].DisplayName);
        Assert.Equal(8, scoredState.Leaderboard[0].TotalPoints);
        var bobDay2 = scoredState.Leaderboard[0].Cells.Single(cell => cell.ChallengeDay == 2);
        Assert.Equal(2, bobDay2.Sleep);
        Assert.Equal(2, bobDay2.Exercise);
        Assert.Equal(2, bobDay2.Nutrition);
        Assert.Equal(2, bobDay2.Vices);
        Assert.Equal("Alice", scoredState.Leaderboard[1].DisplayName);
        Assert.Equal(0, scoredState.Leaderboard[1].TotalPoints);
    }

    [Fact]
    public async Task EarlierKnownCheckInStaysPracticeWhenSignupDerivedPracticeDayIsLater()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("zyron@example.com", "Zyron");

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            1,
            2,
            2,
            2,
            1,
            null), DateTimeOffset.Parse("2026-06-09T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            2,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-10T08:00:00Z"));

        fixture.Db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                UPDATE LongevitymaxxingParticipants
                SET CreatedAtUtc = @created,
                    UpdatedAtUtc = @created
                WHERE AccessToken = @access;
                """;
            cmd.Parameters.AddWithValue("@created", DateTimeOffset.Parse("2026-06-09T12:00:00Z").ToString("o"));
            cmd.Parameters.AddWithValue("@access", access);
            cmd.ExecuteNonQuery();
        });

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-10T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);
        var day1 = row.Cells.Single(cell => cell.ChallengeDay == 1);
        var day2 = row.Cells.Single(cell => cell.ChallengeDay == 2);

        Assert.False(day1.CountsForScore);
        Assert.Null(day1.Score);
        Assert.True(day2.CountsForScore);
        Assert.Equal(8, day2.Score);
        Assert.Equal(8, row.TotalPoints);
    }

    [Fact]
    public async Task HabitPointsRampSlightlyAfterPracticeDay()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("ramp@example.com", "Ramp Rae");

        SubmitChallengeDays(fixture, access, 14, 2, 2, 2, 2);

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-22T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);

        Assert.Equal(11, state.DailyMaxScore);
        Assert.Equal(125, row.TotalPoints);
        Assert.Null(row.Cells.Single(cell => cell.ChallengeDay == 1).Score);
        Assert.Equal(8, row.Cells.Single(cell => cell.ChallengeDay == 2).Score);
        Assert.Equal(11, row.Cells.Single(cell => cell.ChallengeDay == 14).Score);
    }

    [Fact]
    public async Task LeaderboardAndDailyRemindersContinuePastOriginalDuration()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("ongoing@example.com", "Ongoing Ona");
        SubmitChallengeDays(fixture, access, days: 14, sleep: 2, exercise: 2, nutrition: 2, vices: 2);

        var reminder = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-23T08:05:00Z")));
        Assert.Equal(15, reminder.ChallengeDay);
        Assert.True(reminder.CountsForScore);

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            15,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-23T08:10:00Z"));

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-23T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);
        var day15 = row.Cells.Single(cell => cell.ChallengeDay == 15);

        Assert.Equal("active", state.Phase);
        Assert.True(state.SignupOpen);
        Assert.Empty(state.Podium);
        Assert.Contains(state.Days, day => day.ChallengeDay == 15);
        Assert.Contains(state.Days, day => day.ChallengeDay == 16);
        Assert.True(day15.CheckedIn);
        Assert.True(day15.CountsForScore);
        Assert.Equal(11, day15.Score);
    }

    [Fact]
    public async Task LeaderboardPerformanceCountsOnlyLatestFourteenChallengeDays()
    {
        using var fixture = TestChallengeFixture.Create();
        var oldAccess = await fixture.ConfirmParticipantAsync("old-window@example.com", "Old Window");
        var recentAccess = await fixture.ConfirmParticipantAsync("recent-window@example.com", "Recent Window");

        for (var day = 1; day <= 16; day++)
        {
            fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
                oldAccess,
                day,
                2,
                2,
                2,
                2,
                null), DateTimeOffset.Parse("2026-06-09T08:00:00Z").AddDays(day - 1));
        }

        for (var day = 3; day <= 16; day++)
        {
            fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
                recentAccess,
                day,
                2,
                2,
                2,
                2,
                null), DateTimeOffset.Parse("2026-06-09T08:00:00Z").AddDays(day - 1));
        }

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-23T09:00:00Z"));

        var recent = state.Leaderboard.Single(row => row.DisplayName == "Recent Window");
        var old = state.Leaderboard.Single(row => row.DisplayName == "Old Window");
        Assert.Equal(14, recent.CheckedInDays);
        Assert.Equal(14, old.CheckedInDays);
        Assert.InRange(recent.CurrentStreak, 0, 14);
        Assert.Equal(14, old.CurrentStreak);
        Assert.Equal(recent.TotalPoints, old.TotalPoints);
        Assert.True(old.Cells.Single(cell => cell.ChallengeDay == 1).CheckedIn);
        Assert.True(old.Cells.Single(cell => cell.ChallengeDay == 2).CheckedIn);
        Assert.True(old.Cells.Single(cell => cell.ChallengeDay == 16).CheckedIn);
        Assert.True(old.TotalPoints < old.Cells.Where(cell => cell.CheckedIn && cell.Score is not null).Sum(cell => cell.Score!.Value));
    }

    [Fact]
    public async Task SignupAfterOriginalDurationStartsFromSignupDateWithPersonalPracticeDay()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-25T12:00:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "ongoing-signup@example.com",
            "Ongoing Signup",
            "UTC",
            null), signup);
        var access = await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), signup.AddMinutes(1));

        Assert.Empty(access.State.EligibleDays);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access.AccessToken, 17, 2, 2, 2, 2, null),
            DateTimeOffset.Parse("2026-06-26T08:00:00Z")));

        var nextDay = fixture.Service.GetParticipantState(access.AccessToken, DateTimeOffset.Parse("2026-06-26T08:05:00Z"));
        var practice = Assert.Single(nextDay.EligibleDays);
        Assert.Equal(18, practice.ChallengeDay);
        Assert.False(practice.CountsForScore);

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            18,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-26T08:10:00Z"));

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-26T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);
        var day17 = row.Cells.Single(cell => cell.ChallengeDay == 17);
        var day18 = row.Cells.Single(cell => cell.ChallengeDay == 18);

        Assert.False(day17.CheckedIn);
        Assert.True(day18.CheckedIn);
        Assert.False(day18.CountsForScore);
        Assert.Null(day18.Score);
        Assert.Equal(1, row.CheckedInDays);
        Assert.Equal(0, row.TotalPoints);
    }

    [Fact]
    public async Task SignupDateControlsPracticeDayWhenConfirmationIsNextLocalDay()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-25T23:50:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "overnight-signup@example.com",
            "Overnight Signup",
            "UTC",
            null), signup);

        var access = await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            DateTimeOffset.Parse("2026-06-26T00:05:00Z"));

        var practice = Assert.Single(access.State.EligibleDays);
        Assert.Equal(18, practice.ChallengeDay);
        Assert.False(practice.CountsForScore);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access.AccessToken, 17, 2, 2, 2, 2, null),
            DateTimeOffset.Parse("2026-06-26T08:00:00Z")));

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            18,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-26T08:10:00Z"));

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-26T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);
        Assert.Equal(1, row.CheckedInDays);
        Assert.Equal(0, row.TotalPoints);
        Assert.False(row.Cells.Single(cell => cell.ChallengeDay == 18).CountsForScore);
    }

    [Fact]
    public async Task DailySlipGetsMaxPointsOnlyAfterActuallyPerfectPreviousDay()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("slip@example.com", "Slip Sam");

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            1,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-09T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            2,
            0,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-10T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            3,
            2,
            1,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-11T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            4,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-12T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            5,
            2,
            1,
            1,
            2,
            null), DateTimeOffset.Parse("2026-06-13T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            6,
            0,
            1,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-14T08:00:00Z"));

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-14T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);

        Assert.Equal(8, row.Cells.Single(cell => cell.ChallengeDay == 2).Score);
        Assert.Equal(7, row.Cells.Single(cell => cell.ChallengeDay == 3).Score);
        Assert.Equal(9, row.Cells.Single(cell => cell.ChallengeDay == 4).Score);
        Assert.Equal(9, row.Cells.Single(cell => cell.ChallengeDay == 5).Score);
        Assert.Equal(6, row.Cells.Single(cell => cell.ChallengeDay == 6).Score);
        Assert.Equal(39, row.TotalPoints);
    }

    [Fact]
    public async Task DailyReminderCandidatesSkipCompletedTargetDayAndStoppedEmails()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("daily@example.com", "Daily Dana");
        var earlyTime = DateTimeOffset.Parse("2026-06-09T07:05:00Z");
        var reminderTime = DateTimeOffset.Parse("2026-06-09T11:05:00Z");

        Assert.Empty(fixture.Service.GetDailyReminderCandidates(earlyTime));
        var beforeCheckIn = fixture.Service.GetDailyReminderCandidates(reminderTime);
        var reminder = Assert.Single(beforeCheckIn);
        Assert.Equal(1, reminder.ChallengeDay);

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            1,
            1,
            1,
            1,
            1,
            null), reminderTime.AddMinutes(10));

        Assert.Empty(fixture.Service.GetDailyReminderCandidates(reminderTime.AddMinutes(20)));

        fixture.Service.StopChallengeEmails(access, reminderTime.AddMinutes(30));
        Assert.Empty(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-10T08:05:00Z")));
        var stoppedEmails = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-10T08:06:00Z"));
        Assert.True(stoppedEmails.Participant.ChallengeEmailsStopped);
        Assert.False(stoppedEmails.Participant.ChallengeInactive);
        Assert.True(stoppedEmails.Public.Leaderboard.Single().ChallengeEmailsStopped);
        Assert.False(stoppedEmails.Public.Leaderboard.Single().ChallengeInactive);
    }

    [Fact]
    public async Task StoppedEmailsParticipantWithoutCheckInsIsInactiveAfterMissThreshold()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("legacy-stop@example.com", "Guilherme Schwarz");

        fixture.Service.StopChallengeEmails(access, DateTimeOffset.Parse("2026-06-19T12:57:59Z"));

        var state = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-20T09:00:00Z"));

        Assert.True(state.Participant.ChallengeEmailsStopped);
        Assert.True(state.Participant.ChallengeInactive);
        var row = Assert.Single(state.Public.Leaderboard);
        Assert.True(row.ChallengeEmailsStopped);
        Assert.True(row.ChallengeInactive);
    }

    [Fact]
    public async Task DailyReminderCandidatesStopAfterThreeConsecutiveMissedScoredDays()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("missed@example.com", "Missed Max");

        var beforeThreshold = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-11T08:05:00Z"));
        Assert.Single(beforeThreshold);
        Assert.Equal(3, beforeThreshold[0].ChallengeDay);

        Assert.Empty(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-12T08:05:00Z")));
        var stillActive = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-12T08:05:30Z"));
        Assert.False(stillActive.Participant.ChallengeEmailsStopped);

        fixture.Service.ApplyDailyReminderStopRules(DateTimeOffset.Parse("2026-06-12T08:05:00Z"));
        var stopped = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-12T08:06:00Z"));
        Assert.False(stopped.Participant.ChallengeEmailsStopped);
        Assert.True(stopped.Participant.ChallengeInactive);
        Assert.False(stopped.Public.Leaderboard.Single().ChallengeEmailsStopped);
        Assert.True(stopped.Public.Leaderboard.Single().ChallengeInactive);
    }

    [Fact]
    public async Task DailyReminderMissThresholdIgnoresScoredDaysBeforeSignup()
    {
        using var fixture = TestChallengeFixture.Create(signupClosesAtUtc: "2026-06-12T22:00:00Z");
        var signup = DateTimeOffset.Parse("2026-06-10T12:00:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "joined-missed@example.com",
            "Joined Jenny",
            "UTC",
            null), signup);
        await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), signup.AddMinutes(1));

        var candidates = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-12T08:05:00Z"));

        var candidate = Assert.Single(candidates);
        Assert.Equal(4, candidate.ChallengeDay);
    }

    [Fact]
    public async Task DailyReminderMissThresholdCanBeClearedByEligibleCatchUpCheckIns()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("resume@example.com", "Resume Rae");
        SubmitChallengeDays(fixture, access, 10, 2, 2, 2, 2);

        Assert.Empty(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-21T08:05:00Z")));
        fixture.Service.ApplyDailyReminderStopRules(DateTimeOffset.Parse("2026-06-21T08:05:00Z"));
        var stopped = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-21T08:06:00Z"));
        Assert.True(stopped.Participant.ChallengeInactive);
        Assert.Contains(stopped.EligibleDays, day => day.ChallengeDay == 12);
        Assert.Contains(stopped.EligibleDays, day => day.ChallengeDay == 13);

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            12,
            2,
            2,
            2,
            2,
            "catch-up day 12"), DateTimeOffset.Parse("2026-06-21T08:40:00Z"));
        var caughtUp = fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            13,
            2,
            2,
            2,
            2,
            "catch-up day 13"), DateTimeOffset.Parse("2026-06-21T08:45:00Z"));

        var row = Assert.Single(caughtUp.Public.Leaderboard);
        Assert.False(caughtUp.Participant.ChallengeInactive);
        Assert.False(row.ChallengeInactive);
        Assert.True(row.Cells.Single(cell => cell.ChallengeDay == 12).CheckedIn);
        Assert.True(row.Cells.Single(cell => cell.ChallengeDay == 13).CheckedIn);
        Assert.Contains(caughtUp.Notes, note => note.Note == "catch-up day 12");
        Assert.Contains(caughtUp.Notes, note => note.Note == "catch-up day 13");

        var resumed = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-22T08:05:00Z")));
        Assert.Equal(caughtUp.Participant.Id, resumed.ParticipantId);
        Assert.Equal(14, resumed.ChallengeDay);
    }

    [Fact]
    public async Task DailyReminderStopRulesRepairStaleMissedDayInactiveAfterCatchUp()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("stale-resume@example.com", "Stale Rae");
        SubmitChallengeDays(fixture, access, 10, 2, 2, 2, 2);

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            12,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-21T08:40:00Z"));
        var caughtUp = fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            13,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-21T08:45:00Z"));

        fixture.Db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                UPDATE LongevitymaxxingParticipants
                SET ChallengeInactiveAtUtc = @inactive,
                    ChallengeInactiveReason = @reason,
                    UpdatedAtUtc = @inactive
                WHERE Id = @participantId;
                """;
            cmd.Parameters.AddWithValue("@inactive", DateTimeOffset.Parse("2026-06-21T05:00:00Z").ToString("o"));
            cmd.Parameters.AddWithValue("@reason", "missed-scored-days");
            cmd.Parameters.AddWithValue("@participantId", caughtUp.Participant.Id);
            cmd.ExecuteNonQuery();
        });

        fixture.Service.ApplyDailyReminderStopRules(DateTimeOffset.Parse("2026-06-22T08:04:00Z"));

        var resumed = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-22T08:05:00Z")));
        Assert.Equal(caughtUp.Participant.Id, resumed.ParticipantId);
        Assert.Equal(14, resumed.ChallengeDay);
    }

    [Fact]
    public async Task DailyReminderEmailIncludesUpdatedCallSchedule()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "daily-call@example.com",
            "Daily Call Dana",
            timeZoneId: "Europe/Budapest");

        var reminder = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-09T11:05:00Z")));
        Assert.True(reminder.IncludeCallScheduleUpdate);
        var content = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(
            reminder,
            fixture.Service.BuildAccessUrl(reminder.AccessToken),
            fixture.Service.BuildStopUrl(reminder.StopToken));

        Assert.Contains("Updated call schedule:", content.TextBody);
        Assert.DoesNotContain("- Kickoff:", content.TextBody);
        Assert.DoesNotContain("- Midpoint:", content.TextBody);
        Assert.DoesNotContain("- Finale:", content.TextBody);
        Assert.Contains("- Community call: 2026-06-14 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Contains("- Community call: 2026-06-21 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Contains("Call link: https://meet.example.test", content.TextBody);
        Assert.DoesNotContain("2026-06-07 06:30 UTC", content.TextBody);
        Assert.DoesNotContain("- Kickoff: 2026-06-07 08:30", content.TextBody);
        Assert.DoesNotContain("2026-06-22 15:00", content.TextBody);
        Assert.Empty(content.Attachments);

        fixture.Service.MarkCallScheduleUpdateNoticeSent(reminder.ParticipantId, DateTimeOffset.Parse("2026-06-09T11:06:00Z"));

        var laterReminder = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-09T11:07:00Z")));
        Assert.False(laterReminder.IncludeCallScheduleUpdate);
        var laterContent = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(
            laterReminder,
            fixture.Service.BuildAccessUrl(laterReminder.AccessToken),
            fixture.Service.BuildStopUrl(laterReminder.StopToken));
        Assert.DoesNotContain("Updated call schedule:", laterContent.TextBody);
    }

    [Fact]
    public async Task ResendSendsLinkByEmailWithoutReturningAccessState()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("resend@example.com", "Resend Rae");

        var result = await fixture.Service.ResendAccessLinkAsync(
            "resend@example.com",
            DateTimeOffset.Parse("2026-06-07T12:10:00Z"));

        Assert.Equal("Link sent.", result.Message);
        var link = Assert.Single(fixture.Email.AccessLinks);
        Assert.Equal("resend@example.com", link.Email);
        Assert.Equal(access, ReadQueryToken(link.Url, "token"));
    }

    [Fact]
    public async Task WeeklyCommunityCallIsSelectedAndVideoLinkIsParticipantOnly()
    {
        using var fixture = TestChallengeFixture.Create(callSelectionClosesAtUtc: "2026-06-06T18:00:00Z");
        var one = await fixture.ConfirmParticipantAsync("one@example.com", "One");
        await fixture.ConfirmParticipantAsync("two@example.com", "Two");
        await fixture.ConfirmParticipantAsync("three@example.com", "Three");

        var beforeClose = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-06T17:59:00Z"));
        Assert.Equal("community-2026-06-07-a", beforeClose.Calls.Single(c => c.Key == "community-2026-06-07").SelectedSlot?.Id);
        Assert.Equal("2026-06-08T00:00:00.0000000+00:00", beforeClose.SignupClosesAtUtc);

        var participantBeforeClose = fixture.Service.GetParticipantState(one, DateTimeOffset.Parse("2026-06-06T17:59:30Z"));
        var communityCall = participantBeforeClose.Calls.Single(c => c.Key == "community-2026-06-07");
        Assert.Equal("community-2026-06-07-a", communityCall.SelectedSlot?.Id);
        Assert.Equal("https://meet.example.test", communityCall.VideoCallUrl);

        fixture.Service.TrySelectCallSlots(DateTimeOffset.Parse("2026-06-06T18:01:00Z"));

        var publicState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-06T18:02:00Z"));
        Assert.Equal("community-2026-06-07-a", publicState.Calls.Single(c => c.Key == "community-2026-06-07").SelectedSlot?.Id);

        var participantState = fixture.Service.GetParticipantState(one, DateTimeOffset.Parse("2026-06-06T18:03:00Z"));
        Assert.Equal("https://meet.example.test", participantState.Calls.Single(c => c.Key == "community-2026-06-07").VideoCallUrl);
    }

    [Fact]
    public async Task CallReminderCandidatesCanSendSundayCommunityCall24HourReminderBeforeSignupCloses()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "call@example.com",
            "Call Casey",
            timeZoneId: "Europe/Budapest");

        var candidates = fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-06T06:35:00Z"));
        var reminder = Assert.Single(candidates);
        Assert.Equal("community-2026-06-07", reminder.CallKey);
        Assert.Equal("Community call", reminder.CallLabel);
        Assert.Equal("24h", reminder.ReminderKind);
        Assert.Equal("2026-06-07T06:30:00.0000000+00:00", reminder.StartsAtUtc);
        Assert.Equal("Europe/Budapest", reminder.TimeZoneId);
        Assert.Equal(4, reminder.Calls.Count);

        Assert.Empty(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-06T06:35:00Z")));

        fixture.Service.MarkCallReminderSent(reminder.ParticipantId, reminder.CallKey, reminder.ReminderKind, DateTimeOffset.Parse("2026-06-06T06:36:00Z"));
        Assert.Empty(fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-06T06:37:00Z")));
    }

    [Fact]
    public async Task StoppingCommunityCallEmailsKeepsDailyChallengeRemindersEnabled()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "call-opt-out@example.com",
            "Call Opt Out",
            timeZoneId: "Europe/Budapest");

        var callReminder = Assert.Single(
            fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-06T06:35:00Z")));

        fixture.Service.StopCommunityCallEmails(
            callReminder.StopToken,
            DateTimeOffset.Parse("2026-06-06T06:36:00Z"));

        Assert.Empty(fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-06T06:37:00Z")));
        Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-09T08:05:00Z")));
    }

    [Fact]
    public async Task CallReminderCandidatesExcludeCallsDuringParticipantLocalQuietHours()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "quiet-hours@example.com",
            "Quiet Hours",
            timeZoneId: "America/New_York");
        await fixture.ConfirmParticipantAsync(
            "daytime@example.com",
            "Daytime Dana",
            timeZoneId: "Europe/Budapest");

        var reminder = Assert.Single(
            fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-06T06:35:00Z")));

        Assert.Equal("Daytime Dana", reminder.DisplayName);
        Assert.Equal("Europe/Budapest", reminder.TimeZoneId);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(6, 59, false)]
    [InlineData(7, 0, true)]
    [InlineData(20, 59, true)]
    [InlineData(21, 0, false)]
    [InlineData(23, 59, false)]
    public void CommunityCallReminderLocalTimeWindowIncludesSevenAndExcludesTwentyOne(
        int hour,
        int minute,
        bool expected)
    {
        Assert.Equal(
            expected,
            LongevitymaxxingChallengeService.IsCommunityCallReminderLocalTimeAllowed(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void CallAnnouncementCandidatesUseOneHourWindowAndSendOncePerCall()
    {
        using var fixture = TestChallengeFixture.Create();

        Assert.Empty(fixture.Service.GetCallAnnouncementCandidates(DateTimeOffset.Parse("2026-06-07T05:29:59Z")));

        var candidate = Assert.Single(fixture.Service.GetCallAnnouncementCandidates(DateTimeOffset.Parse("2026-06-07T05:35:00Z")));
        Assert.Equal("community-2026-06-07", candidate.CallKey);
        Assert.Equal("Community call", candidate.CallLabel);
        Assert.Equal("1h", candidate.ReminderKind);
        Assert.Equal("2026-06-07T06:30:00.0000000+00:00", candidate.StartsAtUtc);
        Assert.Equal("https://meet.example.test", candidate.VideoCallUrl);

        fixture.Service.MarkCallAnnouncementQueued(candidate.CallKey, candidate.ReminderKind, "event-1", DateTimeOffset.Parse("2026-06-07T05:36:00Z"));

        Assert.Empty(fixture.Service.GetCallAnnouncementCandidates(DateTimeOffset.Parse("2026-06-07T05:37:00Z")));
        Assert.Empty(fixture.Service.GetCallAnnouncementCandidates(DateTimeOffset.Parse("2026-06-07T06:30:00Z")));
    }

    [Fact]
    public async Task ReminderJobQueuesHiddenSocialOnlyCallAnnouncementEvent()
    {
        using var fixture = TestChallengeFixture.Create();
        using var events = CreateEventDataService(fixture);
        var job = new LongevitymaxxingReminderJob(
            fixture.Service,
            events,
            fixture.Email,
            NullLogger<LongevitymaxxingReminderJob>.Instance);

        await job.ExecuteAtAsync(DateTimeOffset.Parse("2026-06-07T05:35:00Z"));
        await job.ExecuteAtAsync(DateTimeOffset.Parse("2026-06-07T05:36:00Z"));

        var row = Assert.Single(ReadCustomEvents(fixture.Db));
        Assert.Contains("Longevitymaxxing community call starts at 06:30 UTC", row.Text);
        Assert.Contains("Participation is open. Join here:\nhttps://meet.example.test", row.Text);
        Assert.DoesNotContain("token=", row.Text);
        Assert.Equal(0, row.VisibleOnWebsite);
        Assert.Equal(1, row.SlackProcessed);
        Assert.Equal(0, row.XProcessed);
        Assert.Equal(0, row.ThreadsProcessed);
        Assert.Equal(0, row.FacebookProcessed);

        var log = Assert.Single(ReadCallAnnouncementLogs(fixture.Db));
        Assert.Equal("community-2026-06-07", log.CallKey);
        Assert.Equal("1h", log.ReminderKind);
        Assert.Equal(row.Id, log.EventId);
    }

    [Fact]
    public async Task CallReminderEmailIncludesTimeLinkParticipantPageAndCalendarInvite()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "call@example.com",
            "Call Casey",
            timeZoneId: "Europe/Budapest");

        var reminder = Assert.Single(fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-06T06:35:00Z")));
        var content = SmtpLongevitymaxxingEmailSender.BuildCallReminderEmailContent(
            reminder,
            fixture.Service.BuildAccessUrl(reminder.AccessToken),
            fixture.Service.BuildCommunityCallStopUrl(reminder.StopToken));

        Assert.Contains("Call link:\nhttps://meet.example.test", content.TextBody);
        Assert.Contains("The Longevitymaxxing Community call starts", content.TextBody);
        Assert.Contains("2026-06-07 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Equal("Longevitymaxxing Community call reminder", content.Subject);
        Assert.Contains("Participant page:\nhttps://example.test/longevitymaxxing?", content.TextBody);
        Assert.Contains("Stop community call emails: https://example.test/longevitymaxxing?stop=", content.TextBody);
        Assert.Contains("&scope=community-call", content.TextBody);
        Assert.DoesNotContain("Stop Challenge reminder emails:", content.TextBody);
        Assert.DoesNotContain("2026-06-08 06:30 UTC", content.TextBody);
        Assert.DoesNotContain("UTC+02:00", content.TextBody);
        Assert.DoesNotContain("Full call schedule:", content.TextBody);
        Assert.DoesNotContain("- Midpoint:", content.TextBody);
        var attachment = Assert.Single(content.Attachments);
        Assert.Equal("longevitymaxxing-community-call.ics", attachment.FileName);
        Assert.Equal("text/calendar; charset=utf-8", attachment.ContentType);
        Assert.Equal(1, CountOccurrences(attachment.Text, "BEGIN:VEVENT"));
        Assert.Contains("SUMMARY:Longevitymaxxing Community call", attachment.Text);
        Assert.Contains("DTSTART:20260607T063000Z", attachment.Text);
        Assert.Contains("LOCATION:https://meet.example.test", attachment.Text);
        Assert.Contains("Participant page: https://example.test/longevitymaxxing?", attachment.Text);
    }

    [Fact]
    public async Task ChallengeStartCandidatesWaitUntilChallengeStartAndSendOnce()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync("one@example.com", "One");
        await fixture.ConfirmParticipantAsync("two@example.com", "Two");

        Assert.Empty(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-07T23:59:00Z")));

        var candidates = fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-08T00:01:00Z"));

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(4, candidate.Calls.Count);
            Assert.All(candidate.Calls, call =>
            {
                Assert.NotNull(call.SelectedSlot);
                Assert.Equal("https://meet.example.test", call.VideoCallUrl);
            });
        });
        Assert.Equal("2026-06-14T06:30:00.0000000+00:00", candidates[0].Calls.First().SelectedSlot?.StartsAtUtc);

        fixture.Service.MarkChallengeStartSent(candidates[0].ParticipantId, DateTimeOffset.Parse("2026-06-08T00:02:00Z"));

        var remaining = fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-08T00:03:00Z"));
        var single = Assert.Single(remaining);
        Assert.NotEqual(candidates[0].ParticipantId, single.ParticipantId);

        fixture.Service.MarkChallengeStartSent(single.ParticipantId, DateTimeOffset.Parse("2026-06-08T00:04:00Z"));
        Assert.Empty(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-08T00:05:00Z")));
    }

    [Fact]
    public async Task ChallengeStartEmailIncludesAllCallsLinksTimezoneAndCalendarInvite()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "start@example.com",
            "Start Sam",
            timeZoneId: "Europe/Budapest");

        var start = Assert.Single(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-08T00:01:00Z")));
        var content = SmtpLongevitymaxxingEmailSender.BuildChallengeStartEmailContent(
            start,
            fixture.Service.BuildAccessUrl(start.AccessToken),
            fixture.Service.BuildStopUrl(start.StopToken));

        Assert.Contains("Timezone: Europe/Budapest", content.TextBody);
        Assert.Contains("Call link: https://meet.example.test", content.TextBody);
        Assert.Contains("- Community call: 2026-06-14 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Contains("- Community call: 2026-06-21 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Contains("- Community call: 2026-06-28 08:30 (Europe/Budapest)", content.TextBody);
        Assert.DoesNotContain("2026-06-08 06:30 UTC", content.TextBody);
        Assert.DoesNotContain("2026-06-07 08:30", content.TextBody);
        Assert.DoesNotContain("2026-06-22 15:00", content.TextBody);
        Assert.DoesNotContain("UTC+02:00", content.TextBody);
        Assert.DoesNotContain("- Kickoff:", content.TextBody);
        Assert.DoesNotContain("- Midpoint:", content.TextBody);
        Assert.DoesNotContain("- Finale:", content.TextBody);
        Assert.Contains("A calendar invite with all selected calls is attached.", content.TextBody);

        var attachment = Assert.Single(content.Attachments);
        Assert.Equal("longevitymaxxing-calls.ics", attachment.FileName);
        Assert.Equal(4, CountOccurrences(attachment.Text, "BEGIN:VEVENT"));
        Assert.Contains("SUMMARY:Longevitymaxxing Community call", attachment.Text);
        Assert.DoesNotContain("SUMMARY:Longevitymaxxing Community call call", attachment.Text);
    }

    [Fact]
    public async Task ChallengeStartEmailAfterOriginalFinaleIncludesNextWeeklyCommunityCalls()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
        await fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("post-calls-start@example.com", "Post Calls Pat", "UTC", null),
            signup);
        await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            signup.AddMinutes(1));

        var start = Assert.Single(fixture.Service.GetChallengeStartCandidates(signup.AddMinutes(2)));
        Assert.NotEmpty(start.Calls);

        var content = SmtpLongevitymaxxingEmailSender.BuildChallengeStartEmailContent(
            start,
            fixture.Service.BuildAccessUrl(start.AccessToken),
            fixture.Service.BuildStopUrl(start.StopToken));

        Assert.Contains("Calls:", content.TextBody);
        Assert.Contains("- Community call: 2026-06-28 06:30 (UTC)", content.TextBody);
        Assert.Contains("calendar invite", content.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Single(content.Attachments);
    }

    [Fact]
    public async Task DailyReminderAfterOriginalFinaleIncludesNextWeeklyCommunityCalls()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
        await fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("post-calls-daily@example.com", "Post Calls Dana", "UTC", null),
            signup);
        await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            signup.AddMinutes(1));

        var reminder = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-25T08:05:00Z")));
        Assert.NotEmpty(reminder.Calls);
        Assert.True(reminder.IncludeCallScheduleUpdate);

        var content = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(
            reminder,
            fixture.Service.BuildAccessUrl(reminder.AccessToken),
            fixture.Service.BuildStopUrl(reminder.StopToken));

        Assert.Contains("Updated call schedule:", content.TextBody);
        Assert.Contains("- Community call: 2026-06-28 06:30 (UTC)", content.TextBody);
        Assert.Contains("Stop Challenge reminder emails:", content.TextBody);
    }

    [Fact]
    public async Task SignupAfterChallengeStartIsAccepted()
    {
        using var fixture = TestChallengeFixture.Create();

        await fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("started@example.com", "Started Sam", "UTC", null),
            DateTimeOffset.Parse("2026-06-08T00:01:00Z"));
        var access = await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            DateTimeOffset.Parse("2026-06-08T00:02:00Z"));

        var state = fixture.Service.GetParticipantState(access.AccessToken, DateTimeOffset.Parse("2026-06-09T08:05:00Z"));
        var day = Assert.Single(state.EligibleDays);
        Assert.Equal(1, day.ChallengeDay);
        Assert.False(day.CountsForScore);
    }

    [Fact]
    public async Task DayFourteenResultEventsIncludeTopThreeAndLinkedFinishersAfterGraceWindow()
    {
        using var fixture = TestChallengeFixture.Create();
        var bob = await fixture.ConfirmParticipantAsync("bob@example.com", "Bob");
        var cara = await fixture.ConfirmParticipantAsync("cara@example.com", "Cara");
        var dan = await fixture.ConfirmParticipantAsync("dan@example.com", "Dan");
        var alice = await fixture.ConfirmParticipantAsync("alice@example.com", "Alice", athleteLink: "/athlete/alice-athlete");
        var eve = await fixture.ConfirmParticipantAsync("eve@example.com", "Eve", athleteLink: "/athlete/eve-athlete");

        SubmitChallengeDays(fixture, bob, days: 14, sleep: 2, exercise: 2, nutrition: 2, vices: 2);
        SubmitChallengeDays(fixture, cara, days: 14, sleep: 2, exercise: 2, nutrition: 2, vices: 0);
        SubmitChallengeDays(fixture, dan, days: 14, sleep: 1, exercise: 1, nutrition: 1, vices: 1);
        SubmitChallengeDays(fixture, alice, days: 14, sleep: 0, exercise: 0, nutrition: 0, vices: 0);
        SubmitChallengeDays(fixture, eve, days: 13, sleep: 2, exercise: 2, nutrition: 2, vices: 2);
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            eve,
            15,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-23T08:00:00Z"));

        Assert.Empty(fixture.Service.GetFinalResultEventRows(DateTimeOffset.Parse("2026-06-23T12:00:00Z")));

        var rows = fixture.Service.GetFinalResultEventRows(DateTimeOffset.Parse("2026-06-24T00:01:00Z"));

        Assert.Equal(4, rows.Count);
        Assert.Equal(1, rows.Single(row => row.DisplayName == "Bob").Placement);
        Assert.Equal(2, rows.Single(row => row.DisplayName == "Eve").Placement);
        Assert.Equal(3, rows.Single(row => row.DisplayName == "Cara").Placement);

        var aliceRow = rows.Single(row => row.DisplayName == "Alice");
        Assert.Equal(5, aliceRow.Placement);
        Assert.True(aliceRow.Completed);
        Assert.Equal("alice-athlete", aliceRow.AthleteSlug);
        Assert.Equal(14, aliceRow.CheckedInDays);

        Assert.DoesNotContain(rows, row => row.DisplayName == "Dan");
        Assert.All(rows, row => Assert.Equal(DateTimeKind.Utc, row.OccurredAtUtc.Kind));
    }

    [Fact]
    public async Task DayFourteenResultEventsExcludeParticipantsWhoJoinedAfterOriginalEndDate()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-25T12:00:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "post-end-event@example.com",
            "Post End Pat",
            "UTC",
            "/athlete/post-end-pat"), signup);
        var access = await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), signup.AddMinutes(1));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            18,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-26T08:00:00Z"));

        var publicState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-26T09:00:00Z"));
        Assert.Contains(publicState.Leaderboard, row => row.DisplayName == "Post End Pat");

        var rows = fixture.Service.GetFinalResultEventRows(DateTimeOffset.Parse("2026-06-26T09:00:00Z"));
        Assert.Empty(rows);
    }

    [Fact]
    public async Task DayFourteenResultEventsUseSignupDateForOriginalWindowCutoff()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-21T23:50:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "last-day-signup@example.com",
            "Last Day Lee",
            "UTC",
            null), signup);

        var access = await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            DateTimeOffset.Parse("2026-06-22T00:05:00Z"));

        var day = Assert.Single(access.State.EligibleDays, day => day.ChallengeDay == 14);
        Assert.False(day.CountsForScore);

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            14,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-22T08:00:00Z"));

        var rows = fixture.Service.GetFinalResultEventRows(DateTimeOffset.Parse("2026-06-24T00:01:00Z"));
        var row = Assert.Single(rows);
        Assert.Equal("Last Day Lee", row.DisplayName);
        Assert.Equal(1, row.CheckedInDays);
    }

    [Fact]
    public async Task PublicStateStaysActiveAndDoesNotExposePodiumAfterGraceWindow()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync("final@example.com", "Final Finn");

        var finalGraceDay = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-23T12:00:00Z"));
        Assert.Equal("active", finalGraceDay.Phase);
        Assert.Empty(finalGraceDay.Podium);

        var continuing = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-24T00:01:00Z"));
        Assert.Equal("active", continuing.Phase);
        Assert.Empty(continuing.Podium);
        Assert.True(continuing.SignupOpen);
        Assert.Contains(continuing.Days, day => day.ChallengeDay == 17);
    }

    [Fact]
    public async Task PublicLeaderboardDoesNotExposeCompletionBadgeAfterOriginalDuration()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("badge@example.com", "Badge Bea");
        SubmitChallengeDays(fixture, access, 14, 2, 2, 2, 2);

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-24T00:01:00Z"));
        var row = Assert.Single(state.Leaderboard);

        Assert.DoesNotContain("Completion", row.Badges);
    }

    [Fact]
    public async Task ParticipantsCanContinuePastOriginalDuration()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("continuing@example.com", "Continuing Casey");
        SubmitChallengeDays(fixture, access, 14, 2, 2, 2, 2);

        var day15 = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access, 15, 2, 2, 2, 2, null),
            DateTimeOffset.Parse("2026-06-23T08:15:00Z"));

        Assert.True(day15.Public.Leaderboard.Single().Cells.Single(cell => cell.ChallengeDay == 15).CheckedIn);
    }

    [Fact]
    public async Task StartupRemovesRetiredPledgeDataAndReleasesBlockedParticipants()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("released@example.com", "Released Riley");

        fixture.Db.Run(sqlite =>
        {
            using var legacy = sqlite.CreateCommand();
            legacy.CommandText =
                """
                ALTER TABLE LongevitymaxxingParticipants ADD COLUMN CommitmentAmountUsd TEXT NULL;
                UPDATE LongevitymaxxingParticipants
                SET CommitmentAmountUsd = '25',
                    ChallengeInactiveAtUtc = '2026-06-15T08:05:00Z',
                    ChallengeInactiveReason = 'commitment-payment'
                WHERE AccessToken = @accessToken;
                CREATE TABLE LongevitymaxxingPaymentObligations (Id TEXT PRIMARY KEY);
                INSERT INTO LongevitymaxxingPaymentObligations (Id) VALUES ('legacy');
                INSERT INTO LongevitymaxxingReminderLog (ParticipantId, ChallengeDay, Kind, SentAtUtc)
                SELECT Id, 5, 'commitment-payment', '2026-06-14T08:05:00Z'
                FROM LongevitymaxxingParticipants
                WHERE AccessToken = @accessToken;
                """;
            legacy.Parameters.AddWithValue("@accessToken", access);
            legacy.ExecuteNonQuery();
        });

        _ = new LongevitymaxxingChallengeService(
            fixture.Db,
            fixture.Config,
            fixture.Http,
            fixture.Environment,
            fixture.Email,
            NullLogger<LongevitymaxxingChallengeService>.Instance,
            fixture.Athletes,
            fixture.Statistics);

        fixture.Db.Run(sqlite =>
        {
            using var participant = sqlite.CreateCommand();
            participant.CommandText =
                "SELECT ChallengeInactiveAtUtc, ChallengeInactiveReason FROM LongevitymaxxingParticipants WHERE AccessToken = @accessToken;";
            participant.Parameters.AddWithValue("@accessToken", access);
            using (var reader = participant.ExecuteReader())
            {
                Assert.True(reader.Read());
                Assert.True(reader.IsDBNull(0));
                Assert.True(reader.IsDBNull(1));
            }

            using var tables = sqlite.CreateCommand();
            tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'LongevitymaxxingPaymentObligations';";
            Assert.Equal(0L, (long)tables.ExecuteScalar()!);

            using var reminders = sqlite.CreateCommand();
            reminders.CommandText = "SELECT COUNT(*) FROM LongevitymaxxingReminderLog WHERE Kind = 'commitment-payment';";
            Assert.Equal(0L, (long)reminders.ExecuteScalar()!);

            using var columns = sqlite.CreateCommand();
            columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('LongevitymaxxingParticipants') WHERE name = 'CommitmentAmountUsd';";
            Assert.Equal(0L, (long)columns.ExecuteScalar()!);
        });
    }

    private static string ReadQueryToken(string url, string key)
    {
        var uri = new Uri(url);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        return query[key].ToString();
    }

    private static int CountOccurrences(string value, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static void SubmitChallengeDays(
        TestChallengeFixture fixture,
        string accessToken,
        int days,
        int sleep,
        int exercise,
        int nutrition,
        int vices)
    {
        for (var day = 1; day <= days; day++)
        {
            fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
                accessToken,
                day,
                sleep,
                exercise,
                nutrition,
                vices,
                null), DateTimeOffset.Parse("2026-06-09T08:00:00Z").AddDays(day - 1));
        }
    }


    private static MemoryStream CreatePngStream(int width = 4, int height = 4)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, new Rgba32(21, 184, 166));
        image.SaveAsPng(stream);
        stream.Position = 0;
        return stream;
    }

    private static IFormFile CreatePngFormFile(MemoryStream stream, string formName = "profilePicture", string fileName = "profile.png")
    {
        return new FormFile(stream, 0, stream.Length, formName, fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
    }

    private static EventDataService CreateEventDataService(TestChallengeFixture fixture)
    {
        var appConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EnableEventDispatch"] = "false",
                ["EnableXDevPreviewBrowser"] = "false"
            })
            .Build();
        var customImages = new CustomEventImageService(fixture.Environment, NullLogger<CustomEventImageService>.Instance);
        var services = new EmptyServiceProvider();
        var xClient = new XApiClient(
            new HttpClient(new StaticHttpMessageHandler()),
            fixture.Config,
            fixture.Environment,
            NullLogger<XApiClient>.Instance,
            new XDevPreviewService(NullLogger<XDevPreviewService>.Instance, fixture.Http, appConfig));
        var threadsClient = new ThreadsApiClient(
            new HttpClient(new StaticHttpMessageHandler()),
            fixture.Config,
            NullLogger<ThreadsApiClient>.Instance);
        var facebookClient = new FacebookApiClient(
            new HttpClient(new StaticHttpMessageHandler()),
            fixture.Config,
            NullLogger<FacebookApiClient>.Instance);
        var slackEvents = new SlackEventService(
            new SlackWebhookClient(
                new HttpClient(new StaticHttpMessageHandler()),
                fixture.Config,
                NullLogger<SlackWebhookClient>.Instance),
            NullLogger<SlackEventService>.Instance);

        return new EventDataService(
            fixture.Environment,
            slackEvents,
            new XEventService(xClient, NullLogger<XEventService>.Instance, services, customImages),
            new ThreadsEventService(threadsClient, NullLogger<ThreadsEventService>.Instance, services, customImages),
            new FacebookEventService(facebookClient, NullLogger<FacebookEventService>.Instance, customImages),
            fixture.Db,
            NullLogger<EventDataService>.Instance,
            appConfig);
    }

    private static IReadOnlyList<(string Id, string Text, int VisibleOnWebsite, int SlackProcessed, int XProcessed, int ThreadsProcessed, int FacebookProcessed)> ReadCustomEvents(DatabaseManager db)
    {
        return db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT Id, Text, VisibleOnWebsite, SlackProcessed, XProcessed, ThreadsProcessed, FacebookProcessed
                FROM Events
                WHERE Type = @type
                ORDER BY OccurredAt ASC;
                """;
            cmd.Parameters.AddWithValue("@type", (int)EventType.CustomEvent);
            using var reader = cmd.ExecuteReader();
            var rows = new List<(string Id, string Text, int VisibleOnWebsite, int SlackProcessed, int XProcessed, int ThreadsProcessed, int FacebookProcessed)>();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6)));
            }

            return rows;
        });
    }

    private static IReadOnlyList<(string CallKey, string ReminderKind, string EventId)> ReadCallAnnouncementLogs(DatabaseManager db)
    {
        return db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT CallKey, ReminderKind, EventId
                FROM LongevitymaxxingCallAnnouncementLog
                ORDER BY QueuedAtUtc ASC;
                """;
            using var reader = cmd.ExecuteReader();
            var rows = new List<(string CallKey, string ReminderKind, string EventId)>();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            return rows;
        });
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private sealed class TestChallengeFixture : IDisposable
    {
        private TestChallengeFixture(
            string root,
            DatabaseManager db,
            FakeEmailSender email,
            FakeHttpClientFactory http,
            FakeAthleteSnapshotProvider athletes,
            Config config,
            FakeEnvironment environment,
            SiteStatisticsService statistics,
            LongevitymaxxingChallengeService service)
        {
            ContentRoot = root;
            Db = db;
            Email = email;
            Http = http;
            Athletes = athletes;
            Config = config;
            Environment = environment;
            Statistics = statistics;
            Service = service;
        }

        public string ContentRoot { get; }
        public DatabaseManager Db { get; }
        public FakeEmailSender Email { get; }
        public FakeHttpClientFactory Http { get; }
        public FakeAthleteSnapshotProvider Athletes { get; }
        public Config Config { get; }
        public FakeEnvironment Environment { get; }
        public SiteStatisticsService Statistics { get; }
        public LongevitymaxxingChallengeService Service { get; }

        public static TestChallengeFixture Create(
            byte[]? gravatarResponse = null,
            string? profileJson = null,
            byte[]? profileImageResponse = null,
            string? signupClosesAtUtc = null,
            string? callSelectionClosesAtUtc = null,
            ManualResetEventSlim? gravatarGate = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "lwc-lmx-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var db = new DatabaseManager(dbPath: Path.Combine(root, "challenge.db"));
            var email = new FakeEmailSender();
            var http = new FakeHttpClientFactory(gravatarResponse, profileJson, profileImageResponse, gravatarGate);
            var athletes = new FakeAthleteSnapshotProvider();
            var env = new FakeEnvironment(root);
            var statistics = new SiteStatisticsService(db, NullLogger<SiteStatisticsService>.Instance);
            var config = new Config
            {
                EmailFrom = "hi@example.test",
                SmtpServer = "smtp.example.test",
                SmtpPort = 587,
                SmtpUser = "user",
                SmtpPassword = "password",
                LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
                {
                    StartDate = "2026-06-08",
                    PublicBaseUrl = "https://example.test/ignored-path",
                    SignupClosesAtUtc = signupClosesAtUtc ?? "2026-06-08T00:00:00Z",
                    CallSelectionClosesAtUtc = callSelectionClosesAtUtc,
                    DailyReminderHourLocal = 8,
                    SlackInviteUrl = "https://slack.example.test",
                    VideoCallUrl = "https://meet.example.test"
                }
            };
            var service = new LongevitymaxxingChallengeService(
                db,
                config,
                http,
                env,
                email,
                NullLogger<LongevitymaxxingChallengeService>.Instance,
                athletes,
                statistics);
            return new TestChallengeFixture(root, db, email, http, athletes, config, env, statistics, service);
        }

        public void AddAthleteTieBreak(string slug, int? currentPlacement, int birthYear, int birthMonth, int birthDay)
        {
            Athletes.Snapshot.Add(new JsonObject
            {
                ["AthleteSlug"] = slug,
                ["CurrentPlacement"] = currentPlacement,
                ["DateOfBirth"] = new JsonObject
                {
                    ["Year"] = birthYear,
                    ["Month"] = birthMonth,
                    ["Day"] = birthDay
                }
            });
        }

        public async Task<string> ConfirmParticipantAsync(
            string email,
            string name,
            string? athleteLink = null,
            string timeZoneId = "UTC",
            DateTimeOffset? nowUtc = null)
        {
            var now = nowUtc ?? DateTimeOffset.Parse("2026-06-06T12:00:00Z");
            await Service.SignupAsync(new LongevitymaxxingSignupRequest(email, name, timeZoneId, athleteLink), now);
            var token = ReadQueryToken(Email.Confirmations.Last().Url, "confirm");
            var access = await Service.ConfirmAsync(token, now.AddMinutes(1));
            return access.AccessToken;
        }

        public string InsertConfirmedParticipant(string email, string name)
        {
            var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z").ToString("o");
            var accessToken = $"access-{Guid.NewGuid():N}";
            Db.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO LongevitymaxxingParticipants
                    (Id, Email, DisplayName, TimeZoneId, AthleteSlug, AccessToken, ConfirmationToken, StopToken, ConfirmedAtUtc, StoppedEmailsAtUtc, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (@id, @email, @name, 'UTC', NULL, @access, @confirm, @stop, @confirmed, NULL, @created, @updated);
                    """;
                cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@email", email);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@access", accessToken);
                cmd.Parameters.AddWithValue("@confirm", $"confirm-{Guid.NewGuid():N}");
                cmd.Parameters.AddWithValue("@stop", $"stop-{Guid.NewGuid():N}");
                cmd.Parameters.AddWithValue("@confirmed", now);
                cmd.Parameters.AddWithValue("@created", now);
                cmd.Parameters.AddWithValue("@updated", now);
                cmd.ExecuteNonQuery();
            });

            return accessToken;
        }

        public void Dispose()
        {
            Db.Dispose();
            try { Directory.Delete(ContentRoot, recursive: true); } catch { }
        }
    }

    private sealed class FakeAthleteSnapshotProvider : IAthleteSnapshotProvider
    {
        public JsonArray Snapshot { get; } = [];

        public JsonArray GetAthletesSnapshot() => (JsonArray)Snapshot.DeepClone();
    }

    private sealed class FakeEmailSender : ILongevitymaxxingEmailSender
    {
        public List<(string Email, string Url)> Confirmations { get; } = [];
        public List<(string Email, string Url)> AccessLinks { get; } = [];
        public List<LongevitymaxxingReminderCandidate> DailyReminders { get; } = [];
        public bool ThrowOnDailyReminder { get; set; }

        public Task SendConfirmationAsync(string email, string displayName, string confirmationUrl, CancellationToken ct = default)
        {
            Confirmations.Add((email, confirmationUrl));
            return Task.CompletedTask;
        }

        public Task SendAccessLinkAsync(string email, string displayName, string accessUrl, CancellationToken ct = default)
        {
            AccessLinks.Add((email, accessUrl));
            return Task.CompletedTask;
        }

        public Task SendDailyReminderAsync(LongevitymaxxingReminderCandidate reminder, string checkInUrl, string stopUrl, CancellationToken ct = default)
        {
            DailyReminders.Add(reminder);
            if (ThrowOnDailyReminder)
                throw new InvalidOperationException("Daily reminder delivery failed.");
            return Task.CompletedTask;
        }

        public Task SendCallReminderAsync(LongevitymaxxingCallReminderCandidate reminder, string challengeUrl, string stopUrl, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendChallengeStartAsync(LongevitymaxxingChallengeStartCandidate start, string challengeUrl, string stopUrl, CancellationToken ct = default)
            => Task.CompletedTask;
    }


    private sealed class FakeHttpClientFactory(
        byte[]? gravatarResponse,
        string? profileJson,
        byte[]? profileImageResponse,
        ManualResetEventSlim? gravatarGate) : IHttpClientFactory
    {
        public List<Uri> Requests { get; } = [];
        public List<string> UserAgents { get; } = [];

        public HttpClient CreateClient(string name)
            => new(new FakeHttpMessageHandler(Requests, UserAgents, gravatarResponse, profileJson, profileImageResponse, gravatarGate));
    }

    private sealed class FakeHttpMessageHandler(
        List<Uri> requests,
        List<string> userAgents,
        byte[]? gravatarResponse,
        string? profileJson,
        byte[]? profileImageResponse,
        ManualResetEventSlim? gravatarGate) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => BuildResponse(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(BuildResponse(request));

        private HttpResponseMessage BuildResponse(HttpRequestMessage request)
        {
            requests.Add(request.RequestUri!);
            userAgents.Add(request.Headers.UserAgent.ToString());
            gravatarGate?.Wait();
            if (string.IsNullOrWhiteSpace(request.Headers.UserAgent.ToString()))
                return new HttpResponseMessage(HttpStatusCode.Forbidden);

            if (request.RequestUri!.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                if (profileJson is null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(profileJson, System.Text.Encoding.UTF8, "application/json")
                };
            }

            if (request.RequestUri.AbsoluteUri.Contains("profile-hash", StringComparison.Ordinal) && profileImageResponse is not null)
            {
                var profileImage = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(profileImageResponse)
                };
                profileImage.Content.Headers.ContentType = new("image/png");
                return profileImage;
            }

            if (gravatarResponse is null)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(gravatarResponse)
            };
            response.Content.Headers.ContentType = new("image/png");
            return response;
        }
    }

    private sealed class FakeEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(root);
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }
}
