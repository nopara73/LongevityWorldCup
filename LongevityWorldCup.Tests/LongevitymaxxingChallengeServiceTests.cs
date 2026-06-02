using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LongevitymaxxingChallengeServiceTests
{
    [Fact]
    public async Task SignupRequiresConfirmationBeforePublicRosterAndSubscribesNewsletterOnConfirm()
    {
        using var fixture = TestChallengeFixture.Create();
        var now = DateTimeOffset.Parse("2026-06-07T12:00:00Z");

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "athlete@example.com",
            "Momentum Alice",
            "UTC",
            "/athlete/momentum-alice",
            [new("kickoff", "kickoff-a")]), now);

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
    public async Task LeaderboardRanksConsistencyBeforeScoreAndKeepsNotesParticipantOnly()
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

        var publicState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-10T09:00:00Z"));

        Assert.Equal("Bob", publicState.Leaderboard[0].DisplayName);
        Assert.Equal(2, publicState.Leaderboard[0].CheckedInDays);
        Assert.Equal("Alice", publicState.Leaderboard[1].DisplayName);
        Assert.DoesNotContain(publicState.Leaderboard[1].Badges, badge => badge.Contains("perfect start", StringComparison.OrdinalIgnoreCase));

        var participantState = fixture.Service.GetParticipantState(alice, DateTimeOffset.Parse("2026-06-10T09:00:00Z"));
        Assert.Contains(participantState.Notes, note => note.Note == "perfect start");
        Assert.Contains(participantState.Notes, note => note.Note == "still returned");
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
        Assert.Equal("Alice", scoredState.Leaderboard[1].DisplayName);
        Assert.Equal(0, scoredState.Leaderboard[1].TotalPoints);
    }

    [Fact]
    public async Task DailyReminderCandidatesSkipCompletedTargetDayAndStoppedEmails()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("daily@example.com", "Daily Dana");
        var reminderTime = DateTimeOffset.Parse("2026-06-09T08:05:00Z");

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
    public async Task CallSelectionUsesTopVotedSlotAndVideoLinkIsParticipantOnly()
    {
        using var fixture = TestChallengeFixture.Create();
        var one = await fixture.ConfirmParticipantAsync(
            "one@example.com",
            "One",
            [new("kickoff", "kickoff-b")]);
        await fixture.ConfirmParticipantAsync(
            "two@example.com",
            "Two",
            [new("kickoff", "kickoff-b")]);
        await fixture.ConfirmParticipantAsync(
            "three@example.com",
            "Three",
            [new("kickoff", "kickoff-a")]);

        var beforeClose = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-07T23:59:00Z"));
        Assert.Null(beforeClose.Calls.Single(c => c.Key == "kickoff").SelectedSlot);
        Assert.Equal("2026-06-08T00:00:00.0000000+00:00", beforeClose.SignupClosesAtUtc);

        var participantBeforeClose = fixture.Service.GetParticipantState(one, DateTimeOffset.Parse("2026-06-07T23:59:30Z"));
        var pendingKickoff = participantBeforeClose.Calls.Single(c => c.Key == "kickoff");
        Assert.Null(pendingKickoff.SelectedSlot);
        Assert.Equal("https://meet.example.test", pendingKickoff.VideoCallUrl);

        fixture.Service.TrySelectCallSlots(DateTimeOffset.Parse("2026-06-08T00:01:00Z"));

        var publicState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-08T00:02:00Z"));
        Assert.Equal("kickoff-b", publicState.Calls.Single(c => c.Key == "kickoff").SelectedSlot?.Id);

        var participantState = fixture.Service.GetParticipantState(one, DateTimeOffset.Parse("2026-06-08T00:03:00Z"));
        Assert.Equal("https://meet.example.test", participantState.Calls.Single(c => c.Key == "kickoff").VideoCallUrl);
        Assert.Contains(participantState.CallAvailability, selection => selection is { CallKey: "kickoff", SlotId: "kickoff-b" });
    }

    [Fact]
    public async Task ChallengeStartCandidatesWaitUntilSignupCloseAndSendOnce()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "one@example.com",
            "One",
            [new("kickoff", "kickoff-b"), new("kickoff", "kickoff-a"), new("midpoint", "midpoint-a")]);
        await fixture.ConfirmParticipantAsync(
            "two@example.com",
            "Two",
            [new("kickoff", "kickoff-b"), new("finale", "finale-a")]);

        Assert.Empty(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-07T23:59:00Z")));

        var candidates = fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-08T00:01:00Z"));

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(3, candidate.Calls.Count);
            Assert.All(candidate.Calls, call =>
            {
                Assert.NotNull(call.SelectedSlot);
                Assert.Equal("https://meet.example.test", call.VideoCallUrl);
            });
        });
        Assert.Equal("kickoff-b", candidates[0].Calls.Single(call => call.Key == "kickoff").SelectedSlot?.Id);

        fixture.Service.MarkChallengeStartSent(candidates[0].ParticipantId, DateTimeOffset.Parse("2026-06-08T00:02:00Z"));

        var remaining = fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-08T00:03:00Z"));
        var single = Assert.Single(remaining);
        Assert.NotEqual(candidates[0].ParticipantId, single.ParticipantId);

        fixture.Service.MarkChallengeStartSent(single.ParticipantId, DateTimeOffset.Parse("2026-06-08T00:04:00Z"));
        Assert.Empty(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-08T00:05:00Z")));
    }

    [Fact]
    public async Task SignupAfterChallengeStartIsRejected()
    {
        using var fixture = TestChallengeFixture.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("late@example.com", "Late Lee", "UTC", null, []),
            DateTimeOffset.Parse("2026-06-08T00:01:00Z")));
    }

    [Fact]
    public async Task FinalPodiumWaitsUntilFinalCheckInGraceWindowCloses()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync("final@example.com", "Final Finn");

        var finalGraceDay = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-23T12:00:00Z"));
        Assert.Equal("active", finalGraceDay.Phase);
        Assert.Empty(finalGraceDay.Podium);

        var completed = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-24T00:01:00Z"));
        Assert.Equal("completed", completed.Phase);
        Assert.Single(completed.Podium);
    }

    private static string ReadQueryToken(string url, string key)
    {
        var uri = new Uri(url);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        return query[key].ToString();
    }

    private sealed class TestChallengeFixture : IDisposable
    {
        private TestChallengeFixture(string root, DatabaseManager db, FakeEmailSender email, LongevitymaxxingChallengeService service)
        {
            ContentRoot = root;
            Db = db;
            Email = email;
            Service = service;
        }

        public string ContentRoot { get; }
        public DatabaseManager Db { get; }
        public FakeEmailSender Email { get; }
        public LongevitymaxxingChallengeService Service { get; }

        public static TestChallengeFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "lwc-lmx-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var db = new DatabaseManager(dbPath: Path.Combine(root, "challenge.db"));
            var email = new FakeEmailSender();
            var env = new FakeEnvironment(root);
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
                    SignupClosesAtUtc = "2026-06-08T00:00:00Z",
                    DailyReminderHourLocal = 8,
                    SlackInviteUrl = "https://slack.example.test",
                    VideoCallUrl = "https://meet.example.test",
                    Calls =
                    [
                        new()
                        {
                            Key = "kickoff",
                            Label = "Kickoff",
                            CandidateSlots =
                            [
                                new() { Id = "kickoff-a", StartsAtUtc = "2026-06-07T18:00:00Z" },
                                new() { Id = "kickoff-b", StartsAtUtc = "2026-06-08T02:00:00Z" }
                            ]
                        },
                        new()
                        {
                            Key = "midpoint",
                            Label = "Midpoint",
                            CandidateSlots =
                            [
                                new() { Id = "midpoint-a", StartsAtUtc = "2026-06-14T18:00:00Z" }
                            ]
                        },
                        new()
                        {
                            Key = "finale",
                            Label = "Finale",
                            CandidateSlots =
                            [
                                new() { Id = "finale-a", StartsAtUtc = "2026-06-22T18:00:00Z" }
                            ]
                        }
                    ]
                }
            };
            var service = new LongevitymaxxingChallengeService(
                db,
                config,
                env,
                email,
                NullLogger<LongevitymaxxingChallengeService>.Instance);
            return new TestChallengeFixture(root, db, email, service);
        }

        public async Task<string> ConfirmParticipantAsync(
            string email,
            string name,
            IReadOnlyList<LongevitymaxxingCallAvailabilitySelection>? callAvailability = null)
        {
            var now = DateTimeOffset.Parse("2026-06-07T12:00:00Z");
            await Service.SignupAsync(new LongevitymaxxingSignupRequest(email, name, "UTC", null, callAvailability ?? []), now);
            var token = ReadQueryToken(Email.Confirmations.Last().Url, "confirm");
            var access = await Service.ConfirmAsync(token, now.AddMinutes(1));
            return access.AccessToken;
        }

        public void Dispose()
        {
            Db.Dispose();
            try { Directory.Delete(ContentRoot, recursive: true); } catch { }
        }
    }

    private sealed class FakeEmailSender : ILongevitymaxxingEmailSender
    {
        public List<(string Email, string Url)> Confirmations { get; } = [];
        public List<(string Email, string Url)> AccessLinks { get; } = [];

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
            => Task.CompletedTask;

        public Task SendCallReminderAsync(LongevitymaxxingCallReminderCandidate reminder, string challengeUrl, string stopUrl, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendChallengeStartAsync(LongevitymaxxingChallengeStartCandidate start, string challengeUrl, string stopUrl, CancellationToken ct = default)
            => Task.CompletedTask;
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
