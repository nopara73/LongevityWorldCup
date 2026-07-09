(function () {
    "use strict";

    const endpoint = "/api/site-statistics/event";
    const sessionKey = "lwcSiteStatsSessionId";
    const firstTouchKey = "lwcSiteStatsFirstTouch";
    const sessionHeader = "X-LWC-Stats-Session";
    const originalFetch = window.fetch ? window.fetch.bind(window) : null;
    const pageStartedAt = now();
    const sentOnce = new Set();
    const trackedPageViews = new Set();
    const completedFields = new Set();
    const touchedFields = new Set();
    let lastRequiredProgress = -1;
    let lastCheckInKind = "scored";

    function now() {
        return typeof performance !== "undefined" && performance.now ? performance.now() : Date.now();
    }

    function safe(fn) {
        try { return fn(); } catch (_) { return null; }
    }

    function listen(target, type, handler, options) {
        if (!target || typeof target.addEventListener !== "function") return;
        safe(() => {
            target.addEventListener(type, event => {
                safe(() => handler(event));
            }, options);
        });
    }

    function getSessionId() {
        const existing = safe(() => sessionStorage.getItem(sessionKey));
        if (existing) return existing;
        const id = safe(() => crypto.randomUUID()) || `${Date.now()}-${Math.random().toString(16).slice(2)}`;
        safe(() => sessionStorage.setItem(sessionKey, id));
        return id;
    }

    function route() {
        return `${window.location.pathname}${window.location.search || ""}`;
    }

    function campaignValue(name) {
        const value = safe(() => new URLSearchParams(window.location.search).get(name)) || "";
        return safeToken(value, 96);
    }

    function hasCampaignParams() {
        return !!(campaignValue("campaign") || campaignValue("utm_source") || campaignValue("utm_medium") || campaignValue("utm_campaign"));
    }

    function flowFromPath() {
        const path = window.location.pathname.toLowerCase();
        if (path.includes("bortz-age") || path === "/bortz-age") return "bortz";
        if (path.includes("pheno-age") || path === "/pheno-age") return "pheno";
        if (path.includes("join-game") || path === "/join") return "onboarding";
        if (path.includes("convergence") || path === "/apply") return "application";
        if (path.includes("application-review") || path === "/review") return "application";
        if (path.includes("proof-upload") || path === "/proofs") return "application";
        if (path.includes("longevitymaxxing")) return "challenge";
        return "site";
    }

    function isReviewSource(value) {
        return value === "proof-upload" || value === "edit-profile";
    }

    function hasApplicationReviewContext() {
        return !!safe(() => {
            const params = new URLSearchParams(window.location.search);
            return sessionStorage.getItem("pendingPaymentInvoice") ||
                localStorage.getItem("pendingPaymentInvoicePersistent") ||
                isReviewSource(params.get("from")) ||
                isReviewSource(sessionStorage.getItem("came-from")) ||
                sessionStorage.getItem("contactEmail") ||
                localStorage.getItem("contactEmail");
        });
    }

    function deviceClass() {
        const ua = navigator.userAgent || "";
        if (/ipad|tablet/i.test(ua)) return "tablet";
        if (/mobi|android|iphone/i.test(ua)) return "mobile";
        return "desktop";
    }

    function browserFamily() {
        const ua = navigator.userAgent || "";
        if (/Edg\//.test(ua)) return "Edge";
        if (/Chrome\//.test(ua)) return "Chrome";
        if (/Firefox\//.test(ua)) return "Firefox";
        if (/Safari\//.test(ua)) return "Safari";
        return "other";
    }

    function referrerDomain() {
        return safe(() => document.referrer ? new URL(document.referrer).hostname.toLowerCase() : "") || "";
    }

    function isInternalReferrer(host) {
        if (!host) return false;
        const normalizedHost = String(host).toLowerCase().replace(/^www\./, "");
        const currentHost = safe(() => window.location.hostname.toLowerCase().replace(/^www\./, "")) || "";
        return normalizedHost === currentHost || normalizedHost === "longevityworldcup.com";
    }

    function isEmailReferrer(host) {
        if (!host) return false;
        const normalizedHost = String(host).toLowerCase();
        return normalizedHost === "com.google.android.gm"
            || /^mail\./i.test(normalizedHost)
            || /\.mail\./i.test(normalizedHost)
            || /gmail|outlook|hotmail|protonmail|proton\.me|fastmail|icloud|mail\.yahoo|yahoomail/i.test(normalizedHost);
    }

    function source() {
        const host = referrerDomain();
        if (!host) return hasCampaignParams() ? "campaign" : "direct";
        if (isInternalReferrer(host)) return "internal";
        if (isEmailReferrer(host)) return "email";
        if (/google|bing|duckduckgo|yahoo|brave|search/i.test(host)) return "search";
        if (/x\.com|twitter|facebook|instagram|threads|youtube|linkedin|reddit|slack/i.test(host)) return "social";
        return "referral";
    }

    function getFirstTouch() {
        const existing = safe(() => sessionStorage.getItem(firstTouchKey));
        if (existing) {
            const parsed = safe(() => JSON.parse(existing));
            if (parsed && typeof parsed === "object") return parsed;
        }

        const touch = {
            landingRoute: route(),
            firstReferrerDomain: referrerDomain(),
            firstSource: source(),
            firstCampaign: campaignValue("campaign") || campaignValue("utm_campaign"),
            firstUtmSource: campaignValue("utm_source"),
            firstUtmMedium: campaignValue("utm_medium"),
            firstUtmCampaign: campaignValue("utm_campaign"),
            firstUtmTerm: campaignValue("utm_term"),
            firstUtmContent: campaignValue("utm_content")
        };
        safe(() => sessionStorage.setItem(firstTouchKey, JSON.stringify(touch)));
        return touch;
    }

    function track(eventName, options) {
        safe(() => {
            if (!eventName || !originalFetch) return;
            const firstTouch = getFirstTouch();
            const payload = Object.assign({
                eventName,
                sessionId: getSessionId(),
                flow: flowFromPath(),
                route: route(),
                durationMs: Math.max(0, Math.round(now() - pageStartedAt)),
                deviceClass: deviceClass(),
                browserFamily: browserFamily(),
                referrerDomain: referrerDomain(),
                source: source(),
                landingRoute: firstTouch.landingRoute,
                firstReferrerDomain: firstTouch.firstReferrerDomain,
                firstSource: firstTouch.firstSource,
                firstCampaign: firstTouch.firstCampaign,
                firstUtmSource: firstTouch.firstUtmSource,
                firstUtmMedium: firstTouch.firstUtmMedium,
                firstUtmCampaign: firstTouch.firstUtmCampaign,
                firstUtmTerm: firstTouch.firstUtmTerm,
                firstUtmContent: firstTouch.firstUtmContent
            }, options || {});

            const body = JSON.stringify(payload);
            if (navigator.sendBeacon && body.length < 60000) {
                const blob = new Blob([body], { type: "application/json" });
                navigator.sendBeacon(endpoint, blob);
                return;
            }

            originalFetch(endpoint, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body,
                keepalive: body.length < 60000
            }).catch(() => {});
        });
    }

    function trackOnce(key, eventName, options) {
        if (sentOnce.has(key)) return;
        sentOnce.add(key);
        track(eventName, options);
    }

    function isIgnoredClientErrorMessage(message) {
        message = String(message || "").trim();
        return message === "ResizeObserver loop completed with undelivered notifications." ||
            message === "ResizeObserver loop completed with undelivered notifications" ||
            message === "ResizeObserver loop limit exceeded";
    }

    function isSameOriginFetch(input) {
        const raw = safe(() => input && input.url ? input.url : input) || "";
        const parsed = safe(() => new URL(String(raw), window.location.href));
        return !!parsed && parsed.origin === window.location.origin && parsed.pathname.indexOf("/api/") === 0;
    }

    function withStatsSessionHeader(args) {
        if (!args || !args.length || typeof Headers === "undefined" || !isSameOriginFetch(args[0])) return args;
        const init = args[1] ? Object.assign({}, args[1]) : {};
        const headers = new Headers(init.headers || (typeof Request !== "undefined" && args[0] instanceof Request ? args[0].headers : undefined));
        if (!headers.has(sessionHeader)) headers.set(sessionHeader, getSessionId());
        init.headers = headers;

        if (typeof Request !== "undefined" && args[0] instanceof Request) {
            return [new Request(args[0], init)];
        }

        return [args[0], init];
    }

    function fieldKey(el) {
        return (el && (el.id || el.name || el.getAttribute("aria-label"))) || "unknown";
    }

    function safeFieldKey(el) {
        const key = fieldKey(el);
        if (/email|name|token|note|why|media|link/i.test(key)) return "private_field";
        return key;
    }

    function safeToken(value, maxLength) {
        value = String(value || "").trim();
        if (!value) return "";
        if (/@|https?:\/\/|data:|secret|bearer|password|token/i.test(value)) return "";
        return value
            .slice(0, maxLength || 96)
            .replace(/[^\w./:$ -]/g, "")
            .trim();
    }

    function fileTypeBucket(file) {
        const type = String(file && file.type || "").toLowerCase();
        const name = String(file && file.name || "").toLowerCase();
        if (type === "application/pdf" || name.endsWith(".pdf")) return "pdf";
        if (name.endsWith(".heic") || name.endsWith(".heif")) return "heic_heif";
        if (type.startsWith("image/")) return "image";
        return "unsupported";
    }

    function fileSizeBucket(file) {
        const size = Number(file && file.size);
        if (!Number.isFinite(size)) return "unknown";
        if (size < 500 * 1024) return "under_500kb";
        if (size < 2 * 1024 * 1024) return "500kb_to_2mb";
        if (size < 5 * 1024 * 1024) return "2mb_to_5mb";
        if (size < 10 * 1024 * 1024) return "5mb_to_10mb";
        return "over_10mb";
    }

    function countBucket(count) {
        if (count <= 0) return "0";
        if (count <= 2) return "1_2";
        if (count <= 5) return "3_5";
        if (count <= 10) return "6_10";
        if (count <= 30) return "11_30";
        return "over_30";
    }

    function amountBucket(raw) {
        const value = Number(raw);
        if (!Number.isFinite(value)) return "unknown";
        if (value <= 0) return "$0";
        if (value < 50) return "$1_49";
        if (value < 100) return "$50_99";
        if (value < 300) return "$100_299";
        if (value < 1000) return "$300_999";
        return "$1000_plus";
    }

    function ageReductionBucketFromText(text) {
        const value = Number(String(text || "").replace(/[^\d+.-]/g, ""));
        if (!Number.isFinite(value)) return "unknown";
        const reduction = String(text || "").includes("-") ? Math.abs(value) : -value;
        if (reduction < 0) return "below_0";
        if (reduction < 5) return "0_to_5";
        if (reduction < 10) return "5_to_10";
        if (reduction < 20) return "10_to_20";
        return "20_plus";
    }

    function pathLower() {
        return String(window.location.pathname || "/").toLowerCase();
    }

    function queryValue(name) {
        return safe(() => new URLSearchParams(window.location.search || "").get(name)) || "";
    }

    function pathSegmentAfter(prefix) {
        const path = pathLower();
        if (!path.startsWith(prefix)) return "";
        return safeToken(decodeURIComponent(path.slice(prefix.length).split("/")[0] || ""), 64);
    }

    function currentAthleteSlug() {
        return pathSegmentAfter("/athlete/") || safeToken(queryValue("athlete"), 64);
    }

    function currentLeagueSlug() {
        const routeSlug = pathSegmentAfter("/league/");
        if (routeSlug) return routeSlug;
        const view = safeToken(queryValue("view"), 64);
        if (view) return view;
        const filters = safeToken(queryValue("filters"), 128);
        if (filters) return safeToken(filters.split(",")[0], 64);
        return pathLower() === "/leaderboard" ? "ultimate" : "";
    }

    function isHomepagePath() {
        const path = pathLower();
        return path === "/" || path === "/index.html";
    }

    function isEventBoardPath() {
        const path = pathLower();
        return path === "/events" || path.includes("/event-board/");
    }

    function eventBoardClickMetadata(target) {
        const link = target && target.closest ? target.closest("a[href]") : null;
        const row = target && target.closest ? target.closest("tr.main-row") : null;
        const href = link ? String(link.getAttribute("href") || "") : "";
        let targetKind = "event";
        if (link && link.classList.contains("view-all")) targetKind = "view_all";
        else if (link && (link.classList.contains("event-athlete-link") || href.startsWith("/athlete/"))) targetKind = "athlete";
        else if (href.startsWith("/league/") || href.includes("view=") || href.includes("filters=")) targetKind = "league";
        else if (link) targetKind = "link";

        const metadata = { targetKind };
        if (row && row.dataset.eventTypeName) metadata.eventType = safeToken(row.dataset.eventTypeName, 64);
        if (row && row.dataset.highlightSelection) metadata.highlightSelection = safeToken(row.dataset.highlightSelection, 64);
        if (row && row.dataset.primarySlug) metadata.primarySlug = safeToken(row.dataset.primarySlug, 64);
        return metadata;
    }

    function trackPublicPageViews() {
        if (isHomepagePath() && !currentAthleteSlug()) {
            const highlightsRoot = document.getElementById("events-root-index");
            if (highlightsRoot) {
                track("homepage_highlight_viewed", {
                    component: "homepage",
                    step: "highlights",
                    outcome: "viewed"
                });
            }
        }

        if (isEventBoardPath()) {
            track("event_viewed", {
                component: "event_board",
                outcome: "viewed"
            });
        }

        const athleteSlug = currentAthleteSlug();
        if (athleteSlug) {
            track("athlete_profile_viewed", {
                component: "athlete_profile",
                outcome: "viewed",
                metadata: { athleteSlug }
            });
        }

        const leagueSlug = currentLeagueSlug();
        if (leagueSlug) {
            track("league_viewed", {
                component: "leaderboard",
                outcome: "viewed",
                metadata: { league: leagueSlug }
            });
        }
    }

    function setupPageViews() {
        const pageViewKey = `${window.location.pathname.toLowerCase()}${window.location.search || ""}`;
        if (trackedPageViews.has(pageViewKey)) return;
        trackedPageViews.add(pageViewKey);

        const path = window.location.pathname.toLowerCase();
        if (flowFromPath() === "challenge") {
            track("challenge_page_viewed", { component: "challenge", outcome: "viewed" });
            const params = new URLSearchParams(window.location.search);
            if (params.has("confirm")) track("challenge_confirmation_link_opened", { component: "challenge", outcome: "opened" });
            if (params.has("token")) track("challenge_participant_page_opened", { component: "challenge", outcome: "opened" });
            if (params.has("stop")) track("challenge_stop_email_clicked", { component: "challenge", outcome: "clicked" });
            return;
        }

        if (flowFromPath() === "onboarding") {
            track("onboarding_entry_viewed", { component: "join_game", outcome: "viewed" });
            return;
        }

        if (path.includes("application-review") || path === "/review") {
            track("application_review_opened", { component: "application_review", outcome: "opened" });
            const hasContext = hasApplicationReviewContext();
            track(hasContext ? "application_review_context_found" : "application_review_context_missing", {
                component: "application_review",
                outcome: hasContext ? "found" : "missing"
            });
            return;
        }

        if (path.includes("convergence") || path === "/apply") {
            track("proof_flow_opened", { component: "application", outcome: "opened" });
            return;
        }

        if (path.includes("proof-upload") || path === "/proofs") {
            track("proof_flow_opened", { component: "application", outcome: "opened" });
            const hasBiomarkerData = !!safe(() => sessionStorage.getItem("biomarkerData"));
            track(hasBiomarkerData ? "biomarker_handoff_found" : "proof_flow_missing_handoff", {
                component: "handoff",
                outcome: hasBiomarkerData ? "found" : "missing"
            });
            return;
        }

        if (flowFromPath() === "pheno" || flowFromPath() === "bortz") {
            track("onboarding_page_viewed", { component: "calculator", outcome: "viewed" });
            track("calculator_form_visible", { component: "calculator", outcome: "visible" });
            return;
        }

        trackPublicPageViews();
        track("site_page_viewed", { component: "site", outcome: "viewed" });
    }

    function scheduleJoinPanelViewForCurrentRoute() {
        const enqueue = typeof queueMicrotask === "function"
            ? queueMicrotask
            : callback => setTimeout(callback, 0);

        enqueue(() => safe(trackJoinPanelViewForCurrentRoute));
    }

    function trackJoinPanelViewForCurrentRoute() {
        if (window.location.pathname.toLowerCase() === "/join") {
            setupPageViews();
        }
    }

    function setupSpaRouteTracking() {
        if (!window.history) return;

        ["pushState", "replaceState"].forEach(method => {
            const original = window.history[method];
            if (typeof original !== "function") return;
            window.history[method] = function () {
                const result = original.apply(this, arguments);
                scheduleJoinPanelViewForCurrentRoute();
                return result;
            };
        });

        listen(window, "popstate", scheduleJoinPanelViewForCurrentRoute);
    }

    function setupCalculatorTracking() {
        const form = document.getElementById("phenoAgeForm") || document.getElementById("bortzAgeForm");
        if (!form) return;

        const required = Array.from(form.querySelectorAll("input[required], select[required], textarea[required]"));

        function fieldHasRequiredValue(el) {
            if (!el) return false;
            if (el.type === "checkbox" || el.type === "radio") return !!el.checked;
            return String(el.value || "").trim().length > 0;
        }

        function requiredProgressThreshold() {
            const pct = required.length ? Math.floor((completedFields.size / required.length) * 100) : 0;
            return pct >= 100 ? 100 : pct >= 75 ? 75 : pct >= 50 ? 50 : pct >= 25 ? 25 : 0;
        }

        function recordRequiredProgress(source) {
            const threshold = requiredProgressThreshold();
            if (threshold <= lastRequiredProgress) return;

            lastRequiredProgress = threshold;
            track("calculator_required_progress_changed", {
                component: "calculator",
                step: "required_fields",
                outcome: "progress",
                metadata: {
                    progressBucket: String(threshold),
                    completionSource: source
                }
            });
            if (threshold === 100) {
                track("calculator_all_required_fields_completed", {
                    component: "calculator",
                    outcome: "completed",
                    metadata: { completionSource: source }
                });
            }
        }

        function recordFieldCompletion(el, source) {
            const key = safeFieldKey(el);
            if (!fieldHasRequiredValue(el) || completedFields.has(key)) return false;

            completedFields.add(key);
            track("calculator_field_completed", {
                component: "calculator",
                step: key,
                outcome: "completed",
                metadata: { completionSource: source }
            });
            return true;
        }

        function scanRequiredFields(source) {
            let changed = false;
            required.forEach(el => {
                if (recordFieldCompletion(el, source)) changed = true;
            });
            if (changed || completedFields.size > 0) recordRequiredProgress(source);
        }

        listen(form, "submit", () => {
            scanRequiredFields("submit");
            track("calculator_started", { component: "calculator", outcome: "submitted" });
        }, true);

        listen(form, "invalid", event => {
            track("calculator_validation_failed", {
                component: "calculator",
                step: safeFieldKey(event.target),
                outcome: "failed",
                errorCode: "browser_validation"
            });
        }, true);

        listen(form, "input", event => {
            if (event && required.includes(event.target)) {
                recordFieldCompletion(event.target, "input");
                recordRequiredProgress("input");
            }
        }, true);

        required.forEach(el => {
            listen(el, "focus", () => {
                const key = safeFieldKey(el);
                if (touchedFields.has(key)) return;
                touchedFields.add(key);
                track("calculator_field_touched", { component: "calculator", step: key, outcome: "touched" });
            }, { passive: true });

            listen(el, "change", () => {
                recordFieldCompletion(el, "change");
                recordRequiredProgress("change");
            }, { passive: true });
        });

        scanRequiredFields("initial");
        [250, 1000, 2500].forEach(delay => {
            setTimeout(() => safe(() => scanRequiredFields("autofill")), delay);
        });
        listen(window, "pageshow", () => scanRequiredFields("pageshow"));

        const result = document.getElementById("phenoAgeResult") || document.getElementById("bortzAgeResult");
        const rank = document.getElementById("phenoAgeRankPreview") || document.getElementById("bortzAgeRankPreview");
        const continueButton = document.getElementById("continueButton");
        if (continueButton) {
            listen(continueButton, "click", () => {
                track("calculator_continue_clicked", { component: "calculator", outcome: "clicked" });
                const paymentOffer = !!safe(() => sessionStorage.getItem("pendingPaymentOffer"));
                const biomarkerData = !!safe(() => sessionStorage.getItem("biomarkerData"));
                if (paymentOffer) track("payment_offer_stored", { component: "handoff", outcome: "stored" });
                if (biomarkerData) track("biomarker_handoff_stored", { component: "handoff", outcome: "stored" });
            }, { passive: true });
        }

        if (result) {
            const observer = new MutationObserver(() => {
                safe(() => {
                    if (!result.classList.contains("show")) return;
                    scanRequiredFields("result");
                    const yearsText = document.getElementById("yearsText");
                    trackOnce(`result-${flowFromPath()}`, "calculator_result_generated", {
                        component: "calculator",
                        outcome: "succeeded",
                        metadata: { ageReductionBucket: ageReductionBucketFromText(yearsText && yearsText.textContent) }
                    });
                });
            });
            observer.observe(result, { attributes: true, attributeFilter: ["class"] });
        }

        if (rank) {
            const observer = new MutationObserver(() => {
                safe(() => {
                    if (rank.hidden || !rank.textContent.trim()) return;
                    trackOnce(`rank-${flowFromPath()}`, "rank_preview_rendered", {
                        component: "rank_preview",
                        outcome: "rendered"
                    });
                });
            });
            observer.observe(rank, { childList: true, subtree: true, attributes: true, attributeFilter: ["hidden", "aria-busy"] });
        }
    }

    function setupProofTracking() {
        ["proofPicInput", "proofCameraInput", "profilePicInput", "profileCameraInput"].forEach(id => {
            const input = document.getElementById(id);
            if (!input) return;
            listen(input, "change", () => {
                const files = Array.from(input.files || []);
                const isProof = id.toLowerCase().includes("proof");
                const first = files[0];
                track(isProof ? "proof_files_selected" : "profile_picture_started", {
                    component: isProof ? "proof_upload" : "profile_picture",
                    outcome: files.length ? "selected" : "empty",
                    metadata: {
                        fileCountBucket: countBucket(files.length),
                        fileTypeBucket: fileTypeBucket(first),
                        fileSizeBucket: fileSizeBucket(first),
                        sourceControl: id
                    }
                });
            }, { passive: true });
        });

        listen(document.getElementById("uploadProofButton"), "click", () => {
            track("proof_upload_clicked", { component: "proof_upload", outcome: "clicked" });
        }, { passive: true });
        listen(document.getElementById("takeProofPhotoButton"), "click", () => {
            track("proof_camera_clicked", { component: "proof_upload", outcome: "clicked" });
        }, { passive: true });
        listen(document.getElementById("nextButton"), "click", () => {
            const applyDetailsVisible = document.getElementById("applyDetails")?.style.display !== "none";
            if (applyDetailsVisible) {
                track("application_submit_clicked", { component: "application", outcome: "clicked" });
            }
        }, { passive: true });
    }

    function setupJoinGameTracking() {
        const amateur = document.getElementById("joinStartAmateurBtn") || document.querySelector("[onclick*='startAmateurApplication']");
        const pro = document.getElementById("joinGoProButton") || document.querySelector("[onclick*='startProApplication']");
        const challenge = document.getElementById("joinStartChallengeLink");
        listen(amateur, "click", () => {
            track("onboarding_clock_selected", {
                flow: "pheno",
                component: "join_game",
                step: "amateur",
                outcome: "selected",
                metadata: { track: "amateur" }
            });
        }, { passive: true });
        listen(pro, "click", () => {
            track("onboarding_clock_selected", {
                flow: "bortz",
                component: "join_game",
                step: "pro",
                outcome: "selected",
                metadata: { track: "pro" }
            });
        }, { passive: true });
        listen(challenge, "click", () => {
            track("onboarding_challenge_selected", {
                flow: "challenge",
                component: "join_game",
                step: "challenge",
                outcome: "selected",
                metadata: { track: "challenge" }
            });
        }, { passive: true });

        document.querySelectorAll(".biomarker-disclosure, .play-join-biomarkers details").forEach(details => {
            listen(details, "toggle", () => {
                if (!details.open) return;
                const isPro = !!details.closest(".track-card.pro, .play-join-card--pro");
                track("onboarding_biomarker_requirements_opened", {
                    flow: isPro ? "bortz" : "pheno",
                    component: "join_game",
                    step: "biomarkers",
                    outcome: "opened",
                    metadata: { track: isPro ? "pro" : "amateur" }
                });
            });
        });
    }

    function setupChallengeTracking() {
        const signupForm = document.getElementById("lmxSignupForm");
        if (!signupForm) return;

        listen(signupForm, "input", event => {
            const target = event.target;
            if (!target) return;
            if (target.id === "lmxSignupEmail") {
                trackOnce("challenge-signup-started", "challenge_signup_started", { component: "signup", outcome: "started" });
                return;
            }
            if (target.id === "lmxSignupName") {
                trackOnce("challenge-username-completed", "challenge_username_completed", { component: "signup", outcome: "completed" });
                return;
            }
        }, { passive: true });

        listen(signupForm, "invalid", event => {
            const key = safeFieldKey(event.target);
            track(key === "lmxSignupEmail" ? "challenge_email_validation_failed" : "challenge_signup_validation_failed", {
                component: "signup",
                step: key,
                outcome: "failed",
                errorCode: "browser_validation"
            });
        }, true);

        listen(signupForm, "submit", () => {
            track("challenge_signup_submitted", { component: "signup", outcome: "submitted" });
        }, true);

        document.querySelectorAll("[name='lmxSignupIdentity']").forEach(input => {
            listen(input, "change", () => {
                track("challenge_identity_selected", {
                    component: "signup",
                    step: "identity",
                    outcome: "selected",
                    metadata: { identityMode: input.value === "athlete" ? "athlete" : "participant" }
                });
            }, { passive: true });
        });

        listen(document, "input", event => {
            const target = event.target;
            if (!target || (target.id !== "lmxPledgeCommitmentAmount" && target.id !== "lmxBlockedCommitmentAmount" && target.id !== "lmxEditCommitmentAmount")) return;
            const component = target.id === "lmxEditCommitmentAmount" ? "profile" : "commitment";
            trackOnce(`challenge-pledge-touched-${component}`, "challenge_pledge_touched", { component, step: "pledge", outcome: "touched" });
            if (target.value) {
                track("challenge_pledge_completed", {
                    component,
                    step: "pledge",
                    outcome: "completed",
                    metadata: { pledgeBucket: amountBucket(target.value) }
                });
            }
        }, { passive: true });

        listen(document, "invalid", event => {
            const target = event.target;
            if (!target || (target.id !== "lmxPledgeCommitmentAmount" && target.id !== "lmxBlockedCommitmentAmount" && target.id !== "lmxEditCommitmentAmount")) return;
            track("challenge_pledge_validation_failed", {
                component: target.id === "lmxEditCommitmentAmount" ? "profile" : "commitment",
                step: safeFieldKey(target),
                outcome: "failed",
                errorCode: "browser_validation"
            });
        }, true);

        listen(document.getElementById("lmxSignupAthlete"), "input", () => {
            trackOnce("challenge-athlete-search-started", "challenge_athlete_search_started", { component: "signup", step: "athlete_search", outcome: "started" });
        }, { passive: true });
        listen(document, "mousedown", event => {
            const option = event.target && event.target.closest ? event.target.closest("#lmxSignupAthlete-autocomplete-list .lmx-athlete-option") : null;
            if (!option) return;
            track("challenge_athlete_search_result_selected", {
                component: "signup",
                step: "athlete_search",
                outcome: "selected"
            });
        }, true);
        listen(document.getElementById("lmxSignupTimeZoneButton"), "click", () => {
            track("challenge_timezone_picker_opened", { component: "signup", step: "timezone", outcome: "opened" });
        }, { passive: true });
        listen(document.getElementById("lmxSignupTimeZoneSearch"), "input", () => {
            trackOnce("challenge-timezone-searched", "challenge_timezone_searched", { component: "signup", step: "timezone", outcome: "searched" });
        }, { passive: true });
        listen(document.getElementById("lmxSignupTimeZone"), "change", () => {
            track("challenge_timezone_selected", { component: "signup", step: "timezone", outcome: "selected" });
        }, { passive: true });

        listen(document, "submit", event => {
            const form = event.target;
            if (!(form instanceof HTMLFormElement) || !form.classList.contains("lmx-checkin-card")) return;
            const day = Number(form.dataset.day || form.querySelector("[name='challengeDay']")?.value);
            const practice = !!form.querySelector(".lmx-practice-note");
            lastCheckInKind = practice ? "practice" : "scored";
            track(practice ? "challenge_practice_checkin_started" : "challenge_scored_checkin_started", {
                component: "checkin",
                step: Number.isFinite(day) ? `day_${day}` : "day_unknown",
                outcome: "started",
                metadata: { checkinKind: practice ? "practice" : "scored" }
            });
        }, true);

        const commitmentPanel = document.getElementById("lmxCommitmentPanel");
        if (commitmentPanel && typeof MutationObserver === "function") {
            const trackCommitmentBlock = () => {
                if (commitmentPanel.classList.contains("lmx-hidden")) return;
                const card = commitmentPanel.querySelector(".lmx-commitment-card");
                if (!card) return;
                trackOnce("challenge-commitment-block-seen", "challenge_commitment_block_seen", {
                    component: "commitment",
                    step: "block",
                    outcome: "viewed",
                    metadata: { commitmentState: card.classList.contains("setup") ? "needs_amount" : "due" }
                });
            };
            const observer = new MutationObserver(trackCommitmentBlock);
            observer.observe(commitmentPanel, { childList: true, subtree: true, attributes: true, attributeFilter: ["class"] });
            trackCommitmentBlock();
        }
    }

    function setupPublicContentTracking() {
        listen(document, "click", event => {
            if (event.defaultPrevented) return;
            if (event.button !== undefined && event.button !== 0) return;
            if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

            const target = event.target;
            if (!target || !target.closest) return;

            const homepageRoot = target.closest("#events-root-index");
            if (homepageRoot) {
                const rowOrLink = target.closest("tr.main-row, a[href], button");
                if (!rowOrLink) return;
                track("homepage_highlight_clicked", {
                    component: "homepage",
                    step: "highlights",
                    outcome: "clicked",
                    metadata: eventBoardClickMetadata(target)
                });
                return;
            }

            const eventRoot = target.closest("#events-root, .event-board-page .events-board");
            if (eventRoot) {
                const link = target.closest("a[href]");
                if (!link) return;
                track("event_link_clicked", {
                    component: "event_board",
                    outcome: "clicked",
                    metadata: eventBoardClickMetadata(target)
                });
            }
        }, true);
    }

    function setupFetchTracking() {
        if (!originalFetch) return;

        window.fetch = function () {
            const args = withStatsSessionHeader(arguments);
            const url = safe(() => String(args[0] && args[0].url || args[0] || "")) || "";
            const started = now();
            const observed = safe(() => classifyFetch(url));
            let request;
            try {
                request = originalFetch.apply(this, args);
            } catch (error) {
                safe(() => {
                    if (observed) {
                        track(observed.failureEvent, {
                            component: observed.component,
                            step: observed.step,
                            outcome: "failed",
                            errorCode: error && error.name ? error.name : "fetch_failed",
                            durationMs: Math.round(now() - started)
                        });
                    }
                });
                throw error;
            }

            return Promise.resolve(request).then(response => {
                safe(() => {
                    if (!observed) return;
                    track(observed.successEvent(response), {
                        component: observed.component,
                        step: observed.step,
                        outcome: response.ok ? "succeeded" : "failed",
                        errorCode: response.ok ? null : `http_${response.status}`,
                        durationMs: Math.round(now() - started)
                    });
                });
                return response;
            }, error => {
                safe(() => {
                    if (!observed) return;
                    track(observed.failureEvent, {
                        component: observed.component,
                        step: observed.step,
                        outcome: "failed",
                        errorCode: error && error.name ? error.name : "network_failed",
                        durationMs: Math.round(now() - started)
                    });
                });
                throw error;
            });
        };
    }

    function classifyFetch(url) {
        if (!url || url.includes("/api/site-statistics/event")) return null;
        if (url.includes("/api/data/hypothetical-rank")) {
            track("rank_preview_requested", { component: "rank_preview", outcome: "requested" });
            return {
                component: "rank_preview",
                step: "hypothetical_rank",
                successEvent: response => response.ok ? "rank_preview_rendered" : "rank_preview_failed",
                failureEvent: "rank_preview_failed"
            };
        }
        if (url.includes("/api/application/application")) {
            return {
                component: "application",
                step: "submit",
                successEvent: response => response.ok ? null : "api_request_failed",
                failureEvent: "api_request_failed"
            };
        }
        if (url.includes("/api/application/payment-status")) {
            return {
                component: "application_review",
                step: "payment_status",
                successEvent: response => response.ok ? "payment_status_checked" : "payment_status_failed",
                failureEvent: "payment_status_failed"
            };
        }
        if (url.includes("/api/longevitymaxxing/state")) {
            return {
                component: "challenge",
                step: "public_state",
                successEvent: response => response.ok ? "challenge_public_state_loaded" : "api_request_failed",
                failureEvent: "api_request_failed"
            };
        }
        if (url.includes("/api/longevitymaxxing/signup")) {
            return {
                component: "signup",
                step: "submit",
                successEvent: response => response.ok ? null : "api_request_failed",
                failureEvent: "api_request_failed"
            };
        }
        if (url.includes("/api/longevitymaxxing/participant")) {
            return {
                component: "challenge",
                step: "participant_state",
                successEvent: response => response.ok ? "challenge_participant_state_loaded" : "challenge_participant_state_failed",
                failureEvent: "challenge_participant_state_failed"
            };
        }
        if (url.includes("/api/longevitymaxxing/check-in")) {
            return {
                component: "checkin",
                step: "submit",
                successEvent: response => response.ok ? null : "api_request_failed",
                failureEvent: "api_request_failed"
            };
        }
        if (url.includes("/api/longevitymaxxing/commitment-payment/status")) {
            return {
                component: "commitment",
                step: "status",
                successEvent: response => response.ok ? null : "api_request_failed",
                failureEvent: "api_request_failed"
            };
        }
        if (url.includes("/api/longevitymaxxing/commitment-payment")) {
            return {
                component: "commitment",
                step: "payment",
                successEvent: response => response.ok ? null : "api_request_failed",
                failureEvent: "api_request_failed"
            };
        }
        return null;
    }

    window.LwcSiteStats = {
        track,
        amountBucket,
        fileSizeBucket,
        fileTypeBucket,
        countBucket
    };

    setupFetchTracking();
    setupSpaRouteTracking();
    listen(document, "DOMContentLoaded", () => {
        safe(setupPageViews);
        safe(setupJoinGameTracking);
        safe(setupCalculatorTracking);
        safe(setupProofTracking);
        safe(setupChallengeTracking);
        safe(setupPublicContentTracking);
    });
    listen(window, "error", event => {
        const message = event && event.message ? String(event.message) : "";
        if (isIgnoredClientErrorMessage(message)) return;
        track("client_error_observed", {
            component: "client",
            outcome: "failed",
            errorCode: message ? message.slice(0, 80) : "error"
        });
    });
})();
