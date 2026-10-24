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
   Legacy visuals (backgrounds)
   ========================= */
const LEGACY_BG = {
    default: "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;",
    medal: [
        "background: linear-gradient(135deg, #ffd700, #8b8000); border: 2px solid #8a6f00;", // Gold
        "background: linear-gradient(135deg, #c0c0c0, #696969); border: 2px solid #6e6e6e;", // Silver
        "background: linear-gradient(135deg, #cd7f32, #5c4033); border: 2px solid #6b3519;"  // Bronze
    ],
    pheno:        "background: linear-gradient(135deg, #3b82f6, #1d4ed8); border: 2px solid #1e40af;",
    bortz:        "background: linear-gradient(135deg, #22c55e, #15803d); border: 2px solid #166534;",
    liver:        "background: linear-gradient(135deg, #aa336a, #6e0f3c); border: 2px solid #4a0b27;",
    kidney:       "background: linear-gradient(135deg, #128fa1, #0e4d64); border: 2px solid #082c3a;",
    metabolic:    "background: linear-gradient(135deg, #ff9800, #9c5700); border: 2px solid #5c3200;",
    inflammation: "background: linear-gradient(135deg, #b71c1c, #7f0000); border: 2px solid #4a0000;",
    immune:       "background: linear-gradient(135deg, #43a047, #1b5e20); border: 2px solid #0d3a12;",
    vitaminD:     "background: linear-gradient(135deg, #f9a825, #f57f17); border: 2px solid #8d5b00;",
    phenoPace:    "background: linear-gradient(135deg, #3b82f6, #1d4ed8); border: 2px solid #1e40af;",
    bortzPace:    "background: linear-gradient(135deg, #22c55e, #15803d); border: 2px solid #166534;",
    personal:     "background: linear-gradient(135deg, #00bcd4, #006e7a); border: 2px solid #004f56;",
    black:        "background: linear-gradient(135deg, #2a2a2a, #1e1e1e); border: 2px solid #333333;"
};

