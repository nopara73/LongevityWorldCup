using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LongevitymaxxingChallengePageTests
{
    [Fact]
    public async Task LongevitymaxxingPage_RendersProductCopyAndVersionedAssets()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/longevitymaxxing");

        Assert.Contains("Longevitymaxxing Challenge", html);
        Assert.Contains("Starts soon", html);
        Assert.Contains("Free", html);
        Assert.Contains("Join free", html);
        Assert.Contains("<label for=\"lmxSignupName\">Username</label>", html);
        Assert.Contains("<label for=\"lmxEditName\">Username</label>", html);
        Assert.Contains("autocomplete=\"username\"", html);
        Assert.DoesNotContain("<label for=\"lmxSignupName\">Name</label>", html);
        Assert.Contains("Fell off your habits?", html);
        Assert.Contains("Too busy for a full reset?", html);
        Assert.Contains("Travel, stress, or deadlines?", html);
        Assert.Contains("Perfect plans keep failing?", html);
        Assert.Contains("Habits you'll track", html);
        Assert.DoesNotContain("Visible momentum", html);
        Assert.DoesNotContain("Gentle accountability", html);
        Assert.Contains("get sleep, movement, food, and vices back under control", html);
        Assert.Contains("For busy people.", html);
        Assert.Contains("Travel compatible", html);
        Assert.Contains("Children compatible", html);
        Assert.Contains("Work compatible", html);
        Assert.Contains("Injury compatible", html);
        Assert.DoesNotContain("No perfection required", html);
        Assert.DoesNotContain("Bad days can be logged honestly", html);
        Assert.DoesNotContain("Use this when the basics slipped", html);
        Assert.DoesNotContain("Already joined?", html);
        Assert.Contains("Need your check-in link?", html);
        Assert.Contains("private link", html);
        Assert.Contains("Send check-in link", html);
        Assert.Contains("The first check-in email arrives the morning after Day 1", html);
        Assert.DoesNotContain("Get your link", html);
        Assert.DoesNotContain("daily max", html);
        Assert.Contains("peak points/day", html);
        Assert.Contains("points/day", html);
        Assert.DoesNotContain("Score colors and habit key", html);
        Assert.Contains("aria-label=\"Habit key\"", html);
        Assert.Contains("lmx-habit-key", html);
        Assert.Contains("fa-moon", html);
        Assert.Contains("fa-dumbbell", html);
        Assert.Contains("fa-bowl-food", html);
        Assert.Contains("fa-shield-halved", html);
        Assert.Contains("id=\"lmxSlackInviteLink\"", html);
        Assert.Contains("Join Slack", html);
        Assert.Contains("id=\"lmxSlackRoomLink\"", html);
        Assert.Contains("Slack room", html);
        Assert.Contains("LWC athlete profile <span>optional</span>", html);
        Assert.Contains("lmx-athlete-selector", html);
        Assert.Contains("Only if you are already listed as an athlete", html);
        Assert.Contains("id=\"lmxProfilePictureField\"", html);
        Assert.Contains("Upload profile picture", html);
        Assert.Contains("id=\"lmxProfilePictureInput\" type=\"file\" accept=\"image/*\"", html);
        Assert.Contains("Timezone, profile, and calls", html);
        Assert.Contains("Used for reminder timing and call times.", html);
        Assert.Contains("Call availability <span>optional</span>", html);
        Assert.Contains("/css/longevitymaxxing.css?v=", html);
        Assert.Contains("/js/longevitymaxxing.js?v=", html);
        Assert.Contains("property=\"og:image\" content=\"https://longevityworldcup.com/assets/longevitymaxxing-og.png?v=", html);
        Assert.Contains("name=\"twitter:image\" content=\"https://longevityworldcup.com/assets/longevitymaxxing-og.png?v=", html);
        Assert.DoesNotContain("{{ASSET_LONGEVITYMAXXING", html);
    }

    [Fact]
    public async Task LongevitymaxxingPublicState_DoesNotExposeParticipantOnlyMeetingLink()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/api/longevitymaxxing/state");

        Assert.Contains("\"challengeName\":\"Longevitymaxxing Challenge\"", json);
        Assert.Contains("\"startDate\":\"2026-06-08\"", json);
        Assert.Contains("\"signupClosesAtUtc\":\"2026-06-09T22:00:00.0000000+00:00\"", json);
        Assert.Contains("\"callSelectionClosesAtUtc\":\"2026-06-07T06:30:00.0000000+00:00\"", json);
        Assert.Contains("\"startsAtUtc\":\"2026-06-08T13:00:00.0000000+00:00\"", json);
        Assert.Contains("\"startsAtUtc\":\"2026-06-15T06:30:00.0000000+00:00\"", json);
        Assert.Contains("\"startsAtUtc\":\"2026-06-21T06:30:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-07T06:30:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-22T06:30:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-22T13:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-22T16:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-09T02:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("kem-kfpt-bhs", json);
        Assert.DoesNotContain("videoCallUrl", json);
    }

    [Fact]
    public async Task LongevitymaxxingPublicState_RegeneratesBuiltInCallDefaultsOnSundaysForFutureCompetition()
    {
        using var factory = CreateFactory(new Config
        {
            LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
            {
                StartDate = "2099-06-08",
                SignupClosesAtUtc = "2099-06-09T22:00:00Z"
            }
        });
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/api/longevitymaxxing/state");

        Assert.Contains("\"startDate\":\"2099-06-08\"", json);
        Assert.Contains("\"startsAtUtc\":\"2099-06-07T06:30:00.0000000+00:00\"", json);
        Assert.Contains("\"startsAtUtc\":\"2099-06-14T06:30:00.0000000+00:00\"", json);
        Assert.Contains("\"startsAtUtc\":\"2099-06-21T06:30:00.0000000+00:00\"", json);
        Assert.DoesNotContain("T13:00:00.0000000+00:00", json);
        Assert.DoesNotContain("T16:00:00.0000000+00:00", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-08T06:30:00.0000000+00:00\"", json);
    }

    [Fact]
    public async Task Homepage_AdvertisesLongevitymaxxingChallengeWhileSignupIsOpen()
    {
        using var factory = CreateFactory(new Config
        {
            LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
            {
                StartDate = "2099-06-08",
                SignupClosesAtUtc = "2099-06-08T00:00:00Z"
            }
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("id=\"longevitymaxxingPromo\"", html);
        Assert.Contains("Separate Lifestyle challenge", html);
        Assert.Contains("does not affect Ultimate League rankings", html);
        Assert.Contains("href=\"/longevitymaxxing\"", html);
        Assert.DoesNotContain("/api/longevitymaxxing/state", html);
    }

    [Fact]
    public async Task Homepage_AdvertisesLongevitymaxxingChallengeWhileActiveSignupIsOpen()
    {
        var now = DateTimeOffset.UtcNow;
        using var factory = CreateFactory(new Config
        {
            LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
            {
                StartDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1).ToString("yyyy-MM-dd"),
                SignupClosesAtUtc = now.AddDays(1).ToString("O")
            }
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("id=\"longevitymaxxingPromo\"", html);
        Assert.Contains("href=\"/longevitymaxxing\"", html);
    }

    [Fact]
    public async Task Homepage_AdvertisesLongevitymaxxingChallengeBeforeStartAfterConfiguredSignupClose()
    {
        var now = DateTimeOffset.UtcNow;
        using var factory = CreateFactory(new Config
        {
            LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
            {
                StartDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(10).ToString("yyyy-MM-dd"),
                SignupClosesAtUtc = now.AddDays(-1).ToString("O")
            }
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");
        var json = await client.GetStringAsync("/api/longevitymaxxing/state");

        Assert.Contains("id=\"longevitymaxxingPromo\"", html);
        Assert.Contains("href=\"/longevitymaxxing\"", html);
        Assert.Contains("\"phase\":\"signup\"", json);
        Assert.Contains("\"signupOpen\":true", json);
    }

    [Fact]
    public async Task Homepage_AdvertisesLongevitymaxxingChallengeAfterOriginalSignupClose()
    {
        using var factory = CreateFactory(new Config
        {
            LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
            {
                StartDate = "2000-06-08",
                SignupClosesAtUtc = "2000-06-08T00:00:00Z"
            }
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("id=\"longevitymaxxingPromo\"", html);
        Assert.Contains("Separate Lifestyle challenge", html);
        Assert.Contains("does not affect Ultimate League rankings", html);
    }

    [Fact]
    public async Task LongevitymaxxingScript_KeepsSignupLeaderboardVisibleAndFocusesDueCheckIn()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/longevitymaxxing.js");
        var css = await client.GetStringAsync("/css/longevitymaxxing.css");

        Assert.Contains("const pendingCheckInDays = hasParticipant ? getPendingCheckInDays(participantState) : [];", javascript);
        Assert.Contains("if (isAuthFailure(err))", javascript);
        Assert.Contains("err.status = response.status;", javascript);
        Assert.Contains("function isAuthFailure(err)", javascript);
        Assert.Contains("const checkInOnly = pendingCheckInDays.length > 0;", javascript);
        Assert.DoesNotContain("publicClosed", javascript);
        Assert.DoesNotContain("public-board-only", javascript);
        Assert.Contains("toggle(\"lmxBoardSection\", !checkInOnly);", javascript);
        Assert.Contains("toggle(\"lmxParticipantTools\", !checkInOnly);", javascript);
        Assert.Contains("toggle(\"lmxTitlePanel\", !checkInOnly);", javascript);
        Assert.Contains("toggle(\"lmxResendPanel\", !hasParticipant);", javascript);
        Assert.Contains("toggle(\"lmxHabitHeading\", !hasParticipant);", javascript);
        Assert.Contains("toggle(\"lmxHabitGrid\", !hasParticipant);", javascript);
        Assert.Contains("toggle(\"lmxTrack\", hasParticipant && dashboardMode && !checkInOnly);", javascript);
        Assert.Contains("toggle(\"lmxNotesPanel\", dashboardMode && !checkInOnly);", javascript);
        Assert.Contains("renderNotes(state.notes || []);", javascript);
        Assert.Contains("renderNotes(state.notes || state.public.notes || []);", javascript);
        Assert.Contains("placeholder=\"Visible publicly\"", javascript);
        Assert.DoesNotContain("Visible to participants only", javascript);
        Assert.DoesNotContain("<h2>Category dashboard</h2>", javascript);
        Assert.DoesNotContain("<div role=\"columnheader\">Category</div>", javascript);
        Assert.Contains("<span class=\"lmx-mini-label\">your trend</span>", javascript);
        Assert.Contains("function normalizeDashboardCells", javascript);
        Assert.Contains("function categoryDashboardRow", javascript);
        Assert.Contains("function categoryDayCell", javascript);
        Assert.Contains("function clampHabitValue", javascript);
        Assert.Contains("Locked-in days", javascript);
        Assert.Contains("const fullDays = checkedCells.filter(cell => isLockedInDay(cell, categories)).length;", javascript);
        Assert.Contains("function isLockedInDay", javascript);
        Assert.DoesNotContain("lockedInDetail", javascript);
        Assert.DoesNotContain("scored days", javascript);
        Assert.DoesNotContain("scoredFullDays", javascript);
        Assert.Contains("dashboardStat(\"Locked-in days\", String(fullDays), \"\", \"fa-calendar-check\")", javascript);
        Assert.Contains("dashboardStat(\"Points\", scoredCells.length ? String(totalPoints) : \"-\", \"\", \"fa-chart-line\")", javascript);
        Assert.Contains("row.totalPoints", javascript);
        Assert.Contains("board.className = publicViewer ? \"lmx-board public\" : \"lmx-board\";", javascript);
        Assert.Contains("lmx-cell-strip", javascript);
        Assert.Contains("const visibleDays = cells.length || state.durationDays || 14;", javascript);
        Assert.Contains("`${checkedCells.length}/${visibleDays} days`", javascript);
        Assert.Contains("emptyBoardRow(dayCount, publicViewer)", javascript);
        Assert.Contains("emptyRosterRow(dayCount)", javascript);
        Assert.Contains("class=\"lmx-name lmx-sticky-heading\" role=\"columnheader\">Participant", javascript);
        Assert.Contains("class=\"lmx-number lmx-sticky-heading\" role=\"columnheader\">Score", javascript);
        Assert.Contains("function setBoardDayColumns", javascript);
        Assert.Contains("--lmx-day-columns", javascript);
        Assert.Contains("var(--lmx-day-columns)", css);
        Assert.Contains("function scrollBoardToLatestDay", javascript);
        Assert.Contains("scroller.scrollLeft = Math.max(0, scroller.scrollWidth - scroller.clientWidth);", javascript);
        Assert.Contains("new ResizeObserver(scrollRight)", javascript);
        Assert.Contains("scrollbar-gutter: stable;", css);
        Assert.Contains("--lmx-sticky-name-width", css);
        Assert.Contains("position: sticky;", css);
        Assert.Contains("left: calc(var(--lmx-sticky-name-width) + var(--lmx-board-gap) - var(--lmx-board-row-padding));", css);
        Assert.DoesNotContain("S/E/N/V dots show habit gaps", javascript);
        Assert.Contains("function scoredDayCellHtml", javascript);
        Assert.Contains("function practiceDayCellHtml", javascript);
        Assert.Contains("lmx-cell lmx-cell-breakdown practice", javascript);
        Assert.Contains("fa fa-rocket", javascript);
        Assert.Contains("function habitBreakdown", javascript);
        Assert.Contains("function habitCellTitle", javascript);
        Assert.DoesNotContain("data-label=\"Days\"", javascript);
        Assert.DoesNotContain("data-label=\"Streak\"", javascript);
        Assert.DoesNotContain("lmx-badge", javascript);
        Assert.DoesNotContain(".lmx-badge", css);
        Assert.DoesNotContain("Final leaderboard", javascript);
        Assert.DoesNotContain("The challenge is complete. The final board is archived below.", javascript);
        Assert.DoesNotContain("Check-ins are closed.", javascript);
        Assert.DoesNotContain("case \"completed\"", javascript);
        Assert.DoesNotContain("Signup is paused", javascript);
        Assert.DoesNotContain("signup paused", javascript);
        Assert.DoesNotContain("lmxClosedPanel", javascript);
        Assert.DoesNotContain("lmx-closed-panel", css);
        Assert.DoesNotContain("renderPodium", javascript);
        Assert.DoesNotContain("lmxPodium", javascript);
        Assert.DoesNotContain("lmx-podium", css);
        Assert.DoesNotContain("id=\"lmxPodium\"", await client.GetStringAsync("/longevitymaxxing"));
        Assert.Contains("Already joined or opened this page in a new browser?", javascript);
        Assert.DoesNotContain("Send participant link", javascript);
        Assert.Contains(".lmx-checkin-card > .lmx-button", css);
        Assert.Contains(".lmx-checkin-switcher", css);
        Assert.Contains("function pickActiveCheckInDay", javascript);
        Assert.Contains("function checkInSwitcherHtml", javascript);
        Assert.Contains("function getPendingCheckInDays", javascript);
        Assert.Contains("formatCheckInDate(day.date)", javascript);
        Assert.Contains(".lmx-dashboard-grid", css);
        Assert.Contains(".lmx-category-day.partial", css);
        Assert.Contains("background: linear-gradient(90deg, #bbf7d0 0 50%, #ffffff 50% 100%);", css);
        Assert.DoesNotContain("${value}</span>", javascript);
        Assert.DoesNotContain(".lmx-category-day.partial {\r\n    background: #fde68a;", css);
        Assert.Contains(".lmx-dashboard-stats", css);
        Assert.Contains("function revealSignupDetailsBeforeSubmit()", javascript);
        Assert.Contains("setStatus(\"lmxSignupStatus\", \"Check these once, then join.\", false);", javascript);
        Assert.Contains("free signup", javascript);
        Assert.Contains("Join free before", javascript);
        Assert.Contains("Join free today", javascript);
        Assert.Contains("Signup is open today. Join from the card and start from your private link.", javascript);
        Assert.Contains("function isPreStartSignup", javascript);
        Assert.Contains("function hasOpenCallVoting", javascript);
        Assert.Contains("function getOpenCallVoteCalls", javascript);
        Assert.Contains("renderCallsForSignup(state);", javascript);
        Assert.Contains("if (publicState) renderCallsForSignup(publicState);", javascript);
        Assert.Contains("Timezone and profile", javascript);
        Assert.Contains("hasParticipant && hasOpenCallVoting(state) && !checkInOnly", javascript);
        Assert.Contains("id=\"lmxSignupCallField\"", await client.GetStringAsync("/longevitymaxxing"));
        Assert.Contains("Intl.DateTimeFormat().resolvedOptions().timeZone", javascript);
        Assert.Contains("const COMMON_TIME_ZONES = [", javascript);
        Assert.Contains("\"America/New_York\"", javascript);
        Assert.DoesNotContain("supportedValuesOf(\"timeZone\")", javascript);
        Assert.Contains("fillTimeZones(document.getElementById(\"lmxSignupTimeZone\"));", javascript);
        Assert.Contains("fillTimeZones(document.getElementById(\"lmxEditTimeZone\"));", javascript);
        Assert.Contains("setDefaultTimezone(document.getElementById(\"lmxSignupTimeZone\"));", javascript);
        Assert.Contains("signupTimeZone.addEventListener(\"change\"", javascript);
        Assert.Contains("editTimeZone.addEventListener(\"change\"", javascript);
        Assert.Contains("formatDateTime(slot.startsAtUtc, getCallDisplayTimeZone(containerId))", javascript);
        Assert.Contains("options.timeZone = timeZone", javascript);
        Assert.Contains("initAthleteSelectors();", javascript);
        Assert.Contains("fetch(\"/api/data/athletes\")", javascript);
        Assert.Contains("function getAthleteSelectorPayload", javascript);
        Assert.Contains("ATHLETE_PLACEHOLDER_IMAGE = \"/assets/content-images/headshot.webp\"", javascript);
        Assert.Contains("function participantNameHtml", javascript);
        Assert.Contains("findAthleteForParticipant(row)", javascript);
        Assert.Contains("function isPlaceholderProfileImage", javascript);
        Assert.Contains("const athleteProfileImage = isPlaceholderProfileImage(athlete?.profilePic) ? \"\" : (athlete?.profilePic || \"\");", javascript);
        Assert.Contains("const image = athleteProfileImage || profileImage || ATHLETE_PLACEHOLDER_IMAGE;", javascript);
        Assert.Contains("function renderProfilePictureControls", javascript);
        Assert.Contains("participant.profileImageUrl", javascript);
        Assert.Contains("row.profileImageUrl", javascript);
        Assert.Contains("postForm(`${API}/profile-picture`, formData)", javascript);
        Assert.Contains("function prepareProfilePictureFile", javascript);
        Assert.Contains("canvas.toBlob(resolve, \"image/jpeg\", 0.88)", javascript);
        Assert.Contains("formData.append(\"profilePicture\", uploadFile, uploadFile.name || \"profile-picture.jpg\");", javascript);
        Assert.DoesNotContain("Profile picture must be 8 MB or smaller.", javascript);
        Assert.Contains("const MAX_NOTE_PHOTOS = 4;", javascript);
        Assert.Contains("Note photos <span>optional</span>", javascript);
        Assert.Contains("accept=\"image/*,.heic,.heif\" multiple data-note-photos", javascript);
        Assert.Contains("function postCheckInWithPhotos", javascript);
        Assert.Contains("formData.append(\"notePhotos\", photo", javascript);
        Assert.Contains("function prepareNotePhotoFile", javascript);
        Assert.Contains("canvas.toBlob(resolve, \"image/webp\", 0.86)", javascript);
        Assert.Contains("postForm(`${API}/check-in`, formData)", javascript);
        Assert.Contains("lmx-athlete-options", css);
        Assert.Contains(".lmx-athlete-option.autocomplete-active", css);
        Assert.Contains(".lmx-participant-avatar", css);
        Assert.Contains(".lmx-participant-avatar.placeholder img", css);
        Assert.Contains(".lmx-profile-upload", css);
        Assert.Contains(".lmx-profile-preview.placeholder img", css);
        Assert.Contains(".lmx-note-photo-grid", css);
        Assert.Contains(".lmx-note-photo img", css);
        Assert.Contains("object-fit: contain", css);
        Assert.Contains("white-space: pre-wrap;", css);
        Assert.Contains("lmxSlackInviteLink", javascript);
        Assert.Contains("lmxSlackRoomLink", javascript);
        Assert.Contains("state.slackRoomUrl", javascript);
        Assert.Contains("weekday: \"long\"", javascript);
        Assert.Contains("weekday: \"short\"", javascript);
        Assert.DoesNotContain("setText(\"lmxBoardTitle\", \"Starting grid\");", javascript);
        Assert.Contains("function renderRosterBoard", javascript);
        Assert.Contains("people signed up", javascript);
        Assert.DoesNotContain("email confirmed", javascript);
        Assert.Contains("renderParticipantCalls(state.calls || [], state.public.callSelectionClosesAtUtc);", javascript);
        Assert.Contains("function pendingCallTimeLabel", javascript);
        Assert.Contains("Meeting time pending. Availability closes on", javascript);
        Assert.DoesNotContain("Signup closes on", javascript);
        Assert.Contains("class=\"lmx-call-link\"", javascript);
        Assert.Contains(".lmx-call-link", css);
        Assert.Contains("Your first check-in email arrives the morning after Day 1. Nothing is due before then.", javascript);
        Assert.Contains("Practice check-in", javascript);
        Assert.Contains("Counts for checked-in days and streak, not points.", javascript);
        Assert.Contains("later days score higher", javascript);
        Assert.Contains("one slip can still score max, never twice in a row", javascript);
        Assert.Contains("Peak points/day", javascript);
        Assert.Contains("cell.countsForScore === false", javascript);
        Assert.Contains(".lmx-cell.practice", css);
        Assert.Contains(".lmx-practice-note", css);
    }

    private static WebApplicationFactory<Program> CreateFactory(Config? config = null)
    {
        return new TestWebApplicationFactory(builder =>
        {
            if (config is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<Config>();
                    services.AddSingleton(config);
                });
            }
        });
    }
}
