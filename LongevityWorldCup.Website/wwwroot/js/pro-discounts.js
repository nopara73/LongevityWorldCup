const PRO_BASE_PRICE_USD = 100;
const PERFECT_GUESS_KEY = "gmaHasPerfectGuess";
const PERFECT_GUESS_DISCOUNT = 10;
const BADGE_BG_DEFAULT = "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;";
const BADGE_BG_PERSONAL = "background: linear-gradient(135deg, #00bcd4, #006e7a); border: 2px solid #004f56;";
const BADGE_BG_BLACK = "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;";

function readBadgeLabel(badge) {
    return badge?.BadgeLabel || badge?.Label || "";
}

function readBadgeCategory(badge) {
    return badge?.LeagueCategory || badge?.Category || "";
}

function readBadgeLeagueValue(badge) {
    return badge?.LeagueValue ?? badge?.Value ?? null;
}

function readBadgePlace(badge) {
    const place = badge?.Place;
    return Number.isFinite(place) ? Number(place) : null;
}

function normalizeCategory(category) {
    return String(category || "").trim().toLowerCase();
}

function toTitleCategory(category) {
    const c = normalizeCategory(category);
    if (!c) return "Global";
    return c[0].toUpperCase() + c.slice(1);
}

function isSeasonBadgeLabel(label) {
    return /^S\d{2}$/i.test(String(label || "").trim());
}

function readServerBadges(athlete) {
    if (!athlete) return [];
    if (Array.isArray(athlete.Badges)) return athlete.Badges;
    if (Array.isArray(athlete.badges)) return athlete.badges;
    return [];
}

function hasPersonalLink(athlete) {
    if (!athlete) return false;
    const link = athlete.PersonalLink ?? athlete.personalLink;
    return Boolean(String(link || "").trim());
}

function getPersonalLinkUrl(athlete) {
    const link = athlete?.PersonalLink ?? athlete?.personalLink ?? "";
    const raw = String(link || "").trim();
    if (!raw) return null;
    return raw.startsWith("http") ? raw : `https://${raw}`;
}

function hasPerfectGuessMarker() {
    try {
        if (localStorage.getItem(PERFECT_GUESS_KEY) === "1") return true;
        const allGuesses = JSON.parse(localStorage.getItem("gmaAllGuesses") || "{}");
        const hasExact = Object.values(allGuesses).some(g => g && g.exact === true);
        if (hasExact) {
            localStorage.setItem(PERFECT_GUESS_KEY, "1");
            return true;
        }
        return false;
    } catch (_) {
        return false;
    }
}

function setPerfectGuessMarker() {
    try {
        localStorage.setItem(PERFECT_GUESS_KEY, "1");
    } catch (_) {
    }
}

function slugToDisplayName(slug) {
    const raw = String(slug || "").trim();
    if (!raw) return "";
    return raw
        .split(/[-_]+/)
        .filter(Boolean)
        .map(part => part.charAt(0).toUpperCase() + part.slice(1))
        .join(" ");
}

function getExactGuessAthleteNames(currentAthlete) {
    try {
        const allGuesses = JSON.parse(localStorage.getItem("gmaAllGuesses") || "{}");
        const exactSlugs = Object.entries(allGuesses)
            .filter(([_, guess]) => guess && guess.exact === true)
            .map(([slug]) => slug)
            .filter(Boolean);

        if (exactSlugs.length === 0) return [];

        const selectedName = String(currentAthlete?.Name || currentAthlete?.name || "").trim();
        const selectedSlug = String(currentAthlete?.AthleteSlug || currentAthlete?.athleteSlug || "").trim();
        const slugify = typeof window.slugifyName === "function" ? window.slugifyName : null;
        const normalizedSelectedSlug =
            selectedSlug || (slugify && selectedName ? slugify(selectedName, true) : "");

        return exactSlugs.map(slug => {
            if (
                selectedName &&
                normalizedSelectedSlug &&
                slugify &&
                slugify(slug, true) === slugify(normalizedSelectedSlug, true)
            ) {
                return selectedName;
            }
            return slugToDisplayName(slug);
        });
    } catch (_) {
        return [];
    }
}

