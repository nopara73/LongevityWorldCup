/* badges.js
   Server-driven badge rendering.
   We no longer compute competitive badges on the client; we only render what the server embedded
   into athlete.Badges. Local/novelty badges (host, podcast, pregnancy, first applicants, etc.)
   remain client-only visuals by design.
*/

/* =========================
   Shared UI helpers
   ========================= */
const spanA11y = 'role="button" tabindex="0" aria-label="Open league"';

/* =========================
   LEGACY LOOK & FEEL (exact gradients + borders)
   ========================= */
const LEGACY_BG = {
    // same order and appearance as the old file
    default: "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;",
    medal: [
        "background: linear-gradient(135deg, #ffd700, #8b8000); border: 2px solid #8a6f00;", // Gold
        "background: linear-gradient(135deg, #c0c0c0, #696969); border: 2px solid #6e6e6e;", // Silver
        "background: linear-gradient(135deg, #cd7f32, #5c4033); border: 2px solid #6b3519;"  // Bronze
    ],
    // thematic backgrounds (identical to legacy)
    liver:        "background: linear-gradient(135deg, #aa336a, #6e0f3c); border: 2px solid #4a0b27;",
    kidney:       "background: linear-gradient(135deg, #128fa1, #0e4d64); border: 2px solid #082c3a;",
    metabolic:    "background: linear-gradient(135deg, #ff9800, #9c5700); border: 2px solid #5c3200;",
    inflammation: "background: linear-gradient(135deg, #b71c1c, #7f0000); border: 2px solid #4a0000;",
    immune:       "background: linear-gradient(135deg, #43a047, #1b5e20); border: 2px solid #0d3a12;",
    personal:     "background: linear-gradient(135deg, #00bcd4, #006e7a); border: 2px solid #004f56;",
    black:        "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;" // used for podcast/submission
};

// Base icons keyed by exact server labels (supports server naming)
const BASE_ICONS = {
    'Age Reduction': 'fa-bolt',
    'Chronological Age – Oldest': 'fa-infinity',
    'Chronological Age – Youngest': 'fa-baby',
    'PhenoAge – Lowest': 'fa-feather',
    'Most Submissions': 'fa-skull-crossbones',
    '≥2 Submissions': 'fa-calendar-check',
    'Crowd – Most Guessed': 'fa-users',
    'Crowd – Age Gap (Chrono−Crowd)': 'fa-user-ninja',
    'Crowd – Lowest Crowd Age': 'fa-baby',
    'PhenoAge Best Improvement': 'fa-clock',
    // Domain winners (match backend labels)
    'Best Domain – Liver': 'fa-droplet',
    'Best Domain – Kidney': 'fa-toilet',
    'Best Domain – Metabolic': 'fa-fire',
    'Best Domain – Inflammation': 'fa-temperature-three-quarters',
    'Best Domain – Immune': 'fa-virus'
};

// Helpers to read both PascalCase (server) and camelCase (legacy FE) fields
function getLabel(b){ return b.Label || b.BadgeLabel || ''; }
function getCat(b){ return (b.LeagueCategory || '').toString(); }
function getVal(b){ return b.LeagueValue || ''; }
function getPlace(b){ return typeof b.Place === 'number' ? b.Place : null; }

