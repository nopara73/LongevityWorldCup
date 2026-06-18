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
    const PARTICIPANT_TABS = ["checkin", "profile", "home"];
    const athleteSelectors = new Map();
    let athleteDirectory = [];
    let athleteDirectoryPromise = null;
    let boardScrollObserver = null;
    let boardScrollObservedElement = null;
    let dashboardScrollObserver = null;
    let dashboardScrollObservedElement = null;
    let participantActiveTab = null;
    let participantTabManual = false;
    let participantNotice = null;
    let showInactiveLeaderboard = false;

    document.addEventListener("DOMContentLoaded", init);

    async function init() {
        fillTimeZones(document.getElementById("lmxSignupTimeZone"));
        fillTimeZones(document.getElementById("lmxEditTimeZone"));
        renderQuestionPreview();
        wireForms();
        initAthleteSelectors();

        try {
            await consumeUrlTokens();
            await refreshState();
            scrollBoardToLatestDay();
        } catch (err) {
            setStatus("lmxSignupStatus", messageOf(err), true);
            if (!publicState) await refreshPublicOnly();
        }
    }

    function wireForms() {
        const signupForm = document.getElementById("lmxSignupForm");
        const resendForm = document.getElementById("lmxResendForm");
        const editForm = document.getElementById("lmxEditForm");
        const signupAgain = document.getElementById("lmxSignupAgain");
        const profilePictureInput = document.getElementById("lmxProfilePictureInput");
        const profilePictureButton = document.getElementById("lmxProfilePictureButton");
        const editTimeZone = document.getElementById("lmxEditTimeZone");
        const inactiveToggle = document.getElementById("lmxInactiveToggle");
        wireCommitmentAmountValidation("lmxSignupCommitmentAmount");
        wireCommitmentAmountValidation("lmxEditCommitmentAmount");

        signupForm.addEventListener("submit", async event => {
            event.preventDefault();
            if (revealSignupDetailsBeforeSubmit()) return;

            await withButton(signupForm.querySelector("button[type='submit']"), async () => {
                const payload = {
                    email: document.getElementById("lmxSignupEmail").value.trim(),
                    displayName: document.getElementById("lmxSignupName").value.trim(),
                    timeZoneId: document.getElementById("lmxSignupTimeZone").value,
                    athleteLink: getAthleteSelectorPayload("lmxSignupAthlete"),
                    commitmentAmountUsd: parseCommitmentAmount("lmxSignupCommitmentAmount")
                };
                const result = await postJson(`${API}/signup`, payload);
                setStatus("lmxSignupStatus", result.message || "Check your email.", false);
                signupForm.reset();
                clearAthleteSelector("lmxSignupAthlete");
                setCommitmentInputValue("lmxSignupCommitmentAmount", null);
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

        document.querySelectorAll("[data-lmx-tab]").forEach(button => {
            button.addEventListener("click", () => {
                setParticipantTab(button.dataset.lmxTab, true);
            });
            button.addEventListener("keydown", event => {
                handleParticipantTabKeydown(event, button);
            });
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
                    commitmentAmountUsd: parseCommitmentAmount("lmxEditCommitmentAmount")
                });
                participantState = result;
                publicState = result.public;
                renderAll();
                setStatus("lmxEditStatus", "Saved.", false);
            }, "Saving...");
        });

        inactiveToggle?.addEventListener("click", () => {
            showInactiveLeaderboard = !showInactiveLeaderboard;
            if (publicState) renderBoard(publicState);
        });

        editTimeZone.addEventListener("change", () => {
            if (participantState) renderParticipantCalls(participantState.calls || [], participantState.public.callSelectionClosesAtUtc);
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

        if (!publicState) {
            await refreshPublicOnly({ keepParticipant: !!accessToken });
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
                } else {
                    setStatus("lmxResendStatus", "Your private console did not load yet. Refresh to try again.", true);
                    renderAll();
                    return;
                }
            }
        }

        if (!publicState) {
            await refreshPublicOnly();
        } else {
            participantState = null;
            renderAll();
        }
    }

    async function refreshPublicOnly(options) {
        const keepParticipant = !!(options && options.keepParticipant);
        publicState = await getJson(`${API}/state`);
        if (!keepParticipant) participantState = null;
        renderAll();
    }

    function renderAll() {
        const state = participantState ? participantState.public : publicState;
        if (!state) return;

        renderMetrics(state);
        renderHeroContext(state);
        renderChallengeVisuals(state);
        renderBoard(state);
        renderPanels(state);
        scrollDashboardToLatestDay();

        if (participantState) {
            renderParticipant(participantState);
        } else {
            renderNotes(state.notes || []);
        }

        scrollBoardToLatestDay();
    }

    function renderQuestionPreview() {
        const list = document.getElementById("lmxQuestionPreviewList");
        if (!list) return;

        list.innerHTML = QUESTIONS.map(q => `
            <div class="lmx-question-preview-item">
                <div class="lmx-question-preview-label">
                    <i class="fas ${q.icon}" aria-hidden="true"></i>
                    <span>${esc(q.text)}</span>
                </div>
            </div>`).join("");
    }

    function renderMetrics(state) {
        const preStartSignup = isPreStartSignup(state);
        const boardRows = splitLeaderboardRows(state);
        const checks = boardRows.active.reduce((sum, row) => sum + row.checkedInDays, 0);
        setText("lmxMetricPeople", String(boardRows.active.length));
        setText("lmxMetricChecks", String(checks));
        setText("lmxMetricMax", "4");
        setText("lmxMetricPhase", phaseLabel(state.phase));
        setText("lmxHeroStatus", phaseLabel(state.phase));
        setText("lmxStartChip", "Agency > Outcome");
        const boardSection = document.getElementById("lmxBoardSection");
        if (boardSection) boardSection.classList.toggle("signup-roster", preStartSignup);
        if (preStartSignup) {
            setText("lmxBoardTitle", "Leaderboard");
            setText("lmxBoardMeta", `${boardRows.active.length} active people signed up · starts ${formatDateLabel(state.startDate)}`);
        } else {
            setText("lmxBoardTitle", "Live leaderboard");
            setText("lmxBoardMeta", `${boardRows.active.length} active people · ${checks} check-ins · later days score higher · one slip can still score max, never twice in a row`);
        }
        setText("lmxSignupKicker", "signup for free");
    }

    function renderHeroContext(state) {
        const hasParticipant = !!participantState;
        const preStartSignup = isPreStartSignup(state);
        const dashboardMode = hasParticipant || !preStartSignup;
        const highlights = document.getElementById("lmxHeroHighlights");
        const life = document.getElementById("lmxLifeStrip");
        if (!highlights || !life) return;

        if (!dashboardMode) {
            toggle("lmxHeroStatus", true);
            toggle("lmxHeroMode", true);
            toggle("lmxHeroCopy", true);
            setText("lmxHeroMode", `Starts ${formatDateLabel(state.startDate)}`);
            setText("lmxHeroCopy", "Track four daily habits and get sleep, movement, food, and vices back under control.");
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
                <span><i class="fas fa-briefcase" aria-hidden="true"></i>Work compatible</span>
                <span><i class="fas fa-plane" aria-hidden="true"></i>Travel compatible</span>
                <span><i class="fas fa-people-roof" aria-hidden="true"></i>Family compatible</span>
                <span><i class="fas fa-notes-medical" aria-hidden="true"></i>Illness compatible</span>`;
            return;
        }

        if (!hasParticipant) {
            toggle("lmxHeroStatus", false);
            toggle("lmxHeroMode", false);
            toggle("lmxHeroCopy", false);
            highlights.className = "lmx-benefit-strip lmx-ops-strip";
            highlights.setAttribute("aria-label", "Challenge status");
            const boardRows = splitLeaderboardRows(state);
            highlights.innerHTML = [
                opsTile("People", boardRows.active.length, "fa-users"),
                opsTile("Check-ins", boardRows.active.reduce((sum, row) => sum + row.checkedInDays, 0), "fa-list-check"),
                opsTile("Agencies", 4, "fa-layer-group"),
                opsTile("", phaseLabel(state.phase), "fa-signal")
            ].join("");
            life.className = "lmx-life-strip lmx-ops-status";
            life.setAttribute("aria-label", "Challenge compatibility");
            life.innerHTML = `
                <span><i class="fas fa-briefcase" aria-hidden="true"></i>Work compatible</span>
                <span><i class="fas fa-plane" aria-hidden="true"></i>Travel compatible</span>
                <span><i class="fas fa-people-roof" aria-hidden="true"></i>Family compatible</span>
                <span><i class="fas fa-notes-medical" aria-hidden="true"></i>Illness compatible</span>`;
            return;
        }

        const participant = participantState.participant || {};
        const leaderboardRows = splitLeaderboardRows(state);
        const leaderboard = participant.challengeEmailsStopped ? (state.leaderboard || []) : leaderboardRows.active;
        const rowIndex = leaderboard.findIndex(row => row.participantId === participant.id);
        const row = rowIndex >= 0 ? leaderboard[rowIndex] : null;
        const duration = (state.days || []).length || state.durationDays || 14;

        toggle("lmxHeroStatus", true);
        toggle("lmxHeroMode", true);
        toggle("lmxHeroCopy", true);
        setText("lmxHeroMode", "You're in");
        setText("lmxHeroCopy", "Use this page for check-ins, standings, scheduled calls, and participant notes.");
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
        const hasLabel = !!String(label || "").trim();
        return `<div class="lmx-ops-tile${hasLabel ? "" : " no-label"}">
            <i class="fas ${escAttr(icon)}" aria-hidden="true"></i>
            ${hasLabel ? `<span>${esc(label)}</span>` : ""}
            <strong>${esc(value)}</strong>
        </div>`;
    }

    function participantStatus(state) {
        const commitment = participantState && participantState.commitment;
        if (commitment && commitment.blocksParticipant) {
            if (commitment.status === "due") {
                return {
                    title: "Commitment due",
                    chips: [
                        { icon: "fa-credit-card", text: formatUsd(commitment.owedAmountUsd) },
                        { icon: "fa-triangle-exclamation", text: `Day ${commitment.triggerChallengeDay || "-"}` }
                    ],
                    note: "Pay it, or fix the eligible check-in before the edit window closes."
                };
            }

            return {
                title: "Commitment setup",
                chips: [
                    { icon: "fa-dollar-sign", text: "Amount required" }
                ],
                note: "Configure the amount before continuing."
            };
        }

        const dueDays = getPendingCheckInDays(participantState);
        const selectedCalls = (participantState.calls || []).filter(call => call.selectedSlot);
        if (dueDays.length > 0) {
            return {
                title: "Due now",
                chips: dueDays.map(day => ({ icon: "fa-list-check", text: `Day ${day.challengeDay} · ${formatWeekday(day.date)}` })),
                note: "Save the check-in first; the board appears after you are caught up."
            };
        }

        if (state.phase === "signup" || state.phase === "roster") {
            return {
                title: "You're in",
                chips: [
                    { icon: "fa-flag-checkered", text: `Starts ${formatDateLabel(state.startDate)}` },
                    { icon: "fa-calendar-days", text: selectedCalls.length ? "Calls set" : "Call times pending" }
                ],
                note: "Your first check-in email arrives after your first eligible day. Nothing is due before then."
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
        const track = document.getElementById("lmxTrack");
        if (!track) return;

        if (!participantState) {
            track.innerHTML = "";
            return;
        }

        const participant = participantState.participant || {};
        const row = (state.leaderboard || []).find(item => item.participantId === participant.id);
        const cells = normalizeDashboardCells(row, state);
        const visibleDays = cells.length || state.durationDays || 14;
        const dayCount = Math.max(1, Math.trunc(Number(visibleDays) || 14));
        const scoredCells = cells.filter(cell => cell.checkedIn && cell.countsForScore !== false);
        const checkedCells = cells.filter(cell => cell.checkedIn);
        const categories = dashboardCategories();
        const summaries = categories.map(category => categorySummary(category, cells, scoredCells));
        const rankedSummaries = summaries
            .filter(item => item.max > 0)
            .sort((a, b) => b.rate - a.rate || b.total - a.total || a.category.label.localeCompare(b.category.label));
        const best = rankedSummaries[0];
        const focus = [...rankedSummaries].reverse()[0];
        const fullDays = checkedCells.filter(cell => isLockedInDay(cell, categories)).length;
        const totalPoints = row && typeof row.totalPoints === "number"
            ? row.totalPoints
            : scoredCells.reduce((sum, cell) => sum + (typeof cell.score === "number" ? cell.score : 0), 0);
        const today = new Date().toISOString().slice(0, 10);
        const dayHeaders = cells.map(cell => {
            const classes = ["lmx-dashboard-day"];
            if (cell.date === today) classes.push("today");
            if (cell.countsForScore === false) classes.push("practice");
            return `<span class="${classes.join(" ")}" title="${escAttr(dayTitle(cell))}">${cell.challengeDay}</span>`;
        }).join("");
        const rows = summaries.map(summary => categoryDashboardRow(summary, cells, today)).join("");
        const emptyLabel = state.phase === "signup" || state.phase === "roster" ? "Starts soon" : "No check-ins";

        track.innerHTML = `
            <div class="lmx-dashboard-head">
                <div>
                    <span class="lmx-mini-label">your trend</span>
                </div>
                <strong>${checkedCells.length ? `${checkedCells.length}/${visibleDays} days` : emptyLabel}</strong>
            </div>
            <div class="lmx-dashboard-stats" aria-label="Personal challenge stats">
                ${dashboardStat("Best", best ? best.category.label : "-", best ? `${Math.round(best.rate * 100)}%` : "-", best ? best.category.icon : "fa-arrow-trend-up", best ? best.category.tone : "")}
                ${dashboardStat("Focus", focus ? focus.category.label : "-", focus ? `${Math.round(focus.rate * 100)}%` : "-", focus ? focus.category.icon : "fa-crosshairs", focus ? focus.category.tone : "")}
                ${dashboardStat("Locked-in days", String(fullDays), "", "fa-calendar-check")}
                ${dashboardStat("Points", scoredCells.length ? String(totalPoints) : "-", "", "fa-chart-line")}
            </div>
            ${participantState.trendGuidance?.text ? `<div class="lmx-trend-guidance"><i class="fas fa-scale-balanced" aria-hidden="true"></i><span>${esc(participantState.trendGuidance.text)}</span></div>` : ""}
            <div class="lmx-dashboard-scroll">
                <div class="lmx-dashboard-grid" role="table" aria-label="Sleep, exercise, nutrition, and vices over time" style="--lmx-dashboard-day-columns: repeat(${dayCount}, 2.15rem); --lmx-dashboard-min-width: ${(13.05 + (dayCount * 2.5)).toFixed(2)}rem;">
                    <div class="lmx-dashboard-row lmx-dashboard-row-head" role="row">
                        <div class="lmx-dashboard-corner" role="columnheader">Habit</div>
                        <div class="lmx-dashboard-days" role="presentation">${dayHeaders}</div>
                    </div>
                    ${rows}
                </div>
            </div>`;
    }

    function normalizeDashboardCells(row, state) {
        const byDay = new Map(((row && row.cells) || []).map(cell => [cell.challengeDay, cell]));
        return (state.days || []).map(day => {
            const cell = byDay.get(day.challengeDay) || {
                challengeDay: day.challengeDay,
                checkedIn: false,
                score: null,
                countsForScore: day.challengeDay !== 1,
                sleep: null,
                exercise: null,
                nutrition: null,
                vices: null
            };
            return { ...cell, date: day.date };
        });
    }

    function dashboardCategories() {
        return [
            { key: "sleep", label: "Sleep", icon: "fa-moon", tone: "sleep" },
            { key: "exercise", label: "Exercise", icon: "fa-dumbbell", tone: "exercise" },
            { key: "nutrition", label: "Nutrition", icon: "fa-bowl-food", tone: "nutrition" },
            { key: "vices", label: "Vices", icon: "fa-shield-halved", tone: "vices" }
        ];
    }

    function categorySummary(category, cells, scoredCells) {
        const denominatorCells = scoredCells.length ? scoredCells : cells.filter(cell => cell.checkedIn);
        const total = denominatorCells.reduce((sum, cell) => sum + clampHabitValue(cell[category.key]), 0);
        const max = denominatorCells.length * 2;
        return {
            category,
            total,
            max,
            rate: max > 0 ? total / max : 0
        };
    }

    function categoryDashboardRow(summary, cells, today) {
        const category = summary.category;
        const width = summary.max > 0 ? Math.round(summary.rate * 100) : 0;
        const dayCells = cells.map(cell => categoryDayCell(category, cell, today)).join("");
        return `<div class="lmx-dashboard-row" role="row">
            <div class="lmx-dashboard-category ${escAttr(category.tone)}" role="cell">
                <i class="fas ${escAttr(category.icon)}" aria-hidden="true"></i>
                <span>${esc(category.label)}</span>
                <strong>${summary.max > 0 ? `${summary.total}/${summary.max}` : "-"}</strong>
                <div class="lmx-dashboard-bar" aria-hidden="true"><span style="width:${width}%"></span></div>
            </div>
            <div class="lmx-dashboard-days" role="cell" aria-label="${escAttr(`${category.label} by challenge day`)}">${dayCells}</div>
        </div>`;
    }

    function categoryDayCell(category, cell, today) {
        const classes = ["lmx-category-day"];
        if (cell.date === today) classes.push("today");
        if (cell.countsForScore === false) classes.push("practice");
        if (!cell.checkedIn) {
            classes.push("empty");
            return `<span class="${classes.join(" ")}" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(`${dayTitle(cell)}: no check-in`)}" aria-label="${escAttr(`${category.label} day ${cell.challengeDay}: no check-in`)}"></span>`;
        }

        const value = clampHabitValue(cell[category.key]);
        classes.push(value >= 2 ? "full" : value > 0 ? "partial" : "missed");
        return `<span class="${classes.join(" ")}" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(`${dayTitle(cell)}: ${category.label} ${value}/2`)}" aria-label="${escAttr(`${category.label} day ${cell.challengeDay}: ${value} of 2`)}"></span>`;
    }

    function dashboardStat(label, value, detail, icon, tone) {
        const toneClass = tone ? ` ${escAttr(tone)}` : "";
        return `<div class="lmx-dashboard-stat${toneClass}">
            <i class="fas ${escAttr(icon)}" aria-hidden="true"></i>
            <span>${esc(label)}</span>
            <strong>${esc(value)}</strong>
            ${detail ? `<em>${esc(detail)}</em>` : ""}
        </div>`;
    }

    function isLockedInDay(cell, categories) {
        return categories.every(category => clampHabitValue(cell[category.key]) >= 2);
    }

    function clampHabitValue(value) {
        const number = Number(value);
        return Number.isFinite(number) ? Math.max(0, Math.min(2, number)) : 0;
    }

    function dayTitle(cell) {
        const date = cell.date ? ` · ${formatCheckInDate(cell.date)}` : "";
        const practice = cell.countsForScore === false ? " · practice" : "";
        return `Day ${cell.challengeDay}${date}${practice}`;
    }

    function renderPanels(state) {
        const hasParticipant = !!participantState;
        const pendingCheckInDays = hasParticipant ? getPendingCheckInDays(participantState) : [];
        const commitmentBlocked = hasCommitmentBlock(participantState);
        const activeParticipantTab = hasParticipant ? ensureParticipantTab(participantState) : null;
        const checkInOnly = !commitmentBlocked && pendingCheckInDays.length > 0 && activeParticipantTab === "checkin";
        const dashboardMode = hasParticipant || !isPreStartSignup(state);
        const hero = document.getElementById("lmxHeroLayout");
        if (hero) {
            hero.classList.toggle("checkin-only", checkInOnly);
        }

        toggle("lmxTitlePanel", !checkInOnly);
        toggle("lmxSignupPanel", !hasParticipant);
        toggle("lmxParticipantPanel", hasParticipant);
        toggle("lmxResendPanel", !hasParticipant);
        toggle("lmxNotesPanel", dashboardMode && !checkInOnly);
        toggle("lmxSignupIntro", !signupSubmitted);
        toggle("lmxSignupDonePanel", signupSubmitted);
        toggle("lmxHabitHeading", !hasParticipant);
        toggle("lmxHabitGrid", !hasParticipant);
        toggle("lmxTrack", hasParticipant && dashboardMode && !checkInOnly);
        toggle("lmxMetrics", hasParticipant && dashboardMode && !checkInOnly);
        toggle("lmxBoardSection", !checkInOnly);
        toggle("lmxParticipantTabs", hasParticipant && !commitmentBlocked);
        toggle("lmxCommitmentPanel", hasParticipant && commitmentBlocked);
        toggle("lmxCheckinPanel", hasParticipant && !commitmentBlocked && activeParticipantTab === "checkin");
        toggle("lmxEditForm", hasParticipant && !commitmentBlocked && activeParticipantTab === "profile");
        toggle("lmxHomePanel", hasParticipant && !commitmentBlocked && activeParticipantTab === "home");
        toggle("lmxParticipantTools", hasParticipant && !commitmentBlocked && activeParticipantTab === "home");
        toggle("lmxParticipantCalls", hasParticipant && !commitmentBlocked && activeParticipantTab === "home");
        renderParticipantTabs();
        if (!hasParticipant) {
            participantActiveTab = null;
            participantTabManual = false;
            participantNotice = null;
            setText("lmxResendTitle", "Check-in link");
            setText("lmxResendButtonText", "Send check-in link");
        }
        const details = document.getElementById("lmxSignupDetails");
        if (details && !signupDetailsPrompted && !signupSubmitted) {
            details.open = false;
        }
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
        const activeTab = ensureParticipantTab(state);
        const title = participantPanelTitle(activeTab, pendingCheckInDays, participant, state.public.phase);
        const kicker = participantPanelKicker(activeTab, pendingCheckInDays, state.public.phase);
        setText("lmxParticipantKicker", kicker);
        setText("lmxParticipantTitle", title);
        renderCommitmentPanel(state);
        renderParticipantNotice();
        renderParticipantHome(state, pendingCheckInDays);

        document.getElementById("lmxEditName").value = participant.displayName || "";
        setAthleteSelectorValue("lmxEditAthlete", participant.athleteSlug || participant.athleteUrl || "");
        setSelectValue(document.getElementById("lmxEditTimeZone"), participant.timeZoneId);
        setCommitmentInputValue("lmxEditCommitmentAmount", participant.commitmentAmountUsd ?? state.commitment?.amountUsd);
        const commitmentInput = document.getElementById("lmxEditCommitmentAmount");
        if (commitmentInput) commitmentInput.disabled = state.commitment?.canEditAmount === false;
        renderProfilePictureControls(participant);
        renderParticipantCalls(state.calls || [], state.public.callSelectionClosesAtUtc);
        if (!hasCommitmentBlock(state)) renderCheckIns(state.eligibleDays || []);
        renderNotes(state.notes || state.public.notes || []);
        renderParticipantTabs();
    }

    function participantPanelTitle(activeTab, pendingCheckInDays, participant, phase) {
        const name = participant.displayName || "participant";
        if (hasCommitmentBlock(participantState)) {
            return participantState.commitment?.status === "due"
                ? `Commitment due, ${name}`
                : `Commitment setup, ${name}`;
        }
        if (activeTab === "profile") return `Profile, ${name}`;
        if (activeTab === "home") {
            if (pendingCheckInDays.length) return `Home, ${name}`;
            return phase === "active" ? `Caught up, ${name}` : `Ready, ${name}`;
        }

        return pendingCheckInDays.length ? `Check in, ${name}` : `Check-in, ${name}`;
    }

    function participantPanelKicker(activeTab, pendingCheckInDays, phase) {
        if (hasCommitmentBlock(participantState)) return "commitment";
        if (activeTab === "profile") return "profile";
        if (activeTab === "home") {
            if (pendingCheckInDays.length) return "home";
            return phase === "signup" || phase === "roster" ? "you're in" : "caught up";
        }

        return pendingCheckInDays.length ? "due now" : "no due day";
    }

    function renderParticipantHome(state, pendingCheckInDays) {
        const hasDue = pendingCheckInDays.length > 0;
        const nextCall = (state.calls || []).filter(call => !isParticipantCallDone(call))[0];
        setText("lmxHomeStatusTitle", hasDue ? "Check-in open" : "Nothing due");
        setText("lmxHomeStatusCopy", hasDue
            ? "Use the Check-in tab when ready."
            : nextCall
                ? "Next call is listed below."
                : "You're caught up.");
    }

    function renderParticipantNotice() {
        const notice = document.getElementById("lmxParticipantNotice");
        if (!notice) return;

        const visible = !!(participantNotice && participantNotice.message && participantState && !hasCommitmentBlock(participantState));
        notice.textContent = visible ? participantNotice.message : "";
        notice.classList.toggle("lmx-hidden", !visible);
        notice.classList.toggle("error", visible && !!participantNotice.isError);
        notice.classList.toggle("success", visible && !participantNotice.isError);
    }

    function ensureParticipantTab(state) {
        if (!state) return null;

        const fallback = getDefaultParticipantTab(state);
        if (!PARTICIPANT_TABS.includes(participantActiveTab)) {
            participantActiveTab = fallback;
            participantTabManual = false;
            return participantActiveTab;
        }

        if (!participantTabManual && participantActiveTab !== fallback) {
            participantActiveTab = fallback;
        }

        return participantActiveTab;
    }

    function getDefaultParticipantTab(state) {
        return getPendingCheckInDays(state).length ? "checkin" : "home";
    }

    function setParticipantTab(tab, manual) {
        if (!PARTICIPANT_TABS.includes(tab) || !participantState) return;
        participantActiveTab = tab;
        participantTabManual = !!manual;
        renderPanels(participantState.public);
        renderParticipant(participantState);
        if (tab === "checkin") {
            document.getElementById("lmxParticipantPanel")?.scrollIntoView({ behavior: "smooth", block: "nearest" });
        }
    }

    function renderParticipantTabs() {
        if (!participantState) return;
        if (hasCommitmentBlock(participantState)) {
            PARTICIPANT_TABS.forEach(tab => {
                const panel = getParticipantTabPanel(tab);
                const button = document.querySelector(`[data-lmx-tab="${tab}"]`);
                if (button) {
                    button.setAttribute("aria-selected", "false");
                    button.setAttribute("tabindex", "-1");
                    button.hidden = true;
                }
                if (panel) {
                    panel.classList.add("lmx-hidden");
                    panel.toggleAttribute("hidden", true);
                }
            });
            return;
        }

        const activeTab = ensureParticipantTab(participantState);
        PARTICIPANT_TABS.forEach(tab => {
            const button = document.querySelector(`[data-lmx-tab="${tab}"]`);
            const panel = getParticipantTabPanel(tab);
            const isActive = tab === activeTab;
            if (button) {
                button.setAttribute("aria-selected", isActive ? "true" : "false");
                button.setAttribute("tabindex", isActive ? "0" : "-1");
                button.hidden = false;
            }
            if (panel) {
                panel.classList.toggle("lmx-hidden", !isActive);
                panel.toggleAttribute("hidden", !isActive);
            }
        });
    }

    function getParticipantTabPanel(tab) {
        if (tab === "checkin") return document.getElementById("lmxCheckinPanel");
        if (tab === "profile") return document.getElementById("lmxEditForm");
        if (tab === "home") return document.getElementById("lmxHomePanel");
        return null;
    }

    function handleParticipantTabKeydown(event, button) {
        if (!participantState || hasCommitmentBlock(participantState)) return;

        const currentIndex = PARTICIPANT_TABS.indexOf(button.dataset.lmxTab);
        if (currentIndex < 0) return;

        let nextIndex = currentIndex;
        if (event.key === "ArrowRight" || event.key === "ArrowDown") {
            nextIndex = (currentIndex + 1) % PARTICIPANT_TABS.length;
        } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
            nextIndex = (currentIndex - 1 + PARTICIPANT_TABS.length) % PARTICIPANT_TABS.length;
        } else if (event.key === "Home") {
            nextIndex = 0;
        } else if (event.key === "End") {
            nextIndex = PARTICIPANT_TABS.length - 1;
        } else {
            return;
        }

        event.preventDefault();
        const nextTab = PARTICIPANT_TABS[nextIndex];
        setParticipantTab(nextTab, true);
        document.querySelector(`[data-lmx-tab="${nextTab}"]`)?.focus();
    }

    function renderCommitmentPanel(state) {
        const panel = document.getElementById("lmxCommitmentPanel");
        if (!panel) return;

        const commitment = state.commitment || {};
        if (!commitment.blocksParticipant) {
            panel.innerHTML = "";
            return;
        }

        if (commitment.status === "needs-amount") {
            panel.innerHTML = `
                <form id="lmxCommitmentAmountForm" class="lmx-commitment-card">
                    <div>
                        <strong>Set your commitment amount</strong>
                        <span id="lmxBlockedCommitmentHelp" class="lmx-commitment-copy">Set a real stake. Pay it only if you fall below your recent average, or stop there; choose an amount that'll hurt.</span>
                    </div>
                    <div class="lmx-field">
                        <label for="lmxBlockedCommitmentAmount">USD amount</label>
                        <input id="lmxBlockedCommitmentAmount" type="text" inputmode="decimal" required placeholder="$300" aria-describedby="lmxBlockedCommitmentHelp">
                    </div>
                    <button class="lmx-button" type="submit">
                        <i class="fas fa-lock-open" aria-hidden="true"></i>
                        Activate commitment
                    </button>
                    <div class="lmx-status" role="status" aria-live="polite" aria-atomic="true"></div>
                </form>`;
            panel.querySelector("form")?.addEventListener("submit", event => {
                event.preventDefault();
                saveCommitmentAmountFromBlockedPanel(panel.querySelector("button[type='submit']"));
            });
            wireCommitmentAmountValidation("lmxBlockedCommitmentAmount");
            return;
        }

        const invoiceStatus = String(commitment.invoiceStatus || "");
        const hasInvoice = !!(commitment.invoiceId || commitment.checkoutLink || invoiceStatus);
        const replacesInvoice = ["expired", "failed", "invalid"].includes(invoiceStatus.toLowerCase());
        const payText = hasInvoice && !replacesInvoice ? "Open invoice" : "Create invoice";
        const payBusyText = hasInvoice && !replacesInvoice ? "Opening invoice..." : "Creating invoice...";
        const invoiceLine = commitment.checkoutLink && !replacesInvoice
            ? `<a class="lmx-payment-link" href="${escAttr(commitment.checkoutLink)}" target="_blank" rel="noopener">Open BTCPay invoice</a>`
            : "";
        const refreshDisabled = hasInvoice ? "" : " disabled";
        const refreshTitle = hasInvoice
            ? "Check whether BTCPay has confirmed the full payment."
            : "Create an invoice before refreshing payment status.";
        const hint = hasInvoice
            ? "Already paid? Refresh payment. Expired or failed invoices can be replaced from here."
            : "Create a BTCPay invoice. The amount is locked until this commitment is cleared.";
        const editableDays = getCommitmentEditableDays(state);
        panel.innerHTML = `
            <div class="lmx-commitment-card due">
                <div class="lmx-commitment-main">
                    <i class="fas fa-triangle-exclamation" aria-hidden="true"></i>
                    <div>
                        <strong>Commitment due</strong>
                        <span>${esc(commitment.message || "This check-in landed below your recent average. Pay the locked amount, or improve the editable check-in enough to clear it.")}</span>
                    </div>
                    <b aria-label="${escAttr(`Commitment due amount ${formatUsd(commitment.owedAmountUsd)}`)}">${esc(formatUsd(commitment.owedAmountUsd))}</b>
                </div>
                <div class="lmx-commitment-meta">
                    <span>Trigger: Day ${esc(commitment.triggerChallengeDay || "-")}</span>
                    <span>Score: ${esc(commitment.triggerScore ?? "-")}</span>
                    <span>Baseline: ${esc(formatNumber(commitment.thresholdAverage))}</span>
                    ${commitment.invoiceStatus ? `<span>Invoice: ${esc(commitment.invoiceStatus)}</span>` : ""}
                </div>
                <div class="lmx-button-row">
                    <button id="lmxCommitmentPayButton" class="lmx-button" type="button" data-busy-text="${escAttr(payBusyText)}">
                        <i class="fas fa-credit-card" aria-hidden="true"></i>
                        ${esc(payText)}
                    </button>
                    <button id="lmxCommitmentRefreshButton" class="lmx-button secondary" type="button" title="${escAttr(refreshTitle)}"${refreshDisabled}>
                        <i class="fas fa-rotate" aria-hidden="true"></i>
                        Refresh payment
                    </button>
                    ${invoiceLine}
                </div>
                <div class="lmx-commitment-hint">${esc(hint)}</div>
                <div id="lmxCommitmentStatus" class="lmx-status" role="status" aria-live="polite" aria-atomic="true"></div>
            </div>
            <div class="lmx-commitment-edit">
                <strong>Eligible fixes</strong>
                <div id="lmxCommitmentCheckinList" class="lmx-checkin-list"></div>
            </div>`;

        panel.querySelector("#lmxCommitmentPayButton")?.addEventListener("click", event => {
            payCommitment(event.currentTarget);
        });
        panel.querySelector("#lmxCommitmentRefreshButton")?.addEventListener("click", event => {
            refreshCommitmentPayment(event.currentTarget);
        });
        if (editableDays.length) {
            renderCheckIns(editableDays, "lmxCommitmentCheckinList");
        } else {
            const list = document.getElementById("lmxCommitmentCheckinList");
            if (list) {
                list.innerHTML = `<div class="lmx-empty-state">
                    <i class="fas fa-lock" aria-hidden="true"></i>
                    <strong>No editable fix available.</strong>
                    <span>The edit window closed, so payment is required to continue.</span>
                </div>`;
            }
        }
    }

    function hasCommitmentBlock(state) {
        return !!(state && state.commitment && state.commitment.blocksParticipant);
    }

    function getCommitmentEditableDays(state) {
        const triggerDay = Number(state?.commitment?.triggerChallengeDay || 0);
        return ((state && state.eligibleDays) || [])
            .filter(day => day.existing && (!triggerDay || day.challengeDay === triggerDay));
    }

    async function saveCommitmentAmountFromBlockedPanel(button) {
        if (!accessToken || !participantState) return;
        await withButton(button, async () => {
            const participant = participantState.participant || {};
            const result = await postJson(`${API}/edit`, {
                accessToken,
                displayName: participant.displayName || "",
                timeZoneId: participant.timeZoneId || Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
                athleteLink: participant.athleteSlug || participant.athleteUrl || null,
                commitmentAmountUsd: parseCommitmentAmount("lmxBlockedCommitmentAmount")
            });
            participantState = result;
            publicState = result.public;
            if (!hasCommitmentBlock(result)) {
                participantNotice = { message: "Commitment amount saved. You can continue.", isError: false };
                participantActiveTab = null;
                participantTabManual = false;
            }
            renderAll();
        }, "Saving...");
    }

    async function payCommitment(button) {
        if (!accessToken) return;
        const checkoutWindow = window.open("", "_blank", "noopener");
        await withStandaloneButton(button, button?.dataset.busyText || "Creating invoice...", async () => {
            const result = await postJson(`${API}/commitment-payment`, { accessToken });
            participantState = result;
            publicState = result.public;
            const checkoutLink = result.commitment && result.commitment.checkoutLink;
            if (!checkoutLink) throw new Error("The payment invoice did not return a checkout link.");
            if (checkoutWindow) {
                checkoutWindow.location = checkoutLink;
            } else {
                window.location.href = checkoutLink;
            }
            renderAll();
            setCommitmentStatus("Invoice opened. After paying, refresh payment status here.", false);
        }, err => {
            if (checkoutWindow) checkoutWindow.close();
            setCommitmentStatus(messageOf(err), true);
        });
    }

    async function refreshCommitmentPayment(button) {
        if (!accessToken) return;
        await withStandaloneButton(button, "Refreshing...", async () => {
            const result = await postJson(`${API}/commitment-payment/status`, { accessToken });
            participantState = result;
            publicState = result.public;
            if (!hasCommitmentBlock(result)) {
                participantNotice = { message: "Payment confirmed. Your check-ins are unlocked.", isError: false };
                participantActiveTab = null;
                participantTabManual = false;
                renderAll();
                return;
            }
            renderAll();
            setCommitmentStatus(commitmentRefreshMessage(result.commitment), true);
        }, err => setCommitmentStatus(messageOf(err), true));
    }

    function commitmentRefreshMessage(commitment) {
        const status = String(commitment?.invoiceStatus || "").trim();
        const normalized = status.toLowerCase();
        if (["expired", "failed", "invalid"].includes(normalized)) {
            return `Still due. The invoice is ${normalized}; create a new invoice when ready.`;
        }

        return "Still due. BTCPay has not confirmed the full locked amount yet.";
    }

    async function withStandaloneButton(button, busyText, work, onError) {
        const original = button ? button.innerHTML : "";
        if (button) {
            button.disabled = true;
            button.setAttribute("aria-busy", "true");
            button.innerHTML = `<i class="fas fa-spinner fa-spin" aria-hidden="true"></i>${busyText}`;
        }

        try {
            await work();
        } catch (err) {
            if (onError) onError(err);
        } finally {
            if (button) {
                button.disabled = false;
                button.removeAttribute("aria-busy");
                button.innerHTML = original;
            }
        }
    }

    function setCommitmentStatus(message, isError) {
        const status = document.getElementById("lmxCommitmentStatus");
        if (!status) return;
        status.textContent = message || "";
        status.classList.toggle("error", !!isError);
        status.classList.toggle("success", !!message && !isError);
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

    function renderParticipantCalls(calls, callSelectionClosesAtUtc) {
        const container = document.getElementById("lmxParticipantCalls");
        const visibleCalls = (calls || []).filter(call => !isParticipantCallDone(call));
        if (!visibleCalls.length) {
            container.innerHTML = "";
            return;
        }

        container.innerHTML = visibleCalls.map(call => {
            const timeZoneId = getParticipantTimeZone();
            const when = call.selectedSlot ? formatDateTime(call.selectedSlot.startsAtUtc, timeZoneId) : pendingCallTimeLabel(callSelectionClosesAtUtc, timeZoneId);
            const label = visibleCalls.length === 1 && isFinaleCall(call) ? "Next call" : call.label;
            const link = call.videoCallUrl
                ? `<a class="lmx-call-link" href="${escAttr(call.videoCallUrl)}" target="_blank" rel="noopener">Google Meet</a>`
                : "";
            return `<div class="lmx-call-group"><strong>${esc(label)}</strong><div class="lmx-call-meta"><span>${esc(when)}</span>${link}</div></div>`;
        }).join("");
    }

    function isParticipantCallDone(call) {
        const startsAtMs = call && call.selectedSlot ? Date.parse(call.selectedSlot.startsAtUtc) : NaN;
        return Number.isFinite(startsAtMs) && startsAtMs + CALL_ACTIVE_WINDOW_MS < Date.now();
    }

    function isFinaleCall(call) {
        const key = String(call && call.key || "").trim().toLowerCase();
        const label = String(call && call.label || "").trim().toLowerCase();
        return key === "finale" || label === "finale";
    }

    function renderBoard(state) {
        const board = document.getElementById("lmxBoard");
        if (isPreStartSignup(state)) {
            renderRosterBoard(board, state);
            return;
        }

        const publicViewer = !participantState;
        board.className = publicViewer ? "lmx-board public" : "lmx-board";
        const dayCount = (state.days || []).length || state.durationDays || 14;
        setBoardDayColumns(board, dayCount, false);
        updateInactiveToggle(state);
        const dayHeaders = (state.days || []).map(day => `<div class="lmx-cell">${day.challengeDay}</div>`).join("");
        const leaderboardRows = splitLeaderboardRows(state);
        const rows = leaderboardRows.visible.map(row => {
            const name = row.athleteUrl
                ? `<a href="${escAttr(row.athleteUrl)}">${esc(row.displayName)}</a>`
                : `<span>${esc(row.displayName)}</span>`;
            const participant = participantNameHtml(row, name);
            const cells = (row.cells || []).map(cell => {
                if (!cell.checkedIn) return `<div class="lmx-cell empty" data-day="${escAttr(cell.challengeDay)}" title="Day ${cell.challengeDay}"></div>`;
                if (cell.countsForScore === false) {
                    return practiceDayCellHtml(cell);
                }
                return scoredDayCellHtml(cell);
            }).join("");
            return `<div class="lmx-board-row${row.challengeEmailsStopped ? " inactive" : ""}" role="row">
                <div class="lmx-name" role="cell">${participant}</div>
                <div class="lmx-number" role="cell" data-label="Score">${row.totalPoints}</div>
                <div class="lmx-cell-strip" role="cell" aria-label="Daily scores">${cells}</div>
            </div>`;
        }).join("");

        board.innerHTML = `<div class="lmx-board-row header" role="row">
            <div class="lmx-name lmx-sticky-heading" role="columnheader">Participant</div>
            <div class="lmx-number lmx-sticky-heading" role="columnheader">Score</div>
            <div class="lmx-cell-strip lmx-header-days" role="presentation">${dayHeaders}</div>
        </div>${rows || emptyBoardRow(dayCount, leaderboardRows.inactive.length)}`;
    }

    function practiceDayCellHtml(cell) {
        const breakdown = habitBreakdown(cell);
        const title = practiceCellTitle(cell, breakdown);
        if (!breakdown.length) {
            return `<div class="lmx-cell practice" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(title)}" aria-label="${escAttr(title)}"><i class="fa fa-rocket" aria-hidden="true"></i></div>`;
        }

        const marks = breakdown.map(item => `<span class="${habitMarkClass(item.value)}" title="${escAttr(`${item.label} ${item.value}/2`)}" aria-hidden="true">${esc(item.short)}</span>`).join("");
        return `<div class="lmx-cell lmx-cell-breakdown practice" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(title)}" aria-label="${escAttr(title)}">
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
            return `<div class="lmx-cell ${scoreClass}" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(title)}" aria-label="${escAttr(title)}">${score}</div>`;
        }

        const rawScore = breakdown.reduce((sum, item) => sum + item.value, 0);
        const scoreClass = rawScore >= 6 ? "score-high" : rawScore >= 3 ? "score-mid" : "score-low";
        const marks = breakdown.map(item => `<span class="${habitMarkClass(item.value)}" title="${escAttr(`${item.label} ${item.value}/2`)}" aria-hidden="true">${esc(item.short)}</span>`).join("");
        return `<div class="lmx-cell lmx-cell-breakdown ${scoreClass}" data-day="${escAttr(cell.challengeDay)}" title="${escAttr(title)}" aria-label="${escAttr(title)}">
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
        const dayCount = (state.days || []).length || state.durationDays || 14;
        setBoardDayColumns(board, dayCount, true);
        updateInactiveToggle(state);
        const dayHeaders = (state.days || []).map(day => `<div class="lmx-cell">${day.challengeDay}</div>`).join("");
        const leaderboardRows = splitLeaderboardRows(state);
        const rows = leaderboardRows.visible.map(row => {
            const name = row.athleteUrl
                ? `<a href="${escAttr(row.athleteUrl)}">${esc(row.displayName)}</a>`
                : `<span>${esc(row.displayName)}</span>`;
            const participant = participantNameHtml(row, name);
            const cells = (row.cells || state.days || []).map(cell => `<div class="lmx-cell empty" data-day="${escAttr(cell.challengeDay)}" title="Day ${cell.challengeDay}"></div>`).join("");
            return `<div class="lmx-board-row lmx-roster-row${row.challengeEmailsStopped ? " inactive" : ""}" role="row">
                <div class="lmx-name" role="cell">${participant}</div>
                <div class="lmx-cell-strip" role="cell" aria-label="Challenge days">${cells}</div>
            </div>`;
        }).join("");

        board.innerHTML = `<div class="lmx-board-row lmx-roster-row header" role="row">
            <div class="lmx-name lmx-sticky-heading" role="columnheader">Participant</div>
            <div class="lmx-cell-strip lmx-header-days" role="presentation">${dayHeaders}</div>
        </div>${rows || emptyRosterRow(dayCount, leaderboardRows.inactive.length)}`;
    }

    function setBoardDayColumns(board, dayCount, rosterMode) {
        const count = Math.max(1, Math.trunc(Number(dayCount) || 14));
        board.style.setProperty("--lmx-day-columns", `repeat(${count}, 2.55rem)`);
        const baseWidthRem = rosterMode ? 14.35 : 21.15;
        board.style.setProperty("--lmx-board-min-width", `${(baseWidthRem + (count * 2.9)).toFixed(2)}rem`);
    }

    function splitLeaderboardRows(state) {
        const all = (state && state.leaderboard) || [];
        const active = all.filter(row => !row.challengeEmailsStopped);
        const inactive = all.filter(row => row.challengeEmailsStopped);
        return {
            active,
            inactive,
            visible: showInactiveLeaderboard ? [...active, ...inactive] : active
        };
    }

    function updateInactiveToggle(state) {
        const button = document.getElementById("lmxInactiveToggle");
        if (!button) return;
        const rows = splitLeaderboardRows(state);
        button.classList.toggle("lmx-hidden", rows.inactive.length === 0);
        button.setAttribute("aria-pressed", showInactiveLeaderboard ? "true" : "false");
        button.setAttribute("aria-label", showInactiveLeaderboard
            ? "Hide inactive participants"
            : `Show inactive participants (${rows.inactive.length})`);
        button.innerHTML = showInactiveLeaderboard
            ? `<i class="fas fa-users" aria-hidden="true"></i>Hide inactive`
            : `<i class="fas fa-users-slash" aria-hidden="true"></i>Show inactive (${rows.inactive.length})`;
    }

    function scrollBoardToLatestDay() {
        const scroller = document.querySelector("#lmxBoardSection .lmx-board-scroll");
        if (!scroller) return;

        const scrollRight = () => {
            scroller.scrollLeft = Math.max(0, scroller.scrollWidth - scroller.clientWidth);
        };

        requestAnimationFrame(() => {
            scrollRight();
            requestAnimationFrame(scrollRight);
            window.setTimeout(scrollRight, 120);
            window.setTimeout(scrollRight, 500);
            window.setTimeout(scrollRight, 1200);
        });

        if (document.fonts && document.fonts.ready) {
            document.fonts.ready.then(scrollRight).catch(() => { });
        }

        if (window.ResizeObserver && boardScrollObservedElement !== scroller) {
            if (boardScrollObserver) boardScrollObserver.disconnect();
            boardScrollObservedElement = scroller;
            boardScrollObserver = new ResizeObserver(scrollRight);
            boardScrollObserver.observe(scroller);
            const board = document.getElementById("lmxBoard");
            if (board) boardScrollObserver.observe(board);
        }
    }

    function scrollDashboardToLatestDay() {
        const scroller = document.querySelector("#lmxTrack .lmx-dashboard-scroll");
        if (!scroller) return;

        const scrollRight = () => {
            scroller.scrollLeft = Math.max(0, scroller.scrollWidth - scroller.clientWidth);
        };

        requestAnimationFrame(() => {
            scrollRight();
            requestAnimationFrame(scrollRight);
            window.setTimeout(scrollRight, 120);
            window.setTimeout(scrollRight, 500);
        });

        if (document.fonts && document.fonts.ready) {
            document.fonts.ready.then(scrollRight).catch(() => { });
        }

        if (window.ResizeObserver && dashboardScrollObservedElement !== scroller) {
            if (dashboardScrollObserver) dashboardScrollObserver.disconnect();
            dashboardScrollObservedElement = scroller;
            dashboardScrollObserver = new ResizeObserver(scrollRight);
            dashboardScrollObserver.observe(scroller);
            const dashboard = scroller.querySelector(".lmx-dashboard-grid");
            if (dashboard) dashboardScrollObserver.observe(dashboard);
        }
    }

    function emptyBoardRow(durationDays, hiddenInactiveCount) {
        const hasHiddenInactive = hiddenInactiveCount > 0 && !showInactiveLeaderboard;
        const message = hasHiddenInactive
            ? `${hiddenInactiveCount} inactive participant${hiddenInactiveCount === 1 ? " is" : "s are"} hidden`
            : "No one has joined yet";
        const scoreLabel = hasHiddenInactive ? "Inactive participants hidden" : "No score yet";
        return `<div class="lmx-board-row" role="row">
            <div class="lmx-name" role="cell">
                <span class="lmx-empty-name">${esc(message)}</span>
            </div>
            <div class="lmx-number lmx-empty-score" role="cell" data-label="Score" aria-label="${escAttr(scoreLabel)}">-</div>
            <div class="lmx-cell-strip" role="cell" aria-label="Daily scores">${Array.from({ length: durationDays }, (_, index) => `<div class="lmx-cell empty" data-day="${index + 1}"></div>`).join("")}</div>
        </div>`;
    }

    function emptyRosterRow(durationDays, hiddenInactiveCount) {
        const hasHiddenInactive = hiddenInactiveCount > 0 && !showInactiveLeaderboard;
        const message = hasHiddenInactive
            ? `${hiddenInactiveCount} inactive participant${hiddenInactiveCount === 1 ? " is" : "s are"} hidden`
            : "No one has joined yet";
        return `<div class="lmx-board-row lmx-roster-row" role="row">
            <div class="lmx-name" role="cell">
                <span class="lmx-empty-name">${esc(message)}</span>
            </div>
            <div class="lmx-cell-strip" role="cell" aria-label="Challenge days">${Array.from({ length: durationDays }, (_, index) => `<div class="lmx-cell empty" data-day="${index + 1}"></div>`).join("")}</div>
        </div>`;
    }

    function renderCheckIns(days, containerId) {
        const container = document.getElementById(containerId || "lmxCheckinList");
        if (!container) return;
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
                renderCheckIns(orderedDays, containerId);
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
                <textarea id="lmx-note-${day.challengeDay}" maxlength="240" placeholder="Visible publicly">${esc(note)}</textarea>
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

    function parseCommitmentAmount(inputId) {
        const input = document.getElementById(inputId);
        const raw = String(input?.value || "").trim();
        const normalized = raw.replace(/^\$/, "").trim();
        const value = Number(normalized);
        if (!Number.isFinite(value) || value < 1) {
            const message = raw ? "Commitment amount must be at least USD 1." : "Enter a commitment amount of at least USD 1.";
            markCommitmentAmountInvalid(input, message, true);
            input?.focus();
            throw new Error(message);
        }

        clearCommitmentAmountValidity(input);
        return Math.round(value * 100) / 100;
    }

    function wireCommitmentAmountValidation(inputId) {
        const input = document.getElementById(inputId);
        if (!input || input.dataset.commitmentValidationWired) return;
        input.dataset.commitmentValidationWired = "true";
        input.addEventListener("input", () => clearCommitmentAmountValidity(input));
        input.addEventListener("invalid", () => {
            const raw = String(input.value || "").trim();
            markCommitmentAmountInvalid(input, raw ? "Commitment amount must be at least USD 1." : "Enter a commitment amount of at least USD 1.");
        });
    }

    function markCommitmentAmountInvalid(input, message, report) {
        if (!input) return;
        input.setAttribute("aria-invalid", "true");
        if (typeof input.setCustomValidity === "function") {
            input.setCustomValidity(message);
            if (report) input.reportValidity?.();
        }
    }

    function clearCommitmentAmountValidity(input) {
        if (!input) return;
        input.removeAttribute("aria-invalid");
        if (typeof input.setCustomValidity === "function") input.setCustomValidity("");
    }

    function setCommitmentInputValue(inputId, value) {
        const input = document.getElementById(inputId);
        if (!input) return;
        input.value = value === null || value === undefined ? "" : String(value);
    }

    function formatUsd(value) {
        const amount = Number(value);
        if (!Number.isFinite(amount)) return "USD -";
        return new Intl.NumberFormat("en-US", {
            style: "currency",
            currency: "USD",
            minimumFractionDigits: amount % 1 === 0 ? 0 : 2,
            maximumFractionDigits: 2
        }).format(amount);
    }

    function formatNumber(value) {
        const number = Number(value);
        if (!Number.isFinite(number)) return "-";
        return new Intl.NumberFormat("en-US", { maximumFractionDigits: 2 }).format(number);
    }

    function participantNameHtml(row, nameHtml) {
        const athlete = findAthleteForParticipant(row);
        const profileImage = String(row.profileImageUrl || "").trim();
        const athleteProfileImage = isPlaceholderProfileImage(athlete?.profilePic) ? "" : (athlete?.profilePic || "");
        const image = athleteProfileImage || profileImage || ATHLETE_PLACEHOLDER_IMAGE;
        const hasProfileImage = !!(athleteProfileImage || profileImage);
        const avatarClass = hasProfileImage ? "lmx-participant-avatar" : "lmx-participant-avatar placeholder";
        const alt = hasProfileImage ? `${row.displayName || "Participant"} profile picture` : "";
        const badges = [
            row.commitmentStatus === "commitment-due" ? "Commitment due" : "",
            row.challengeEmailsStopped ? "Inactive" : ""
        ].filter(Boolean);
        return `<div class="lmx-participant-name">
            <span class="${avatarClass}" aria-hidden="${hasProfileImage ? "false" : "true"}">
                <img src="${escAttr(image)}" alt="${escAttr(alt)}" loading="lazy" decoding="async">
            </span>
            <span class="lmx-participant-label">${nameHtml}${badges.length ? `<span class="lmx-row-badges">${badges.map(badge => `<em>${esc(badge)}</em>`).join("")}</span>` : ""}</span>
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
                        name: String(a.DisplayName || a.Name || "").trim(),
                        legalName: String(a.Name || "").trim(),
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
            input.setAttribute("aria-haspopup", "listbox");
        });

        loadAthleteDirectory()
            .then(athletes => inputs.forEach(input => wireAthleteSelector(input, athletes)));
    }

    function wireAthleteSelector(input, athletes) {
        if (athleteSelectors.has(input.id)) return;

        let currentFocus = -1;
        const selected = document.getElementById(`${input.id}Selected`);
        const clearButton = document.getElementById(`${input.id}Clear`);
        const selector = {
            input,
            athletes,
            setValue(value) {
                const raw = String(value || "").trim();
                const normalized = normalizeAthleteSlug(raw);
                const match = athletes.find(a =>
                    normalizeAthleteSlug(a.slug) === normalized ||
                    a.name.toLowerCase() === raw.toLowerCase() ||
                    a.legalName.toLowerCase() === raw.toLowerCase());
                if (match) {
                    select(match);
                    return;
                }

                input.value = raw;
                clearSelection();
                updateSelectedState(null);
            },
            clear() {
                input.value = "";
                clearSelection();
                updateSelectedState(null);
                closeList();
            },
            getPayload() {
                const raw = input.value.trim();
                if (!raw) {
                    input.setCustomValidity?.("");
                    return null;
                }

                const selectedName = input.dataset.athleteName || "";
                if (input.dataset.athleteSlug && raw.toLowerCase() === selectedName.toLowerCase()) {
                    input.setCustomValidity?.("");
                    return input.dataset.athleteSlug;
                }

                const normalized = normalizeAthleteSlug(raw);
                const match = athletes.find(a =>
                    a.name.toLowerCase() === raw.toLowerCase() ||
                    a.legalName.toLowerCase() === raw.toLowerCase() ||
                    normalizeAthleteSlug(a.slug) === normalized);
                if (match) {
                    select(match);
                    input.setCustomValidity?.("");
                    return match.slug;
                }

                const message = "Select an athlete from the list or clear this field.";
                input.setCustomValidity?.(message);
                input.reportValidity?.();
                throw new Error(message);
            }
        };

        athleteSelectors.set(input.id, selector);
        selector.setValue(input.value);

        input.addEventListener("input", () => {
            input.setCustomValidity?.("");
            clearSelection();
            updateSelectedState(null);
            renderSuggestions();
        });

        input.addEventListener("focus", () => {
            if (!input.value.trim()) renderSuggestions(true);
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

        clearButton?.addEventListener("click", () => {
            selector.clear();
            input.focus();
        });

        function renderSuggestions(showInitial) {
            const query = input.value.trim().toLowerCase();
            const terms = query.split(/\s+/).filter(Boolean);
            closeList();
            if (!terms.length && !showInitial) return;

            const matches = athletes
                .filter(a => {
                    if (!terms.length) return true;
                    const name = a.name.toLowerCase();
                    const legalName = a.legalName.toLowerCase();
                    const slug = normalizeAthleteSlug(a.slug);
                    return terms.every(term => name.includes(term) || legalName.includes(term) || slug.includes(term));
                })
                .slice(0, 6);

            const list = document.createElement("div");
            list.id = `${input.id}-autocomplete-list`;
            list.className = "lmx-athlete-options";
            list.setAttribute("role", "listbox");
            input.closest(".lmx-athlete-picker")?.appendChild(list);
            input.setAttribute("aria-expanded", "true");

            if (!matches.length) {
                list.innerHTML = `<div class="lmx-athlete-empty" role="option" aria-disabled="true">No listed athlete found</div>`;
                return;
            }

            matches.forEach(athlete => {
                const item = document.createElement("div");
                item.className = "lmx-athlete-option";
                item.setAttribute("role", "option");
                item.innerHTML = `
                    ${athlete.profilePic ? `<img src="${escAttr(athlete.profilePic)}" alt="" loading="lazy">` : "<span class=\"lmx-athlete-fallback\"></span>"}
                    <span><span class="lmx-athlete-name">${highlightMatch(athlete.name, terms[0] || "")}</span><em>${esc(athlete.slug.replace(/_/g, "-"))}</em></span>`;
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
            input.setCustomValidity?.("");
            updateSelectedState(athlete);
        }

        function clearSelection() {
            delete input.dataset.athleteSlug;
            delete input.dataset.athleteName;
        }

        function updateSelectedState(athlete) {
            const hasSelection = !!athlete;
            input.classList.toggle("has-athlete-selection", hasSelection);
            clearButton?.classList.toggle("lmx-hidden", !hasSelection && !input.value.trim());

            if (!selected) return;
            selected.classList.toggle("lmx-hidden", !hasSelection);
            selected.innerHTML = hasSelection
                ? `${athlete.profilePic ? `<img src="${escAttr(athlete.profilePic)}" alt="" loading="lazy">` : "<span class=\"lmx-athlete-fallback\"></span>"}
                   <span><strong>${esc(athlete.name)}</strong><em>${esc(athlete.slug.replace(/_/g, "-"))}</em></span>`
                : "";
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
        const selector = athleteSelectors.get(id);
        if (selector) return selector.getPayload();

        const input = document.getElementById(id);
        if (!input) return null;

        const raw = input.value.trim();
        if (!raw) return null;

        const message = "Select an athlete from the list or clear this field.";
        input.setCustomValidity?.(message);
        input.reportValidity?.();
        throw new Error(message);
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
        const selector = athleteSelectors.get(id);
        if (selector) {
            selector.clear();
            return;
        }

        const input = document.getElementById(id);
        if (!input) return;
        input.value = "";
        delete input.dataset.athleteSlug;
        delete input.dataset.athleteName;
        input.setCustomValidity?.("");
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
        button.setAttribute("aria-busy", "true");
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
            button.removeAttribute("aria-busy");
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
            default: return "loading";
        }
    }

    function isPreStartSignup(state) {
        return !!state && state.phase === "signup";
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

    function pendingCallTimeLabel(callSelectionClosesAtUtc, timeZoneId) {
        return "Meeting time pending.";
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
        if (!el) return;
        el.classList.toggle("lmx-hidden", !visible);
        el.toggleAttribute("hidden", !visible);
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
