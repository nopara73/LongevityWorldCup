(function () {
    "use strict";

    const endpoint = "/api/site-statistics/event";
    const sessionKey = "lwcSiteStatsSessionId";
    const originalFetch = window.fetch ? window.fetch.bind(window) : null;
    const pageStartedAt = now();
    const sentOnce = new Set();
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

    function source() {
        const host = referrerDomain();
        if (!host) return "direct";
        if (/google|bing|duckduckgo|yahoo|brave|search/i.test(host)) return "search";
        if (/x\.com|twitter|facebook|instagram|threads|youtube|linkedin|reddit|slack/i.test(host)) return "social";
        return "referral";
    }

    function track(eventName, options) {
        safe(() => {
            if (!eventName || !originalFetch) return;
            const payload = Object.assign({
                eventName,
                sessionId: getSessionId(),
                flow: flowFromPath(),
                route: route(),
                durationMs: Math.max(0, Math.round(now() - pageStartedAt)),
                deviceClass: deviceClass(),
                browserFamily: browserFamily(),
                referrerDomain: referrerDomain(),
                source: source()
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

    function fieldKey(el) {
        return (el && (el.id || el.name || el.getAttribute("aria-label"))) || "unknown";
    }

    function safeFieldKey(el) {
        const key = fieldKey(el);
        if (/email|name|token|note|why|media|link/i.test(key)) return "private_field";
        return key;
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

    function setupPageViews() {
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
            const hasContext = !!safe(() => sessionStorage.getItem("pendingPaymentInvoice") || localStorage.getItem("pendingPaymentInvoicePersistent"));
            track(hasContext ? "application_review_context_found" : "application_review_context_missing", {
                component: "application_review",
                outcome: hasContext ? "found" : "missing"
            });
            return;
        }

        if (path.includes("convergence") || path === "/apply" || path.includes("proof-upload") || path === "/proofs") {
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

        track("site_page_viewed", { component: "site", outcome: "viewed" });
    }

    function setupCalculatorTracking() {
        const form = document.getElementById("phenoAgeForm") || document.getElementById("bortzAgeForm");
        if (!form) return;

        form.addEventListener("submit", () => {
            track("calculator_started", { component: "calculator", outcome: "submitted" });
        }, true);

        form.addEventListener("invalid", event => {
            track("calculator_validation_failed", {
                component: "calculator",
                step: safeFieldKey(event.target),
                outcome: "failed",
                errorCode: "browser_validation"
            });
        }, true);

        const required = Array.from(form.querySelectorAll("input[required], select[required], textarea[required]"));
        required.forEach(el => {
            el.addEventListener("focus", () => {
                const key = safeFieldKey(el);
                if (touchedFields.has(key)) return;
                touchedFields.add(key);
                track("calculator_field_touched", { component: "calculator", step: key, outcome: "touched" });
            }, { passive: true });

            el.addEventListener("change", () => {
                const key = safeFieldKey(el);
                if (el.value && !completedFields.has(key)) {
                    completedFields.add(key);
                    track("calculator_field_completed", { component: "calculator", step: key, outcome: "completed" });
                }
                const pct = required.length ? Math.floor((completedFields.size / required.length) * 100) : 0;
                const threshold = pct >= 100 ? 100 : pct >= 75 ? 75 : pct >= 50 ? 50 : pct >= 25 ? 25 : 0;
                if (threshold > lastRequiredProgress) {
                    lastRequiredProgress = threshold;
                    track("calculator_required_progress_changed", {
                        component: "calculator",
                        step: "required_fields",
                        outcome: "progress",
                        metadata: { progressBucket: String(threshold) }
                    });
                    if (threshold === 100) {
                        track("calculator_all_required_fields_completed", { component: "calculator", outcome: "completed" });
                    }
                }
            }, { passive: true });
        });

        const result = document.getElementById("phenoAgeResult") || document.getElementById("bortzAgeResult");
        const rank = document.getElementById("phenoAgeRankPreview") || document.getElementById("bortzAgeRankPreview");
        const continueButton = document.getElementById("continueButton");
        if (continueButton) {
            continueButton.addEventListener("click", () => {
                track("calculator_continue_clicked", { component: "calculator", outcome: "clicked" });
                const paymentOffer = !!safe(() => sessionStorage.getItem("pendingPaymentOffer"));
                const biomarkerData = !!safe(() => sessionStorage.getItem("biomarkerData"));
                if (paymentOffer) track("payment_offer_stored", { component: "handoff", outcome: "stored" });
                if (biomarkerData) track("biomarker_handoff_stored", { component: "handoff", outcome: "stored" });
            }, { passive: true });
        }

        if (result) {
            const observer = new MutationObserver(() => {
                if (!result.classList.contains("show")) return;
                const yearsText = document.getElementById("yearsText");
                trackOnce(`result-${flowFromPath()}`, "calculator_result_generated", {
                    component: "calculator",
                    outcome: "succeeded",
                    metadata: { ageReductionBucket: ageReductionBucketFromText(yearsText && yearsText.textContent) }
                });
            });
            observer.observe(result, { attributes: true, attributeFilter: ["class"] });
        }

        if (rank) {
            const observer = new MutationObserver(() => {
                if (rank.hidden || !rank.textContent.trim()) return;
                trackOnce(`rank-${flowFromPath()}`, "rank_preview_rendered", {
                    component: "rank_preview",
                    outcome: "rendered"
                });
            });
            observer.observe(rank, { childList: true, subtree: true, attributes: true, attributeFilter: ["hidden", "aria-busy"] });
        }
    }

    function setupProofTracking() {
        ["proofPicInput", "proofCameraInput", "profilePicInput", "profileCameraInput"].forEach(id => {
            const input = document.getElementById(id);
            if (!input) return;
            input.addEventListener("change", () => {
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

        document.getElementById("uploadProofButton")?.addEventListener("click", () => {
            track("proof_upload_clicked", { component: "proof_upload", outcome: "clicked" });
        }, { passive: true });
        document.getElementById("takeProofPhotoButton")?.addEventListener("click", () => {
            track("proof_camera_clicked", { component: "proof_upload", outcome: "clicked" });
        }, { passive: true });
        document.getElementById("nextButton")?.addEventListener("click", () => {
            const applyDetailsVisible = document.getElementById("applyDetails")?.style.display !== "none";
            if (applyDetailsVisible) {
                track("application_submit_clicked", { component: "application", outcome: "clicked" });
            }
        }, { passive: true });
    }

    function setupJoinGameTracking() {
        const amateur = document.querySelector("[onclick*='startAmateurApplication']");
        const pro = document.querySelector("[onclick*='startProApplication']");
        amateur?.addEventListener("click", () => {
            track("onboarding_clock_selected", {
                flow: "pheno",
                component: "join_game",
                step: "amateur",
                outcome: "selected",
                metadata: { track: "amateur" }
            });
        }, { passive: true });
        pro?.addEventListener("click", () => {
            track("onboarding_clock_selected", {
                flow: "bortz",
                component: "join_game",
                step: "pro",
                outcome: "selected",
                metadata: { track: "pro" }
            });
        }, { passive: true });

        document.querySelectorAll(".biomarker-disclosure").forEach(details => {
            details.addEventListener("toggle", () => {
                if (!details.open) return;
                const isPro = !!details.closest(".track-card.pro");
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

        signupForm.addEventListener("input", event => {
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
            if (target.id === "lmxSignupCommitmentAmount") {
                trackOnce("challenge-pledge-touched", "challenge_pledge_touched", { component: "signup", step: "pledge", outcome: "touched" });
                if (target.value) {
                    track("challenge_pledge_completed", {
                        component: "signup",
                        step: "pledge",
                        outcome: "completed",
                        metadata: { pledgeBucket: amountBucket(target.value) }
                    });
                }
            }
        }, { passive: true });

        signupForm.addEventListener("invalid", event => {
            const key = safeFieldKey(event.target);
            track(key === "lmxSignupCommitmentAmount" ? "challenge_pledge_validation_failed" : "challenge_email_validation_failed", {
                component: "signup",
                step: key,
                outcome: "failed",
                errorCode: "browser_validation"
            });
        }, true);

        signupForm.addEventListener("submit", () => {
            track("challenge_signup_submitted", { component: "signup", outcome: "submitted" });
        }, true);

        document.querySelectorAll("[name='lmxSignupIdentity']").forEach(input => {
            input.addEventListener("change", () => {
                track("challenge_identity_selected", {
                    component: "signup",
                    step: "identity",
                    outcome: "selected",
                    metadata: { identityMode: input.value === "athlete" ? "athlete" : "participant" }
                });
            }, { passive: true });
        });

        document.getElementById("lmxSignupAthlete")?.addEventListener("input", () => {
            trackOnce("challenge-athlete-search-started", "challenge_athlete_search_started", { component: "signup", step: "athlete_search", outcome: "started" });
        }, { passive: true });
        document.getElementById("lmxSignupTimeZoneButton")?.addEventListener("click", () => {
            track("challenge_timezone_picker_opened", { component: "signup", step: "timezone", outcome: "opened" });
        }, { passive: true });
        document.getElementById("lmxSignupTimeZoneSearch")?.addEventListener("input", () => {
            trackOnce("challenge-timezone-searched", "challenge_timezone_searched", { component: "signup", step: "timezone", outcome: "searched" });
        }, { passive: true });
        document.getElementById("lmxSignupTimeZone")?.addEventListener("change", () => {
            track("challenge_timezone_selected", { component: "signup", step: "timezone", outcome: "selected" });
        }, { passive: true });

        document.addEventListener("submit", event => {
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
    }

    function setupFetchTracking() {
        if (!originalFetch) return;

        window.fetch = function () {
            const args = arguments;
            const url = String(args[0] && args[0].url || args[0] || "");
            const started = now();
            const observed = classifyFetch(url);
            return originalFetch.apply(this, args).then(response => {
                if (observed) {
                    track(observed.successEvent(response), {
                        component: observed.component,
                        step: observed.step,
                        outcome: response.ok ? "succeeded" : "failed",
                        errorCode: response.ok ? null : `http_${response.status}`,
                        durationMs: Math.round(now() - started)
                    });
                }
                return response;
            }, error => {
                if (observed) {
                    track(observed.failureEvent, {
                        component: observed.component,
                        step: observed.step,
                        outcome: "failed",
                        errorCode: error && error.name ? error.name : "network_failed",
                        durationMs: Math.round(now() - started)
                    });
                }
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
                successEvent: response => response.ok ? "application_submit_succeeded" : "application_submit_failed",
                failureEvent: "application_submit_failed"
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
                successEvent: response => response.ok ? "challenge_signup_succeeded" : "challenge_signup_failed",
                failureEvent: "challenge_signup_failed"
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
                step: "save",
                successEvent: response => response.ok
                    ? (lastCheckInKind === "practice" ? "challenge_practice_checkin_submitted" : "challenge_scored_checkin_submitted")
                    : (lastCheckInKind === "practice" ? "challenge_practice_checkin_failed" : "challenge_scored_checkin_failed"),
                failureEvent: lastCheckInKind === "practice" ? "challenge_practice_checkin_failed" : "challenge_scored_checkin_failed"
            };
        }
        if (url.includes("/api/longevitymaxxing/commitment-payment/status")) {
            return {
                component: "commitment",
                step: "status",
                successEvent: response => response.ok ? "challenge_commitment_payment_status_checked" : "api_request_failed",
                failureEvent: "api_request_failed"
            };
        }
        if (url.includes("/api/longevitymaxxing/commitment-payment")) {
            return {
                component: "commitment",
                step: "payment",
                successEvent: () => "challenge_commitment_payment_opened",
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
    document.addEventListener("DOMContentLoaded", () => {
        setupPageViews();
        setupJoinGameTracking();
        setupCalculatorTracking();
        setupProofTracking();
        setupChallengeTracking();
    });
    window.addEventListener("error", event => {
        track("client_error_observed", {
            component: "client",
            outcome: "failed",
            errorCode: event && event.message ? String(event.message).slice(0, 80) : "error"
        });
    });
})();
