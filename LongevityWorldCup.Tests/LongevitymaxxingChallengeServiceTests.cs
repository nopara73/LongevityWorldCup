using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    public async Task SignupCanRemainOpenDuringActiveChallengeWhenConfigured()
    {
        using var fixture = TestChallengeFixture.Create(signupClosesAtUtc: "2026-06-09T22:00:00Z");
        var lateSignup = DateTimeOffset.Parse("2026-06-09T12:00:00Z");

        var publicState = fixture.Service.GetPublicState(lateSignup);
        Assert.Equal("active", publicState.Phase);
        Assert.True(publicState.SignupOpen);

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "late@example.com",
            "Late Lina",
            "UTC",
            null,
            []), lateSignup);
        var access = await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), lateSignup.AddMinutes(1));

        Assert.Contains(access.State.EligibleDays, day => day.ChallengeDay == 1);
        Assert.DoesNotContain(access.State.EligibleDays, day => day.ChallengeDay == 2);
        Assert.False(fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-09T22:01:00Z")).SignupOpen);
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
        var access = await fixture.ConfirmParticipantAsync("notes@example.com", "Notes Nora");
        using var stream = CreatePngStream(width: 2400, height: 1200);
        var file = CreatePngFormFile(stream, formName: "notePhotos", fileName: "kitchen.png");
        var now = DateTimeOffset.Parse("2026-06-09T08:00:00Z");

        var state = await fixture.Service.SubmitCheckInAsync(
            new LongevitymaxxingCheckInRequest(access, 1, 2, 2, 2, 2, "Good breakfast prep.\nps.: kept line break"),
            [file],
            now);

        var note = Assert.Single(state.Notes);
        Assert.Equal("Good breakfast prep.\nps.: kept line break", note.Note);
        var image = Assert.Single(note.Images);
        Assert.Contains("/generated/longevitymaxxing/check-in-photos/", image.Url);
        Assert.Contains("?v=", image.Url);
        Assert.Equal(1600, image.Width);
        Assert.Equal(800, image.Height);

        var draft = Assert.Single(state.EligibleDays).Existing;
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
            new LongevitymaxxingCheckInRequest(access, 1, 2, 1, 2, 2, "Edited note."),
            now.AddMinutes(5));

        var editedNote = Assert.Single(edited.Notes);
        Assert.Equal("Edited note.", editedNote.Note);
        Assert.Single(editedNote.Images);
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

        Assert.True(SpinWait.SpinUntil(() => fixture.Http.Requests.Count > 0, TimeSpan.FromSeconds(1)));
        gravatarGate.Set();
        Assert.True(SpinWait.SpinUntil(() =>
            fixture.Service.GetParticipantState(access).Participant.ProfileImageUrl is not null,
            TimeSpan.FromSeconds(2)));
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

        Assert.True(SpinWait.SpinUntil(() => fixture.Http.Requests.Count > 0, TimeSpan.FromSeconds(1)));
        gravatarGate.Set();
        Assert.True(SpinWait.SpinUntil(() =>
            fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-09T09:00:01Z")).Leaderboard.Single().ProfileImageUrl is not null,
            TimeSpan.FromSeconds(2)));

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
            TimeSpan.FromSeconds(2)));
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
            TimeSpan.FromSeconds(2)));
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
        Assert.True(SpinWait.SpinUntil(() => fixture.Http.Requests.Count > 0, TimeSpan.FromSeconds(1)));
        gravatarGate.Set();
        Assert.True(SpinWait.SpinUntil(() =>
            fixture.Service.GetParticipantState(access).Participant.ProfileImageUrl is not null,
            TimeSpan.FromSeconds(2)));
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
            ["Young Ranked", "Zelda Older", "Same Rank Old", "Same Rank Young", "Aaron Plain"],
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
    public async Task HabitPointsRampSlightlyAfterPracticeDay()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("ramp@example.com", "Ramp Rae");

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
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-10T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            14,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-22T08:00:00Z"));

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-22T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);

        Assert.Equal(11, state.DailyMaxScore);
        Assert.Equal(19, row.TotalPoints);
        Assert.Null(row.Cells.Single(cell => cell.ChallengeDay == 1).Score);
        Assert.Equal(8, row.Cells.Single(cell => cell.ChallengeDay == 2).Score);
        Assert.Equal(11, row.Cells.Single(cell => cell.ChallengeDay == 14).Score);
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
    }

    [Fact]
    public async Task DailyReminderCandidatesStopAfterThreeConsecutiveMissedScoredDays()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync("missed@example.com", "Missed Max");

        var beforeThreshold = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-11T08:05:00Z"));
        Assert.Single(beforeThreshold);
        Assert.Equal(3, beforeThreshold[0].ChallengeDay);

        Assert.Empty(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-12T08:05:00Z")));
    }

    [Fact]
    public async Task DailyReminderMissThresholdIgnoresScoredDaysBeforeLateSignup()
    {
        using var fixture = TestChallengeFixture.Create(signupClosesAtUtc: "2026-06-12T22:00:00Z");
        var signup = DateTimeOffset.Parse("2026-06-10T12:00:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "late-missed@example.com",
            "Late Missy",
            "UTC",
            null,
            []), signup);
        await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), signup.AddMinutes(1));

        var candidates = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-12T08:05:00Z"));

        var candidate = Assert.Single(candidates);
        Assert.Equal(4, candidate.ChallengeDay);
    }

    [Fact]
    public async Task DailyReminderMissThresholdResumesAfterParticipantChecksIn()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("resume@example.com", "Resume Rae");

        Assert.Empty(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-12T08:05:00Z")));

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            4,
            1,
            1,
            1,
            1,
            null), DateTimeOffset.Parse("2026-06-12T09:00:00Z"));

        var resumed = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-13T08:05:00Z"));

        var reminder = Assert.Single(resumed);
        Assert.Equal(5, reminder.ChallengeDay);
    }

    [Fact]
    public async Task DailyReminderEmailIncludesUpdatedCallSchedule()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "daily-call@example.com",
            "Daily Call Dana",
            [new("kickoff", "kickoff-b"), new("midpoint", "midpoint-a"), new("finale", "finale-a")],
            timeZoneId: "Europe/Budapest");

        var reminder = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-09T11:05:00Z")));
        Assert.True(reminder.IncludeCallScheduleUpdate);
        var content = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(
            reminder,
            fixture.Service.BuildAccessUrl(reminder.AccessToken),
            fixture.Service.BuildStopUrl(reminder.StopToken));

        Assert.Contains("Updated call schedule:", content.TextBody);
        Assert.Contains("- Kickoff: 2026-06-08 15:00 (Europe/Budapest)", content.TextBody);
        Assert.Contains("- Midpoint: 2026-06-15 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Contains("- Finale: 2026-06-21 08:30 (Europe/Budapest)", content.TextBody);
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
    public async Task CallSelectionUsesTopVotedSlotAndVideoLinkIsParticipantOnly()
    {
        using var fixture = TestChallengeFixture.Create(callSelectionClosesAtUtc: "2026-06-06T18:00:00Z");
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

        var candidates = fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-07T13:05:00Z"));
        var reminder = Assert.Single(candidates);
        Assert.Equal("kickoff", reminder.CallKey);
        Assert.Equal("24h", reminder.ReminderKind);
        Assert.Equal("2026-06-08T13:00:00.0000000+00:00", reminder.StartsAtUtc);
        Assert.Equal("UTC", reminder.TimeZoneId);
        Assert.Equal(3, reminder.Calls.Count);

        Assert.Empty(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-07T13:05:00Z")));

        fixture.Service.MarkCallReminderSent(reminder.ParticipantId, reminder.CallKey, reminder.ReminderKind, DateTimeOffset.Parse("2026-06-07T13:06:00Z"));
        Assert.Empty(fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-07T13:07:00Z")));
    }

    [Fact]
    public async Task CallReminderEmailIncludesTimeLinkAndParticipantPageOnly()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "call@example.com",
            "Call Casey",
            [new("kickoff", "kickoff-b")],
            timeZoneId: "Europe/Budapest");

        var reminder = Assert.Single(fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-07T13:05:00Z")));
        var content = SmtpLongevitymaxxingEmailSender.BuildCallReminderEmailContent(
            reminder,
            fixture.Service.BuildAccessUrl(reminder.AccessToken),
            fixture.Service.BuildStopUrl(reminder.StopToken));

        Assert.Contains("Call link:\nhttps://meet.example.test", content.TextBody);
        Assert.Contains("2026-06-08 15:00 (Europe/Budapest)", content.TextBody);
        Assert.Contains("Participant page:\nhttps://example.test/longevitymaxxing?", content.TextBody);
        Assert.DoesNotContain("2026-06-08 13:00 UTC", content.TextBody);
        Assert.DoesNotContain("2026-06-07 08:30", content.TextBody);
        Assert.DoesNotContain("UTC+02:00", content.TextBody);
        Assert.DoesNotContain("Full call schedule:", content.TextBody);
        Assert.DoesNotContain("- Midpoint:", content.TextBody);
        Assert.DoesNotContain("calendar invite", content.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(content.Attachments);
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
        Assert.Equal("2026-06-08T13:00:00.0000000+00:00", candidates[0].Calls.Single(call => call.Key == "kickoff").SelectedSlot?.StartsAtUtc);

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
            [new("kickoff", "kickoff-b"), new("midpoint", "midpoint-a"), new("finale", "finale-b")],
            timeZoneId: "Europe/Budapest");

        var start = Assert.Single(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-08T00:01:00Z")));
        var content = SmtpLongevitymaxxingEmailSender.BuildChallengeStartEmailContent(
            start,
            fixture.Service.BuildAccessUrl(start.AccessToken),
            fixture.Service.BuildStopUrl(start.StopToken));

        Assert.Contains("Timezone: Europe/Budapest", content.TextBody);
        Assert.Contains("Call link: https://meet.example.test", content.TextBody);
        Assert.Contains("- Kickoff: 2026-06-08 15:00 (Europe/Budapest)", content.TextBody);
        Assert.Contains("- Midpoint: 2026-06-15 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Contains("- Finale: 2026-06-21 08:30 (Europe/Budapest)", content.TextBody);
        Assert.DoesNotContain("2026-06-08 13:00 UTC", content.TextBody);
        Assert.DoesNotContain("2026-06-07 08:30", content.TextBody);
        Assert.DoesNotContain("2026-06-22 15:00", content.TextBody);
        Assert.DoesNotContain("UTC+02:00", content.TextBody);
        Assert.Contains("- Kickoff:", content.TextBody);
        Assert.Contains("- Midpoint:", content.TextBody);
        Assert.Contains("- Finale:", content.TextBody);
        Assert.Contains("A calendar invite with all selected calls is attached.", content.TextBody);

        var attachment = Assert.Single(content.Attachments);
        Assert.Equal("longevitymaxxing-calls.ics", attachment.FileName);
        Assert.Equal(3, CountOccurrences(attachment.Text, "BEGIN:VEVENT"));
        Assert.Contains("SUMMARY:Longevitymaxxing Kickoff call", attachment.Text);
        Assert.Contains("SUMMARY:Longevitymaxxing Midpoint call", attachment.Text);
        Assert.Contains("SUMMARY:Longevitymaxxing Finale call", attachment.Text);
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

    private sealed class TestChallengeFixture : IDisposable
    {
        private TestChallengeFixture(
            string root,
            DatabaseManager db,
            FakeEmailSender email,
            FakeHttpClientFactory http,
            FakeAthleteSnapshotProvider athletes,
            LongevitymaxxingChallengeService service)
        {
            ContentRoot = root;
            Db = db;
            Email = email;
            Http = http;
            Athletes = athletes;
            Service = service;
        }

        public string ContentRoot { get; }
        public DatabaseManager Db { get; }
        public FakeEmailSender Email { get; }
        public FakeHttpClientFactory Http { get; }
        public FakeAthleteSnapshotProvider Athletes { get; }
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
                    VideoCallUrl = "https://meet.example.test",
                    Calls =
                    [
                        new()
                        {
                            Key = "kickoff",
                            Label = "Kickoff",
                            CandidateSlots =
                            [
                                new() { Id = "kickoff-a", StartsAtUtc = "2026-06-08T06:30:00Z" },
                                new() { Id = "kickoff-b", StartsAtUtc = "2026-06-08T13:00:00Z" },
                                new() { Id = "kickoff-c", StartsAtUtc = "2026-06-08T16:00:00Z" }
                            ]
                        },
                        new()
                        {
                            Key = "midpoint",
                            Label = "Midpoint",
                            CandidateSlots =
                            [
                                new() { Id = "midpoint-a", StartsAtUtc = "2026-06-15T06:30:00Z" },
                                new() { Id = "midpoint-b", StartsAtUtc = "2026-06-15T13:00:00Z" },
                                new() { Id = "midpoint-c", StartsAtUtc = "2026-06-15T16:00:00Z" }
                            ]
                        },
                        new()
                        {
                            Key = "finale",
                            Label = "Finale",
                            CandidateSlots =
                            [
                                new() { Id = "finale-a", StartsAtUtc = "2026-06-22T06:30:00Z" },
                                new() { Id = "finale-b", StartsAtUtc = "2026-06-22T13:00:00Z" },
                                new() { Id = "finale-c", StartsAtUtc = "2026-06-22T16:00:00Z" }
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
                NullLogger<LongevitymaxxingChallengeService>.Instance,
                athletes);
            return new TestChallengeFixture(root, db, email, http, athletes, service);
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
            IReadOnlyList<LongevitymaxxingCallAvailabilitySelection>? callAvailability = null,
            string? athleteLink = null,
            string timeZoneId = "UTC")
        {
            var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");
            await Service.SignupAsync(new LongevitymaxxingSignupRequest(email, name, timeZoneId, athleteLink, callAvailability ?? []), now);
            var token = ReadQueryToken(Email.Confirmations.Last().Url, "confirm");
            var access = await Service.ConfirmAsync(token, now.AddMinutes(1));
            return access.AccessToken;
        }

        public void InsertConfirmedParticipant(string email, string name)
        {
            var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z").ToString("o");
            Db.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO LongevitymaxxingParticipants
                    (Id, Email, DisplayName, TimeZoneId, AthleteSlug, AccessToken, ConfirmationToken, StopToken, ConfirmedAtUtc, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (@id, @email, @name, 'UTC', NULL, @access, @confirm, @stop, @confirmed, @created, @updated);
                    """;
                cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@email", email);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@access", $"access-{Guid.NewGuid():N}");
                cmd.Parameters.AddWithValue("@confirm", $"confirm-{Guid.NewGuid():N}");
                cmd.Parameters.AddWithValue("@stop", $"stop-{Guid.NewGuid():N}");
                cmd.Parameters.AddWithValue("@confirmed", now);
                cmd.Parameters.AddWithValue("@created", now);
                cmd.Parameters.AddWithValue("@updated", now);
                cmd.ExecuteNonQuery();
            });
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
