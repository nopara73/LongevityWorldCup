/* badges.js
   Server-driven badge rendering only.
   IMPORTANT:
   - Competitive + editorial badges are rendered ONLY if the server includes them in athlete.Badges.
   - No client-side fallbacks for
   Podcast
   First applicants
   Season badges (S25, S26, ...)
   Pregnancy
   Host
   Perfect application
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
    pheno:        "background: linear-gradient(135deg, var(--main-action-selected-color, #24332c), var(--ink, #131b17)); border: 2px solid var(--surface-border-strong, rgba(28,39,34,.18));",
    bortz:        "background: linear-gradient(135deg, var(--accent-bright, #43ee83), var(--primary-color, #0b7d45)); border: 2px solid var(--success-text, #075f35); color: #04130a;",
    liver:        "background: linear-gradient(135deg, #aa336a, #6e0f3c); border: 2px solid #4a0b27;",
    kidney:       "background: linear-gradient(135deg, #128fa1, #0e4d64); border: 2px solid #082c3a;",
    metabolic:    "background: linear-gradient(135deg, #ff9800, #9c5700); border: 2px solid #5c3200;",
    inflammation: "background: linear-gradient(135deg, #b71c1c, #7f0000); border: 2px solid #4a0000;",
    immune:       "background: linear-gradient(135deg, #43a047, #1b5e20); border: 2px solid #0d3a12;",
    vitaminD:     "background: linear-gradient(135deg, #f9a825, #f57f17); border: 2px solid #8d5b00;",
    phenoPace:    "background: linear-gradient(135deg, var(--main-action-selected-color, #24332c), var(--ink, #131b17)); border: 2px solid var(--surface-border-strong, rgba(28,39,34,.18));",
    bortzPace:    "background: linear-gradient(135deg, var(--accent-bright, #43ee83), var(--primary-color, #0b7d45)); border: 2px solid var(--success-text, #075f35); color: #04130a;",
    personal:     "background: linear-gradient(135deg, var(--accent-bright, #43ee83), var(--primary-color, #0b7d45)); border: 2px solid var(--success-text, #075f35); color: #04130a;",
    black:        "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;"
};

// FontAwesome icon mapping (by server labels)
const BASE_ICONS = {
    'Age reduction': 'fa-bolt',
    'Chronological age – oldest': 'fa-infinity',
    'Chronological age – youngest': 'fa-baby',
    'Pheno Age – lowest': 'fa-feather',
    'Bortz Age – lowest': 'fa-feather-pointed',
    'Pheno pace of aging': 'fa-gauge-simple-high',
    'Bortz pace of aging': 'fa-gauge-high',
    'Most submissions': 'fa-skull-crossbones',
    '≥2 submissions': 'fa-calendar-check',
    'Crowd – most guessed': 'fa-users',
    'Crowd – age gap (chrono−crowd)': 'fa-user-ninja',
    'Crowd Age – lowest': 'fa-baby',
    'Pheno Age best improvement': 'fa-clock',
    'Bortz Age best improvement': 'fa-clock',
    'Best domain – liver': 'fa-droplet',
    'Best domain – kidney': 'fa-toilet',
    'Best domain – metabolic': 'fa-fire',
    'Best domain – inflammation': 'fa-temperature-three-quarters',
    'Best domain – immune': 'fa-virus',
    'Best domain – vitamin D': 'fa-sun',
    'Podcast': 'fa-microphone',
    'First applicants': 'fa-dove',
    'Pregnancy': 'fa-baby-carriage',
    'Host': 'fa-house',
    'Perfect application': 'fa-ruler'
};

function canonicalizeBadgeLabel(label) {
    const normalized = String(label || '')
        .replace(/â€“/g, '-')
        .replace(/[–—]/g, '-')
        .trim();

    const aliases = {
        'Age Reduction': 'Age reduction',
        'Chronological Age - Oldest': 'Chronological age - oldest',
        'Chronological Age - Youngest': 'Chronological age - youngest',
        'PhenoAge - Lowest': 'Pheno Age - lowest',
        'PhenoAge Best Improvement': 'Pheno Age best improvement',
        'Bortz Age - Lowest': 'Bortz Age - lowest',
        'Bortz Age Best Improvement': 'Bortz Age best improvement',
        'Pheno Pace of Aging': 'Pheno pace of aging',
        'Bortz Pace of Aging': 'Bortz pace of aging',
        'Most Submissions': 'Most submissions',
        '>=2 Submissions': '≥2 submissions',
        '≥2 Submissions': '≥2 submissions',
        'Crowd - Most Guessed': 'Crowd – most guessed',
        'Crowd - Age Gap (Chrono−Crowd)': 'Crowd – age gap (chrono−crowd)',
        'Crowd Age - lowest': 'Crowd Age – lowest',
        'First Applicants': 'First applicants',
        'Perfect Application': 'Perfect application'
    };

    if (aliases[normalized]) return aliases[normalized];
    if (normalized.startsWith('Best Domain - ')) {
        const domain = normalized.slice('Best Domain - '.length).trim();
        return 'Best domain – ' + (domain === 'Vitamin D' ? 'vitamin D' : domain.toLowerCase());
    }
    return normalized.replace(/ - /g, ' – ');
}

// Normalized getters (support server PascalCase and possible legacy camelCase)
function getLabel(b) { return canonicalizeBadgeLabel(b.BadgeLabel || b.Label || ''); }
function getCat(b)   { return b.LeagueCategory || b.Category || 'Global'; }
function getVal(b)   { return b.LeagueValue ?? b.Value ?? null; }
function getPlace(b) { return (typeof b.Place === 'number') ? b.Place : null; }

function isProofCountBadge(b) {
    const label = getLabel(b);
    return /^\s*\d+\s+public\s+proofs?\s*$/i.test(label);
}

function isSeasonBadgeLabel(label) {
    return /^S\d{2}$/i.test(String(label || '').trim());
}

function getSeasonBadgeYear(label) {
    const match = /^S(\d{2})$/i.exec(String(label || '').trim());
    if (!match) return null;
    return 2000 + Number(match[1]);
}

function capitalizeBadgeTooltipPrefix(tooltip) {
    const text = String(tooltip || '');
    const colonIndex = text.indexOf(':');
    if (colonIndex < 0) return text;

    const prefix = text.slice(0, colonIndex);
    return prefix + text.slice(colonIndex);
}

function getSeasonBadgeTag(label) {
    const year = getSeasonBadgeYear(label);
    return year ? `LWC${String(year).slice(-2)}` : String(label || '').trim().toUpperCase();
}

function escapeAttr(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

function styleWithBadgeVars(style) {
    const raw = String(style || '');
    const match = /background\s*:\s*([^;]+);?/i.exec(raw);
    return match ? `${raw}--badge-bg:${match[1]};` : raw;
}

function getAthleteProfileSlug(athlete) {
    const explicitSlug = athlete && (athlete.AthleteSlug || athlete.athleteSlug || athlete.Slug || athlete.slug);
    const raw = explicitSlug || (athlete && (athlete.name || athlete.Name || athlete.displayName || athlete.DisplayName));
    if (!raw) return '';

    if (typeof window.slugifyName === 'function') {
        return window.slugifyName(String(raw), false);
    }

    let s = String(raw).toLowerCase();
    try { s = s.normalize('NFKD'); } catch (_) {}
    return s.replace(/[^\w]+/g, '-').replace(/^-+|-+$/g, '');
}

function getAthleteProfileUrl(athlete) {
    const slug = getAthleteProfileSlug(athlete);
    return slug ? `/athlete/${encodeURIComponent(slug)}` : '';
}

function getBadgeFamilyClass(b) {
    const label = getLabel(b);
    const place = getPlace(b);

    if (typeof label === 'string' && label.startsWith('Best domain')) {
        return 'badge-family-domain';
    }

    if (
        label === 'Pheno pace of aging' ||
        label === 'Bortz pace of aging' ||
        label === 'Pheno Age best improvement' ||
        label === 'Bortz Age best improvement'
    ) {
        return 'badge-family-pace';
    }

    if (
        label === 'Age reduction' ||
        (typeof label === 'string' && label.startsWith('Crowd – ')) ||
        label === 'Chronological age – oldest' ||
        label === 'Chronological age – youngest' ||
        label === 'Pheno Age – lowest' ||
        label === 'Bortz Age – lowest' ||
        (isSeasonBadgeLabel(label) && place)
    ) {
        return 'badge-family-rank';
    }

    return 'badge-family-utility';
}

// Choose proper icon; override special cases
function pickIconForServerBadge(b) {
    const label = getLabel(b);
    const cat = (getCat(b) || '').toLowerCase();
    const place = getPlace(b);
    const val = getVal(b);

    if (label === 'Age reduction' && (cat === 'global' || cat === 'amateur') && place) {
        if (place === 1) return 'fa-crown';
        if (place === 2) return 'fa-medal';
        if (place === 3) return 'fa-award';
    }

    if (label === 'Age reduction' && (cat === 'division' || cat === 'generation') && val) {
        if (cat === 'division' && typeof window.TryGetDivisionFaIcon === 'function') {
            return window.TryGetDivisionFaIcon(val) || (BASE_ICONS[label] || 'fa-award');
        }
        if (cat === 'generation' && typeof window.TryGetGenerationFaIcon === 'function') {
            return window.TryGetGenerationFaIcon(val) || (BASE_ICONS[label] || 'fa-award');
        }
    }

    if (label === 'Age reduction' && cat === 'exclusive') return 'fa-umbrella-beach';

    if (label === 'Chronological age – oldest' && place) {
        if (place === 1) return 'fa-infinity';
        if (place === 2) return 'fa-scroll';
        if (place === 3) return 'fa-leaf';
    }
    if (label === 'Chronological age – youngest' && place) {
        if (place === 1) return 'fa-baby';
        if (place === 2) return 'fa-child';
        if (place === 3) return 'fa-running';
    }
    if (label === 'Pheno Age – lowest' && place) {
        if (place === 1) return 'fa-feather';
        if (place === 2) return 'fa-portrait';
        if (place === 3) return 'fa-hourglass-start';
    }
    if (label === 'Bortz Age – lowest' && place) {
        if (place === 1) return 'fa-feather-pointed';
        if (place === 2) return 'fa-portrait';
        if (place === 3) return 'fa-hourglass-start';
    }

    if (label === 'Pheno pace of aging' && place) {
        if (place === 1) return 'fa-gauge-simple-high';
        if (place === 2) return 'fa-stopwatch';
        if (place === 3) return 'fa-wave-square';
    }
    if (label === 'Bortz pace of aging' && place) {
        if (place === 1) return 'fa-gauge-simple-high';
        if (place === 2) return 'fa-stopwatch';
        if (place === 3) return 'fa-wave-square';
    }

    if (label === 'First applicants') {
        if (place === 1) return 'fa-circle-notch';
        if (place === 2) return 'fa-star';
        if (place === 3) return 'fa-bolt';
        if (place && place >= 4 && place <= 10) return 'fa-dove';
        return 'fa-dove';
    }

    if (isSeasonBadgeLabel(label)) {
        if (place === 1) return 'fa-trophy';
        if (place === 2) return 'fa-medal';
        if (place === 3) return 'fa-award';
        if (place && place >= 4 && place <= 20) return 'fa-ranking-star';
        return 'fa-ranking-star';
    }

    return BASE_ICONS[label] || 'fa-award';
}

// Backgrounds to mimic the old palette
function pickBackgroundForServerBadge(b) {
    const label = getLabel(b);
    const place = getPlace(b);

    if (
        label === 'Chronological age – oldest' ||
        label === 'Chronological age – youngest'
    ) {
        return LEGACY_BG.default;
    }

    if (label === 'Pheno Age – lowest') return LEGACY_BG.pheno;
    if (label === 'Bortz Age – lowest') return LEGACY_BG.bortz;

    const medalLike =
        label === 'Age reduction' ||
        (typeof label === 'string' && label.startsWith('Crowd – '));

    if (medalLike && place && [1, 2, 3].includes(place)) {
        return LEGACY_BG.medal[place - 1];
    }

    if (label === 'Most submissions' || label === '≥2 submissions') return LEGACY_BG.black;
    if (label === 'Podcast') return LEGACY_BG.black;

    if (label === 'Best domain – liver')        return LEGACY_BG.liver;
    if (label === 'Best domain – kidney')       return LEGACY_BG.kidney;
    if (label === 'Best domain – metabolic')    return LEGACY_BG.metabolic;
    if (label === 'Best domain – inflammation') return LEGACY_BG.inflammation;
    if (label === 'Best domain – immune')       return LEGACY_BG.immune;
    if (label === 'Best domain – vitamin D')    return LEGACY_BG.vitaminD;

    if (label === 'Pheno pace of aging')         return LEGACY_BG.phenoPace;
    if (label === 'Bortz pace of aging')         return LEGACY_BG.bortzPace;
    if (label === 'Pheno Age best improvement')   return LEGACY_BG.pheno;
    if (label === 'Bortz Age best improvement')  return LEGACY_BG.bortz;

    return LEGACY_BG.default;
}

// Optional link target (when clicking a medal/badge)
function pickClickUrl(b, athlete) {
    const label = getLabel(b);
    const cat = (getCat(b) || '').toLowerCase();
    const val = getVal(b);

    if (label === 'Age reduction') {
        if (cat === 'global') return '/';
        if (cat === 'amateur') return '/?filters=amateur';
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

function placementText(place) {
    if (place === 1) return '1st';
    if (place === 2) return '2nd';
    if (place === 3) return '3rd';
    return `#${place}`;
}

// Tooltip builders for official competition records.
function makeTooltipFromServerBadge(b, athlete, opts) {
    const label = getLabel(b);
    const place = getPlace(b);
    const cat = (getCat(b) || '').toLowerCase();
    const val = getVal(b);
    const suppressValues = !!(opts && opts.suppressValues);

    if (label === 'Age reduction') {
        if (cat === 'global') {
            if (place) return `Ultimate League #${place}: age reduction`;
            return 'Age reduction';
        }
        if (cat === 'amateur') {
            if (place) return `Amateur League #${place}: pheno age reduction`;
            return 'Amateur League placement';
        }
        if (cat === 'division') {
            return place ? `${val} League #${place}: age reduction` : `${val} League placement`;
        }
        if (cat === 'generation') {
            return place ? `${val} League #${place}: age reduction` : `${val} League placement`;
        }
        if (cat === 'exclusive') {
            return place ? `${val} League #${place}: age reduction` : `${val} League placement`;
        }
    }

    if (label === 'Chronological age – oldest' && place) {
        if (suppressValues) {
            return `Oldest athlete: ${placementText(place)} by chronological age`;
        }

        const age = (athlete?.chronologicalAge ?? athlete?.ChronoAge ?? athlete?.chronological_age);
        const hasAge = Number.isFinite(Number(age));
        const ageText = hasAge ? Number(age).toFixed(1) : '';
        if (!hasAge) {
            return `Oldest athlete: ${placementText(place)} by chronological age`;
        }
        return `Oldest athlete: ${placementText(place)} by chronological age (${ageText} years)`;
    }

    if (label === 'Chronological age – youngest' && place) {
        if (suppressValues) {
            return `Youngest athlete: ${placementText(place)} by chronological age`;
        }

        const age = (athlete?.chronologicalAge ?? athlete?.ChronoAge ?? athlete?.chronological_age);
        const hasAge = Number.isFinite(Number(age));
        const ageText = hasAge ? Number(age).toFixed(1) : '';
        if (!hasAge) {
            return `Youngest athlete: ${placementText(place)} by chronological age`;
        }
        return `Youngest athlete: ${placementText(place)} by chronological age (${ageText} years)`;
    }

    if (label === 'Pheno Age – lowest' && place) {
        if (suppressValues) {
            return `Lowest Pheno Age: ${placementText(place)} in the field`;
        }

        const ph = (athlete?.lowestPhenoAge ?? athlete?.LowestPhenoAge);
        const hasPh = Number.isFinite(Number(ph));
        const phText = hasPh ? Number(ph).toFixed(1) : '';
        if (!hasPh) {
            return `Lowest Pheno Age: ${placementText(place)} in the field`;
        }
        return `Lowest Pheno Age: ${placementText(place)} in the field (${phText} years)`;
    }

    if (label === 'Bortz Age – lowest' && place) {
        if (suppressValues) {
            return `Lowest Bortz Age: ${placementText(place)} in the field`;
        }

        const ba = (athlete?.lowestBortzAge ?? athlete?.LowestBortzAge);
        const hasBa = Number.isFinite(Number(ba));
        const baText = hasBa ? Number(ba).toFixed(1) : '';
        if (!hasBa) {
            return `Lowest Bortz Age: ${placementText(place)} in the field`;
        }
        return `Lowest Bortz Age: ${placementText(place)} in the field (${baText} years)`;
    }

    if (label === 'Pheno pace of aging' && place) {
        return `Pheno pace of aging: ${placementText(place)} in the field`;
    }

    if (label === 'Bortz pace of aging' && place) {
        return `Bortz pace of aging: ${placementText(place)} in the field`;
    }

    if (label === 'Most submissions') {
        if (suppressValues) return 'Most submissions: highest number of accepted result submissions';
        const c = (athlete?.submissionCount ?? athlete?.SubmissionCount ?? 0);
        return `Most submissions: ${c} accepted result submissions`;
    }

    if (label === '≥2 submissions') {
        return 'The Regular: two or more tests submitted';
    }

    if (typeof label === 'string' && label.startsWith('Crowd – ')) {
        if (label.endsWith('most guessed') && place) {
            if (suppressValues) {
                return `Most guessed: ${placementText(place)} by accepted crowd guesses`;
            }
            const count = (athlete?.crowdCount ?? athlete?.CrowdCount ?? 0);
            return `Most guessed: ${placementText(place)} by accepted crowd guesses (${count})`;
        }

        if (label.includes('age gap') && place) {
            if (suppressValues) {
                return `Crowd age gap: ${placementText(place)} largest chrono-to-crowd gap`;
            }
            const ch = Number(athlete?.chronologicalAge ?? athlete?.ChronoAge ?? 0);
            const cr = Number(athlete?.crowdAge ?? athlete?.CrowdAge ?? 0);
            const gap = Math.max(0, ch - cr);
            const gapText = Number.isFinite(gap) ? gap.toFixed(1) : '';
            const yearWord = gapText === '1.0' ? 'year' : 'years';
            return `Crowd age gap: ${placementText(place)} largest chrono-to-crowd gap (${gapText} ${yearWord})`;
        }

        if (label.endsWith('lowest crowd age') && place) {
            if (suppressValues) {
                return `Lowest Crowd Age: ${placementText(place)} in the crowd age field`;
            }
            const cr = Number(athlete?.crowdAge ?? athlete?.CrowdAge ?? 0);
            const ageText = Number.isFinite(cr) ? cr.toFixed(1) : '';
            const yearWord = ageText === '1.0' ? 'year' : 'years';
            return `Lowest Crowd Age: ${placementText(place)} in the crowd age field (${ageText} ${yearWord})`;
        }
    }

    if (label === 'Pheno Age best improvement') {
        if (suppressValues) return 'Best Pheno improvement: greatest age reduction from first submission';
        let delta = null;
        if (athlete && typeof athlete.phenoAgeDifference === 'number') delta = athlete.phenoAgeDifference;
        else if (athlete && typeof athlete.PhenoAgeDiffFromBaseline === 'number') delta = athlete.PhenoAgeDiffFromBaseline;
        if (Number.isFinite(delta)) {
            const years = Number(delta).toFixed(1);
            return `Best Pheno improvement: ${years} years from first submission`;
        }
        return 'Best Pheno improvement: greatest age reduction from first submission';
    }

    if (label === 'Bortz Age best improvement') {
        if (suppressValues) return 'Best Bortz improvement: greatest age reduction from first submission';
        let delta = null;
        if (athlete && typeof athlete.bortzAgeDifference === 'number') delta = athlete.bortzAgeDifference;
        else if (athlete && typeof athlete.BortzAgeDiffFromBaseline === 'number') delta = athlete.BortzAgeDiffFromBaseline;
        if (Number.isFinite(delta)) {
            const years = Number(delta).toFixed(1);
            return `Best Bortz improvement: ${years} years from first submission`;
        }
        return 'Best Bortz improvement: greatest age reduction from first submission';
    }

    if (typeof label === 'string' && label.startsWith('Best domain')) {
        if (suppressValues) {
            if (label === 'Best domain – liver') return 'Best liver profile: top domain score';
            if (label === 'Best domain – kidney') return 'Best kidney profile: top domain score';
            if (label === 'Best domain – metabolic') return 'Best metabolic profile: top domain score';
            if (label === 'Best domain – inflammation') return 'Best inflammation profile: top domain score';
            if (label === 'Best domain – immune') return 'Best immune profile: top domain score';
            if (label === 'Best domain – vitamin D') return 'Best vitamin D profile: top domain score';
        }

        // Best domain awards use domain contribution; biomarkers from athlete profile (age-visualization), not onboarding.
        const bortz = athlete?.bestBortzValues || athlete?.BestBortzValues;
        if (typeof window.getBestDomainBiomarkerTooltip === 'function' && Array.isArray(bortz) && bortz.length >= 22) {
            const biomarkerPart = window.getBestDomainBiomarkerTooltip(label, bortz);
            if (biomarkerPart) {
                if (label === 'Best domain – liver') return 'Best liver profile: top domain score ' + biomarkerPart;
                if (label === 'Best domain – kidney') return 'Best kidney profile: top domain score ' + biomarkerPart;
                if (label === 'Best domain – metabolic') return 'Best metabolic profile: top domain score ' + biomarkerPart;
                if (label === 'Best domain – immune') return 'Best immune profile: top domain score ' + biomarkerPart;
                if (label === 'Best domain – inflammation') return 'Best inflammation profile: top domain score ' + biomarkerPart;
                if (label === 'Best domain – vitamin D') return 'Best vitamin D profile: top domain score ' + biomarkerPart;
            }
        }

        // Fallback when bestBortzValues not available (e.g. Pheno-only data).
        const best = athlete?.bestBiomarkerValues || athlete?.BestMarkerValues;
        if (Array.isArray(best) && best.length >= 10) {
            const crp = (Math.exp(Number(best[4])) * 10).toFixed(2);
            if (label === 'Best domain – inflammation') return `Best inflammation profile: top domain score (CRP ${crp} mg/L)`;

            const alb = Number(best[1]).toFixed(1);
            const creat = Number(best[2]).toFixed(1);
            const glu = Number(best[3]).toFixed(1);
            const wbc = Number(best[5]).toFixed(1);
            const lym = Number(best[6]).toFixed(1);
            const mcv = Number(best[7]).toFixed(1);
            const rdw = Number(best[8]).toFixed(1);
            const alp = Number(best[9]).toFixed(1);
            if (label === 'Best domain – liver') return `Best liver profile: top domain score (albumin ${alb} g/L, ALP ${alp} U/L)`;
            if (label === 'Best domain – kidney') return `Best kidney profile: top domain score (creatinine ${creat} µmol/L)`;
            if (label === 'Best domain – metabolic') return `Best metabolic profile: top domain score (glucose ${glu} mmol/L)`;
            if (label === 'Best domain – immune') return `Best immune profile: top domain score (WBC ${wbc} 10³ cells/µL, lymphocyte ${lym}%, MCV ${mcv} fL, RDW ${rdw}%)`;
        }
        if (label === 'Best domain – vitamin D') return 'Best vitamin D profile: top domain score';
    }

    if (label === 'First applicants') {
        if (place) return `Founding athlete: ${placementText(place)} athlete to join the Longevity World Cup`;
        return 'First applicants';
    }

    if (isSeasonBadgeLabel(label)) {
        const tag = getSeasonBadgeTag(label);
        const year = getSeasonBadgeYear(label);
        const seasonText = year ? `${year} Longevity World Cup` : 'Longevity World Cup season';
        if (place === 1) return `${tag} · winner: finished 1st in the ${seasonText}`;
        if (place === 2) return `${tag} · 2nd place: finished 2nd in the ${seasonText}`;
        if (place === 3) return `${tag} · 3rd place: finished 3rd in the ${seasonText}`;
        if (place && place >= 4 && place <= 10) return `${tag} · top 10: finished among the top 10 in the ${seasonText}`;
        if (place && place >= 11 && place <= 20) return `${tag} · top 20: finished among the top 20 in the ${seasonText}`;
        return `${tag} · seasonal finish`;
    }


    if (label === 'Podcast') return "Podcast: hear this athlete's story in depth";
    if (label === 'Pregnancy') return 'Pregnancy: result context noted';
    if (label === 'Host') return 'Host: organizer of the Longevity World Cup';
    if (label === 'Perfect application') return 'Complete application: entry form accepted without corrections';

    return place ? `${label}: #${place}` : `${label}`;
}

const makeUnformattedTooltipFromServerBadge = makeTooltipFromServerBadge;
makeTooltipFromServerBadge = function (b, athlete, opts) {
    return capitalizeBadgeTooltipPrefix(makeUnformattedTooltipFromServerBadge(b, athlete, opts));
};

/* -------------------------------------------------
   ORDERING — match legacy: link → podcast → neutrals → medals
   ------------------------------------------------- */
