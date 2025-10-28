﻿/* badges.js
   Server-driven badge rendering only.
   IMPORTANT:
   - Competitive + editorial badges are rendered ONLY if the server includes them in athlete.Badges.
   - No client-side fallbacks for Podcast / First Applicants / Pregnancy / Host / Perfect Application.
   - Perfect Guess remains local-only (user-specific, based on localStorage).
*/

/* =========================
   Shared UI helpers
   ========================= */
const spanA11y = 'role="button" tabindex="0" aria-label="Open league"';

/* =========================
   Legacy visuals (backgrounds)
   ========================= */
const LEGACY_BG = {
    default: "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;",
    medal: [
        "background: linear-gradient(135deg, #ffd700, #8b8000); border: 2px solid #8a6f00;", // Gold
        "background: linear-gradient(135deg, #c0c0c0, #696969); border: 2px solid #6e6e6e;", // Silver
        "background: linear-gradient(135deg, #cd7f32, #5c4033); border: 2px solid #6b3519;"  // Bronze
    ],
    liver:        "background: linear-gradient(135deg, #aa336a, #6e0f3c); border: 2px solid #4a0b27;",
    kidney:       "background: linear-gradient(135deg, #128fa1, #0e4d64); border: 2px solid #082c3a;",
    metabolic:    "background: linear-gradient(135deg, #ff9800, #9c5700); border: 2px solid #5c3200;",
    inflammation: "background: linear-gradient(135deg, #b71c1c, #7f0000); border: 2px solid #4a0000;",
    immune:       "background: linear-gradient(135deg, #43a047, #1b5e20); border: 2px solid #0d3a12;",
    personal:     "background: linear-gradient(135deg, #00bcd4, #006e7a); border: 2px solid #004f56;",
    black:        "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;"
};

// FontAwesome icon mapping (by server labels)
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
    'Best Domain – Liver': 'fa-droplet',
    'Best Domain – Kidney': 'fa-toilet',
    'Best Domain – Metabolic': 'fa-fire',
    'Best Domain – Inflammation': 'fa-temperature-three-quarters',
    'Best Domain – Immune': 'fa-virus',
    'Podcast': 'fa-microphone',
    'First Applicants': 'fa-dove',
    'Pregnancy': 'fa-baby-carriage',
    'Host': 'fa-house',
    'Perfect Application': 'fa-ruler'
};

// Normalized getters (support server PascalCase and possible legacy camelCase)
function getLabel(b) { return b.BadgeLabel || b.Label || ''; }
function getCat(b)   { return b.LeagueCategory || b.Category || 'Global'; }
function getVal(b)   { return b.LeagueValue ?? b.Value ?? null; }
function getPlace(b) { return (typeof b.Place === 'number') ? b.Place : null; }

// Choose proper icon; override special cases
function pickIconForServerBadge(b) {
    const label = getLabel(b);
    const cat = (getCat(b) || '').toLowerCase();
    const place = getPlace(b);
    const val = getVal(b);

    // Ultimate League (global Age Reduction): 1=crown, 2=medal, 3=award
    if (label === 'Age Reduction' && cat === 'global' && place) {
        if (place === 1) return 'fa-crown';
        if (place === 2) return 'fa-medal';
        if (place === 3) return 'fa-award';
    }

    // Division / Generation icons via FE helpers (if available) — use getVal(b)!
    if (label === 'Age Reduction' && (cat === 'division' || cat === 'generation') && val) {
        if (cat === 'division' && typeof window.TryGetDivisionFaIcon === 'function') {
            return window.TryGetDivisionFaIcon(val) || (BASE_ICONS[label] || 'fa-award');
        }
        if (cat === 'generation' && typeof window.TryGetGenerationFaIcon === 'function') {
            return window.TryGetGenerationFaIcon(val) || (BASE_ICONS[label] || 'fa-award');
        }
    }

    // Exclusive league icon
    if (label === 'Age Reduction' && cat === 'exclusive') return 'fa-umbrella-beach';

    // Rank-specific icons for these badges
    if (label === 'Chronological Age – Oldest' && place) {
        if (place === 1) return 'fa-infinity';
        if (place === 2) return 'fa-scroll';
        if (place === 3) return 'fa-leaf';
    }
    if (label === 'Chronological Age – Youngest' && place) {
        if (place === 1) return 'fa-baby';
        if (place === 2) return 'fa-child';
        if (place === 3) return 'fa-running';
    }
    if (label === 'PhenoAge – Lowest' && place) {
        if (place === 1) return 'fa-feather';
        if (place === 2) return 'fa-portrait';
        if (place === 3) return 'fa-hourglass-start';
    }

    // First Applicants — legacy icon mapping by place
    if (label === 'First Applicants') {
        if (place === 1) return 'fa-circle-notch'; // Athlete Zero
        if (place === 2) return 'fa-star';         // Athlete Beta
        if (place === 3) return 'fa-bolt';         // Athlete Gamma
        if (place && place >= 4 && place <= 10) return 'fa-dove'; // Early Bird
        return 'fa-dove';
    }

    return BASE_ICONS[label] || 'fa-award';
}

