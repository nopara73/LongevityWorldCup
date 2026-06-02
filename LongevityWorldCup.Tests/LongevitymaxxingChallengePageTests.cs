using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Mvc.Testing;
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
        Assert.DoesNotContain("Get your link", html);
        Assert.DoesNotContain("daily max", html);
        Assert.Contains("points/day", html);
        Assert.Contains("LWC athlete profile <span>optional</span>", html);
        Assert.Contains("Only if you are already listed as an athlete", html);
        Assert.Contains("Timezone, profile, and calls", html);
        Assert.Contains("Call availability <span>optional</span>", html);
        Assert.Contains("/css/longevitymaxxing.css?v=", html);
        Assert.Contains("/js/longevitymaxxing.js?v=", html);
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
        Assert.Contains("\"startsAtUtc\":\"2026-06-08T06:30:00.0000000+00:00\"", json);
        Assert.Contains("\"startsAtUtc\":\"2026-06-08T13:00:00.0000000+00:00\"", json);
        Assert.Contains("\"startsAtUtc\":\"2026-06-08T16:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-09T02:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("kem-kfpt-bhs", json);
        Assert.DoesNotContain("videoCallUrl", json);
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
        Assert.Contains("toggle(\"lmxResendPanel\", publicClosed);", javascript);
        Assert.Contains("toggle(\"lmxTrack\", hasParticipant && dashboardMode && !checkInOnly);", javascript);
        Assert.Contains("board.className = publicViewer ? \"lmx-board public\" : \"lmx-board\";", javascript);
        Assert.Contains("lmx-cell-strip", javascript);
        Assert.Contains("data-label=\"Days\"", javascript);
        Assert.Contains("Need participant access?", javascript);
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
        Assert.Contains("Intl.DateTimeFormat().resolvedOptions().timeZone", javascript);
        Assert.Contains("fillTimeZones(document.getElementById(\"lmxSignupTimeZone\"));", javascript);
        Assert.Contains("fillTimeZones(document.getElementById(\"lmxEditTimeZone\"));", javascript);
        Assert.Contains("setDefaultTimezone(document.getElementById(\"lmxSignupTimeZone\"));", javascript);
        Assert.Contains("weekday: \"long\"", javascript);
        Assert.Contains("weekday: \"short\"", javascript);
        Assert.DoesNotContain("setText(\"lmxBoardTitle\", \"Starting grid\");", javascript);
        Assert.Contains("function renderRosterBoard", javascript);
        Assert.Contains("people signed up", javascript);
        Assert.DoesNotContain("email confirmed", javascript);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("EnableScheduledJobs", "false");
                builder.UseSetting("EnableStartupBadgeRefresh", "false");
            });
    }
}
