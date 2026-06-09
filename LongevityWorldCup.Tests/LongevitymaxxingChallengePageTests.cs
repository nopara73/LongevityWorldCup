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
        Assert.Contains("private check-in link", html);
        Assert.Contains("Send check-in link", html);
        Assert.Contains("The first check-in email arrives the morning after Day 1", html);
        Assert.DoesNotContain("Get your link", html);
        Assert.DoesNotContain("daily max", html);
        Assert.Contains("peak points/day", html);
        Assert.Contains("points/day", html);
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
        Assert.Contains("\"startsAtUtc\":\"2026-06-08T06:30:00.0000000+00:00\"", json);
        Assert.Contains("\"startsAtUtc\":\"2026-06-08T13:00:00.0000000+00:00\"", json);
        Assert.Contains("\"startsAtUtc\":\"2026-06-08T16:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-09T02:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("kem-kfpt-bhs", json);
        Assert.DoesNotContain("videoCallUrl", json);
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
        Assert.Contains("/api/longevitymaxxing/state", html);
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
    public async Task Homepage_HidesLongevitymaxxingPromoAfterSignupCloses()
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

        Assert.DoesNotContain("id=\"longevitymaxxingPromo\"", html);
        Assert.DoesNotContain("Separate Lifestyle challenge", html);
        Assert.DoesNotContain("does not affect Ultimate League rankings", html);
    }

    [Fact]
    public async Task LongevitymaxxingScript_KeepsSignupLeaderboardVisibleAndFocusesDueCheckIn()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/longevitymaxxing.js");
        var css = await client.GetStringAsync("/css/longevitymaxxing.css");

        Assert.Contains("const pendingCheckInDays = hasParticipant ? getPendingCheckInDays(participantState) : [];", javascript);
        Assert.Contains("const checkInOnly = pendingCheckInDays.length > 0;", javascript);
        Assert.Contains("const publicClosed = !hasParticipant && !state.signupOpen;", javascript);
        Assert.Contains("hero.classList.toggle(\"public-board-only\", publicClosed);", javascript);
        Assert.Contains("toggle(\"lmxBoardSection\", !checkInOnly);", javascript);
        Assert.Contains("toggle(\"lmxParticipantTools\", !checkInOnly);", javascript);
        Assert.Contains("toggle(\"lmxTitlePanel\", !checkInOnly && !publicClosed);", javascript);
        Assert.Contains("toggle(\"lmxResendPanel\", !hasParticipant);", javascript);
        Assert.Contains("toggle(\"lmxTrack\", hasParticipant && dashboardMode && !checkInOnly);", javascript);
        Assert.Contains("board.className = publicViewer ? \"lmx-board public\" : \"lmx-board\";", javascript);
        Assert.Contains("lmx-cell-strip", javascript);
        Assert.Contains("data-label=\"Days\"", javascript);
        Assert.Contains("Need participant access?", javascript);
        Assert.Contains("Already joined or opened this page in a new browser?", javascript);
        Assert.Contains("Send participant link", javascript);
        Assert.Contains(".lmx-checkin-card > .lmx-button", css);
        Assert.Contains(".lmx-checkin-switcher", css);
        Assert.Contains("function pickActiveCheckInDay", javascript);
        Assert.Contains("function checkInSwitcherHtml", javascript);
        Assert.Contains("function getPendingCheckInDays", javascript);
        Assert.Contains("formatCheckInDate(day.date)", javascript);
        Assert.Contains("function revealSignupDetailsBeforeSubmit()", javascript);
        Assert.Contains("setStatus(\"lmxSignupStatus\", \"Check these once, then join.\", false);", javascript);
        Assert.Contains("free signup", javascript);
        Assert.Contains("Join free before", javascript);
        Assert.Contains("Join free today", javascript);
        Assert.Contains("Signup is open today. Join from the card and catch up from your private link.", javascript);
        Assert.Contains("function isPreStartSignup", javascript);
        Assert.Contains("function hasOpenCallVoting", javascript);
        Assert.Contains("function getOpenCallVoteCalls", javascript);
        Assert.Contains("renderCallsForSignup(state);", javascript);
        Assert.Contains("Timezone and profile", javascript);
        Assert.Contains("state.signupOpen && hasOpenCallVoting(state)", javascript);
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
        Assert.Contains("lmx-athlete-options", css);
        Assert.Contains(".lmx-athlete-option.autocomplete-active", css);
        Assert.Contains(".lmx-participant-avatar", css);
        Assert.Contains(".lmx-participant-avatar.placeholder img", css);
        Assert.Contains(".lmx-profile-upload", css);
        Assert.Contains(".lmx-profile-preview.placeholder img", css);
        Assert.Contains("lmxSlackInviteLink", javascript);
        Assert.Contains("lmxSlackRoomLink", javascript);
        Assert.Contains("state.slackRoomUrl", javascript);
        Assert.Contains("weekday: \"long\"", javascript);
        Assert.Contains("weekday: \"short\"", javascript);
        Assert.DoesNotContain("setText(\"lmxBoardTitle\", \"Starting grid\");", javascript);
        Assert.Contains("function renderRosterBoard", javascript);
        Assert.Contains("people signed up", javascript);
        Assert.DoesNotContain("email confirmed", javascript);
        Assert.Contains("renderParticipantCalls(state.calls || [], state.public.signupClosesAtUtc);", javascript);
        Assert.Contains("function pendingCallTimeLabel", javascript);
        Assert.Contains("Meeting time pending. Signup closes on", javascript);
        Assert.Contains("class=\"lmx-call-link\"", javascript);
        Assert.Contains(".lmx-call-link", css);
        Assert.Contains("Your first check-in email arrives the morning after Day 1. Nothing is due before then.", javascript);
        Assert.Contains("Practice check-in", javascript);
        Assert.Contains("Counts for checked-in days and streak, not points.", javascript);
        Assert.Contains("later days ramp", javascript);
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