// Background to exactly mimic old palette
function pickBackgroundForServerBadge(b) {
    const label = getLabel(b);
    const place = getPlace(b);

    // Always neutral background for these
    if (
        label === 'Chronological Age – Oldest' ||
        label === 'Chronological Age – Youngest' ||
        label === 'PhenoAge – Lowest'
    ) {
        return LEGACY_BG.default;
    }

    // Medal backgrounds for real medal badges only
    const medalLike =
        label === 'Age Reduction' ||
        (typeof label === 'string' && label.startsWith('Crowd – '));

    if (medalLike && place && [1, 2, 3].includes(place)) {
        return LEGACY_BG.medal[place - 1];
    }

    if (label === 'Most Submissions' || label === '≥2 Submissions') return LEGACY_BG.black;
    if (label === 'Podcast') return LEGACY_BG.black;

    if (label === 'Best Domain – Liver')        return LEGACY_BG.liver;
    if (label === 'Best Domain – Kidney')       return LEGACY_BG.kidney;
    if (label === 'Best Domain – Metabolic')    return LEGACY_BG.metabolic;
    if (label === 'Best Domain – Inflammation') return LEGACY_BG.inflammation;
    if (label === 'Best Domain – Immune')       return LEGACY_BG.immune;

    if (label === 'PhenoAge Best Improvement')  return LEGACY_BG.default;

    return LEGACY_BG.default;
}

// Optional link target (when clicking a medal/badge)
function pickClickUrl(b, athlete) {
    const label = getLabel(b);
    const cat = (getCat(b) || '').toLowerCase();
    const val = getVal(b);

    if (label === 'Age Reduction') {
        if (cat === 'global') return '/leaderboard/leaderboard.html';
        if ((cat === 'division' || cat === 'generation' || cat === 'exclusive') && val) {
            const slug = (typeof window.slugifyName === 'function') ? window.slugifyName(String(val), true) : null;
            return slug ? `/league/${slug}` : null;
        }
    }
    if (label === 'Podcast') {
        const podcastLink = (athlete && (athlete.podcastLink || athlete.PodcastLink)) || null;
        if (!podcastLink) return null;
        return podcastLink.startsWith('http') ? podcastLink : ('https://' + podcastLink);
    }
    return null;
}

