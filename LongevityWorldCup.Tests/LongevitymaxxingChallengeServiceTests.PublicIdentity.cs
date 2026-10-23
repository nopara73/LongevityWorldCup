using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Theory]
    [InlineData("person@example.com")]
    [InlineData("Person <person+challenge@example.com>")]
    [InlineData("mailto:person@example.com")]
    public async Task SignupRejectsEmailShapedCustomNamesBeforeSavingOrSending(string name)
    {
        using var fixture = TestChallengeFixture.Create();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(
            new("contact@example.com", name, "UTC", null)));
        Assert.Equal("Choose a public username without an email address.", error.Message);
        Assert.Empty(fixture.Email.Confirmations);
        fixture.Db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM LongevitymaxxingParticipants";
            Assert.Equal(0L, command.ExecuteScalar());
        });
    }

    [Fact]
    public async Task LegacyEmailShapedNamesArePrivateAcrossPublicBoardAndDiscussionWithoutChangingIdentityOrScores()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("contact@example.com", "Legacy Name");
        var other = await fixture.ConfirmParticipantAsync("other@example.com", "Other Person");
        var now = DateTimeOffset.Parse("2026-06-22T12:00:00Z");
        var id = fixture.Service.GetParticipantState(access, now).Participant.Id;
        SubmitChallengeDays(fixture, access, 1, 2, 2, 2, 2);
        // Public discussions begin on June 15. Keep the privacy fixture inside that boundary.
        fixture.Service.SubmitCheckIn(new(access, 12, 2, 2, 2, 2, "A public check-in."), now.AddDays(-2));
        var original = fixture.Service.GetPublicState(now).Leaderboard.Single(row => row.ParticipantId == id);
        const string legacyName = "legacy.private@example.com";
        fixture.Db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "UPDATE LongevitymaxxingParticipants SET DisplayName = $name WHERE Id = $id";
            command.Parameters.AddWithValue("$name", legacyName);
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        });
        var parentId = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(access, id, 12, "A public reply.", parentId), now);
        var childId = Guid.NewGuid().ToString("N");
        fixture.Service.SubmitDiscussionReply(new(other, id, 12, "A response.", childId, ReplyToId: parentId), now);

        var state = fixture.Service.GetPublicState(now);
        Assert.Equal("Participant", state.Leaderboard.Single(row => row.ParticipantId == id).DisplayName);
        var masked = state.Leaderboard.Single(row => row.ParticipantId == id);
        Assert.Equal(original.TotalPoints, masked.TotalPoints);
        Assert.Equal(original.Rank, masked.Rank);
        Assert.Equal(original.Cells, masked.Cells);
        Assert.DoesNotContain(legacyName, JsonSerializer.Serialize(state));
        Assert.Contains("Other Person", JsonSerializer.Serialize(state));
        Assert.DoesNotContain(legacyName, JsonSerializer.Serialize(fixture.Service.GetDiscussionThread(id, 12)));
        Assert.DoesNotContain(legacyName, JsonSerializer.Serialize(fixture.Service.GetDiscussionFeed(new(null), now)));
        var page = fixture.Service.GetDiscussionReplyPage(new(null, id, 12, null, null));
        Assert.DoesNotContain(legacyName, JsonSerializer.Serialize(page));
        Assert.Equal("Participant", page.Replies.Single(reply => reply.Id == childId).ReplyTo!.DisplayName);
        Assert.Equal("Participant", fixture.Service.EditDiscussionReply(new(access, parentId, "An edited reply."), now).DisplayName);
        var welcome = state.SystemDiscussionPosts.Single(post => post.ParticipantId == id);
        Assert.Equal("Participant", fixture.Service.GetDiscussionThread(null, 0, welcome.Id).SystemPost!.DisplayName);

        // The owner can still retrieve the saved identity. No production record migration is needed.
        Assert.Equal(legacyName, fixture.Service.GetParticipantState(access, now).Participant.DisplayName);
        fixture.Db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT DisplayName FROM LongevitymaxxingParticipants WHERE Id = $id";
            command.Parameters.AddWithValue("$id", id);
            Assert.Equal(legacyName, command.ExecuteScalar());
        });
    }
}
