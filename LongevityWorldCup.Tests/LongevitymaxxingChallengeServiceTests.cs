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
            25m), now);

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
    public async Task SignupStaysOpenDuringActiveChallengeWithoutBackfillingBeforeSignup()
    {
        using var fixture = TestChallengeFixture.Create(signupClosesAtUtc: "2026-06-09T22:00:00Z");
        var signup = DateTimeOffset.Parse("2026-06-09T12:00:00Z");

        var publicState = fixture.Service.GetPublicState(signup);
        Assert.Equal("active", publicState.Phase);
        Assert.True(publicState.SignupOpen);

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "signup@example.com",
            "Signup Sue",
            "UTC",
            null,
            25m), signup);
        var access = await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), signup.AddMinutes(1));

        Assert.Empty(access.State.EligibleDays);
        var nextDay = fixture.Service.GetParticipantState(access.AccessToken, DateTimeOffset.Parse("2026-06-10T08:05:00Z"));
        var practice = Assert.Single(nextDay.EligibleDays);
        Assert.Equal(2, practice.ChallengeDay);
        Assert.False(practice.CountsForScore);
        Assert.DoesNotContain(access.State.EligibleDays, day => day.ChallengeDay == 2);
        Assert.True(fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-09T22:01:00Z")).SignupOpen);
    }

    [Fact]
    public async Task SignupStaysOpenBeforeChallengeStartAfterConfiguredSignupClose()
    {
        using var fixture = TestChallengeFixture.Create(signupClosesAtUtc: "2026-06-01T00:00:00Z");
        var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");

        var publicState = fixture.Service.GetPublicState(now);
        Assert.Equal("signup", publicState.Phase);
        Assert.True(publicState.SignupOpen);
        Assert.Empty(fixture.Service.GetChallengeStartCandidates(now));

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "prestart@example.com",
            "Prestart Pat",
            "UTC",
            null,
            25m), now);
        var access = await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), now.AddMinutes(1));

        Assert.Empty(access.State.EligibleDays);
        Assert.True(access.State.Public.SignupOpen);
    }

    [Fact]
    public async Task SignupReservesAthleteNamesForSelectedAthleteProfiles()
    {
        using var fixture = TestChallengeFixture.Create();
        var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z");
        await fixture.ConfirmParticipantAsync("desktop@example.com", "Desktop Dana");
        fixture.Athletes.Snapshot.Add(new JsonObject
        {
            ["AthleteSlug"] = "athlete_alex",
            ["Name"] = "Athlete Alex",
            ["DisplayName"] = "Athlete Display"
        });

        var duplicateParticipant = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("duplicate@example.com", "desktop   dana", "UTC", null, 25m),
            now));
        Assert.Equal("That username is already taken.", duplicateParticipant.Message);

        var duplicateAthleteName = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("athlete-name@example.com", "athlete alex", "UTC", null, 25m),
            now));
        Assert.Equal("That username is already used by a Longevity athlete.", duplicateAthleteName.Message);

        var duplicateAthleteDisplay = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("athlete-display@example.com", "Athlete Display", "UTC", null, 25m),
            now));
        Assert.Equal("That username is already used by a Longevity athlete.", duplicateAthleteDisplay.Message);

        await fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("real-athlete@example.com", "Ignored Name", "UTC", "/athlete/athlete-alex", 25m),
            now);
        var athleteAccess = await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            now.AddMinutes(1));
        Assert.Equal("Athlete Display", athleteAccess.State.Participant.DisplayName);
        Assert.Equal("athlete-alex", athleteAccess.State.Participant.AthleteSlug);

        var duplicateAthleteProfile = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("same-athlete@example.com", "Athlete Display", "UTC", "/athlete/athlete-alex", 25m),
            now));
        Assert.Equal("That athlete profile is already in the challenge.", duplicateAthleteProfile.Message);
    }

    [Fact]
    public async Task EditRejectsDisplayNameChangesAfterSignup()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync("taken@example.com", "Taken Tina");
        var access = await fixture.ConfirmParticipantAsync("editor@example.com", "Editor Eli");

        var rename = Assert.Throws<InvalidOperationException>(() => fixture.Service.EditParticipant(
            new LongevitymaxxingParticipantEditRequest(access, "UTC", 25m, "taken tina")));
        Assert.Equal("Identity cannot be changed after signup.", rename.Message);

        var profile = fixture.Service.EditParticipant(
            new LongevitymaxxingParticipantEditRequest(access, "Europe/Budapest", 26m));
        Assert.Equal("Editor Eli", profile.Participant.DisplayName);
        Assert.Equal("Europe/Budapest", profile.Participant.TimeZoneId);
        Assert.Equal(26m, profile.Participant.CommitmentAmountUsd);
    }

    [Fact]
    public async Task EditRejectsAthleteProfileChangesAfterSignup()
    {
        using var fixture = TestChallengeFixture.Create();
        fixture.Athletes.Snapshot.Add(new JsonObject
        {
            ["AthleteSlug"] = "athlete_bea",
            ["Name"] = "Athlete Bea",
            ["DisplayName"] = "Bea Baseline"
        });
        var access = await fixture.ConfirmParticipantAsync("bea@example.com", "Bea User");

        var link = Assert.Throws<InvalidOperationException>(() => fixture.Service.EditParticipant(
            new LongevitymaxxingParticipantEditRequest(access, "UTC", 25m, AthleteLink: "/athlete/athlete-bea")));
        Assert.Equal("Identity cannot be changed after signup.", link.Message);

        var state = fixture.Service.GetParticipantState(access);
        Assert.Equal("Bea User", state.Participant.DisplayName);
        Assert.Null(state.Participant.AthleteSlug);
    }

    [Fact]
    public async Task EmptyPostDayFourteenLeaderboardStillRendersVisibleDayCells()
    {
        using var fixture = TestChallengeFixture.Create();

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-25T09:00:00Z"));

        Assert.Empty(state.Leaderboard);
        Assert.Contains(state.Days, day => day.ChallengeDay == 18);
        Assert.Equal(18, state.Days.Count);
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
        var access = await fixture.ConfirmParticipantAsync(
            "notes@example.com",
            "Notes Nora",
            nowUtc: DateTimeOffset.Parse("2026-06-19T12:00:00Z"));
        using var stream = CreatePngStream(width: 2400, height: 1200);
        var file = CreatePngFormFile(stream, formName: "notePhotos", fileName: "kitchen.png");
        var now = DateTimeOffset.Parse("2026-06-20T08:00:00Z");

        var state = await fixture.Service.SubmitCheckInAsync(
            new LongevitymaxxingCheckInRequest(access, 12, 2, 2, 2, 2, "Good breakfast prep.\nps.: kept line break"),
            [file],
            now);

        var note = Assert.Single(state.Notes);
        Assert.Equal("Good breakfast prep.\nps.: kept line break", note.Note);
        var image = Assert.Single(note.Images);
        Assert.Contains("/generated/longevitymaxxing/check-in-photos/", image.Url);
        Assert.Contains("?v=", image.Url);
        Assert.Equal(1600, image.Width);
        Assert.Equal(800, image.Height);

        var publicNote = Assert.Single(state.Public.Notes);
        Assert.Equal(note.ParticipantId, publicNote.ParticipantId);
        Assert.Equal("Notes Nora", publicNote.DisplayName);
        Assert.Equal("Good breakfast prep.\nps.: kept line break", publicNote.Note);
        Assert.Single(publicNote.Images);

        var draft = state.EligibleDays.Single(day => day.ChallengeDay == 12).Existing;
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
            new LongevitymaxxingCheckInRequest(access, 12, 2, 1, 2, 2, "Edited note."),
            now.AddMinutes(5));

        var editedNote = Assert.Single(edited.Notes);
        Assert.Equal("Edited note.", editedNote.Note);
        Assert.Single(editedNote.Images);
        Assert.Equal("Edited note.", Assert.Single(edited.Public.Notes).Note);
    }

    [Fact]
    public async Task PublicNotesOnlyExposeCheckInsAfterPublicNotesCutoff()
    {
        using var fixture = TestChallengeFixture.Create();
        var oldAccess = await fixture.ConfirmParticipantAsync("old-note@example.com", "Old Note");
        var newAccess = await fixture.ConfirmParticipantAsync(
            "new-note@example.com",
            "New Note",
            nowUtc: DateTimeOffset.Parse("2026-06-19T12:00:00Z"));

        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(oldAccess, 1, 2, 2, 2, 2, "legacy note"),
            DateTimeOffset.Parse("2026-06-09T08:00:00Z"));

        var afterCutoff = DateTimeOffset.Parse("2026-06-20T08:00:00Z");
        fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(newAccess, 12, 2, 2, 2, 2, "public note"),
            afterCutoff);

        var publicState = fixture.Service.GetPublicState(afterCutoff.AddMinutes(1));

        var publicNote = Assert.Single(publicState.Notes);
        Assert.Equal("public note", publicNote.Note);
        Assert.Equal("New Note", publicNote.DisplayName);
        Assert.DoesNotContain(publicState.Notes, note => note.Note == "legacy note");

        var participantState = fixture.Service.GetParticipantState(newAccess, afterCutoff.AddMinutes(1));
        Assert.Contains(participantState.Notes, note => note.Note == "legacy note" && note.DisplayName == "Old Note");
        Assert.Contains(participantState.Notes, note => note.Note == "public note" && note.DisplayName == "New Note");
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
            TimeSpan.FromSeconds(8)));
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
            TimeSpan.FromSeconds(8)));
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

        Assert.Contains("without a linked Longevity athlete profile", ex.Message);
    }

    [Fact]
    public async Task LeaderboardRanksScoreBeforeConsistencyAndKeepsLegacyNotesOutOfPublicState()
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
            alice,
            2,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-10T08:00:00Z"));
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
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            bob,
            3,
            0,
            0,
            0,
            0,
            null), DateTimeOffset.Parse("2026-06-11T08:00:00Z"));

        var publicState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-11T09:00:00Z"));

        Assert.Equal("Alice", publicState.Leaderboard[0].DisplayName);
        Assert.Equal(8, publicState.Leaderboard[0].TotalPoints);
        Assert.Equal(2, publicState.Leaderboard[0].CheckedInDays);
        Assert.Equal("Bob", publicState.Leaderboard[1].DisplayName);
        Assert.Equal(0, publicState.Leaderboard[1].TotalPoints);
        Assert.Equal(3, publicState.Leaderboard[1].CheckedInDays);
        Assert.DoesNotContain(publicState.Leaderboard[0].Badges, badge => badge.Contains("perfect start", StringComparison.OrdinalIgnoreCase));

        var participantState = fixture.Service.GetParticipantState(alice, DateTimeOffset.Parse("2026-06-11T09:00:00Z"));
        Assert.DoesNotContain(publicState.Notes, note => note.Note == "perfect start");
        Assert.DoesNotContain(publicState.Notes, note => note.Note == "still returned");
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
            ["Young Ranked", "Older Ranked", "Same Rank Old", "Same Rank Young", "Aaron Plain"],
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
    public async Task EarlierKnownCheckInStaysPracticeWhenSignupDerivedPracticeDayIsLater()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("zyron@example.com", "Zyron");

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            1,
            2,
            2,
            2,
            1,
            null), DateTimeOffset.Parse("2026-06-09T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            2,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-10T08:00:00Z"));

        fixture.Db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                UPDATE LongevitymaxxingParticipants
                SET CreatedAtUtc = @created,
                    UpdatedAtUtc = @created
                WHERE AccessToken = @access;
                """;
            cmd.Parameters.AddWithValue("@created", DateTimeOffset.Parse("2026-06-09T12:00:00Z").ToString("o"));
            cmd.Parameters.AddWithValue("@access", access);
            cmd.ExecuteNonQuery();
        });

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-10T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);
        var day1 = row.Cells.Single(cell => cell.ChallengeDay == 1);
        var day2 = row.Cells.Single(cell => cell.ChallengeDay == 2);

        Assert.False(day1.CountsForScore);
        Assert.Null(day1.Score);
        Assert.True(day2.CountsForScore);
        Assert.Equal(8, day2.Score);
        Assert.Equal(8, row.TotalPoints);
    }

    [Fact]
    public async Task HabitPointsRampSlightlyAfterPracticeDay()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("ramp@example.com", "Ramp Rae");

        SubmitChallengeDays(fixture, access, 14, 2, 2, 2, 2);

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-22T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);

        Assert.Equal(11, state.DailyMaxScore);
        Assert.Equal(125, row.TotalPoints);
        Assert.Null(row.Cells.Single(cell => cell.ChallengeDay == 1).Score);
        Assert.Equal(8, row.Cells.Single(cell => cell.ChallengeDay == 2).Score);
        Assert.Equal(11, row.Cells.Single(cell => cell.ChallengeDay == 14).Score);
    }

    [Fact]
    public async Task LeaderboardAndDailyRemindersContinuePastOriginalDuration()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("ongoing@example.com", "Ongoing Ona");
        SubmitChallengeDays(fixture, access, days: 14, sleep: 2, exercise: 2, nutrition: 2, vices: 2);

        var reminder = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-23T08:05:00Z")));
        Assert.Equal(15, reminder.ChallengeDay);
        Assert.True(reminder.CountsForScore);

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            15,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-23T08:10:00Z"));

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-23T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);
        var day15 = row.Cells.Single(cell => cell.ChallengeDay == 15);

        Assert.Equal("active", state.Phase);
        Assert.True(state.SignupOpen);
        Assert.Empty(state.Podium);
        Assert.Contains(state.Days, day => day.ChallengeDay == 15);
        Assert.Contains(state.Days, day => day.ChallengeDay == 16);
        Assert.True(day15.CheckedIn);
        Assert.True(day15.CountsForScore);
        Assert.Equal(11, day15.Score);
    }

    [Fact]
    public async Task LeaderboardPerformanceCountsOnlyLatestFourteenChallengeDays()
    {
        using var fixture = TestChallengeFixture.Create();
        var oldAccess = await fixture.ConfirmParticipantAsync("old-window@example.com", "Old Window");
        var recentAccess = await fixture.ConfirmParticipantAsync("recent-window@example.com", "Recent Window");

        for (var day = 1; day <= 16; day++)
        {
            fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
                oldAccess,
                day,
                2,
                2,
                2,
                2,
                null), DateTimeOffset.Parse("2026-06-09T08:00:00Z").AddDays(day - 1));
        }

        for (var day = 3; day <= 16; day++)
        {
            fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
                recentAccess,
                day,
                2,
                2,
                2,
                2,
                null), DateTimeOffset.Parse("2026-06-09T08:00:00Z").AddDays(day - 1));
        }

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-23T09:00:00Z"));

        var recent = state.Leaderboard.Single(row => row.DisplayName == "Recent Window");
        var old = state.Leaderboard.Single(row => row.DisplayName == "Old Window");
        Assert.Equal(14, recent.CheckedInDays);
        Assert.Equal(14, old.CheckedInDays);
        Assert.InRange(recent.CurrentStreak, 0, 14);
        Assert.Equal(14, old.CurrentStreak);
        Assert.Equal(recent.TotalPoints, old.TotalPoints);
        Assert.True(old.Cells.Single(cell => cell.ChallengeDay == 1).CheckedIn);
        Assert.True(old.Cells.Single(cell => cell.ChallengeDay == 2).CheckedIn);
        Assert.True(old.Cells.Single(cell => cell.ChallengeDay == 16).CheckedIn);
        Assert.True(old.TotalPoints < old.Cells.Where(cell => cell.CheckedIn && cell.Score is not null).Sum(cell => cell.Score!.Value));
    }

    [Fact]
    public async Task SignupAfterOriginalDurationStartsFromSignupDateWithPersonalPracticeDay()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-25T12:00:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "ongoing-signup@example.com",
            "Ongoing Signup",
            "UTC",
            null,
            25m), signup);
        var access = await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), signup.AddMinutes(1));

        Assert.Empty(access.State.EligibleDays);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access.AccessToken, 17, 2, 2, 2, 2, null),
            DateTimeOffset.Parse("2026-06-26T08:00:00Z")));

        var nextDay = fixture.Service.GetParticipantState(access.AccessToken, DateTimeOffset.Parse("2026-06-26T08:05:00Z"));
        var practice = Assert.Single(nextDay.EligibleDays);
        Assert.Equal(18, practice.ChallengeDay);
        Assert.False(practice.CountsForScore);

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            18,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-26T08:10:00Z"));

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-26T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);
        var day17 = row.Cells.Single(cell => cell.ChallengeDay == 17);
        var day18 = row.Cells.Single(cell => cell.ChallengeDay == 18);

        Assert.False(day17.CheckedIn);
        Assert.True(day18.CheckedIn);
        Assert.False(day18.CountsForScore);
        Assert.Null(day18.Score);
        Assert.Equal(1, row.CheckedInDays);
        Assert.Equal(0, row.TotalPoints);
    }

    [Fact]
    public async Task SignupDateControlsPracticeDayWhenConfirmationIsNextLocalDay()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-25T23:50:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "overnight-signup@example.com",
            "Overnight Signup",
            "UTC",
            null,
            25m), signup);

        var access = await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            DateTimeOffset.Parse("2026-06-26T00:05:00Z"));

        var practice = Assert.Single(access.State.EligibleDays);
        Assert.Equal(18, practice.ChallengeDay);
        Assert.False(practice.CountsForScore);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access.AccessToken, 17, 2, 2, 2, 2, null),
            DateTimeOffset.Parse("2026-06-26T08:00:00Z")));

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            18,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-26T08:10:00Z"));

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-26T09:00:00Z"));
        var row = Assert.Single(state.Leaderboard);
        Assert.Equal(1, row.CheckedInDays);
        Assert.Equal(0, row.TotalPoints);
        Assert.False(row.Cells.Single(cell => cell.ChallengeDay == 18).CountsForScore);
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
        var stoppedEmails = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-10T08:06:00Z"));
        Assert.True(stoppedEmails.Participant.ChallengeEmailsStopped);
        Assert.False(stoppedEmails.Participant.ChallengeInactive);
        Assert.True(stoppedEmails.Public.Leaderboard.Single().ChallengeEmailsStopped);
        Assert.False(stoppedEmails.Public.Leaderboard.Single().ChallengeInactive);
    }

    [Fact]
    public async Task StoppedEmailsParticipantWithoutCheckInsIsInactiveAfterMissThreshold()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("legacy-stop@example.com", "Guilherme Schwarz");

        fixture.Service.StopChallengeEmails(access, DateTimeOffset.Parse("2026-06-19T12:57:59Z"));

        var state = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-20T09:00:00Z"));

        Assert.True(state.Participant.ChallengeEmailsStopped);
        Assert.True(state.Participant.ChallengeInactive);
        var row = Assert.Single(state.Public.Leaderboard);
        Assert.True(row.ChallengeEmailsStopped);
        Assert.True(row.ChallengeInactive);
    }

    [Fact]
    public async Task DailyReminderCandidatesStopAfterThreeConsecutiveMissedScoredDays()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("missed@example.com", "Missed Max");

        var beforeThreshold = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-11T08:05:00Z"));
        Assert.Single(beforeThreshold);
        Assert.Equal(3, beforeThreshold[0].ChallengeDay);

        Assert.Empty(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-12T08:05:00Z")));
        var stillActive = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-12T08:05:30Z"));
        Assert.False(stillActive.Participant.ChallengeEmailsStopped);

        fixture.Service.ApplyDailyReminderStopRules(DateTimeOffset.Parse("2026-06-12T08:05:00Z"));
        var stopped = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-12T08:06:00Z"));
        Assert.False(stopped.Participant.ChallengeEmailsStopped);
        Assert.True(stopped.Participant.ChallengeInactive);
        Assert.False(stopped.Public.Leaderboard.Single().ChallengeEmailsStopped);
        Assert.True(stopped.Public.Leaderboard.Single().ChallengeInactive);
    }

    [Fact]
    public async Task DailyReminderMissThresholdIgnoresScoredDaysBeforeSignup()
    {
        using var fixture = TestChallengeFixture.Create(signupClosesAtUtc: "2026-06-12T22:00:00Z");
        var signup = DateTimeOffset.Parse("2026-06-10T12:00:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "joined-missed@example.com",
            "Joined Jenny",
            "UTC",
            null,
            25m), signup);
        await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), signup.AddMinutes(1));

        var candidates = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-12T08:05:00Z"));

        var candidate = Assert.Single(candidates);
        Assert.Equal(4, candidate.ChallengeDay);
    }

    [Fact]
    public async Task DailyReminderMissThresholdBlocksRestingCheckIn()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("resume@example.com", "Resume Rae");

        Assert.Empty(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-12T08:05:00Z")));
        fixture.Service.ApplyDailyReminderStopRules(DateTimeOffset.Parse("2026-06-12T08:05:00Z"));
        var stopped = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-12T08:06:00Z"));
        Assert.True(stopped.Participant.ChallengeInactive);
        Assert.Empty(stopped.EligibleDays);

        InsertLegacyCheckInAfterResting(
            fixture,
            access,
            4,
            "2026-06-11",
            DateTimeOffset.Parse("2026-06-12T09:00:00Z"));

        var legacyState = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-12T09:30:00Z"));
        var legacyRow = Assert.Single(legacyState.Public.Leaderboard);
        Assert.True(legacyRow.ChallengeInactive);
        Assert.Equal(0, legacyRow.CheckedInDays);
        Assert.False(legacyRow.Cells.Single(cell => cell.ChallengeDay == 4).CheckedIn);
        Assert.DoesNotContain(legacyState.Notes, note => note.Note == "legacy post-rest check-in");

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            4,
            1,
            1,
            1,
            1,
            null), DateTimeOffset.Parse("2026-06-12T09:00:00Z")));
        Assert.Contains("resting", error.Message, StringComparison.OrdinalIgnoreCase);

        var resumed = fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-13T08:05:00Z"));

        Assert.Empty(resumed);
    }

    [Fact]
    public async Task DailyReminderEmailIncludesUpdatedCallSchedule()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "daily-call@example.com",
            "Daily Call Dana",
            timeZoneId: "Europe/Budapest");

        var reminder = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-09T11:05:00Z")));
        Assert.True(reminder.IncludeCallScheduleUpdate);
        var content = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(
            reminder,
            fixture.Service.BuildAccessUrl(reminder.AccessToken),
            fixture.Service.BuildStopUrl(reminder.StopToken));

        Assert.Contains("Updated call schedule:", content.TextBody);
        Assert.DoesNotContain("- Kickoff:", content.TextBody);
        Assert.DoesNotContain("- Midpoint:", content.TextBody);
        Assert.DoesNotContain("- Finale:", content.TextBody);
        Assert.Contains("- Community call: 2026-06-14 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Contains("- Community call: 2026-06-21 08:30 (Europe/Budapest)", content.TextBody);
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
    public async Task WeeklyCommunityCallIsSelectedAndVideoLinkIsParticipantOnly()
    {
        using var fixture = TestChallengeFixture.Create(callSelectionClosesAtUtc: "2026-06-06T18:00:00Z");
        var one = await fixture.ConfirmParticipantAsync("one@example.com", "One");
        await fixture.ConfirmParticipantAsync("two@example.com", "Two");
        await fixture.ConfirmParticipantAsync("three@example.com", "Three");

        var beforeClose = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-06T17:59:00Z"));
        Assert.Equal("community-2026-06-07-a", beforeClose.Calls.Single(c => c.Key == "community-2026-06-07").SelectedSlot?.Id);
        Assert.Equal("2026-06-08T00:00:00.0000000+00:00", beforeClose.SignupClosesAtUtc);

        var participantBeforeClose = fixture.Service.GetParticipantState(one, DateTimeOffset.Parse("2026-06-06T17:59:30Z"));
        var communityCall = participantBeforeClose.Calls.Single(c => c.Key == "community-2026-06-07");
        Assert.Equal("community-2026-06-07-a", communityCall.SelectedSlot?.Id);
        Assert.Equal("https://meet.example.test", communityCall.VideoCallUrl);

        fixture.Service.TrySelectCallSlots(DateTimeOffset.Parse("2026-06-06T18:01:00Z"));

        var publicState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-06T18:02:00Z"));
        Assert.Equal("community-2026-06-07-a", publicState.Calls.Single(c => c.Key == "community-2026-06-07").SelectedSlot?.Id);

        var participantState = fixture.Service.GetParticipantState(one, DateTimeOffset.Parse("2026-06-06T18:03:00Z"));
        Assert.Equal("https://meet.example.test", participantState.Calls.Single(c => c.Key == "community-2026-06-07").VideoCallUrl);
    }

    [Fact]
    public async Task CallReminderCandidatesCanSendSundayCommunityCall24HourReminderBeforeSignupCloses()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync("call@example.com", "Call Casey");

        var candidates = fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-06T06:35:00Z"));
        var reminder = Assert.Single(candidates);
        Assert.Equal("community-2026-06-07", reminder.CallKey);
        Assert.Equal("Community call", reminder.CallLabel);
        Assert.Equal("24h", reminder.ReminderKind);
        Assert.Equal("2026-06-07T06:30:00.0000000+00:00", reminder.StartsAtUtc);
        Assert.Equal("UTC", reminder.TimeZoneId);
        Assert.Equal(4, reminder.Calls.Count);

        Assert.Empty(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-06T06:35:00Z")));

        fixture.Service.MarkCallReminderSent(reminder.ParticipantId, reminder.CallKey, reminder.ReminderKind, DateTimeOffset.Parse("2026-06-06T06:36:00Z"));
        Assert.Empty(fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-06T06:37:00Z")));
    }

    [Fact]
    public async Task CallReminderEmailIncludesTimeLinkParticipantPageAndCalendarInvite()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync(
            "call@example.com",
            "Call Casey",
            timeZoneId: "Europe/Budapest");

        var reminder = Assert.Single(fixture.Service.GetCallReminderCandidates(DateTimeOffset.Parse("2026-06-06T06:35:00Z")));
        var content = SmtpLongevitymaxxingEmailSender.BuildCallReminderEmailContent(
            reminder,
            fixture.Service.BuildAccessUrl(reminder.AccessToken),
            fixture.Service.BuildStopUrl(reminder.StopToken));

        Assert.Contains("Call link:\nhttps://meet.example.test", content.TextBody);
        Assert.Contains("The Longevitymaxxing Community call starts", content.TextBody);
        Assert.Contains("2026-06-07 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Equal("Longevitymaxxing Community call reminder", content.Subject);
        Assert.Contains("Participant page:\nhttps://example.test/longevitymaxxing?", content.TextBody);
        Assert.DoesNotContain("2026-06-08 06:30 UTC", content.TextBody);
        Assert.DoesNotContain("UTC+02:00", content.TextBody);
        Assert.DoesNotContain("Full call schedule:", content.TextBody);
        Assert.DoesNotContain("- Midpoint:", content.TextBody);
        var attachment = Assert.Single(content.Attachments);
        Assert.Equal("longevitymaxxing-community-call.ics", attachment.FileName);
        Assert.Equal("text/calendar; charset=utf-8", attachment.ContentType);
        Assert.Equal(1, CountOccurrences(attachment.Text, "BEGIN:VEVENT"));
        Assert.Contains("SUMMARY:Longevitymaxxing Community call", attachment.Text);
        Assert.Contains("DTSTART:20260607T063000Z", attachment.Text);
        Assert.Contains("LOCATION:https://meet.example.test", attachment.Text);
        Assert.Contains("Participant page: https://example.test/longevitymaxxing?", attachment.Text);
    }

    [Fact]
    public async Task ChallengeStartCandidatesWaitUntilChallengeStartAndSendOnce()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync("one@example.com", "One");
        await fixture.ConfirmParticipantAsync("two@example.com", "Two");

        Assert.Empty(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-07T23:59:00Z")));

        var candidates = fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-08T00:01:00Z"));

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(4, candidate.Calls.Count);
            Assert.All(candidate.Calls, call =>
            {
                Assert.NotNull(call.SelectedSlot);
                Assert.Equal("https://meet.example.test", call.VideoCallUrl);
            });
        });
        Assert.Equal("2026-06-14T06:30:00.0000000+00:00", candidates[0].Calls.First().SelectedSlot?.StartsAtUtc);

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
            timeZoneId: "Europe/Budapest");

        var start = Assert.Single(fixture.Service.GetChallengeStartCandidates(DateTimeOffset.Parse("2026-06-08T00:01:00Z")));
        var content = SmtpLongevitymaxxingEmailSender.BuildChallengeStartEmailContent(
            start,
            fixture.Service.BuildAccessUrl(start.AccessToken),
            fixture.Service.BuildStopUrl(start.StopToken));

        Assert.Contains("Timezone: Europe/Budapest", content.TextBody);
        Assert.Contains("Call link: https://meet.example.test", content.TextBody);
        Assert.Contains("- Community call: 2026-06-14 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Contains("- Community call: 2026-06-21 08:30 (Europe/Budapest)", content.TextBody);
        Assert.Contains("- Community call: 2026-06-28 08:30 (Europe/Budapest)", content.TextBody);
        Assert.DoesNotContain("2026-06-08 06:30 UTC", content.TextBody);
        Assert.DoesNotContain("2026-06-07 08:30", content.TextBody);
        Assert.DoesNotContain("2026-06-22 15:00", content.TextBody);
        Assert.DoesNotContain("UTC+02:00", content.TextBody);
        Assert.DoesNotContain("- Kickoff:", content.TextBody);
        Assert.DoesNotContain("- Midpoint:", content.TextBody);
        Assert.DoesNotContain("- Finale:", content.TextBody);
        Assert.Contains("A calendar invite with all selected calls is attached.", content.TextBody);

        var attachment = Assert.Single(content.Attachments);
        Assert.Equal("longevitymaxxing-calls.ics", attachment.FileName);
        Assert.Equal(4, CountOccurrences(attachment.Text, "BEGIN:VEVENT"));
        Assert.Contains("SUMMARY:Longevitymaxxing Community call", attachment.Text);
        Assert.DoesNotContain("SUMMARY:Longevitymaxxing Community call call", attachment.Text);
    }

    [Fact]
    public async Task ChallengeStartEmailAfterOriginalFinaleIncludesNextWeeklyCommunityCalls()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
        await fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("post-calls-start@example.com", "Post Calls Pat", "UTC", null, 25m),
            signup);
        await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            signup.AddMinutes(1));

        var start = Assert.Single(fixture.Service.GetChallengeStartCandidates(signup.AddMinutes(2)));
        Assert.NotEmpty(start.Calls);

        var content = SmtpLongevitymaxxingEmailSender.BuildChallengeStartEmailContent(
            start,
            fixture.Service.BuildAccessUrl(start.AccessToken),
            fixture.Service.BuildStopUrl(start.StopToken));

        Assert.Contains("Calls:", content.TextBody);
        Assert.Contains("- Community call: 2026-06-28 06:30 (UTC)", content.TextBody);
        Assert.Contains("calendar invite", content.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.Single(content.Attachments);
    }

    [Fact]
    public async Task DailyReminderAfterOriginalFinaleIncludesNextWeeklyCommunityCalls()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-24T12:00:00Z");
        await fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("post-calls-daily@example.com", "Post Calls Dana", "UTC", null, 25m),
            signup);
        await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            signup.AddMinutes(1));

        var reminder = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-25T08:05:00Z")));
        Assert.NotEmpty(reminder.Calls);
        Assert.True(reminder.IncludeCallScheduleUpdate);

        var content = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(
            reminder,
            fixture.Service.BuildAccessUrl(reminder.AccessToken),
            fixture.Service.BuildStopUrl(reminder.StopToken));

        Assert.Contains("Updated call schedule:", content.TextBody);
        Assert.Contains("- Community call: 2026-06-28 06:30 (UTC)", content.TextBody);
        Assert.Contains("Stop challenge emails:", content.TextBody);
    }

    [Fact]
    public async Task SignupAfterChallengeStartIsAccepted()
    {
        using var fixture = TestChallengeFixture.Create();

        await fixture.Service.SignupAsync(
            new LongevitymaxxingSignupRequest("started@example.com", "Started Sam", "UTC", null, 25m),
            DateTimeOffset.Parse("2026-06-08T00:01:00Z"));
        var access = await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            DateTimeOffset.Parse("2026-06-08T00:02:00Z"));

        var state = fixture.Service.GetParticipantState(access.AccessToken, DateTimeOffset.Parse("2026-06-09T08:05:00Z"));
        var day = Assert.Single(state.EligibleDays);
        Assert.Equal(1, day.ChallengeDay);
        Assert.False(day.CountsForScore);
    }

    [Fact]
    public async Task DayFourteenResultEventsIncludeTopThreeAndLinkedFinishersAfterGraceWindow()
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
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            eve,
            15,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-23T08:00:00Z"));

        Assert.Empty(fixture.Service.GetFinalResultEventRows(DateTimeOffset.Parse("2026-06-23T12:00:00Z")));

        var rows = fixture.Service.GetFinalResultEventRows(DateTimeOffset.Parse("2026-06-24T00:01:00Z"));

        Assert.Equal(4, rows.Count);
        Assert.Equal(1, rows.Single(row => row.DisplayName == "Bob").Placement);
        Assert.Equal(2, rows.Single(row => row.DisplayName == "Eve").Placement);
        Assert.Equal(3, rows.Single(row => row.DisplayName == "Cara").Placement);

        var aliceRow = rows.Single(row => row.DisplayName == "Alice");
        Assert.Equal(5, aliceRow.Placement);
        Assert.True(aliceRow.Completed);
        Assert.Equal("alice-athlete", aliceRow.AthleteSlug);
        Assert.Equal(14, aliceRow.CheckedInDays);

        Assert.DoesNotContain(rows, row => row.DisplayName == "Dan");
        Assert.All(rows, row => Assert.Equal(DateTimeKind.Utc, row.OccurredAtUtc.Kind));
    }

    [Fact]
    public async Task DayFourteenResultEventsExcludeParticipantsWhoJoinedAfterOriginalEndDate()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-25T12:00:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "post-end-event@example.com",
            "Post End Pat",
            "UTC",
            "/athlete/post-end-pat",
            25m), signup);
        var access = await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), signup.AddMinutes(1));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            18,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-26T08:00:00Z"));

        var publicState = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-26T09:00:00Z"));
        Assert.Contains(publicState.Leaderboard, row => row.DisplayName == "Post End Pat");

        var rows = fixture.Service.GetFinalResultEventRows(DateTimeOffset.Parse("2026-06-26T09:00:00Z"));
        Assert.Empty(rows);
    }

    [Fact]
    public async Task DayFourteenResultEventsUseSignupDateForOriginalWindowCutoff()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-21T23:50:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "last-day-signup@example.com",
            "Last Day Lee",
            "UTC",
            null,
            25m), signup);

        var access = await fixture.Service.ConfirmAsync(
            ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"),
            DateTimeOffset.Parse("2026-06-22T00:05:00Z"));

        var day = Assert.Single(access.State.EligibleDays, day => day.ChallengeDay == 14);
        Assert.False(day.CountsForScore);

        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            14,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-22T08:00:00Z"));

        var rows = fixture.Service.GetFinalResultEventRows(DateTimeOffset.Parse("2026-06-24T00:01:00Z"));
        var row = Assert.Single(rows);
        Assert.Equal("Last Day Lee", row.DisplayName);
        Assert.Equal(1, row.CheckedInDays);
    }

    [Fact]
    public async Task PublicStateStaysActiveAndDoesNotExposePodiumAfterGraceWindow()
    {
        using var fixture = TestChallengeFixture.Create();
        await fixture.ConfirmParticipantAsync("final@example.com", "Final Finn");

        var finalGraceDay = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-23T12:00:00Z"));
        Assert.Equal("active", finalGraceDay.Phase);
        Assert.Empty(finalGraceDay.Podium);

        var continuing = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-24T00:01:00Z"));
        Assert.Equal("active", continuing.Phase);
        Assert.Empty(continuing.Podium);
        Assert.True(continuing.SignupOpen);
        Assert.Contains(continuing.Days, day => day.ChallengeDay == 17);
    }

    [Fact]
    public async Task PublicLeaderboardDoesNotExposeCompletionBadgeAfterOriginalDuration()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("badge@example.com", "Badge Bea");
        SubmitChallengeDays(fixture, access, 14, 2, 2, 2, 2);

        var state = fixture.Service.GetPublicState(DateTimeOffset.Parse("2026-06-24T00:01:00Z"));
        var row = Assert.Single(state.Leaderboard);

        Assert.DoesNotContain("Completion", row.Badges);
    }

    [Fact]
    public async Task SignupAndProfileRequireValidCommitmentAmount()
    {
        using var fixture = TestChallengeFixture.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "missing-commitment@example.com",
            "Missing Commitment",
            "UTC",
            null)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "small-commitment@example.com",
            "Small Commitment",
            "UTC",
            null,
            0.99m)));

        var access = fixture.InsertConfirmedParticipant("legacy-commitment@example.com", "Legacy Lou", commitmentAmountUsd: null);
        var deferred = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-09T08:05:00Z"));
        Assert.Equal("deferred", deferred.Commitment.Status);
        Assert.False(deferred.Commitment.BlocksParticipant);
        Assert.False(deferred.Commitment.CanEditAmount);
        Assert.False(deferred.Commitment.CanPay);

        var checkedIn = fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access, 1, 2, 2, 2, 2, null),
            DateTimeOffset.Parse("2026-06-09T08:05:00Z"));
        Assert.Equal("deferred", checkedIn.Commitment.Status);

        var editedBeforeConclusion = fixture.Service.EditParticipant(new LongevitymaxxingParticipantEditRequest(
            access,
            "Europe/London"),
            DateTimeOffset.Parse("2026-06-10T08:05:00Z"));

        Assert.Equal("deferred", editedBeforeConclusion.Commitment.Status);
        Assert.Null(editedBeforeConclusion.Participant.CommitmentAmountUsd);

        var blocked = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-22T00:05:00Z"));
        Assert.Equal("needs-amount", blocked.Commitment.Status);
        Assert.True(blocked.Commitment.BlocksParticipant);
        Assert.True(blocked.Commitment.CanEditAmount);
        Assert.False(blocked.Commitment.CanPay);
        Assert.Contains("Configure an amount that'd hurt before continuing.", blocked.Commitment.Message);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.SubmitCheckIn(
            new LongevitymaxxingCheckInRequest(access, 14, 2, 2, 2, 2, null),
            DateTimeOffset.Parse("2026-06-22T08:05:00Z")));

        var configured = fixture.Service.EditParticipant(new LongevitymaxxingParticipantEditRequest(
            access,
            "UTC",
            12.345m),
            DateTimeOffset.Parse("2026-06-22T08:10:00Z"));

        Assert.Equal("clear", configured.Commitment.Status);
        Assert.Equal(12.35m, configured.Participant.CommitmentAmountUsd);
    }

    [Fact]
    public async Task CommitmentTriggersOnFourthScoredCheckInUsingExactAverageAndKeepsAmountPrivate()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("commitment-trigger@example.com", "Trigger Tess");

        SubmitCommitmentBaseline(fixture, access);
        var beforeTrigger = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-12T09:00:00Z"));
        Assert.Equal("clear", beforeTrigger.Commitment.Status);

        var due = SubmitCommitmentTriggerMiss(fixture, access);

        Assert.Equal("due", due.Commitment.Status);
        Assert.True(due.Commitment.BlocksParticipant);
        Assert.Equal(25m, due.Commitment.OwedAmountUsd);
        Assert.Equal(5, due.Commitment.TriggerChallengeDay);
        Assert.Equal(8, due.Commitment.TriggerScore);
        Assert.True(due.Commitment.ThresholdAverage is > 8m and < 9m);

        var row = Assert.Single(due.Public.Leaderboard);
        Assert.Equal("commitment-due", row.CommitmentStatus);
        Assert.True(row.Cells.Single(cell => cell.ChallengeDay == 5).CheckedIn);

        var publicJson = System.Text.Json.JsonSerializer.Serialize(due.Public);
        Assert.DoesNotContain("amountUsd", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("owedAmountUsd", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("commitmentAmountUsd", publicJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommitmentCanTriggerAfterOriginalDurationOnDay15()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("day15-commitment@example.com", "Day Fifteen Dana");
        SubmitChallengeDays(fixture, access, 14, 2, 2, 2, 2);

        var due = fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            15,
            0,
            0,
            0,
            0,
            null), DateTimeOffset.Parse("2026-06-23T08:00:00Z"));

        Assert.Equal("due", due.Commitment.Status);
        Assert.True(due.Commitment.BlocksParticipant);
        Assert.Equal(15, due.Commitment.TriggerChallengeDay);
        Assert.Equal(0, due.Commitment.TriggerScore);
        Assert.True(due.Commitment.ThresholdAverage >= 10m);
        Assert.Equal("commitment-due", due.Public.Leaderboard.Single().CommitmentStatus);
        Assert.True(due.Public.Leaderboard.Single().Cells.Single(cell => cell.ChallengeDay == 15).CheckedIn);
    }

    [Fact]
    public async Task PostDay14SignupKeepsCommitmentClearThroughPersonalPracticeDay()
    {
        using var fixture = TestChallengeFixture.Create();
        var signup = DateTimeOffset.Parse("2026-06-25T12:00:00Z");
        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "post-day14-commitment@example.com",
            "Post Day Paula",
            "UTC",
            null,
            25m), signup);
        var access = await fixture.Service.ConfirmAsync(ReadQueryToken(fixture.Email.Confirmations.Last().Url, "confirm"), signup.AddMinutes(1));

        var nextDay = fixture.Service.GetParticipantState(access.AccessToken, DateTimeOffset.Parse("2026-06-26T08:05:00Z"));
        var practice = Assert.Single(nextDay.EligibleDays);
        Assert.Equal(18, practice.ChallengeDay);
        Assert.False(practice.CountsForScore);

        var practiceState = fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access.AccessToken,
            18,
            0,
            0,
            0,
            0,
            null), DateTimeOffset.Parse("2026-06-26T08:10:00Z"));

        Assert.Equal("clear", practiceState.Commitment.Status);
        Assert.False(practiceState.Commitment.BlocksParticipant);
        var row = Assert.Single(practiceState.Public.Leaderboard);
        Assert.Null(row.CommitmentStatus);
        Assert.Null(row.Cells.Single(cell => cell.ChallengeDay == 18).Score);
    }

    [Fact]
    public async Task TrendGuidanceIncludesScoringForgivenessAllowance()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await fixture.ConfirmParticipantAsync("trend-forgiveness@example.com", "Trend Tina");
        SubmitChallengeDays(fixture, access, 4, 2, 2, 2, 2);

        var state = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-12T09:00:00Z"));

        Assert.True(state.TrendGuidance.Enforced);
        Assert.Equal(3, state.TrendGuidance.PriorScoredDays);
        Assert.Contains("Next scored day: need at least", state.TrendGuidance.Text);
        Assert.Contains("you can miss one whole habit or two somewhat", state.TrendGuidance.Text);
    }

    [Fact]
    public async Task SignupCannotMutateExistingParticipantWhileCommitmentIsDue()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await CreateCommitmentDueParticipantAsync(fixture, "blocked-signup@example.com", "Blocked Bea");

        await fixture.Service.SignupAsync(new LongevitymaxxingSignupRequest(
            "blocked-signup@example.com",
            "Renamed Bea",
            "Europe/Budapest",
            "/athlete/renamed-bea",
            99m), DateTimeOffset.Parse("2026-06-13T10:00:00Z"));

        var state = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-13T10:01:00Z"));

        Assert.Equal("Blocked Bea", state.Participant.DisplayName);
        Assert.Equal("UTC", state.Participant.TimeZoneId);
        Assert.Null(state.Participant.AthleteSlug);
        Assert.Equal(25m, state.Participant.CommitmentAmountUsd);
        Assert.Equal("due", state.Commitment.Status);
        Assert.Single(fixture.Email.AccessLinks);
    }

    [Fact]
    public async Task DatabaseRejectsDuplicateActiveCommitmentObligations()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await CreateCommitmentDueParticipantAsync(fixture, "duplicate-obligation@example.com", "Duplicate Dee");
        var participantId = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-13T09:10:00Z")).Participant.Id;

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => fixture.Db.Run(sqlite =>
        {
            using var insert = sqlite.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO LongevitymaxxingPaymentObligations
                (Id, ParticipantId, TriggerChallengeDay, TriggerScore, ThresholdAverage, AmountUsd, CreatedAtUtc, UpdatedAtUtc)
                VALUES (@id, @participantId, 6, 1, '8', '25', @created, @updated);
                """;
            insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("@participantId", participantId);
            insert.Parameters.AddWithValue("@created", "2026-06-13T09:11:00.0000000+00:00");
            insert.Parameters.AddWithValue("@updated", "2026-06-13T09:11:00.0000000+00:00");
            insert.ExecuteNonQuery();
        }));
    }

    [Fact]
    public async Task EditingTriggerDayCanClearCommitmentBlock()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await CreateCommitmentDueParticipantAsync(fixture, "edit-clear@example.com", "Edit Eddie");

        var cleared = fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            access,
            5,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-13T09:00:00Z"));

        Assert.Equal("clear", cleared.Commitment.Status);
        Assert.False(cleared.Commitment.BlocksParticipant);
        Assert.Null(cleared.Public.Leaderboard.Single().CommitmentStatus);
    }

    [Fact]
    public async Task CommitmentPaymentReusesOpenInvoiceAndReplacesExpiredInvoice()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await CreateCommitmentDueParticipantAsync(fixture, "pay-flow@example.com", "Pay Paula");

        var firstInvoice = await fixture.Service.CreateCommitmentPaymentInvoiceAsync(access, DateTimeOffset.Parse("2026-06-13T09:01:00Z"));
        Assert.Equal("lmx-invoice-1", firstInvoice.Commitment.InvoiceId);
        Assert.Equal(25m, fixture.Btcpay.CreatedRequests.Single().Amount);
        Assert.Equal("USD", fixture.Btcpay.CreatedRequests.Single().Currency);

        var sameInvoice = await fixture.Service.CreateCommitmentPaymentInvoiceAsync(access, DateTimeOffset.Parse("2026-06-13T09:02:00Z"));
        Assert.Equal("lmx-invoice-1", sameInvoice.Commitment.InvoiceId);
        Assert.Single(fixture.Btcpay.CreatedRequests);

        fixture.Btcpay.LookupResults["lmx-invoice-1"] = BtcpayInvoiceClient.ParseInvoiceJson(
            """
            {
              "status": "Expired",
              "amount": "25",
              "currency": "USD",
              "paidAmount": "0",
              "checkoutLink": "https://btcpay.example.test/i/lmx-invoice-1"
            }
            """);
        var expired = await fixture.Service.RefreshCommitmentPaymentStatusAsync(access, DateTimeOffset.Parse("2026-06-13T09:03:00Z"));
        Assert.Equal("due", expired.Commitment.Status);
        Assert.Equal("Expired", expired.Commitment.InvoiceStatus);

        var replacementInvoice = await fixture.Service.CreateCommitmentPaymentInvoiceAsync(access, DateTimeOffset.Parse("2026-06-13T09:04:00Z"));
        Assert.Equal("lmx-invoice-2", replacementInvoice.Commitment.InvoiceId);
        Assert.Equal(2, fixture.Btcpay.CreatedRequests.Count);

        fixture.Service.StopChallengeEmails(access, DateTimeOffset.Parse("2026-06-13T09:05:00Z"));
        fixture.Btcpay.LookupResults["lmx-invoice-2"] = new BtcpayInvoiceLookupResult(
            true,
            true,
            "Settled",
            null,
            "25",
            "USD",
            "25",
            25m,
            25m,
            "https://btcpay.example.test/i/lmx-invoice-2",
            null,
            null,
            null);

        var paid = await fixture.Service.RefreshCommitmentPaymentStatusAsync(access, DateTimeOffset.Parse("2026-06-13T09:06:00Z"));

        Assert.Equal("clear", paid.Commitment.Status);
        Assert.False(paid.Commitment.BlocksParticipant);
        Assert.True(paid.Participant.ChallengeEmailsStopped);
        Assert.False(paid.Participant.ChallengeInactive);
    }

    [Fact]
    public async Task CommitmentPaymentRequiresFullInvoiceAmountBeforeClearingBlock()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await CreateCommitmentDueParticipantAsync(fixture, "partial-pay@example.com", "Partial Pam");

        await fixture.Service.CreateCommitmentPaymentInvoiceAsync(access, DateTimeOffset.Parse("2026-06-13T09:01:00Z"));
        fixture.Btcpay.LookupResults["lmx-invoice-1"] = BtcpayInvoiceClient.ParseInvoiceJson(
            """
            {
              "status": "Processing",
              "amount": "25",
              "currency": "USD",
              "paidAmount": "10",
              "checkoutLink": "https://btcpay.example.test/i/lmx-invoice-1"
            }
            """);

        var partial = await fixture.Service.RefreshCommitmentPaymentStatusAsync(access, DateTimeOffset.Parse("2026-06-13T09:02:00Z"));

        Assert.Equal("due", partial.Commitment.Status);
        Assert.True(partial.Commitment.BlocksParticipant);

        fixture.Btcpay.LookupResults["lmx-invoice-1"] = BtcpayInvoiceClient.ParseInvoiceJson(
            """
            {
              "status": "Settled",
              "amount": "25",
              "currency": "USD",
              "paidAmount": "10",
              "checkoutLink": "https://btcpay.example.test/i/lmx-invoice-1"
            }
            """);

        var settledPartial = await fixture.Service.RefreshCommitmentPaymentStatusAsync(access, DateTimeOffset.Parse("2026-06-13T09:02:30Z"));

        Assert.Equal("due", settledPartial.Commitment.Status);
        Assert.True(settledPartial.Commitment.BlocksParticipant);

        fixture.Btcpay.LookupResults["lmx-invoice-1"] = BtcpayInvoiceClient.ParseInvoiceJson(
            """
            {
              "status": "Processing",
              "amount": "25",
              "currency": "USD",
              "paidAmount": "25",
              "checkoutLink": "https://btcpay.example.test/i/lmx-invoice-1"
            }
            """);

        var paid = await fixture.Service.RefreshCommitmentPaymentStatusAsync(access, DateTimeOffset.Parse("2026-06-13T09:03:00Z"));

        Assert.Equal("clear", paid.Commitment.Status);
        Assert.False(paid.Commitment.BlocksParticipant);
    }

    [Fact]
    public async Task CommitmentBlocksProfilePictureUploadsUntilResolved()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await CreateCommitmentDueParticipantAsync(fixture, "blocked-upload@example.com", "Blocked Bea");
        using var dueStream = CreatePngStream();
        var dueFile = CreatePngFormFile(dueStream);

        var dueError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UploadParticipantProfilePictureAsync(access, dueFile));
        Assert.Contains("Pay the commitment due", dueError.Message);

        var setupAccess = fixture.InsertConfirmedParticipant("setup-upload@example.com", "Setup Sid", commitmentAmountUsd: null);
        using var setupStream = CreatePngStream();
        var setupFile = CreatePngFormFile(setupStream);

        var setupError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.UploadParticipantProfilePictureAsync(
                setupAccess,
                setupFile,
                nowUtc: DateTimeOffset.Parse("2026-06-22T08:05:00Z")));
        Assert.Contains("Configure your commitment amount", setupError.Message);
    }

    [Fact]
    public async Task CommitmentPaymentReminderStopsAfterTriggerEditWindowCloses()
    {
        using var fixture = TestChallengeFixture.Create();
        var access = await CreateCommitmentDueParticipantAsync(fixture, "reminder-stop@example.com", "Reminder Remy");

        var editableReminder = Assert.Single(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-14T08:05:00Z")));
        Assert.True(editableReminder.IsCommitmentPaymentReminder);
        Assert.Equal(5, editableReminder.CommitmentTriggerChallengeDay);
        Assert.Equal(25m, editableReminder.CommitmentOwedAmountUsd);
        Assert.Equal(8, editableReminder.CommitmentTriggerScore);
        Assert.True(editableReminder.CommitmentThresholdAverage is > 8m and < 9m);
        var emailContent = SmtpLongevitymaxxingEmailSender.BuildDailyReminderEmailContent(
            editableReminder,
            "https://example.test/check-in",
            "https://example.test/stop");
        Assert.Equal("Longevitymaxxing commitment due", emailContent.Subject);
        Assert.Contains("Day 5 scored 8 points.", emailContent.TextBody);
        Assert.Contains("Your recent average was", emailContent.TextBody);
        Assert.Contains("so the commitment is due: USD 25.", emailContent.TextBody);
        Assert.Contains("You can either pay the locked amount, or edit Day 5 while it is still eligible.", emailContent.TextBody);
        Assert.Contains("You also can quit, but you'll still have to live with yourself.", emailContent.TextBody);
        Assert.DoesNotContain("landed below your recent average", emailContent.TextBody);
        Assert.DoesNotContain("for Day 5", emailContent.Subject);

        Assert.Empty(fixture.Service.GetDailyReminderCandidates(DateTimeOffset.Parse("2026-06-15T08:05:00Z")));
        var stillActive = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-15T08:05:30Z"));
        Assert.False(stillActive.Participant.ChallengeEmailsStopped);

        fixture.Service.ApplyDailyReminderStopRules(DateTimeOffset.Parse("2026-06-15T08:05:00Z"));
        var stopped = fixture.Service.GetParticipantState(access, DateTimeOffset.Parse("2026-06-15T08:06:00Z"));
        Assert.False(stopped.Participant.ChallengeEmailsStopped);
        Assert.True(stopped.Participant.ChallengeInactive);
        Assert.False(stopped.Public.Leaderboard.Single().ChallengeEmailsStopped);
        Assert.True(stopped.Public.Leaderboard.Single().ChallengeInactive);
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

    private static void InsertLegacyCheckInAfterResting(
        TestChallengeFixture fixture,
        string accessToken,
        int challengeDay,
        string challengeDate,
        DateTimeOffset checkedInAtUtc)
    {
        fixture.Db.Run(sqlite =>
        {
            using var findParticipant = sqlite.CreateCommand();
            findParticipant.CommandText = "SELECT Id FROM LongevitymaxxingParticipants WHERE AccessToken = @accessToken;";
            findParticipant.Parameters.AddWithValue("@accessToken", accessToken);
            var participantId = findParticipant.ExecuteScalar()?.ToString()
                ?? throw new InvalidOperationException("Test participant not found.");

            using var insert = sqlite.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO LongevitymaxxingCheckIns
                (ParticipantId, ChallengeDay, ChallengeDate, Sleep, Exercise, Nutrition, Vices, Note, CheckedInAtUtc, UpdatedAtUtc)
                VALUES (@participantId, @day, @date, 1, 1, 1, 1, @note, @checked, @updated);
                """;
            insert.Parameters.AddWithValue("@participantId", participantId);
            insert.Parameters.AddWithValue("@day", challengeDay);
            insert.Parameters.AddWithValue("@date", challengeDate);
            insert.Parameters.AddWithValue("@note", "legacy post-rest check-in");
            insert.Parameters.AddWithValue("@checked", checkedInAtUtc.ToString("o"));
            insert.Parameters.AddWithValue("@updated", checkedInAtUtc.ToString("o"));
            insert.ExecuteNonQuery();
        });
    }

    private static async Task<string> CreateCommitmentDueParticipantAsync(TestChallengeFixture fixture, string email, string name)
    {
        var access = await fixture.ConfirmParticipantAsync(email, name);
        SubmitCommitmentBaseline(fixture, access);
        SubmitCommitmentTriggerMiss(fixture, access);
        return access;
    }

    private static void SubmitCommitmentBaseline(TestChallengeFixture fixture, string accessToken)
    {
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            accessToken,
            1,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-09T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            accessToken,
            2,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-10T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            accessToken,
            3,
            2,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-11T08:00:00Z"));
        fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            accessToken,
            4,
            1,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-12T08:00:00Z"));
    }

    private static LongevitymaxxingParticipantState SubmitCommitmentTriggerMiss(TestChallengeFixture fixture, string accessToken)
        => fixture.Service.SubmitCheckIn(new LongevitymaxxingCheckInRequest(
            accessToken,
            5,
            1,
            2,
            2,
            2,
            null), DateTimeOffset.Parse("2026-06-13T08:00:00Z"));

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
            FakeBtcpayInvoiceClient btcpay,
            LongevitymaxxingChallengeService service)
        {
            ContentRoot = root;
            Db = db;
            Email = email;
            Http = http;
            Athletes = athletes;
            Btcpay = btcpay;
            Service = service;
        }

        public string ContentRoot { get; }
        public DatabaseManager Db { get; }
        public FakeEmailSender Email { get; }
        public FakeHttpClientFactory Http { get; }
        public FakeAthleteSnapshotProvider Athletes { get; }
        public FakeBtcpayInvoiceClient Btcpay { get; }
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
            var btcpay = new FakeBtcpayInvoiceClient();
            var env = new FakeEnvironment(root);
            var config = new Config
            {
                EmailFrom = "hi@example.test",
                SmtpServer = "smtp.example.test",
                SmtpPort = 587,
                SmtpUser = "user",
                SmtpPassword = "password",
                BTCPayBaseUrl = "https://btcpay.example.test",
                BTCPayStoreId = "store",
                BTCPayGreenfieldApiKey = "secret",
                LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
                {
                    StartDate = "2026-06-08",
                    PublicBaseUrl = "https://example.test/ignored-path",
                    SignupClosesAtUtc = signupClosesAtUtc ?? "2026-06-08T00:00:00Z",
                    CallSelectionClosesAtUtc = callSelectionClosesAtUtc,
                    DailyReminderHourLocal = 8,
                    SlackInviteUrl = "https://slack.example.test",
                    VideoCallUrl = "https://meet.example.test"
                }
            };
            var service = new LongevitymaxxingChallengeService(
                db,
                config,
                http,
                env,
                email,
                NullLogger<LongevitymaxxingChallengeService>.Instance,
                athletes,
                btcpay);
            return new TestChallengeFixture(root, db, email, http, athletes, btcpay, service);
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
            string? athleteLink = null,
            string timeZoneId = "UTC",
            DateTimeOffset? nowUtc = null)
        {
            var now = nowUtc ?? DateTimeOffset.Parse("2026-06-06T12:00:00Z");
            await Service.SignupAsync(new LongevitymaxxingSignupRequest(email, name, timeZoneId, athleteLink, 25m), now);
            var token = ReadQueryToken(Email.Confirmations.Last().Url, "confirm");
            var access = await Service.ConfirmAsync(token, now.AddMinutes(1));
            return access.AccessToken;
        }

        public string InsertConfirmedParticipant(string email, string name, decimal? commitmentAmountUsd = 25m)
        {
            var now = DateTimeOffset.Parse("2026-06-06T12:00:00Z").ToString("o");
            var accessToken = $"access-{Guid.NewGuid():N}";
            Db.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO LongevitymaxxingParticipants
                    (Id, Email, DisplayName, TimeZoneId, AthleteSlug, AccessToken, ConfirmationToken, StopToken, ConfirmedAtUtc, StoppedEmailsAtUtc, CommitmentAmountUsd, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (@id, @email, @name, 'UTC', NULL, @access, @confirm, @stop, @confirmed, NULL, @commitmentAmount, @created, @updated);
                    """;
                cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@email", email);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@access", accessToken);
                cmd.Parameters.AddWithValue("@confirm", $"confirm-{Guid.NewGuid():N}");
                cmd.Parameters.AddWithValue("@stop", $"stop-{Guid.NewGuid():N}");
                cmd.Parameters.AddWithValue("@confirmed", now);
                cmd.Parameters.AddWithValue("@commitmentAmount", commitmentAmountUsd is null ? DBNull.Value : commitmentAmountUsd.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@created", now);
                cmd.Parameters.AddWithValue("@updated", now);
                cmd.ExecuteNonQuery();
            });

            return accessToken;
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

    private sealed class FakeBtcpayInvoiceClient : IBtcpayInvoiceClient
    {
        private int _createdCount;

        public List<BtcpayInvoiceCreateRequest> CreatedRequests { get; } = [];
        public Dictionary<string, BtcpayInvoiceLookupResult> LookupResults { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<BtcpayInvoiceCreateResult> CreateInvoiceAsync(Config config, BtcpayInvoiceCreateRequest request, CancellationToken ct = default)
        {
            CreatedRequests.Add(request);
            _createdCount++;
            var invoiceId = $"lmx-invoice-{_createdCount}";
            return Task.FromResult(new BtcpayInvoiceCreateResult(
                true,
                $"https://btcpay.example.test/i/{invoiceId}",
                invoiceId,
                null));
        }

        public Task<BtcpayInvoiceLookupResult> GetInvoiceAsync(Config config, string invoiceId, CancellationToken ct = default)
            => Task.FromResult(
                LookupResults.TryGetValue(invoiceId, out var result)
                    ? result
                    : new BtcpayInvoiceLookupResult(
                        true,
                        false,
                        "New",
                        null,
                        "25",
                        "USD",
                        "0",
                        25m,
                        0m,
                        $"https://btcpay.example.test/i/{invoiceId}",
                        null,
                        null,
                        null));
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