function weightForAgeReduction(category, place) {
    if (![1, 2, 3].includes(place)) return 0;
    if (category === "amateur") return 100;
    if (category === "global") return [100, 50, 40][place - 1];
    if (category === "division") return [100, 50, 20][place - 1];
    if (category === "generation") return [100, 50, 20][place - 1];
    if (category === "exclusive") return [100, 50, 20][place - 1];
    return 0;
}

function weightForBadge(badge) {
    const label = readBadgeLabel(badge);
    const place = readBadgePlace(badge);
    const category = normalizeCategory(readBadgeCategory(badge));

    if (label === "Age Reduction") return weightForAgeReduction(category, place);

    if (label === "Chronological Age – Oldest") {
        if (place === 1) return 100;
        if (place === 2) return 50;
        if (place === 3) return 20;
        return 0;
    }
    if (label === "Chronological Age – Youngest") {
        if (place === 1 || place === 2 || place === 3) return 10;
        return 0;
    }
    if (label === "PhenoAge – Lowest") {
        if (place === 1) return 100;
        if (place === 2) return 50;
        if (place === 3) return 20;
        return 0;
    }
    if (label === "Bortz Age – Lowest") {
        if (place === 1) return 100;
        if (place === 2) return 50;
        if (place === 3) return 20;
        return 0;
    }
    if (label === "≥2 Submissions") return 10;
    if (label === "Most Submissions") return 20;
    if (label === "PhenoAge Best Improvement") return 100;
    if (label === "Bortz Age Best Improvement") return 100;

    if (
        label === "Best Domain – Liver" ||
        label === "Best Domain – Kidney" ||
        label === "Best Domain – Metabolic" ||
        label === "Best Domain – Inflammation" ||
        label === "Best Domain – Immune" ||
        label === "Best Domain – Vitamin D"
    ) {
        return 20;
    }

    if (label === "Crowd – Most Guessed") {
        if (place === 1) return 100;
        if (place === 2) return 90;
        if (place === 3) return 80;
        return 0;
    }
    if (label === "Crowd – Age Gap (Chrono−Crowd)") {
        if (place === 1) return 30;
        if (place === 2) return 20;
        if (place === 3) return 10;
        return 0;
    }
    if (label === "Crowd – Lowest Crowd Age") {
        if (place === 1) return 30;
        if (place === 2) return 20;
        if (place === 3) return 10;
        return 0;
    }

    if (label === "Podcast") return 100;
    if (label === "First Applicants") {
        if (place === 1) return 100;
        if (place === 2) return 90;
        if (place === 3) return 80;
        if (place >= 4 && place <= 10) return 70;
        return 0;
    }
    if (label === "Pregnancy") return 10;
    if (label === "Host") return 100;
    if (label === "Perfect Application") return 10;

    if (isSeasonBadgeLabel(label)) {
        if (place === 1) return 100;
        if (place === 2) return 90;
        if (place === 3) return 80;
        if (place >= 4 && place <= 10) return 70;
        if (place >= 11 && place <= 20) return 60;
        return 0;
    }

    return 0;
}

function badgeComponentLabel(badge) {
    const label = readBadgeLabel(badge);
    const place = readBadgePlace(badge);
    const category = toTitleCategory(readBadgeCategory(badge));
    if (place) return `${label} (${category} #${place})`;
    return `${label} (${category})`;
}

function toMoney(value) {
    const rounded = Math.round(value * 100) / 100;
    const asFixed = rounded.toFixed(2);
    return asFixed.endsWith(".00") ? asFixed.slice(0, -3) : asFixed;
}