// Legacy tooltip builders (frontend-owned, not from server)
function makeTooltipFromServerBadge(b, athlete) {
    const label = getLabel(b);
    const cat = getCat(b).toLowerCase();
    const val = getVal(b);
    const place = getPlace(b);

    // Age Reduction family (global/division/generation/exclusive)
    if (label === 'Age Reduction') {
        if (cat === 'global') {
            if (place === 1) return 'Ultimate Lifeform: #1 in the Ultimate League';
            if (place === 2) return 'Near Immortal: #2 in the Ultimate League';
            if (place === 3) return 'Third and Threatening: #3 in the Ultimate League';
            return 'Age Reduction';
        }
        if (cat === 'division') {
            if (place === 1) {
                const v = val.toLowerCase();
                if (v === "men's") return "The Alpha Male: #1 in Men's League";
                if (v === "women's") return "The Empress: #1 in Women's League";
                if (v === "open") return 'The Machine: #1 in Open League';
            }
            return `#${place} in ${val} League`;
        }
        if (cat === 'generation') {
            if (place === 1) {
                const g = val.toLowerCase();
                if (g === 'silent generation') return 'The Grandmaster: #1 in Silent Generation League';
                if (g === 'baby boomers')      return 'The Iron Throne: #1 in Baby Boomers League';
                if (g === 'gen x')             return 'The Last Ronin: #1 in Gen X League';
                if (g === 'millennials')       return 'The Chosen One: #1 in Millennials League';
                if (g === 'gen z')             return 'The Meme Lord: #1 in Gen Z League';
                if (g === 'gen alpha')         return 'The Singularity: #1 in Gen Alpha League';
            }
            return `#${place} in ${val} League`;
        }
        if (cat === 'exclusive') {
            return `#${place} in ${val} League`;
        }
    }

    // Chronological Age – Oldest
    if (label === 'Chronological Age – Oldest' && place) {
        const age = (athlete.chronologicalAge ?? athlete.ChronoAge ?? athlete.chronological_age);
        const ageText = isFinite(age) ? Number(age).toFixed(1) : '';
        if (place === 1) return `Yoda: Chronologically Oldest (Age: ${ageText} years)`;
        if (place === 2) return `Master Roshi: Chronologically 2nd Oldest (Age: ${ageText})`;
        if (place === 3) return `Mr. Miyagi: Chronologically 3rd Oldest (Age: ${ageText})`;
    }

    // Chronological Age – Youngest
    if (label === 'Chronological Age – Youngest' && place) {
        const age = (athlete.chronologicalAge ?? athlete.ChronoAge ?? athlete.chronological_age);
        const ageText = isFinite(age) ? Number(age).toFixed(1) : '';
        if (place === 1) return `Son Goten: Chronologically Youngest (Age: ${ageText} years)`;
        if (place === 2) return `Son Gohan: Chronologically 2nd Youngest (Age: ${ageText})`;
        if (place === 3) return `Son Goku: Chronologically 3rd Youngest (Age: ${ageText})`;
    }

    // PhenoAge – Lowest
    if (label === 'PhenoAge – Lowest' && place) {
        const ph = (athlete.lowestPhenoAge ?? athlete.LowestPhenoAge);
        const phText = isFinite(ph) ? Number(ph).toFixed(1) : '';
        if (place === 1) return `Peter Pan: Biologically Youngest (Pheno Age: ${phText} years)`;
        if (place === 2) return `Dorian Gray: Biologically 2nd Youngest (Pheno Age: ${phText})`;
        if (place === 3) return `Benjamin Button: Biologically 3rd Youngest (Pheno Age: ${phText})`;
    }

    // Submissions
    if (label === 'Most Submissions') {
        const c = (athlete.submissionCount ?? athlete.SubmissionCount ?? 0);
        return `The Submittinator: Most Tests Submitted: ${c}`;
    }
    if (label === '≥2 Submissions') {
        return 'The Regular: Two or More Tests Submitted';
    }

    // Crowd badges (need crowdCount / crowdAge from athlete)
    if (label === 'Crowd – Most Guessed' && place) {
        const count = (athlete.crowdCount ?? athlete.CrowdCount ?? 0);
        if (place === 1) return `Popular AF: Most Age Guesses Received (${count})`;
        if (place === 2) return `Pretty Damn Popular: 2nd Most Age Guesses Received (${count})`;
        if (place === 3) return `Shockingly Popular: 3rd Most Age Guesses Received (${count})`;
    }
    if (label === 'Crowd – Age Gap (Chrono−Crowd)' && place) {
        const ch = (athlete.chronologicalAge ?? athlete.ChronoAge ?? 0);
        const cr = (athlete.crowdAge ?? athlete.CrowdAge ?? 0);
        const gap = Math.abs(Number(ch) - Number(cr));
        const gapText = isFinite(gap) ? gap.toFixed(1) : '';
        const yearWord = gapText === '1.0' ? 'year' : 'years';
        if (place === 1) return `Skin Trafficker: Perceived ${gapText} ${yearWord} younger`;
        if (place === 2) return `Wrinkle Launderer: Perceived ${gapText} ${yearWord} younger`;
        if (place === 3) return `Collagen Smuggler: Perceived ${gapText} ${yearWord} younger`;
    }
    if (label === 'Crowd – Lowest Crowd Age' && place) {
        const cr = (athlete.crowdAge ?? athlete.CrowdAge ?? 0);
        const ageText = isFinite(cr) ? Number(cr).toFixed(1) : '';
        const yearWord = ageText === '1.0' ? 'year' : 'years';
        if (place === 1) return `Baby Boss: Youngest Looking (Crowd Age: ${ageText} ${yearWord})`;
        if (place === 2) return `Lullaby Lord: 2nd Youngest Looking (Crowd Age: ${ageText} ${yearWord})`;
        if (place === 3) return `Diaper Don: 3rd Youngest Looking (Crowd Age: ${ageText} ${yearWord})`;
    }

    // PhenoAge Best Improvement (a.k.a. Redemption Arc)
    if (label === 'PhenoAge Best Improvement') {
        // Server doesn’t push the delta value; show generic legacy label
        return 'Redemption Arc: Greatest Age Reversal from First Submission (Baseline)';
    }

    // Domain winners: rebuild legacy informative tooltips if BestMarkerValues present
    if (label.startsWith('Best Domain')) {
        const best = athlete.bestBiomarkerValues || athlete.BestMarkerValues;
        if (Array.isArray(best) && best.length >= 10) {
            // [0]=AgeAtTest, [1]=Alb g/L, [2]=Creat umol/L, [3]=Glu mmol/L, [4]=ln(CRP/10), [5]=WBC,
            // [6]=Lymph %, [7]=MCV fL, [8]=RDW %, [9]=ALP U/L
            const alb = Number(best[1]).toFixed(1);
            const creat = Number(best[2]).toFixed(1);
            const glu = Number(best[3]).toFixed(1);
            const crp = (Math.exp(Number(best[4])) * 10).toFixed(2);
            const wbc = Number(best[5]).toFixed(1);
            const lym = Number(best[6]).toFixed(1);
            const mcv = Number(best[7]).toFixed(1);
            const rdw = Number(best[8]).toFixed(1);
            const alp = Number(best[9]).toFixed(1);

            if (label === 'Best Domain – Liver')        return `Liver King: Top Liver Profile (Albumin ${alb} g/L, ALP ${alp} U/L)`;
            if (label === 'Best Domain – Kidney')       return `Kidney Overlord: Top Kidney Profile (Creatinine ${creat} µmol/L)`;
            if (label === 'Best Domain – Metabolic')    return `Glucose Gladiator: Top Metabolic Profile (Glucose ${glu} mmol/L)`;
            if (label === 'Best Domain – Inflammation') return `Inflammation Whisperer: Top Inflammation Profile (CRP ${crp} mg/L)`;
            if (label === 'Best Domain – Immune')       return `Pathogen Punisher: Top Immune Profile (WBC ${wbc} 10³ cells/µL, Lymphocyte ${lym}%, MCV ${mcv} fL, RDW ${rdw}%)`;
        }
    }

    // Fallback
    return place ? `${label}: #${place}` : `${label}`;
}

