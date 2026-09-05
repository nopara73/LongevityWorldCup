(() => {
    if (window.__lwcPlayFlowScrollInitialized) return;
    window.__lwcPlayFlowScrollInitialized = true;

    function isPlayFlowRoute() {
        return !!document.body?.classList.contains('play-flow-route');
    }

    function setManualFlowScrollRestoration() {
        if (!isPlayFlowRoute()) return;

        try {
            if (window.history) {
                window.history.scrollRestoration = 'manual';
            }
        } catch (_) {
        }
    }

    function resetPlayFlowScroll() {
        if (!isPlayFlowRoute()) return;
        if (window.location.hash) return;

        setManualFlowScrollRestoration();

        window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
        window.requestAnimationFrame(() => {
            window.scrollTo({ top: 0, left: 0, behavior: 'auto' });
            window.LwcFlowActionDock?.refresh?.();
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', resetPlayFlowScroll, { once: true });
    } else {
        resetPlayFlowScroll();
    }

    window.addEventListener('pageshow', event => {
        if (event.persisted) {
            resetPlayFlowScroll();
        } else {
            setManualFlowScrollRestoration();
        }
    });
})();

window.parseMediaContact = function (link) {
    let value = String(link || '').trim();
    if (!value) return null;

    // A bare handle does not identify which social platform owns it.
    if (value.startsWith('@')) return { href: null, isEmail: false, hostname: '' };

    // A handle inside a web URL is not an email address.
    if (/^[^\s@/:?#]+@[^\s@/:?#]+\.[^\s@/:?#]+$/.test(value)) {
        value = `mailto:${value}`;
    }

    const hasScheme = /^[a-z][a-z\d+.-]*:/i.test(value)
        && !/^[^/?#]+:\d+(?:[/?#]|$)/.test(value);
    try {
        const url = new URL(hasScheme ? value : `https://${value.replace(/^\/\//, '')}`);
        if (!['http:', 'https:', 'mailto:'].includes(url.protocol)) return null;
        return {
            href: url.href,
            isEmail: url.protocol === 'mailto:',
            hostname: url.hostname.toLowerCase().replace(/\.$/, '')
        };
    } catch (_) {
        return null;
    }
};

window.getIcon = function (link) {
    const contact = window.parseMediaContact(link);
    if (contact?.isEmail) return '<i class="fas fa-envelope"></i>';
    if (contact && !contact.href) return '<i class="fas fa-at"></i>';

    // Mapping of link identifiers to Font Awesome icon classes
    const iconMap = {
        'facebook.com': '<i class="fab fa-facebook"></i>',
        'twitter.com': '<i class="fab fa-twitter"></i>',
        'x.com': '<i class="fab fa-twitter"></i>',
        'linkedin.com': '<i class="fab fa-linkedin"></i>',
        'instagram.com': '<i class="fab fa-instagram"></i>',
        'youtube.com': '<i class="fab fa-youtube"></i>',
        'github.com': '<i class="fab fa-github"></i>',
        'gitlab.com': '<i class="fab fa-gitlab"></i>',
        'bitbucket.org': '<i class="fab fa-bitbucket"></i>',
        'paypal.com': '<i class="fab fa-paypal"></i>',
        'stackoverflow.com': '<i class="fab fa-stack-overflow"></i>',
        'medium.com': '<i class="fab fa-medium"></i>',
        'reddit.com': '<i class="fab fa-reddit"></i>',
        'tumblr.com': '<i class="fab fa-tumblr"></i>',
        'pinterest.com': '<i class="fab fa-pinterest"></i>',
        'snapchat.com': '<i class="fab fa-snapchat"></i>',
        'whatsapp.com': '<i class="fab fa-whatsapp"></i>',
        'telegram.org': '<i class="fab fa-telegram"></i>',
        'discord.com': '<i class="fab fa-discord"></i>',
        'slack.com': '<i class="fab fa-slack"></i>',
        'dribbble.com': '<i class="fab fa-dribbble"></i>',
        'behance.net': '<i class="fab fa-behance"></i>',
        'flickr.com': '<i class="fab fa-flickr"></i>',
        'spotify.com': '<i class="fab fa-spotify"></i>',
        'vimeo.com': '<i class="fab fa-vimeo"></i>',
        'tiktok.com': '<i class="fab fa-tiktok"></i>',
        'skype.com': '<i class="fab fa-skype"></i>',
        'wordpress.org': '<i class="fab fa-wordpress"></i>',
        'amazon.com': '<i class="fab fa-amazon"></i>',
        'google.com': '<i class="fab fa-google"></i>',
        'apple.com': '<i class="fab fa-apple"></i>',
        'microsoft.com': '<i class="fab fa-microsoft"></i>',
        'npmjs.com': '<i class="fab fa-npm"></i>',
        'bitly.com': '<i class="fas fa-link"></i>', // Bitly doesn't have a specific icon
        // Add more mappings as needed
    };

    // Match the actual host, including subdomains, rather than URL paths or queries.
    for (const [key, icon] of Object.entries(iconMap)) {
        if (contact?.hostname === key || contact?.hostname.endsWith(`.${key}`)) {
            return icon;
        }
    }

    // Return a generic link icon if no match is found
    return '<i class="fas fa-link"></i>';
}

window.slugifyName = function (name, encode) {
    // Normalize the string: trim, lowercase, remove accents,
    // replace spaces with hyphens, remove any disallowed characters,
    // collapse multiple hyphens and trim hyphens from start/end.
    let normalized = name
        .trim()
        .toLowerCase()
        .normalize('NFKD')
        .replace(/[\u0300-\u036f]/g, '')
        .replace(/\s+/g, '-')
        .replace(/[^a-z0-9\-]/g, '')
        .replace(/-+/g, '-')
        .replace(/^-|-$/g, '');

    // If encoding is desired, run encodeURIComponent.
    // If decoding, first decode then re-normalize (or simply return the normalized value)
    if (encode) {
        return encodeURIComponent(normalized);
    } else {
        // In our case the normalized string should be safe already;
        // calling decodeURIComponent won’t change it unless there are percent sequences.
        return decodeURIComponent(normalized);
    }
}

const GUESS_MY_AGE_STORAGE_KEY = 'gmaAllGuesses';
let guessStateMemoryStore: Record<string, unknown> = {};
let guessStateStorageUnavailable = false;

interface GuessStateIdentity {
    readonly canonicalSlug: string;
    readonly aliasSlugs: readonly string[];
    readonly profileImageId: string;
}

interface GuessStateMigration {
    readonly canonicalSlug: string;
    readonly profileImageId: string;
    readonly state: unknown;
    readonly changed: boolean;
}

interface StoredGuessHistory {
    byImage: Record<string, unknown>;
}

function firstGuessStateString(values: readonly unknown[]): string {
    for (const value of values) {
        if (typeof value === 'string' && value.trim()) {
            return value;
        }
    }
    return '';
}

function normalizeGuessStateSlug(value: unknown): string {
    const routeSlug = String(value || '').replace(/_/g, '-');
    return routeSlug ? window.slugifyName(routeSlug, true) : '';
}

function normalizeGuessStateProfileImageId(value: unknown): string {
    return typeof value === 'string' ? value.trim().toLowerCase() : '';
}

function getGuessStateIdentity(athleteOrSlug: unknown): GuessStateIdentity {
    if (!isRecord(athleteOrSlug)) {
        return {
            canonicalSlug: normalizeGuessStateSlug(athleteOrSlug),
            aliasSlugs: [],
            profileImageId: ''
        };
    }

    const explicitSlug = firstGuessStateString([
        athleteOrSlug.AthleteSlug,
        athleteOrSlug.athleteSlug,
        athleteOrSlug.Slug,
        athleteOrSlug.slug
    ]);
    const name = firstGuessStateString([athleteOrSlug.Name, athleteOrSlug.name]);
    const displayName = firstGuessStateString([
        athleteOrSlug.DisplayName,
        athleteOrSlug.displayName
    ]);
    const canonicalSlug = normalizeGuessStateSlug(explicitSlug || name || displayName);
    const aliasSlugs = [...new Set([name, displayName]
        .map(normalizeGuessStateSlug)
        .filter(slug => slug && slug !== canonicalSlug))];
    const profileImageId = normalizeGuessStateProfileImageId(
        athleteOrSlug.ProfileImageId ?? athleteOrSlug.profileImageId);

    return { canonicalSlug, aliasSlugs, profileImageId };
}

function readGuessStateStore(): Record<string, unknown> {
    if (guessStateStorageUnavailable) return guessStateMemoryStore;

    let raw: string | null;
    try {
        raw = localStorage.getItem(GUESS_MY_AGE_STORAGE_KEY);
    } catch (_) {
        guessStateStorageUnavailable = true;
        return guessStateMemoryStore;
    }

    try {
        const parsed: unknown = JSON.parse(raw || '{}');
        guessStateMemoryStore = isRecord(parsed) ? parsed : {};
    } catch (_) {
        guessStateMemoryStore = {};
    }
    return guessStateMemoryStore;
}

function writeGuessStateStore(store: Record<string, unknown>): void {
    // Keep this page's progress when storage is blocked or full. A failed write
    // must not be replaced by stale persisted data on the next read.
    guessStateMemoryStore = store;
    if (guessStateStorageUnavailable) return;
    try {
        localStorage.setItem(GUESS_MY_AGE_STORAGE_KEY, JSON.stringify(store));
    } catch (_) {
        guessStateStorageUnavailable = true;
    }
}

function guessStatePriority(state: unknown): number {
    if (!isRecord(state)) return 0;
    if (state.value != null) return 3;
    if (state.skipped === true) return 2;
    return 1;
}

function isStoredGuessHistory(value: unknown): value is StoredGuessHistory {
    return isRecord(value) && isRecord(value.byImage);
}

function isStoredGuessState(value: unknown): value is Record<string, unknown> {
    return isRecord(value) && (
        Object.prototype.hasOwnProperty.call(value, 'value') ||
        typeof value.skipped === 'boolean' ||
        typeof value.first === 'boolean' ||
        typeof value.exact === 'boolean');
}

function createGuessHistory(entry: unknown, profileImageId: string): StoredGuessHistory {
    if (isStoredGuessHistory(entry)) {
        return entry;
    }

    const history: StoredGuessHistory = { byImage: {} };
    if (profileImageId && isStoredGuessState(entry)) {
        history.byImage[profileImageId] = entry;
    }
    return history;
}

function mergeGuessHistory(
    target: StoredGuessHistory,
    source: StoredGuessHistory
): boolean {
    let changed = false;
    for (const [profileImageId, sourceState] of Object.entries(source.byImage)) {
        const targetState = target.byImage[profileImageId];
        if (!isStoredGuessState(targetState) ||
            guessStatePriority(sourceState) > guessStatePriority(targetState)) {
            target.byImage[profileImageId] = sourceState;
            changed = true;
        }
    }
    return changed;
}

function normalizeGuessHistoryImageIds(history: StoredGuessHistory): boolean {
    let changed = false;
    for (const rawProfileImageId of Object.keys(history.byImage)) {
        const profileImageId = normalizeGuessStateProfileImageId(rawProfileImageId);
        if (!profileImageId || profileImageId === rawProfileImageId) continue;

        const state = history.byImage[rawProfileImageId];
        const existingState = history.byImage[profileImageId];
        if (!isStoredGuessState(existingState) ||
            guessStatePriority(state) > guessStatePriority(existingState)) {
            history.byImage[profileImageId] = state;
        }
        delete history.byImage[rawProfileImageId];
        changed = true;
    }
    return changed;
}

function migrateGuessState(
    store: Record<string, unknown>,
    athleteOrSlug: unknown
): GuessStateMigration {
    const identity = getGuessStateIdentity(athleteOrSlug);
    if (!identity.canonicalSlug || !identity.profileImageId) {
        return {
            canonicalSlug: identity.canonicalSlug,
            profileImageId: identity.profileImageId,
            state: null,
            changed: false
        };
    }

    const hasOwn = (key: string) =>
        Object.prototype.hasOwnProperty.call(store, key);
    const hadCanonicalEntry = hasOwn(identity.canonicalSlug);
    const canonicalEntry = hadCanonicalEntry ? store[identity.canonicalSlug] : null;
    const history = createGuessHistory(canonicalEntry, identity.profileImageId);
    let changed = hadCanonicalEntry
        ? !isStoredGuessHistory(canonicalEntry)
        : false;
    changed = normalizeGuessHistoryImageIds(history) || changed;

    for (const aliasSlug of identity.aliasSlugs) {
        if (!hasOwn(aliasSlug)) continue;

        const aliasHistory = createGuessHistory(store[aliasSlug], identity.profileImageId);
        changed = normalizeGuessHistoryImageIds(aliasHistory) || changed;
        changed = mergeGuessHistory(history, aliasHistory) || changed;
        delete store[aliasSlug];
        changed = true;
    }

    if (hadCanonicalEntry || Object.keys(history.byImage).length > 0) {
        store[identity.canonicalSlug] = history;
    }

    return {
        canonicalSlug: identity.canonicalSlug,
        profileImageId: identity.profileImageId,
        state: history.byImage[identity.profileImageId] ?? null,
        changed
    };
}

window.LwcGuessState = {
    getAthleteSlug(athleteOrSlug) {
        return getGuessStateIdentity(athleteOrSlug).canonicalSlug;
    },
    getProfileImageId(athleteOrSlug) {
        return getGuessStateIdentity(athleteOrSlug).profileImageId;
    },
    get(athleteOrSlug) {
        const store = readGuessStateStore();
        const migration = migrateGuessState(store, athleteOrSlug);
        if (migration.changed) {
            writeGuessStateStore(store);
        }
        return isRecord(migration.state)
            ? migration.state as unknown as GuessMyAgeState
            : null;
    },
    set(athleteOrSlug, state) {
        const store = readGuessStateStore();
        const migration = migrateGuessState(store, athleteOrSlug);
        if (!migration.canonicalSlug || !migration.profileImageId) return;

        const history = createGuessHistory(store[migration.canonicalSlug], migration.profileImageId);
        history.byImage[migration.profileImageId] = state;
        store[migration.canonicalSlug] = history;
        writeGuessStateStore(store);
    },
    ensureSkipped(athleteOrSlug) {
        const store = readGuessStateStore();
        const migration = migrateGuessState(store, athleteOrSlug);
        if (!migration.canonicalSlug || !migration.profileImageId) return null;

        if (isRecord(migration.state)) {
            if (migration.changed) {
                writeGuessStateStore(store);
            }
            return migration.state as unknown as GuessMyAgeState;
        }

        const skippedState: GuessMyAgeState = {
            value: null,
            skipped: true,
            first: false,
            exact: false
        };
        const history = createGuessHistory(store[migration.canonicalSlug], migration.profileImageId);
        history.byImage[migration.profileImageId] = skippedState;
        store[migration.canonicalSlug] = history;
        writeGuessStateStore(store);
        return skippedState;
    },
    ensureSkippedMany(athletes) {
        if (!Array.isArray(athletes) || athletes.length === 0) return;

        const store = readGuessStateStore();
        let changed = false;
        for (const athlete of athletes) {
            const migration = migrateGuessState(store, athlete);
            changed = changed || migration.changed;
            if (!migration.canonicalSlug ||
                !migration.profileImageId ||
                isRecord(migration.state)) continue;

            const history = createGuessHistory(
                store[migration.canonicalSlug],
                migration.profileImageId);
            history.byImage[migration.profileImageId] = {
                value: null,
                skipped: true,
                first: false,
                exact: false
            };
            store[migration.canonicalSlug] = history;
            changed = true;
        }

        if (changed) {
            writeGuessStateStore(store);
        }
    },
    getAll() {
        const store = readGuessStateStore();
        const entries: GuessMyAgeStoredEntry[] = [];

        for (const [rawSlug, entry] of Object.entries(store)) {
            const athleteSlug = normalizeGuessStateSlug(rawSlug);
            if (!athleteSlug) continue;

            if (isStoredGuessHistory(entry)) {
                for (const [rawProfileImageId, state] of Object.entries(entry.byImage)) {
                    const profileImageId = normalizeGuessStateProfileImageId(rawProfileImageId);
                    if (!profileImageId || !isStoredGuessState(state)) continue;
                    entries.push({
                        athleteSlug,
                        profileImageId,
                        state: state as unknown as GuessMyAgeState
                    });
                }
            } else if (isStoredGuessState(entry)) {
                // Keep pre-image-versioning achievements discoverable until this
                // athlete is next loaded and the state can be bound to an image.
                entries.push({
                    athleteSlug,
                    profileImageId: null,
                    state: entry as unknown as GuessMyAgeState
                });
            }
        }

        return entries;
    }
};

function normalizeString(str: string): string {
    return str.normalize('NFKD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
}
window.normalizeString = normalizeString;

function escapeHTML(string: string | null): string {
    const div = document.createElement('div');
    div.textContent = string;
    return div.innerHTML;
}
window.escapeHTML = escapeHTML;

/**
 * Comparator function to rank athletes based on competition rules.
 * The ranking is determined by:
 * 1. bortz age presence (athletes with bortz age rank ahead of those with only pheno age)
 * 2. Age reduction (more negative value indicates greater reversal; uses Bortz when present, else Pheno)
 * 3. Chronological age (older athletes rank higher)
 * 4. Alphabetical Order (as a last resort)
 */
window.compareAthleteRank = function (a, b) {
    const hasBortz = function (x: RankedAthlete): boolean {
        return typeof x.bortzAgeReduction === 'number' && Number.isFinite(x.bortzAgeReduction);
    };
    const getAgeReduction = function (x: RankedAthlete): number {
        const value = hasBortz(x) ? x.bortzAgeReduction : x.ageReduction;
        return Number(value);
    };

    // First criterion: athletes with bortz age rank ahead of those with only pheno age
    const aHasBortz = hasBortz(a);
    const bHasBortz = hasBortz(b);
    if (aHasBortz && !bHasBortz) return -1; // 'a' ranks higher
    if (!aHasBortz && bHasBortz) return 1;  // 'b' ranks higher

    // Second criterion: age reduction (more negative is better)
    const aRed = getAgeReduction(a);
    const bRed = getAgeReduction(b);
    if (aRed < bRed) return -1; // Athlete 'a' ranks higher
    if (aRed > bRed) return 1;  // Athlete 'b' ranks higher

    // Third Criterion: Date of Birth
    // Older athletes rank higher when age reversal is equal.
    // Credit goes to Dave Pascoe for the idea of using date of birth as a tiebreaker.
    if (a.dateOfBirth < b.dateOfBirth) {
        return -1; // Athlete 'a' is older; ranks higher
    }
    if (a.dateOfBirth > b.dateOfBirth) {
        return 1; // Athlete 'b' is older; ranks higher
    }

    // Fourth Criterion: Alphabetical Order of Names
    // As a last resort, sort alphabetically by athlete's name.
    if (a.name < b.name) {
        return -1; // Athlete 'a' comes first alphabetically
    }
    if (a.name > b.name) {
        return 1; // Athlete 'b' comes first alphabetically
    }

    // If all criteria are equal, preserve original order (stable sort behavior).
    return 0;
};

/**
 * Pheno-only comparator: rank by pheno age reduction only (no Bortz-first rule).
 * Used for the Pheno Age view. Order: ageReduction (more negative better), then DoB (older first), then name.
 */
window.compareAthleteRankPhenoOnly = function (a, b) {
    const aRed = a.ageReduction != null && Number.isFinite(a.ageReduction) ? a.ageReduction : 0;
    const bRed = b.ageReduction != null && Number.isFinite(b.ageReduction) ? b.ageReduction : 0;
    if (aRed < bRed) return -1;
    if (aRed > bRed) return 1;
    if (a.dateOfBirth < b.dateOfBirth) return -1;
    if (a.dateOfBirth > b.dateOfBirth) return 1;
    if (a.name < b.name) return -1;
    if (a.name > b.name) return 1;
    return 0;
};

/**
 * Pheno improvement comparator: rank by latest eligible pheno age minus worst eligible pheno age.
 * More negative values are better. Ties follow the Pheno Age view ordering.
 */
function finiteNumberOr(value: number | null | undefined, fallback: number): number {
    return typeof value === 'number' && Number.isFinite(value) ? value : fallback;
}

window.compareAthleteRankPhenoImprovement = function (a, b) {
    const aImprovement = finiteNumberOr(a.phenoAgeImprovement, Infinity);
    const bImprovement = finiteNumberOr(b.phenoAgeImprovement, Infinity);
    if (aImprovement < bImprovement) return -1;
    if (aImprovement > bImprovement) return 1;
    return window.compareAthleteRankPhenoOnly(a, b);
};

/**
 * Bortz improvement comparator: rank by latest eligible bortz age minus worst eligible bortz age.
 * More negative values are better. Ties follow the Bortz Age view ordering.
 */
window.compareAthleteRankBortzImprovement = function (a, b) {
    const aImprovement = finiteNumberOr(a.bortzAgeImprovement, Infinity);
    const bImprovement = finiteNumberOr(b.bortzAgeImprovement, Infinity);
    if (aImprovement < bImprovement) return -1;
    if (aImprovement > bImprovement) return 1;
    return window.compareAthleteRank(a, b);
};

/**
 * crowd age comparator: lower median crowd age minus chronological age is better, with more guesses winning ties.
 * Used only for the Crowd Age view; Ultimate League ordering still follows compareAthleteRank.
 */
window.compareAthleteRankCrowdAge = function (a, b) {
    const aReduction = finiteNumberOr(a.crowdAgeReduction, -Infinity);
    const bReduction = finiteNumberOr(b.crowdAgeReduction, -Infinity);
    if (aReduction < bReduction) return -1;
    if (aReduction > bReduction) return 1;

    const aCount = finiteNumberOr(a.crowdCount, 0);
    const bCount = finiteNumberOr(b.crowdCount, 0);
    if (aCount > bCount) return -1;
    if (aCount < bCount) return 1;

    if (a.dateOfBirth < b.dateOfBirth) return -1;
    if (a.dateOfBirth > b.dateOfBirth) return 1;
    if (a.name < b.name) return -1;
    if (a.name > b.name) return 1;
    return 0;
};

window.getGeneration = function (birthYear) {
    if (birthYear >= 1928 && birthYear <= 1945) {
        return "Silent Generation";
    } else if (birthYear >= 1946 && birthYear <= 1964) {
        return "Baby Boomers";
    } else if (birthYear >= 1965 && birthYear <= 1980) {
        return "Gen X";
    } else if (birthYear >= 1981 && birthYear <= 1996) {
        return "Millennials";
    } else if (birthYear >= 1997 && birthYear <= 2012) {
        return "Gen Z";
    } else if (birthYear >= 2013) {
        return "Gen Alpha";
    } else {
        return "Unknown Generation";
    }
}

window.showWithDelay = function (element) {
    element.style.display = ''; // Remove display: none to make it visible
    element.classList.add('fade-in'); // Start fade-in transition

    setTimeout(() => {
        element.classList.remove('fade-in'); // Clean up fade-in class after transition
    }, 300); // Match the fade-out delay for smoothness
}

// Helper function to calculate age from date of birth at a specific date
window.calculateAgeAtDate = function (birthDate, atDate) {
    if (!(birthDate instanceof Date)) throw new Error("Invalid input: birthDate must be a Date object");
    if (!(atDate instanceof Date)) throw new Error("Invalid input: atDate must be a Date object");
    if (Number.isNaN(birthDate.getTime())) throw new Error("Invalid date of birth.");
    if (Number.isNaN(atDate.getTime())) throw new Error("Invalid blood draw date.");

    if (birthDate > atDate) throw new Error("Date of birth cannot be in the future.");

    // Calculate total days lived
    const msPerDay = 1000 * 60 * 60 * 24;
    const utc1 = Date.UTC(birthDate.getFullYear(), birthDate.getMonth(), birthDate.getDate());
    const utc2 = Date.UTC(atDate.getFullYear(), atDate.getMonth(), atDate.getDate());
    const totalDays = (utc2 - utc1) / msPerDay;

    // Keep calculation precision here. Callers round only when presenting the age.
    return totalDays / 365.2425;
}

window.calculateCompletedYearsAtDate = function (birthDate, atDate) {
    if (!(birthDate instanceof Date)) throw new Error("Invalid input: birthDate must be a Date object");
    if (!(atDate instanceof Date)) throw new Error("Invalid input: atDate must be a Date object");
    if (Number.isNaN(birthDate.getTime())) throw new Error("Invalid date of birth.");
    if (Number.isNaN(atDate.getTime())) throw new Error("Invalid date.");

    if (birthDate > atDate) throw new Error("Date of birth cannot be in the future.");

    let years = atDate.getFullYear() - birthDate.getFullYear();
    const birthdayThisYear = new Date(atDate.getFullYear(), birthDate.getMonth(), birthDate.getDate());
    if (atDate < birthdayThisYear) {
        years -= 1;
    }

    return years;
}

window.removeAllHighlights = function () {
    document.querySelectorAll('.athlete-name').forEach(element => {
        element.innerHTML = escapeHTML(element.textContent);
    });
}

window.highlightText = function (element, searchTerms) {
    const originalText = element.textContent ?? '';
    const normalizedText = normalizeString(originalText);

    // Build a mapping from positions in normalizedText to positions in originalText
    const mapping: number[] = [];
    let originalIndex = 0;
    let normalizedIndex = 0;

    while (originalIndex < originalText.length) {
        const originalChar = originalText.charAt(originalIndex);
        const normalizedChar = normalizeString(originalChar);

        for (let i = 0; i < normalizedChar.length; i++) {
            mapping[normalizedIndex] = originalIndex;
            normalizedIndex++;
        }

        originalIndex++;
    }

    // Now search for matches in normalizedText
    const matchIndices: Array<{ start: number; end: number }> = [];

    searchTerms.forEach(term => {
        let startIndex = 0;
        const termLength = term.length;

        while (startIndex <= normalizedText.length - termLength) {
            if (normalizedText.substr(startIndex, termLength) === term) {
                // Map the normalized indices back to original indices
                const originalStart = mapping[startIndex];
                const mappedEnd = mapping[startIndex + termLength - 1];
                if (originalStart === undefined || mappedEnd === undefined) {
                    startIndex += termLength;
                    continue;
                }
                const originalEnd = mappedEnd + 1;
                matchIndices.push({ start: originalStart, end: originalEnd });
                startIndex += termLength;
            } else {
                startIndex++;
            }
        }
    });

    // Merge overlapping matches
    matchIndices.sort((a, b) => a.start - b.start);
    const mergedIndices: Array<{ start: number; end: number }> = [];
    matchIndices.forEach(match => {
        if (mergedIndices.length === 0) {
            mergedIndices.push(match);
        } else {
            const last = mergedIndices[mergedIndices.length - 1];
            if (last && match.start <= last.end) {
                last.end = Math.max(last.end, match.end);
            } else {
                mergedIndices.push(match);
            }
        }
    });

    // Build the highlighted HTML
    let highlightedHTML = '';
    let currentIndex = 0;

    mergedIndices.forEach(match => {
        // Append text before the match
        highlightedHTML += escapeHTML(originalText.slice(currentIndex, match.start));

        // Append the matched text with highlight
        highlightedHTML += '<span class="highlight">' +
            escapeHTML(originalText.slice(match.start, match.end)) +
            '</span>';

        currentIndex = match.end;
    });

    // Append any remaining text
    highlightedHTML += escapeHTML(originalText.slice(currentIndex));

    element.innerHTML = highlightedHTML;
}

window.goBackOrHome = function () {
    // Prefer history.back() for legacy generic back buttons.
    // Referrer can be empty or stripped, so do not rely on it; going back is safe for same-tab navigation.
    if (window.history.length > 1) {
        window.history.back();
    } else {
        window.location.href = '/';
    }
}

window.navigateToFlowDestination = function (destination) {
    const target = typeof destination === 'string' && destination.trim()
        ? destination
        : '/';
    window.location.replace(target);
}

window.optimizeImageClient = async function (dataUri, options) {
    const settings = options || {};
    const MaxBase64Length = 50 * 1024 * 1024; // 50 MB
    if (!dataUri || dataUri.length > MaxBase64Length) {
        return { dataUrl: null, contentType: null, extension: null };
    }

    // Parse the data URI
    const match = dataUri.match(/^data:(?<type>.+?);base64,(?<data>.+)$/);
    if (!match) {
        return { dataUrl: null, contentType: null, extension: null };
    }
    const contentType = match.groups?.type;
    const base64Data = match.groups?.data;
    if (!contentType || !base64Data) {
        return { dataUrl: null, contentType: null, extension: null };
    }

    // Decode Base64 to a Blob
    const binaryString = atob(base64Data);
    const len = binaryString.length;
    const bytes = new Uint8Array(len);
    for (let i = 0; i < len; i++) {
        bytes[i] = binaryString.charCodeAt(i);
    }
    const blob = new Blob([bytes], { type: contentType });

    let img: ImageBitmap;
    try {
        img = await createImageBitmap(blob);
    } catch (_) {
        return { dataUrl: dataUri, contentType, extension: contentType.split('/')[1] ?? null };
    }

    const targetContentType = settings.contentType || 'image/webp';
    const initialQuality = typeof settings.quality === 'number' && Number.isFinite(settings.quality)
        ? settings.quality
        : 0.8;
    const initialMaxSize = typeof settings.maxSize === 'number' && Number.isFinite(settings.maxSize)
        ? settings.maxSize
        : 2048;
    const targetMaxBytes = typeof settings.targetMaxBytes === 'number'
        && Number.isFinite(settings.targetMaxBytes)
        && settings.targetMaxBytes > 0
        ? settings.targetMaxBytes
        : null;
    const minQuality = typeof settings.minQuality === 'number' && Number.isFinite(settings.minQuality)
        ? settings.minQuality
        : 0.76;
    const minResizeQuality = typeof settings.minResizeQuality === 'number' && Number.isFinite(settings.minResizeQuality)
        ? settings.minResizeQuality
        : 0.72;
    const minMaxSize = typeof settings.minMaxSize === 'number' && Number.isFinite(settings.minMaxSize)
        ? settings.minMaxSize
        : 2048;

    const encode = async (maxSize: number, quality: number): Promise<Blob | null> => {
        let { width, height } = img;
        if (width > maxSize || height > maxSize) {
            const ratio = Math.min(maxSize / width, maxSize / height);
            width = Math.round(width * ratio);
            height = Math.round(height * ratio);
        }

        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext('2d');
        if (!ctx) return null;
        ctx.drawImage(img, 0, 0, width, height);
        return await new Promise<Blob | null>(resolve =>
            canvas.toBlob(resolve, targetContentType, quality)
        );
    };

    let optimizedBlob: Blob | null = null;
    let maxSize = initialMaxSize;
    let quality = initialQuality;
    for (let attempt = 0; attempt < 8; attempt++) {
        optimizedBlob = await encode(maxSize, quality);
        if (!optimizedBlob || !targetMaxBytes || optimizedBlob.size <= targetMaxBytes) {
            break;
        }

        if (quality > minQuality) {
            quality = Math.max(minQuality, quality - 0.06);
        } else if (maxSize > minMaxSize) {
            maxSize = Math.max(minMaxSize, Math.round(maxSize * 0.86));
            quality = Math.max(minResizeQuality, quality - 0.04);
        } else {
            break;
        }
    }

    if (typeof img.close === 'function') img.close();

    if (!optimizedBlob) {
        // fallback to original if conversion failed
        return { dataUrl: dataUri, contentType, extension: contentType.split('/')[1] ?? null };
    }

    const optimizedContentType = optimizedBlob.type || targetContentType;

    // Convert Blob back to Base64 data URI
    const arrayBuffer = await optimizedBlob.arrayBuffer();
    const u8 = new Uint8Array(arrayBuffer);
    let binary = '';
    const chunk = 0x8000;
    for (let i = 0; i < u8.length; i += chunk) {
        binary += String.fromCharCode(...u8.subarray(i, i + chunk));
    }
    const optimizedBase64 = btoa(binary);
    const optimizedDataUrl = 'data:' + optimizedContentType + ';base64,' + optimizedBase64;

    return {
        dataUrl: optimizedDataUrl,
        contentType: optimizedContentType,
        extension: optimizedContentType.split('/')[1] ?? null
    };
};

window.PROFILE_IMAGE_OPTIMIZATION_OPTIONS = {
    maxSize: 1024,
    minMaxSize: 640,
    quality: 0.82,
    minQuality: 0.68,
    minResizeQuality: 0.68,
    targetMaxBytes: 900 * 1024
};

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function getErrorMessage(error: unknown): string | null {
    if (!isRecord(error) || typeof error.message !== 'string' || !error.message) return null;
    return error.message.slice(0, 500);
}

function compactApplicationDataString(value: string): string {
    let firstHash = 2166136261;
    let secondHash = 0x9e3779b9;
    for (let index = 0; index < value.length; index++) {
        const code = value.charCodeAt(index);
        firstHash = Math.imul(firstHash ^ code, 16777619);
        secondHash = Math.imul(secondHash ^ code, 2246822519);
    }
    return `${value.length}:${(firstHash >>> 0).toString(16)}:${(secondHash >>> 0).toString(16)}`;
}

const PENDING_APPLICATION_SUBMISSION_STORAGE_KEY = 'pendingApplicationSubmission';
const PENDING_APPLICATION_SUBMISSION_LIFETIME_MS = 24 * 24 * 60 * 60 * 1000;

function readPendingApplicationSubmission(): PendingApplicationSubmission | null {
    let raw: string | null = null;
    try {
        raw = window.localStorage.getItem(PENDING_APPLICATION_SUBMISSION_STORAGE_KEY);
    } catch (_) {
        return null;
    }

    if (!raw) return null;

    try {
        const parsed: unknown = JSON.parse(raw);
        if (!isRecord(parsed)
            || typeof parsed.submissionId !== 'string'
            || !parsed.submissionId
            || typeof parsed.payloadFingerprint !== 'string'
            || typeof parsed.submissionKind !== 'string'
            || typeof parsed.createdAt !== 'number'
            || !Number.isFinite(parsed.createdAt)
            || parsed.createdAt + PENDING_APPLICATION_SUBMISSION_LIFETIME_MS <= Date.now()) {
            window.localStorage.removeItem(PENDING_APPLICATION_SUBMISSION_STORAGE_KEY);
            return null;
        }

        return {
            submissionId: parsed.submissionId,
            payloadFingerprint: parsed.payloadFingerprint,
            submissionKind: parsed.submissionKind,
            applicantName: typeof parsed.applicantName === 'string' ? parsed.applicantName : null,
            accountEmail: typeof parsed.accountEmail === 'string' ? parsed.accountEmail : null,
            createdAt: parsed.createdAt
        };
    } catch (_) {
        try {
            window.localStorage.removeItem(PENDING_APPLICATION_SUBMISSION_STORAGE_KEY);
        } catch (_) {
        }
        return null;
    }
}

function storePendingApplicationSubmission(pending: PendingApplicationSubmission): void {
    try {
        window.localStorage.setItem(PENDING_APPLICATION_SUBMISSION_STORAGE_KEY, JSON.stringify(pending));
    } catch (_) {
        // Recovery is best effort when storage is unavailable (for example, private browsing).
    }
}

async function fetchWithApplicationTimeout(
    input: RequestInfo | URL,
    init: RequestInit,
    timeoutMs: number
): Promise<Response> {
    const timeoutController = typeof AbortController !== 'undefined' && !init.signal
        ? new AbortController()
        : null;
    const timeoutId = window.setTimeout(() => timeoutController?.abort(), timeoutMs);

    try {
        return await fetch(input, timeoutController ? { ...init, signal: timeoutController.signal } : init);
    } catch (error) {
        if (timeoutController?.signal.aborted) {
            throw new Error('Request timed out');
        }
        throw error;
    } finally {
        window.clearTimeout(timeoutId);
    }
}

async function recoverApplicationSubmissionResponse(
    submissionId: string,
    timeoutMs: number
): Promise<Record<string, unknown> | null> {
    const response = await fetchWithApplicationTimeout('/api/application/submission-status', {
        method: 'POST',
        cache: 'no-store',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ submissionId })
    }, timeoutMs);

    if (response.status === 404) return null;
    if (!response.ok) {
        throw new Error(`Submission recovery failed with HTTP ${response.status}`);
    }

    const recovered: unknown = await response.json();
    return isRecord(recovered) && recovered.success === true ? recovered : null;
}

window.createApplicationSubmissionPayloadKey = function (applicantData: unknown): string {
    const data = isRecord(applicantData) ? applicantData : {};
    const normalizedPayload = JSON.stringify(data, (key, value: unknown) => {
        if (key === 'proofPics' && Array.isArray(value)) {
            return value.map(proof => typeof proof === 'string'
                ? compactApplicationDataString(proof)
                : proof);
        }
        if (key === 'profilePic' && typeof value === 'string') {
            return compactApplicationDataString(value);
        }
        return value;
    }) ?? '{}';
    return compactApplicationDataString(normalizedPayload);
};

window.createApplicationSubmissionId = function (payloadFingerprint?: string) {
    const normalizedFingerprint = typeof payloadFingerprint === 'string'
        ? payloadFingerprint
        : undefined;
    if (typeof window.__pendingApplicationSubmissionId === 'string'
        && window.__pendingApplicationSubmissionId.length > 0
        && (normalizedFingerprint === undefined
            || window.__pendingApplicationSubmissionFingerprint === normalizedFingerprint)) {
        return window.__pendingApplicationSubmissionId;
    }

    const storedSubmission = normalizedFingerprint === undefined
        ? null
        : readPendingApplicationSubmission();
    if (storedSubmission
        && normalizedFingerprint !== undefined
        && storedSubmission.payloadFingerprint === normalizedFingerprint) {
        window.__pendingApplicationSubmissionId = storedSubmission.submissionId;
        window.__pendingApplicationSubmissionFingerprint = normalizedFingerprint;
        return storedSubmission.submissionId;
    }

    let submissionId;
    if (window.crypto && typeof window.crypto.randomUUID === 'function') {
        submissionId = window.crypto.randomUUID();
    } else {
        submissionId = 'submission-' + Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 10);
    }

    window.__pendingApplicationSubmissionId = submissionId;
    if (normalizedFingerprint === undefined) {
        delete window.__pendingApplicationSubmissionFingerprint;
    } else {
        window.__pendingApplicationSubmissionFingerprint = normalizedFingerprint;
    }
    return submissionId;
};

window.rememberPendingApplicationSubmission = function (details) {
    const submissionId = typeof details?.submissionId === 'string' ? details.submissionId.trim() : '';
    const payloadFingerprint = typeof details?.payloadFingerprint === 'string' ? details.payloadFingerprint : '';
    const submissionKind = typeof details?.submissionKind === 'string' ? details.submissionKind.trim() : '';
    if (!submissionId || !payloadFingerprint || !submissionKind) return;

    const pending: PendingApplicationSubmission = {
        submissionId,
        payloadFingerprint,
        submissionKind,
        applicantName: typeof details.applicantName === 'string' ? details.applicantName.trim() || null : null,
        accountEmail: typeof details.accountEmail === 'string' ? details.accountEmail.trim() || null : null,
        createdAt: Date.now()
    };
    window.__pendingApplicationSubmissionId = submissionId;
    window.__pendingApplicationSubmissionFingerprint = payloadFingerprint;
    storePendingApplicationSubmission(pending);
};

window.getPendingApplicationSubmission = function (submissionKind?: string) {
    const pending = readPendingApplicationSubmission();
    if (!pending || (submissionKind && pending.submissionKind !== submissionKind)) return null;
    return pending;
};

window.clearPendingApplicationSubmission = function (submissionId?: string) {
    const pending = readPendingApplicationSubmission();
    if (submissionId && pending && pending.submissionId !== submissionId) return;

    try {
        window.localStorage.removeItem(PENDING_APPLICATION_SUBMISSION_STORAGE_KEY);
    } catch (_) {
    }
    if (!submissionId || window.__pendingApplicationSubmissionId === submissionId) {
        delete window.__pendingApplicationSubmissionId;
        delete window.__pendingApplicationSubmissionFingerprint;
    }
};

window.tryRecoverPendingApplicationSubmission = async function (submissionKind: string) {
    const pending = window.getPendingApplicationSubmission(submissionKind);
    if (!pending) return null;

    const submitResult = await recoverApplicationSubmissionResponse(
        pending.submissionId,
        window.APPLICATION_SUBMISSION_TIMEOUT_MS || 310000);
    return submitResult ? { pending, submitResult } : null;
};

window.submitApplicationWithRecovery = async function (applicantData: unknown, timeoutMs?: number) {
    const data = isRecord(applicantData) ? applicantData : {};
    const submissionId = typeof data.submissionId === 'string' ? data.submissionId : '';
    const requestTimeout = Number.isFinite(timeoutMs) && Number(timeoutMs) > 0
        ? Number(timeoutMs)
        : window.APPLICATION_SUBMISSION_TIMEOUT_MS || 310000;
    const requestInit: RequestInit = {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    };
    let lastResponse: Response | null = null;
    let lastError: unknown = null;

    for (let attempt = 0; attempt < 2; attempt++) {
        try {
            const response = await fetchWithApplicationTimeout(
                '/api/application/application',
                requestInit,
                requestTimeout);
            lastResponse = response;

            if (response.ok) {
                const submitResult: unknown = await response.json();
                if (!isRecord(submitResult)) throw new Error('The submission response was invalid.');
                return { ok: true, response, submitResult, recovered: false };
            }

            if (response.status !== 408 && response.status < 500) {
                return { ok: false, response, submitResult: null, recovered: false };
            }
        } catch (error) {
            lastError = error;
        }

        if (submissionId) {
            try {
                const recovered = await recoverApplicationSubmissionResponse(submissionId, requestTimeout);
                if (recovered) {
                    return { ok: true, response: null, submitResult: recovered, recovered: true };
                }
            } catch (error) {
                lastError = error;
            }
        }
    }

    if (lastResponse && !lastResponse.ok) {
        return { ok: false, response: lastResponse, submitResult: null, recovered: false };
    }
    throw lastError || new Error('Application submission failed.');
};

window.APPLICATION_SUBMISSION_TIMEOUT_MS = 310000;
window.APPLICATION_SUBMISSION_REPORT_TIMEOUT_MS = 2500;

window.readApplicationErrorMessage = async function (response) {
    const fallback = response && response.statusText
        ? response.statusText
        : response && Number.isFinite(response.status)
            ? `HTTP ${response.status}`
            : 'Request failed';
    let text = '';
    try {
        text = response ? await response.text() : '';
    } catch (_) {
        return fallback;
    }
    return window.extractApplicationErrorMessage(text, fallback);
};

window.extractApplicationErrorMessage = function (text, fallback) {
    const raw = String(text || '').trim();
    if (!raw) return fallback || 'Request failed';
    if (/^(?:<!doctype\s+html\b|<html[\s>])/i.test(raw)) return fallback || 'Request failed';
    const collectMessages = function (values: readonly unknown[]): string[] {
        return values
            .flatMap<unknown>(value => Array.isArray(value) ? value : [value])
            .filter((value): value is string => typeof value === 'string')
            .map(value => value.trim())
            .filter(Boolean);
    };

    try {
        const data: unknown = JSON.parse(raw);
        if (typeof data === 'string' && data.trim()) {
            return data.trim();
        }

        if (isRecord(data) && typeof data.message === 'string' && data.message.trim()) {
            return data.message.trim();
        }

        if (isRecord(data) && typeof data.detail === 'string' && data.detail.trim()) {
            return data.detail.trim();
        }

        if (isRecord(data) && isRecord(data.errors)) {
            const messages = collectMessages(Object.values(data.errors));
            if (messages.length) return messages.join('\n');
        }

        if (Array.isArray(data)) {
            const messages = collectMessages(data);
            if (messages.length) return messages.join('\n');
        }

        if (isRecord(data) && typeof data.title === 'string' && data.title.trim()) {
            return data.title.trim();
        }

        if (isRecord(data)) {
            const messages = collectMessages(Object.values(data));
            if (messages.length) return messages.join('\n');
        }
    } catch (_) {
        return raw;
    }

    return fallback || raw || 'Request failed';
};

window.buildApplicationSubmissionReport = function (applicantData, submissionId, phase, submissionKind, error) {
    const data = isRecord(applicantData) ? applicantData : {};
    const proofs: readonly unknown[] = Array.isArray(data.proofPics) ? data.proofPics : [];
    const profilePic = typeof data.profilePic === 'string' && data.profilePic.startsWith('data:')
        ? data.profilePic
        : null;
    let jsonBodyLength = null;
    try {
        let omittedDataLength = 0;
        const skeleton = JSON.stringify(data, (key, value: unknown) => {
            if (key === 'proofPics' && Array.isArray(value)) {
                return value.map(proof => {
                    if (typeof proof !== 'string') return proof;
                    omittedDataLength += proof.length;
                    return '';
                });
            }
            if (key === 'profilePic' && typeof value === 'string') {
                omittedDataLength += value.length;
                return '';
            }
            return value;
        });
        jsonBodyLength = skeleton.length + omittedDataLength;
    } catch (_) {
        jsonBodyLength = null;
    }

    return {
        submissionId,
        phase,
        pagePath: window.location.pathname,
        submissionKind,
        proofCount: proofs.length,
        proofDataUrlLengths: proofs.map(value => typeof value === 'string' ? value.length : 0),
        profilePicDataUrlLength: profilePic ? profilePic.length : null,
        jsonBodyLength,
        errorType: isRecord(error) ? error.type || null : null,
        errorMessage: getErrorMessage(error)
    };
};

window.sendApplicationSubmissionReport = async function (report) {
    if (!report || !report.submissionId) {
        return;
    }

    let body;
    try {
        body = JSON.stringify(report);
    } catch (_) {
        return;
    }

    const timeoutMs = window.APPLICATION_SUBMISSION_REPORT_TIMEOUT_MS || 2500;
    const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;
    const timer = controller
        ? window.setTimeout(() => controller.abort(), timeoutMs)
        : null;

    try {
        await fetch('/api/application/submission-report', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body,
            keepalive: body.length < 60000,
            ...(controller ? { signal: controller.signal } : {})
        });
    } catch (_) {
        // Best-effort diagnostics must never block an application attempt.
    } finally {
        if (timer) window.clearTimeout(timer);
    }
};

window.trySendApplicationSubmissionReport = function (applicantData, submissionId, phase, submissionKind, error) {
    try {
        if (typeof window.buildApplicationSubmissionReport !== 'function'
            || typeof window.sendApplicationSubmissionReport !== 'function') {
            return;
        }

        const report = window.buildApplicationSubmissionReport(applicantData, submissionId, phase, submissionKind, error);
        void window.sendApplicationSubmissionReport(report);
    } catch (_) {
        // Best-effort diagnostics must never block or reset an application attempt.
    }
};

function isHypotheticalRankNearbyItem(value: unknown): value is HypotheticalRankNearbyItem {
    if (!isRecord(value)) return false;
    return (value.rank === undefined || typeof value.rank === 'number')
        && (value.name === undefined || typeof value.name === 'string')
        && (value.category === undefined || typeof value.category === 'string')
        && (value.ageDifference === undefined || typeof value.ageDifference === 'number')
        && (value.isHypothetical === undefined || typeof value.isHypothetical === 'boolean');
}

function isHypotheticalRankResult(value: unknown): value is HypotheticalRankResult {
    if (!isRecord(value)
        || typeof value.rank !== 'number'
        || typeof value.fieldSize !== 'number') {
        return false;
    }

    return (value.currentFieldSize === undefined || typeof value.currentFieldSize === 'number')
        && (value.category === undefined || typeof value.category === 'string')
        && (value.leagueName === undefined || typeof value.leagueName === 'string')
        && (value.nearby === undefined
            || (Array.isArray(value.nearby) && value.nearby.every(isHypotheticalRankNearbyItem)));
}

window.updateHypotheticalRankResult = async function (options) {
    const container = document.getElementById(options && options.containerId);
    if (!container) return;

    const requestId = (window.__hypotheticalRankRequestSequence || 0) + 1;
    window.__hypotheticalRankRequestSequence = requestId;
    container.__hypotheticalRankRequestId = requestId;
    const isCurrentRequest = () => container.__hypotheticalRankRequestId === requestId;

    const payload = {
        calculator: options.calculator,
        chronologicalAge: options.chronologicalAge,
        biologicalAge: options.biologicalAge,
        birthYear: options.birthYear,
        birthMonth: options.birthMonth,
        birthDay: options.birthDay
    };

    if (!payload.calculator ||
        !Number.isFinite(payload.chronologicalAge) ||
        !Number.isFinite(payload.biologicalAge) ||
        !Number.isInteger(payload.birthYear) ||
        !Number.isInteger(payload.birthMonth) ||
        !Number.isInteger(payload.birthDay)) {
        container.hidden = true;
        container.removeAttribute('aria-busy');
        return;
    }

    container.hidden = false;
    container.setAttribute('aria-busy', 'true');
    container.innerHTML = '<div class="bioage-rank-pending">Checking the current leaderboard...</div>';

    const timeoutMs = 10000;
    const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;
    const timer = controller
        ? window.setTimeout(() => controller.abort(), timeoutMs)
        : null;

    try {
        const response = await fetch('/api/data/hypothetical-rank', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
            ...(controller ? { signal: controller.signal } : {})
        });

        if (!response.ok) throw new Error('Rank lookup failed.');
        const result: unknown = await response.json();
        if (!isCurrentRequest()) return;

        window.renderHypotheticalRankResult(container, result);
    } catch (_) {
        if (isCurrentRequest()) {
            container.hidden = true;
        }
    } finally {
        if (timer) window.clearTimeout(timer);
        if (isCurrentRequest()) {
            container.removeAttribute('aria-busy');
        }
    }
};