function buildDiscountBreakdown(athlete, options = {}) {
    const components = [];

    if (options.isOnLeaderboard !== false) {
        components.push({ label: "Leaderboard", percent: 10, isBadge: false, kind: "leaderboard" });
    }

    if (hasPersonalLink(athlete)) {
        components.push({
            label: "Personal page link",
            percent: 10,
            isBadge: true,
            kind: "personalLink",
            athlete
        });
    }

    if (hasPerfectGuessMarker()) {
        components.push({
            label: "discount for guessing an athlete's age perfectly",
            percent: PERFECT_GUESS_DISCOUNT,
            isBadge: true,
            kind: "perfectGuess",
            athlete
        });
    }

    for (const badge of readServerBadges(athlete)) {
        const percent = weightForBadge(badge);
        if (percent > 0) {
            components.push({
                label: badgeComponentLabel(badge),
                percent,
                isBadge: true,
                kind: "serverBadge",
                badge,
                athlete
            });
        }
    }

    const rawDiscount = components.reduce((sum, c) => sum + c.percent, 0);
    const totalDiscount = Math.min(100, rawDiscount);
    const finalPriceUsd = PRO_BASE_PRICE_USD * (1 - totalDiscount / 100);
    const finalPriceText = finalPriceUsd <= 0 ? "free" : `$${toMoney(finalPriceUsd)}`;

    return {
        basePriceUsd: PRO_BASE_PRICE_USD,
        components,
        rawDiscount,
        totalDiscount,
        finalPriceUsd,
        finalPriceText
    };
}

function createBreakdownText(result) {
    const ordered = [...result.components].sort((a, b) => {
        if (Boolean(a.isBadge) !== Boolean(b.isBadge)) {
            return a.isBadge ? 1 : -1; // non-badge items first
        }
        if (a.percent !== b.percent) return a.percent - b.percent;
        return String(a.label).localeCompare(String(b.label));
    });
    const pieces = ordered.map(c => formatDiscountLine(c.percent, describeDiscountReason(c)));
    return pieces.length > 0 ? pieces.join("\n") : "0%";
}

function formatDiscountLine(percent, reason) {
    const raw = String(reason || "").trim();
    if (raw.toLowerCase().startsWith("while ")) {
        return `${percent}% discount ${raw}`;
    }
    return `${percent}% discount for ${raw}`;
}

function describeDiscountReason(component) {
    if (!component) return "eligible achievements";

    if (component.kind === "leaderboard") return "being on the leaderboard";
    if (component.kind === "personalLink") return "linking your personal page";
    if (component.kind === "perfectGuess") return "guessing an athlete's age perfectly";

    if (component.kind === "serverBadge" && component.badge) {
        const natural = describeServerBadgeReason(component.badge);
        if (natural) return natural;

        let badgeTitle = "";
        const tooltipBuilder = window.makeTooltipFromServerBadge;
        if (typeof tooltipBuilder === "function") {
            const tooltip = String(tooltipBuilder(component.badge, component.athlete) || "").trim();
            if (tooltip) {
                badgeTitle = extractBadgeTitle(tooltip);
            }
        }

        if (!badgeTitle) {
            const label = readBadgeLabel(component.badge);
            const place = readBadgePlace(component.badge);
            const category = toTitleCategory(readBadgeCategory(component.badge));
            badgeTitle = place ? `${label} #${place} (${category})` : `${label} (${category})`;
        }

        return `holding the ${badgeTitle} badge`;
    }

    return String(component.label || "eligible achievements");
}