// Choose proper icon; override special cases
function pickIconForServerBadge(b) {
    const label = getLabel(b);
    const cat = getCat(b).toLowerCase();
    const place = getPlace(b);

    // Ultimate League (global Age Reduction): 1=crown, 2=medal, 3=award
    if (label === 'Age Reduction' && cat === 'global' && place) {
        if (place === 1) return 'fa-crown';
        if (place === 2) return 'fa-medal';
        if (place === 3) return 'fa-award';
    }

    // Division / Generation ikonok
    if (label === 'Age Reduction' && (cat === 'division' || cat === 'generation') && b.LeagueValue) {
        if (cat === 'division' && typeof window.TryGetDivisionFaIcon === 'function') {
            return window.TryGetDivisionFaIcon(b.LeagueValue) || (BASE_ICONS[label] || 'fa-award');
        }
        if (cat === 'generation' && typeof window.TryGetGenerationFaIcon === 'function') {
            return window.TryGetGenerationFaIcon(b.LeagueValue) || (BASE_ICONS[label] || 'fa-award');
        }
    }

    // Exclusive league ikon
    if (label === 'Age Reduction' && cat === 'exclusive') return 'fa-umbrella-beach';

    // LEGACY: rank-specific icons for these badges
    if (label === 'Chronological Age – Oldest' && place) {
        if (place === 1) return 'fa-infinity';
        if (place === 2) return 'fa-scroll';
        if (place === 3) return 'fa-leaf';
    }
    if (label === 'Chronological Age – Youngest' && place) {
        if (place === 1) return 'fa-baby';
        if (place === 2) return 'fa-child';
        if (place === 3) return 'fa-running';       // ← Son Goku
    }
    if (label === 'PhenoAge – Lowest' && place) {
        if (place === 1) return 'fa-feather';
        if (place === 2) return 'fa-portrait';      // ← Dorian Gray
        if (place === 3) return 'fa-hourglass-start';
    }

    return BASE_ICONS[label] || 'fa-award';
}

