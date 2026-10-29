using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Fact]
    public async Task AdvicePrompt_StartsOnPersonalDaySevenAndKeepsOneDisposition()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("story@example.com", "Story Sam");
        for (var day = 1; day <= 6; day++)
        {
            Assert.Null(SubmitAdviceScore(fixture, access, day, 0).PendingAdvicePrompt);
            SubmitAdviceScore(fixture, access, day, 8);
        }
        var failed = SubmitAdviceScore(fixture, access, 7, 4);
        var prompt = Assert.IsType<LongevitymaxxingAdvicePrompt>(failed.PendingAdvicePrompt);
        Assert.Equal(4, prompt.RawScore);
        Assert.Null(prompt.CheckInNote);
        Assert.Empty(failed.Notes);
        var edited = SubmitAdviceScore(fixture, access, 7, 4, "Some context.");
        Assert.Equal(prompt.Id, edited.PendingAdvicePrompt!.Id);
        Assert.Equal("Some context.", edited.PendingAdvicePrompt.CheckInNote);
        fixture.Service.DismissAdviceStory(new(access, prompt.Id, prompt.Version));
        var afterDismiss = SubmitAdviceScore(fixture, access, 7, 0, "Some context.");
        Assert.Null(afterDismiss.PendingAdvicePrompt);
    }

    [Fact]
    public async Task AdvicePrompt_RecognizesPersonalDeclineWithoutTreatingMissingDaysAsZero()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("trend@example.com", "Trend Terry");
        foreach (var day in new[] { 1, 2, 4, 5, 7, 8 }) SubmitAdviceScore(fixture, access, day, 8);
        var decline = SubmitAdviceScore(fixture, access, 10, 5);
        Assert.Equal(8, decline.PendingAdvicePrompt!.BaselineRawScore);
        Assert.Equal(5, decline.PendingAdvicePrompt.RawScore);
    }

    [Fact]
    public async Task AdvicePrompt_CorrectionDismissesPendingAndForgivenSlipDoesNotPrompt()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("correct@example.com", "Correct Casey");
        SeedAdviceBaseline(fixture, access);
        Assert.NotNull(SubmitAdviceScore(fixture, access, 7, 4).PendingAdvicePrompt);
        var corrected = SubmitAdviceScore(fixture, access, 7, 8);
        Assert.Null(corrected.PendingAdvicePrompt);
        var slip = SubmitAdviceScore(fixture, access, 8, 6);
        Assert.Null(slip.PendingAdvicePrompt);
    }

    [Theory]
    [InlineData(new[] { 8, 8, 8, 8, 8, 8 }, 5, true)]
    [InlineData(new[] { 8, 8, 8, 8, 8, 8 }, 6, false)]
    [InlineData(new[] { 8, 8, 8, 8, 8, 8 }, 7, false)]
    [InlineData(new[] { 7, 7, 7, 7, 7, 7 }, 6, true)]
    [InlineData(new[] { 7, 7, 7, 7, 7, 7 }, 7, false)]
    [InlineData(new[] { 4, 4, 4, 4, 4, 4 }, 3, true)]
    [InlineData(new[] { 4, 4, 4, 4, 4, 4 }, 4, false)]
    [InlineData(new[] { 4, 4, 4, 4, 4, 4 }, 0, true)]
    [InlineData(new[] { 8, 0, 8, 0, 8, 0 }, 0, false)]
    // Sample SD is 1.549; population SD would incorrectly flag this three-point drop.
    [InlineData(new[] { 5, 5, 5, 5, 8, 8 }, 3, false)]
    public async Task AdvicePrompt_RequiresTwoSampleStandardDeviationsAndExcludesForgivenSlips(int[] scores, int current, bool prompts)
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("deviation@example.com", "Deviation Devin");
        for (var i = 0; i < scores.Length; i++) SubmitAdviceScore(fixture, access, i + 1, scores[i]);
        var result = SubmitAdviceScore(fixture, access, 7, current);
        Assert.Equal(prompts, result.PendingAdvicePrompt is not null);
        if (prompts) Assert.Equal(scores.Average(), result.PendingAdvicePrompt!.BaselineRawScore);
    }

    [Fact]
    public async Task AdvicePrompt_RequiresSixRecentObservationsAndUsesOnlyTheLatestSeven()
    {
        using var fixture = TestChallengeFixture.Create();
        var sparse = await fixture.ConfirmParticipantAsync("sparse@example.com", "Sparse Sam");
        for (var day = 1; day <= 5; day++) SubmitAdviceScore(fixture, sparse, day, 8);
        Assert.Null(SubmitAdviceScore(fixture, sparse, 12, 0).PendingAdvicePrompt);
        var stale = await fixture.ConfirmParticipantAsync("stale@example.com", "Stale Sam");
        SeedAdviceBaseline(fixture, stale);
        Assert.Null(SubmitAdviceScore(fixture, stale, 21, 0).PendingAdvicePrompt);
        var recent = await fixture.ConfirmParticipantAsync("recent@example.com", "Recent Rae");
        SubmitAdviceScore(fixture, recent, 1, 0);
        for (var day = 2; day <= 8; day++) SubmitAdviceScore(fixture, recent, day, 8);
        Assert.Equal(8, SubmitAdviceScore(fixture, recent, 9, 5).PendingAdvicePrompt!.BaselineRawScore);
    }

    [Fact]
    public async Task AdvicePrompt_LateJoinerWaitsUntilTheirOwnSeventhDay()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("late-story@example.com", "Late Lee",
            nowUtc: DateTimeOffset.Parse("2026-06-20T12:00:00Z"));
        for (var day = 13; day <= 18; day++)
        {
            Assert.Null(SubmitAdviceScore(fixture, access, day, 0).PendingAdvicePrompt);
            SubmitAdviceScore(fixture, access, day, 8);
        }
        Assert.Equal(19, SubmitAdviceScore(fixture, access, 19, 5).PendingAdvicePrompt!.ChallengeDay);
    }

    [Fact]
    public async Task AdviceStory_PersistsSeparatelyPublishesAfterDayClosesAndRetriesAfterWithdrawal()
    {
        using var fixture = TestChallengeFixture.Create();
        fixture.Config.LongevitymaxxingChallenge!.StartDate = "2026-06-20";
        var access = await fixture.ConfirmParticipantAsync("public@example.com", "Public Pat");
        SeedAdviceBaseline(fixture, access);
        const string note = "Travelling today.";
        var saved = SubmitAdviceScore(fixture, access, 7, 4, note);
        var prompt = saved.PendingAdvicePrompt!;
        Assert.Equal(note, prompt.CheckInNote);
        var later = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-30T10:00:00Z"));
        Assert.DoesNotContain(later.EligibleDays, day => day.ChallengeDay == 7);
        Assert.Equal(note, later.PendingAdvicePrompt!.CheckInNote);
        var request = new LongevitymaxxingAdviceStoryRequest(access, prompt.Id, prompt.Version, "Travel derailed my routine. How do you get back on track?");
        var published = fixture.Service.PublishAdviceStory(request, DateTimeOffset.Parse("2026-06-30T10:00:00Z"));
        Assert.Null(published.PendingAdvicePrompt);
        var post = Assert.Single(published.Notes);
        Assert.Equal(note, post.Note);
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
        SeedAdviceBaseline(fixture, owner);
        var state = SubmitAdviceScore(fixture, owner, 7, 0);
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
        var higherId = fixture.Service.GetParticipantState(higher, now).Participant.Id;
        Assert.Contains(higherId, firstState.Notes.Single(note => note.Advice != null).Advice!.BetterAdviceParticipantIds);
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
        Assert.Equal(3, fixture.Service.SubmitDiscussionReply(first, now.AddMinutes(4)).Notes.Single(note => note.Advice != null).ReplyCount);
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
        Assert.Throws<LongevitymaxxingAdviceChangedException>(() => fixture.Service.SubmitDiscussionReply(new(lower, post.ParticipantId, post.ChallengeDay, "Take a walk.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddMinutes(1)));
        Assert.Throws<LongevitymaxxingAdviceChangedException>(() => fixture.Service.SubmitDiscussionReply(new(twin, post.ParticipantId, post.ChallengeDay, "Another option.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddMinutes(2)));
        SubmitAdviceScore(fixture, lower, 3, 8);
        SubmitAdviceScore(fixture, lower, 4, 8);
        Assert.Throws<LongevitymaxxingAdviceChangedException>(() => fixture.Service.SubmitDiscussionReply(new(lower, post.ParticipantId, post.ChallengeDay, "I rank higher now, but not in this request's snapshot.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddMinutes(3)));
        var result = fixture.Service.GetParticipantState(owner, now);
        var advice = result.Notes.Single(note => note.Advice != null).Advice!;
        Assert.Equal(first.ReplyId, advice.ActiveAdviceReplyId);
        Assert.Single(advice.Replies);
        Assert.DoesNotContain(fixture.Service.GetParticipantState(lower, now).Participant.Id, advice.BetterAdviceParticipantIds);
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
        var now = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        SeedAdviceBaseline(fixture, owner);
        var saved = SubmitAdviceScore(fixture, owner, 7, 0);
        var prompt = saved.PendingAdvicePrompt!;
        var published = fixture.Service.PublishAdviceStory(new(owner, prompt.Id, prompt.Version, "What is a manageable restart?"), now);
        var ownerId = published.Participant.Id;
        var firstId = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(first, ownerId, 7, "Start with one walk.", firstId, IsAdvice: true), now.AddMinutes(1));
        Assert.Throws<LongevitymaxxingAdviceChangedException>(() => fixture.Service.SubmitDiscussionReply(new(second, ownerId, 7, "Plan the easiest version.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddMinutes(2)));
        var result = fixture.Service.GetParticipantState(owner, now.AddMinutes(2));
        var advice = Assert.Single(result.Notes).Advice!;
        Assert.Equal(firstId, advice.ActiveAdviceReplyId);
        Assert.Single(advice.Replies.Select(reply => reply.AdviceRank).Distinct());
    }

    [Fact]
    public async Task Advice_AnUnrankedParticipantCanAnswerFirstButOnlyRankedParticipantsCanTakeOver()
    {
        using var fixture = TestChallengeFixture.Create();
        var (_, _, higher, post, now) = await AdviceScenarioAsync(fixture);
        var newcomer = await fixture.ConfirmParticipantAsync("new-adviser@example.com", "New adviser");
        var first = new LongevitymaxxingDiscussionReplyRequest(newcomer, post.ParticipantId, post.ChallengeDay,
            "Start with a short walk.", Guid.NewGuid().ToString("N"), IsAdvice: true);
        var state = fixture.Service.SubmitDiscussionReply(first, now);
        var advice = state.Notes.Single(note => note.Advice != null).Advice!;
        Assert.Null(Assert.Single(advice.Replies).AdviceRank);
        var higherId = fixture.Service.GetParticipantState(higher, now).Participant.Id;
        Assert.Contains(higherId, advice.BetterAdviceParticipantIds);
        var promoted = fixture.Service.SubmitDiscussionReply(new(higher, post.ParticipantId, post.ChallengeDay,
            "Tie the walk to lunch.", Guid.NewGuid().ToString("N"), IsAdvice: true), now.AddMinutes(1));
        Assert.NotEqual(first.ReplyId, promoted.Notes.Single(note => note.Advice != null).Advice!.ActiveAdviceReplyId);
    }

    [Fact]
    public async Task Advice_StoryOnlyThreadSupportsPagingHabitCorrectionsAndStoryEditsWithoutChangingLead()
    {
        using var fixture = TestChallengeFixture.Create();
        var owner = await fixture.ConfirmParticipantAsync("paging-owner@example.com", "Owner");
        var helper = await fixture.ConfirmParticipantAsync("paging-helper@example.com", "Helper");
        var now = DateTimeOffset.Parse("2026-06-15T10:00:00Z");
        SeedAdviceBaseline(fixture, owner);
        var saved = SubmitAdviceScore(fixture, owner, 7, 4);
        var prompt = saved.PendingAdvicePrompt!;
        var originalPublish = new LongevitymaxxingAdviceStoryRequest(owner, prompt.Id, prompt.Version, "My original explanation.");
        var published = fixture.Service.PublishAdviceStory(originalPublish, now);
        var rootId = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(helper, published.Participant.Id, 7, "Take a small next step.", rootId, IsAdvice: true), now.AddMinutes(1));
        for (var i = 0; i < 6; i++)
            fixture.Service.SubmitDiscussionReply(new(owner, published.Participant.Id, 7, $"Follow-up {i}.", Guid.NewGuid().ToString("N"), AdviceRootId: rootId), now.AddMinutes(i + 2));
        var page = fixture.Service.GetDiscussionReplyPage(new(owner, published.Participant.Id, 7, null, null));
        Assert.Equal(7, page.TotalCount);
        Assert.All(page.Replies, reply => Assert.Equal(rootId, reply.AdviceRootId));
        var corrected = fixture.Service.SubmitCheckIn(new(owner, 7, 2, 2, 2, 2, null), now.AddMinutes(10));
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
                WITH RECURSIVE days(day) AS (SELECT 8 UNION ALL SELECT day + 1 FROM days WHERE day < 109)
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
        var now = new DateTimeOffset(DateOnly.Parse(fixture.Config.LongevitymaxxingChallenge!.StartDate)
            .AddDays(9).ToDateTime(new TimeOnly(13, 0)), TimeSpan.Zero);
        SubmitAdviceScore(fixture, lower, 2, 8);
        SubmitAdviceScore(fixture, higher, 2, 8);
        SubmitAdviceScore(fixture, higher, 3, 8);
        SeedAdviceBaseline(fixture, owner);
        var saved = SubmitAdviceScore(fixture, owner, 7, 4);
        var prompt = saved.PendingAdvicePrompt!;
        var published = fixture.Service.PublishAdviceStory(new(owner, prompt.Id, prompt.Version, "Work and travel derailed my routine. Where should I restart?"), now);
        return (owner, lower, higher, Assert.Single(published.Notes), now);
    }

    private static void SeedAdviceBaseline(TestChallengeFixture fixture, string access, int firstDay = 1)
    {
        for (var day = firstDay; day < firstDay + 6; day++) SubmitAdviceScore(fixture, access, day, 8);
    }

    private static LongevitymaxxingParticipantState SubmitAdviceScore(TestChallengeFixture fixture, string access,
        int day, int score, string? note = null)
    {
        var now = new DateTimeOffset(DateOnly.Parse(fixture.Config.LongevitymaxxingChallenge!.StartDate)
            .AddDays(day).ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        return fixture.Service.SubmitCheckIn(new(access, day, Math.Clamp(score, 0, 2), Math.Clamp(score - 2, 0, 2),
            Math.Clamp(score - 4, 0, 2), Math.Clamp(score - 6, 0, 2), note), now);
    }
}