function describeServerBadgeReason(badge) {
    const label = readBadgeLabel(badge);
    const place = readBadgePlace(badge);
    const category = toTitleCategory(readBadgeCategory(badge));
    const leagueValue = readBadgeLeagueValue(badge);

    if (isSeasonBadgeLabel(label)) {
        const seasonTag = `LWC${String(label).replace(/^S/i, "")}`;
        if (place === 1) return `winning ${seasonTag}`;
        if (place === 2) return `a 2nd-place finish in ${seasonTag}`;
        if (place === 3) return `a 3rd-place finish in ${seasonTag}`;
        if (place >= 4 && place <= 10) return `a top-10 finish in ${seasonTag}`;
        if (place >= 11 && place <= 20) return `a top-20 finish in ${seasonTag}`;
        return `a seasonal finish in ${seasonTag}`;
    }

    if (label === "First Applicants") {
        if (place === 1) return "being the first applicant to join";
        if (place === 2) return "being the second applicant to join";
        if (place === 3) return "being the third applicant to join";
        if (place >= 4 && place <= 10) return "being among the first 10 applicants";
        return "being an early applicant";
    }

    if (label === "Podcast") return "appearing on the Longevity World Cup podcast";
    if (label === "Host") return "hosting the Longevity World Cup";
    if (label === "Perfect Application") return "submitting a perfect application";
    if (label === "Most Submissions") return "having the most submissions";
    if (label === "≥2 Submissions") return "submitting at least two results";
    if (label === "PhenoAge Best Improvement") return "having the best PhenoAge improvement";
    if (label === "Bortz Age Best Improvement") return "having the best Bortz Age improvement";
    if (label === "Pregnancy") return "holding the Pregnancy badge";

    if (label.startsWith("Best Domain")) {
        const domain = label.replace("Best Domain – ", "").trim();
        return `having the best ${domain} domain profile`;
    }

    if (label === "Age Reduction" && place) {
        if (category === "Amateur") return `while #${place} in the Amateur League`;
        if (category === "Global") return `while #${place} in the Ultimate League`;
        if (category === "Division" && leagueValue) return `while #${place} in the ${leagueValue} League`;
        if (category === "Generation" && leagueValue) return `while #${place} in the ${leagueValue} League`;
        if (category === "Exclusive" && leagueValue) return `while #${place} in the ${leagueValue} League`;
        return `while #${place} in the ${category} League`;
    }

    if ((label === "Chronological Age – Youngest" || label === "Chronological Age – Oldest") && place) {
        const which = label.endsWith("Youngest") ? "youngest" : "oldest";
        return `while being the #${place} ${which} athlete`;
    }

    if ((label === "PhenoAge – Lowest" || label === "Bortz Age – Lowest") && place) {
        const which = label.startsWith("PhenoAge") ? "PhenoAge" : "Bortz Age";
        return `while being #${place} lowest by ${which}`;
    }

    if (label === "Crowd – Most Guessed" && place) {
        return `while being #${place} most-guessed by the crowd`;
    }
    if (label === "Crowd – Age Gap (Chrono−Crowd)" && place) {
        return `while being #${place} in age-gap vs the crowd`;
    }
    if (label === "Crowd – Lowest Crowd Age" && place) {
        return `while being #${place} youngest-looking to the crowd`;
    }

    return null;
}

function extractBadgeTitle(tooltip) {
    const raw = String(tooltip || "").trim();
    if (!raw) return "";
    const beforeColon = raw.includes(":") ? raw.slice(0, raw.indexOf(":")).trim() : raw;
    return beforeColon
        .replace(/\s*\([^)]*\)\s*$/g, "")
        .trim();
}

function escapeHtml(text) {
    return String(text)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");
}

function getDiscountIconClass(component) {
    if (!component || !component.isBadge) return null;
    if (component.kind === "personalLink") return "fa-link";
    if (component.kind === "perfectGuess") return "fa-bullseye";
    if (component.kind === "serverBadge") {
        const picker = window.pickIconForServerBadge;
        if (typeof picker === "function" && component.badge) {
            const icon = picker(component.badge);
            if (icon) return icon;
        }
        return "fa-award";
    }
    return "fa-award";
}