// Background to exactly mimic old palette
function pickBackgroundForServerBadge(b) {
    const label = getLabel(b);
    const place = getPlace(b);

    // LEGACY: these have always been black bubbles
    if (
        label === 'Chronological Age – Oldest' ||
        label === 'Chronological Age – Youngest' ||
        label === 'PhenoAge – Lowest'
    ) {
        return LEGACY_BG.default;
    }

    // Apply medal background only to real medal badges
    const medalLike =
        label === 'Age Reduction' ||
        label.startsWith('Crowd – ');

    if (medalLike && place && [1, 2, 3].includes(place)) {
        return LEGACY_BG.medal[place - 1];
    }

    if (label === 'Most Submissions' || label === '≥2 Submissions') return LEGACY_BG.black;

    if (label === 'Best Domain – Liver')        return LEGACY_BG.liver;
    if (label === 'Best Domain – Kidney')       return LEGACY_BG.kidney;
    if (label === 'Best Domain – Metabolic')    return LEGACY_BG.metabolic;
    if (label === 'Best Domain – Inflammation') return LEGACY_BG.inflammation;
    if (label === 'Best Domain – Immune')       return LEGACY_BG.immune;

    if (label === 'PhenoAge Best Improvement')  return LEGACY_BG.default;

    return LEGACY_BG.default;
}

// Optional link target (when clicking a medal/badge)
function pickClickUrl(b) {
    const label = getLabel(b);
    const cat = getCat(b).toLowerCase();
    const val = getVal(b);

    if (label === 'Age Reduction') {
        if (cat === 'global') return '/leaderboard/leaderboard.html';
        if ((cat === 'division' || cat === 'generation' || cat === 'exclusive') && val) {
            const slug = window.slugifyName ? window.slugifyName(val, true) : null;
            return slug ? `/league/${slug}` : null;
        }
    }
    return null;
}