function computeOrder(b) {
    const label = getLabel(b);
    const cat = (getCat(b) || '').toLowerCase();
    const place = getPlace(b);

    const isMedalFamily =
        label === 'Age reduction' ||
        (typeof label === 'string' && label.startsWith('Crowd – '));

    if (isMedalFamily && place && [1, 2, 3].includes(place)) {
        const base = place === 1 ? 5.10 : (place === 2 ? 5.20 : 5.30);
        let micro = 0;

        if (label === 'Age reduction') {
            if (cat === 'global')          micro = 0.01;
            else if (cat === 'division')   micro = 0.02;
            else if (cat === 'generation') micro = 0.03;
            else if (cat === 'exclusive')  micro = 0.04;
        } else if (label === 'Crowd – most guessed')         micro = 0.20;
        else if (label === 'Crowd – age gap (chrono−crowd)') micro = 0.21;
        else if (label === 'Crowd Age – lowest')       micro = 0.22;

        return base + micro;
    }

    if (label === 'Podcast') return 1.10;
    if (label === 'Chronological age – oldest') return 1.11;
    if (label === 'Chronological age – youngest') return 1.12;
    if (label === 'First applicants') return 1.13;
    if (label === 'Pregnancy') return 1.14;
    if (label === 'Host') return 1.15;
    if (label === 'Perfect application') return 1.16;
    if (label === 'Most submissions') return 1.17;
    if (label === '≥2 submissions') return 1.18;
    if (isSeasonBadgeLabel(label) && place) return 1.19;

    if (label === 'Pheno Age – lowest') return 2.10;
    if (label === 'Pheno Age best improvement') return 2.11;
    if (label === 'Pheno pace of aging') return 2.12;

    if (label === 'Bortz Age – lowest') return 3.10;
    if (label === 'Bortz Age best improvement') return 3.11;
    if (label === 'Bortz pace of aging') return 3.12;

    if (label === 'Best domain – liver') return 4.10;
    if (label === 'Best domain – kidney') return 4.11;
    if (label === 'Best domain – metabolic') return 4.12;
    if (label === 'Best domain – inflammation') return 4.13;
    if (label === 'Best domain – immune') return 4.14;
    if (label === 'Best domain – vitamin D') return 4.15;

    return 4.90;
}