function getDiscountBadgeBackground(component) {
    if (!component || !component.isBadge) return BADGE_BG_DEFAULT;
    if (component.kind === "personalLink") return BADGE_BG_PERSONAL;
    if (component.kind === "perfectGuess") return BADGE_BG_BLACK;
    if (component.kind === "serverBadge") {
        const picker = window.pickBackgroundForServerBadge;
        if (typeof picker === "function" && component.badge) {
            const bg = picker(component.badge);
            if (bg) return bg;
        }
    }
    return BADGE_BG_DEFAULT;
}

function createDiscountBadgeChipHtml(component) {
    if (!component || !component.isBadge) return "";
    const icon = getDiscountIconClass(component) || "fa-award";
    const background = getDiscountBadgeBackground(component);
    const compact = "display:inline-flex;vertical-align:middle;align-items:center;justify-content:center;width:22px;height:22px;font-size:0.78rem;margin:0;";
    let tooltip = "";
    let clickUrl = null;

    if (component.kind === "personalLink") {
        tooltip = "Personal page";
        clickUrl = getPersonalLinkUrl(component.athlete);
    } else if (component.kind === "perfectGuess") {
        const names = getExactGuessAthleteNames(component.athlete);
        if (names.length === 1) {
            tooltip = `Bullseye: You guessed ${names[0]}'s age perfectly!`;
        } else if (names.length > 1) {
            tooltip = `Bullseye: You guessed these athletes' ages perfectly: ${names.join(", ")}`;
        } else {
            tooltip = "Bullseye: You guessed an athlete's age perfectly!";
        }
    } else if (component.kind === "serverBadge" && component.badge) {
        if (typeof window.makeTooltipFromServerBadge === "function") {
            tooltip = window.makeTooltipFromServerBadge(component.badge, component.athlete) || "";
        }
        if (typeof window.pickClickUrl === "function") {
            clickUrl = window.pickClickUrl(component.badge, component.athlete);
        }
    }

    const titleAttr = tooltip ? ` title="${escapeHtml(tooltip)}"` : "";
    const chipInner = `<i class="fa ${escapeHtml(icon)}"></i>`;
    if (clickUrl) {
        const personalLinkAttrs = component.kind === "personalLink"
            ? ` target="_blank" rel="noopener"`
            : "";
        return `<a class="badge-class badge-clickable" href="${escapeHtml(clickUrl)}"${personalLinkAttrs}${titleAttr} style="${escapeHtml(background)} ${compact}">${chipInner}</a>`;
    }
    return `<span class="badge-class"${titleAttr} aria-hidden="true" style="${escapeHtml(background)} ${compact}">${chipInner}</span>`;
}

function createBreakdownHtml(result) {
    const ordered = [...result.components].sort((a, b) => {
        if (Boolean(a.isBadge) !== Boolean(b.isBadge)) {
            return a.isBadge ? 1 : -1; // non-badge items first
        }
        if (a.percent !== b.percent) return a.percent - b.percent;
        return String(a.label).localeCompare(String(b.label));
    });
    if (ordered.length === 0) return "0%";

    return ordered.map(c => {
        const badgeChipHtml = createDiscountBadgeChipHtml(c);
        const badgeSlotHtml = `<span class="pro-discount-badge-slot">${badgeChipHtml}</span>`;
        const textHtml = `<span class="pro-discount-text">${escapeHtml(formatDiscountLine(c.percent, describeDiscountReason(c)))}</span>`;
        return `<span class="pro-discount-line">${badgeSlotHtml}${textHtml}</span>`;
    }).join("");
}

function createPriceHtml(result) {
    const oldText = `$${result.basePriceUsd}`;
    if (result.finalPriceUsd < result.basePriceUsd) {
        return `<span class="pro-old-price">${oldText}</span> <span class="pro-new-price">${result.finalPriceText}</span>`;
    }
    return `<span class="pro-new-price">${oldText}</span>`;
}

window.proDiscounts = {
    PERFECT_GUESS_KEY,
    setPerfectGuessMarker,
    weightForBadge,
    buildDiscountBreakdown,
    createBreakdownText,
    createBreakdownHtml,
    createPriceHtml
};
