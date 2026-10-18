using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Fact]
    public async Task AdvicePrompt_UsesRawFailureExcludesPracticeAndKeepsOneDisposition()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("story@example.com", "Story Sam");
        var practice = fixture.Service.SubmitCheckIn(new(access, 1, 0, 0, 0, 0, null), DateTimeOffset.Parse("2026-06-09T10:00:00Z"));
        Assert.Null(practice.PendingAdvicePrompt);
        var failed = fixture.Service.SubmitCheckIn(new(access, 2, 2, 2, 0, 0, null, Guid.NewGuid().ToString("D")), DateTimeOffset.Parse("2026-06-10T10:00:00Z"));
        var prompt = Assert.IsType<LongevitymaxxingAdvicePrompt>(failed.PendingAdvicePrompt);
        Assert.Equal(4, prompt.RawScore);
        Assert.Empty(failed.Notes);
        var edited = fixture.Service.SubmitCheckIn(new(access, 2, 2, 2, 0, 0, "Some context."), DateTimeOffset.Parse("2026-06-10T10:01:00Z"));
        Assert.Equal(prompt.Id, edited.PendingAdvicePrompt!.Id);
        fixture.Service.DismissAdviceStory(new(access, prompt.Id, prompt.Version));
        var afterDismiss = fixture.Service.SubmitCheckIn(new(access, 2, 0, 0, 0, 0, "Some context."), DateTimeOffset.Parse("2026-06-10T10:02:00Z"));
        Assert.Null(afterDismiss.PendingAdvicePrompt);
    }

    [Fact]
    public async Task AdvicePrompt_RecognizesPersonalDeclineWithoutTreatingMissingDaysAsZero()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("trend@example.com", "Trend Terry");
        for (var day = 2; day <= 4; day++)
            fixture.Service.SubmitCheckIn(new(access, day, 2, 2, 2, 2, null), DateTimeOffset.Parse("2026-06-08T10:00:00Z").AddDays(day));
        var decline = fixture.Service.SubmitCheckIn(new(access, 6, 2, 1, 1, 1, null), DateTimeOffset.Parse("2026-06-14T10:00:00Z"));
        Assert.Equal(8, decline.PendingAdvicePrompt!.BaselineRawScore);
        Assert.Equal(5, decline.PendingAdvicePrompt.RawScore);
    }

    [Fact]
    public async Task AdvicePrompt_CorrectionDismissesPendingAndForgivenSlipDoesNotPrompt()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("correct@example.com", "Correct Casey");
        var now = DateTimeOffset.Parse("2026-06-10T10:00:00Z");
        fixture.Service.SubmitCheckIn(new(access, 2, 2, 2, 0, 0, null), now);
        var corrected = fixture.Service.SubmitCheckIn(new(access, 2, 2, 2, 2, 2, null), now.AddMinutes(1));
        Assert.Null(corrected.PendingAdvicePrompt);
        var slip = fixture.Service.SubmitCheckIn(new(access, 3, 2, 2, 2, 0, null), now.AddDays(1));
        Assert.Null(slip.PendingAdvicePrompt);
    }

    [Fact]
    public async Task AdviceStory_PersistsSeparatelyPublishesAfterDayClosesAndRetriesAfterWithdrawal()
    {
        using var fixture = TestChallengeFixture.Create();
        fixture.Config.LongevitymaxxingChallenge!.StartDate = "2026-06-20";
        var access = await fixture.ConfirmParticipantAsync("public@example.com", "Public Pat");
        var saved = fixture.Service.SubmitCheckIn(new(access, 2, 2, 2, 0, 0, null), DateTimeOffset.Parse("2026-06-22T10:00:00Z"));
        var prompt = saved.PendingAdvicePrompt!;
        var request = new LongevitymaxxingAdviceStoryRequest(access, prompt.Id, prompt.Version, "Travel derailed my routine. How do you get back on track?");
        var published = fixture.Service.PublishAdviceStory(request, DateTimeOffset.Parse("2026-06-25T10:00:00Z"));
        Assert.Null(published.PendingAdvicePrompt);
        var post = Assert.Single(published.Notes);
        Assert.Null(post.Note);
        Assert.Equal(request.Story, post.Advice!.Story);
        Assert.Equal(request.Story, Assert.Single(published.Public.Notes).Advice!.Story);
        Assert.Equal(post.Advice.Id, Assert.Single(fixture.Service.PublishAdviceStory(request).Notes).Advice!.Id);
        var withdrawn = fixture.Service.WithdrawAdviceStory(new(access, prompt.Id, post.Advice.Version));
        Assert.True(Assert.Single(withdrawn.Notes).Advice!.Withdrawn);
        var retried = fixture.Service.PublishAdviceStory(request);
        Assert.True(Assert.Single(retried.Notes).Advice!.Withdrawn);
        Assert.Equal("", Assert.Single(retried.Notes).Advice!.Story);
    }

    [Fact]
    public async Task AdviceStory_RequiresOwnershipExplicitStoryAndOfferedCapacity()
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("owner@example.com", "Owner");
        var other = await fixture.ConfirmParticipantAsync("other@example.com", "Other");
        var state = fixture.Service.SubmitCheckIn(new(owner, 2, 0, 0, 0, 0, null), DateTimeOffset.Parse("2026-06-10T10:00:00Z"));
        var prompt = state.PendingAdvicePrompt!;
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Service.PublishAdviceStory(new(other, prompt.Id, prompt.Version, "My story.")));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.PublishAdviceStory(new(owner, prompt.Id, prompt.Version, "  ")));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.PublishAdviceStory(new(owner, prompt.Id, prompt.Version, new string('x', prompt.CharacterLimit + 1))));
        Assert.NotNull(fixture.Service.GetParticipantState(owner).PendingAdvicePrompt);
    }

    [Fact]
    public async Task Advice_HigherRankPromotesAndArchivesExistingConversationWhileAcceptedReplayStillWorks()
    {
        using var fixture = TestChallengeFixture.Create();
        var (owner, lower, higher, post, now) = await AdviceScenarioAsync(fixture);
        var first = new LongevitymaxxingDiscussionReplyRequest(lower, post.ParticipantId, post.ChallengeDay, "Pick one small habit to restart.", Guid.NewGuid().ToString("N"), IsAdvice: true);
        var firstState = fixture.Service.SubmitDiscussionReply(first, now);
        var firstRoot = firstState.Notes.Single(note => note.Advice != null).Advice!.ActiveAdviceReplyId!;
        var comment = new LongevitymaxxingDiscussionReplyRequest(owner, post.ParticipantId, post.ChallengeDay, "I can do a short walk after lunch.", Guid.NewGuid().ToString("N"), ReplyToId: firstRoot, AdviceRootId: firstRoot);
        fixture.Service.SubmitDiscussionReply(comment, now.AddMinutes(1));
        var promoted = fixture.Service.SubmitDiscussionReply(new(higher, post.ParticipantId, post.ChallengeDay, "Tie that walk to a reliable event in your day.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddMinutes(2));
        var advice = promoted.Notes.Single(note => note.Advice != null).Advice!;
        Assert.NotEqual(firstRoot, advice.ActiveAdviceReplyId);
        Assert.True(advice.Replies.Single(reply => reply.Id == firstRoot).IsArchived);
        Assert.True(advice.Replies.Single(reply => reply.Id == comment.ReplyId).IsArchived);
        Assert.ThrowsAny<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(comment with { ReplyId = Guid.NewGuid().ToString("N") }, now.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.EditDiscussionReply(new(owner, comment.ReplyId, "An edit to the old branch."), now.AddMinutes(3)));
        var replay = fixture.Service.SubmitDiscussionReply(comment, now.AddMinutes(4));
        Assert.Equal(3, replay.Notes.Single(note => note.Advice != null).ReplyCount);
        Assert.Equal(advice.ActiveAdviceReplyId, replay.Notes.Single(note => note.Advice != null).Advice!.ActiveAdviceReplyId);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(comment with { IsAdvice = true }, now.AddMinutes(5)));
    }

    [Fact]
    public async Task Advice_TiesAndFutureRankingChangesDoNotMoveTheLeadAndRootsCannotBeForged()
    {
        using var fixture = TestChallengeFixture.Create();
        var (owner, lower, higher, post, now) = await AdviceScenarioAsync(fixture);
        var twin = await fixture.ConfirmParticipantAsync("twin@example.com", "Twin");
        // A participant joining after publication is unranked for this case.
        var first = new LongevitymaxxingDiscussionReplyRequest(higher, post.ParticipantId, post.ChallengeDay, "Start small.", Guid.NewGuid().ToString("N"), IsAdvice: true);
        fixture.Service.SubmitDiscussionReply(first, now);
        var result = fixture.Service.SubmitDiscussionReply(new(lower, post.ParticipantId, post.ChallengeDay, "Take a walk.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddMinutes(1));
        result = fixture.Service.SubmitDiscussionReply(new(twin, post.ParticipantId, post.ChallengeDay, "Another option.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddMinutes(2));
        var advice = result.Notes.Single(note => note.Advice != null).Advice!;
        Assert.Equal(first.ReplyId, advice.ActiveAdviceReplyId);
        Assert.Null(advice.Replies.Single(reply => reply.DisplayName == "Twin").AdviceRank);
        Assert.ThrowsAny<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(new(owner, post.ParticipantId, post.ChallengeDay, "Unscoped reply.", Guid.NewGuid().ToString("N")), now.AddMinutes(3)));
        Assert.ThrowsAny<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(new(owner, post.ParticipantId, post.ChallengeDay, "Forged root.", Guid.NewGuid().ToString("N"), AdviceRootId: Guid.NewGuid().ToString("N")), now.AddMinutes(3)));
        var nextDay = fixture.Service.GetParticipantState(owner, now.AddDays(20));
        Assert.Equal(first.ReplyId, nextDay.Notes.Single(note => note.Advice != null).Advice!.ActiveAdviceReplyId);
    }

    [Fact]
    public async Task Advice_DeletionKeepsAnchorWithoutReopeningHistoryAndWithdrawalFreezesReplies()
    {
        using var fixture = TestChallengeFixture.Create();
        var (owner, lower, higher, post, now) = await AdviceScenarioAsync(fixture);
        var id = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(lower, post.ParticipantId, post.ChallengeDay, "Restart with a walk.", id, IsAdvice: true), now);
        fixture.Service.SubmitDiscussionReply(new(owner, post.ParticipantId, post.ChallengeDay, "I'll try it.", Guid.NewGuid().ToString("N"), AdviceRootId: id), now.AddMinutes(1));
        var deleted = fixture.Service.DeleteDiscussionReply(new(lower, id), now.AddMinutes(2));
        var advice = deleted.Notes.Single(note => note.Advice != null).Advice!;
        Assert.Null(advice.ActiveAdviceReplyId);
        Assert.True(advice.Replies.Single(reply => reply.Id == id).IsDeleted);
        Assert.DoesNotContain("Restart with a walk", advice.Replies.Single(reply => reply.Id == id).Body);
        var reopened = fixture.Service.SubmitDiscussionReply(new(higher, post.ParticipantId, post.ChallengeDay, "A fresh suggestion.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddMinutes(3));
        var current = reopened.Notes.Single(note => note.Advice != null).Advice!;
        fixture.Service.WithdrawAdviceStory(new(owner, current.Id, current.Version), now.AddMinutes(4));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(new(owner, post.ParticipantId, post.ChallengeDay, "Too late.", Guid.NewGuid().ToString("N"), AdviceRootId: current.ActiveAdviceReplyId), now.AddMinutes(5)));
    }

    [Fact]
    public async Task Advice_ActualEqualRanksKeepTheFirstAdviceInCharge()
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("ties-owner@example.com", "Owner");
        var first = await fixture.ConfirmParticipantAsync("ties-first@example.com", "First");
        var second = await fixture.ConfirmParticipantAsync("ties-second@example.com", "Second");
        var now = DateTimeOffset.Parse("2026-06-10T10:00:00Z");
        var saved = fixture.Service.SubmitCheckIn(new(owner, 2, 0, 0, 0, 0, null), now);
        var prompt = saved.PendingAdvicePrompt!;
        var published = fixture.Service.PublishAdviceStory(new(owner, prompt.Id, prompt.Version, "What is a manageable restart?"), now);
        var ownerId = published.Participant.Id;
        var firstId = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(first, ownerId, 2, "Start with one walk.", firstId, IsAdvice: true), now.AddMinutes(1));
        var result = fixture.Service.SubmitDiscussionReply(new(second, ownerId, 2, "Plan the easiest version.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddMinutes(2));
        var advice = Assert.Single(result.Notes).Advice!;
        Assert.Equal(firstId, advice.ActiveAdviceReplyId);
        Assert.Single(advice.Replies.Select(reply => reply.AdviceRank).Distinct());
    }

    [Fact]
    public async Task Advice_StoryOnlyThreadSupportsPagingHabitCorrectionsAndStoryEditsWithoutChangingLead()
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("paging-owner@example.com", "Owner");
        var helper = await fixture.ConfirmParticipantAsync("paging-helper@example.com", "Helper");
        var now = DateTimeOffset.Parse("2026-06-12T10:00:00Z");
        var saved = fixture.Service.SubmitCheckIn(new(owner, 4, 0, 0, 2, 2, null), now);
        var prompt = saved.PendingAdvicePrompt!;
        var originalPublish = new LongevitymaxxingAdviceStoryRequest(owner, prompt.Id, prompt.Version, "My original explanation.");
        var published = fixture.Service.PublishAdviceStory(originalPublish, now);
        var rootId = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(helper, published.Participant.Id, 4, "Take a small next step.", rootId, IsAdvice: true), now.AddMinutes(1));
        for (var i = 0; i < 6; i++)
            fixture.Service.SubmitDiscussionReply(new(owner, published.Participant.Id, 4, $"Follow-up {i}.", Guid.NewGuid().ToString("N"), AdviceRootId: rootId), now.AddMinutes(i + 2));
        var page = fixture.Service.GetDiscussionReplyPage(new(owner, published.Participant.Id, 4, null, null));
        Assert.Equal(7, page.TotalCount);
        Assert.All(page.Replies, reply => Assert.Equal(rootId, reply.AdviceRootId));
        var corrected = fixture.Service.SubmitCheckIn(new(owner, 4, 2, 2, 2, 2, null), now.AddMinutes(10));
        var advice = Assert.Single(corrected.Notes).Advice!;
        Assert.Equal("My original explanation.", advice.Story);
        var edited = fixture.Service.EditAdviceStory(new(owner, advice.Id, advice.Version, "More context after trying it."), now.AddMinutes(11));
        Assert.Equal(rootId, edited.UpdatedDiscussionThread!.Advice!.ActiveAdviceReplyId);
        var replay = fixture.Service.PublishAdviceStory(originalPublish, now.AddMinutes(12));
        Assert.Equal("More context after trying it.", replay.UpdatedDiscussionThread!.Advice!.Story);
    }

    [Fact]
    public async Task Advice_WithdrawalReturnsTheExactThreadEvenWhenTheStateWindowOmitsIt()
    {
        using var fixture = TestChallengeFixture.Create();
        var (owner, _, _, post, now) = await AdviceScenarioAsync(fixture);
        fixture.Db.Run(sqlite =>
        {
            using var insert = sqlite.CreateCommand();
            insert.CommandText = """
                WITH RECURSIVE days(day) AS (SELECT 5 UNION ALL SELECT day + 1 FROM days WHERE day < 106)
                INSERT INTO LongevitymaxxingCheckIns
                (ParticipantId, ChallengeDay, ChallengeDate, Sleep, Exercise, Nutrition, Vices, Note, DiscussionUpdatedAtUtc, CheckedInAtUtc, UpdatedAtUtc)
                SELECT @participant, day, '2026-06-14', 2, 2, 2, 2, 'Newer discussion', @time, @time, @time FROM days;
                """;
            insert.Parameters.AddWithValue("@participant", post.ParticipantId);
            insert.Parameters.AddWithValue("@time", now.AddHours(1).ToString("o"));
            insert.ExecuteNonQuery();
        });
        var state = fixture.Service.WithdrawAdviceStory(new(owner, post.Advice!.Id, post.Advice.Version), now.AddHours(2));
        Assert.DoesNotContain(state.Notes, note => note.ChallengeDay == post.ChallengeDay);
        Assert.True(state.UpdatedDiscussionThread!.Advice!.Withdrawn);
        Assert.Equal("", state.UpdatedDiscussionThread.Advice.Story);
    }

    private static async Task<(string Owner, string Lower, string Higher, LongevitymaxxingParticipantNote Post, DateTimeOffset Now)> AdviceScenarioAsync(TestChallengeFixture fixture)
    {
        var owner = await fixture.ConfirmParticipantAsync("ask@example.com", "Asking Alex");
        var lower = await fixture.ConfirmParticipantAsync("lower@example.com", "Helper Bea");
        var higher = await fixture.ConfirmParticipantAsync("higher@example.com", "Helper Ari");
        var now = DateTimeOffset.Parse("2026-06-14T13:00:00Z");
        fixture.Service.SubmitCheckIn(new(lower, 2, 2, 2, 2, 2, null), DateTimeOffset.Parse("2026-06-10T10:00:00Z"));
        fixture.Service.SubmitCheckIn(new(higher, 2, 2, 2, 2, 2, null), DateTimeOffset.Parse("2026-06-10T10:00:00Z"));
        fixture.Service.SubmitCheckIn(new(higher, 3, 2, 2, 2, 2, null), DateTimeOffset.Parse("2026-06-11T10:00:00Z"));
        var saved = fixture.Service.SubmitCheckIn(new(owner, 4, 2, 2, 0, 0, null), DateTimeOffset.Parse("2026-06-12T10:00:00Z"));
        var prompt = saved.PendingAdvicePrompt!;
        var published = fixture.Service.PublishAdviceStory(new(owner, prompt.Id, prompt.Version, "Work and travel derailed my routine. Where should I restart?"), now);
        return (owner, lower, higher, Assert.Single(published.Notes), now);
    }
}