window.renderHypotheticalRankResult = function (container, result) {
    if (!container || !isHypotheticalRankResult(result)
        || !Number.isFinite(result.rank)
        || !Number.isFinite(result.fieldSize)) {
        if (container) container.hidden = true;
        return;
    }

    const rank = Math.round(result.rank);
    const fieldSize = Math.round(result.fieldSize);
    const currentFieldSize = typeof result.currentFieldSize === 'number'
        && Number.isFinite(result.currentFieldSize)
        ? Math.round(result.currentFieldSize)
        : Math.max(0, fieldSize - 1);
    const category = escapeHTML(result.category || '');
    const leagueName = escapeHTML(result.leagueName || 'Ultimate League');
    const nearby = Array.isArray(result.nearby) ? result.nearby : [];

    const nearbyHtml = nearby.map(item => {
        const place = typeof item.rank === 'number' && Number.isFinite(item.rank)
            ? Math.round(item.rank)
            : '';
        const name = escapeHTML(item.isHypothetical ? 'You' : (item.name || ''));
        const itemCategory = item.category ? ` · ${escapeHTML(item.category)}` : '';
        const diff = typeof item.ageDifference === 'number' && Number.isFinite(item.ageDifference)
            ? `${item.ageDifference > 0 ? '+' : ''}${item.ageDifference.toFixed(1)}y`
            : '';
        return `<li class="${item.isHypothetical ? 'is-you' : ''}">
            <span class="rank-place">#${place}</span>
            <span class="rank-name">${name}${itemCategory}</span>
            <span class="rank-diff">${diff}</span>
        </li>`;
    }).join('');

    container.hidden = false;
    container.innerHTML = `
        <div class="bioage-rank-summary">
            <div class="bioage-rank-number">#${rank}</div>
            <div>
                <div class="bioage-rank-label">Hypothetical rank</div>
                <div class="bioage-rank-context">
                    <strong>${leagueName}</strong>${category ? ` · ${category}` : ''} · ${rank} of ${fieldSize}
                </div>
                <div class="bioage-rank-context">Current field: ${currentFieldSize}</div>
            </div>
        </div>
        ${nearbyHtml ? `<ol class="bioage-rank-nearby">${nearbyHtml}</ol>` : ''}
    `;
};

export {};