// FontAwesome icon mapping (by server labels)
const BASE_ICONS: Readonly<Record<string, string>> = {
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

interface BadgeTooltipOptions {
    readonly suppressValues?: boolean;
}

interface RenderedBadgeItem {
    readonly order: number;
    readonly searchText: string;
    readonly html: string;
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function canonicalizeBadgeLabel(label: unknown): string {
    const normalized = String(label || '')
        .replace(/â€“/g, '-')
        .replace(/[–—]/g, '-')
        .trim();

    const aliases: Readonly<Record<string, string>> = {
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
        'Crowd Age - Lowest': 'Crowd Age – lowest',
        'Crowd - lowest crowd age': 'Crowd Age – lowest',
        'First Applicants': 'First applicants',
        'Perfect Application': 'Perfect application'
    };

    const alias = aliases[normalized];
    if (alias) return alias;
    if (normalized.startsWith('Best Domain - ')) {
        const domain = normalized.slice('Best Domain - '.length).trim();
        return 'Best domain – ' + (domain === 'Vitamin D' ? 'vitamin D' : domain.toLowerCase());
    }
    return normalized.replace(/ - /g, ' – ');
}

// Normalized getters (support server PascalCase and possible legacy camelCase)
function getLabel(b: ServerBadge): string { return canonicalizeBadgeLabel(b.BadgeLabel || b.Label || ''); }
function getCat(b: ServerBadge): string { return String(b.LeagueCategory || b.Category || 'Global'); }
function getVal(b: ServerBadge): unknown { return b.LeagueValue ?? b.Value ?? null; }
function getPlace(b: ServerBadge): number | null { return (typeof b.Place === 'number') ? b.Place : null; }

function isCrowdMedalLabel(label: string): boolean {
    return label === 'Crowd – most guessed' || label === 'Crowd – age gap (chrono−crowd)' || label === 'Crowd Age – lowest';
}

function isSeasonBadgeLabel(label: unknown): boolean {
    return /^S\d{2}$/i.test(String(label || '').trim());
}

function getSeasonBadgeYear(label: unknown): number | null {
    const match = /^S(\d{2})$/i.exec(String(label || '').trim());
    if (!match) return null;
    return 2000 + Number(match[1]);
}

function capitalizeBadgeTooltipPrefix(tooltip: unknown): string {
    const text = String(tooltip || '');
    const colonIndex = text.indexOf(':');
    if (colonIndex < 0) return text;

    const prefix = text.slice(0, colonIndex).replace(/(^|[\s·–—-])([a-z])/g, (_, boundary, letter) => {
        return boundary + letter.toUpperCase();
    });
    return prefix + text.slice(colonIndex);
}

function getSeasonBadgeTag(label: unknown): string {
    const year = getSeasonBadgeYear(label);
    return year ? `LWC${String(year).slice(-2)}` : String(label || '').trim().toUpperCase();
}

function escapeAttr(value: unknown): string {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

function styleWithBadgeVars(style: string): string {
    const raw = String(style || '');
    const match = /background\s*:\s*([^;]+);?/i.exec(raw);
    return match ? `${raw}--badge-bg:${match[1]};` : raw;
}

function getAthleteProfileSlug(athlete: BadgeAthlete | null | undefined): string {
    if (window.LwcGuessState) {
        return window.LwcGuessState.getAthleteSlug(athlete);
    }

    const explicitSlug = athlete && (athlete.AthleteSlug || athlete.athleteSlug || athlete.Slug || athlete.slug);
    const raw = explicitSlug || (athlete && (athlete.name || athlete.Name || athlete.displayName || athlete.DisplayName));
    if (!raw) return '';

    if (typeof window.slugifyName === 'function') {
        return window.slugifyName(String(raw).replace(/_/g, '-'), false);
    }

    let s = String(raw).toLowerCase();
    try { s = s.normalize('NFKD'); } catch (_) {}
    return s.replace(/[^\w]+/g, '-').replace(/^-+|-+$/g, '');
}

function getAthleteProfileUrl(athlete: BadgeAthlete | null | undefined): string {
    const slug = getAthleteProfileSlug(athlete);
    return slug ? `/athlete/${encodeURIComponent(slug)}` : '';
}

function getBadgeFamilyClass(b: ServerBadge): string {
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
        isCrowdMedalLabel(label) ||
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
function pickIconForServerBadge(b: ServerBadge): string {
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
            return window.TryGetDivisionFaIcon(String(val)) || (BASE_ICONS[label] || 'fa-award');
        }
        if (cat === 'generation' && typeof window.TryGetGenerationFaIcon === 'function') {
            return window.TryGetGenerationFaIcon(String(val)) || (BASE_ICONS[label] || 'fa-award');
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
function pickBackgroundForServerBadge(b: ServerBadge): string {
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
        isCrowdMedalLabel(label);

    if (medalLike && place && [1, 2, 3].includes(place)) {
        return LEGACY_BG.medal[place - 1] ?? LEGACY_BG.default;
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
function pickClickUrl(b: ServerBadge, athlete?: BadgeAthlete | null): string | null {
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
        return window.normalizeWebUrl(String(podcastLink));
    }
    return null;
}

// Legacy tooltip builders (exact old copy)
function makeUnformattedTooltipFromServerBadge(
    b: ServerBadge,
    athlete?: BadgeAthlete | null,
    opts?: BadgeTooltipOptions
): string {
    const label = getLabel(b);
    const place = getPlace(b);
    const cat = (getCat(b) || '').toLowerCase();
    const val = getVal(b);
    const suppressValues = !!(opts && opts.suppressValues);

    if (label === 'Age reduction') {
        if (cat === 'global') {
            if (place === 1) return 'Ultimate Lifeform: #1 in the Ultimate League';
            if (place === 2) return 'Near Immortal: #2 in the Ultimate League';
            if (place === 3) return 'Third and threatening: #3 in the Ultimate League';
            return 'Age reduction';
        }
        if (cat === 'amateur') {
            if (place === 1) return 'Upstart: #1 in Amateur League';
            if (place === 2) return 'Challenger: #2 in Amateur League';
            if (place === 3) return 'Harbinger of Change: #3 in Amateur League';
            return '# in Amateur League';
        }
        if (cat === 'division') {
            if (place === 1) {
                const v = String(val || '').toLowerCase();
                if (v === "men's") return "The Alpha Male: #1 in Men's League";
                if (v === "women's") return "The Empress: #1 in Women's League";
                if (v === "open") return 'The Machine: #1 in Open League';
            }
            return `#${place} in ${val} League`;
        }
        if (cat === 'generation') {
            if (place === 1) {
                const g = String(val || '').toLowerCase();
                if (g === 'silent generation') return 'The Grandmaster: #1 in Silent Generation League';
                if (g === 'baby boomers') return 'The Iron Throne: #1 in Baby Boomers League';
                if (g === 'gen x') return 'The Last Ronin: #1 in Gen X League';
                if (g === 'millennials') return 'The Chosen One: #1 in Millennials League';
                if (g === 'gen z') return 'The Meme Lord: #1 in Gen Z League';
                if (g === 'gen alpha') return 'The Singularity: #1 in Gen Alpha League';
            }
            return `#${place} in ${val} League`;
        }
        if (cat === 'exclusive') {
            return `#${place} in ${val} League`;
        }
    }

    if (label === 'Chronological age – oldest' && place) {
        if (suppressValues) {
            if (place === 1) return 'Yoda: chronologically oldest';
            if (place === 2) return 'Master Roshi: chronologically 2nd oldest';
            if (place === 3) return 'Mr. Miyagi: chronologically 3rd oldest';
        }

        const age = (athlete?.chronologicalAge ?? athlete?.ChronoAge ?? athlete?.chronological_age);
        const hasAge = Number.isFinite(Number(age));
        const ageText = hasAge ? Number(age).toFixed(1) : '';
        if (!hasAge) {
            if (place === 1) return 'Yoda: chronologically oldest';
            if (place === 2) return 'Master Roshi: chronologically 2nd oldest';
            if (place === 3) return 'Mr. Miyagi: chronologically 3rd oldest';
        }
        if (place === 1) return `Yoda: chronologically oldest (age: ${ageText} years)`;
        if (place === 2) return `Master Roshi: chronologically 2nd oldest (age: ${ageText})`;
        if (place === 3) return `Mr. Miyagi: chronologically 3rd oldest (age: ${ageText})`;
    }

    if (label === 'Chronological age – youngest' && place) {
        if (suppressValues) {
            if (place === 1) return 'Son Goten: chronologically youngest';
            if (place === 2) return 'Son Gohan: chronologically 2nd youngest';
            if (place === 3) return 'Son Goku: chronologically 3rd youngest';
        }

        const age = (athlete?.chronologicalAge ?? athlete?.ChronoAge ?? athlete?.chronological_age);
        const hasAge = Number.isFinite(Number(age));
        const ageText = hasAge ? Number(age).toFixed(1) : '';
        if (!hasAge) {
            if (place === 1) return 'Son Goten: chronologically youngest';
            if (place === 2) return 'Son Gohan: chronologically 2nd youngest';
            if (place === 3) return 'Son Goku: chronologically 3rd youngest';
        }
        if (place === 1) return `Son Goten: chronologically youngest (age: ${ageText} years)`;
        if (place === 2) return `Son Gohan: chronologically 2nd youngest (age: ${ageText})`;
        if (place === 3) return `Son Goku: chronologically 3rd youngest (age: ${ageText})`;
    }

    if (label === 'Pheno Age – lowest' && place) {
        if (suppressValues) {
            if (place === 1) return 'Peter Pan: biologically youngest according to pheno age';
            if (place === 2) return 'Dorian Gray: biologically 2nd youngest according to pheno age';
            if (place === 3) return 'Benjamin Button: biologically 3rd youngest according to pheno age';
        }

        const ph = (athlete?.lowestPhenoAge ?? athlete?.LowestPhenoAge);
        const hasPh = Number.isFinite(Number(ph));
        const phText = hasPh ? Number(ph).toFixed(1) : '';
        if (!hasPh) {
            if (place === 1) return 'Peter Pan: biologically youngest according to pheno age';
            if (place === 2) return 'Dorian Gray: biologically 2nd youngest according to pheno age';
            if (place === 3) return 'Benjamin Button: biologically 3rd youngest according to pheno age';
        }
        if (place === 1) return `Peter Pan: biologically youngest according to pheno age: ${phText} years old`;
        if (place === 2) return `Dorian Gray: biologically 2nd youngest according to pheno age: ${phText} years old`;
        if (place === 3) return `Benjamin Button: biologically 3rd youngest according to pheno age: ${phText} years old`;
    }

    if (label === 'Bortz Age – lowest' && place) {
        if (suppressValues) {
            if (place === 1) return 'Peter Pan: biologically youngest according to bortz age';
            if (place === 2) return 'Dorian Gray: biologically 2nd youngest according to bortz age';
            if (place === 3) return 'Benjamin Button: biologically 3rd youngest according to bortz age';
        }

        const ba = (athlete?.lowestBortzAge ?? athlete?.LowestBortzAge);
        const hasBa = Number.isFinite(Number(ba));
        const baText = hasBa ? Number(ba).toFixed(1) : '';
        if (!hasBa) {
            if (place === 1) return 'Peter Pan: biologically youngest according to bortz age';
            if (place === 2) return 'Dorian Gray: biologically 2nd youngest according to bortz age';
            if (place === 3) return 'Benjamin Button: biologically 3rd youngest according to bortz age';
        }
        if (place === 1) return `Peter Pan: biologically youngest according to bortz age: ${baText} years old`;
        if (place === 2) return `Dorian Gray: biologically 2nd youngest according to bortz age: ${baText} years old`;
        if (place === 3) return `Benjamin Button: biologically 3rd youngest according to bortz age: ${baText} years old`;
    }

    if (label === 'Pheno pace of aging' && place) {
        if (place === 1) return 'Time Bender: best pheno pace of aging';
        if (place === 2) return 'Slow Clock: 2nd best pheno pace of aging';
        if (place === 3) return 'Time Hacker: 3rd best pheno pace of aging';
    }

    if (label === 'Bortz pace of aging' && place) {
        if (place === 1) return 'Time Bender: best bortz pace of aging';
        if (place === 2) return 'Slow Clock: 2nd best bortz pace of aging';
        if (place === 3) return 'Time Hacker: 3rd best bortz pace of aging';
    }

    if (label === 'Most submissions') {
        if (suppressValues) return 'The Submittinator: most tests submitted';
        const c = (athlete?.submissionCount ?? athlete?.SubmissionCount ?? 0);
        return `The Submittinator: most tests submitted: ${c}`;
    }

    if (label === '≥2 submissions') {
        return 'The Regular: two or more tests submitted';
    }

    if (isCrowdMedalLabel(label)) {
        if (label.endsWith('most guessed') && place) {
            if (suppressValues) {
                if (place === 1) return 'Popular AF: most age guesses received';
                if (place === 2) return 'Pretty damn popular: 2nd most age guesses received';
                if (place === 3) return 'Shockingly popular: 3rd most age guesses received';
            }
            const count = (athlete?.crowdCount ?? athlete?.CrowdCount ?? 0);
            if (place === 1) return `Popular AF: most age guesses received (${count})`;
            if (place === 2) return `Pretty damn popular: 2nd most age guesses received (${count})`;
            if (place === 3) return `Shockingly popular: 3rd most age guesses received (${count})`;
        }

        if (label.includes('age gap') && place) {
            if (suppressValues) {
                if (place === 1) return 'Skin Trafficker: perceived younger';
                if (place === 2) return 'Wrinkle Launderer: perceived younger';
                if (place === 3) return 'Collagen Smuggler: perceived younger';
            }
            const ch = Number(athlete?.chronologicalAge ?? athlete?.ChronoAge ?? 0);
            const cr = Number(athlete?.crowdAge ?? athlete?.CrowdAge ?? 0);
            const gap = Math.max(0, ch - cr);
            const gapText = Number.isFinite(gap) ? gap.toFixed(1) : '';
            const yearWord = gapText === '1.0' ? 'year' : 'years';
            if (place === 1) return `Skin Trafficker: perceived ${gapText} ${yearWord} younger`;
            if (place === 2) return `Wrinkle Launderer: perceived ${gapText} ${yearWord} younger`;
            if (place === 3) return `Collagen Smuggler: perceived ${gapText} ${yearWord} younger`;
        }

        if (label === 'Crowd Age – lowest' && place) {
            if (suppressValues) {
                if (place === 1) return 'Baby Boss: youngest looking';
                if (place === 2) return 'Lullaby Lord: 2nd youngest looking';
                if (place === 3) return 'Diaper Don: 3rd youngest looking';
            }
            const cr = Number(athlete?.crowdAge ?? athlete?.CrowdAge ?? 0);
            const ageText = Number.isFinite(cr) ? cr.toFixed(1) : '';
            const yearWord = ageText === '1.0' ? 'year' : 'years';
            if (place === 1) return `Baby Boss: youngest looking (crowd age: ${ageText} ${yearWord})`;
            if (place === 2) return `Lullaby Lord: 2nd youngest looking (crowd age: ${ageText} ${yearWord})`;
            if (place === 3) return `Diaper Don: 3rd youngest looking (crowd age: ${ageText} ${yearWord})`;
        }
    }

    if (label === 'Pheno Age best improvement') {
        if (suppressValues) return 'Redemption Arc: greatest age reversal from first submission according to pheno age';
        let delta = null;
        if (athlete && typeof athlete.phenoAgeDifference === 'number') delta = athlete.phenoAgeDifference;
        else if (athlete && typeof athlete.PhenoAgeDiffFromBaseline === 'number') delta = athlete.PhenoAgeDiffFromBaseline;
        if (Number.isFinite(delta)) {
            const years = Number(delta).toFixed(1);
            return `Redemption Arc: greatest age reversal from first submission according to pheno age: ${years} years`;
        }
        return 'Redemption Arc: greatest age reversal from first submission according to pheno age';
    }

    if (label === 'Bortz Age best improvement') {
        if (suppressValues) return 'Redemption Arc: greatest age reversal from first submission according to bortz age';
        let delta = null;
        if (athlete && typeof athlete.bortzAgeDifference === 'number') delta = athlete.bortzAgeDifference;
        else if (athlete && typeof athlete.BortzAgeDiffFromBaseline === 'number') delta = athlete.BortzAgeDiffFromBaseline;
        if (Number.isFinite(delta)) {
            const years = Number(delta).toFixed(1);
            return `Redemption Arc: greatest age reversal from first submission according to bortz age: ${years} years`;
        }
        return 'Redemption Arc: greatest age reversal from first submission according to bortz age';
    }

    if (typeof label === 'string' && label.startsWith('Best domain')) {
        if (suppressValues) {
            if (label === 'Best domain – liver') return 'Liver King: top liver profile';
            if (label === 'Best domain – kidney') return 'Kidney Overlord: top kidney profile';
            if (label === 'Best domain – metabolic') return 'Metabolic Machine: top metabolic profile';
            if (label === 'Best domain – inflammation') return 'Inflammation Whisperer: top inflammation profile';
            if (label === 'Best domain – immune') return 'Pathogen Punisher: top immune profile';
            if (label === 'Best domain – vitamin D') return 'Sun God: top vitamin D profile';
        }

        // Best domain awards use domain contribution; biomarkers from athlete profile (age-visualization), not onboarding.
        const bortz = athlete?.bestBortzValues || athlete?.BestBortzValues;
        if (typeof window.getBestDomainBiomarkerTooltip === 'function' && Array.isArray(bortz) && bortz.length >= 22) {
            const biomarkerPart = window.getBestDomainBiomarkerTooltip(label, bortz);
            if (biomarkerPart) {
                if (label === 'Best domain – liver') return 'Liver King: top liver profile ' + biomarkerPart;
                if (label === 'Best domain – kidney') return 'Kidney Overlord: top kidney profile ' + biomarkerPart;
                if (label === 'Best domain – metabolic') return 'Metabolic Machine: top metabolic profile ' + biomarkerPart;
                if (label === 'Best domain – immune') return 'Pathogen Punisher: top immune profile ' + biomarkerPart;
                if (label === 'Best domain – inflammation') return 'Inflammation Whisperer: top inflammation profile ' + biomarkerPart;
                if (label === 'Best domain – vitamin D') return 'Sun God: top vitamin D profile ' + biomarkerPart;
            }
        }

        // Fallback when bestBortzValues not available (e.g. Pheno-only data).
        const best = athlete?.bestBiomarkerValues || athlete?.BestMarkerValues;
        if (Array.isArray(best) && best.length >= 10) {
            const crp = (Math.exp(Number(best[4])) * 10).toFixed(2);
            if (label === 'Best domain – inflammation') return `Inflammation Whisperer: top inflammation profile (CRP ${crp} mg/L)`;

            const alb = Number(best[1]).toFixed(1);
            const creat = Number(best[2]).toFixed(1);
            const glu = Number(best[3]).toFixed(1);
            const wbc = Number(best[5]).toFixed(1);
            const lym = Number(best[6]).toFixed(1);
            const mcv = Number(best[7]).toFixed(1);
            const rdw = Number(best[8]).toFixed(1);
            const alp = Number(best[9]).toFixed(1);
            if (label === 'Best domain – liver') return `Liver King: top liver profile (albumin ${alb} g/L, ALP ${alp} U/L)`;
            if (label === 'Best domain – kidney') return `Kidney Overlord: top kidney profile (creatinine ${creat} µmol/L)`;
            if (label === 'Best domain – metabolic') return `Metabolic Machine: top metabolic profile (glucose ${glu} mmol/L)`;
            if (label === 'Best domain – immune') return `Pathogen Punisher: top immune profile (WBC ${wbc} 10³ cells/µL, lymphocyte ${lym}%, MCV ${mcv} fL, RDW ${rdw}%)`;
        }
        if (label === 'Best domain – vitamin D') return 'Sun God: top vitamin D profile';
    }

    if (label === 'First applicants') {
        if (place === 1) return 'Athlete Zero: 1st athlete to join the Longevity World Cup';
        if (place === 2) return 'Athlete Beta: 2nd athlete to join the Longevity World Cup';
        if (place === 3) return 'Athlete Gamma: 3rd athlete to join the Longevity World Cup';
        if (place && place >= 4 && place <= 10) return 'Early Bird: among the first 10 athletes to join the Longevity World Cup';
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
    if (label === 'Pregnancy') return 'Baby on board';
    if (label === 'Host') return 'Host: organizer of the Longevity World Cup';
    if (label === 'Perfect application') return 'Perfect application: most flawless entry form ever submitted';

    return place ? `${label}: #${place}` : `${label}`;
}

function makeTooltipFromServerBadge(
    b: ServerBadge,
    athlete?: BadgeAthlete | null,
    opts?: BadgeTooltipOptions
): string {
    return capitalizeBadgeTooltipPrefix(makeUnformattedTooltipFromServerBadge(b, athlete, opts));
}

/* -------------------------------------------------
   ORDERING — match legacy: link → podcast → neutrals → medals
   ------------------------------------------------- */
function computeOrder(b: ServerBadge): number {
    const label = getLabel(b);
    const cat = (getCat(b) || '').toLowerCase();
    const place = getPlace(b);

    const isMedalFamily =
        label === 'Age reduction' ||
        isCrowdMedalLabel(label);

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

/* -------------------------------------------------
   RARITY: podium places are the badges worth collecting
   ------------------------------------------------- */
type BadgeRarity = 'legendary' | 'rare' | '';

function getBadgeRarity(b: ServerBadge): BadgeRarity {
    const label = getLabel(b);
    const place = getPlace(b);
    if (!Number.isInteger(place) || place === null || place < 1 || place > 3) return '';
    const ranked =
        label === 'Age reduction' ||
        isCrowdMedalLabel(label) ||
        isSeasonBadgeLabel(label) ||
        label === 'First applicants' ||
        label === 'Chronological age – oldest' ||
        label === 'Chronological age – youngest' ||
        label === 'Pheno Age – lowest' ||
        label === 'Bortz Age – lowest' ||
        label === 'Pheno pace of aging' ||
        label === 'Bortz pace of aging';
    if (!ranked) return '';
    return place === 1 ? 'legendary' : 'rare';
}

function rarityAttr(b: ServerBadge): string {
    const rarity = getBadgeRarity(b);
    return rarity ? ` data-badge-rarity="${rarity}"` : '';
}

// Build one server badge bubble HTML
function buildServerBadgeHtml(b: ServerBadge, athlete: BadgeAthlete): RenderedBadgeItem {
    const icon = pickIconForServerBadge(b);
    const tooltip = makeTooltipFromServerBadge(b, athlete);
    const style = pickBackgroundForServerBadge(b);
    const url = pickClickUrl(b, athlete);
    const order = computeOrder(b);
    const familyClass = getBadgeFamilyClass(b);
    const label = getLabel(b);
    const typeClass = label === 'Podcast' ? 'badge-podcast' : '';
    const className = `badge-class ${familyClass}${typeClass ? ` ${typeClass}` : ''}`;
    const rarity = rarityAttr(b);

    if (url && label === 'Podcast') {
        return {
            order,
            searchText: tooltip,
            html: `<a class="${className} badge-clickable" href="${escapeAttr(url)}" target="_blank" rel="noopener" aria-label="${escapeAttr(tooltip)}. Open podcast" title="${escapeAttr(tooltip)}" style="${styleWithBadgeVars(style)}"${rarity}><i class="fa ${icon}" aria-hidden="true"></i></a>`
        };
    }

    return {
        order,
        searchText: tooltip,
        html: url
            ? `<a class="${className} badge-clickable" href="${escapeAttr(url)}" aria-label="${escapeAttr(tooltip)}. Open league" title="${escapeAttr(tooltip)}" style="${styleWithBadgeVars(style)}"${rarity}><i class="fa ${icon}" aria-hidden="true"></i></a>`
            : `<span class="${className} badge-explained" role="button" aria-haspopup="dialog" tabindex="0" aria-label="${escapeAttr(tooltip)}" title="${escapeAttr(tooltip)}" style="${styleWithBadgeVars(style)}"${rarity}><i class="fa ${icon}" aria-hidden="true"></i></span>`
    };
}

interface StashedBadge { readonly text: string; readonly html: string; }
const overflowStash = new WeakMap<HTMLElement, readonly StashedBadge[]>();

/* =========================
   Public API
   ========================= */
window.setBadges = function (athlete, athleteCell) {
    let badgeContainer: HTMLElement | null = null;
    if (athleteCell && athleteCell.classList && athleteCell.classList.contains('badge-section')) {
        badgeContainer = athleteCell;
    } else if (athleteCell && typeof athleteCell.querySelector === 'function') {
        badgeContainer = athleteCell.querySelector('.badge-section') || athleteCell.querySelector('.badges');
    }
    if (!badgeContainer) return;

    const items: RenderedBadgeItem[] = [];
    const nameLinks: RenderedBadgeItem[] = [];
    const nameLinksContainer = athleteCell?.classList?.contains('athlete-td')
        ? athleteCell.querySelector<HTMLElement>('.athlete-name-links')
        : null;

    const href = window.normalizeWebUrl(String(athlete.personalLink || athlete.PersonalLink || ''));
    if (href) {
        (nameLinksContainer ? nameLinks : items).push({
            order: 0,
            searchText: 'Personal page',
            html: `<a class="badge-class badge-family-utility badge-personal badge-clickable" href="${escapeAttr(href)}" target="_blank" rel="noopener" aria-label="Open personal page"
               title="Personal page" style="${LEGACY_BG.personal}">
               <i class="fa fa-link"></i>
             </a>`
        });
    }

    const serverBadges =
        Array.isArray(athlete.Badges) ? athlete.Badges :
            Array.isArray(athlete.badges) ? athlete.badges : [];

    serverBadges.forEach(b => {
        const item = buildServerBadgeHtml(b, athlete);
        const isNameLink = nameLinksContainer && getLabel(b) === 'Podcast' && pickClickUrl(b, athlete);
        (isNameLink ? nameLinks : items).push(item);
    });

    if (nameLinksContainer) {
        nameLinksContainer.innerHTML = nameLinks.sort((a, b) => a.order - b.order).map(item => item.html).join('');
    }

    try {
        const slug = getAthleteProfileSlug(athlete);
        if (slug) {
            // A bullseye belongs to the portrait the player actually judged.
            // Historical exact guesses still count toward the lifetime Pro
            // discount, but must not decorate a replacement portrait.
            const g = window.LwcGuessState?.get(athlete);
            const guessed = (isRecord(g) && g.value != null) ? parseInt(String(g.value), 10) : null;

            const chrono = athlete.chronologicalAge ?? athlete.ChronoAge ?? null;
            const actualInt = Number.isFinite(chrono) ? parseInt(String(chrono), 10) : null;

            if (guessed !== null && actualInt !== null
                && Number.isInteger(guessed)
                && Number.isInteger(actualInt)
                && (guessed - actualInt === 0)) {
                if (window.proDiscounts && typeof window.proDiscounts.setPerfectGuessMarker === 'function') {
                    window.proDiscounts.setPerfectGuessMarker();
                }
                items.push({
                    order: 1.194,
                    searchText: 'Bullseye: you guessed their age perfectly!',
                    html: `<span class="badge-class badge-family-utility badge-explained" role="button" aria-haspopup="dialog" tabindex="0" aria-label="Bullseye: you guessed their age perfectly!" title="Bullseye: you guessed their age perfectly!" style="${LEGACY_BG.black}">
                               <i class="fa fa-bullseye" aria-hidden="true"></i>
                           </span>`
                });
            }
        }
    } catch {}

    items.sort((a, b) => a.order - b.order);
    const isModalStrip =
        (badgeContainer.id && badgeContainer.id === 'modalBadgeStrip') ||
        (typeof badgeContainer.closest === 'function' && !!badgeContainer.closest('#detailsModal'));

    let renderedItems = items;
    if (!isModalStrip && items.length > 4) {
        const visibleItems = items.slice(0, 3);
        const hiddenItems = items.slice(3);
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
            : `<span class="badge-class badge-family-utility badge-overflow-count badge-explained" role="button" aria-haspopup="dialog" tabindex="0" aria-label="${escapeAttr(overflowTitle)}" title="${escapeAttr(overflowTitle)}">+${hiddenItems.length}</span>`;

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

    // The +N tile keeps the badges it hides, so its explanation can lay them
    // out as a small tray of the actual badges instead of a bare list of names.
    const overflowTile = badgeContainer.querySelector<HTMLElement>('.badge-overflow-count');
    if (overflowTile && renderedItems !== items) {
        overflowStash.set(overflowTile, items.slice(3).map(x => ({ text: x.searchText, html: x.html })));
    }

    try {
        if (!isModalStrip) {
            Array.from(badgeContainer.children).forEach((badge: Element) => {
                if (badge instanceof HTMLElement) badge.style.animation = '';
            });
        }
    } catch {}
};

/* =========================
   Badge explanation popover
   =========================
   One popover explains every badge on every surface. Native titles appear late
   and in the OS style, and a CSS ::after tooltip is clipped by the badge's own
   shape, so the title is lifted into this popover while it is shown and put
   back afterwards. The profile shelf explains its badges in place instead. */
const BADGE_TIP_TARGET = '.badge-class[title], .badge-class[data-badge-tip]';
let badgeTip: HTMLDialogElement | null = null;
let badgeTipOwner: HTMLElement | null = null;
let badgeTipDismissTimer: number | undefined;
let badgeTipObserver: MutationObserver | null = null;
let badgeTipOriginalTitle: string | null = null;
let badgeTipOriginalData: string | null = null;
let badgeTipOriginalDescription: string | null = null;
let badgeTipTouchTarget: HTMLElement | null = null;
let badgeTipPointerDown = false;
let badgeTipHadOwnerFocus = false;
let badgeTipOwnerBounds: DOMRect | null = null;
let badgeTipChanging = false;
let badgeTipDismissedOwner: HTMLElement | null = null;
let badgeTipScrollLock: { element: HTMLElement; overflow: string }[] = [];

function cancelBadgeTipDismiss(): void {
    window.clearTimeout(badgeTipDismissTimer);
    badgeTipDismissTimer = undefined;
}

function getBadgeTipText(badge: HTMLElement): string {
    return badge.getAttribute('data-badge-tip') || badge.getAttribute('title') || '';
}

function ensureBadgeTip(): HTMLElement {
    if (badgeTip && badgeTip.isConnected) return badgeTip;
    badgeTip = document.createElement('dialog');
    badgeTip.className = 'badge-tip';
    badgeTip.setAttribute('role', 'tooltip');
    badgeTip.id = 'badge-tip';
    badgeTip.hidden = true;
    badgeTip.addEventListener('cancel', event => { event.preventDefault(); hideBadgeTip(true); });
    badgeTip.addEventListener('click', event => {
        if (badgeTip && event.target === badgeTip && isBadgeSheet()) {
            const r = badgeTip.getBoundingClientRect();
            if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) hideBadgeTip(true);
        }
    });
    document.body.appendChild(badgeTip);
    return badgeTip;
}

type BadgeTipMode = 'peek' | 'card';
let badgeTipMode: BadgeTipMode = 'peek';
const badgeSheetQuery = window.matchMedia('(max-width: 640px)');
const badgeReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
// The card's medallion hangs over its top edge by this much.
const BADGE_CARD_OVERHANG = 40;

function badgeRarityOf(el: Element): string {
    return el.getAttribute('data-badge-rarity') || '';
}

function makeBadgeMedal(source: Element, extraClass: string): HTMLElement {
    const medal = document.createElement('span');
    const keep = Array.from(source.classList).filter(c => c === 'badge-class' || c.startsWith('badge-family-'));
    medal.className = [...keep, extraClass].join(' ');
    const style = source.getAttribute('style');
    if (style) medal.setAttribute('style', style);
    const rarity = badgeRarityOf(source);
    if (rarity) medal.dataset['badgeRarity'] = rarity;
    medal.setAttribute('aria-hidden', 'true');
    const icon = source.querySelector('i');
    if (icon) medal.appendChild(icon.cloneNode(true));
    return medal;
}

function makeBadgeTipRow(line: string, index: number): HTMLElement {
    const row = document.createElement('span');
    row.className = 'badge-tip-row';
    row.style.setProperty('--row-i', String(index));
    const colon = line.indexOf(':');
    const title = document.createElement('strong');
    title.className = 'badge-tip-title';
    title.textContent = colon > 0 ? line.slice(0, colon) : line;
    row.appendChild(title);
    if (colon > 0) {
        const detail = document.createElement('span');
        detail.className = 'badge-tip-detail';
        detail.textContent = line.slice(colon + 1).trim();
        row.appendChild(detail);
    }
    return row;
}

function fillBadgeTip(tip: HTMLElement, text: string, owner: HTMLElement, mode: BadgeTipMode): void {
    const stash = overflowStash.get(owner);
    const rarity = badgeRarityOf(owner);
    tip.classList.toggle('is-card', mode === 'card' && !stash);
    tip.classList.toggle('has-tray', !!stash);
    if (rarity) tip.dataset['rarity'] = rarity;
    else delete tip.dataset['rarity'];

    const parts: HTMLElement[] = [];
    if (mode === 'card' && !stash) {
        // The unlock stage: the badge itself, larger, minted again in front of light rays.
        const stage = document.createElement('div');
        stage.className = 'badge-tip-stage';
        stage.setAttribute('aria-hidden', 'true');
        const rays = document.createElement('span');
        rays.className = 'badge-tip-rays';
        stage.append(rays, makeBadgeMedal(owner, 'badge-tip-medal'));
        parts.push(stage);
    }

    const content = document.createElement('div');
    content.className = 'badge-tip-content';
    if (stash) {
        // An overflow tile expands into the badges it stands for.
        stash.forEach((entry, index) => {
            const template = document.createElement('template');
            template.innerHTML = entry.html.trim();
            const source = template.content.firstElementChild;
            const row = makeBadgeTipRow(entry.text, index);
            row.classList.add('has-medal');
            if (source) row.prepend(makeBadgeMedal(source, 'badge-tip-mini'));
            content.appendChild(row);
        });
    } else {
        text.split('\n').filter(Boolean).forEach((line, index) => content.appendChild(makeBadgeTipRow(line, index)));
    }
    parts.push(content);
    if (mode === 'card') {
        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'badge-tip-close';
        close.setAttribute('aria-label', 'Close badge details');
        close.textContent = '×';
        close.addEventListener('click', () => hideBadgeTip(true));
        parts.push(close);
        content.tabIndex = 0;
        content.setAttribute('role', 'region');
        content.setAttribute('aria-label', owner.classList.contains('badge-overflow-count') ? text : text.split(':')[0] || text);
    }
    tip.replaceChildren(...parts);
}

function isBadgeSheet(): boolean {
    return badgeTipMode === 'card' && badgeSheetQuery.matches;
}

function setBadgeScrollLock(locked: boolean): void {
    if (locked && !badgeTipScrollLock.length) {
        badgeTipScrollLock = [document.documentElement, document.body].map(element => ({ element, overflow: element.style.overflow }));
        badgeTipScrollLock.forEach(({ element }) => { element.style.overflow = 'hidden'; });
    } else if (!locked) {
        badgeTipScrollLock.forEach(({ element, overflow }) => { element.style.overflow = overflow; });
        badgeTipScrollLock = [];
    }
}

function placeBadgeTip(tip: HTMLElement, badge: HTMLElement): void {
    if (isBadgeSheet()) {
        // On a phone the card is a bottom sheet: the thumb that tapped stays near it.
        tip.dataset['side'] = 'sheet';
        tip.style.left = '';
        tip.style.top = '';
        tip.style.maxHeight = '';
        tip.style.maxWidth = '';
        return;
    }
    const overhang = tip.classList.contains('is-card') ? BADGE_CARD_OVERHANG : 0;
    const gap = 10;
    const edge = 8;
    const viewportWidth = document.documentElement.clientWidth;
    const preferredWidth = tip.classList.contains('has-tray') ? '22rem' : tip.classList.contains('is-card') ? '17.5rem' : '20rem';
    tip.style.maxWidth = `min(${preferredWidth}, ${Math.max(0, viewportWidth - edge * 2)}px)`;
    const rect = badge.getBoundingClientRect();
    tip.style.maxHeight = '';
    // Entrance transforms change painted bounds, never the layout box we fit.
    let tipRect = { width: tip.offsetWidth, height: tip.offsetHeight };
    const aboveRoom = rect.top - gap - edge - overhang;
    const belowRoom = window.innerHeight - rect.bottom - gap - edge - overhang;
    const below = tipRect.height > aboveRoom && belowRoom > aboveRoom;
    // A long overflow list scrolls beside its owner instead of covering the
    // badge during touch activation or growing the document under the pointer.
    tip.style.maxHeight = `${Math.max(32, below ? belowRoom : aboveRoom)}px`;
    tipRect = { width: tip.offsetWidth, height: tip.offsetHeight };
    const center = rect.left + rect.width / 2;
    const left = Math.min(Math.max(center - tipRect.width / 2, edge), viewportWidth - tipRect.width - edge);
    const top = Math.max(edge + overhang, Math.min(below ? rect.bottom + gap + overhang : rect.top - tipRect.height - gap,
        window.innerHeight - tipRect.height - edge));
    tip.dataset['side'] = below ? 'below' : 'above';
    tip.style.left = `${Math.round(left)}px`;
    tip.style.top = `${Math.round(top)}px`;
    tip.style.setProperty('--badge-tip-arrow', `${Math.round(Math.max(12, Math.min(center - left, tipRect.width - 12)))}px`);
    // The card grows out of the badge that was chosen.
    tip.style.setProperty('--badge-tip-origin', `${Math.round(center - left)}px ${below ? '0' : '100%'}`);
}

function openBadgeCard(tip: HTMLElement, badge: HTMLElement, text: string): void {
    badgeTipChanging = true;
    badgeTipMode = 'card';
    fillBadgeTip(tip, text, badge, 'card');
    tip.setAttribute('role', 'dialog');
    tip.setAttribute('aria-hidden', 'false');
    tip.setAttribute('aria-label', overflowStash.has(badge) ? text.split('\n')[0] || text : text.split(':')[0] || text);
    tip.setAttribute('aria-modal', String(isBadgeSheet()));
    badge.setAttribute('aria-expanded', 'true');
    badge.setAttribute('aria-controls', tip.id);
    badge.removeAttribute('aria-describedby');
    const dialog = tip as HTMLDialogElement;
    // Place before native dialog focus can scroll an unpositioned control.
    placeBadgeTip(tip, badge);
    if (isBadgeSheet()) dialog.showModal();
    else dialog.show();
    setBadgeScrollLock(isBadgeSheet());
    placeBadgeTip(tip, badge);
    badge.classList.add('is-badge-tip-owner');
    tip.querySelector<HTMLElement>('.badge-tip-content')?.focus({ preventScroll: true });
    badgeTipChanging = false;
}

function leaveBadgeTip(tip: HTMLElement): void {
    // A short exit for the card: a detached copy fades out while the live
    // popover is already free for the next badge.
    if (badgeReducedMotion.matches || tip.hidden || !tip.classList.contains('is-card')) return;
    const ghost = tip.cloneNode(true) as HTMLElement;
    ghost.classList.replace('badge-tip', 'badge-tip-exit');
    ghost.removeAttribute('id');
    ghost.querySelectorAll('[id]').forEach(el => el.removeAttribute('id'));
    ghost.removeAttribute('aria-modal');
    ghost.removeAttribute('aria-label');
    ghost.setAttribute('role', 'presentation');
    ghost.inert = true;
    ghost.setAttribute('aria-hidden', 'true');
    ghost.classList.add('is-leaving');
    tip.after(ghost);
    const drop = () => ghost.remove();
    ghost.addEventListener('animationend', event => { if (event.target === ghost) drop(); });
    ghost.addEventListener('animationcancel', drop);
    window.setTimeout(drop, 400);
}

function showBadgeTip(badge: HTMLElement, mode: BadgeTipMode = 'peek'): void {
    if (mode === 'peek' && badge === badgeTipDismissedOwner) return;
    if (mode === 'card') badgeTipDismissedOwner = null;
    if (badgeTipChanging || (mode === 'peek' && badgeTipMode === 'card')) return;
    cancelBadgeTipDismiss();
    if (badgeTipOwner === badge) {
        badgeTipHadOwnerFocus ||= badge.contains(document.activeElement);
        // A hover peek becomes the full card when the badge is chosen.
        if (mode === 'card' && badgeTipMode !== 'card' && badgeTip) {
            openBadgeCard(badgeTip, badge, getBadgeTipText(badge));
        }
        return;
    }
    const text = getBadgeTipText(badge);
    if (!text || !badge.isConnected) return;
    hideBadgeTip();
    badgeTipOriginalTitle = badge.getAttribute('title');
    badgeTipOriginalData = badge.getAttribute('data-badge-tip');
    badgeTipOriginalDescription = badge.getAttribute('aria-describedby');
    const title = badge.getAttribute('title');
    if (title) {
        badge.setAttribute('data-badge-tip', title);
        badge.removeAttribute('title');
    }
    const tip = ensureBadgeTip();
    badgeTipOwner = badge;
    badgeTipHadOwnerFocus = badge.contains(document.activeElement);
    // Tooltips in an embedded athlete dialog must share its non-inert portal.
    const host = badge.closest('#detailsModal') || badge.closest('#athleteDialogRuntime') || document.body;
    if (tip.parentElement !== host) host.appendChild(tip);
    badgeTipMode = 'peek';
    tip.setAttribute('role', 'tooltip');
    tip.removeAttribute('aria-modal');
    tip.removeAttribute('aria-label');
    fillBadgeTip(tip, text, badge, 'peek');
    tip.hidden = false;
    if (mode === 'card') openBadgeCard(tip, badge, text);
    else placeBadgeTip(tip, badge);
    // Most badge names already include the explanation. Repeating it as an
    // accessible description makes screen readers announce the same text twice.
    const alreadyNamed = (badge.getAttribute('aria-label') || '').toLowerCase().includes(text.toLowerCase());
    if (mode === 'peek') {
        tip.setAttribute('aria-hidden', alreadyNamed ? 'true' : 'false');
        if (!alreadyNamed) badge.setAttribute('aria-describedby', [badgeTipOriginalDescription, tip.id].filter(Boolean).join(' '));
    }
    badgeTipOwner = badge;
    badgeTipHadOwnerFocus = badge.contains(document.activeElement);
    const ownerBounds = badgeTipOwnerBounds = badge.getBoundingClientRect();
    badgeTipObserver = new MutationObserver(() => {
        const bounds = badge.getBoundingClientRect();
        if (!badge.isConnected || !badge.getClientRects().length || badge.closest('[hidden], [inert]')
            || badge.getAttribute('title') !== null || getBadgeTipText(badge) !== text
            || (badgeTipMode === 'peek' && (Math.abs(bounds.left - ownerBounds.left) > 0.5 || Math.abs(bounds.top - ownerBounds.top) > 0.5
            || Math.abs(bounds.width - ownerBounds.width) > 0.5 || Math.abs(bounds.height - ownerBounds.height) > 0.5))) hideBadgeTip();
    });
    badgeTipObserver.observe(document.body, { childList: true, subtree: true, attributes: true,
        attributeFilter: ['title', 'data-badge-tip', 'hidden', 'inert', 'class', 'style'] });
}

function hideBadgeTip(restoreFocus = false): void {
    if (badgeTipChanging) return;
    badgeTipChanging = true;
    cancelBadgeTipDismiss();
    badgeTipObserver?.disconnect();
    badgeTipObserver = null;
    const owner = badgeTipOwner;
    if (restoreFocus) badgeTipDismissedOwner = owner?.matches(':hover') ? owner : null;
    const active = document.activeElement;
    const wasCard = badgeTipMode === 'card';
    if (badgeTip) {
        leaveBadgeTip(badgeTip);
        if (badgeTip.open) badgeTip.close();
        badgeTip.hidden = true;
    }
    setBadgeScrollLock(false);
    if (owner?.isConnected && owner.getClientRects().length && !owner.closest('[hidden], [inert]')
        && (badgeTip?.contains(active) || (restoreFocus && wasCard) || (restoreFocus && badgeTipHadOwnerFocus
            && (active === document.body || active === document.documentElement)))) {
        // Keep the owner set during focus restoration: focusin then sees the
        // same explanation instead of reopening a card that is being closed.
        owner.focus({ preventScroll: true });
    }
    badgeTipOwner = null;
    badgeTipOwnerBounds = null;
    badgeTipPointerDown = false;
    badgeTipHadOwnerFocus = false;
    badgeTipMode = 'peek';
    badgeTipChanging = false;
    if (!owner) return;
    owner.classList.remove('is-badge-tip-owner');
    owner.removeAttribute('aria-expanded');
    owner.removeAttribute('aria-controls');
    // Do not overwrite a title deliberately changed by a renderer while open.
    if (!owner.hasAttribute('title') && badgeTipOriginalTitle !== null) owner.setAttribute('title', badgeTipOriginalTitle);
    if (badgeTipOriginalData === null) owner.removeAttribute('data-badge-tip');
    else owner.setAttribute('data-badge-tip', badgeTipOriginalData);
    if (badgeTipOriginalDescription === null) owner.removeAttribute('aria-describedby');
    else owner.setAttribute('aria-describedby', badgeTipOriginalDescription);
}

function scheduleBadgeTipDismiss(): void {
    cancelBadgeTipDismiss();
    if (badgeTipMode === 'card') return;
    if (badgeTipPointerDown || badgeTipOwner?.contains(document.activeElement) || badgeTip?.contains(document.activeElement)) return;
    // Permit travel across the small arrow/gap to a hoverable explanation.
    badgeTipDismissTimer = window.setTimeout(hideBadgeTip, 140);
}

function isBadgeTipHovered(): boolean {
    return !!(badgeTipOwner?.matches(':hover') || badgeTip?.matches(':hover'));
}

function badgeTipTarget(target: EventTarget | null): HTMLElement | null {
    if (!(target instanceof Element)) return null;
    const badge = target.closest<HTMLElement>(BADGE_TIP_TARGET);
    if (!badge || badge.closest('#modalBadgeStrip')) return null;
    return badge;
}

if (!window.__lwcBadgeTipBound) {
    window.__lwcBadgeTipBound = true;
    document.addEventListener('pointerover', event => {
        if (event.pointerType === 'touch') return;
        if (event.target instanceof Node && badgeTip?.contains(event.target)) {
            cancelBadgeTipDismiss();
            return;
        }
        const badge = badgeTipTarget(event.target);
        if (badge && badge !== badgeTipOwner) showBadgeTip(badge);
        else if (badge) cancelBadgeTipDismiss();
        else if (badgeTipOwner) scheduleBadgeTipDismiss();
    });
    document.addEventListener('pointerout', event => {
        if (badgeTipDismissedOwner?.contains(event.target as Node)
            && !(event.relatedTarget instanceof Node && badgeTipDismissedOwner.contains(event.relatedTarget))) badgeTipDismissedOwner = null;
        if (!badgeTipOwner || event.pointerType === 'touch') return;
        const next = event.relatedTarget instanceof Node ? event.relatedTarget : null;
        if (next && (badgeTipOwner.contains(next) || badgeTip?.contains(next))) return;
        if (badgeTipTarget(event.target) === badgeTipOwner || (event.target instanceof Node && badgeTip?.contains(event.target))) scheduleBadgeTipDismiss();
    });
    document.addEventListener('focusin', event => {
        if (badgeTipChanging) return;
        if (event.target instanceof Node && badgeTip?.contains(event.target)) {
            cancelBadgeTipDismiss();
            badgeTip.setAttribute('aria-hidden', 'false');
            return;
        }
        const badge = badgeTipTarget(event.target);
        if (badge && badge !== badgeTipTouchTarget) showBadgeTip(badge);
        else if (badgeTipOwner && !badgeTipPointerDown && !isBadgeTipHovered()) hideBadgeTip();
    });
    document.addEventListener('focusout', event => {
        if (event.target === badgeTipDismissedOwner) badgeTipDismissedOwner = null;
        if (badgeTipChanging || badgeTipMode === 'card') return;
        if (!badgeTipOwner || !(event.target === badgeTipOwner || (event.target instanceof Node && badgeTip?.contains(event.target)))) return;
        const next = event.relatedTarget instanceof Node ? event.relatedTarget : null;
        if (next && (badgeTipOwner.contains(next) || badgeTip?.contains(next))) return;
        if (!badgeTipPointerDown && !isBadgeTipHovered()) hideBadgeTip();
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Tab' && badgeTipMode === 'card' && badgeTip?.contains(event.target as Node)) {
            const controls = Array.from(badgeTip.querySelectorAll<HTMLElement>('[tabindex="0"], button'));
            const index = controls.indexOf(document.activeElement as HTMLElement);
            if (isBadgeSheet()) {
                event.preventDefault();
                controls[(index + (event.shiftKey ? -1 : 1) + controls.length) % controls.length]?.focus();
            }
            // The enclosing athlete dialog must not steal focus from its child.
            event.stopPropagation();
        }
        if (event.key === 'Escape' && badgeTipOwner) {
            event.preventDefault();
            event.stopPropagation();
            hideBadgeTip(true);
        }
    }, true);
    // Enter or Space on a focused badge opens the full card, as a tap does.
    document.addEventListener('keydown', event => {
        if (event.key !== 'Enter' && event.key !== ' ') return;
        const badge = badgeTipTarget(event.target);
        if (!badge || badge.matches('a')) return;
        event.preventDefault();
        event.stopPropagation();
        showBadgeTip(badge, 'card');
    }, true);
    document.addEventListener('pointerdown', event => {
        badgeTipPointerDown = event.target instanceof Node && !!badgeTip?.contains(event.target);
        badgeTipTouchTarget = event.pointerType === 'touch' ? badgeTipTarget(event.target) : null;
    });
    document.addEventListener('pointerup', event => {
        // Touch activation can focus during the compatibility click after up.
        if (event.pointerType === 'touch') return;
        badgeTipPointerDown = false;
        if (badgeTipOwner && !isBadgeTipHovered()) scheduleBadgeTipDismiss();
    });
    document.addEventListener('pointercancel', () => { badgeTipTouchTarget = null; badgeTipPointerDown = false; });
    // Show touch explanations after activation so a focus tooltip cannot
    // intercept the pointerup/click that is still on its way to the badge.
    document.addEventListener('click', event => {
        const badge = badgeTipTarget(event.target);
        if (badge && !badge.matches('a')) showBadgeTip(badge, 'card');
        else if (!badge && !(event.target instanceof Node && badgeTip?.contains(event.target))) hideBadgeTip(true);
        badgeTipTouchTarget = null;
        badgeTipPointerDown = false;
    });
    window.addEventListener('scroll', event => {
        if (!badgeTipOwner || isBadgeSheet() || (event.target instanceof Node && badgeTip?.contains(event.target))) return;
        // A scroll queued before pointerover can arrive after the card opens.
        // Dismiss only when its anchor has actually moved from the shown position.
        const bounds = badgeTipOwner.getBoundingClientRect();
        if (!badgeTipOwnerBounds || Math.abs(bounds.left - badgeTipOwnerBounds.left) > 0.5
            || Math.abs(bounds.top - badgeTipOwnerBounds.top) > 0.5) hideBadgeTip();
    }, { capture: true, passive: true });
    window.addEventListener('resize', () => {
        if (!badgeTipOwner || !badgeTip) return;
        if (isBadgeSheet() && badgeTip.dataset['side'] === 'sheet') placeBadgeTip(badgeTip, badgeTipOwner);
        else hideBadgeTip(true);
    }, { passive: true });
    window.addEventListener('pagehide', () => hideBadgeTip());
}

/* =========================
   Collectible light
   =========================
   A pointer over a badge moves a soft highlight across its face and nudges
   the glyph the other way, so the badge reads as a struck coin, not a sticker.
   Only custom properties change; the badge box never moves. */
let badgeLightTarget: HTMLElement | null = null;
let badgeLightEvent: PointerEvent | null = null;
let badgeLightFrame = 0;

function clearBadgeLight(): void {
    const target = badgeLightTarget;
    if (!target) return;
    ['--badge-mx', '--badge-my', '--badge-px', '--badge-py'].forEach(name => target.style.removeProperty(name));
    badgeLightTarget = null;
}

function applyBadgeLight(): void {
    badgeLightFrame = 0;
    const event = badgeLightEvent;
    if (!event || badgeReducedMotion.matches) return;
    const badge = event.target instanceof Element ? event.target.closest<HTMLElement>('.badge-class') : null;
    if (badge !== badgeLightTarget) clearBadgeLight();
    if (!badge || badge.classList.contains('badge-overflow-count') || badge.closest('.athlete-name-links')) return;
    const rect = badge.getBoundingClientRect();
    if (!rect.width || !rect.height) return;
    const x = Math.min(1, Math.max(0, (event.clientX - rect.left) / rect.width));
    const y = Math.min(1, Math.max(0, (event.clientY - rect.top) / rect.height));
    badge.style.setProperty('--badge-mx', `${(x * 100).toFixed(1)}%`);
    badge.style.setProperty('--badge-my', `${(y * 100).toFixed(1)}%`);
    badge.style.setProperty('--badge-px', `${((0.5 - x) * 3).toFixed(2)}px`);
    badge.style.setProperty('--badge-py', `${((0.5 - y) * 3).toFixed(2)}px`);
    badgeLightTarget = badge;
}

if (!window.__lwcBadgeLightBound) {
    window.__lwcBadgeLightBound = true;
    document.addEventListener('pointermove', event => {
        if (event.pointerType === 'touch' || badgeReducedMotion.matches) return;
        badgeLightEvent = event;
        if (!badgeLightFrame) badgeLightFrame = window.requestAnimationFrame(applyBadgeLight);
    }, { passive: true });
    document.addEventListener('pointerleave', () => { window.cancelAnimationFrame(badgeLightFrame); badgeLightFrame = 0; badgeLightEvent = null; clearBadgeLight(); });
}

/* =========================
   Newly earned
   =========================
   An unseen award arriving in the feed receives a short mint:
   it pops in, a ring rolls off it and a glint crosses its face. Each event
   celebrates once per visit. */
const BADGE_CELEBRATED_KEY = 'lwcBadgeCelebrated:v1';
const celebrated = new Set<string>();
try {
    const saved: unknown = JSON.parse(sessionStorage.getItem(BADGE_CELEBRATED_KEY) || '[]');
    if (Array.isArray(saved)) saved.filter((key): key is string => typeof key === 'string').forEach(key => celebrated.add(key));
} catch { /* The in-memory set still deduplicates feed refreshes. */ }
const badgeCelebrations = new Map<HTMLElement, number>();

function stopBadgeCelebration(badge: HTMLElement): void {
    window.clearTimeout(badgeCelebrations.get(badge));
    badgeCelebrations.delete(badge);
    badge.classList.remove('badge-earned');
    badge.style.removeProperty('--badge-i');
}

function celebrateBadges(scope: Element | null | undefined): void {
    // Only the feed's actual unseen/arrival path may call this. The renderer
    // identifies gained awards from event data, never from dates or prose.
    const row = scope?.closest<HTMLElement>('tr[data-badge-earned="true"]');
    const key = row?.dataset['eventId'];
    if (!key || celebrated.has(key)) return;
    celebrated.add(key);
    try { sessionStorage.setItem(BADGE_CELEBRATED_KEY, JSON.stringify([...celebrated])); } catch {}
    if (badgeReducedMotion.matches) return;
    const badges = Array.from(row.querySelectorAll<HTMLElement>('.event-message-cell .badge-class:not(.badge-overflow-count)'));
    badges.forEach((badge, index) => {
        stopBadgeCelebration(badge);
        badge.style.setProperty('--badge-i', String(index));
        badge.classList.add('badge-earned');
        badgeCelebrations.set(badge, window.setTimeout(() => stopBadgeCelebration(badge), 1500 + index * 90));
    });
}

function clearBadgeMotion(): void {
    window.cancelAnimationFrame(badgeLightFrame);
    badgeLightFrame = 0;
    badgeLightEvent = null;
    clearBadgeLight();
    badgeCelebrations.forEach((_, badge) => stopBadgeCelebration(badge));
    document.querySelectorAll('.badge-tip-exit').forEach(el => el.remove());
}

if (!window.LwcBadges) {
    window.LwcBadges = {
        celebrate: celebrateBadges,
        rarity: getBadgeRarity,
        stashOverflow(owner, html) {
            const template = document.createElement('template');
            template.innerHTML = html;
            overflowStash.set(owner, Array.from(template.content.children).map(el => ({
                text: el.getAttribute('title') || '', html: el.outerHTML
            })));
        }
    };
    badgeReducedMotion.addEventListener('change', () => { if (badgeReducedMotion.matches) clearBadgeMotion(); });
    window.addEventListener('pagehide', clearBadgeMotion);
}

window.computeBadges = window.computeBadges || function () {};
window.pickIconForServerBadge = pickIconForServerBadge;
window.pickBackgroundForServerBadge = pickBackgroundForServerBadge;
window.makeTooltipFromServerBadge = makeTooltipFromServerBadge;
window.pickClickUrl = pickClickUrl;
window.getBadgeFamilyClass = getBadgeFamilyClass;
window.getBadgeRarity = getBadgeRarity;
window.styleWithBadgeVars = styleWithBadgeVars;

export {};
