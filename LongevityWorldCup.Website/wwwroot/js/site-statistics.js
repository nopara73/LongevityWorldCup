(function () {
    "use strict";

    const trafficOverviewTab = "Traffic Overview";
    const onboardingDiagnosticsTab = "Onboarding Diagnostics";
    const challengeDiagnosticsTab = "Challenge Diagnostics";
    const sourceQualityTab = "Source Quality";
    const reliabilityDiagnosticsTab = "Reliability Diagnostics";
    const reviewDiagnosticsTab = "Review Queue Diagnostics";
    const publicEventsDiagnosticsTab = "Public Event Diagnostics";
    const trafficOverviewEventLimit = 100;
    const diagnosticEventLimit = 5000;
    const tabs = [
        trafficOverviewTab,
        onboardingDiagnosticsTab,
        challengeDiagnosticsTab,
        sourceQualityTab,
        reliabilityDiagnosticsTab,
        reviewDiagnosticsTab,
        publicEventsDiagnosticsTab
    ];
    const legacyTabs = {
        "Onboarding": onboardingDiagnosticsTab,
        "Challenge": challengeDiagnosticsTab,
        "Traffic": sourceQualityTab,
        "Reliability": reliabilityDiagnosticsTab,
        "Review Queue": reviewDiagnosticsTab,
        "Events": publicEventsDiagnosticsTab
    };
    const state = {
        tab: trafficOverviewTab,
        flow: "all",
        selectedFlow: "pheno",
        events: [],
        defaultEvents: [],
        previousEvents: [],
        previousDefaultEvents: [],
        trafficSummary: emptyTrafficSummary(),
        loadedEventLimit: 0,
        decisionActions: [],
        selectedEventName: null,
        selectedSession: null,
        selectedLabel: "all events"
    };

    const funnelDefs = {
        pheno: [
            ["onboarding_entry_viewed", "Join page viewed"],
            ["onboarding_clock_selected", "Amateur selected"],
            ["onboarding_biomarker_requirements_opened", "Biomarkers inspected"],
            ["onboarding_page_viewed", "Page viewed"],
            ["calculator_form_visible", "Form visible"],
            ["calculator_started", "Calculator started"],
            ["calculator_field_touched", "First field touched"],
            ["calculator_field_completed", "Field completed"],
            ["calculator_required_progress_changed", "Required progress"],
            ["calculator_all_required_fields_completed", "All fields completed"],
            ["calculator_validation_failed", "Validation failed"],
            ["calculator_result_generated", "Result generated"],
            ["rank_preview_requested", "Rank preview requested"],
            ["rank_preview_rendered", "Rank preview rendered"],
            ["calculator_continue_clicked", "Continue clicked"],
            ["payment_offer_stored", "Payment offer stored"],
            ["biomarker_handoff_stored", "Biomarker handoff stored"],
            ["proof_flow_opened", "Proof/profile opened"]
        ],
        bortz: [
            ["onboarding_entry_viewed", "Join page viewed"],
            ["onboarding_clock_selected", "Pro selected"],
            ["onboarding_biomarker_requirements_opened", "Biomarkers inspected"],
            ["onboarding_page_viewed", "Page viewed"],
            ["calculator_form_visible", "Form visible"],
            ["calculator_started", "Calculator started"],
            ["calculator_field_touched", "First field touched"],
            ["calculator_field_completed", "Field completed"],
            ["calculator_required_progress_changed", "Required progress"],
            ["calculator_all_required_fields_completed", "All fields completed"],
            ["calculator_validation_failed", "Validation failed"],
            ["calculator_result_generated", "Result generated"],
            ["rank_preview_requested", "Rank preview requested"],
            ["rank_preview_rendered", "Rank preview rendered"],
            ["calculator_continue_clicked", "Continue clicked"],
            ["payment_offer_stored", "Payment offer stored"],
            ["biomarker_handoff_stored", "Biomarker handoff stored"],
            ["proof_flow_opened", "Proof/profile opened"]
        ],
        application: [
            ["proof_flow_opened", "Application/proof opened"],
            ["biomarker_handoff_found", "Handoff found"],
            ["proof_flow_missing_handoff", "Missing handoff"],
            ["profile_picture_started", "Profile picture started"],
            ["proof_upload_clicked", "Proof upload clicked"],
            ["proof_camera_clicked", "Proof camera clicked"],
            ["proof_files_selected", "Proof files selected"],
            ["proof_file_rejected", "Proof file rejected"],
            ["proof_processing_succeeded", "Proof processed"],
            ["proof_processing_failed", "Proof processing failed"],
            ["application_submit_clicked", "Submit clicked"],
            ["application_submit_succeeded", "Submit accepted"],
            ["application_submit_failed", "Submit failed"],
            ["payment_unavailable", "Payment unavailable"],
            ["checkout_redirect_started", "Checkout redirect"],
            ["application_review_opened", "Review opened"],
            ["application_review_context_found", "Review context found"],
            ["application_review_context_missing", "Review context missing"],
            ["payment_status_checked", "Payment status checked"],
            ["payment_status_failed", "Payment status failed"]
        ],
        challenge: [
            ["challenge_page_viewed", "Challenge viewed"],
            ["challenge_public_state_loaded", "Public state loaded"],
            ["challenge_signup_tab_opened", "Signup tab opened"],
            ["challenge_signup_started", "Signup started"],
            ["challenge_email_validation_failed", "Email validation failed"],
            ["challenge_identity_selected", "Identity selected"],
            ["challenge_athlete_search_started", "Athlete search started"],
            ["challenge_athlete_search_result_selected", "Athlete selected"],
            ["challenge_pledge_touched", "Pledge touched"],
            ["challenge_pledge_validation_failed", "Pledge validation failed"],
            ["challenge_pledge_completed", "Pledge completed"],
            ["challenge_timezone_picker_opened", "Timezone opened"],
            ["challenge_timezone_selected", "Timezone selected"],
            ["challenge_signup_submitted", "Signup submitted"],
            ["challenge_signup_succeeded", "Signup accepted"],
            ["challenge_signup_failed", "Signup failed"],
            ["challenge_confirmation_link_opened", "Confirmation opened"],
            ["challenge_participant_page_opened", "Participant page opened"],
            ["challenge_participant_state_loaded", "Participant state loaded"],
            ["challenge_practice_checkin_started", "Practice started"],
            ["challenge_practice_checkin_submitted", "Practice submitted"],
            ["challenge_scored_checkin_started", "Scored check-in started"],
            ["challenge_scored_checkin_submitted", "Scored check-in submitted"],
            ["challenge_stop_email_clicked", "Stop emails clicked"],
            ["challenge_commitment_payment_opened", "Commitment payment opened"],
            ["challenge_commitment_resolved", "Commitment resolved"]
        ]
    };

    const outcomeTiles = [
        ["Page views", ["site_page_viewed", "onboarding_entry_viewed", "onboarding_page_viewed", "challenge_page_viewed"]],
        ["Calculator starts", ["calculator_started"]],
        ["Calculator results", ["calculator_result_generated"]],
        ["Rank previews", ["rank_preview_rendered"]],
        ["Application starts", ["proof_flow_opened"]],
        ["Applications submitted", ["application_submit_succeeded"]],
        ["Challenge signups", ["challenge_signup_succeeded"]],
        ["Practice check-ins", ["challenge_practice_checkin_submitted"]],
        ["First scored", ["challenge_scored_checkin_submitted"]],
        ["Pending review", ["application_review_opened"]],
        ["Friction", []],
        ["Error rate", []]
    ];

    document.addEventListener("DOMContentLoaded", init);

    function init() {
        wireControls();
        renderTabs();
        readUrlState();
        loadDashboard();
    }

    function wireControls() {
        for (const id of ["statsRange", "statsFlow", "statsDevice", "statsSource"]) {
            el(id).addEventListener("change", () => {
                if (id === "statsFlow") state.flow = el(id).value;
                updateUrl();
                loadDashboard();
            });
        }
        el("statsReset").addEventListener("click", () => {
            el("statsRange").value = "7d";
            el("statsFlow").value = "all";
            el("statsDevice").value = "all";
            el("statsSource").value = "all";
            state.tab = trafficOverviewTab;
            state.flow = "all";
            state.selectedFlow = "pheno";
            state.selectedEventName = null;
            state.selectedSession = null;
            updateUrl();
            loadDashboard();
        });
        el("statsExport").addEventListener("click", exportCsv);
        el("resetDrilldown").addEventListener("click", () => selectDrilldown(null, "all events"));
        el("copyLink").addEventListener("click", copyLink);
    }

    function readUrlState() {
        const params = new URLSearchParams(window.location.search);
        const requestedTab = params.get("tab");
        if (requestedTab && legacyTabs[requestedTab]) state.tab = legacyTabs[requestedTab];
        else if (requestedTab && tabs.includes(requestedTab)) state.tab = requestedTab;
        if (params.get("selectedFlow")) state.selectedFlow = params.get("selectedFlow");
        for (const [id, key] of [["statsRange", "range"], ["statsFlow", "flow"], ["statsDevice", "device"], ["statsSource", "source"]]) {
            if (params.get(key)) el(id).value = params.get(key);
        }
        state.flow = el("statsFlow").value;
        state.selectedEventName = params.get("event");
        state.selectedSession = params.get("session");
    }

    function updateUrl() {
        const params = new URLSearchParams();
        params.set("tab", state.tab);
        params.set("range", el("statsRange").value);
        params.set("flow", el("statsFlow").value);
        params.set("device", el("statsDevice").value);
        params.set("source", el("statsSource").value);
        params.set("selectedFlow", state.selectedFlow);
        if (state.selectedEventName) params.set("event", state.selectedEventName);
        if (state.selectedSession) params.set("session", state.selectedSession);
        history.replaceState({}, "", `${window.location.pathname}?${params.toString()}`);
    }

    async function loadDashboard() {
        setStatus("Loading redacted analytics...");
        const params = new URLSearchParams({
            range: el("statsRange").value,
            flow: el("statsFlow").value,
            device: el("statsDevice").value,
            source: el("statsSource").value,
            limit: String(dashboardEventLimit())
        });

        try {
            const response = await fetch(`/api/site-statistics/dashboard?${params.toString()}`, { headers: { "Accept": "application/json" } });
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const payload = await response.json();
            state.events = Array.isArray(payload.events) ? payload.events.map(normalizeEvent) : [];
            state.previousEvents = Array.isArray(payload.previousEvents) ? payload.previousEvents.map(normalizeEvent) : [];
            state.trafficSummary = normalizeTrafficSummary(payload.trafficSummary);
            state.loadedEventLimit = Number(payload.filters && payload.filters.limit) || dashboardEventLimit();
            state.defaultEvents = collapseRepeatedPageViewBursts(state.events);
            state.previousDefaultEvents = collapseRepeatedPageViewBursts(state.previousEvents);
            const totals = state.trafficSummary.totals;
            setStatus(`${formatNumber(totals.sessions)} visitor sessions and ${formatNumber(totals.pageViews)} page views loaded. ${state.events.length} recent diagnostic rows available. Generated ${formatTime(payload.generatedAtUtc)}.`);
            renderAll();
        } catch (error) {
            state.events = [];
            state.defaultEvents = [];
            state.previousEvents = [];
            state.previousDefaultEvents = [];
            state.trafficSummary = emptyTrafficSummary();
            state.loadedEventLimit = 0;
            setStatus(`Dashboard data could not be loaded: ${error.message || "unknown error"}.`, true);
            renderAll();
        }
    }

    function dashboardEventLimit() {
        return state.tab === trafficOverviewTab ? trafficOverviewEventLimit : diagnosticEventLimit;
    }

    function needsDiagnosticEventReload() {
        return state.tab !== trafficOverviewTab && state.loadedEventLimit < diagnosticEventLimit;
    }

    function renderAll() {
        renderTabs();
        renderPageMode();
        renderTrafficOverview();
        renderDataQuality();
        renderDecisionLayer();
        renderOutcomeStrip();
        renderPrimaryPanel();
        renderFriction();
        renderDetailSections();
        renderDrilldown();
        updateUrl();
    }

    function renderPageMode() {
        const traffic = state.tab === trafficOverviewTab;
        el("trafficOverview").hidden = !traffic;
        for (const id of ["dataQualityStrip", "decisionGrid", "decisionSupportGrid", "outcomeStrip", "drilldownPanel", "statsMainGrid", "detailSections"]) {
            el(id).hidden = traffic;
        }
    }

    function renderTrafficOverview() {
        const host = el("trafficOverview");
        if (state.tab !== trafficOverviewTab) {
            host.innerHTML = "";
            return;
        }

        const summary = state.trafficSummary || emptyTrafficSummary();
        const totals = summary.totals || emptyTrafficTotals();
        const previous = summary.previousTotals || emptyTrafficTotals();
        const clean = summary.cleanTotals || emptyTrafficTotals();
        const quality = summary.quality || emptyTrafficQuality();
        host.innerHTML = `
            <section class="traffic-metrics" aria-label="Traffic totals">
                ${trafficMetric("Visitor sessions", totals.sessions, previous.sessions, `${formatNumber(clean.sessions)} clean / ${formatNumber(quality.noisySessions)} noisy`)}
                ${trafficMetric("Clean sessions", clean.sessions, null, `${percentNumber(ratio(clean.sessions, totals.sessions))} of raw`)}
                ${trafficMetric("Page views", totals.pageViews, previous.pageViews)}
                ${trafficMetric("Clean page views", clean.pageViews, null, `${percentNumber(ratio(clean.pageViews, totals.pageViews))} of raw`)}
                ${trafficMetric("Interactions", totals.events, previous.events)}
                ${trafficMetric("Ranked pages", (summary.topPages || []).length, null, "top page list")}
            </section>
            ${successTrendPanel(summary)}
            ${trafficQualityPanel(summary)}
            <section class="stats-panel traffic-chart-panel">
                <div class="panel-heading">
                    <h2>Daily traffic</h2>
                    <span class="panel-meta">${formatNumber((summary.daily || []).length)} days</span>
                </div>
                ${dailyTrafficChart(summary.daily || [])}
            </section>
            <section class="traffic-grid" aria-label="Traffic breakdowns">
                ${trafficPanel("Top pages", trafficPageTable(summary.topPages || []))}
                ${trafficPanel("Sources", trafficBreakdownTable(summary.sources || []))}
                ${trafficPanel("Referrers", trafficBreakdownTable(summary.referrers || []))}
                ${trafficPanel("Devices", trafficBreakdownTable(summary.devices || []))}
                ${trafficPanel("Browsers", trafficBreakdownTable(summary.browsers || []))}
            </section>
        `;
    }

    function trafficQualityPanel(summary) {
        const totals = summary.totals || emptyTrafficTotals();
        const clean = summary.cleanTotals || emptyTrafficTotals();
        const quality = summary.quality || emptyTrafficQuality();
        const noisyLevel = quality.noisySessions ? "warn" : "good";
        const topLevel = quality.topSessionShare >= 0.5 ? "bad" : quality.topSessionShare >= 0.25 ? "warn" : "good";
        const viewLevel = quality.noisyPageViewShare >= 0.5 ? "bad" : quality.noisyPageViewShare >= 0.25 ? "warn" : "good";
        const repeatedLevel = quality.repeatedPageViewSessions ? "warn" : "good";
        return `
            <section class="stats-panel traffic-quality-panel">
                <div class="panel-heading">
                    <h2>Clean vs raw traffic</h2>
                    <span class="panel-meta">${esc(pageViewMixLabel(quality))}</span>
                </div>
                <div class="traffic-quality-grid">
                    ${trafficQualityCard("Raw sessions", totals.sessions, `${formatNumber(totals.pageViews)} raw page views`, "")}
                    ${trafficQualityCard("Clean sessions", clean.sessions, `${percentNumber(ratio(clean.sessions, totals.sessions))} of raw sessions`, clean.sessions === totals.sessions ? "good" : "warn")}
                    ${trafficQualityCard("Noisy sessions", quality.noisySessions, `${formatNumber(quality.rawSessions)} raw sessions`, noisyLevel)}
                    ${trafficQualityCard("Top-session share", percentNumber(quality.topSessionShare), `${formatNumber(quality.topSessionEvents)} interactions in one session`, topLevel)}
                    ${trafficQualityCard("Noisy page-view share", percentNumber(quality.noisyPageViewShare), `${formatNumber(quality.noisyPageViews)} noisy page views`, viewLevel)}
                    ${trafficQualityCard("Repeated-refresh sessions", quality.repeatedPageViewSessions, `${formatNumber(quality.pageViewDominantSessions)} page-view-heavy`, repeatedLevel)}
                </div>
            </section>
        `;
    }

    function trafficQualityCard(label, value, detail, level) {
        return `
            <div class="traffic-quality-card ${escAttr(level)}">
                <span>${esc(label)}</span>
                <strong>${esc(String(value))}</strong>
                <em>${esc(detail)}</em>
            </div>
        `;
    }

    function pageViewMixLabel(quality) {
        if ((quality.noisyPageViewShare || 0) >= 0.5) return "page views dominated by noisy sessions";
        if ((quality.noisyPageViewShare || 0) >= 0.25) return "page views pressured by noisy sessions";
        if ((quality.repeatedPageViewSessions || 0) > 0) return "repeated refreshes detected";
        return "page views mostly clean";
    }

    function trafficMetric(label, value, previous, detail) {
        const current = Number(value) || 0;
        const previousValue = previous === null || previous === undefined ? null : Number(previous) || 0;
        const delta = previousValue === null
            ? detail || ""
            : `${signedNumber(current - previousValue)} vs previous`;
        const level = previousValue === null || current === previousValue
            ? ""
            : current > previousValue ? "good" : "warn";
        return `
            <div class="traffic-metric ${escAttr(level)}">
                <span>${esc(label)}</span>
                <strong>${esc(formatNumber(current))}</strong>
                <em>${esc(delta)}</em>
            </div>
        `;
    }

    function successTrendPanel(summary) {
        const points = summary.daily || [];
        const stats = successTrendStats(points);
        return `
            <section class="stats-panel success-trend-panel">
                <div class="panel-heading">
                    <h2>Conversions over time</h2>
                    <span class="panel-meta">${esc(selectedRangeLabel())}</span>
                </div>
                <div class="success-summary" aria-label="Conversion summary">
                    ${successTrendCard("Conversion actions", stats.actions, "calculator results / submitted applications / challenge signups")}
                    ${successTrendCard("Converting sessions", stats.sessions, `${percentNumber(stats.rate)} of visitor sessions`)}
                    ${successTrendCard("Best conversion day", stats.bestDayLabel, stats.bestDayRate)}
                </div>
                ${successTrendChart(points)}
            </section>
        `;
    }

    function successTrendCard(label, value, detail) {
        return `
            <div class="success-summary-card">
                <span>${esc(label)}</span>
                <strong>${esc(String(value))}</strong>
                <em>${esc(detail)}</em>
            </div>
        `;
    }

    function successTrendStats(points) {
        const actions = points.reduce((sum, point) => sum + (Number(point.successActions) || 0), 0);
        const sessions = points.reduce((sum, point) => sum + (Number(point.successSessions) || 0), 0);
        const visitorSessions = points.reduce((sum, point) => sum + (Number(point.sessions) || 0), 0);
        const best = points.reduce((winner, point) => {
            const currentRate = successRate(point);
            return !winner || currentRate > winner.rate ? { point, rate: currentRate } : winner;
        }, null);
        return {
            actions: formatNumber(actions),
            sessions: formatNumber(sessions),
            rate: ratio(sessions, visitorSessions),
            bestDayLabel: best ? formatDay(best.point.day) : "-",
            bestDayRate: best ? percentNumber(best.rate) : "0%"
        };
    }

    function successTrendChart(points) {
        if (!points.length) return empty("No conversion data for the active timeframe.");

        const width = Math.max(720, points.length * 58);
        const height = 260;
        const left = 46;
        const right = 18;
        const top = 20;
        const bottom = 50;
        const plotWidth = width - left - right;
        const plotHeight = height - top - bottom;
        const maxRate = Math.max(0.05, ...points.map(successRate));
        const maxActions = Math.max(1, ...points.map(point => Number(point.successActions) || 0));
        const labelsEvery = Math.max(1, Math.ceil(points.length / 9));
        const barWidth = Math.max(6, Math.min(26, (plotWidth / Math.max(1, points.length)) * 0.36));
        const x = index => left + (points.length === 1 ? plotWidth / 2 : (index / (points.length - 1)) * plotWidth);
        const yRate = value => top + (1 - ((Number(value) || 0) / maxRate)) * plotHeight;
        const yActions = value => top + (1 - ((Number(value) || 0) / maxActions)) * plotHeight;
        const rateLine = points.map((point, index) => `${x(index).toFixed(1)},${yRate(successRate(point)).toFixed(1)}`).join(" ");
        const gridLines = [0, 0.25, 0.5, 0.75, 1].map(step => {
            const y = top + step * plotHeight;
            return `<line class="success-grid-line" x1="${left}" y1="${y.toFixed(1)}" x2="${width - right}" y2="${y.toFixed(1)}"></line>`;
        }).join("");

        return `
            <div class="success-trend-wrap">
                <svg class="success-trend-svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" role="img" aria-label="Conversions over time">
                    ${gridLines}
                    <text class="success-axis-label" x="${left - 8}" y="${top + 4}" text-anchor="end">${esc(percentNumber(maxRate))}</text>
                    <text class="success-axis-label" x="${left - 8}" y="${top + plotHeight}" text-anchor="end">0%</text>
                    ${points.map((point, index) => {
                        const cx = x(index);
                        const actions = Number(point.successActions) || 0;
                        const rate = successRate(point);
                        const barHeight = Math.max(actions ? 4 : 0, top + plotHeight - yActions(actions));
                        const barY = top + plotHeight - barHeight;
                        const showLabel = index === 0 || index === points.length - 1 || index % labelsEvery === 0;
                        return `
                            <g>
                                <title>${esc(`${point.day}: ${formatNumber(point.successActions)} conversion actions, ${formatNumber(point.successSessions)} converting sessions, ${percentNumber(rate)} session conversion rate`)}</title>
                                <rect class="success-action-bar" x="${(cx - barWidth / 2).toFixed(1)}" y="${barY.toFixed(1)}" width="${barWidth.toFixed(1)}" height="${barHeight.toFixed(1)}" rx="4"></rect>
                                ${showLabel ? `<text class="success-day-label" x="${cx.toFixed(1)}" y="${height - 16}" text-anchor="middle">${esc(formatDay(point.day))}</text>` : ""}
                            </g>
                        `;
                    }).join("")}
                    <polyline class="success-rate-line" points="${rateLine}"></polyline>
                    ${points.map((point, index) => `
                        <circle class="success-rate-dot" cx="${x(index).toFixed(1)}" cy="${yRate(successRate(point)).toFixed(1)}" r="3.4"></circle>
                    `).join("")}
                </svg>
            </div>
            <div class="traffic-legend" aria-label="Conversion legend">
                <span><i class="success-rate"></i>Session conversion rate</span>
                <span><i class="success-actions"></i>Conversion actions</span>
            </div>
        `;
    }

    function successRate(point) {
        return ratio(point && point.successSessions, point && point.sessions);
    }

    function selectedRangeLabel() {
        const range = el("statsRange");
        const option = range.options[range.selectedIndex];
        return option ? option.text : "selected timeframe";
    }

    function dailyTrafficChart(points) {
        if (!points.length) return empty("No daily traffic for the active filters.");

        const maxValue = Math.max(1, ...points.flatMap(point => [point.sessions, point.pageViews]));
        return `
            <div class="traffic-chart">
                ${points.map(point => `
                    <div class="traffic-day">
                        <div class="traffic-bars" aria-label="${escAttr(`${point.day}: ${point.sessions} sessions, ${point.pageViews} page views`)}">
                            <span class="traffic-bar sessions" style="height:${trafficBarHeight(point.sessions, maxValue)}%"></span>
                            <span class="traffic-bar page-views" style="height:${trafficBarHeight(point.pageViews, maxValue)}%"></span>
                        </div>
                        <strong>${esc(formatDay(point.day))}</strong>
                        <span>${esc(formatNumber(point.sessions))} sessions</span>
                        <span>${esc(formatNumber(point.pageViews))} views</span>
                    </div>
                `).join("")}
            </div>
            <div class="traffic-legend" aria-label="Daily traffic legend">
                <span><i class="sessions"></i>Sessions</span>
                <span><i class="page-views"></i>Page views</span>
            </div>
        `;
    }

    function trafficBarHeight(value, maxValue) {
        return Math.max(4, Math.round(((Number(value) || 0) / Math.max(1, maxValue)) * 100));
    }

    function trafficPanel(title, content) {
        return `<section class="stats-panel traffic-panel"><div class="panel-heading"><h2>${esc(title)}</h2></div>${content}</section>`;
    }

    function trafficPageTable(rows) {
        if (!rows.length) return empty("No page-view traffic for the active filters.");
        return table(
            ["Page", "Sessions", "Page views"],
            rows.map(row => [row.route || "unknown", formatNumber(row.sessions), formatNumber(row.pageViews)]));
    }

    function trafficBreakdownTable(rows) {
        if (!rows.length) return empty("No traffic for the active filters.");
        const maxSessions = Math.max(1, ...rows.map(row => Number(row.sessions) || 0));
        return `
            <div class="traffic-breakdown">
                ${rows.map(row => `
                    <div class="traffic-breakdown-row">
                        <strong>${esc(row.label || "unknown")}</strong>
                        <span class="traffic-breakdown-bar"><span style="width:${barWidth(row.sessions, maxSessions)}%"></span></span>
                        <span>${esc(formatNumber(row.sessions))} sessions</span>
                        <span>${esc(formatNumber(row.pageViews))} views</span>
                    </div>
                `).join("")}
            </div>
        `;
    }

    function renderDataQuality() {
        const host = el("dataQualityStrip");
        const active = decisionScopeEvents(defaultEvents());
        const previous = decisionScopeEvents(defaultPreviousEvents());
        const rawActive = decisionScopeEvents(state.events);
        const rawPrevious = decisionScopeEvents(state.previousEvents);
        const quality = dataQualityStats(rawActive, rawPrevious);
        const cards = [
            {
                label: "Sample size",
                value: `${quality.sessions} sessions`,
                detail: `${active.length} default events from ${quality.events} raw`,
                level: quality.sessions >= 10 ? "good" : quality.sessions >= 4 ? "warn" : ""
            },
            {
                label: "Noisy sessions",
                value: String(quality.noisySessions),
                detail: `${quality.cleanSessions} clean sessions`,
                level: quality.noisySessions ? "warn" : "good"
            },
            {
                label: "Top-session share",
                value: percentNumber(quality.topSessionShare),
                detail: quality.topSessionEvents ? `${quality.topSessionEvents} events in one anonymous session` : "no events yet",
                level: quality.topSessionShare >= 0.5 ? "bad" : quality.topSessionShare >= 0.25 ? "warn" : "good"
            },
            {
                label: "Comparison baseline",
                value: uniqueSessions(previous) ? `${uniqueSessions(previous)} sessions` : "pending",
                detail: uniqueSessions(previous) ? `${previous.length} default events from ${quality.previousEvents} raw` : "trends should be read as sparse",
                level: uniqueSessions(previous) ? "good" : "warn"
            }
        ];

        host.innerHTML = cards.map(card => `
            <div class="quality-card ${escAttr(card.level)}">
                <span>${esc(card.label)}</span>
                <strong>${esc(card.value)}</strong>
                <em>${esc(card.detail)}</em>
            </div>
        `).join("");
    }

    function renderDecisionLayer() {
        state.decisionActions = [];
        const active = decisionScopeEvents(defaultEvents());
        const previous = decisionScopeEvents(defaultPreviousEvents());
        const insights = buildDecisionInsights(active, previous).slice(0, 7);
        renderDecisionBrief(insights, active, previous);
        renderRecommendedInvestigations(insights, active);
        renderSegmentComparisons(active, previous);
        renderTrendWatch(active, previous);
        wireDecisionActions();
    }

    function renderDecisionBrief(insights, active, previous) {
        const host = el("decisionBrief");
        el("decisionBriefMeta").textContent = `${uniqueSessions(active)} sessions / ${uniqueSessions(previous)} previous`;
        if (!active.length) {
            host.innerHTML = empty("No decision signal yet for the active filters.");
            return;
        }

        if (!insights.length) {
            host.innerHTML = `
                <button type="button" class="decision-card calm" data-action="${registerDecisionAction("Inspect active onboarding sessions", active)}">
                    <span class="decision-rank">0</span>
                    <span class="decision-body">
                        <span class="decision-title">No high-confidence product issue detected</span>
                        <span class="decision-gridline"><strong>Flow</strong><span>${esc(decisionLensLabel())}</span></span>
                        <span class="decision-gridline"><strong>Evidence</strong><span>${uniqueSessions(active)} sessions across ${active.length} events</span></span>
                        <span class="decision-gridline"><strong>Hypothesis</strong><span>Current sample is too small or too healthy to prioritize a fix.</span></span>
                        <span class="decision-gridline"><strong>Action</strong><span>Inspect recent sessions or wait for more data.</span></span>
                        <span class="decision-footer"><span class="severity low">LOW</span><span>confidence low</span></span>
                    </span>
                </button>
            `;
            return;
        }

        host.innerHTML = insights.map((insight, index) => `
            <button type="button" class="decision-card ${escAttr(insight.severity)}" data-action="${registerDecisionAction(insight.title, insight.events)}">
                <span class="decision-rank">${index + 1}</span>
                <span class="decision-body">
                    <span class="decision-title">${esc(insight.title)}</span>
                    <span class="decision-gridline"><strong>Flow</strong><span>${esc(flowLabel(insight.flow))}</span></span>
                    <span class="decision-gridline"><strong>Evidence</strong><span>${esc(insight.evidence)}</span></span>
                    <span class="decision-gridline"><strong>Hypothesis</strong><span>${esc(insight.hypothesis)}</span></span>
                    <span class="decision-gridline"><strong>Action</strong><span>${esc(insight.action)}</span></span>
                    <span class="decision-footer">
                        <span class="severity ${escAttr(insight.severity)}">${esc(insight.severity.toUpperCase())}</span>
                        <span>${esc(insight.confidence)} confidence</span>
                        <span>${esc(insight.trend)}</span>
                    </span>
                </span>
            </button>
        `).join("");
    }

    function renderRecommendedInvestigations(insights, active) {
        const host = el("recommendedInvestigations");
        const investigations = buildRecommendedInvestigations(insights, active).slice(0, 6);
        el("investigationMeta").textContent = `${investigations.length} next clicks`;
        if (!investigations.length) {
            host.innerHTML = empty("No concrete investigation is ready for this sample.");
            return;
        }

        host.innerHTML = investigations.map(item => `
            <button type="button" class="investigation-row" data-action="${registerDecisionAction(item.title, item.events)}">
                <strong>${esc(item.title)}</strong>
                <span>${esc(item.reason)}</span>
                <em>${esc(item.sessions)} sessions</em>
            </button>
        `).join("");
    }

    function renderSegmentComparisons(active, previous) {
        const host = el("segmentComparisons");
        const rows = buildSegmentRows(active, previous).slice(0, 10);
        el("segmentMeta").textContent = `${rows.length} segments`;
        if (!rows.length) {
            host.innerHTML = empty("No segment comparison is available yet.");
            return;
        }

        host.innerHTML = rows.map(row => `
            <button type="button" class="segment-row ${escAttr(row.level)}" data-action="${registerDecisionAction(`${row.dimension}: ${row.segment}`, row.events)}">
                <span><strong>${esc(row.segment)}</strong><em>${esc(row.dimension)}</em></span>
                <span>${row.sessions} sessions</span>
                <span>${esc(row.frictionRate)} friction</span>
                <span>${esc(row.delta)}</span>
            </button>
        `).join("");
    }

    function renderTrendWatch(active, previous) {
        const host = el("trendWatch");
        const rows = buildTrendRows(active, previous);
        el("trendMeta").textContent = `${rows.length} metrics`;
        if (!rows.length) {
            host.innerHTML = empty("No previous-period trend data yet.");
            return;
        }

        host.innerHTML = rows.map(row => `
            <button type="button" class="trend-row ${escAttr(row.level)}" data-action="${registerDecisionAction(row.label, row.events)}">
                <span><strong>${esc(row.label)}</strong><em>${esc(row.confidence)}</em></span>
                <span>${esc(row.current)}</span>
                <span>${esc(row.previous)}</span>
                <span>${esc(row.delta)}</span>
            </button>
        `).join("");
    }

    function buildDecisionInsights(active, previous) {
        const insights = []
            .concat(joinTrackSelectionInsights(active, previous))
            .concat(funnelBottleneckInsights(active, previous))
            .concat(frictionInsights(active, previous))
            .concat(continuationInsights(active, previous))
            .concat(segmentIssueInsights(active, previous));

        const seen = new Set();
        return insights
            .filter(item => {
                const key = `${item.title}|${item.flow}|${item.evidence}`;
                if (seen.has(key)) return false;
                seen.add(key);
                return item.events.length > 0;
            })
            .sort((a, b) => b.score - a.score);
    }

    function joinTrackSelectionInsights(active, previous) {
        if (state.tab === challengeDiagnosticsTab) return [];
        const entrySessions = sessionsForNames(active, ["onboarding_entry_viewed"]);
        const selectedSessions = sessionsForNames(active, ["onboarding_clock_selected"]);
        const base = entrySessions.size;
        if (base < 3) return [];

        const missing = difference(entrySessions, selectedSessions);
        const dropRate = missing.length / base;
        if (dropRate < 0.35 && missing.length < 3) return [];

        const previousEntries = sessionsForNames(previous, ["onboarding_entry_viewed"]);
        const previousSelected = sessionsForNames(previous, ["onboarding_clock_selected"]);
        const previousDropRate = previousEntries.size
            ? Math.max(0, previousEntries.size - intersectionSize(previousEntries, previousSelected)) / previousEntries.size
            : null;
        const support = active.filter(e =>
            (missing.includes(e.sessionHash) && e.eventName === "onboarding_entry_viewed") ||
            (entrySessions.has(e.sessionHash) && e.eventName === "onboarding_clock_selected"));
        const trend = trendPhrase(dropRate, previousDropRate, true);

        return [{
            title: "Join track selection bottleneck",
            flow: "onboarding",
            evidence: `${missing.length} of ${base} Join sessions did not choose Pro or Amateur (${percentNumber(dropRate)})`,
            hypothesis: "The Join page is being seen, but the next track choice may not be obvious or the traffic may be repeated refresh noise.",
            action: "Inspect the anonymous Join sessions, then compare repeated page-view bursts against sessions that choose a track.",
            confidence: confidenceFor(base, previousEntries.size),
            severity: severityFor(dropRate, missing.length),
            trend,
            score: 72 + dropRate * 70 + missing.length * 5 + trendScore(trend),
            events: support
        }];
    }

    function funnelBottleneckInsights(active, previous) {
        const flows = state.tab === challengeDiagnosticsTab ? ["challenge"] : ["pheno", "bortz", "application", "challenge"];
        const insights = [];
        flows.forEach(flow => {
            const defs = funnelDefs[flow] || [];
            for (let index = 1; index < defs.length; index++) {
                const [eventName, label] = defs[index];
                const [priorName, priorLabel] = defs[index - 1];
                if ((flow === "pheno" || flow === "bortz") && eventName === "onboarding_clock_selected") continue;
                const priorSessions = sessionsForFunnelStep(active, flow, priorName);
                const reachedSessions = sessionsForFunnelStep(active, flow, eventName);
                const base = priorSessions.size;
                if (base < 3) continue;

                const missing = difference(priorSessions, reachedSessions);
                const dropRate = missing.length / base;
                if (dropRate < 0.35 && missing.length < 3) continue;

                const previousPrior = sessionsForFunnelStep(previous, flow, priorName).size;
                const previousReached = sessionsForFunnelStep(previous, flow, eventName).size;
                const previousDropRate = previousPrior ? Math.max(0, previousPrior - previousReached) / previousPrior : null;
                const support = active.filter(e => missing.includes(e.sessionHash) || e.eventName === eventName || e.eventName === priorName);
                const trend = trendPhrase(dropRate, previousDropRate, true);
                insights.push({
                    title: `${flowLabel(flow)} bottleneck at ${label}`,
                    flow,
                    evidence: `${missing.length} of ${base} sessions dropped after ${priorLabel} (${percentNumber(dropRate)})`,
                    hypothesis: hypothesisFor(eventName),
                    action: actionFor(eventName),
                    confidence: confidenceFor(base, previousPrior),
                    severity: severityFor(dropRate, missing.length),
                    trend,
                    score: 45 + dropRate * 70 + missing.length * 3 + flowPriority(flow) + trendScore(trend),
                    events: support
                });
            }
        });
        return insights;
    }

    function frictionInsights(active, previous) {
        const grouped = groupBy(frictionEvents(active), e => `${e.flow || "site"}|${e.eventName}|${e.step || e.errorCode || "general"}`);
        return Array.from(grouped.entries()).map(([key, items]) => {
            const [flow, eventName, step] = key.split("|");
            const affected = uniqueSessions(items);
            const flowSessions = uniqueSessions(active.filter(e => (e.flow || "site") === flow)) || uniqueSessions(active);
            const matchingPrevious = frictionEvents(previous).filter(e => (e.flow || "site") === flow && e.eventName === eventName && (e.step || e.errorCode || "general") === step);
            const previousAffected = uniqueSessions(matchingPrevious);
            const previousFlowSessions = uniqueSessions(previous.filter(e => (e.flow || "site") === flow));
            const activeRate = affected / Math.max(1, flowSessions);
            const previousRate = previousFlowSessions ? previousAffected / previousFlowSessions : null;
            const trend = trendPhrase(activeRate, previousRate, true);
            return {
                title: `${friendlyEvent(eventName)} repeats at ${step}`,
                flow,
                evidence: `${affected} affected sessions (${items.length} events)`,
                hypothesis: hypothesisFor(eventName),
                action: actionFor(eventName),
                confidence: confidenceFor(affected, previousAffected),
                severity: severityFor(activeRate, affected),
                trend,
                score: 32 + affected * 14 + Math.min(items.length, affected * 4) * 2 + flowPriority(flow) + trendScore(trend),
                events: items
            };
        }).filter(item => item.events.length >= 2 || uniqueSessions(item.events) >= 2);
    }

    function continuationInsights(active, previous) {
        const insights = [];
        const calcResults = sessionsForNames(active, ["calculator_result_generated"]);
        const proofOpened = sessionsForNames(active, ["proof_flow_opened"]);
        const calcMissing = difference(calcResults, proofOpened);
        if (calcResults.size >= 2 && calcMissing.length > 0) {
            const previousRate = missingRate(previous, ["calculator_result_generated"], ["proof_flow_opened"]);
            const activeRate = calcMissing.length / calcResults.size;
            const support = active.filter(e => calcMissing.includes(e.sessionHash));
            const trend = trendPhrase(activeRate, previousRate, true);
            insights.push({
                title: "Calculator completions are not reaching proof flow",
                flow: "application",
                evidence: `${calcMissing.length} of ${calcResults.size} result sessions stopped before proof/profile upload`,
                hypothesis: "The continue handoff, payment offer storage, or proof entry step may be unclear or brittle.",
                action: "Inspect these sessions, then tighten the continue state and handoff recovery.",
                confidence: confidenceFor(calcResults.size, sessionsForNames(previous, ["calculator_result_generated"]).size),
                severity: severityFor(activeRate, calcMissing.length),
                trend,
                score: 54 + activeRate * 80 + calcMissing.length * 6 + trendScore(trend),
                events: support
            });
        }

        const signups = sessionsForNames(active, ["challenge_signup_succeeded"]);
        const practice = sessionsForNames(active, ["challenge_practice_checkin_submitted"]);
        const noPractice = difference(signups, practice);
        if (signups.size >= 2 && noPractice.length > 0) {
            const previousRate = missingRate(previous, ["challenge_signup_succeeded"], ["challenge_practice_checkin_submitted"]);
            const activeRate = noPractice.length / signups.size;
            const support = active.filter(e => noPractice.includes(e.sessionHash));
            const trend = trendPhrase(activeRate, previousRate, true);
            insights.push({
                title: "Challenge signups are not reaching practice check-in",
                flow: "challenge",
                evidence: `${noPractice.length} of ${signups.size} signup sessions stopped before practice`,
                hypothesis: "Post-confirmation or participant-page activation may not be obvious enough.",
                action: "Inspect signup-to-practice sessions and improve the first check-in prompt.",
                confidence: confidenceFor(signups.size, sessionsForNames(previous, ["challenge_signup_succeeded"]).size),
                severity: severityFor(activeRate, noPractice.length),
                trend,
                score: 50 + activeRate * 72 + noPractice.length * 6 + trendScore(trend),
                events: support
            });
        }

        const missingReview = active.filter(e => e.eventName === "application_review_context_missing" && !isBenignMissingContextEvent(e));
        if (missingReview.length > 0) {
            const affected = uniqueSessions(missingReview);
            const previousAffected = uniqueSessions(previous.filter(e => e.eventName === "application_review_context_missing" && !isBenignMissingContextEvent(e)));
            const trend = trendPhrase(affected, previousAffected || null, true);
            insights.push({
                title: "Application review opens without stored context",
                flow: "application",
                evidence: `${affected} sessions opened review without stored context (${missingReview.length} events)`,
                hypothesis: "Review links or storage recovery may be losing the invoice/submission context.",
                action: "Inspect review sessions and add clearer recovery for missing context.",
                confidence: confidenceFor(affected, previousAffected),
                severity: severityFor(affected / Math.max(3, uniqueSessions(active)), affected),
                trend,
                score: 46 + affected * 12 + Math.min(missingReview.length, affected * 4) + trendScore(trend),
                events: missingReview
            });
        }

        return insights;
    }

    function segmentIssueInsights(active, previous) {
        return buildSegmentRows(active, previous)
            .filter(row => row.level === "high" || row.level === "med")
            .slice(0, 3)
            .map(row => ({
                title: `${row.segment} ${row.dimension} segment is over-friction`,
                flow: row.flow || "site",
                evidence: `${row.frictionRate} friction across ${row.sessions} sessions (${row.delta})`,
                hypothesis: "This segment may be hitting a device, traffic-quality, or flow-specific issue.",
                action: "Inspect the segment sessions and compare the first failing step against healthier segments.",
                confidence: row.confidence,
                severity: row.level === "high" ? "high" : "med",
                trend: row.trend,
                score: row.score,
                events: row.events
            }));
    }

    function buildRecommendedInvestigations(insights, active) {
        const items = insights.map(insight => ({
            title: `Inspect: ${insight.title}`,
            reason: insight.evidence,
            sessions: uniqueSessions(insight.events),
            events: insight.events
        }));
        addInvestigation(items, active, ["proof_processing_failed", "proof_file_rejected"], "Inspect failed proof upload sessions", "Proof upload friction blocks application completion.");
        addInvestigation(items, active, ["application_review_context_missing"], "Inspect application-review sessions with missing context", "Missing review context points at brittle storage or handoff recovery.");
        addInvestigation(items, active, ["challenge_signup_succeeded"], "Inspect Challenge signups that did not reach practice", "Activation depends on the first eligible practice check-in.", sessionsMissing(active, ["challenge_signup_succeeded"], ["challenge_practice_checkin_submitted"]));
        addInvestigation(items, active, ["calculator_result_generated"], "Inspect calculator completions without proof flow", "These sessions generated a result but did not open proof/profile upload.", sessionsMissing(active, ["calculator_result_generated"], ["proof_flow_opened"]));
        return items.filter(item => item.events.length > 0);
    }

    function addInvestigation(items, events, names, title, reason, sessionFilter) {
        const filtered = events.filter(e => names.includes(e.eventName) && !isBenignMissingContextEvent(e) && (!sessionFilter || sessionFilter.includes(e.sessionHash)));
        if (!filtered.length || items.some(item => item.title === title)) return;
        items.push({ title, reason, sessions: uniqueSessions(filtered), events: filtered });
    }

    function buildSegmentRows(active, previous) {
        const previousSessions = new Set(previous.map(e => e.sessionHash));
        const dimensions = [
            ["device", e => e.deviceClass || "unknown"],
            ["source", acquisitionSource],
            ["flow", e => flowLabel(e.flow || "site")],
            ["session", e => previousSessions.has(e.sessionHash) ? "returning" : "new"]
        ];
        const overallRate = frictionSessionRate(active);
        const rows = [];
        dimensions.forEach(([dimension, picker]) => {
            const grouped = groupBy(active, picker);
            grouped.forEach((events, segment) => {
                const sessions = uniqueSessions(events);
                if (sessions < 2) return;
                const rate = frictionSessionRate(events);
                const previousMatching = previous.filter(e => picker(e) === segment);
                const previousSessionCount = uniqueSessions(previousMatching);
                const previousRate = previousSessionCount ? frictionSessionRate(previousMatching) : null;
                const delta = rate - overallRate;
                const trend = trendPhrase(rate, previousRate, true);
                rows.push({
                    dimension,
                    segment,
                    sessions,
                    frictionRate: percentNumber(rate),
                    delta: `${delta >= 0 ? "+" : ""}${percentNumber(delta)} vs avg`,
                    confidence: confidenceFor(sessions, previousSessionCount),
                    level: delta >= 0.25 && sessions >= 4 ? "high" : delta >= 0.12 ? "med" : "low",
                    trend,
                    score: rate * 70 + delta * 80 + sessions * 2 + trendScore(trend),
                    events,
                    flow: mostCommon(events.map(e => e.flow || "site"))
                });
            });
        });
        return rows.sort((a, b) => b.score - a.score);
    }

    function buildTrendRows(active, previous) {
        const rows = [
            rateTrend("Calculator result conversion", active, previous, ["calculator_started"], ["calculator_result_generated"]),
            rateTrend("Result to proof handoff", active, previous, ["calculator_result_generated"], ["proof_flow_opened"]),
            rateTrend("Application submit conversion", active, previous, ["proof_flow_opened"], ["application_submit_succeeded"]),
            rateTrend("Challenge signup to practice", active, previous, ["challenge_signup_succeeded"], ["challenge_practice_checkin_submitted"]),
            frictionTrend("Friction rate", active, previous)
        ].filter(Boolean);
        return rows;
    }

    function rateTrend(label, active, previous, startNames, finishNames) {
        const starts = sessionsForNames(active, startNames);
        const previousStarts = sessionsForNames(previous, startNames);
        if (starts.size === 0 && previousStarts.size === 0) return null;
        const rate = starts.size ? intersectionSize(starts, sessionsForNames(active, finishNames)) / starts.size : 0;
        const previousRate = previousStarts.size ? intersectionSize(previousStarts, sessionsForNames(previous, finishNames)) / previousStarts.size : 0;
        const hasBaseline = previousStarts.size > 0;
        const delta = hasBaseline ? rate - previousRate : null;
        const events = active.filter(e => startNames.includes(e.eventName) || finishNames.includes(e.eventName));
        return {
            label,
            current: `${percentNumber(rate)} (${starts.size} sessions)`,
            previous: hasBaseline ? percentNumber(previousRate) : "no baseline",
            delta: hasBaseline ? `${delta >= 0 ? "+" : ""}${percentNumber(delta)}` : "baseline pending",
            confidence: hasBaseline ? `${confidenceFor(starts.size, previousStarts.size)} confidence` : "baseline pending",
            level: !hasBaseline ? "low" : delta <= -0.15 ? "high" : delta < -0.05 ? "med" : delta > 0.05 ? "good" : "low",
            events
        };
    }

    function frictionTrend(label, active, previous) {
        const activeRate = frictionSessionRate(active);
        const previousRate = frictionSessionRate(previous);
        if (!active.length && !previous.length) return null;
        const previousSessionCount = uniqueSessions(previous);
        const hasBaseline = previousSessionCount > 0;
        const delta = hasBaseline ? activeRate - previousRate : null;
        return {
            label,
            current: percentNumber(activeRate),
            previous: hasBaseline ? percentNumber(previousRate) : "no baseline",
            delta: hasBaseline ? `${delta >= 0 ? "+" : ""}${percentNumber(delta)}` : "baseline pending",
            confidence: hasBaseline ? `${confidenceFor(uniqueSessions(active), previousSessionCount)} confidence` : "baseline pending",
            level: !hasBaseline ? "low" : delta >= 0.15 ? "high" : delta > 0.05 ? "med" : delta < -0.05 ? "good" : "low",
            events: frictionEvents(active)
        };
    }

    function wireDecisionActions() {
        document.querySelectorAll("[data-action]").forEach(node => {
            node.addEventListener("click", () => {
                const action = state.decisionActions[Number(node.dataset.action)];
                if (!action) return;
                drillToEvents(action.events, action.label);
            });
        });
    }

    function registerDecisionAction(label, events) {
        const index = state.decisionActions.length;
        state.decisionActions.push({ label, events: events || [] });
        return index;
    }

    function drillToEvents(events, label) {
        state.selectedEventName = null;
        state.selectedSession = null;
        state.selectedLabel = label || "Decision evidence";
        renderDrilldown(events && events.length ? events : decisionScopeEvents(defaultEvents()));
        el("drilldownTitle").scrollIntoView({ block: "nearest" });
    }

    function renderTabs() {
        const host = el("statsTabs");
        host.innerHTML = "";
        tabs.forEach(tab => {
            const button = document.createElement("button");
            button.type = "button";
            button.className = `stats-tab${state.tab === tab ? " active" : ""}`;
            button.textContent = tab;
            button.addEventListener("click", () => {
                state.tab = tab;
                if (tab === challengeDiagnosticsTab) state.selectedFlow = "challenge";
                if (tab === onboardingDiagnosticsTab && state.selectedFlow === "challenge") state.selectedFlow = "pheno";
                state.selectedEventName = null;
                state.selectedLabel = `${tab} / all events`;
                if (needsDiagnosticEventReload()) loadDashboard();
                else renderAll();
            });
            host.appendChild(button);
        });
    }

    function renderOutcomeStrip() {
        const host = el("outcomeStrip");
        const events = scopedEvents();
        const friction = frictionEvents(events);
        host.innerHTML = "";
        outcomeTiles.forEach(([label, names]) => {
            const tileEvents = label === "Friction"
                ? friction
                : label === "Error rate"
                    ? friction
                    : events.filter(e => names.includes(e.eventName));
            const value = label === "Friction"
                ? uniqueSessions(friction)
                : label === "Error rate"
                    ? percent(uniqueSessions(friction), uniqueSessions(events))
                    : tileEvents.length;
            const footer = label === "Friction"
                ? `${friction.length} events`
                : label === "Error rate"
                    ? `${friction.length}/${events.length} events`
                    : `${uniqueSessions(tileEvents)} sessions`;
            const tile = document.createElement("button");
            tile.type = "button";
            tile.className = `metric-tile${state.selectedLabel === label ? " active" : ""}`;
            tile.innerHTML = `
                <span class="metric-label">${esc(label)}</span>
                <strong class="metric-value">${esc(String(value))}</strong>
                <span class="metric-footer"><span>${esc(footer)}</span>${spark(tileEvents)}</span>
            `;
            tile.addEventListener("click", () => {
                state.selectedEventName = names.length === 1 ? names[0] : null;
                state.selectedSession = null;
                state.selectedLabel = label;
                renderDrilldown(label === "Error rate" || label === "Friction" ? friction : tileEvents);
            });
            host.appendChild(tile);
        });
    }

    function renderPrimaryPanel() {
        const selected = state.tab === challengeDiagnosticsTab ? "challenge" : state.selectedFlow;
        const title = state.tab === challengeDiagnosticsTab
            ? "Challenge Activation Funnel"
            : state.tab === sourceQualityTab
                ? "Source Quality Funnel"
                : state.tab === reliabilityDiagnosticsTab
                    ? "Reliability Funnel"
                    : state.tab === reviewDiagnosticsTab
                        ? "Review Handoff Funnel"
                        : state.tab === publicEventsDiagnosticsTab
                            ? "Public Event Funnel"
                            : "Onboarding Funnel";
        el("primaryTitle").textContent = title;
        el("primaryMeta").textContent = `${uniqueSessions(scopedEvents())} sessions / ${scopedEvents().length} events`;
        renderFlowSelectors();

        const defs = state.tab === challengeDiagnosticsTab
            ? funnelDefs.challenge
            : state.tab === sourceQualityTab
                ? sourceFunnelDefs()
                : state.tab === reliabilityDiagnosticsTab
                    ? reliabilityFunnelDefs()
                    : state.tab === reviewDiagnosticsTab
                        ? funnelDefs.application.slice(10)
                        : state.tab === publicEventsDiagnosticsTab
                            ? eventsFunnelDefs()
                            : funnelDefs[selected] || funnelDefs.pheno;
        renderFunnel(defs, scopedEvents());
    }

    function renderFlowSelectors() {
        const host = el("flowSelectors");
        host.innerHTML = "";
        if (state.tab !== onboardingDiagnosticsTab) return;
        ["pheno", "bortz", "application"].forEach(flow => {
            const events = defaultEvents().filter(e => e.flow === flow || (flow === "application" && e.flow === "application"));
            const started = uniqueSessionsFor(events, flow === "application" ? ["proof_flow_opened"] : ["calculator_started"]);
            const completed = uniqueSessionsFor(events, flow === "application" ? ["application_submit_succeeded"] : ["calculator_result_generated"]);
            const failed = uniqueSessions(frictionEvents(events));
            const button = document.createElement("button");
            button.type = "button";
            button.className = `flow-card${state.selectedFlow === flow ? " active" : ""}`;
            button.innerHTML = `
                <strong>${flowLabel(flow)}</strong>
                <span class="flow-card-grid">
                    <span>started ${started}</span><span>done ${completed}</span>
                    <span>conv ${percent(completed, started)}</span><span>fail ${failed}</span>
                </span>
            `;
            button.addEventListener("click", () => {
                state.selectedFlow = flow;
                state.selectedEventName = null;
                state.selectedSession = null;
                renderAll();
            });
            host.appendChild(button);
        });
    }

    function renderFunnel(defs, events) {
        const host = el("primaryFunnel");
        host.innerHTML = "";
        if (!defs.length) {
            host.innerHTML = empty("No funnel data for the active filters.");
            return;
        }
        const first = Math.max(1, uniqueSessionsFor(events, [defs[0][0]]));
        let previous = first;
        defs.forEach(([name, label]) => {
            const count = uniqueSessionsFor(events, [name]);
            const fromPrev = percent(count, previous);
            const fromFirst = percent(count, first);
            const drop = Math.max(0, previous - count);
            const row = document.createElement("button");
            row.type = "button";
            row.className = `funnel-row${state.selectedEventName === name ? " active" : ""}`;
            row.innerHTML = `
                <span class="funnel-name">${esc(label)}</span>
                <span class="funnel-bar"><span class="funnel-fill" style="width:${Math.min(100, Math.round((count / first) * 100))}%"></span></span>
                <span class="funnel-number">${count}</span>
                <span class="funnel-rate">${fromPrev}</span>
                <span class="funnel-drop${drop > Math.max(2, previous * 0.35) ? " high" : ""}">drop ${drop}</span>
            `;
            row.addEventListener("click", () => selectDrilldown(name, label));
            host.appendChild(row);
            if (count > 0) previous = count;
        });
    }

    function renderFriction() {
        const host = el("frictionRadar");
        const events = frictionEvents(scopedEvents());
        el("frictionMeta").textContent = `${uniqueSessions(events)} affected sessions / ${events.length} events`;
        if (!events.length) {
            host.innerHTML = empty("No friction events for the active filters.");
            return;
        }

        const grouped = groupBy(events, e => `${e.eventName}|${e.step || e.errorCode || "general"}`);
        const rows = Array.from(grouped.entries()).map(([key, items]) => {
            const [eventName, step] = key.split("|");
            return {
                eventName,
                step,
                count: items.length,
                sessions: new Set(items.map(i => i.sessionHash)).size,
                flow: mostCommon(items.map(i => i.flow || "site")),
                error: mostCommon(items.map(i => i.errorCode || i.outcome || "friction"))
            };
        }).sort((a, b) => b.sessions - a.sessions || b.count - a.count).slice(0, 12);

        host.innerHTML = "";
        rows.forEach(row => {
            const severity = row.sessions >= 10 ? "high" : row.sessions >= 3 ? "med" : "low";
            const node = document.createElement("button");
            node.type = "button";
            node.className = `friction-row${state.selectedEventName === row.eventName ? " active" : ""}`;
            node.innerHTML = `
                <span class="friction-name">
                    <strong>${esc(row.error)}</strong>
                    <span>${esc(row.flow)} / ${esc(row.step)} / ${esc(row.eventName)}</span>
                </span>
                <span class="friction-count">${row.count} events<br>${row.sessions} sessions</span>
                <span class="friction-severity ${severity}">${severity}</span>
            `;
            node.addEventListener("click", () => selectDrilldown(row.eventName, row.error));
            host.appendChild(node);
        });
    }

    function renderDetailSections() {
        const host = el("detailSections");
        const events = scopedEvents();
        if (state.tab === challengeDiagnosticsTab) {
            host.innerHTML = [
                detailPanel("Challenge activation", challengeActivationTable(events)),
                detailPanel("Check-in detail", groupedTable(events, ["challenge_practice_checkin_started", "challenge_practice_checkin_submitted", "challenge_scored_checkin_started", "challenge_scored_checkin_submitted", "challenge_scored_checkin_failed"])),
                detailPanel("Commitment status", groupedTable(events, ["challenge_commitment_block_seen", "challenge_commitment_payment_opened", "challenge_commitment_payment_status_checked", "challenge_commitment_resolved"]))
            ].join("");
            return;
        }
        if (state.tab === sourceQualityTab) {
            host.innerHTML = [
                detailPanel("Acquisition quality", sourceQualityTable(events)),
                detailPanel("Campaigns", campaignTable(events)),
                detailPanel("Device split", splitTable(events, e => e.deviceClass || "unknown")),
                detailPanel("First referrers", splitTable(events, e => e.firstReferrerDomain || e.referrerDomain || acquisitionSource(e)))
            ].join("");
            return;
        }
        if (state.tab === reliabilityDiagnosticsTab) {
            host.innerHTML = [
                detailPanel("Error codes", splitTable(frictionEvents(events), e => e.errorCode || e.eventName)),
                detailPanel("Affected components", splitTable(frictionEvents(events), e => e.component || "unknown")),
                detailPanel("Slow steps", slowStepTable(events))
            ].join("");
            return;
        }
        if (state.tab === reviewDiagnosticsTab) {
            host.innerHTML = [
                detailPanel("Review states", groupedTable(events, ["application_review_opened", "application_review_context_found", "application_review_context_missing", "payment_status_checked", "payment_status_failed"])),
                detailPanel("Payment handoff", groupedTable(events, ["payment_offer_stored", "payment_unavailable", "checkout_redirect_started", "payment_status_checked"])),
                detailPanel("Recent sessions", sessionStateTable(events))
            ].join("");
            return;
        }
        if (state.tab === publicEventsDiagnosticsTab) {
            host.innerHTML = [
                detailPanel("Public events", groupedTable(events, ["event_viewed", "event_link_clicked", "homepage_highlight_viewed", "homepage_highlight_clicked"])),
                detailPanel("Profile traffic", groupedTable(events, ["athlete_profile_viewed", "league_viewed"])),
                detailPanel("Social/output", splitTable(events.filter(e => /event|highlight|social/i.test(e.eventName)), e => e.eventName))
            ].join("");
            return;
        }
        host.innerHTML = [
            detailPanel("Calculator completion sources", calculatorCompletionSourceTable(events)),
            detailPanel("Calculator fields", fieldFrictionTable(events)),
            detailPanel("Proof upload", proofUploadTable(events)),
            detailPanel("Handoff integrity", handoffTable(events))
        ].join("");
    }

    function renderDrilldown(explicitEvents) {
        const events = explicitEvents || selectedEvents();
        const selected = events.length ? events : scopedEvents();
        const session = state.selectedSession || (selected[0] && selected[0].sessionHash);
        el("drilldownTitle").textContent = state.selectedLabel || "Drilldown";
        el("drilldownBreadcrumb").textContent = `${state.tab} / ${flowLabel(state.selectedFlow)} / ${state.selectedLabel || "all events"}`;
        renderSummary(selected);
        renderSamples(selected);
        renderTimeline(session, selected);
    }

    function renderSummary(events) {
        const sessions = new Set(events.map(e => e.sessionHash)).size;
        const failures = frictionEvents(events).length;
        const median = medianDuration(events);
        const quality = dataQualityStats(events, []);
        const burstCount = collapseEventBursts(events).filter(group => group.count > 1).length;
        el("drilldownSummary").innerHTML = `
            <div class="summary-grid">
                ${summaryItem("Events", events.length)}
                ${summaryItem("Sessions", sessions)}
                ${summaryItem("Friction", failures)}
                ${summaryItem("Median duration", median)}
                ${summaryItem("Top source", mostCommon(events.map(acquisitionSource)))}
                ${summaryItem("Top device", mostCommon(events.map(e => e.deviceClass || "unknown")))}
                ${summaryItem("Noisy sessions", quality.noisySessions)}
                ${summaryItem("Burst groups", burstCount)}
            </div>
            <div class="chip-row" style="margin-top:10px">
                ${Array.from(new Set(events.slice(0, 40).map(e => e.flow || "site"))).map(v => `<span class="chip">${esc(v)}</span>`).join("")}
            </div>
        `;
    }

    function renderSamples(events) {
        const host = el("eventSamples");
        const rows = collapseEventBursts(events).sort((a, b) => b.lastTime - a.lastTime).slice(0, 50);
        if (!rows.length) {
            host.innerHTML = empty("No event samples.");
            return;
        }
        host.innerHTML = rows.map(group => `
            <button type="button" class="sample-row${state.selectedSession === group.sessionHash ? " active" : ""}" data-session="${escAttr(group.sessionHash)}">
                <span class="sample-main"><strong>${esc(group.eventName)}</strong><span>${esc(group.sessionHash)}</span></span>
                <span class="sample-meta">${esc(formatTime(group.last.occurredAtUtc))} / ${esc(group.flow || "site")} / ${esc(group.step || group.component || "-")} / ${esc(group.errorCode || group.outcome || "-")}${burstLabel(group)}</span>
            </button>
        `).join("");
        host.querySelectorAll(".sample-row").forEach(row => {
            row.addEventListener("click", () => {
                state.selectedSession = row.dataset.session;
                renderDrilldown(events);
            });
        });
    }

    function renderTimeline(sessionHash, contextEvents) {
        const host = el("sessionTimeline");
        if (!sessionHash) {
            host.innerHTML = empty("Select an event sample to inspect an anonymous session timeline.");
            return;
        }
        const rows = state.events
            .filter(e => e.sessionHash === sessionHash)
            .sort((a, b) => a.time - b.time);
        if (!rows.length) {
            host.innerHTML = empty("No events found for that session.");
            return;
        }
        const started = rows[0].time || 0;
        const groups = collapseEventBursts(rows, true);
        host.innerHTML = `
            <h3 style="margin-bottom:8px">Session ${esc(sessionHash)}</h3>
            ${groups.map(group => `
                <div class="timeline-row">
                    <span class="timeline-main"><strong>${esc(offset(group.firstTime - started))}</strong><span>${esc(group.eventName)}</span></span>
                    <span class="timeline-meta">${esc(group.route || "-")} / ${esc(group.step || group.component || "-")} / ${esc(group.errorCode || group.outcome || "-")}${metadataChips(group.last.metadata)}${burstLabel(group)}</span>
                </div>
            `).join("")}
        `;
    }

    function decisionScopeEvents(events) {
        if (state.tab === onboardingDiagnosticsTab) {
            return events.filter(e => ["onboarding", "pheno", "bortz", "application", "challenge"].includes(e.flow || ""));
        }
        return scopeEventsFor(events, state.tab, state.selectedFlow);
    }

    function defaultEvents() {
        return state.defaultEvents;
    }

    function defaultPreviousEvents() {
        return state.previousDefaultEvents;
    }

    function scopeEventsFor(events, tab, selectedFlow) {
        events = events.slice();
        if (tab === onboardingDiagnosticsTab) {
            if (selectedFlow === "application") return events.filter(e => e.flow === "application");
            return events.filter(e => e.flow === selectedFlow || e.flow === "onboarding");
        }
        if (tab === challengeDiagnosticsTab) return events.filter(e => e.flow === "challenge");
        if (tab === reliabilityDiagnosticsTab) return frictionEvents(events);
        if (tab === reviewDiagnosticsTab) return events.filter(e => e.flow === "application" || /application|payment|review/.test(e.eventName));
        if (tab === publicEventsDiagnosticsTab) return events.filter(e => /event|highlight|athlete_profile|league/.test(e.eventName));
        return events;
    }

    function decisionLensLabel() {
        return state.tab === onboardingDiagnosticsTab ? "onboarding + activation" : state.tab;
    }

    function scopedEvents() {
        return scopeEventsFor(defaultEvents(), state.tab, state.selectedFlow);
    }

    function rawScopedEvents() {
        return scopeEventsFor(state.events, state.tab, state.selectedFlow);
    }

    function selectedEvents() {
        let events = scopedEvents();
        if (state.selectedEventName) events = events.filter(e => e.eventName === state.selectedEventName);
        if (state.selectedSession) events = events.filter(e => e.sessionHash === state.selectedSession);
        return events;
    }

    function selectedRawEvents() {
        let events = rawScopedEvents();
        if (state.selectedEventName) events = events.filter(e => e.eventName === state.selectedEventName);
        if (state.selectedSession) events = events.filter(e => e.sessionHash === state.selectedSession);
        return events;
    }

    function selectDrilldown(eventName, label) {
        state.selectedEventName = eventName;
        state.selectedSession = null;
        state.selectedLabel = label || eventName || "all events";
        renderOutcomeStrip();
        renderFriction();
        renderFunnel(state.tab === challengeDiagnosticsTab ? funnelDefs.challenge : (funnelDefs[state.selectedFlow] || funnelDefs.pheno), scopedEvents());
        renderDrilldown();
        updateUrl();
    }

    function isIgnoredClientError(e) {
        if (!e || e.eventName !== "client_error_observed") return false;
        const errorCode = String(e.errorCode || "").trim();
        return errorCode === "ResizeObserver loop completed with undelivered notifications." ||
            errorCode === "ResizeObserver loop completed with undelivered notifications" ||
            errorCode === "ResizeObserver loop limit exceeded";
    }

    function isBenignMissingContextEvent(e) {
        if (!e) return false;
        const route = String(e.route || "").toLowerCase();
        if (e.eventName === "proof_flow_missing_handoff") {
            return route === "/apply" || route.startsWith("/apply?") || route.includes("convergence");
        }
        if (e.eventName === "application_review_context_missing") {
            return route.includes("from=proof-upload") || route.includes("from=edit-profile");
        }
        return false;
    }

    function frictionEvents(events) {
        return events.filter(e =>
            !isIgnoredClientError(e) &&
            !isBenignMissingContextEvent(e) &&
            (/failed|failure|missing|rejected|unavailable|invalid|error|blocked/.test(e.eventName) ||
                /failed|missing|error|blocked/.test(e.outcome || "") ||
                !!e.errorCode));
    }

    function dataQualityStats(events, previous) {
        const sessionGroups = Array.from(groupBy(events, e => e.sessionHash).values());
        const sessionCounts = sessionGroups.map(items => items.length);
        const topSessionEvents = sessionCounts.length ? Math.max(...sessionCounts) : 0;
        const noisySessions = sessionGroups.filter(isNoisySession).length;
        return {
            events: events.length,
            sessions: sessionGroups.length,
            cleanSessions: Math.max(0, sessionGroups.length - noisySessions),
            noisySessions,
            topSessionEvents,
            topSessionShare: events.length ? topSessionEvents / events.length : 0,
            previousEvents: previous.length,
            previousSessions: uniqueSessions(previous)
        };
    }

    function isNoisySession(events) {
        if (events.length < 20) return false;
        const bursts = collapseEventBursts(events);
        const largestBurst = bursts.length ? Math.max(...bursts.map(group => group.count)) : 0;
        const pageViews = events.filter(isPageViewEvent).length;
        return largestBurst >= 20 ||
            (events.length >= 40 && largestBurst / events.length >= 0.6) ||
            (pageViews >= 20 && pageViews / events.length >= 0.6);
    }

    function isPageViewEvent(event) {
        return ["site_page_viewed", "onboarding_entry_viewed", "onboarding_page_viewed", "challenge_page_viewed"].includes(event.eventName);
    }

    function collapseRepeatedPageViewBursts(events) {
        const collapsed = [];
        const pageViewsByRoute = new Map();
        events.slice().sort((a, b) => a.time - b.time).forEach(event => {
            if (!isPageViewEvent(event)) {
                collapsed.push(event);
                return;
            }

            const key = [event.sessionHash, event.route || ""].join("|");
            const existing = pageViewsByRoute.get(key);
            if (!existing) {
                const copy = Object.assign({}, event, {
                    collapsedCount: 1,
                    collapsedFirstTime: event.time || 0,
                    collapsedLastTime: event.time || 0,
                    collapsedPageViewPriority: pageViewPriority(event)
                });
                pageViewsByRoute.set(key, copy);
                collapsed.push(copy);
                return;
            }

            existing.collapsedCount += 1;
            if (pageViewPriority(event) > existing.collapsedPageViewPriority) {
                existing.eventName = event.eventName;
                existing.flow = event.flow;
                existing.component = event.component;
                existing.step = event.step;
                existing.outcome = event.outcome;
                existing.errorCode = event.errorCode;
                existing.collapsedPageViewPriority = pageViewPriority(event);
            }
            if ((event.time || 0) >= existing.collapsedLastTime) {
                existing.collapsedLastTime = event.time || 0;
                existing.time = event.time;
                existing.occurredAtUtc = event.occurredAtUtc;
                existing.durationMs = event.durationMs;
            }
        });

        return collapsed.sort((a, b) => b.time - a.time);
    }

    function pageViewPriority(event) {
        if (event.eventName === "challenge_page_viewed") return 4;
        if (event.eventName === "onboarding_entry_viewed") return 3;
        if (event.eventName === "onboarding_page_viewed") return 3;
        if (event.eventName === "site_page_viewed") return 1;
        return 0;
    }

    function collapseEventBursts(events, sequential) {
        const sorted = events.slice().sort((a, b) => a.time - b.time);
        if (sequential) {
            const groups = [];
            sorted.forEach(event => {
                const key = burstSignature(event);
                const last = groups[groups.length - 1];
                if (last && last.key === key) {
                    addToBurst(last, event);
                } else {
                    groups.push(createBurst(key, event));
                }
            });
            return groups;
        }

        const groups = new Map();
        sorted.forEach(event => {
            const key = burstSignature(event);
            if (!groups.has(key)) groups.set(key, createBurst(key, event));
            else addToBurst(groups.get(key), event);
        });
        return Array.from(groups.values());
    }

    function burstSignature(event) {
        return [
            event.sessionHash,
            event.eventName,
            event.flow || "",
            event.route || "",
            event.component || "",
            event.step || "",
            event.outcome || "",
            event.errorCode || ""
        ].join("|");
    }

    function createBurst(key, event) {
        const count = event.collapsedCount || 1;
        return {
            key,
            count,
            first: event,
            last: event,
            firstTime: event.collapsedFirstTime || event.time || 0,
            lastTime: event.collapsedLastTime || event.time || 0,
            sessionHash: event.sessionHash,
            eventName: event.eventName,
            flow: event.flow,
            route: event.route,
            component: event.component,
            step: event.step,
            outcome: event.outcome,
            errorCode: event.errorCode
        };
    }

    function addToBurst(group, event) {
        const count = event.collapsedCount || 1;
        const firstTime = event.collapsedFirstTime || event.time || 0;
        const lastTime = event.collapsedLastTime || event.time || 0;
        group.count += count;
        if (firstTime < group.firstTime) {
            group.first = event;
            group.firstTime = firstTime;
        }
        if (lastTime >= group.lastTime) {
            group.last = event;
            group.lastTime = lastTime;
        }
    }

    function burstLabel(group) {
        if (!group || group.count <= 1) return "";
        const span = Math.max(0, group.lastTime - group.firstTime);
        return ` / burst x${group.count}${span >= 60000 ? ` over ${timeSpan(span)}` : ""}`;
    }

    function timeSpan(value) {
        const totalMinutes = Math.max(1, Math.round((Number(value) || 0) / 60000));
        if (totalMinutes < 60) return `${totalMinutes}m`;
        const hours = totalMinutes / 60;
        if (hours < 24) return `${hours.toFixed(hours >= 10 ? 0 : 1)}h`;
        const days = hours / 24;
        return `${days.toFixed(days >= 10 ? 0 : 1)}d`;
    }

    function sessionsForNames(events, names) {
        return new Set(events.filter(e => names.includes(e.eventName)).map(e => e.sessionHash));
    }

    function sessionsForFunnelStep(events, flow, name) {
        return new Set(events.filter(e => e.eventName === name && eventBelongsToFlowStep(e, flow, name)).map(e => e.sessionHash));
    }

    function eventBelongsToFlowStep(event, flow, name) {
        if (flow === "pheno" || flow === "bortz") {
            if (name === "onboarding_entry_viewed") return event.flow === "onboarding" || event.flow === flow;
            return event.flow === flow;
        }
        return event.flow === flow;
    }

    function difference(left, right) {
        return Array.from(left).filter(item => !right.has(item));
    }

    function intersectionSize(left, right) {
        let count = 0;
        left.forEach(item => {
            if (right.has(item)) count++;
        });
        return count;
    }

    function sessionsMissing(events, startNames, finishNames) {
        return difference(sessionsForNames(events, startNames), sessionsForNames(events, finishNames));
    }

    function missingRate(events, startNames, finishNames) {
        const starts = sessionsForNames(events, startNames);
        if (!starts.size) return null;
        return difference(starts, sessionsForNames(events, finishNames)).length / starts.size;
    }

    function frictionSessionRate(events) {
        const sessions = uniqueSessions(events);
        if (!sessions) return 0;
        return uniqueSessions(frictionEvents(events)) / sessions;
    }

    function percentNumber(value) {
        value = Number(value);
        if (!Number.isFinite(value)) return "0%";
        return `${Math.round(value * 100)}%`;
    }

    function severityFor(rate, count) {
        if (rate >= 0.6 || count >= 10) return "high";
        if (rate >= 0.35 || count >= 4) return "med";
        return "low";
    }

    function confidenceFor(currentSessions, previousSessions) {
        const total = (Number(currentSessions) || 0) + (Number(previousSessions) || 0);
        if (total >= 30 && currentSessions >= 10) return "high";
        if (total >= 10 && currentSessions >= 4) return "medium";
        return "low";
    }

    function trendPhrase(current, previous, higherIsBad) {
        if (previous === null || previous === undefined || !Number.isFinite(Number(previous))) return "trend sparse";
        current = Number(current) || 0;
        previous = Number(previous) || 0;
        const delta = current - previous;
        if (Math.abs(delta) < 0.03) return "flat";
        const bad = higherIsBad ? delta > 0 : delta < 0;
        return bad ? "worse vs previous" : "better vs previous";
    }

    function trendScore(trend) {
        if (trend === "worse vs previous") return 18;
        if (trend === "trend sparse") return -4;
        if (trend === "better vs previous") return -8;
        return 0;
    }

    function flowPriority(flow) {
        if (flow === "application") return 14;
        if (flow === "pheno" || flow === "bortz") return 12;
        if (flow === "challenge") return 10;
        return 0;
    }

    function friendlyEvent(eventName) {
        return String(eventName || "event").replace(/_/g, " ");
    }

    function hypothesisFor(eventName) {
        if (/validation/.test(eventName)) return "The field expectation, default, or browser validation state is unclear.";
        if (/missing_handoff|context_missing/.test(eventName)) return "Browser storage or handoff recovery is failing for some sessions.";
        if (/proof|upload|file_rejected/.test(eventName)) return "File type, file size, camera, or PDF processing may be blocking submission.";
        if (/rank_preview/.test(eventName)) return "Rank preview latency or API failure may be weakening result confidence.";
        if (/payment|checkout|commitment/.test(eventName)) return "Payment handoff or commitment recovery may be interrupting continuation.";
        if (/challenge_.*signup|practice|checkin/.test(eventName)) return "Challenge activation may not make the next required action obvious.";
        if (/calculator_continue|biomarker_handoff/.test(eventName)) return "The result-to-application transition may not be durable enough.";
        return "This step is behaving worse than nearby steps or segments.";
    }

    function actionFor(eventName) {
        if (/validation/.test(eventName)) return "Inspect field-level failures, then adjust labels, defaults, or validation recovery.";
        if (/missing_handoff|context_missing/.test(eventName)) return "Inspect affected timelines and add a visible recovery path for missing context.";
        if (/proof|upload|file_rejected/.test(eventName)) return "Compare failed uploads by device and file bucket, then improve upload guidance or fallback handling.";
        if (/rank_preview/.test(eventName)) return "Check API latency/failures and keep the result state useful when preview is unavailable.";
        if (/payment|checkout|commitment/.test(eventName)) return "Verify handoff state, failure copy, and retry paths before changing payment logic.";
        if (/challenge_.*signup|practice|checkin/.test(eventName)) return "Inspect signup-to-practice sessions and sharpen the first check-in call to action.";
        if (/calculator_continue|biomarker_handoff/.test(eventName)) return "Inspect stopped calculator sessions and harden the continue/handoff state.";
        return "Open supporting sessions and compare the failing step against healthier cohorts.";
    }

    function countMatching(events, names) {
        if (!names.length) return 0;
        return events.filter(e => names.includes(e.eventName)).length;
    }

    function uniqueSessionsFor(events, names) {
        const filtered = names && names.length ? events.filter(e => names.includes(e.eventName)) : events;
        return new Set(filtered.map(e => e.sessionHash)).size;
    }

    function metadataValue(event, key) {
        if (!event || !event.metadata || !Object.prototype.hasOwnProperty.call(event.metadata, key)) return "";
        return String(event.metadata[key] || "");
    }

    function calculatorEntryMode(event) {
        return metadataValue(event, "entryMode") || "standard";
    }

    function completionSource(event) {
        return metadataValue(event, "completionSource") || "legacy";
    }

    function completionSourceLabel(source) {
        const normalized = String(source || "legacy").toLowerCase();
        if (normalized === "pageshow") return "page restore";
        return normalized || "legacy";
    }

    function isAutomaticCompletion(event) {
        const source = completionSource(event).toLowerCase();
        return source === "initial" || source === "autofill" || source === "pageshow";
    }

    function isLateCompletion(event) {
        const source = completionSource(event).toLowerCase();
        return source === "submit" || source === "result";
    }

    function isManualCompletion(event) {
        const source = completionSource(event).toLowerCase();
        return source === "change" || source === "input";
    }

    function completionSourceClass(source) {
        source = String(source || "legacy").toLowerCase();
        if (source === "initial" || source === "autofill" || source === "pageshow" || source === "page restore") return "auto";
        if (source === "submit" || source === "result") return "late";
        if (source === "change" || source === "input") return "manual";
        return "legacy";
    }

    function firstEntryMode(events) {
        return events.map(calculatorEntryMode).find(mode => mode && mode !== "standard") || "standard";
    }

    function calculatorCompletionSourceTable(events) {
        const completions = events.filter(e => e.eventName === "calculator_field_completed");
        if (!completions.length) return empty("No calculator completion-source events yet.");

        const eventsBySession = groupBy(events, e => e.sessionHash);
        const sessions = Array.from(groupBy(completions, e => e.sessionHash).entries()).map(([sessionHash, items]) => {
            const sessionEvents = eventsBySession.get(sessionHash) || items;
            return {
                entryMode: firstEntryMode(sessionEvents.concat(items)),
                source: completionSourceLabel(mostCommon(items.map(completionSource))),
                flow: mostCommon(sessionEvents.map(e => e.flow || "site")),
                fields: items.length,
                allFields: sessionEvents.some(e => e.eventName === "calculator_all_required_fields_completed"),
                result: sessionEvents.some(e => e.eventName === "calculator_result_generated")
            };
        });

        const rows = Array.from(groupBy(sessions, s => `${s.entryMode}|${s.source}`).entries())
            .map(([key, items]) => {
                const [entryMode, source] = key.split("|");
                const fields = items.reduce((sum, item) => sum + item.fields, 0);
                return {
                    entryMode,
                    source,
                    sessions: items.length,
                    fields,
                    allFields: items.filter(item => item.allFields).length,
                    results: items.filter(item => item.result).length,
                    flow: flowLabel(mostCommon(items.map(item => item.flow)))
                };
            })
            .sort((a, b) => b.results - a.results || b.sessions - a.sessions || b.fields - a.fields)
            .slice(0, 12);

        const maxFields = Math.max(1, ...rows.map(row => row.fields));
        return `
            <div class="source-visual-list">
                ${completionLegend()}
                ${rows.map(row => {
                    const sourceClass = completionSourceClass(row.source);
                    return `
                        <div class="source-visual-row">
                            <div class="source-identity">
                                <strong>${esc(row.entryMode)} / ${esc(row.source)}</strong>
                                <span>
                                    <span class="source-pill entry">${esc(row.entryMode)}</span>
                                    <span class="source-pill ${escAttr(sourceClass)}">${esc(row.source)}</span>
                                    <span class="source-pill flow">${esc(row.flow)}</span>
                                </span>
                            </div>
                            <div class="visual-meter" title="${escAttr(`${row.fields} completed required fields`)}">
                                <span class="visual-meter-fill ${escAttr(sourceClass)}" style="width:${barWidth(row.fields, maxFields)}%"></span>
                            </div>
                            <div class="source-metrics">
                                ${metricChip(row.sessions, "sessions")}
                                ${metricChip(row.fields, "fields")}
                                ${metricChip(row.allFields, "all")}
                                ${metricChip(row.results, "results")}
                            </div>
                        </div>
                    `;
                }).join("")}
            </div>
        `;
    }

    function fieldFrictionTable(events) {
        const relevant = events.filter(e => /^calculator_field_|calculator_validation_failed/.test(e.eventName));
        if (!relevant.length) return empty("No calculator field events yet.");
        const rows = Array.from(groupBy(relevant, e => e.step || "unknown").entries()).map(([field, items]) => {
            const completions = items.filter(e => e.eventName === "calculator_field_completed");
            const auto = completions.filter(isAutomaticCompletion).length;
            const late = completions.filter(isLateCompletion).length;
            const manual = completions.filter(isManualCompletion).length;
            const failed = countMatching(items, ["calculator_validation_failed"]);
            return {
                field,
                touched: countMatching(items, ["calculator_field_touched"]),
                completed: completions.length,
                manual,
                auto,
                late,
                legacy: Math.max(0, completions.length - manual - auto - late),
                failed,
                top: failed
                    ? mostCommon(items.filter(i => i.eventName === "calculator_validation_failed").map(i => i.errorCode || i.outcome || "-"))
                    : mostCommon(completions.map(e => completionSourceLabel(completionSource(e))))
            };
        }).sort((a, b) => b.failed - a.failed || b.late - a.late || b.auto - a.auto || b.touched - a.touched).slice(0, 12);
        return `
            <div class="field-visual-list">
                ${completionLegend(true)}
                ${rows.map(row => {
                    const total = Math.max(1, row.manual + row.auto + row.late + row.legacy + row.failed);
                    return `
                        <div class="field-visual-row">
                            <div class="field-visual-head">
                                <strong>${esc(row.field)}</strong>
                                <span>${esc(row.top)}</span>
                            </div>
                            <div class="stacked-bar" title="${escAttr(`${row.completed} completed, ${row.failed} failed`)}">
                                ${barSegment(row.manual, total, "manual", "manual")}
                                ${barSegment(row.auto, total, "auto", "auto")}
                                ${barSegment(row.late, total, "late", "late")}
                                ${barSegment(row.legacy, total, "legacy", "legacy")}
                                ${barSegment(row.failed, total, "failed", "failed")}
                            </div>
                            <div class="field-metrics">
                                ${metricChip(row.touched, "touch")}
                                ${metricChip(row.completed, "done")}
                                ${metricChip(row.auto, "auto")}
                                ${metricChip(row.late, "late")}
                                ${metricChip(row.failed, "fail")}
                            </div>
                        </div>
                    `;
                }).join("")}
            </div>
        `;
    }

    function completionLegend(includeFailed) {
        const entries = [
            ["manual", "Manual"],
            ["auto", "Auto"],
            ["late", "Late"],
            ["legacy", "Legacy"]
        ];
        if (includeFailed) entries.push(["failed", "Failed"]);
        return `<div class="visual-legend">${entries.map(([key, label]) => `<span><i class="${escAttr(key)}"></i>${esc(label)}</span>`).join("")}</div>`;
    }

    function metricChip(value, label) {
        return `<span class="metric-chip"><strong>${esc(String(value))}</strong>${esc(label)}</span>`;
    }

    function barWidth(value, total) {
        value = Number(value) || 0;
        total = Math.max(1, Number(total) || 1);
        if (value <= 0) return 0;
        return Math.max(4, Math.round((value / total) * 100));
    }

    function barSegment(value, total, className, label) {
        value = Number(value) || 0;
        if (value <= 0) return "";
        return `<span class="${escAttr(className)}" style="width:${barWidth(value, total)}%" title="${escAttr(`${value} ${label}`)}"></span>`;
    }

    function proofUploadTable(events) {
        const names = ["proof_upload_clicked", "proof_camera_clicked", "proof_files_selected", "proof_file_rejected", "proof_processing_succeeded", "proof_processing_failed"];
        const relevant = events.filter(e => names.includes(e.eventName));
        if (!relevant.length) return empty("No proof upload events yet.");
        return groupedTable(relevant, names);
    }

    function handoffTable(events) {
        const relevant = events.filter(e => /handoff|context|payment_offer|browser_storage|review/.test(e.eventName));
        if (!relevant.length) return empty("No handoff events yet.");
        return splitTable(relevant, e => e.eventName);
    }

    function challengeActivationTable(events) {
        return groupedTable(events, [
            "challenge_signup_started",
            "challenge_signup_succeeded",
            "challenge_participant_page_opened",
            "challenge_practice_checkin_submitted",
            "challenge_scored_checkin_submitted"
        ]);
    }

    function sourceQualityTable(events) {
        const rows = Array.from(groupBy(events, acquisitionSource).entries()).map(([source, items]) => {
            const results = uniqueSessionsFor(items, ["calculator_result_generated"]);
            const applications = uniqueSessionsFor(items, ["application_submit_succeeded"]);
            const challenge = uniqueSessionsFor(items, ["challenge_scored_checkin_submitted"]);
            const quality = applications * 5 + challenge * 4 + results;
            return [source, uniqueSessions(items), results, applications, challenge, quality];
        }).sort((a, b) => b[5] - a[5]);
        return table(["Source", "Sessions", "Results", "Apps", "Challenge", "Quality"], rows.slice(0, 12));
    }

    function campaignTable(events) {
        const rows = Array.from(groupBy(events, campaignLabel).entries())
            .filter(([campaign]) => campaign !== "none")
            .map(([campaign, items]) => [
                campaign,
                uniqueSessions(items),
                mostCommon(items.map(acquisitionSource)),
                uniqueSessionsFor(items, ["calculator_result_generated"]),
                uniqueSessionsFor(items, ["application_submit_succeeded", "challenge_signup_succeeded"])
            ])
            .sort((a, b) => b[1] - a[1] || b[4] - a[4])
            .slice(0, 12);
        return rows.length ? table(["Campaign", "Sessions", "Top source", "Results", "Conversions"], rows) : empty("No campaign-tagged sessions yet.");
    }

    function slowStepTable(events) {
        const rows = events.filter(e => Number(e.durationMs) > 5000)
            .sort((a, b) => Number(b.durationMs) - Number(a.durationMs))
            .slice(0, 12)
            .map(e => [e.eventName, e.step || e.component || "-", ms(e.durationMs), e.sessionHash]);
        return rows.length ? table(["Event", "Step", "Duration", "Session"], rows) : empty("No slow events over 5s.");
    }

    function sessionStateTable(events) {
        const rows = Array.from(groupBy(events, e => e.sessionHash).entries()).map(([session, items]) => {
            const sorted = items.slice().sort((a, b) => b.time - a.time);
            const last = sorted[0];
            return [session, last.eventName, last.outcome || "-", formatTime(last.occurredAtUtc)];
        }).slice(0, 12);
        return rows.length ? table(["Session", "Last event", "State", "Time"], rows) : empty("No review sessions yet.");
    }

    function groupedTable(events, names) {
        const rows = names.map(name => {
            const items = events.filter(e => e.eventName === name);
            return [name, items.length, new Set(items.map(i => i.sessionHash)).size, mostCommon(items.map(acquisitionSource))];
        }).filter(r => r[1] > 0);
        return rows.length ? table(["Event", "Events", "Sessions", "Top source"], rows) : empty("No matching events yet.");
    }

    function splitTable(events, picker) {
        const rows = Array.from(groupBy(events, picker).entries())
            .map(([key, items]) => [key || "unknown", items.length, new Set(items.map(i => i.sessionHash)).size])
            .sort((a, b) => b[1] - a[1])
            .slice(0, 12);
        return rows.length ? table(["Segment", "Events", "Sessions"], rows) : empty("No data for this split.");
    }

    function sourceFunnelDefs() {
        return [["site_page_viewed", "Site viewed"], ["calculator_result_generated", "Calculator result"], ["application_submit_succeeded", "Application submitted"], ["challenge_signup_succeeded", "Challenge signup"], ["challenge_scored_checkin_submitted", "Scored check-in"]];
    }

    function reliabilityFunnelDefs() {
        return [["client_error_observed", "Client errors"], ["api_request_failed", "API failures"], ["calculator_validation_failed", "Calculator validation"], ["rank_preview_failed", "Rank preview failed"], ["application_submit_failed", "Application submit failed"], ["challenge_signup_failed", "Challenge signup failed"], ["challenge_scored_checkin_failed", "Check-in failed"]];
    }

    function eventsFunnelDefs() {
        return [["homepage_highlight_viewed", "Highlight viewed"], ["homepage_highlight_clicked", "Highlight clicked"], ["event_viewed", "Event viewed"], ["event_link_clicked", "Event link clicked"], ["athlete_profile_viewed", "Athlete profile"], ["league_viewed", "League viewed"]];
    }

    function normalizeEvent(event) {
        const referrerDomain = event.referrerDomain || "";
        return Object.assign({}, event, {
            eventName: event.eventName || "",
            sessionHash: event.sessionHash || "S-UNKNOWN",
            referrerDomain,
            source: effectiveSource(event.source, referrerDomain),
            landingRoute: event.landingRoute || "",
            firstReferrerDomain: event.firstReferrerDomain || "",
            firstSource: effectiveSource(event.firstSource || event.source, event.firstReferrerDomain || referrerDomain),
            firstCampaign: event.firstCampaign || "",
            firstUtmSource: event.firstUtmSource || "",
            firstUtmMedium: event.firstUtmMedium || "",
            firstUtmCampaign: event.firstUtmCampaign || "",
            firstUtmTerm: event.firstUtmTerm || "",
            firstUtmContent: event.firstUtmContent || "",
            metadata: event.metadata || {},
            time: Date.parse(event.occurredAtUtc || "") || 0
        });
    }

    function normalizeTrafficSummary(summary) {
        summary = summary && typeof summary === "object" ? summary : {};
        return {
            totals: normalizeTrafficTotals(summary.totals),
            previousTotals: normalizeTrafficTotals(summary.previousTotals),
            cleanTotals: normalizeTrafficTotals(summary.cleanTotals),
            quality: normalizeTrafficQuality(summary.quality),
            daily: normalizeTrafficRows(summary.daily, row => ({
                day: row.day || "",
                sessions: numberValue(row.sessions),
                pageViews: numberValue(row.pageViews),
                events: numberValue(row.events),
                successSessions: numberValue(row.successSessions),
                successActions: numberValue(row.successActions)
            })),
            topPages: normalizeTrafficRows(summary.topPages, row => ({
                route: row.route || "unknown",
                sessions: numberValue(row.sessions),
                pageViews: numberValue(row.pageViews)
            })),
            sources: normalizeTrafficRows(summary.sources, normalizeTrafficBreakdown),
            referrers: normalizeTrafficRows(summary.referrers, normalizeTrafficBreakdown),
            devices: normalizeTrafficRows(summary.devices, normalizeTrafficBreakdown),
            browsers: normalizeTrafficRows(summary.browsers, normalizeTrafficBreakdown)
        };
    }

    function normalizeTrafficTotals(totals) {
        totals = totals && typeof totals === "object" ? totals : {};
        return {
            sessions: numberValue(totals.sessions),
            pageViews: numberValue(totals.pageViews),
            events: numberValue(totals.events)
        };
    }

    function normalizeTrafficRows(rows, mapper) {
        return Array.isArray(rows) ? rows.map(row => mapper(row || {})) : [];
    }

    function normalizeTrafficBreakdown(row) {
        return {
            label: row.label || "unknown",
            sessions: numberValue(row.sessions),
            pageViews: numberValue(row.pageViews),
            events: numberValue(row.events)
        };
    }

    function normalizeTrafficQuality(quality) {
        quality = quality && typeof quality === "object" ? quality : {};
        return {
            rawSessions: numberValue(quality.rawSessions),
            cleanSessions: numberValue(quality.cleanSessions),
            noisySessions: numberValue(quality.noisySessions),
            topSessionEvents: numberValue(quality.topSessionEvents),
            topSessionShare: numberValue(quality.topSessionShare),
            repeatedPageViewSessions: numberValue(quality.repeatedPageViewSessions),
            pageViewDominantSessions: numberValue(quality.pageViewDominantSessions),
            noisyPageViews: numberValue(quality.noisyPageViews),
            noisyPageViewShare: numberValue(quality.noisyPageViewShare)
        };
    }

    function emptyTrafficSummary() {
        return {
            totals: emptyTrafficTotals(),
            previousTotals: emptyTrafficTotals(),
            cleanTotals: emptyTrafficTotals(),
            quality: emptyTrafficQuality(),
            daily: [],
            topPages: [],
            sources: [],
            referrers: [],
            devices: [],
            browsers: []
        };
    }

    function emptyTrafficTotals() {
        return { sessions: 0, pageViews: 0, events: 0 };
    }

    function emptyTrafficQuality() {
        return {
            rawSessions: 0,
            cleanSessions: 0,
            noisySessions: 0,
            topSessionEvents: 0,
            topSessionShare: 0,
            repeatedPageViewSessions: 0,
            pageViewDominantSessions: 0,
            noisyPageViews: 0,
            noisyPageViewShare: 0
        };
    }

    function numberValue(value) {
        value = Number(value);
        return Number.isFinite(value) ? value : 0;
    }

    function effectiveSource(source, referrerDomain) {
        if (isInternalReferrer(referrerDomain)) return "internal";
        return source || "direct";
    }

    function acquisitionSource(event) {
        return event.firstSource || event.source || "direct";
    }

    function campaignLabel(event) {
        return event.firstCampaign
            || event.firstUtmCampaign
            || event.firstUtmSource
            || "none";
    }

    function isInternalReferrer(referrerDomain) {
        if (!referrerDomain) return false;
        const normalized = String(referrerDomain).toLowerCase().replace(/^www\./, "");
        return normalized === "longevityworldcup.com";
    }

    function table(headers, rows) {
        return `
            <table class="compact-table">
                <thead><tr>${headers.map(h => `<th>${esc(h)}</th>`).join("")}</tr></thead>
                <tbody>${rows.map(row => `<tr>${row.map(cell => `<td>${esc(String(cell ?? ""))}</td>`).join("")}</tr>`).join("")}</tbody>
            </table>
        `;
    }

    function detailPanel(title, content) {
        return `<section class="detail-panel"><div class="panel-heading"><h2>${esc(title)}</h2></div>${content}</section>`;
    }

    function summaryItem(label, value) {
        return `<div class="summary-item"><span>${esc(label)}</span><strong>${esc(String(value))}</strong></div>`;
    }

    function spark(events, names) {
        const buckets = Array.from({ length: 7 }, () => 0);
        const now = Date.now();
        const day = 24 * 60 * 60 * 1000;
        const filtered = names && names.length ? events.filter(e => names.includes(e.eventName)) : events;
        filtered.forEach(e => {
            const index = 6 - Math.floor((now - e.time) / day);
            if (index >= 0 && index < buckets.length) buckets[index]++;
        });
        const max = Math.max(1, ...buckets);
        return `<span class="spark" aria-hidden="true">${buckets.map(v => `<span style="height:${Math.max(2, Math.round((v / max) * 18))}px"></span>`).join("")}</span>`;
    }

    function groupBy(items, picker) {
        const map = new Map();
        items.forEach(item => {
            const key = picker(item) || "unknown";
            if (!map.has(key)) map.set(key, []);
            map.get(key).push(item);
        });
        return map;
    }

    function mostCommon(values) {
        const map = new Map();
        values.filter(Boolean).forEach(v => map.set(v, (map.get(v) || 0) + 1));
        return Array.from(map.entries()).sort((a, b) => b[1] - a[1])[0]?.[0] || "-";
    }

    function medianDuration(events) {
        const values = events.map(e => Number(e.durationMs)).filter(Number.isFinite).sort((a, b) => a - b);
        if (!values.length) return "-";
        return ms(values[Math.floor(values.length / 2)]);
    }

    function percent(value, total) {
        if (!total) return "0%";
        return `${Math.round((value / total) * 100)}%`;
    }

    function ratio(value, total) {
        total = Number(total) || 0;
        return total ? (Number(value) || 0) / total : 0;
    }

    function formatNumber(value) {
        return numberValue(value).toLocaleString();
    }

    function signedNumber(value) {
        value = Number(value) || 0;
        if (value === 0) return "0";
        return `${value > 0 ? "+" : "-"}${formatNumber(Math.abs(value))}`;
    }

    function formatDay(value) {
        const date = new Date(`${value}T00:00:00Z`);
        if (Number.isNaN(date.getTime())) return value || "-";
        return date.toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });
    }

    function uniqueSessions(items) {
        return new Set(items.map(i => i.sessionHash)).size;
    }

    function ms(value) {
        value = Number(value);
        if (!Number.isFinite(value)) return "-";
        if (value < 1000) return `${Math.round(value)}ms`;
        return `${(value / 1000).toFixed(1)}s`;
    }

    function offset(value) {
        value = Math.max(0, Number(value) || 0);
        const total = Math.floor(value / 1000);
        const min = Math.floor(total / 60);
        const sec = total % 60;
        return `${String(min).padStart(2, "0")}:${String(sec).padStart(2, "0")}`;
    }

    function metadataChips(metadata) {
        const entries = Object.entries(metadata || {}).slice(0, 4);
        if (!entries.length) return "";
        return ` / ${entries.map(([k, v]) => `${k}=${v}`).join(" / ")}`;
    }

    function formatTime(value) {
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return "-";
        return date.toLocaleString(undefined, { month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
    }

    function flowLabel(flow) {
        return flow === "pheno" ? "pheno age" : flow === "bortz" ? "bortz age" : flow === "challenge" ? "Challenge" : flow === "application" ? "application" : flow === "onboarding" ? "onboarding" : "all";
    }

    function setStatus(message, error) {
        const host = el("statsStatus");
        host.textContent = message;
        host.classList.toggle("error", !!error);
    }

    function exportCsv() {
        const events = selectedRawEvents();
        const headers = ["occurredAtUtc", "sessionHash", "actorHash", "eventName", "flow", "route", "component", "step", "outcome", "errorCode", "durationMs", "deviceClass", "browserFamily", "referrerDomain", "source", "landingRoute", "firstReferrerDomain", "firstSource", "firstCampaign", "firstUtmSource", "firstUtmMedium", "firstUtmCampaign", "firstUtmTerm", "firstUtmContent"];
        const lines = [headers.join(",")].concat(events.map(e => headers.map(h => csv(e[h])).join(",")));
        const blob = new Blob([lines.join("\n")], { type: "text/csv;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = "site-statistics-redacted.csv";
        a.click();
        setTimeout(() => URL.revokeObjectURL(url), 500);
    }

    async function copyLink() {
        try {
            await navigator.clipboard.writeText(window.location.href);
            setStatus("Copied dashboard link.");
        } catch (_) {
            setStatus("Copy failed; the URL is already in the address bar.", true);
        }
    }

    function csv(value) {
        const text = String(value ?? "");
        return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
    }

    function empty(message) {
        return `<div class="empty-state">${esc(message)}</div>`;
    }

    function el(id) {
        return document.getElementById(id);
    }

    function esc(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    function escAttr(value) {
        return esc(value).replace(/'/g, "&#39;");
    }
})();
