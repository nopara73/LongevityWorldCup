using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionContext_SurvivesPagingEditsAndDeletionWithoutDuplicateNotifications(bool welcome)
    {
        using var fixture = TestChallengeFixture.Create();
        var author = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var replier = await fixture.ConfirmParticipantAsync("replier@example.com", "Reply Bea");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var state = fixture.Service.SubmitCheckIn(new(author, 12, 2, 2, 2, 2, "Workout discussion."), now);
        var participantId = state.Participant.Id;
        var post = welcome ? state.Public.SystemDiscussionPosts.First(item => item.ParticipantId == participantId).Id : null;
        var parentId = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(replier, participantId, 12, "Try inverted rows.", parentId, post), now.AddMinutes(1));
        var childId = Guid.NewGuid().ToString("N");
        var request = new LongevitymaxxingDiscussionReplyRequest(author, participantId, 12, "@Reply Bea That helps.", childId, post, parentId);
        fixture.Service.SubmitDiscussionReply(request, now.AddMinutes(2));
        var notifications = CountContextNotifications(fixture, childId);
        fixture.Service.SubmitDiscussionReply(request, now.AddMinutes(3));
        Assert.Equal(notifications, CountContextNotifications(fixture, childId));
        Assert.Equal(1, notifications);

        var page = fixture.Service.GetDiscussionReplyPage(new(author, participantId, 12, null, null, post));
        var child = page.Replies.Single(reply => reply.Id == childId);
        Assert.Equal(parentId, child.ReplyToId);
        Assert.Equal("Reply Bea", child.ReplyTo!.DisplayName);
        Assert.Equal("Try inverted rows.", child.ReplyTo.Body);
        var edited = fixture.Service.EditDiscussionReply(new(author, childId, "@Reply Bea I’ll try it."), now.AddMinutes(4));
        Assert.Equal(parentId, edited.ReplyToId);
        Assert.Equal("Try inverted rows.", edited.ReplyTo!.Body);
        fixture.Service.EditDiscussionReply(new(replier, parentId, "Try slow inverted rows."), now.AddMinutes(5));
        var linked = fixture.Service.GetDiscussionThread(participantId, 12, post);
        Assert.Equal("Try slow inverted rows.", (linked.Note?.Replies ?? linked.SystemPost!.Replies).Single(reply => reply.Id == childId).ReplyTo!.Body);

        fixture.Service.DeleteDiscussionReply(new(replier, parentId), now.AddMinutes(6));
        page = fixture.Service.GetDiscussionReplyPage(new(author, participantId, 12, null, null, post));
        child = Assert.Single(page.Replies);
        Assert.Equal(parentId, child.ReplyToId);
        Assert.Null(child.ReplyTo);
        // An accepted request remains replayable after its source disappears.
        fixture.Service.SubmitDiscussionReply(request with { Body = edited.Body }, now.AddMinutes(7));
        Assert.Equal(notifications, CountContextNotifications(fixture, childId));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(
            request with { ReplyId = Guid.NewGuid().ToString("N") }, now.AddMinutes(8)));
    }

    [Fact]
    public async Task DiscussionContext_RejectsAnotherThreadAndChangedReplayContext()
    {
        using var fixture = TestChallengeFixture.Create();
        var author = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var state = fixture.Service.SubmitCheckIn(new(author, 12, 2, 2, 2, 2, "First discussion."), now);
        fixture.Service.SubmitCheckIn(new(author, 11, 2, 2, 2, 2, "Other discussion."), now);
        var parentId = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(author, state.Participant.Id, 12, "Original comment.", parentId), now);
        var invalid = new LongevitymaxxingDiscussionReplyRequest(author, state.Participant.Id, 11, "Wrong thread.", Guid.NewGuid().ToString("N"), ReplyToId: parentId);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(invalid, now));
        var request = invalid with { ChallengeDay = 12, Body = "Correct thread." };
        fixture.Service.SubmitDiscussionReply(request, now);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(request with { ReplyToId = null }, now));
    }

    [Fact]
    public async Task DiscussionLimits_RejectExcessTextWithoutSavingOrNotifying()
    {
        using var fixture = TestChallengeFixture.Create();
        var author = await fixture.ConfirmParticipantAsync("author@example.com", "Author Ana");
        var replier = await fixture.ConfirmParticipantAsync("reply@example.com", "Reply Bea");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        var state = fixture.Service.SubmitCheckIn(new(author, 12, 2, 2, 2, 2, "Original text."), now);
        var replyId = Guid.NewGuid().ToString("N");
        var request = new LongevitymaxxingDiscussionReplyRequest(replier, state.Participant.Id, 12, new string('x', 241), replyId);
        Assert.Contains("240", Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(request, now)).Message);
        Assert.Equal(0, CountContextNotifications(fixture, replyId));
        fixture.Service.SubmitDiscussionReply(request with { Body = new string('x', 240) }, now);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.EditDiscussionReply(new(replier, replyId, new string('y', 241)), now));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(new(author, 12, 2, 2, 2, 2, new string('z', 241)), now));
        var saved = fixture.Service.GetParticipantState(author, now).Notes.Single(note => note.ParticipantId == state.Participant.Id && note.ChallengeDay == 12);
        Assert.Equal("Original text.", saved.Note);
        Assert.Equal(new string('x', 240), Assert.Single(saved.Replies).Body);
        Assert.Equal(1, CountContextNotifications(fixture, replyId));
    }

    private static long CountContextNotifications(TestChallengeFixture fixture, string replyId) => fixture.Db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "SELECT (SELECT COUNT(*) FROM LongevitymaxxingDiscussionNotifications WHERE SourceReplyId = @id) + (SELECT COUNT(*) FROM LongevitymaxxingDiscussionSystemPostNotifications WHERE SourceReplyId = @id);";
        cmd.Parameters.AddWithValue("@id", replyId);
        return (long)cmd.ExecuteScalar()!;
    });
}