// Build one server badge bubble HTML
function buildServerBadgeHtml(b, athlete) {
    const icon = pickIconForServerBadge(b);
    const tooltip = makeTooltipFromServerBadge(b, athlete);
    const style = pickBackgroundForServerBadge(b);
    const url = pickClickUrl(b, athlete);
    const order = computeOrder(b);
    const familyClass = getBadgeFamilyClass(b);
    const label = getLabel(b);
    const typeClass = label === 'Podcast' ? 'badge-podcast' : '';
    const className = `badge-class ${familyClass}${typeClass ? ` ${typeClass}` : ''}`;

    if (url && label === 'Podcast') {
        return {
            order,
            searchText: tooltip,
            html: `<a class="${className} badge-clickable" href="${escapeAttr(url)}" target="_blank" rel="noopener" aria-label="Open podcast" title="${tooltip}" style="${styleWithBadgeVars(style)}"><i class="fa ${icon}"></i></a>`
        };
    }

    const clickableAttrs = url
        ? `class="${className} badge-clickable" ${spanA11y} onclick="window.location.href='${url}';"`
        : `class="${className}"`;

    return {
        order,
        searchText: tooltip,
        html: `<span ${clickableAttrs} title="${tooltip}" style="${styleWithBadgeVars(style)}"><i class="fa ${icon}"></i></span>`
    };
}