// Legacy tooltip builders (frontend-owned, not from server)
function makeTooltipFromServerBadge(b, athlete) {
    const label = getLabel(b);
    const place = getPlace(b);
    const cat = getCat(b);
    const val = getVal(b);

    if (label === 'Age Reduction') {
        if ((cat || '').toLowerCase() === 'global') {
            if (place === 1) return 'Ultimate Lifeform: #1 in the Ultimate League';
            if (place === 2) return 'Near Immortal: #2 in the Ultimate League';
            if (place === 3) return 'Third and Threatening: #3 in the Ultimate League';
            return 'Age Reduction';
        }
        if ((cat || '').toLowerCase() === 'division') {
            if (place === 1) {
                const v = String(val || '').toLowerCase();
                if (v === "men's") return "The Alpha Male: #1 in Men's League";
                if (v === "women's") return "The Empress: #1 in Women's League";
                if (v === "open") return 'The Machine: #1 in Open League';
            }
            return `#${place} in ${val} League`;
        }
        if ((cat || '').toLowerCase() === 'generation') return `#${place} in ${val}`;
        if ((cat || '').toLowerCase() === 'exclusive')  return `#${place} in ${val}`;
    }

    if (label === 'PhenoAge – Lowest' && place) {
        const ph =
            (athlete && (athlete.lowestPhenoAge ?? athlete.LowestPhenoAge ?? athlete.phenoAge)) != null
                ? Number(athlete.lowestPhenoAge ?? athlete.LowestPhenoAge ?? athlete.phenoAge).toFixed(1)
                : undefined;

        if (place === 1) return `Eternal Youth: Lowest PhenoAge${ph ? ` (${ph})` : ''}`;
        if (place === 2) return `Ageless Wonder: 2nd Lowest PhenoAge${ph ? ` (${ph})` : ''}`;
        if (place === 3) return `Timeless Spirit: 3rd Lowest PhenoAge${ph ? ` (${ph})` : ''}`;
    }

    if (label === 'Most Submissions') return 'Most submissions';
    if (label === '≥2 Submissions') return '≥ 2 submissions';

    if (typeof label === 'string' && label.startsWith('Crowd – ')) {
        if (label.endsWith('Most Guessed')) return 'Crowd favorite: most guessed';
        if (label.endsWith('Lowest Crowd Age')) return 'Lowest crowd-sourced age';
        if (label.includes('Age Gap')) return 'Largest gap between Chrono and Crowd';
    }

    if (typeof label === 'string' && label.startsWith('Best Domain – ')) {
        const raw = label.replace('Best Domain – ', '');
        return `Best ${raw} markers`;
    }

    // First Applicants — legacy tooltips by place
    if (label === 'First Applicants') {
        if (place === 1) return 'Athlete Zero: 1st Athlete to Join the Longevity World Cup';
        if (place === 2) return 'Athlete Beta: 2nd Athlete to Join the Longevity World Cup';
        if (place === 3) return 'Athlete Gamma: 3rd Athlete to Join the Longevity World Cup';
        if (place && place >= 4 && place <= 10) return 'Early Bird: Among the First 10 Athletes to Join the Longevity World Cup';
        return 'First Applicants';
    }

    if (label === 'Podcast') return 'Podcast: hear this athlete\'s story in depth';
    if (label === 'Pregnancy') return 'Baby on Board';
    if (label === 'Host') return 'Host: Organizer of the Longevity World Cup';
    if (label === 'Perfect Application') return 'Perfect Application';

    return place ? `${label}: #${place}` : `${label}`;
}

/* -------------------------------------------------
   ORDERING — match legacy: link → podcast → neutrals → medals
   ------------------------------------------------- */