/// Order: personal link (0) → podcast (1.10) → neutrals (1.2x) → medals (2/3/4 + micro)
// This matches the legacy visual order exactly.
function computeOrder(b) {
    const label = getLabel(b);
    const cat = getCat(b).toLowerCase();
    const place = getPlace(b);

    // Medal family only: Age Reduction + Crowd
    const isMedalFamily =
        label === 'Age Reduction' ||
        label.startsWith('Crowd – ');

    if (isMedalFamily && place && [1, 2, 3].includes(place)) {
        const base = place === 1 ? 2 : (place === 2 ? 3 : 4);
        let micro = 0;

        if (label === 'Age Reduction') {
            if (cat === 'global')      micro = 0.01;
            else if (cat === 'division')   micro = 0.02;
            else if (cat === 'generation') micro = 0.03;
            else if (cat === 'exclusive')  micro = 0.04;
        } else if (label === 'Crowd – Most Guessed')             micro = 0.20;
        else if (label === 'Crowd – Age Gap (Chrono−Crowd)')   micro = 0.21;
        else if (label === 'Crowd – Lowest Crowd Age')         micro = 0.22;

        return base + micro;
    }

    // LEGACY: within the neutral block, these come after the podcast
    if (label === 'Chronological Age – Oldest')   return 1.12;
    if (label === 'Chronological Age – Youngest') return 1.13; // ← Son Goku goes here
    if (label === 'PhenoAge – Lowest')            return 1.14; // ← Dorian Gray goes here

    // Neutrals
    if (label === 'Most Submissions') return 1.20;
    if (label === '≥2 Submissions')   return 1.21;

    if (label === 'Best Domain – Liver')        return 1.30;
    if (label === 'Best Domain – Kidney')       return 1.31;
    if (label === 'Best Domain – Metabolic')    return 1.32;
    if (label === 'Best Domain – Inflammation') return 1.33;
    if (label === 'Best Domain – Immune')       return 1.34;

    if (label === 'PhenoAge Best Improvement')  return 1.40;

    return 1.50;
}

// Build one server badge bubble HTML
function buildServerBadgeHtml(b, athlete) {
    const icon = pickIconForServerBadge(b);
    const tooltip = makeTooltipFromServerBadge(b, athlete);
    const style = pickBackgroundForServerBadge(b);
    const url = pickClickUrl(b);
    const order = computeOrder(b);

    const clickableAttrs = url
        ? `class="badge-class badge-clickable" ${spanA11y} onclick="window.location.href='${url}';"`
        : `class="badge-class"`;

    return {
        order,
        html: `<span ${clickableAttrs} title="${tooltip}" style="${style}"><i class="fa ${icon}"></i></span>`
    };
}

/* =========================
   Public API: window.setBadges
   ========================= */
