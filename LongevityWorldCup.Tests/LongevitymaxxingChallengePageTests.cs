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

        Assert.Contains("<h1 id=\"lmx-title\">Longevitymaxxing</h1>", html);
        Assert.Contains("Starts soon", html);
        Assert.Contains("id=\"lmxAccessTabs\" class=\"lmx-tabs lmx-access-tabs\" role=\"tablist\"", html);
        Assert.Contains("data-lmx-access-tab=\"signup\"", html);
        Assert.Contains("data-lmx-access-tab=\"signin\"", html);
        Assert.Contains("Sign up", html);
        Assert.Contains("Sign in", html);
        Assert.DoesNotContain("signup for free", html);
        Assert.Contains("<label for=\"lmxSignupName\">Username</label>", html);
        Assert.DoesNotContain("<label for=\"lmxEditName\">Username</label>", html);
        Assert.Contains("id=\"lmxProfileIdentity\"", html);
        Assert.Contains("autocomplete=\"username\"", html);
        Assert.Contains("Are you already a Longevity World Cup athlete?", html);
        Assert.Contains("name=\"lmxSignupIdentity\" value=\"participant\" checked", html);
        Assert.Contains("name=\"lmxSignupIdentity\" value=\"athlete\"", html);
        Assert.DoesNotContain("name=\"lmxEditIdentity\"", html);
        Assert.DoesNotContain("<label for=\"lmxSignupName\">Name</label>", html);
        Assert.Contains("Fell off your habits?", html);
        Assert.Contains("Too busy for a full reset?", html);
        Assert.Contains("Travel, stress, or deadlines?", html);
        Assert.Contains("Perfect plans keep failing?", html);
        Assert.Contains("Agencies to develop", html);
        Assert.Contains("Daily checkins", html);
        Assert.Contains("id=\"lmxQuestionPreview\"", html);
        Assert.Contains("id=\"lmxQuestionPreviewList\"", html);
        Assert.DoesNotContain("Visible momentum", html);
        Assert.DoesNotContain("Gentle accountability", html);
        Assert.Contains("The first muscle to train is your mind.", html);
        Assert.DoesNotContain("Agency &gt; Outcome", html);
        Assert.DoesNotContain("lmxStartChip", html);
        Assert.Contains("Travel compatible", html);
        Assert.Contains("Family compatible", html);
        Assert.Contains("Work compatible", html);
        Assert.Contains("Illness compatible", html);
        Assert.DoesNotContain("Optional weekly community calls", html);
        Assert.DoesNotContain("Children compatible", html);
        Assert.DoesNotContain("Injury compatible", html);
        Assert.DoesNotContain("No perfection required", html);
        Assert.DoesNotContain("Bad days can be logged honestly", html);
        Assert.DoesNotContain("Use this when the basics slipped", html);
        Assert.DoesNotContain("Already joined?", html);
        Assert.Contains("id=\"lmxResendPanel\" class=\"lmx-access-panel lmx-hidden\" role=\"tabpanel\"", html);
        Assert.Contains("<button class=\"lmx-button lmx-primary-action\" type=\"submit\">", html);
        Assert.Contains("<span id=\"lmxResendButtonText\">Send check-in link</span>", html);
        Assert.Contains("Send check-in link", html);
        Assert.DoesNotContain("Need your check-in link?", html);
        Assert.DoesNotContain("participant access", html);
        Assert.DoesNotContain("Already joined or opened this page in a new browser?", html);
        Assert.Contains("Your first check-in email arrives tomorrow morning at 7 AM", html);
        Assert.DoesNotContain("Get your link", html);
        Assert.DoesNotContain("lmx-resend-panel", html);
        Assert.DoesNotContain("daily max", html);
        Assert.Contains("Calls", html);
        Assert.DoesNotContain("peak points/day", html);
        Assert.DoesNotContain("points/day", html);
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
        Assert.Contains("Challenge channel", html);
        Assert.Contains("id=\"lmxParticipantTabs\" class=\"lmx-tabs\" role=\"tablist\"", html);
        Assert.Contains("data-lmx-tab=\"checkin\"", html);
        Assert.Contains("data-lmx-tab=\"profile\"", html);
        Assert.Contains("data-lmx-tab=\"home\"", html);
        Assert.Contains("id=\"lmxHomePanel\"", html);
        Assert.DoesNotContain("id=\"lmxEditToggle\"", html);
        Assert.Contains("<label for=\"lmxSignupAthlete\">Athlete profile</label>", html);
        Assert.Contains("id=\"lmxProfileIdentity\"", html);
        Assert.DoesNotContain("<label for=\"lmxEditAthlete\">Athlete profile</label>", html);
        Assert.DoesNotContain("name=\"lmxEditIdentity\"", html);
        Assert.DoesNotContain("id=\"lmxEditName\"", html);
        Assert.DoesNotContain("id=\"lmxEditAthlete\"", html);
        Assert.DoesNotContain("Athlete profile <span>optional</span>", html);
        Assert.DoesNotContain("LWC athlete profile <span>optional</span>", html);
        Assert.Contains("lmx-athlete-selector", html);
        Assert.Contains("lmx-athlete-picker", html);
        Assert.Contains("Search athlete name", html);
        Assert.Contains("lmxSignupAthleteClear", html);
        Assert.DoesNotContain("lmxEditAthleteSelected", html);
        Assert.DoesNotContain("Choose this only if the participant is already listed as a Longevity athlete.", html);
        Assert.DoesNotContain("Only if you are already listed as an athlete", html);
        Assert.Contains("id=\"lmxProfilePictureField\"", html);
        Assert.Contains("Upload profile picture", html);
        Assert.Contains("id=\"lmxProfilePictureInput\" type=\"file\" accept=\"image/*\"", html);
        Assert.Contains("id=\"lmxSignupTimeZoneLabel\">Timezone</span>", html);
        Assert.Contains("class=\"lmx-timezone-picker\" data-timezone-picker data-select-id=\"lmxSignupTimeZone\"", html);
        Assert.Contains("id=\"lmxSignupTimeZoneSearch\" type=\"search\"", html);
        Assert.Contains("placeholder=\"Search city or timezone\"", html);
        Assert.Contains("id=\"lmxSignupTimeZone\" class=\"lmx-timezone-native\" name=\"timeZoneId\" required", html);
        Assert.Contains("id=\"lmxEditTimeZone\" class=\"lmx-timezone-native\" required", html);
        Assert.DoesNotContain("Extra details", html);
        Assert.DoesNotContain("id=\"lmxSignupDetails\"", html);
        Assert.DoesNotContain("Used for reminder timing and call times.", html);
        Assert.DoesNotContain("Call availability <span>optional</span>", html);
        Assert.DoesNotContain("Available call times are picked automatically before reminders go out.", html);
        Assert.DoesNotContain("Available call times are chosen before reminders go out.", html);
        Assert.DoesNotContain("The best kickoff, midpoint, and finale times", html);
        Assert.Contains("/css/longevitymaxxing.css?v=", html);
        Assert.Contains("/js/longevitymaxxing.js?v=", html);
        Assert.Contains("property=\"og:image\" content=\"https://longevityworldcup.com/og/page/longevitymaxxing.png?v=", html);
        Assert.Contains("name=\"twitter:image\" content=\"https://longevityworldcup.com/og/page/longevitymaxxing.png?v=", html);
        Assert.DoesNotContain("{{ASSET_LONGEVITYMAXXING", html);
    }

    [Fact]
    public async Task LongevitymaxxingCheckInQuotes_AreBucketedAndRenderLinkedSources()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/longevitymaxxing");
        var javascript = await client.GetStringAsync("/js/longevitymaxxing.js");
        var css = await client.GetStringAsync("/css/longevitymaxxing.css");

        Assert.Contains("import(`/js/misc.js?v=", html);
        Assert.Contains("import(`/js/pheno-age.js?v=", html);
        Assert.Contains("import(`/js/bortz-age.js?v=", html);

        Assert.Contains("const LMX_QUOTES = {", javascript);
        Assert.Contains("Sleep quality is immeasurably better", javascript);
        Assert.Contains("The wrong lifestyle decisions I've made were my decisions.", javascript);
        Assert.Contains("function selectQuoteBucket", javascript);
        Assert.Contains("if (values.every(item => item.value === 2)) return null;", javascript);
        Assert.Contains("return worst.length === 1 ? worst[0].key : \"mindset\";", javascript);
        Assert.Contains("const quoteBucket = selectQuoteBucket(draft);", javascript);
        Assert.Contains("if (quoteBucket) void showRandomCheckInQuote(quoteBucket);", javascript);
        Assert.Contains("showCheckInQuoteDialog(quote, null, token);", javascript);
        Assert.Contains("updateCheckInQuoteDialogRank(quote, computeQuoteAthleteBestRank(athlete), token);", javascript);
        Assert.Contains("dialog.dataset.quoteToken !== token", javascript);
        Assert.Contains("function computeQuoteAthleteBestRank", javascript);
        Assert.Contains("leagueType: \"pheno-improvement\"", javascript);
        Assert.Contains("leagueType: \"bortz-improvement\"", javascript);
        Assert.Contains("CROWD_AGE_LEADERBOARD_MINIMUM_GUESS_COUNT", javascript);
        Assert.Contains("href: buildQuoteViewHref(\"improvement\")", javascript);
        Assert.Contains("href: buildQuoteFiltersHref([generation, division])", javascript);
        Assert.Contains("targetBlank: true", javascript);
        Assert.Contains("target=\"_blank\" rel=\"noopener noreferrer\"", javascript);
        Assert.Contains("OK</button>", javascript);

        Assert.Contains("body.lmx-quote-open", css);
        Assert.Contains(".lmx-quote-dialog", css);
        Assert.Contains(".lmx-quote-dialog-card", css);
        Assert.Contains("max-height: min(88vh, 38rem);", css);
        Assert.Contains(".lmx-quote-source", css);
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
        Assert.Contains("\"callSelectionClosesAtUtc\":", json);
        Assert.Contains("\"key\":\"community-", json);
        Assert.Contains("\"label\":\"Community call\"", json);
        Assert.Contains("T06:30:00.0000000+00:00", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-08T13:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-22T06:30:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-22T13:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-22T16:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-09T02:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("kem-kfpt-bhs", json);
        Assert.DoesNotContain("videoCallUrl", json);
    }

    [Fact]
    public async Task LongevitymaxxingPublicState_GeneratesWeeklySundayCommunityCallsForFutureCompetition()
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
        Assert.Contains("\"label\":\"Community call\"", json);
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
        Assert.Contains("Don't feel ready for the Longevity World Cup yet? Try longevitymaxxing first.", html);
        Assert.Contains("Start longevitymaxxing", html);
        Assert.Contains("href=\"/longevitymaxxing\"", html);
        Assert.DoesNotContain("Separate Lifestyle challenge", html);
        Assert.DoesNotContain("does not affect Ultimate League rankings", html);
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
        Assert.Contains("Start longevitymaxxing", html);
        Assert.DoesNotContain("Separate Lifestyle challenge", html);
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
        Assert.Contains("Start longevitymaxxing", html);
        Assert.DoesNotContain("Separate Lifestyle challenge", html);
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
        Assert.Contains("Don't feel ready for the Longevity World Cup yet? Try longevitymaxxing first.", html);
        Assert.Contains("Start longevitymaxxing", html);
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
        Assert.Contains("let accessLoading = !!accessToken;", javascript);
        Assert.Contains("if (accessLoading) renderAccessLoading();", javascript);
        Assert.Contains("toggle(\"lmxAccessLoadingPanel\", isAccessLoading);", javascript);
        Assert.Contains("function renderAccessLoading()", javascript);
        Assert.Contains("accessTab = \"signin\";", javascript);
        Assert.Contains("id=\"lmxAccessLoadingPanel\"", await client.GetStringAsync("/longevitymaxxing"));
        Assert.Contains(".lmx-access-loading", css);
        Assert.Contains("if (isAuthFailure(err))", javascript);
        Assert.Contains("err.status = response.status;", javascript);
        Assert.Contains("function isAuthFailure(err)", javascript);
        Assert.Contains("const activeParticipantTab = hasParticipant ? ensureParticipantTab(participantState) : null;", javascript);
        Assert.Contains("const commitmentBlocked = hasCommitmentBlock(participantState);", javascript);
        Assert.Contains("const checkInOnly = !commitmentBlocked && pendingCheckInDays.length > 0 && activeParticipantTab === \"checkin\";", javascript);
        Assert.Contains("const PARTICIPANT_TABS = [\"checkin\", \"profile\", \"home\"];", javascript);
        Assert.Contains("setParticipantTab(button.dataset.lmxTab, true);", javascript);
        Assert.Contains("return getPendingCheckInDays(state).length ? \"checkin\" : \"home\";", javascript);
        Assert.DoesNotContain("publicClosed", javascript);
        Assert.DoesNotContain("public-board-only", javascript);
        Assert.Contains("const participantGateOnly = commitmentBlocked || checkInOnly;", javascript);
        Assert.Contains("toggle(\"lmxBoardSection\", !participantGateOnly);", javascript);
        Assert.Contains("toggle(\"lmxCommitmentPanel\", hasParticipant && commitmentBlocked);", javascript);
        Assert.Contains("toggle(\"lmxParticipantTools\", hasParticipant && !commitmentBlocked && activeParticipantTab === \"home\");", javascript);
        Assert.Contains("toggle(\"lmxParticipantCalls\", hasParticipant && !commitmentBlocked && activeParticipantTab === \"home\");", javascript);
        Assert.Contains("toggle(\"lmxParticipantKicker\", !!kicker);", javascript);
        Assert.Contains("function isParticipantTabLocked", javascript);
        Assert.Contains("if (isParticipantTabLocked(tab, participantState)) return;", javascript);
        Assert.Contains("button.toggleAttribute(\"disabled\", locked);", javascript);
        Assert.Contains("const availableTabs = PARTICIPANT_TABS.filter(tab => !isParticipantTabLocked(tab, participantState));", javascript);
        Assert.Contains("Next community call", javascript);
        Assert.Contains("data-call-countdown", javascript);
        Assert.Contains("function callCountdownHtml", javascript);
        Assert.Contains("function formatCallCountdown", javascript);
        Assert.Contains("Live now", javascript);
        Assert.Contains("window.setInterval(updateCallCountdowns, 60000)", javascript);
        Assert.Contains(".lmx-call-main", css);
        Assert.Contains(".lmx-call-side", css);
        Assert.Contains(".lmx-call-countdown", css);
        Assert.Contains("function formatCallWhen", javascript);
        Assert.Contains("class=\"lmx-call-when\"", javascript);
        Assert.Contains("#lmxParticipantTools", css);
        Assert.Contains(".lmx-tab:disabled", css);
        Assert.Contains(".lmx-call-when small", css);
        Assert.Contains("const leaderboardRows = splitLeaderboardRows(state);", javascript);
        Assert.Contains("const leaderboard = participant.challengeInactive ? (state.leaderboard || []) : leaderboardRows.active;", javascript);
        Assert.Contains("toggle(\"lmxTitlePanel\", !participantGateOnly);", javascript);
        Assert.Contains("toggle(\"lmxAccessTabs\", !hasParticipant && !isAccessLoading);", javascript);
        Assert.Contains("toggle(\"lmxResendPanel\", !hasParticipant && !isAccessLoading && accessTab === \"signin\");", javascript);
        Assert.Contains("toggle(\"lmxHabitHeading\", !hasParticipant);", javascript);
        Assert.Contains("toggle(\"lmxHabitGrid\", !hasParticipant);", javascript);
        Assert.Contains("toggle(\"lmxQuestionPreview\", !commitmentBlocked && (!hasParticipant || pendingCheckInDays.length > 0));", javascript);
        Assert.Contains("toggle(\"lmxTrack\", hasParticipant && dashboardMode && !participantGateOnly);", javascript);
        Assert.Contains("toggle(\"lmxNotesPanel\", dashboardMode && !participantGateOnly);", javascript);
        Assert.Contains("renderNotes(state.notes || [], false);", javascript);
        Assert.Contains("renderNotes(state.notes || state.public.notes || [], true);", javascript);
        Assert.Contains("No public notes yet.", javascript);
        Assert.Contains("No participant notes yet.", javascript);
        Assert.Contains("placeholder=\"Visible publicly\"", javascript);
        Assert.DoesNotContain("Visible to participants only", javascript);
        Assert.DoesNotContain("<h2>Category dashboard</h2>", javascript);
        Assert.DoesNotContain("<div role=\"columnheader\">Category</div>", javascript);
        Assert.Contains("<span class=\"lmx-mini-label\">your trend</span>", javascript);
        Assert.Contains("participantState.trendGuidance?.text", javascript);
        Assert.Contains("function renderCommitmentPanel", javascript);
        Assert.Contains("function payCommitment", javascript);
        Assert.Contains("commitmentAmountUsd: parseCommitmentAmount", javascript);
        Assert.DoesNotContain("displayName: getIdentityDisplayName(\"edit\")", javascript);
        Assert.DoesNotContain("athleteLink: getIdentityAthletePayload(\"edit\")", javascript);
        Assert.Contains("renderCheckIns(editableDays, \"lmxCommitmentCheckinList\");", javascript);
        Assert.Contains("renderCheckIns(orderedDays, containerId);", javascript);
        Assert.Contains("if (!hasCommitmentBlock(state)) renderCheckIns(state.eligibleDays || []);", javascript);
        Assert.Contains("function renderParticipantNotice", javascript);
        Assert.Contains("setAttribute(\"aria-invalid\", \"true\")", javascript);
        Assert.Contains("Payment confirmed. You're unlocked.", javascript);
        Assert.Contains("Redeem yourself", javascript);
        Assert.Contains(": \"Make a pledge to continue\";", javascript);
        Assert.DoesNotContain("<strong>Set a real stake</strong>", javascript);
        Assert.DoesNotContain("Fall below your recent average and either pay it or stop. Choose an amount that would hurt.", javascript);
        Assert.DoesNotContain("id=\"lmxBlockedCommitmentAmount\"", javascript);
        Assert.Contains("Check again", javascript);
        Assert.Contains("Waiting for payment confirmation...", javascript);
        Assert.Contains("Still waiting. This can take a minute.", javascript);
        Assert.Contains("function startCommitmentPaymentPolling", javascript);
        Assert.Contains("function checkCommitmentPayment", javascript);
        Assert.DoesNotContain("Create invoice", javascript);
        Assert.DoesNotContain("Open invoice", javascript);
        Assert.DoesNotContain("Open BTCPay invoice", javascript);
        Assert.DoesNotContain("Refresh payment", javascript);
        Assert.DoesNotContain("Invoice:", javascript);
        Assert.DoesNotContain("Create a BTCPay invoice. The amount is locked until this commitment is cleared.", javascript);
        Assert.DoesNotContain("lmx-commitment-hint", javascript);
        Assert.DoesNotContain("lmx-payment-link", javascript);
        Assert.DoesNotContain(".lmx-commitment-hint", css);
        Assert.DoesNotContain(".lmx-payment-link", css);
        Assert.Contains("const checkoutWindow = window.open(\"\", \"_blank\", \"noopener\");", javascript);
        Assert.Contains("checkoutWindow.location = checkoutLink;", javascript);
        Assert.Contains("window.location.href = checkoutLink;", javascript);
        Assert.Contains("function sanitizeCommitmentAmountInput", javascript);
        var html = await client.GetStringAsync("/longevitymaxxing");
        Assert.Contains("id=\"lmxSignupCommitmentAmount\"", html);
        Assert.Contains("<span aria-hidden=\"true\">$</span>", html);
        Assert.Contains("placeholder=\"300\"", html);
        Assert.DoesNotContain("placeholder=\"$300\"", html);
        Assert.DoesNotContain("placeholder=\"$300\"", javascript);
        Assert.Contains("id=\"lmxCommitmentPanel\"", await client.GetStringAsync("/longevitymaxxing"));
        Assert.Contains("id=\"lmxParticipantNotice\"", await client.GetStringAsync("/longevitymaxxing"));
        Assert.Contains("class=\"lmx-dashboard-scroll\"", javascript);
        Assert.Contains("class=\"lmx-dashboard-corner\" role=\"columnheader\">Agency", javascript);
        Assert.Contains("--lmx-dashboard-day-columns: repeat(${dayCount}, 2.15rem);", javascript);
        Assert.Contains("overflow-x: auto;", css);
        Assert.DoesNotContain(".lmx-dashboard-scroll {\r\n        overflow-x: visible;", css);
        Assert.Contains("--lmx-dashboard-category-width: 10.75rem;", css);
        Assert.Contains("function scrollDashboardToLatestDay", javascript);
        Assert.Contains("const scroller = document.querySelector(\"#lmxTrack .lmx-dashboard-scroll\");", javascript);
        Assert.Contains("const currentDay = scroller.querySelector(\".lmx-dashboard-row-head .lmx-dashboard-day.today\");", javascript);
        Assert.Contains("const stickyWidth = (stickyColumn?.offsetWidth || 0) + gap;", javascript);
        Assert.Contains("const centered = currentDay.offsetLeft - stickyWidth - ((availableWidth - currentDay.offsetWidth) / 2);", javascript);
        Assert.Contains("dashboardScrollObserver = new ResizeObserver(scrollCurrentDayIntoFocus);", javascript);
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
        Assert.Contains("const leaderboardRows = splitLeaderboardRows(state);", javascript);
        Assert.Contains("emptyBoardRow(dayCount, leaderboardRows.inactive.length)", javascript);
        Assert.Contains("emptyRosterRow(dayCount, leaderboardRows.inactive.length)", javascript);
        Assert.Contains("No active participants", javascript);
        Assert.DoesNotContain("inactive participant${hiddenInactiveCount === 1 ? \" is\" : \"s are\"} hidden", javascript);
        Assert.Contains("id=\"lmxInactiveToggle\" class=\"lmx-inactive-toggle lmx-hidden\"", html);
        Assert.Contains("button.textContent = showInactiveLeaderboard ? \"Hide resting\" : `Show resting (${rows.inactive.length})`;", javascript);
        Assert.Contains(".lmx-inactive-toggle", css);
        Assert.DoesNotContain("fa-users-slash", javascript);
        Assert.Contains("const LEADERBOARD_SCORING_WINDOW_DAYS = 14;", javascript);
        Assert.Contains("function leaderboardScoringWindowDays", javascript);
        Assert.Contains("const visibleDays = cells.length || state.durationDays || 14;", javascript);
        Assert.Contains("`${checkedCells.length}/${scoringWindowDays} days`", javascript);
        Assert.DoesNotContain("emptyBoardRow(dayCount, publicViewer)", javascript);
        Assert.DoesNotContain("emptyRosterRow(dayCount)", javascript);
        Assert.Contains("class=\"lmx-name lmx-sticky-heading\" role=\"columnheader\">Participant", javascript);
        Assert.Contains("class=\"lmx-number lmx-sticky-heading\" role=\"columnheader\">Score", javascript);
        Assert.Contains("aria-label=\"Rank ${rankNumber}\"", javascript);
        Assert.Contains(".lmx-rank", css);
        Assert.Contains("data-day=\"${escAttr(cell.challengeDay)}\"", javascript);
        Assert.Contains("data-day=\"${index + 1}\"", javascript);
        Assert.Contains("function setBoardDayColumns", javascript);
        Assert.Contains("--lmx-day-columns", javascript);
        Assert.Contains("repeat(${count}, 2.55rem)", javascript);
        Assert.Contains("const dayWidthRem = count * 2.55;", javascript);
        Assert.Contains("const gapWidthRem = gapCount * 0.35;", javascript);
        Assert.Contains("var(--lmx-day-columns)", css);
        Assert.Contains("function scrollBoardToLatestDay", javascript);
        Assert.Contains("scroller.scrollLeft = Math.max(0, scroller.scrollWidth - scroller.clientWidth);", javascript);
        Assert.Contains("new ResizeObserver(scrollRight)", javascript);
        Assert.Contains("overflow-x: auto;", css);
        Assert.Contains("width: 100%;", css);
        Assert.Contains("--lmx-sticky-gap-cover: calc(var(--lmx-board-gap) + 0.05rem);", css);
        Assert.DoesNotContain("overflow-x: scroll;", css);
        Assert.DoesNotContain(".lmx-board {\r\n    --lmx-day-columns: repeat(14, 2.55rem);\r\n    --lmx-board-min-width: 61.75rem;\r\n    --lmx-sticky-name-width: 16rem;\r\n    --lmx-sticky-score-width: 4.8rem;\r\n    --lmx-board-gap: 0.35rem;\r\n    --lmx-board-row-padding: 0.35rem;\r\n    --lmx-sticky-gap-cover: calc(var(--lmx-board-gap) + 0.05rem);\r\n    min-width: max(100%, var(--lmx-board-min-width));\r\n    width: max-content;", css);
        Assert.Contains("min-width: max(100%, var(--lmx-board-min-width));", css);
        Assert.Contains("scrollbar-gutter: stable;", css);
        Assert.Contains("scroll-margin-top: 4.5rem;", css);
        Assert.Contains("scroll-margin-top: 7.25rem;", css);
        Assert.Contains("--lmx-sticky-name-width", css);
        Assert.Contains("position: sticky;", css);
        Assert.Contains("left: calc(var(--lmx-sticky-name-width) + var(--lmx-board-gap) - var(--lmx-board-row-padding));", css);
        Assert.Contains(".lmx-board-row > .lmx-name,", css);
        Assert.Contains(".lmx-board-row.lmx-roster-row > .lmx-name,", css);
        Assert.Contains(".lmx-board-row > .lmx-number,", css);
        Assert.Contains(".lmx-board-row:not(.header):nth-child(even) > .lmx-number", css);
        Assert.Contains(".lmx-cell[data-day]::before", css);
        Assert.Contains("content: attr(data-day);", css);
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
        Assert.Contains("function wireAccessTabs()", javascript);
        Assert.Contains("function renderAccessTabs()", javascript);
        Assert.Contains("function handleAccessTabKeydown", javascript);
        Assert.DoesNotContain("Already joined or opened this page in a new browser?", javascript);
        Assert.DoesNotContain("Send participant link", javascript);
        Assert.Contains(".lmx-checkin-card > .lmx-button", css);
        Assert.Contains(".lmx-checkin-switcher", css);
        Assert.Contains("function pickActiveCheckInDay", javascript);
        Assert.Contains("function checkInSwitcherHtml", javascript);
        Assert.Contains("function getPendingCheckInDays", javascript);
        Assert.Contains("formatCheckInDate(day.date)", javascript);
        Assert.Contains(".lmx-dashboard-grid", css);
        Assert.Contains(".lmx-dashboard-scroll", css);
        Assert.Contains("--lmx-dashboard-category-width", css);
        Assert.Contains(".lmx-dashboard-corner,", css);
        Assert.Contains(".lmx-dashboard-category {", css);
        Assert.Contains(".lmx-category-day[data-day]::before", css);
        Assert.Contains(".lmx-category-day.partial", css);
        Assert.Contains("background: linear-gradient(90deg, #bbf7d0 0 50%, #ffffff 50% 100%);", css);
        Assert.DoesNotContain("${value}</span>", javascript);
        Assert.DoesNotContain(".lmx-category-day.partial {\r\n    background: #fde68a;", css);
        Assert.Contains(".lmx-dashboard-stats", css);
        Assert.DoesNotContain("function revealSignupDetailsBeforeSubmit()", javascript);
        Assert.DoesNotContain("Check these once, then join.", javascript);
        Assert.DoesNotContain("signup for free", javascript);
        Assert.DoesNotContain("Join free before", javascript);
        Assert.DoesNotContain("Join free today", javascript);
        Assert.DoesNotContain("toggle(\"lmxHeroCopy\", false);", javascript);
        Assert.DoesNotContain("Agency > Outcome", javascript);
        Assert.Contains("The first muscle to train is your mind.", javascript);
        Assert.DoesNotContain("Use this page for check-ins, standings, scheduled calls, and participant notes.", javascript);
        Assert.DoesNotContain("Signup is open today. Join from the card and start from your private link.", javascript);
        Assert.DoesNotContain("Participants use private email links to check in.", javascript);
        Assert.Contains("function isPreStartSignup", javascript);
        Assert.DoesNotContain("function hasOpenCallVoting", javascript);
        Assert.DoesNotContain("function getOpenCallVoteCalls", javascript);
        Assert.DoesNotContain("renderCallsForSignup", javascript);
        Assert.DoesNotContain("renderCallVoteControls", javascript);
        Assert.Contains("lmx-call-list", await client.GetStringAsync("/longevitymaxxing"));
        Assert.DoesNotContain("lmxEditCallField", javascript);
        Assert.DoesNotContain("id=\"lmxSignupCallField\"", await client.GetStringAsync("/longevitymaxxing"));
        Assert.Contains("Intl.DateTimeFormat().resolvedOptions().timeZone", javascript);
        Assert.Contains("const FALLBACK_TIME_ZONES = [", javascript);
        Assert.Contains("\"America/New_York\"", javascript);
        Assert.Contains("Intl.supportedValuesOf(\"timeZone\")", javascript);
        Assert.Contains("const TIME_ZONE_COUNTRY_DATA = \"Europe/Andorra=AD", javascript);
        Assert.Contains("Europe/Budapest=HU", javascript);
        Assert.Contains("function getAvailableTimeZones", javascript);
        Assert.Contains("function initTimeZonePickers()", javascript);
        Assert.Contains("function renderTimeZoneOptions", javascript);
        Assert.Contains("function chooseTimeZone", javascript);
        Assert.Contains("function timeZoneCountryLabel", javascript);
        Assert.Contains("new Intl.DisplayNames([\"en\"], { type: \"region\" })", javascript);
        Assert.Contains("const TIME_ZONE_MATCH_LIMIT = 10;", javascript);
        Assert.Contains("fillTimeZones(document.getElementById(\"lmxSignupTimeZone\"));", javascript);
        Assert.Contains("fillTimeZones(document.getElementById(\"lmxEditTimeZone\"));", javascript);
        Assert.Contains("initTimeZonePickers();", javascript);
        Assert.Contains("setDefaultTimezone(document.getElementById(\"lmxSignupTimeZone\"));", javascript);
        Assert.Contains(".lmx-timezone-popover", css);
        Assert.Contains(".lmx-timezone-option", css);
        Assert.Contains(".lmx-timezone-search:focus-within", css);
        Assert.Contains(".lmx-field .lmx-timezone-search input:focus", css);
        Assert.DoesNotContain("signupTimeZone.addEventListener(\"change\"", javascript);
        Assert.Contains("editTimeZone.addEventListener(\"change\"", javascript);
        Assert.Contains("renderParticipantCalls(participantState.calls || [], participantState.public.callSelectionClosesAtUtc)", javascript);
        Assert.DoesNotContain("getCallDisplayTimeZone", javascript);
        Assert.Contains("options.timeZone = timeZone", javascript);
        Assert.Contains("initAthleteSelectors();", javascript);
        Assert.Contains("wireIdentityControls();", javascript);
        Assert.Contains("function updateIdentityScope", javascript);
        Assert.Contains("function getIdentityDisplayName", javascript);
        Assert.Contains("function getIdentityAthletePayload", javascript);
        Assert.Contains("Select your athlete profile.", javascript);
        Assert.Contains("function renderProfileIdentity", javascript);
        Assert.Contains("Challenge username", javascript);
        Assert.Contains("Longevity athlete", javascript);
        Assert.Contains(".lmx-profile-identity", css);
        Assert.Contains("fetch(\"/api/data/athletes\")", javascript);
        Assert.Contains("function getAthleteSelectorPayload", javascript);
        Assert.Contains("Select an athlete from the list or clear this field.", javascript);
        Assert.Contains("No listed athlete found", javascript);
        Assert.Contains("DisplayName || a.Name", javascript);
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
        Assert.Contains("Remarks <span>optional</span>", javascript);
        Assert.Contains("Photos <span>optional</span>", javascript);
        Assert.Contains(".lmx-field span.lmx-label span", css);
        Assert.Contains("                Save", javascript);
        Assert.DoesNotContain("Participant note <span>optional</span>", javascript);
        Assert.DoesNotContain("Note photos <span>optional</span>", javascript);
        Assert.DoesNotContain("Save day ${day.challengeDay}", javascript);
        Assert.Contains("accept=\"image/*,.heic,.heif\" multiple data-note-photos", javascript);
        Assert.Contains("function postCheckInWithPhotos", javascript);
        Assert.Contains("formData.append(\"notePhotos\", photo", javascript);
        Assert.Contains("function prepareNotePhotoFile", javascript);
        Assert.Contains("canvas.toBlob(resolve, \"image/webp\", 0.86)", javascript);
        Assert.Contains("postForm(`${API}/check-in`, formData)", javascript);
        Assert.Contains("lmx-athlete-options", css);
        Assert.Contains(".lmx-identity-field", css);
        Assert.Contains(".lmx-identity-options", css);
        Assert.Contains(".lmx-identity-options input", css);
        Assert.Contains("clip-path: inset(50%);", css);
        Assert.Contains(".lmx-identity-options label:has(input:checked)", css);
        Assert.Contains(".lmx-athlete-search", css);
        Assert.Contains(".lmx-athlete-selected", css);
        Assert.Contains(".lmx-athlete-clear", css);
        Assert.Contains(".lmx-athlete-empty", css);
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
        Assert.Contains("Meeting time pending.", javascript);
        Assert.DoesNotContain("Availability closes on", javascript);
        Assert.DoesNotContain("Signup closes on", javascript);
        Assert.Contains("class=\"lmx-call-link\"", javascript);
        Assert.Contains(".lmx-call-link", css);
        Assert.Contains(".lmx-call-link:focus-visible", css);
        Assert.Contains(".lmx-commitment-meta span {", css);
        Assert.Contains("justify-content: center;", css);
        Assert.Contains(".lmx-checkin-switcher button:focus-visible", css);
        Assert.Contains(".lmx-segmented button:focus-visible", css);
        Assert.Contains(".lmx-note-photo-remove:focus-visible", css);
        Assert.Contains(".lmx-tabs", css);
        Assert.Contains(".lmx-tab[aria-selected=\"true\"]", css);
        Assert.DoesNotContain(".lmx-home-status", css);
        Assert.Contains("function renderParticipantTabs", javascript);
        Assert.DoesNotContain("function renderParticipantHome", javascript);
        Assert.DoesNotContain("lmxHomeStatusTitle", await client.GetStringAsync("/longevitymaxxing"));
        Assert.Contains("const daysIn = Math.max(0, Math.trunc(Number(participant.daysIn) || 0));", javascript);
        Assert.Contains("opsTile(\"Days in\", daysIn, \"fa-calendar-check\")", javascript);
        Assert.DoesNotContain("`${row.checkedInDays}/${duration}`", javascript);
        Assert.Contains("class=\"lmx-empty-state compact\"", javascript);
        Assert.Contains(".lmx-empty-state.compact", css);
        Assert.Contains(".lmx-empty-state.compact strong", css);
        Assert.Contains("toggle(\"lmxLifeStrip\", false);", javascript);
        Assert.Contains("titlePanel?.classList.toggle(\"stats-last\", !hasParticipant && dashboardMode);", javascript);
        Assert.Contains(".lmx-title-panel.stats-last .lmx-ops-strip", css);
        Assert.DoesNotContain("function participantStatus", javascript);
        Assert.DoesNotContain("Reminder skips when done", javascript);
        Assert.DoesNotContain("Selected calls are listed below.", javascript);
        Assert.Contains("function renderQuestionPreview", javascript);
        Assert.Contains("Did you set yourself up for good sleep last night?", javascript);
        Assert.Contains("Were your vices under control yesterday?", javascript);
        Assert.DoesNotContain("lmx-answer-preview", javascript);
        Assert.Contains("Practice check-in", javascript);
        Assert.Contains("Counts for checked-in days and streak, not points.", javascript);
        Assert.Contains("last 2 weeks count", javascript);
        Assert.Contains("later days score higher", javascript);
        Assert.Contains("one slip can still score max, never twice in a row", javascript);
        Assert.Contains("function challengeCallCount", javascript);
        Assert.Contains("Math.ceil(dayCount / 7)", javascript);
        Assert.Contains("opsTile(responsiveLabel(\"Calls\", \"Community calls\"), callCount", javascript);
        Assert.Contains("function responsiveLabel(shortLabel, longLabel)", javascript);
        Assert.Contains("lmx-ops-label-short", javascript);
        Assert.Contains("lmx-ops-label-long", javascript);
        Assert.Contains("container-type: inline-size;", css);
        Assert.Contains("@container (min-width: 12.5rem)", css);
        Assert.Contains(".lmx-ops-tile.community-calls .lmx-ops-label-long", css);
        Assert.DoesNotContain("Optional weekly community calls", javascript);
        Assert.Contains("grid-template-columns: repeat(auto-fit, minmax(8.75rem, 1fr));", css);
        Assert.Contains("@media (max-width: 360px)", css);
        Assert.DoesNotContain(".lmx-life-strip span:last-child", css);
        Assert.Contains(".lmx-question-preview-label i", css);
        Assert.Contains(".lmx-athlete-search input:focus", css);
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
