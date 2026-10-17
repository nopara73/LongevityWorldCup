using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData(1, 3)]
    [InlineData(2, 2)]
    [InlineData(9, 0)]
    public async Task CharacterAllowance_CountsOnlyAthletesStrictlyBehindTheLinkedUltimatePlacement(int? placement, int behind)
    {
        using var fixture = TestChallengeFixture.Create();
        fixture.AddAthleteTieBreak("ranked-athlete", placement, 1980, 1, 1);
        fixture.AddAthleteTieBreak("leader", 1, 1980, 1, 1);
        fixture.AddAthleteTieBreak("tied-athlete", 2, 1980, 1, 1);
        fixture.AddAthleteTieBreak("lower-athlete", 4, 1980, 1, 1);
        fixture.AddAthleteTieBreak("last-athlete", 9, 1980, 1, 1);
        fixture.AddAthleteTieBreak("unranked-athlete", null, 1980, 1, 1);
        var access = await fixture.ConfirmParticipantAsync("ranked@example.com", "Ranked Athlete", "ranked_athlete");
        var unlinked = await fixture.ConfirmParticipantAsync("unlinked@example.com", "Unlinked");
        var state = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-20T08:00:00Z"));
        var allowance = state.Public.Leaderboard.Single(row => row.ParticipantId == state.Participant.Id).CharacterAllowance;
        Assert.Equal(0, allowance.ChallengePoints);
        Assert.Equal(behind, allowance.UltimateLeagueAthletesBehind);
        Assert.Equal(146 + behind, allowance.Limit);
        var unlinkedState = fixture.Service.GetParticipantState(unlinked);
        Assert.Equal(146, unlinkedState.Public.Leaderboard.Single(row => row.ParticipantId == unlinkedState.Participant.Id).CharacterAllowance.Limit);
    }

    [Fact]
    public async Task CharacterAllowance_FullClosedWindowEarns300AndOldPointsExpire()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("active@example.com", "Active");
        var start = DateTimeOffset.Parse("2026-06-08T00:00:00Z");
        for (var day = 1; day <= 28; day++)
            fixture.Service.SubmitCheckIn(new(access, day, 2, 2, 2, 2, null), start.AddDays(day).AddHours(8));
        var now = DateTimeOffset.Parse("2026-07-07T12:01:00Z");
        var full = fixture.Service.GetParticipantState(access, now);
        var fullRow = Assert.Single(full.Public.Leaderboard);
        Assert.Equal(154, fullRow.TotalPoints);
        Assert.Equal(154, fullRow.CharacterAllowance.ChallengePoints);
        Assert.Equal(300, fullRow.CharacterAllowance.Limit);
        var next = fixture.Service.GetParticipantState(access, now.AddDays(1));
        Assert.Equal(289, Assert.Single(next.Public.Leaderboard).CharacterAllowance.Limit);
    }

    [Fact]
    public async Task CharacterAllowance_EnforcesBothBonusesAndPreservesSavedTextAndRetriesAfterRankDrops()
    {
        using var fixture = TestChallengeFixture.Create();
        fixture.AddAthleteTieBreak("ranked-athlete", 1, 1980, 1, 1);
        fixture.AddAthleteTieBreak("second-athlete", 2, 1980, 1, 1);
        fixture.AddAthleteTieBreak("third-athlete", 3, 1980, 1, 1);
        var access = await fixture.ConfirmParticipantAsync("active@example.com", "Ranked Athlete", "ranked-athlete");
        var start = DateTimeOffset.Parse("2026-06-08T00:00:00Z");
        for (var day = 1; day <= 28; day++)
            fixture.Service.SubmitCheckIn(new(access, day, 2, 2, 2, 2, null), start.AddDays(day).AddHours(8));
        var now = DateTimeOffset.Parse("2026-07-07T12:01:00Z");
        var note = new string('n', 302);
        var request = new LongevitymaxxingCheckInRequest(access, 29, 2, 2, 2, 2, note, Guid.NewGuid().ToString("D"));
        var state = await fixture.Service.SubmitCheckInAsync(request, [], now);
        Assert.Equal(302, Assert.Single(state.Public.Leaderboard).CharacterAllowance.Limit);
        var reply = new LongevitymaxxingDiscussionReplyRequest(access, state.Participant.Id, 29, new string('r', 302), Guid.NewGuid().ToString("N"));
        await fixture.Service.SubmitDiscussionReplyWithPhotosAsync(reply, [], now);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(
            reply with { ReplyId = Guid.NewGuid().ToString("N"), Body = new string('r', 303) }, now));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(
            request with { Note = note + "n", SubmissionId = Guid.NewGuid().ToString("D") }, now));

        fixture.Athletes.Snapshot.OfType<JsonObject>().Single(athlete => (string?)athlete["AthleteSlug"] == "ranked-athlete")["CurrentPlacement"] = 4;
        Assert.Equal(300, Assert.Single(fixture.Service.GetParticipantState(access, now).Public.Leaderboard).CharacterAllowance.Limit);
        fixture.Service.SubmitDiscussionReply(reply, now); // Accepted reply replay remains idempotent.
        fixture.Service.SubmitCheckIn(request, now); // Accepted check-in receipt remains replayable.
        fixture.Service.SubmitCheckIn(request with { Nutrition = 1, SubmissionId = Guid.NewGuid().ToString("D") }, now);
        var edited = fixture.Service.EditDiscussionReply(new(access, reply.ReplyId, new string('e', 302)), now);
        Assert.Equal(302, edited.Body.Length);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.EditDiscussionReply(new(access, reply.ReplyId, new string('e', 303)), now));
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitDiscussionReply(reply with { ReplyId = Guid.NewGuid().ToString("N") }, now));
        var saved = fixture.Service.GetParticipantState(access, now).Notes.Single(post => post.ChallengeDay == 29);
        Assert.Equal(note, saved.Note);
        Assert.Equal(new string('e', 302), Assert.Single(saved.Replies).Body);
    }
}