/* =========================
   Public API
   ========================= */
window.setBadges = function (athlete, athleteCell) {
    let badgeContainer = null;
    if (athleteCell && athleteCell.classList && athleteCell.classList.contains('badge-section')) {
        badgeContainer = athleteCell;
    } else if (athleteCell && typeof athleteCell.querySelector === 'function') {
        badgeContainer = athleteCell.querySelector('.badge-section') || athleteCell.querySelector('.badges');
    }
    if (!badgeContainer) return;

    const items = [];
    const isModalStrip =
        (badgeContainer.id && badgeContainer.id === 'modalBadgeStrip') ||
        (typeof badgeContainer.closest === 'function' && !!badgeContainer.closest('#detailsModal'));

    const personalLink = athlete.personalLink || athlete.PersonalLink;
    if (personalLink) {
        const href = personalLink.startsWith('http') ? personalLink : `https://${personalLink}`;
        items.push({
            order: 0,
            searchText: 'Personal page',
            html: `<a class="badge-class badge-family-utility badge-clickable" ${spanA11y} href="${href}" target="_blank" rel="noopener"
               title="Personal page" style="${LEGACY_BG.personal}">
               <i class="fa fa-link"></i>
             </a>`
        });
    }

    const serverBadges =
        Array.isArray(athlete.Badges) ? athlete.Badges :
            Array.isArray(athlete.badges) ? athlete.badges : [];

    serverBadges
        .filter(b => isModalStrip || !isProofCountBadge(b))
        .forEach(b => items.push(buildServerBadgeHtml(b, athlete)));

    try {
        const rawName = athlete.name || athlete.Name || athlete.displayName || athlete.DisplayName || '';
        if (rawName) {
            let slug;
            if (typeof window.slugifyName === 'function') {
                slug = window.slugifyName(rawName, true);
            } else {
                let s = String(rawName).toLowerCase();
                try { s = s.normalize('NFKD'); } catch (_) {}
                slug = s.replace(/[^\w]+/g, '-').replace(/^-+|-+$/g, '');
            }

            const allGuesses = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}');
            const g = allGuesses && allGuesses[slug];
            const guessed = (g && g.value != null) ? parseInt(g.value, 10) : null;

            const chrono = athlete.chronologicalAge ?? athlete.ChronoAge ?? null;
            const actualInt = Number.isFinite(chrono) ? parseInt(chrono, 10) : null;

            if (Number.isInteger(guessed) && Number.isInteger(actualInt) && (guessed - actualInt === 0)) {
                if (window.proDiscounts && typeof window.proDiscounts.setPerfectGuessMarker === 'function') {
                    window.proDiscounts.setPerfectGuessMarker();
                }
                items.push({
                    order: 1.194,
                    searchText: 'Bullseye: you guessed their age perfectly!',
                    html: `<span class="badge-class badge-family-utility" title="Bullseye: you guessed their age perfectly!" style="${LEGACY_BG.black}">
                               <i class="fa fa-bullseye"></i>
                           </span>`
                });
            }
        }
    } catch {}

    items.sort((a, b) => a.order - b.order);

    let renderedItems = items;
    if (!isModalStrip && items.length > 6) {
        const visibleItems = items.slice(0, 5);
        const hiddenItems = items.slice(5);
        const hiddenTitle = hiddenItems
            .map(x => x.searchText)
            .filter(Boolean)
            .join('\n');
        const athleteSlug = getAthleteProfileSlug(athlete);
        const athleteProfileUrl = getAthleteProfileUrl(athlete);
        const overflowTitle = hiddenTitle || `${hiddenItems.length} more badges`;
        const athleteName = athlete.displayName || athlete.DisplayName || athlete.name || athlete.Name || 'athlete';
        const overflowHtml = athleteProfileUrl
            ? `<a class="badge-class badge-family-utility badge-overflow-count badge-clickable" href="${escapeAttr(athleteProfileUrl)}" data-athlete-slug="${escapeAttr(athleteSlug)}" title="${escapeAttr(overflowTitle)}" aria-label="Open ${escapeAttr(athleteName)} profile to view ${hiddenItems.length} more badges">+${hiddenItems.length}</a>`
            : `<span class="badge-class badge-family-utility badge-overflow-count" title="${escapeAttr(overflowTitle)}">+${hiddenItems.length}</span>`;

        renderedItems = [
            ...visibleItems,
            {
                order: 99,
                searchText: hiddenTitle,
                html: overflowHtml
            }
        ];
    }

    badgeContainer.innerHTML = renderedItems.map(x => x.html).join('');

    try {
        const overflowLinks = badgeContainer.querySelectorAll('.badge-overflow-count[data-athlete-slug]');
        overflowLinks.forEach(link => {
            link.addEventListener('click', event => {
                event.stopPropagation();
                const slug = link.getAttribute('data-athlete-slug');
                if (slug && typeof window.openAthleteModalBySlug === 'function' && window.openAthleteModalBySlug(slug, { suppressGuessMyAge: true })) {
                    event.preventDefault();
                }
            });
        });

        if (!isModalStrip) {
            Array.from(badgeContainer.children).forEach(badge => {
                badge.style.animation = '';
            });
        }
    } catch {}
};

window.computeBadges = window.computeBadges || function () {};
window.pickIconForServerBadge = pickIconForServerBadge;
window.pickBackgroundForServerBadge = pickBackgroundForServerBadge;
window.makeTooltipFromServerBadge = makeTooltipFromServerBadge;
window.pickClickUrl = pickClickUrl;
window.getBadgeFamilyClass = getBadgeFamilyClass;
window.styleWithBadgeVars = styleWithBadgeVars;


