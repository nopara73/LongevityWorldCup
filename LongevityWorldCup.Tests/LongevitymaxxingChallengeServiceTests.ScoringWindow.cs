using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeServiceTests
{
    [Theory]
    [InlineData("2026-06-07T12:00:00Z", 0, 1, "2026-06-10T12:00:00Z")]
    [InlineData("2026-06-10T11:59:59.9999999Z", 0, 1, "2026-06-10T12:00:00Z")]
    [InlineData("2026-06-10T12:00:00Z", 1, 1, "2026-06-11T12:00:00Z")]
    [InlineData("2026-06-25T11:59:59.9999999Z", 15, 2, "2026-06-25T12:00:00Z")]
    [InlineData("2026-06-25T12:00:00Z", 16, 3, "2026-06-26T12:00:00Z")]
    public void ScoringWindowClosesOnlyAfterTheFullReportingDayInEveryTimeZone(
        string timestamp, int endDay, int startDay, string nextClose)
    {
        using var fixture = TestChallengeFixture.Create();
        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse(timestamp));
        Assert.Equal(endDay, state.ScoringWindow.EndDay);
        Assert.Equal(startDay, state.ScoringWindow.StartDay);
        Assert.Equal(DateTimeOffset.Parse(nextClose), DateTimeOffset.Parse(state.ScoringWindow.NextClosesAtUtc));
    }

    [Fact]
    public async Task OpenCheckInCannotChangeStandingsEvenAcrossOppositeTimeZones()
    {
        using var fixture = TestChallengeFixture.Create();
        var east = await fixture.ConfirmParticipantAsync("east@example.com", "East", timeZoneId: "Pacific/Kiritimati");
        var west = await fixture.ConfirmParticipantAsync("west@example.com", "West", timeZoneId: "Etc/GMT+12");
        for (var day = 1; day <= 2; day++)
        {
            var submittedAt = DateTimeOffset.Parse("2026-06-10T00:00:00Z").AddDays(day - 1);
            foreach (var access in new[] { east, west })
                fixture.Service.SubmitCheckIn(new(access, day, 2, 2, 2, 2, null), submittedAt);
        }

        var now = DateTimeOffset.Parse("2026-06-11T10:00:00Z");
        var before = fixture.Service.GetPublicState(now);
        var submitted = fixture.Service.SubmitCheckIn(new(east, 3, 2, 2, 2, 2, null), now);
        var after = fixture.Service.GetPublicState(now);

        Assert.Equal(before.Leaderboard.Select(Standing), after.Leaderboard.Select(Standing));
        Assert.Equal(3, submitted.Garden.CheckedInDays);
        Assert.True(after.Leaderboard.Single(row => row.DisplayName == "East").Cells.Single(cell => cell.ChallengeDay == 3).CheckedIn);
        Assert.DoesNotContain(fixture.Service.GetParticipantState(west, now).EligibleDays, day => day.ChallengeDay == 3);

        var beforeClose = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-12T11:59:59Z"));
        Assert.All(beforeClose.Leaderboard, row =>
        {
            Assert.Equal(8, row.TotalPoints);
            Assert.Equal(2, row.CheckedInDays);
            Assert.Equal(2, row.CurrentStreak);
            Assert.Contains("Sleep", row.Badges);
        });
        var closed = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-12T12:00:00Z"));
        Assert.True(closed.Leaderboard[0].TotalPoints > closed.Leaderboard[1].TotalPoints);
        Assert.Equal("East", closed.Leaderboard[0].DisplayName);
        Assert.DoesNotContain("Sleep", closed.Leaderboard[1].Badges);

        // Closing a reporting day must not prevent the existing catch-up flow.
        var caughtUp = fixture.Service.SubmitCheckIn(new(west, 3, 2, 2, 2, 2, null), DateTimeOffset.Parse("2026-06-12T12:01:00Z"));
        Assert.Equal(caughtUp.Public.Leaderboard[0].TotalPoints, caughtUp.Public.Leaderboard[1].TotalPoints);
    }

    [Fact]
    public async Task OpenDatesCannotAgeOutTheOldestScoredDayOrItsCatchUp()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("window@example.com", "Window");
        SubmitChallengeDays(fixture, access, 17, 2, 2, 2, 2);
        var now = DateTimeOffset.Parse("2026-06-25T12:00:00Z");
        var state = fixture.Service.GetParticipantState(access, now);
        var row = Assert.Single(state.Public.Leaderboard);
        Assert.Equal(3, state.Public.ScoringWindow.StartDay);
        Assert.Equal(16, state.Public.ScoringWindow.EndDay);
        Assert.Equal(14, row.CheckedInDays);
        Assert.Equal(14, row.CurrentStreak);
        Assert.Equal(row.Cells.Where(cell => cell.ChallengeDay is >= 3 and <= 16).Sum(cell => cell.Score ?? 0), row.TotalPoints);
        Assert.True(row.Cells.Single(cell => cell.ChallengeDay == 17).CheckedIn);

        fixture.Db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "DELETE FROM LongevitymaxxingCheckIns WHERE ChallengeDay = 3";
            command.ExecuteNonQuery();
        });
        var missing = fixture.Service.GetParticipantState(access, now);
        Assert.Contains(missing.EligibleDays, day => day.ChallengeDay == 3);
        var restored = fixture.Service.SubmitCheckIn(new(access, 3, 2, 2, 2, 2, null), now);
        Assert.Equal(row.TotalPoints, restored.Public.Leaderboard.Single().TotalPoints);
    }

    [Fact]
    public async Task RestingUsesClosedMissedDaysAndOpenCheckInsCannotChangeTheGrouping()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("resting@example.com", "Resting");
        fixture.Service.SubmitCheckIn(new(access, 1, 2, 2, 2, 2, null), DateTimeOffset.Parse("2026-06-09T08:00:00Z"));
        var before = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-13T11:59:59Z"));
        Assert.False(before.Participant.ChallengeInactive);
        Assert.False(before.Public.Leaderboard.Single().ChallengeInactive);
        var closed = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-13T12:00:00Z"));
        Assert.True(closed.Participant.ChallengeInactive);
        Assert.True(closed.Public.Leaderboard.Single().ChallengeInactive);
        var open = fixture.Service.SubmitCheckIn(new(access, 5, 2, 2, 2, 2, null), DateTimeOffset.Parse("2026-06-13T12:01:00Z"));
        Assert.True(open.Public.Leaderboard.Single().ChallengeInactive);
        var catchUp = fixture.Service.SubmitCheckIn(new(access, 2, 2, 2, 2, 2, null), DateTimeOffset.Parse("2026-06-13T12:02:00Z"));
        Assert.False(catchUp.Public.Leaderboard.Single().ChallengeInactive);
    }

    private static object Standing(LongevitymaxxingLeaderboardRow row) => new
    {
        row.ParticipantId, row.TotalPoints, row.CheckedInDays, row.CurrentStreak,
        Badges = string.Join(",", row.Badges), row.LatestCheckInAtUtc, row.ChallengeInactive
    };
}
