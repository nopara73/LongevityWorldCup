using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Fact]
    public async Task EqualPointsShareCompetitionRanksRegardlessOfConsistencyOrOpenCheckIns()
    {
        using var fixture = TestChallengeFixture.Create();
        var alice = await fixture.ConfirmParticipantAsync("alice@example.com", "Alice");
        var bob = await fixture.ConfirmParticipantAsync("bob@example.com", "Bob");
        var charlie = await fixture.ConfirmParticipantAsync("charlie@example.com", "Charlie");
        await fixture.ConfirmParticipantAsync("dave@example.com", "Dave");
        await fixture.ConfirmParticipantAsync("eve@example.com", "Eve");
        SubmitChallengeDays(fixture, alice, 2, 2, 2, 2, 2);
        SubmitChallengeDays(fixture, bob, 2, 2, 2, 2, 2);
        SubmitChallengeDays(fixture, charlie, 2, 1, 1, 1, 1);
        var now = DateTimeOffset.Parse("2026-06-12T12:00:00Z");
        fixture.Service.SubmitCheckIn(new(alice, 3, 0, 0, 0, 0, null), now);
        var before = fixture.Service.GetPublicState(now);
        Assert.Equal(new[] { 8, 8, 4, 0, 0 }, before.Leaderboard.Select(row => row.TotalPoints));
        Assert.Equal(new[] { 1, 1, 3, 4, 4 }, before.Leaderboard.Select(row => row.Rank));
        Assert.NotEqual(before.Leaderboard[0].CheckedInDays, before.Leaderboard[1].CheckedInDays);

        var after = fixture.Service.SubmitCheckIn(new(bob, 4, 2, 2, 2, 2, null), now).Public;
        Assert.Equal(before.Leaderboard.Select(row => (row.ParticipantId, row.Rank, row.HasFullMarks)),
            after.Leaderboard.Select(row => (row.ParticipantId, row.Rank, row.HasFullMarks)));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(28)]
    public async Task FullMarksRequireTheWholeWindowAndHonorTheExistingForgivenessRule(int endDay)
    {
        using var fixture = TestChallengeFixture.Create();
        var perfect = await fixture.ConfirmParticipantAsync("perfect@example.com", "Perfect");
        var forgiven = await fixture.ConfirmParticipantAsync("forgiven@example.com", "Forgiven");
        var consecutive = await fixture.ConfirmParticipantAsync("consecutive@example.com", "Consecutive");
        var missing = await fixture.ConfirmParticipantAsync("missing@example.com", "Missing");
        foreach (var access in new[] { perfect, forgiven, consecutive, missing })
            SubmitChallengeDays(fixture, access, endDay, 2, 2, 2, 2);

        var now = DateTimeOffset.Parse("2026-06-08T12:00:00Z").AddDays(endDay + 1);
        fixture.Service.SubmitCheckIn(new(forgiven, endDay, 0, 2, 2, 2, null), now);
        fixture.Service.SubmitCheckIn(new(consecutive, endDay - 1, 0, 2, 2, 2, null), now.AddDays(-1));
        fixture.Service.SubmitCheckIn(new(consecutive, endDay, 0, 2, 2, 2, null), now);
        var missingId = fixture.Service.GetParticipantState(missing, now).Participant.Id;
        fixture.Db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "DELETE FROM LongevitymaxxingCheckIns WHERE ParticipantId = $id AND ChallengeDay = $day";
            command.Parameters.AddWithValue("$id", missingId);
            command.Parameters.AddWithValue("$day", endDay - 13);
            command.ExecuteNonQuery();
        });

        var rows = fixture.Service.GetPublicState(now).Leaderboard;
        Assert.Equal(new[] { "Forgiven", "Perfect" }, rows.Where(row => row.HasFullMarks).Select(row => row.DisplayName).Order());
        Assert.Equal(rows.Single(row => row.DisplayName == "Perfect").TotalPoints,
            rows.Single(row => row.DisplayName == "Forgiven").TotalPoints);
        Assert.All(rows.Where(row => row.HasFullMarks), row => Assert.Equal(1, row.Rank));
        Assert.False(rows.Single(row => row.DisplayName == "Missing").HasFullMarks);
        Assert.False(rows.Single(row => row.DisplayName == "Consecutive").HasFullMarks);
    }

    [Fact]
    public async Task FullMarksCannotComeFromAShorterParticipationPeriodOrAnUnclosedWindow()
    {
        using var fixture = TestChallengeFixture.Create();
        var early = await fixture.ConfirmParticipantAsync("early@example.com", "Early");
        SubmitChallengeDays(fixture, early, 13, 2, 2, 2, 2);
        Assert.False(fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-22T12:00:00Z")).Leaderboard.Single().HasFullMarks);
        var joined = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
        var newcomer = await fixture.ConfirmParticipantAsync("new@example.com", "Newcomer", nowUtc: joined);
        fixture.Service.SubmitCheckIn(new(newcomer, 27, 2, 2, 2, 2, null), joined.AddDays(1));
        fixture.Service.SubmitCheckIn(new(newcomer, 28, 2, 2, 2, 2, null), joined.AddDays(2));
        Assert.False(fixture.Service.GetPublicState(joined.AddDays(3)).Leaderboard.Single(row => row.DisplayName == "Newcomer").HasFullMarks);
    }
}
