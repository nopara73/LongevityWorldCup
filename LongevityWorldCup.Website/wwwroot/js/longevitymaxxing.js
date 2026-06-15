(function () {
    const STORAGE_KEY = "lmxAccessToken";
    const API = "/api/longevitymaxxing";
    const CALL_ACTIVE_WINDOW_MS = 90 * 60 * 1000;
    const ANSWERS = [
        { label: "No", value: 0 },
        { label: "Somewhat", value: 1 },
        { label: "Yes", value: 2 }
    ];
    const SAVED_CHECKIN_TEXT = "Saved. You can edit this check-in today.";
    const MAX_NOTE_PHOTOS = 4;
    const NOTE_PHOTO_MAX_DIMENSION = 1600;
    const QUESTIONS = [
        { key: "sleep", icon: "fa-moon", title: "Sleep", text: "Did you set yourself up for good sleep last night?" },
        { key: "exercise", icon: "fa-dumbbell", title: "Exercise", text: "Did you challenge or intentionally rest your body yesterday?" },
        { key: "nutrition", icon: "fa-bowl-food", title: "Nutrition", text: "By your own standards, did you eat healthy yesterday?" },
        { key: "vices", icon: "fa-shield-halved", title: "Vices", text: "Were your vices under control yesterday?" }
    ];
    const ATHLETE_PLACEHOLDER_IMAGE = "/assets/content-images/headshot.webp";
    const COMMON_TIME_ZONES = [
        "UTC",
        "Europe/London",
        "Europe/Berlin",
        "Europe/Budapest",
        "Europe/Athens",
        "Asia/Jerusalem",
        "Asia/Dubai",
        "Asia/Kolkata",
        "Asia/Bangkok",
        "Asia/Singapore",
        "Asia/Tokyo",
        "Australia/Sydney",
        "Pacific/Auckland",
        "America/St_Johns",
        "America/Halifax",
        "America/New_York",
        "America/Chicago",
        "America/Denver",
        "America/Los_Angeles",
        "America/Anchorage",
        "Pacific/Honolulu",
        "America/Mexico_City",
        "America/Bogota",
        "America/Lima",
        "America/Sao_Paulo",
        "America/Argentina/Buenos_Aires"
    ];

    let publicState = null;
    let participantState = null;
    let accessToken = safeStorageGet(STORAGE_KEY);
    let signupSubmitted = false;
    let signupDetailsPrompted = false;
    let selectedCheckInDay = null;
    const savedDays = new Set();
    const pendingNotePhotos = new Map();
    const pendingNotePhotoUrls = new Map();
    const athleteSelectors = new Map();
    let athleteDirectory = [];
    let athleteDirectoryPromise = null;

    document.addEventListener("DOMContentLoaded", init);

    async function init() {
        fillTimeZones(document.getElementById("lmxSignupTimeZone"));
        fillTimeZones(document.getElementById("lmxEditTimeZone"));
        wireForms();
        initAthleteSelectors();

        try {
            await consumeUrlTokens();
            await refreshState();
        } catch (err) {
            setStatus("lmxSignupStatus", messageOf(err), true);
            await refreshPublicOnly();
        }
    }

    function wireForms() {
        const signupForm = document.getElementById("lmxSignupForm");
        const resendForm = document.getElementById("lmxResendForm");
        const editForm = document.getElementById("lmxEditForm");
        const editToggle = document.getElementById("lmxEditToggle");
        const signupAgain = document.getElementById("lmxSignupAgain");
        const profilePictureInput = document.getElementById("lmxProfilePictureInput");
        const profilePictureButton = document.getElementById("lmxProfilePictureButton");
        const signupTimeZone = document.getElementById("lmxSignupTimeZone");
        const editTimeZone = document.getElementById("lmxEditTimeZone");

        signupForm.addEventListener("submit", async event => {
            event.preventDefault();
            if (revealSignupDetailsBeforeSubmit()) return;

            await withButton(signupForm.querySelector("button[type='submit']"), async () => {
                const payload = {
                    email: document.getElementById("lmxSignupEmail").value.trim(),
                    displayName: document.getElementById("lmxSignupName").value.trim(),
                    timeZoneId: document.getElementById("lmxSignupTimeZone").value,
                    athleteLink: getAthleteSelectorPayload("lmxSignupAthlete"),
                    callAvailability: collectAvailability("lmxSignupCalls")
                };
                const result = await postJson(`${API}/signup`, payload);
                setStatus("lmxSignupStatus", result.message || "Check your email.", false);
                signupForm.reset();
                clearAthleteSelector("lmxSignupAthlete");
                setDefaultTimezone(document.getElementById("lmxSignupTimeZone"));
                signupSubmitted = true;
                signupDetailsPrompted = false;
                renderAll();
            }, "Joining...");
        });

        signupAgain.addEventListener("click", () => {
            signupSubmitted = false;
            signupDetailsPrompted = false;
            setStatus("lmxSignupStatus", "", false);
            renderAll();
        });

        resendForm.addEventListener("submit", async event => {
            event.preventDefault();
            await withButton(resendForm.querySelector("button[type='submit']"), async () => {
                await postJson(`${API}/resend`, {
                    email: document.getElementById("lmxResendEmail").value.trim()
                });
                setStatus("lmxResendStatus", "Check your email for your private check-in link.", false);
            }, "Sending...");
        });

        editToggle.addEventListener("click", () => {
            const form = document.getElementById("lmxEditForm");
            form.classList.toggle("lmx-hidden");
        });

        profilePictureButton.addEventListener("click", () => {
            profilePictureInput.click();
        });

        profilePictureInput.addEventListener("change", async () => {
            const file = profilePictureInput.files && profilePictureInput.files[0];
            if (file) await uploadProfilePicture(file, profilePictureInput);
        });

        editForm.addEventListener("submit", async event => {
            event.preventDefault();
            if (!accessToken) return;
            await withButton(editForm.querySelector("button[type='submit']"), async () => {
                const result = await postJson(`${API}/edit`, {
                    accessToken,
                    displayName: document.getElementById("lmxEditName").value.trim(),
                    timeZoneId: document.getElementById("lmxEditTimeZone").value,
                    athleteLink: getAthleteSelectorPayload("lmxEditAthlete"),
                    callAvailability: collectAvailability("lmxEditCalls")
                });
                participantState = result;
                publicState = result.public;
                renderAll();
                setStatus("lmxEditStatus", "Saved.", false);
            }, "Saving...");
        });

        signupTimeZone.addEventListener("change", () => {
            renderCallsForSignup((publicState && publicState.calls) || []);
        });

        editTimeZone.addEventListener("change", () => {
            if (!participantState) return;
            renderCallVoteControls("lmxEditCalls", participantState.public.calls || [], participantState.callAvailability || []);
            renderParticipantCalls(participantState.calls || [], participantState.public.signupClosesAtUtc);
        });
    }

    function revealSignupDetailsBeforeSubmit() {
        const details = document.getElementById("lmxSignupDetails");
        if (!details || details.open) return false;

        details.open = true;
        signupDetailsPrompted = true;
        details.classList.remove("attention");
        void details.offsetWidth;
        details.classList.add("attention");
        window.setTimeout(() => details.classList.remove("attention"), 1400);
        details.scrollIntoView({ behavior: "smooth", block: "nearest" });
        setStatus("lmxSignupStatus", "Check these once, then join.", false);
        return true;
    }

    async function consumeUrlTokens() {
        const params = new URLSearchParams(window.location.search || "");
        let shouldClean = false;

        if (params.has("token")) {
            const token = params.get("token") || "";
            if (token.length > 0) {
                accessToken = token;
                safeStorageSet(STORAGE_KEY, accessToken);
                shouldClean = true;
            }
        }

        if (params.has("confirm")) {
            const result = await postJson(`${API}/confirm`, { token: params.get("confirm") || "" });
            accessToken = result.accessToken;
            safeStorageSet(STORAGE_KEY, accessToken);
            participantState = result.state;
            publicState = result.state.public;
            setStatus("lmxSignupStatus", "You're in.", false);
            shouldClean = true;
        }

        if (params.has("stop")) {
            await postJson(`${API}/stop-emails`, { token: params.get("stop") || "" });
            setStatus("lmxResendStatus", "Challenge emails stopped.", false);
            shouldClean = true;
        }

        if (shouldClean) {
            window.history.replaceState({}, "", window.location.pathname);
        }
    }

    async function refreshState() {
        if (participantState && publicState) {
            renderAll();
            return;
        }

        if (accessToken) {
            try {
                participantState = await postJson(`${API}/participant`, { token: accessToken });
                publicState = participantState.public;
                renderAll();
                return;
            } catch (err) {
                if (isAuthFailure(err)) {
                    safeStorageRemove(STORAGE_KEY);
                    accessToken = null;
                }
            }
        }

        await refreshPublicOnly();
    }

    async function refreshPublicOnly() {
        publicState = await getJson(`${API}/state`);
        participantState = null;
        renderAll();
    }

    function renderAll() {
        const state = participantState ? participantState.public : publicState;
        if (!state) return;

        renderMetrics(state);
        renderHeroContext(state);
        renderChallengeVisuals(state);
        renderCallsForSignup(state);
        renderBoard(state);
        renderPodium(state);
        renderPanels(state);

        if (participantState) {
            renderParticipant(participantState);
        }
    }

    function renderMetrics(state) {
        const preStartSignup = isPreStartSignup(state);
        const checks = (state.leaderboard || []).reduce((sum, row) => sum + row.checkedInDays, 0);
        setText("lmxMetricPeople", String((state.leaderboard || []).length));
        setText("lmxMetricChecks", String(checks));
        setText("lmxMetricMax", String(state.dailyMaxScore || 11));
        setText("lmxMetricPhase", phaseLabel(state.phase));
        setText("lmxHeroStatus", phaseLabel(state.phase));
        setText("lmxPhaseLabel", phaseLabel(state.phase));
        const startLabel = formatDateLabel(state.startDate);
        setText("lmxStartChip", preStartSignup ? `Starts ${startLabel}` : `Started ${startLabel}`);
        const boardSection = document.getElementById("lmxBoardSection");
        if (boardSection) boardSection.classList.toggle("signup-roster", preStartSignup);
        if (preStartSignup) {
            setText("lmxBoardTitle", "Leaderboard");
            setText("lmxBoardMeta", `${(state.leaderboard || []).length} people signed up · starts ${formatDateLabel(state.startDate)}`);
        } else {
            setText("lmxBoardTitle", state.phase === "completed" ? "Final leaderboard" : "Live leaderboard");
            setText("lmxBoardMeta", `${(state.leaderboard || []).length} people · ${checks} check-ins · later days score higher · slipping one territory once a day ok, but never two days in a row`);
        }
        setText("lmxSignupKicker", state.signupOpen ? "free signup" : "signup closed");
        setText("lmxSignupTitle", state.signupOpen ? (preStartSignup ? `Join free before ${formatDateLabel(state.startDate)}` : "Join free today") : "Signup is closed");
    }

    function renderHeroContext(state) {
        const hasParticipant = !!participantState;
        const preStartSignup = isPreStartSignup(state);
        const dashboardMode = hasParticipant || !preStartSignup;
        const highlights = document.getElementById("lmxHeroHighlights");
        const life = document.getElementById("lmxLifeStrip");
        if (!highlights || !life) return;

        if (!dashboardMode) {
            setText("lmxHeroMode", `Starts ${formatDateLabel(state.startDate)}`);
            setText("lmxHeroCopy", "Track four daily habits for two weeks and get sleep, movement, food, and vices back under control.");
            highlights.className = "lmx-benefit-strip";
            highlights.setAttribute("aria-label", "Challenge benefits");
            highlights.innerHTML = `
                <strong>Fell off your habits?</strong>
                <span>Too busy for a full reset?</span>
                <span>Travel, stress, or deadlines?</span>
                <span>Perfect plans keep failing?</span>`;
            life.className = "lmx-life-strip";
            life.setAttribute("aria-label", "Real life compatible challenge");
            life.innerHTML = `
                <strong>For busy people.</strong>
                <span><i class="fas fa-plane" aria-hidden="true"></i>Travel compatible</span>
                <span><i class="fas fa-child" aria-hidden="true"></i>Children compatible</span>
                <span><i class="fas fa-briefcase" aria-hidden="true"></i>Work compatible</span>
                <span><i class="fas fa-heart-pulse" aria-hidden="true"></i>Injury compatible</span>`;
            return;
        }

        if (!hasParticipant) {
            setText("lmxHeroMode", "Live leaderboard");
            setText(
                "lmxHeroCopy",
                state.phase === "completed"
                    ? "The challenge is complete. The final board is archived below."
                    : state.signupOpen
                        ? "Signup is open today. Join from the card and catch up from your private link."
                        : "Signup is closed. Follow the public board as the 14-day challenge plays out.");
            highlights.className = "lmx-benefit-strip lmx-ops-strip";
            highlights.setAttribute("aria-label", "Challenge status");
            highlights.innerHTML = [
                opsTile("People", (state.leaderboard || []).length, "fa-users"),
                opsTile("Check-ins", (state.leaderboard || []).reduce((sum, row) => sum + row.checkedInDays, 0), "fa-list-check"),
                opsTile("Peak points/day", state.dailyMaxScore || 11, "fa-bolt"),
                opsTile("Status", phaseLabel(state.phase), "fa-signal")
            ].join("");
            life.className = "lmx-life-strip lmx-ops-status";
            life.setAttribute("aria-label", "Public challenge status");
            life.innerHTML = `
                <strong>${state.phase === "completed" ? "Final board" : "Live board"}</strong>
                <span><i class="fas fa-ranking-star" aria-hidden="true"></i>Leaderboard</span>
                <span><i class="fas fa-table-cells" aria-hidden="true"></i>Daily grid</span>
                <span><i class="fas fa-medal" aria-hidden="true"></i>Final podium</span>
                <em>Participants use private email links to check in.</em>`;
            return;
        }

        const participant = participantState.participant || {};
        const leaderboard = state.leaderboard || [];
        const rowIndex = leaderboard.findIndex(row => row.participantId === participant.id);
        const row = rowIndex >= 0 ? leaderboard[rowIndex] : null;
        const duration = state.durationDays || 14;

        setText("lmxHeroMode", "You're in");
        setText("lmxHeroCopy", "Use this page for check-ins, standings, calls, and participant notes.");
        highlights.className = "lmx-benefit-strip lmx-ops-strip";
        highlights.setAttribute("aria-label", "Participant status");
        highlights.innerHTML = [
            opsTile("Rank", row ? `#${rowIndex + 1}` : "-", "fa-ranking-star"),
            opsTile("Days", row ? `${row.checkedInDays}/${duration}` : `0/${duration}`, "fa-calendar-check"),
            opsTile("Score", row ? row.totalPoints : 0, "fa-bolt"),
            opsTile("Streak", row ? row.currentStreak : 0, "fa-fire")
        ].join("");

        const status = participantStatus(state);
        life.className = "lmx-life-strip lmx-ops-status";
        life.setAttribute("aria-label", "Participant next action");
        life.innerHTML = `<strong>${esc(status.title)}</strong>${status.chips.map(chip => `<span><i class="fas ${escAttr(chip.icon)}" aria-hidden="true"></i>${esc(chip.text)}</span>`).join("")}<em>${esc(status.note)}</em>`;
    }

    function opsTile(label, value, icon) {
        return `<div class="lmx-ops-tile">
            <i class="fas ${escAttr(icon)}" aria-hidden="true"></i>
            <span>${esc(label)}</span>
            <strong>${esc(value)}</strong>
        </div>`;
    }

    function participantStatus(state) {
        const dueDays = getPendingCheckInDays(participantState);
        const selectedCalls = (participantState.calls || []).filter(call => call.selectedSlot);
        if (dueDays.length > 0) {
            return {
                title: "Due now",
                chips: dueDays.map(day => ({ icon: "fa-list-check", text: `Day ${day.challengeDay} · ${formatWeekday(day.date)}` })),
                note: "Save the check-in first; the board appears after you are caught up."
            };
        }

        if (state.phase === "completed") {
            return {
                title: "Complete",
                chips: [
                    { icon: "fa-trophy", text: "Final board" },
                    { icon: "fa-note-sticky", text: "Notes archived" }
                ],
                note: "Check-ins are closed for this challenge."
            };
        }

        if (state.phase === "signup" || state.phase === "roster") {
            return {
                title: "You're in",
                chips: [
                    { icon: "fa-flag-checkered", text: `Starts ${formatDateLabel(state.startDate)}` },
                    { icon: "fa-calendar-days", text: selectedCalls.length ? "Calls set" : "Call times pending" }
                ],
                note: "Your first check-in email arrives the morning after Day 1. Nothing is due before then."
            };
        }

        return {
            title: "Caught up",
            chips: [
                { icon: "fa-circle-check", text: "No due day" },
                { icon: "fa-envelope", text: "Reminder skips when done" }
            ],
            note: selectedCalls.length ? "Selected calls are listed below." : "Call times appear here before reminders go out."
        };
    }

    function renderChallengeVisuals(state) {
        const strip = document.getElementById("lmxDayStrip");
        if (!strip) return;

        const checkedDays = new Set();
        (state.leaderboard || []).forEach(row => {
            (row.cells || []).forEach(cell => {
                if (cell.checkedIn) checkedDays.add(cell.challengeDay);
            });
        });

        const today = new Date().toISOString().slice(0, 10);
        strip.innerHTML = (state.days || []).map(day => {
            const classes = ["lmx-track-day"];
            if (checkedDays.has(day.challengeDay)) classes.push("done");
            if (day.date === today) classes.push("today");
            return `<span class="${classes.join(" ")}" title="Day ${day.challengeDay} · ${escAttr(formatCheckInDate(day.date))}">${day.challengeDay}</span>`;
        }).join("");
    }

    function renderPanels(state) {
        const hasParticipant = !!participantState;
        const pendingCheckInDays = hasParticipant ? getPendingCheckInDays(participantState) : [];
        const checkInOnly = pendingCheckInDays.length > 0;
        const dashboardMode = hasParticipant || !isPreStartSignup(state);
        const publicClosed = !hasParticipant && !state.signupOpen;
        const hero = document.getElementById("lmxHeroLayout");
        if (hero) {
            hero.classList.toggle("checkin-only", checkInOnly);
            hero.classList.toggle("public-board-only", publicClosed);
        }

        toggle("lmxTitlePanel", !checkInOnly && !publicClosed);
        toggle("lmxSignupPanel", state.signupOpen && !hasParticipant);
        toggle("lmxParticipantPanel", hasParticipant);
        toggle("lmxClosedPanel", false);
        toggle("lmxResendPanel", !hasParticipant);
        toggle("lmxNotesPanel", hasParticipant && !checkInOnly);
        toggle("lmxSignupIntro", !signupSubmitted);
        toggle("lmxSignupDonePanel", signupSubmitted);
        toggle("lmxTrack", hasParticipant && dashboardMode && !checkInOnly);
        toggle("lmxMetrics", hasParticipant && dashboardMode && !checkInOnly);
        toggle("lmxBoardSection", !checkInOnly);
        toggle("lmxParticipantTools", !checkInOnly);
        toggle("lmxParticipantCalls", hasParticipant && !checkInOnly);
        toggle("lmxEditCallField", hasParticipant && state.signupOpen && hasOpenCallVoting(state) && !checkInOnly);
        if (checkInOnly) toggle("lmxEditForm", false);
        if (!hasParticipant) {
            const completed = state.phase === "completed";
            const signupOpen = state.signupOpen;
            setText("lmxResendTitle", completed ? "Need participant access?" : "Need your check-in link?");
            setText(
                "lmxResendCopy",
                completed
                    ? "If you joined the challenge, enter your email and we will send your private participant link."
                    : signupOpen
                        ? "Already joined or opened this page in a new browser? Enter your email and we will send your private link."
                        : "If you joined before signup closed, enter your email and we will send your private check-in link.");
            setText("lmxResendButtonText", completed ? "Send participant link" : "Send check-in link");
        }
        const details = document.getElementById("lmxSignupDetails");
        if (details && !signupDetailsPrompted && !signupSubmitted) {
            details.open = false;
        }
        if (!dashboardMode || checkInOnly) toggle("lmxPodium", false);
        const slackInvite = document.getElementById("lmxSlackInviteLink");
        if (slackInvite) {
            slackInvite.href = state.slackInviteUrl || "#";
            slackInvite.classList.toggle("lmx-hidden", !state.slackInviteUrl);
        }

        const slackRoom = document.getElementById("lmxSlackRoomLink");
        if (slackRoom) {
            slackRoom.href = state.slackRoomUrl || "#";
            slackRoom.classList.toggle("lmx-hidden", !state.slackRoomUrl);
        }
    }

    function renderParticipant(state) {
        const participant = state.participant;
        const pendingCheckInDays = getPendingCheckInDays(state);
        const title = pendingCheckInDays.length
            ? `Check in, ${participant.displayName}`
            : state.public.phase === "completed"
                ? `Final board, ${participant.displayName}`
                : state.public.phase === "active"
                    ? `Caught up, ${participant.displayName}`
                : `Ready, ${participant.displayName}`;
        const kicker = pendingCheckInDays.length
            ? "due now"
            : state.public.phase === "completed"
                ? "final"
                : state.public.phase === "signup" || state.public.phase === "roster"
                    ? "you're in"
                    : "caught up";
        setText("lmxParticipantKicker", kicker);
        setText("lmxParticipantTitle", title);

        document.getElementById("lmxEditName").value = participant.displayName || "";
        setAthleteSelectorValue("lmxEditAthlete", participant.athleteSlug || participant.athleteUrl || "");
        setSelectValue(document.getElementById("lmxEditTimeZone"), participant.timeZoneId);
        renderProfilePictureControls(participant);
        renderCallVoteControls("lmxEditCalls", getOpenCallVoteCalls(state.public), state.callAvailability || []);
        renderParticipantCalls(state.calls || [], state.public.signupClosesAtUtc);
        renderCheckIns(state.eligibleDays || []);
        renderNotes(state.notes || []);
    }

    function renderProfilePictureControls(participant) {
        const field = document.getElementById("lmxProfilePictureField");
        const preview = document.getElementById("lmxProfilePicturePreview");
        const image = document.getElementById("lmxProfilePictureImage");
        if (!field || !preview || !image) return;

        const canUpload = !(participant.athleteSlug || participant.athleteUrl);
        field.classList.toggle("lmx-hidden", !canUpload);
        if (!canUpload) return;

        const profileImage = String(participant.profileImageUrl || "").trim();
        preview.classList.toggle("placeholder", !profileImage);
        preview.setAttribute("aria-hidden", profileImage ? "false" : "true");
        image.src = profileImage || ATHLETE_PLACEHOLDER_IMAGE;
        image.alt = profileImage ? `${participant.displayName || "Participant"} profile picture` : "";
    }

    function renderCallsForSignup(state) {
        const field = document.getElementById("lmxSignupCallField");
        const container = document.getElementById("lmxSignupCalls");
        const summary = document.getElementById("lmxSignupDetailsSummary");
        const calls = getOpenCallVoteCalls(state);
        if (summary) summary.textContent = calls.length ? "Timezone, profile, and calls" : "Timezone and profile";
        if (!calls.length) {
            if (container) container.innerHTML = "";
            if (field) field.classList.add("lmx-hidden");
            return;
        }

        if (field) field.classList.remove("lmx-hidden");
        renderCallVoteControls("lmxSignupCalls", calls, []);
    }

    function renderCallVoteControls(containerId, calls, selected) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const selectedKeys = new Set((selected || []).map(x => `${x.callKey}:${x.slotId}`));
        container.innerHTML = (calls || []).map(call => {
            const slots = (call.candidateSlots || []).map(slot => {
                const id = `${containerId}-${call.key}-${slot.id}`.replace(/[^a-zA-Z0-9_-]/g, "-");
                const checked = selectedKeys.has(`${call.key}:${slot.id}`) ? " checked" : "";
                return `<label class="lmx-check" for="${escAttr(id)}">
                    <input id="${escAttr(id)}" type="checkbox" data-call-key="${escAttr(call.key)}" data-slot-id="${escAttr(slot.id)}"${checked}>
                    <span>${esc(formatDateTime(slot.startsAtUtc, getCallDisplayTimeZone(containerId)))}</span>
                </label>`;
            }).join("");
            return `<div class="lmx-call-group"><strong>${esc(call.label)}</strong>${slots || "<span>No slots yet.</span>"}</div>`;
        }).join("");
    }

    function renderParticipantCalls(calls, signupClosesAtUtc) {
        const container = document.getElementById("lmxParticipantCalls");
        if (!calls.length) {
            container.innerHTML = "";
            return;
        }

        container.innerHTML = (calls || []).map(call => {
            const timeZoneId = getParticipantTimeZone();
            const when = call.selectedSlot ? formatDateTime(call.selectedSlot.startsAtUtc, timeZoneId) : pendingCallTimeLabel(signupClosesAtUtc, timeZoneId);
            const startsAtMs = call.selectedSlot ? Date.parse(call.selectedSlot.startsAtUtc) : NaN;
            const done = Number.isFinite(startsAtMs) && startsAtMs + CALL_ACTIVE_WINDOW_MS < Date.now();
            const link = done
                ? `<span class="lmx-call-done">Done</span>`
                : call.videoCallUrl
                ? `<a class="lmx-call-link" href="${escAttr(call.videoCallUrl)}" target="_blank" rel="noopener">Google Meet</a>`
                : "";
            return `<div class="lmx-call-group${done ? " done" : ""}"><strong>${esc(call.label)}</strong><div class="lmx-call-meta"><span>${esc(when)}</span>${link}</div></div>`;
        }).join("");
    }

    function renderBoard(state) {
        const board = document.getElementById("lmxBoard");
        if (isPreStartSignup(state)) {
            renderRosterBoard(board, state);
            return;
        }

        const publicViewer = !participantState;
        board.className = publicViewer ? "lmx-board public" : "lmx-board";
        const dayHeaders = (state.days || []).map(day => `<div class="lmx-cell">${day.challengeDay}</div>`).join("");
        const rows = (state.leaderboard || []).map(row => {
            const name = row.athleteUrl
                ? `<a href="${escAttr(row.athleteUrl)}">${esc(row.displayName)}</a>`
                : `<span>${esc(row.displayName)}</span>`;
            const participant = participantNameHtml(row, name);
            const cells = (row.cells || []).map(cell => {
                if (!cell.checkedIn) return `<div class="lmx-cell empty" title="Day ${cell.challengeDay}"></div>`;
                if (cell.countsForScore === false) {
                    return practiceDayCellHtml(cell);
                }
                return scoredDayCellHtml(cell);
            }).join("");
            return `<div class="lmx-board-row" role="row">
                <div class="lmx-name" role="cell">${participant}</div>
                <div class="lmx-number" role="cell" data-label="Score">${row.totalPoints}</div>
                <div class="lmx-cell-strip" role="cell" aria-label="Daily scores">${cells}</div>
            </div>`;
        }).join("");

        board.innerHTML = `<div class="lmx-board-row header" role="row">
            <div role="columnheader">Participant</div>
            <div role="columnheader">Score</div>
            <div class="lmx-cell-strip lmx-header-days" role="presentation">${dayHeaders}</div>
        </div>${rows || emptyBoardRow(state.durationDays || 14, publicViewer)}`;
    }

    function practiceDayCellHtml(cell) {
        const breakdown = habitBreakdown(cell);
        const title = practiceCellTitle(cell, breakdown);
        if (!breakdown.length) {
            return `<div class="lmx-cell practice" title="${escAttr(title)}" aria-label="${escAttr(title)}"><i class="fa fa-rocket" aria-hidden="true"></i></div>`;
        }

        const marks = breakdown.map(item => `<span class="${habitMarkClass(item.value)}" title="${escAttr(`${item.label} ${item.value}/2`)}" aria-hidden="true">${esc(item.short)}</span>`).join("");
        return `<div class="lmx-cell lmx-cell-breakdown practice" title="${escAttr(title)}" aria-label="${escAttr(title)}">
            <span class="lmx-cell-score"><i class="fa fa-rocket" aria-hidden="true"></i></span>
            <span class="lmx-habit-marks">${marks}</span>
        </div>`;
    }

    function scoredDayCellHtml(cell) {
        const score = typeof cell.score === "number" ? cell.score : 0;
        const breakdown = habitBreakdown(cell);
        const title = habitCellTitle(cell, score, breakdown);
        if (!breakdown.length) {
            const scoreClass = score >= 8 ? "score-high" : score >= 4 ? "score-mid" : "score-low";
            return `<div class="lmx-cell ${scoreClass}" title="${escAttr(title)}" aria-label="${escAttr(title)}">${score}</div>`;
        }

        const rawScore = breakdown.reduce((sum, item) => sum + item.value, 0);
        const scoreClass = rawScore >= 6 ? "score-high" : rawScore >= 3 ? "score-mid" : "score-low";
        const marks = breakdown.map(item => `<span class="${habitMarkClass(item.value)}" title="${escAttr(`${item.label} ${item.value}/2`)}" aria-hidden="true">${esc(item.short)}</span>`).join("");
        return `<div class="lmx-cell lmx-cell-breakdown ${scoreClass}" title="${escAttr(title)}" aria-label="${escAttr(title)}">
            <span class="lmx-cell-score">${score}</span>
            <span class="lmx-habit-marks">${marks}</span>
        </div>`;
    }

    function habitBreakdown(cell) {
        const habits = [
            { key: "sleep", label: "Sleep", short: "S" },
            { key: "exercise", label: "Exercise", short: "E" },
            { key: "nutrition", label: "Nutrition", short: "N" },
            { key: "vices", label: "Vices", short: "V" }
        ];
        const values = habits.map(habit => {
            const value = Number(cell[habit.key]);
            return Number.isFinite(value)
                ? { ...habit, value: Math.max(0, Math.min(2, value)) }
                : null;
        });

        return values.every(Boolean) ? values : [];
    }

    function habitCellTitle(cell, score, breakdown) {
        if (!breakdown.length) return `Day ${cell.challengeDay}: ${score}`;

        const pieces = breakdown.map(item => `${item.label} ${item.value}/2`);
        const missed = breakdown.filter(item => item.value < 2).map(item => item.label.toLowerCase());
        const missedText = missed.length ? `. Missing: ${missed.join(", ")}` : ". Full day";
        return `Day ${cell.challengeDay}: ${score} points. ${pieces.join(", ")}${missedText}`;
    }

    function practiceCellTitle(cell, breakdown) {
        if (!breakdown.length) return `Day ${cell.challengeDay}: practice check-in`;

        const pieces = breakdown.map(item => `${item.label} ${item.value}/2`);
        const missed = breakdown.filter(item => item.value < 2).map(item => item.label.toLowerCase());
        const missedText = missed.length ? `. Missing: ${missed.join(", ")}` : ". Full practice day";
        return `Day ${cell.challengeDay}: practice check-in. ${pieces.join(", ")}${missedText}`;
    }

    function habitMarkClass(value) {
        if (value >= 2) return "lmx-habit-mark full";
        if (value > 0) return "lmx-habit-mark partial";
        return "lmx-habit-mark missed";
    }

    function renderRosterBoard(board, state) {
        board.className = "lmx-board roster";
        const dayHeaders = (state.days || []).map(day => `<div class="lmx-cell">${day.challengeDay}</div>`).join("");
        const rows = (state.leaderboard || []).map(row => {
            const name = row.athleteUrl
                ? `<a href="${escAttr(row.athleteUrl)}">${esc(row.displayName)}</a>`
                : `<span>${esc(row.displayName)}</span>`;
            const participant = participantNameHtml(row, name);
            const cells = (row.cells || state.days || []).map(cell => `<div class="lmx-cell empty" title="Day ${cell.challengeDay}"></div>`).join("");
            return `<div class="lmx-board-row lmx-roster-row" role="row">
                <div class="lmx-name" role="cell">${participant}</div>
                <div class="lmx-cell-strip" role="cell" aria-label="Challenge days">${cells}</div>
            </div>`;
        }).join("");

        board.innerHTML = `<div class="lmx-board-row lmx-roster-row header" role="row">
            <div role="columnheader">Participant</div>
            <div class="lmx-cell-strip lmx-header-days" role="presentation">${dayHeaders}</div>
        </div>${rows || emptyRosterRow(state.durationDays || 14)}`;
    }

    function emptyBoardRow(durationDays, publicViewer) {
        return `<div class="lmx-board-row" role="row">
            <div class="lmx-name" role="cell">
                <span class="lmx-empty-name">No one has joined yet</span>
            </div>
            <div class="lmx-number" role="cell" data-label="Score">0</div>
            <div class="lmx-cell-strip" role="cell" aria-label="Daily scores">${Array.from({ length: durationDays }, () => `<div class="lmx-cell empty"></div>`).join("")}</div>
        </div>`;
    }

    function emptyRosterRow(durationDays) {
        return `<div class="lmx-board-row lmx-roster-row" role="row">
            <div class="lmx-name" role="cell">
                <span class="lmx-empty-name">No one has joined yet</span>
            </div>
            <div class="lmx-cell-strip" role="cell" aria-label="Challenge days">${Array.from({ length: durationDays }, () => `<div class="lmx-cell empty"></div>`).join("")}</div>
        </div>`;
    }

    function renderPodium(state) {
        const podium = state.podium || [];
        toggle("lmxPodium", podium.length > 0);
        const list = document.getElementById("lmxPodiumList");
        list.innerHTML = podium.map(row => {
            const name = row.athleteUrl
                ? `<a href="${escAttr(row.athleteUrl)}">${esc(row.displayName)}</a>`
                : `<strong>${esc(row.displayName)}</strong>`;
            return `<div class="lmx-podium-item">
                <span class="lmx-podium-place">#${row.placement}</span>
                ${name}
                <span>${row.checkedInDays} days / ${row.totalPoints} pts</span>
            </div>`;
        }).join("");
    }

    function renderCheckIns(days) {
        const container = document.getElementById("lmxCheckinList");
        if (!days.length) {
            container.innerHTML = emptyCheckInHtml();
            return;
        }

        const orderedDays = [...days].sort((a, b) => a.challengeDay - b.challengeDay);
        const activeDay = pickActiveCheckInDay(orderedDays);
        const previousForm = container.querySelector(".lmx-checkin-card");
        if (previousForm) revokePendingNotePhotoUrls(checkInDayKey(previousForm));
        container.innerHTML = checkInSwitcherHtml(orderedDays, activeDay) + checkInCardHtml(activeDay);
        container.querySelectorAll(".lmx-checkin-switcher button").forEach(button => {
            button.addEventListener("click", () => {
                selectedCheckInDay = Number(button.dataset.day);
                renderCheckIns(orderedDays);
            });
        });
        container.querySelectorAll(".lmx-segmented button").forEach(button => {
            button.addEventListener("click", () => {
                const group = button.closest(".lmx-segmented");
                group.querySelectorAll("button").forEach(item => item.setAttribute("aria-pressed", "false"));
                button.setAttribute("aria-pressed", "true");
                updateCheckInSaveState(button.closest("form"));
            });
        });
        container.querySelectorAll("form").forEach(form => {
            form.querySelector("textarea")?.addEventListener("input", () => updateCheckInSaveState(form));
            form.querySelector("[data-photo-button]")?.addEventListener("click", () => {
                form.querySelector("input[data-note-photos]")?.click();
            });
            form.querySelector("input[data-note-photos]")?.addEventListener("change", event => {
                setPendingNotePhotos(form, Array.from(event.target.files || []));
                renderSelectedNotePhotoPreviews(form);
                updateCheckInSaveState(form);
            });
            renderSelectedNotePhotoPreviews(form);
            updateCheckInSaveState(form);
            form.addEventListener("submit", event => {
                event.preventDefault();
                submitCheckIn(form);
            });
        });
    }

    function pickActiveCheckInDay(days) {
        const current = days.find(day => day.challengeDay === selectedCheckInDay);
        if (current) return current;

        const missing = [...days]
            .filter(day => !day.existing)
            .sort((a, b) => b.challengeDay - a.challengeDay)[0];
        const fallback = missing || days[days.length - 1];
        selectedCheckInDay = fallback.challengeDay;
        return fallback;
    }

    function checkInSwitcherHtml(days, activeDay) {
        if (days.length < 2) return "";

        return `<div class="lmx-checkin-switcher" aria-label="Eligible check-in days">
            ${days.map(day => {
                const isActive = day.challengeDay === activeDay.challengeDay;
                const status = day.existing ? "Saved" : "Due";
                return `<button type="button" data-day="${day.challengeDay}" aria-pressed="${isActive ? "true" : "false"}">
                    <strong>Day ${day.challengeDay}</strong>
                    <span>${esc(formatShortDateLabel(day.date))}</span>
                    <em>${status}</em>
                </button>`;
            }).join("")}
        </div>`;
    }

    function checkInCardHtml(day) {
        const existing = day.existing || {};
        const saved = savedDays.has(day.challengeDay);
        const practice = day.countsForScore === false;
        const hasExisting = !!day.existing;
        const note = (existing.note || "").trim();
        const savedImages = Array.isArray(existing.images) ? existing.images : [];
        const savedImageHtml = savedImages.length
            ? `<div class="lmx-note-photo-grid saved" aria-label="Saved note photos">${savedImages.map((image, index) => notePhotoHtml(image, `${day.challengeDay}-${index}`)).join("")}</div>`
            : "";
        const photoSlotsLeft = Math.max(0, MAX_NOTE_PHOTOS - savedImages.length);
        const questions = QUESTIONS.map(q => {
            const current = typeof existing[q.key] === "number" ? existing[q.key] : 1;
            const buttons = ANSWERS.map(answer => `<button type="button" data-value="${answer.value}" aria-pressed="${answer.value === current ? "true" : "false"}">${answer.label}</button>`).join("");
            return `<div class="lmx-question" data-key="${q.key}">
                <div class="lmx-question-label"><i class="fas ${q.icon}" aria-hidden="true"></i><span>${q.text}</span></div>
                <div class="lmx-segmented">${buttons}</div>
            </div>`;
        }).join("");

        const originalAttrs = QUESTIONS
            .map(q => `data-original-${q.key}="${typeof existing[q.key] === "number" ? existing[q.key] : 1}"`)
            .join(" ");

        return `<form class="lmx-checkin-card" data-day="${day.challengeDay}" data-saved="${hasExisting ? "true" : "false"}" ${originalAttrs} data-original-note="${escAttr(note)}">
            <h3>Day ${day.challengeDay} <span class="lmx-phase">${practice ? `Practice check-in - ${esc(formatCheckInDate(day.date))}` : esc(formatCheckInDate(day.date))}</span></h3>
            ${practice ? `<div class="lmx-practice-note"><strong>Practice check-in.</strong><span>Counts for checked-in days and streak, not points.</span></div>` : ""}
            ${questions}
            <div class="lmx-field">
                <label for="lmx-note-${day.challengeDay}">Participant note <span>optional</span></label>
                <textarea id="lmx-note-${day.challengeDay}" maxlength="240" placeholder="Visible to participants only">${esc(note)}</textarea>
            </div>
            <div class="lmx-field lmx-note-photo-field" data-photo-slots="${photoSlotsLeft}">
                <span class="lmx-label">Note photos <span>optional</span></span>
                ${savedImageHtml}
                <div class="lmx-note-photo-picker">
                    <button class="lmx-button secondary" type="button" data-photo-button${photoSlotsLeft <= 0 ? " disabled" : ""}>
                        <i class="fas fa-images" aria-hidden="true"></i>
                        Add photos
                    </button>
                    <input id="lmx-note-photos-${day.challengeDay}" type="file" accept="image/*,.heic,.heif" multiple data-note-photos ${photoSlotsLeft <= 0 ? "disabled" : ""}>
                    <span class="lmx-photo-count" data-photo-count>${photoSlotsLeft <= 0 ? "Photo limit reached" : `${photoSlotsLeft} slots left`}</span>
                </div>
                <div class="lmx-note-photo-grid pending" data-photo-previews></div>
            </div>
            <button class="lmx-button" type="submit"${hasExisting ? " disabled" : ""}>
                <i class="fas fa-check" aria-hidden="true"></i>
                Save day ${day.challengeDay}
            </button>
            <div class="lmx-status${saved || day.existing ? " success" : ""}">${saved || day.existing ? SAVED_CHECKIN_TEXT : ""}</div>
        </form>`;
    }

    function notePhotoHtml(image, key) {
        const url = String(image && image.url || "").trim();
        if (!url) return "";
        const width = Number(image.width) || "";
        const height = Number(image.height) || "";
        return `<a class="lmx-note-photo" href="${escAttr(url)}" target="_blank" rel="noopener" aria-label="Open note photo">
            <img src="${escAttr(url)}" alt="" loading="lazy" decoding="async" width="${escAttr(width)}" height="${escAttr(height)}" data-photo-key="${escAttr(key)}">
        </a>`;
    }

    function setPendingNotePhotos(form, files) {
        const key = checkInDayKey(form);
        const slots = Number(form.querySelector(".lmx-note-photo-field")?.dataset.photoSlots || MAX_NOTE_PHOTOS);
        const photos = files
            .filter(file => /^image\//i.test(String(file.type || "")) || /\.(heic|heif)$/i.test(file.name || ""))
            .slice(0, Math.max(0, slots));

        revokePendingNotePhotoUrls(key);
        if (photos.length) pendingNotePhotos.set(key, photos);
        else pendingNotePhotos.delete(key);
    }

    function removePendingNotePhoto(form, index) {
        const key = checkInDayKey(form);
        const photos = getPendingNotePhotos(form).filter((_, photoIndex) => photoIndex !== index);
        revokePendingNotePhotoUrls(key);
        if (photos.length) pendingNotePhotos.set(key, photos);
        else pendingNotePhotos.delete(key);
        const input = form.querySelector("input[data-note-photos]");
        if (input && !photos.length) input.value = "";
        renderSelectedNotePhotoPreviews(form);
        updateCheckInSaveState(form);
    }

    function renderSelectedNotePhotoPreviews(form) {
        const previews = form.querySelector("[data-photo-previews]");
        const count = form.querySelector("[data-photo-count]");
        if (!previews) return;

        const key = checkInDayKey(form);
        const photos = getPendingNotePhotos(form);
        const slots = Number(form.querySelector(".lmx-note-photo-field")?.dataset.photoSlots || MAX_NOTE_PHOTOS);
        revokePendingNotePhotoUrls(key);
        const urls = photos.map(photo => URL.createObjectURL(photo));
        if (urls.length) pendingNotePhotoUrls.set(key, urls);

        previews.innerHTML = photos.map((photo, index) => `<span class="lmx-note-photo pending-item">
            <img src="${escAttr(urls[index])}" alt="" loading="lazy" decoding="async">
            <button type="button" class="lmx-note-photo-remove" data-remove-photo="${index}" title="Remove photo" aria-label="Remove photo">
                <i class="fas fa-xmark" aria-hidden="true"></i>
            </button>
        </span>`).join("");

        previews.querySelectorAll("[data-remove-photo]").forEach(button => {
            button.addEventListener("click", () => removePendingNotePhoto(form, Number(button.dataset.removePhoto)));
        });

        if (count) {
            const remaining = Math.max(0, slots - photos.length);
            count.textContent = photos.length
                ? `${photos.length} selected · ${remaining} slots left`
                : (slots <= 0 ? "Photo limit reached" : `${slots} slots left`);
        }
    }

    function clearPendingNotePhotos(challengeDay) {
        const key = String(challengeDay);
        revokePendingNotePhotoUrls(key);
        pendingNotePhotos.delete(key);
    }

    function revokePendingNotePhotoUrls(key) {
        (pendingNotePhotoUrls.get(key) || []).forEach(url => URL.revokeObjectURL(url));
        pendingNotePhotoUrls.delete(key);
    }

    function getPendingNotePhotos(form) {
        return pendingNotePhotos.get(checkInDayKey(form)) || [];
    }

    function checkInDayKey(form) {
        return String(Number(form?.dataset.day || 0));
    }

    function emptyCheckInHtml() {
        if (!participantState) return "";
        const phase = participantState.public.phase;

        if (phase === "completed") {
            return `<div class="lmx-empty-state">
                <i class="fas fa-trophy" aria-hidden="true"></i>
                <strong>Check-ins are closed.</strong>
                <span>The final board and participant notes are below.</span>
            </div>`;
        }

        if (phase === "signup" || phase === "roster") {
            const firstOpen = datePlusDays(participantState.public.startDate, 1);
            return `<div class="lmx-empty-state">
                <i class="fas fa-circle-check" aria-hidden="true"></i>
                <strong>You're in.</strong>
                <span>Your first check-in email arrives ${esc(formatDateLabel(firstOpen))}. Nothing is due before then.</span>
            </div>`;
        }

        return `<div class="lmx-empty-state">
            <i class="fas fa-circle-check" aria-hidden="true"></i>
            <strong>You are caught up.</strong>
            <span>Come back tomorrow if another day is due.</span>
        </div>`;
    }

    async function submitCheckIn(form) {
        if (!accessToken) return;
        if (!hasCheckInChanged(form)) return;
        await withButton(form.querySelector("button[type='submit']"), async () => {
            const draft = collectCheckInDraft(form);
            const notePhotos = getPendingNotePhotos(form);
            const payload = {
                accessToken,
                challengeDay: Number(form.dataset.day),
                note: draft.note || null
            };
            QUESTIONS.forEach(q => payload[q.key] = draft[q.key]);
            const result = notePhotos.length
                ? await postCheckInWithPhotos(payload, notePhotos)
                : await postJson(`${API}/check-in`, payload);
            savedDays.add(payload.challengeDay);
            clearPendingNotePhotos(payload.challengeDay);
            participantState = result;
            publicState = result.public;
            const nextMissing = getPendingCheckInDays(participantState)
                .sort((a, b) => b.challengeDay - a.challengeDay)[0];
            selectedCheckInDay = nextMissing ? nextMissing.challengeDay : payload.challengeDay;
            renderAll();
        }, "Saving...");
    }

    async function postCheckInWithPhotos(payload, photos) {
        const formData = new FormData();
        formData.append("accessToken", payload.accessToken);
        formData.append("challengeDay", String(payload.challengeDay));
        formData.append("sleep", String(payload.sleep));
        formData.append("exercise", String(payload.exercise));
        formData.append("nutrition", String(payload.nutrition));
        formData.append("vices", String(payload.vices));
        if (payload.note) formData.append("note", payload.note);

        const prepared = await Promise.all(photos.map(prepareNotePhotoFile));
        prepared.forEach((photo, index) => {
            formData.append("notePhotos", photo, photo.name || `check-in-photo-${index + 1}.webp`);
        });

        return postForm(`${API}/check-in`, formData);
    }

    function collectCheckInDraft(form) {
        const draft = {
            note: form.querySelector("textarea").value.trim()
        };
        QUESTIONS.forEach(q => {
            const pressed = form.querySelector(`.lmx-question[data-key="${q.key}"] button[aria-pressed="true"]`);
            draft[q.key] = Number(pressed ? pressed.dataset.value : 1);
        });
        return draft;
    }

    function hasCheckInChanged(form) {
        if (!form || form.dataset.saved !== "true") return true;

        const draft = collectCheckInDraft(form);
        if (getPendingNotePhotos(form).length > 0) return true;
        if ((form.dataset.originalNote || "") !== draft.note) return true;

        return QUESTIONS.some(q => Number(form.dataset[`original${capitalize(q.key)}`]) !== draft[q.key]);
    }

    function updateCheckInSaveState(form) {
        if (!form) return;

        const button = form.querySelector("button[type='submit']");
        const status = form.querySelector(".lmx-status");
        const changed = hasCheckInChanged(form);

        if (button) button.disabled = !changed;
        if (status && form.dataset.saved === "true") {
            status.textContent = changed ? "" : SAVED_CHECKIN_TEXT;
            status.classList.remove("error");
            status.classList.toggle("success", !changed);
        }
    }

    async function uploadProfilePicture(file, input) {
        if (!accessToken) return;

        const uploadFile = await prepareProfilePictureFile(file);
        const formData = new FormData();
        formData.append("accessToken", accessToken);
        formData.append("profilePicture", uploadFile, uploadFile.name || "profile-picture.jpg");

        const button = document.getElementById("lmxProfilePictureButton");
        input.disabled = true;
        if (button) button.disabled = true;
        setStatus("lmxProfilePictureStatus", "Uploading...", false);
        try {
            const result = await postForm(`${API}/profile-picture`, formData);
            participantState = result;
            publicState = result.public;
            renderAll();
            setStatus("lmxProfilePictureStatus", "Uploaded.", false);
        } catch (err) {
            setStatus("lmxProfilePictureStatus", messageOf(err), true);
        } finally {
            input.disabled = false;
            if (button) button.disabled = false;
            input.value = "";
        }
    }

    async function prepareProfilePictureFile(file) {
        const type = String(file.type || "");
        const isServerPreferred = /^image\/(jpeg|png|webp)$/i.test(type);
        const shouldNormalize = file.size > 1024 * 1024 || !isServerPreferred;
        if (!shouldNormalize) return file;

        try {
            const bitmap = await loadProfileBitmap(file);
            const maxDimension = 1600;
            const scale = Math.min(1, maxDimension / Math.max(bitmap.width, bitmap.height));
            const width = Math.max(1, Math.round(bitmap.width * scale));
            const height = Math.max(1, Math.round(bitmap.height * scale));
            const canvas = document.createElement("canvas");
            canvas.width = width;
            canvas.height = height;
            const ctx = canvas.getContext("2d");
            if (!ctx) return file;

            ctx.fillStyle = "#ffffff";
            ctx.fillRect(0, 0, width, height);
            ctx.drawImage(bitmap, 0, 0, width, height);
            if (typeof bitmap.close === "function") bitmap.close();

            const blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg", 0.88));
            if (!blob) return file;
            return new File([blob], replaceImageExtension(file.name || "profile-picture", "jpg"), { type: "image/jpeg" });
        } catch (_) {
            return file;
        }
    }

    async function prepareNotePhotoFile(file) {
        try {
            const bitmap = await loadProfileBitmap(file);
            const scale = Math.min(1, NOTE_PHOTO_MAX_DIMENSION / Math.max(bitmap.width, bitmap.height));
            const width = Math.max(1, Math.round(bitmap.width * scale));
            const height = Math.max(1, Math.round(bitmap.height * scale));
            const canvas = document.createElement("canvas");
            canvas.width = width;
            canvas.height = height;
            const ctx = canvas.getContext("2d");
            if (!ctx) return file;

            ctx.fillStyle = "#ffffff";
            ctx.fillRect(0, 0, width, height);
            ctx.drawImage(bitmap, 0, 0, width, height);
            if (typeof bitmap.close === "function") bitmap.close();

            let blob = await new Promise(resolve => canvas.toBlob(resolve, "image/webp", 0.86));
            let extension = "webp";
            if (!blob || blob.type !== "image/webp") {
                blob = await new Promise(resolve => canvas.toBlob(resolve, "image/jpeg", 0.88));
                extension = "jpg";
            }

            if (!blob) return file;
            const type = blob.type || (extension === "jpg" ? "image/jpeg" : "image/webp");
            return new File([blob], replaceImageExtension(file.name || "check-in-photo", extension), { type });
        } catch (_) {
            return file;
        }
    }

    async function loadProfileBitmap(file) {
        if (window.createImageBitmap) {
            try {
                return await createImageBitmap(file, { imageOrientation: "from-image" });
            } catch (_) {
            }
        }

        return await new Promise((resolve, reject) => {
            const url = URL.createObjectURL(file);
            const image = new Image();
            image.onload = () => {
                URL.revokeObjectURL(url);
                resolve(image);
            };
            image.onerror = () => {
                URL.revokeObjectURL(url);
                reject(new Error("Image preview failed"));
            };
            image.src = url;
        });
    }

    function replaceImageExtension(name, extension) {
        const clean = String(name || "profile-picture").replace(/\.[^.]+$/, "");
        return `${clean || "profile-picture"}.${extension}`;
    }

    function renderNotes(notes) {
        const container = document.getElementById("lmxNotes");
        if (!notes.length) {
            container.innerHTML = `<div class="lmx-note"><strong>No notes yet.</strong></div>`;
            return;
        }

        container.innerHTML = notes.map(note => {
            const images = Array.isArray(note.images) ? note.images : [];
            const imageHtml = images.length
                ? `<div class="lmx-note-photo-grid">${images.map((image, index) => notePhotoHtml(image, `${note.participantId}-${note.challengeDay}-${index}`)).join("")}</div>`
                : "";
            const noteText = String(note.note || "").trim();
            return `<article class="lmx-note">
                <strong>${esc(note.displayName)} · Day ${note.challengeDay}</strong>
                ${noteText ? `<p>${esc(noteText)}</p>` : ""}
                ${imageHtml}
            </article>`;
        }).join("");
    }

    function collectAvailability(containerId) {
        return Array.from(document.querySelectorAll(`#${containerId} input[type="checkbox"]:checked`))
            .map(input => ({
                callKey: input.dataset.callKey,
                slotId: input.dataset.slotId
            }));
    }

    function participantNameHtml(row, nameHtml) {
        const athlete = findAthleteForParticipant(row);
        const profileImage = String(row.profileImageUrl || "").trim();
        const athleteProfileImage = isPlaceholderProfileImage(athlete?.profilePic) ? "" : (athlete?.profilePic || "");
        const image = athleteProfileImage || profileImage || ATHLETE_PLACEHOLDER_IMAGE;
        const hasProfileImage = !!(athleteProfileImage || profileImage);
        const avatarClass = hasProfileImage ? "lmx-participant-avatar" : "lmx-participant-avatar placeholder";
        const alt = hasProfileImage ? `${row.displayName || "Participant"} profile picture` : "";
        return `<div class="lmx-participant-name">
            <span class="${avatarClass}" aria-hidden="${hasProfileImage ? "false" : "true"}">
                <img src="${escAttr(image)}" alt="${escAttr(alt)}" loading="lazy" decoding="async">
            </span>
            <span class="lmx-participant-label">${nameHtml}</span>
        </div>`;
    }

    function isPlaceholderProfileImage(url) {
        const value = String(url || "").trim();
        return !value || value.includes(ATHLETE_PLACEHOLDER_IMAGE);
    }

    function findAthleteForParticipant(row) {
        if (!row || !row.athleteUrl || !athleteDirectory.length) return null;
        const slug = normalizeAthleteSlug(row.athleteUrl);
        if (!slug) return null;
        return athleteDirectory.find(athlete => normalizeAthleteSlug(athlete.slug) === slug) || null;
    }

    function loadAthleteDirectory() {
        if (athleteDirectoryPromise) return athleteDirectoryPromise;

        athleteDirectoryPromise = fetch("/api/data/athletes")
            .then(response => response.ok ? response.json() : [])
            .then(data => {
                athleteDirectory = (Array.isArray(data) ? data : [])
                    .map(a => ({
                        name: String(a.Name || "").trim(),
                        slug: String(a.AthleteSlug || "").trim(),
                        profilePic: String(a.ProfilePicLeaderboardThumb || a.ProfilePicThumb || a.ProfilePic || "").trim()
                    }))
                    .filter(a => a.name && a.slug)
                    .sort((a, b) => a.name.localeCompare(b.name));

                if (publicState) renderAll();
                return athleteDirectory;
            })
            .catch(() => {
                athleteDirectoryPromise = null;
                return [];
            });

        return athleteDirectoryPromise;
    }

    function initAthleteSelectors() {
        const inputs = ["lmxSignupAthlete", "lmxEditAthlete"]
            .map(id => document.getElementById(id))
            .filter(Boolean);
        if (!inputs.length) return;

        inputs.forEach(input => {
            input.setAttribute("role", "combobox");
            input.setAttribute("aria-autocomplete", "list");
            input.setAttribute("aria-expanded", "false");
        });

        loadAthleteDirectory()
            .then(athletes => inputs.forEach(input => wireAthleteSelector(input, athletes)));
    }

    function wireAthleteSelector(input, athletes) {
        if (athleteSelectors.has(input.id)) return;

        let currentFocus = -1;
        const selector = {
            input,
            athletes,
            setValue(value) {
                const raw = String(value || "").trim();
                const normalized = normalizeAthleteSlug(raw);
                const match = athletes.find(a => normalizeAthleteSlug(a.slug) === normalized);
                if (match) {
                    select(match);
                    return;
                }

                input.value = raw;
                clearSelection();
            }
        };

        athleteSelectors.set(input.id, selector);
        selector.setValue(input.value);

        input.addEventListener("input", () => {
            clearSelection();
            renderSuggestions();
        });

        input.addEventListener("keydown", event => {
            let list = document.getElementById(`${input.id}-autocomplete-list`);
            const items = list ? Array.from(list.getElementsByClassName("lmx-athlete-option")) : [];

            if (event.key === "ArrowDown") {
                event.preventDefault();
                currentFocus++;
                setActive(items);
            } else if (event.key === "ArrowUp") {
                event.preventDefault();
                currentFocus--;
                setActive(items);
            } else if (event.key === "Enter" && currentFocus > -1 && items[currentFocus]) {
                event.preventDefault();
                items[currentFocus].dispatchEvent(new MouseEvent("mousedown"));
            } else if (event.key === "Escape") {
                closeList();
            }
        });

        document.addEventListener("click", event => {
            if (!input.closest(".lmx-athlete-selector")?.contains(event.target)) closeList();
        });

        function renderSuggestions() {
            const query = input.value.trim().toLowerCase();
            const terms = query.split(/\s+/).filter(Boolean);
            closeList();
            if (!terms.length) return;

            const matches = athletes
                .filter(a => {
                    const name = a.name.toLowerCase();
                    const slug = normalizeAthleteSlug(a.slug);
                    return terms.every(term => name.includes(term) || slug.includes(term));
                })
                .slice(0, 6);

            if (!matches.length) return;

            const list = document.createElement("div");
            list.id = `${input.id}-autocomplete-list`;
            list.className = "lmx-athlete-options";
            list.setAttribute("role", "listbox");
            input.parentNode.appendChild(list);
            input.setAttribute("aria-expanded", "true");

            matches.forEach(athlete => {
                const item = document.createElement("div");
                item.className = "lmx-athlete-option";
                item.setAttribute("role", "option");
                item.innerHTML = `
                    ${athlete.profilePic ? `<img src="${escAttr(athlete.profilePic)}" alt="" loading="lazy">` : "<span class=\"lmx-athlete-fallback\"></span>"}
                    <span>${highlightMatch(athlete.name, terms[0])}</span>`;
                item.addEventListener("mousedown", event => {
                    event.preventDefault();
                    select(athlete);
                    closeList();
                });
                list.appendChild(item);
            });
        }

        function select(athlete) {
            input.value = athlete.name;
            input.dataset.athleteSlug = athlete.slug;
            input.dataset.athleteName = athlete.name;
        }

        function clearSelection() {
            delete input.dataset.athleteSlug;
            delete input.dataset.athleteName;
        }

        function closeList() {
            document.getElementById(`${input.id}-autocomplete-list`)?.remove();
            input.setAttribute("aria-expanded", "false");
            currentFocus = -1;
        }

        function setActive(items) {
            if (!items.length) return;
            items.forEach(item => item.classList.remove("autocomplete-active"));
            if (currentFocus >= items.length) currentFocus = 0;
            if (currentFocus < 0) currentFocus = items.length - 1;
            items[currentFocus].classList.add("autocomplete-active");
            items[currentFocus].scrollIntoView({ block: "nearest" });
        }
    }

    function getAthleteSelectorPayload(id) {
        const input = document.getElementById(id);
        if (!input) return null;

        const raw = input.value.trim();
        if (!raw) return null;

        const selectedName = input.dataset.athleteName || "";
        if (input.dataset.athleteSlug && raw.toLowerCase() === selectedName.toLowerCase()) {
            return input.dataset.athleteSlug;
        }

        const normalized = normalizeAthleteSlug(raw);
        const match = athleteSelectors.get(id)?.athletes.find(a =>
            a.name.toLowerCase() === raw.toLowerCase() || normalizeAthleteSlug(a.slug) === normalized);
        return match ? match.slug : raw;
    }

    function setAthleteSelectorValue(id, value) {
        const selector = athleteSelectors.get(id);
        if (selector) {
            selector.setValue(value);
            return;
        }

        const input = document.getElementById(id);
        if (input) input.value = value || "";
    }

    function clearAthleteSelector(id) {
        const input = document.getElementById(id);
        if (!input) return;
        input.value = "";
        delete input.dataset.athleteSlug;
        delete input.dataset.athleteName;
    }

    function normalizeAthleteSlug(value) {
        let raw = String(value || "").trim();
        if (!raw) return "";

        try {
            raw = new URL(raw, window.location.origin).pathname;
        } catch (_) {}

        raw = raw.replace(/^\/+|\/+$/g, "");
        if (raw.toLowerCase().startsWith("athlete/")) raw = raw.slice("athlete/".length);
        return raw
            .trim()
            .replace(/_/g, "-")
            .toLowerCase()
            .replace(/[^a-z0-9-]/g, "-")
            .replace(/-+/g, "-")
            .replace(/^-|-$/g, "");
    }

    function highlightMatch(value, term) {
        const text = String(value || "");
        const lower = text.toLowerCase();
        const needle = String(term || "").toLowerCase();
        const index = needle ? lower.indexOf(needle) : -1;
        if (index < 0) return esc(text);

        return `${esc(text.slice(0, index))}<strong>${esc(text.slice(index, index + needle.length))}</strong>${esc(text.slice(index + needle.length))}`;
    }

    function getPendingCheckInDays(state) {
        return ((state && state.eligibleDays) || []).filter(day => !day.existing);
    }

    async function getJson(url) {
        const response = await fetch(url, { headers: { "Accept": "application/json" } });
        return readJsonResponse(response, url);
    }

    async function postJson(url, payload) {
        const response = await fetch(url, {
            method: "POST",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify(payload)
        });
        return readJsonResponse(response, url);
    }

    async function postForm(url, formData) {
        const response = await fetch(url, {
            method: "POST",
            headers: { "Accept": "application/json" },
            body: formData
        });
        return readJsonResponse(response, url);
    }

    async function readJsonResponse(response, url) {
        const text = await response.text();
        const contentType = response.headers.get("content-type") || "";
        let data = {};
        if (text) {
            try {
                data = JSON.parse(text);
            } catch (_) {
                const target = String(url || response.url || "request");
                const typeLabel = contentType ? ` (${contentType})` : "";
                throw new Error(`${target} returned ${response.status || "a non-JSON response"}${typeLabel}.`);
            }
        }

        if (!response.ok) {
            const err = new Error(data.message || response.statusText || "Request failed");
            err.status = response.status;
            throw err;
        }
        return data;
    }

    function isAuthFailure(err) {
        return err && (err.status === 401 || err.status === 403);
    }

    async function withButton(button, work, busyText) {
        const original = button.innerHTML;
        button.disabled = true;
        button.innerHTML = `<i class="fas fa-spinner fa-spin" aria-hidden="true"></i>${busyText}`;
        try {
            await work();
        } catch (err) {
            const status = button.closest("form")?.querySelector(".lmx-status");
            if (status) {
                status.textContent = messageOf(err);
                status.classList.add("error");
                status.classList.remove("success");
            }
        } finally {
            button.disabled = false;
            button.innerHTML = original;
        }
    }

    function fillTimeZones(select) {
        if (!select) return;
        const current = Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
        select.innerHTML = Array.from(new Set([current, ...COMMON_TIME_ZONES]))
            .filter(Boolean)
            .map(zone => `<option value="${escAttr(zone)}">${esc(zone)}</option>`)
            .join("");
        setDefaultTimezone(select);
    }

    function setDefaultTimezone(select) {
        setSelectValue(select, Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC");
    }

    function setSelectValue(select, value) {
        if (!select) return;
        const candidate = value || "UTC";
        if (!Array.from(select.options).some(option => option.value === candidate)) {
            const option = document.createElement("option");
            option.value = candidate;
            option.textContent = candidate;
            select.appendChild(option);
        }
        select.value = candidate;
    }

    function phaseLabel(phase) {
        switch (phase) {
            case "signup": return "Signup open";
            case "roster": return "Getting ready";
            case "active": return "Live";
            case "completed": return "Final";
            default: return "loading";
        }
    }

    function isPreStartSignup(state) {
        return !!state && state.signupOpen && state.phase === "signup";
    }

    function hasOpenCallVoting(state) {
        return getOpenCallVoteCalls(state).length > 0;
    }

    function getOpenCallVoteCalls(state) {
        return ((state && state.calls) || []).filter(call => !call.selectedSlot && (call.candidateSlots || []).length);
    }

    function formatDateLabel(value) {
        const date = parseIsoDate(value);
        if (!date) return value || "";
        return new Intl.DateTimeFormat("en-US", { weekday: "long", month: "short", day: "numeric" }).format(date);
    }

    function formatShortDateLabel(value) {
        const date = parseIsoDate(value);
        if (!date) return value || "";
        return new Intl.DateTimeFormat("en-US", { weekday: "short", month: "short", day: "numeric" }).format(date);
    }

    function formatCheckInDate(value) {
        const date = parseIsoDate(value);
        if (!date) return value || "";
        return new Intl.DateTimeFormat("en-US", { weekday: "long", month: "short", day: "numeric" }).format(date);
    }

    function formatWeekday(value) {
        const date = parseIsoDate(value);
        if (!date) return value || "";
        return new Intl.DateTimeFormat("en-US", { weekday: "short" }).format(date);
    }

    function parseIsoDate(value) {
        const parts = String(value || "").split("-").map(Number);
        if (parts.length !== 3 || parts.some(Number.isNaN)) return null;
        return new Date(Date.UTC(parts[0], parts[1] - 1, parts[2], 12, 0, 0));
    }

    function datePlusDays(value, days) {
        const parts = String(value || "").split("-").map(Number);
        if (parts.length !== 3 || parts.some(Number.isNaN)) return value || "";
        const date = new Date(Date.UTC(parts[0], parts[1] - 1, parts[2] + days, 12, 0, 0));
        return date.toISOString().slice(0, 10);
    }

    function formatDateTime(value, timeZoneId) {
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return value || "";
        const options = {
            weekday: "short",
            month: "short",
            day: "numeric",
            hour: "2-digit",
            minute: "2-digit",
            timeZoneName: "short"
        };
        const timeZone = normalizeDisplayTimeZone(timeZoneId);
        if (timeZone) options.timeZone = timeZone;
        return new Intl.DateTimeFormat("en-US", options).format(date);
    }

    function pendingCallTimeLabel(signupClosesAtUtc, timeZoneId) {
        const date = new Date(signupClosesAtUtc);
        if (Number.isNaN(date.getTime())) return "Meeting time pending.";
        const options = {
            weekday: "long",
            month: "short",
            day: "numeric"
        };
        const timeZone = normalizeDisplayTimeZone(timeZoneId);
        if (timeZone) options.timeZone = timeZone;
        const closes = new Intl.DateTimeFormat("en-US", options).format(date);
        return `Meeting time pending. Signup closes on ${closes}.`;
    }

    function getCallDisplayTimeZone(containerId) {
        if (containerId === "lmxSignupCalls") {
            return document.getElementById("lmxSignupTimeZone")?.value;
        }
        if (containerId === "lmxEditCalls") {
            return document.getElementById("lmxEditTimeZone")?.value || getParticipantTimeZone();
        }
        return getParticipantTimeZone();
    }

    function getParticipantTimeZone() {
        return participantState?.participant?.timeZoneId || Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
    }

    function normalizeDisplayTimeZone(timeZoneId) {
        const value = String(timeZoneId || "").trim();
        if (!value) return null;
        try {
            new Intl.DateTimeFormat("en-US", { timeZone: value }).format(new Date());
            return value;
        } catch {
            return null;
        }
    }

    function setStatus(id, message, isError) {
        const el = document.getElementById(id);
        if (!el) return;
        el.textContent = message || "";
        el.classList.toggle("error", !!isError);
        el.classList.toggle("success", !isError && !!message);
    }

    function setText(id, value) {
        const el = document.getElementById(id);
        if (el) el.textContent = value;
    }

    function toggle(id, visible) {
        const el = document.getElementById(id);
        if (el) el.classList.toggle("lmx-hidden", !visible);
    }

    function messageOf(err) {
        return err && err.message ? err.message : "Something went wrong.";
    }

    function capitalize(value) {
        return value ? value.charAt(0).toUpperCase() + value.slice(1) : "";
    }

    function esc(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function escAttr(value) {
        return esc(value);
    }

    function safeStorageGet(key) {
        try { return localStorage.getItem(key); } catch (_) { return null; }
    }

    function safeStorageSet(key, value) {
        try { localStorage.setItem(key, value); } catch (_) {}
    }

    function safeStorageRemove(key) {
        try { localStorage.removeItem(key); } catch (_) {}
    }
})();