function computeOrder(b) {
    const label = getLabel(b);
    const cat = (getCat(b) || '').toLowerCase();
    const place = getPlace(b);

    // Medal family only: Age Reduction + Crowd
    const isMedalFamily =
        label === 'Age Reduction' ||
        (typeof label === 'string' && label.startsWith('Crowd – '));

    if (isMedalFamily && place && [1, 2, 3].includes(place)) {
        const base = place === 1 ? 2 : (place === 2 ? 3 : 4);
        let micro = 0;

        if (label === 'Age Reduction') {
            if (cat === 'global')          micro = 0.01;
            else if (cat === 'division')   micro = 0.02;
            else if (cat === 'generation') micro = 0.03;
            else if (cat === 'exclusive')  micro = 0.04;
        } else if (label === 'Crowd – Most Guessed')           micro = 0.20;
        else if (label === 'Crowd – Age Gap (Chrono−Crowd)')   micro = 0.21;
        else if (label === 'Crowd – Lowest Crowd Age')         micro = 0.22;

        return base + micro;
    }

    // Neutrals (come after podcast but before medals)
    if (label === 'Podcast') return 1.10;

    if (label === 'Chronological Age – Oldest')   return 1.12;
    if (label === 'Chronological Age – Youngest') return 1.13;
    if (label === 'PhenoAge – Lowest')            return 1.14;

    if (label === 'Most Submissions') return 1.20;
    if (label === '≥2 Submissions')   return 1.21;

    if (label === 'Best Domain – Liver')        return 1.30;
    if (label === 'Best Domain – Kidney')       return 1.31;
    if (label === 'Best Domain – Metabolic')    return 1.32;
    if (label === 'Best Domain – Inflammation') return 1.33;
    if (label === 'Best Domain – Immune')       return 1.34;

    if (label === 'PhenoAge Best Improvement')  return 1.40;

    // Editorial neutrals (when server actually sends them)
    if (label === 'First Applicants') return 1.19;
    if (label === 'Pregnancy') return 1.191;
    if (label === 'Host') return 1.192;
    if (label === 'Perfect Application') return 1.193;

    return 1.50;
}

// Build one server badge bubble HTML
function buildServerBadgeHtml(b, athlete) {
    const icon = pickIconForServerBadge(b);
    const tooltip = makeTooltipFromServerBadge(b, athlete);
    const style = pickBackgroundForServerBadge(b);
    const url = pickClickUrl(b, athlete);
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
   Public API
   ========================= */
window.setBadges = function (athlete, athleteCell /* row wrapper or modal */) {
    let badgeContainer = null;
    if (athleteCell && athleteCell.classList && athleteCell.classList.contains('badge-section')) {
        badgeContainer = athleteCell;
    } else if (athleteCell && typeof athleteCell.querySelector === 'function') {
        badgeContainer = athleteCell.querySelector('.badge-section') || athleteCell.querySelector('.badges');
    }
    if (!badgeContainer) return;

    const items = [];

    // 0) Personal link bubble (teal, clickable) — not a badge fallback
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

    // 1) Server-sent badges ONLY
    const serverBadges =
        Array.isArray(athlete.Badges) ? athlete.Badges :
            Array.isArray(athlete.badges) ? athlete.badges : [];

    serverBadges.forEach(b => items.push(buildServerBadgeHtml(b, athlete)));

    // 2) Local-only "Perfect Guess" (user-specific)
    try {
        const rawName = athlete.name || athlete.Name || athlete.displayName || athlete.DisplayName || '';
        if (rawName) {
            let slug;
            if (typeof window.slugifyName === 'function') {
                slug = window.slugifyName(rawName, true);
            } else {
                let s = String(rawName).toLowerCase();
                try { s = s.normalize('NFKD'); } catch (_) { /* ignore */ }
                slug = s.replace(/[^\w]+/g, '-').replace(/^-+|-+$/g, '');
            }

            const allGuesses = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}');
            const g = allGuesses && allGuesses[slug];
            const guessed = (g && g.value != null) ? parseInt(g.value, 10) : null;

            const chrono = athlete.chronologicalAge ?? athlete.ChronoAge ?? null;
            const actualInt = Number.isFinite(chrono) ? parseInt(chrono, 10) : null;

            if (Number.isInteger(guessed) && Number.isInteger(actualInt) && (guessed - actualInt === 0)) {
                items.push({
                    order: 1.194,
                    html: `<span class="badge-class" title="Bullseye: You Guessed Their Age Perfectly!" style="${LEGACY_BG.black}">
                               <i class="fa fa-bullseye"></i>
                           </span>`
                });
            }
        }
    } catch { /* no-op: localStorage unavailable or malformed */ }

    // Render
    items.sort((a, b) => a.order - b.order);
    badgeContainer.innerHTML = items.map(x => x.html).join('');

    // Tiny appear animation on leaderboard (not in modal)
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
