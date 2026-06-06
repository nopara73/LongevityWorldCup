using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
    public async Task ParticipantWithoutUploadedPictureFallsBackToCachedGravatar()
    {
        using var gravatar = CreatePngStream();
        using var fixture = TestChallengeFixture.Create(gravatarResponse: gravatar.ToArray());
        var access = await fixture.ConfirmParticipantAsync("gravatar@example.com", "Gravatar Gail");

        var state = fixture.Service.GetParticipantState(access);

        Assert.NotNull(state.Participant.ProfileImageUrl);
        Assert.Contains(".gravatar.webp?v=", state.Participant.ProfileImageUrl);
        var row = Assert.Single(state.Public.Leaderboard);
        Assert.Equal(state.Participant.ProfileImageUrl, row.ProfileImageUrl);
        Assert.DoesNotContain("gravatar.com", state.Participant.ProfileImageUrl);
        Assert.Single(fixture.Http.Requests);

        var cached = fixture.Service.GetParticipantState(access);

        Assert.Equal(state.Participant.ProfileImageUrl, cached.Participant.ProfileImageUrl);
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
        using var fixture = TestChallengeFixture.Create(gravatarResponse: gravatar.ToArray());
        var access = await fixture.ConfirmParticipantAsync("priority@example.com", "Priority Pat");
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

        Assert.Contains("without a LWC athlete profile", ex.Message);
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

        var beforeClose = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-06T17:59:00Z"));
        Assert.Null(beforeClose.Calls.Single(c => c.Key == "kickoff").SelectedSlot);
        Assert.Equal("2026-06-08T00:00:00.0000000+00:00", beforeClose.SignupClosesAtUtc);

        var participantBeforeClose = fixture.Service.GetParticipantState(one, DateTimeOffset.Parse("2026-06-06T17:59:30Z"));
        var pendingKickoff = participantBeforeClose.Calls.Single(c => c.Key == "kickoff");
        Assert.Null(pendingKickoff.SelectedSlot);
        Assert.Equal("https://meet.example.test", pendingKickoff.VideoCallUrl);

        fixture.Service.TrySelectCallSlots(DateTimeOffset.Parse("2026-06-06T18:01:00Z"));

        var publicState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-06T18:02:00Z"));
        Assert.Equal("kickoff-b", publicState.Calls.Single(c => c.Key == "kickoff").SelectedSlot?.Id);

        var participantState = fixture.Service.GetParticipantState(one, DateTimeOffset.Parse("2026-06-06T18:03:00Z"));
        Assert.Equal("https://meet.example.test", participantState.Calls.Single(c => c.Key == "kickoff").VideoCallUrl);
        Assert.Contains(participantState.CallAvailability, selection => selection is { CallKey: "kickoff", SlotId: "kickoff-b" });
    }

    [Fact]
    public async Task CallReminderCandidatesCanSendKickoff24HourReminderBeforeSignupCloses()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "call@example.com",
            "Call Casey",
            [new("kickoff", "kickoff-b")]);

        var candidates = fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-07T02:05:00Z"));
        var reminder = Assert.Single(candidates);
        Assert.Equal("kickoff", reminder.CallKey);
        Assert.Equal("24h", reminder.ReminderKind);
        Assert.Equal("2026-06-08T02:00:00.0000000+00:00", reminder.StartsAtUtc);

        Assert.Empty(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-07T02:05:00Z")));

        fixture.Service.MarkCallReminderSent(reminder.ParticipantId, reminder.CallKey, reminder.ReminderKind, DateTimeOffset.Parse("2026-06-07T02:06:00Z"));
        Assert.Empty(fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-07T02:07:00Z")));
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
    public async Task FinalResultEventsIncludePodiumAndLinkedCompletionsAfterGraceWindow()
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

        Assert.Empty(fixture.Service.GetFinalResultEventRows(DateTimeOffset.Parse("2026-06-23T12:00:00Z")));

        var rows = fixture.Service.GetFinalResultEventRows(DateTimeOffset.Parse("2026-06-24T00:01:00Z"));

        Assert.Equal(4, rows.Count);
        Assert.Equal(1, rows.Single(row => row.DisplayName == "Bob").Placement);
        Assert.Equal(2, rows.Single(row => row.DisplayName == "Cara").Placement);
        Assert.Equal(3, rows.Single(row => row.DisplayName == "Dan").Placement);

        var aliceRow = rows.Single(row => row.DisplayName == "Alice");
        Assert.Equal(4, aliceRow.Placement);
        Assert.True(aliceRow.Completed);
        Assert.Equal("alice-athlete", aliceRow.AthleteSlug);
        Assert.Equal(14, aliceRow.CheckedInDays);

        Assert.DoesNotContain(rows, row => row.DisplayName == "Eve");
        Assert.All(rows, row => Assert.Equal(DateTimeKind.Utc, row.OccurredAtUtc.Kind));
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

    private static MemoryStream CreatePngStream()
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(4, 4, new Rgba32(21, 184, 166));
        image.SaveAsPng(stream);
        stream.Position = 0;
        return stream;
    }

    private static IFormFile CreatePngFormFile(MemoryStream stream)
    {
        return new FormFile(stream, 0, stream.Length, "profilePicture", "profile.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };
    }

    private sealed class TestChallengeFixture : IDisposable
    {
        private TestChallengeFixture(
            string root,
            DatabaseManager db,
            FakeEmailSender email,
            FakeHttpClientFactory http,
            LongevitymaxxingChallengeService service)
        {
            ContentRoot = root;
            Db = db;
            Email = email;
            Http = http;
            Service = service;
        }

        public string ContentRoot { get; }
        public DatabaseManager Db { get; }
        public FakeEmailSender Email { get; }
        public FakeHttpClientFactory Http { get; }
        public LongevitymaxxingChallengeService Service { get; }

        public static TestChallengeFixture Create(
            byte[]? gravatarResponse = null,
            string? profileJson = null,
            byte[]? profileImageResponse = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "lwc-lmx-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var db = new DatabaseManager(dbPath: Path.Combine(root, "challenge.db"));
            var email = new FakeEmailSender();
            var http = new FakeHttpClientFactory(gravatarResponse, profileJson, profileImageResponse);
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
                http,
                env,
                email,
                NullLogger<LongevitymaxxingChallengeService>.Instance);
            return new TestChallengeFixture(root, db, email, http, service);
        }

        public async Task<string> ConfirmParticipantAsync(
            string email,
            string name,
            IReadOnlyList<LongevitymaxxingCallAvailabilitySelection>? callAvailability = null,
            string? athleteLink = null)
        {
            var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");
            await Service.SignupAsync(new LongevitymaxxingSignupRequest(email, name, "UTC", athleteLink, callAvailability ?? []), now);
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

    private sealed class FakeHttpClientFactory(
        byte[]? gravatarResponse,
        string? profileJson,
        byte[]? profileImageResponse) : IHttpClientFactory
    {
        public List<Uri> Requests { get; } = [];
        public List<string> UserAgents { get; } = [];

        public HttpClient CreateClient(string name)
            => new(new FakeHttpMessageHandler(Requests, UserAgents, gravatarResponse, profileJson, profileImageResponse));
    }

    private sealed class FakeHttpMessageHandler(
        List<Uri> requests,
        List<string> userAgents,
        byte[]? gravatarResponse,
        string? profileJson,
        byte[]? profileImageResponse) : HttpMessageHandler
    {
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => BuildResponse(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(BuildResponse(request));

        private HttpResponseMessage BuildResponse(HttpRequestMessage request)
        {
            requests.Add(request.RequestUri!);
            userAgents.Add(request.Headers.UserAgent.ToString());
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