window.setBadges = function (athlete, athleteCell /* table row wrapper (or modal shim) */) {
    let badgeContainer = null;
    if (athleteCell && athleteCell.classList && athleteCell.classList.contains('badge-section')) {
        badgeContainer = athleteCell;
    } else if (athleteCell && typeof athleteCell.querySelector === 'function') {
        badgeContainer = athleteCell.querySelector('.badge-section') || athleteCell.querySelector('.badges');
    }
    if (!badgeContainer) return;

    const items = [];

    // 0) --- PERSONAL LINK (legacy teal bubble, clickable) ---
    const personalLink = athlete.personalLink || athlete.PersonalLink;
    if (personalLink) {
        const href = personalLink.startsWith('http') ? personalLink : `https://${personalLink}`;
        items.push({
            order: 0,
            html: `<a class="badge-class badge-clickable" ${spanA11y} href="${href}" target="_blank" rel="noopener"
               title="Personal Page" style="${LEGACY_BG.personal}">
               <i class="fa fa-link"></i>
             </a>`
        });
    }

    // 1) --- SERVER-DRIVEN BADGES ---
    // Expecting athlete.Badges = [{ BadgeLabel/Label, LeagueCategory, LeagueValue, Place }, ...]
    const serverBadges =
        Array.isArray(athlete.Badges) ? athlete.Badges :
            Array.isArray(athlete.badges) ? athlete.badges : [];

    serverBadges.forEach(b => items.push(buildServerBadgeHtml(b, athlete)));

    // 2) --- CLIENT-ONLY BADGES (novelty) ---
    // Podcast (legacy black bubble, clickable)
    const podcastLink = athlete.podcastLink || athlete.PodcastLink;
    if (podcastLink) {
        items.push({
            order: 1.10, // ensure podcast comes before other 1.2x neutrals (matches old order)
            html: `<a class="badge-class badge-clickable badge-podcast" ${spanA11y}
            href="${podcastLink.startsWith('http') ? podcastLink : 'https://' + podcastLink}"
            target="_blank" rel="noopener" title="Podcast: hear to this athlete's story in depth" style="${LEGACY_BG.black}">
            <i class="fa fa-microphone"></i>
         </a>`
        });
    }

    // First Applicants (same as before)
    const firstApplicantsMapping = {
        "Alan V": 1, "Cody Hergenroeder": 2, "Spiderius": 3, "Jesse": 4, "Tone Vays": 5,
        "Stellar Madic": 6, "RichLee": 7, "ScottBrylow": 8, "Mind4u2cn": 9, "Dave Pascoe": 10
    };
    if (firstApplicantsMapping[athlete.name]) {
        const rank = firstApplicantsMapping[athlete.name];
        let tooltipText = "", iconClass = "";
        if (rank === 1)      { tooltipText = "Athlete Zero: 1st Athlete to Join the Longevity World Cup"; iconClass = "fa-circle-notch"; }
        else if (rank === 2) { tooltipText = "Athlete Beta: 2nd Athlete to Join the Longevity World Cup";  iconClass = "fa-star"; }
        else if (rank === 3) { tooltipText = "Athlete Gamma: 3rd Athlete to Join the Longevity World Cup"; iconClass = "fa-bolt"; }
        else                 { tooltipText = "Early Bird: Among the First 10 Athletes to Join the Longevity World Cup"; iconClass = "fa-dove"; }
        items.push({
            order: 1.19,
            html: `<span class="badge-class" title="${tooltipText}" style="${LEGACY_BG.default}">
               <i class="fa ${iconClass}"></i>
             </span>`
        });
    }

    // Pregnancy (legacy)
    const pregnancyMapping = ["Olga Vresca"];
    if (pregnancyMapping.includes(athlete.name)) {
        items.push({
            order: 1.191,
            html: `<span class="badge-class" title="Baby on Board! Delivering in 2025" style="${LEGACY_BG.default}">
               <i class="fa fa-baby-carriage"></i>
             </span>`
        });
    }

    // Host (legacy)
    if (athlete.name === "nopara73") {
        items.push({
            order: 1.192,
            html: `<span class="badge-class" title="Host: Organizer of the Longevity World Cup" style="${LEGACY_BG.default}">
               <i class="fa fa-house"></i>
             </span>`
        });
    }

    // Perfect Application (legacy)
    if (athlete.name === "Cornee") {
        items.push({
            order: 1.193,
            html: `<span class="badge-class" title="Perfect Application: Most Flawless Entry Form Ever Submitted" style="${LEGACY_BG.default}">
               <i class="fa fa-ruler"></i>
             </span>`
        });
    }

    // 3) --- RENDER to DOM (ordered: link/podcast → medals → neutrals) ---
    items.sort((a, b) => a.order - b.order);
    badgeContainer.innerHTML = items.map(x => x.html).join('');

    try {
        const isModalStrip =
            (badgeContainer.id && badgeContainer.id === 'modalBadgeStrip') ||
            (typeof badgeContainer.closest === 'function' && !!badgeContainer.closest('#detailsModal'));

        if (!isModalStrip) {
            Array.from(badgeContainer.children).forEach((badge, i) => {
                badge.style.animation = `pop .55s ease-out both ${i * 0.2}s`;
                badge.addEventListener('animationend', function handler() {
                    badge.style.animation = '';
                    badge.removeEventListener('animationend', handler);
                });
            });
        }
    } catch { /* no-op */ }
};

// keep a no-op for any legacy calls
window.computeBadges = window.computeBadges || function () {};

/* =========================
   LEGACY (disabled): client-side computation of competitive badges
   =========================
   Historically, we computed league ranks, biomarker MVPs, and crowd medals here on the client.
   That code is now superseded by the backend (BadgeDataService → AthleteDataService embedding),
   because we need authoritative state, ties, and event generation on the server.
   Keeping this section documented for posterity; implementation removed on purpose.
*/
