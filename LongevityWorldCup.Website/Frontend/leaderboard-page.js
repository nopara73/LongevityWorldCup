const leaderboardPageAssets = document.currentScript.dataset;
(function () {
const pageDocument = window.document;
let currentLeaderboardDocumentTitle = /^\/athlete\/[^/]+\/?$/i.test(window.location.pathname)
    ? 'Longevity World Cup'
    : pageDocument.title;
const athleteDialogRuntime = pageDocument.getElementById('athleteDialogRuntime');
if (!athleteDialogRuntime) {
    console.error('Athlete dialog runtime root was not found.');
    return;
}

// Keep the legacy leaderboard controller isolated from unrelated page
// components when the same runtime is mounted outside the leaderboard.
// Selector calls remain scoped to this one component; document-wide APIs
// such as body scroll locking and event listeners still use the real page.
const document = new Proxy(pageDocument, {
    get(target, property) {
        if (property === 'getElementById') {
            return id => athleteDialogRuntime.querySelector(`#${CSS.escape(id)}`);
        }
        if (property === 'querySelector') {
            return selector => athleteDialogRuntime.querySelector(selector);
        }
        if (property === 'querySelectorAll') {
            return selector => athleteDialogRuntime.querySelectorAll(selector);
        }

        const value = Reflect.get(target, property, target);
        return typeof value === 'function' ? value.bind(target) : value;
    },
    set(target, property, value) {
        return Reflect.set(target, property, value, target);
    }
});

let athleteResults = [];
let athletesOrderedByPace;
let athletesOrderedByBortzPace;
const isAthleteDialogOnlyRuntime = athleteDialogRuntime.dataset.athleteDialogOnly === 'true';
let athleteResultsReady = false;
let athleteResultsLoadPromise = null;
let renderedLeaderboardSelection = '';
let athleteDialogReturnFocusElement = null;
const defaultProfilePic = leaderboardPageAssets.defaultProfilePic;

const currentSeasonStartDate = new Date(new Date().getFullYear(), 0, 1);

// Capture this before opening a modal mutates the route. The skip itself is
// deferred until the athlete snapshot supplies the portrait content ID.
let initialAthleteSlugToSkipForGuessMyAge = getAthleteSlugToSkipFromURL();

function orderByNumberDesc(items, selector) {
    return [...items].sort((a, b) => {
        const aValue = selector(a);
        const bValue = selector(b);
        const aRank = Number.isFinite(aValue) ? aValue : Number.NEGATIVE_INFINITY;
        const bRank = Number.isFinite(bValue) ? bValue : Number.NEGATIVE_INFINITY;
        return bRank - aRank;
    });
}

function orderByNumberAsc(items, selector) {
    return [...items].sort((a, b) => {
        const aValue = selector(a);
        const bValue = selector(b);
        const aRank = Number.isFinite(aValue) ? aValue : Number.POSITIVE_INFINITY;
        const bRank = Number.isFinite(bValue) ? bValue : Number.POSITIVE_INFINITY;
        return aRank - bRank;
    });
}

function findAthleteIndexByName(items, name) {
    return items.findIndex(item => item && item.name === name);
}

function normalizeFlagKey(flag) {
    return window.LwcFlags.normalizeFlagKey(flag);
}

function getCanonicalFlagName(flag) {
    return window.LwcFlags.getCanonicalFlagName(flag);
}

function getFlagFilterKey(flag) {
    return window.LwcFlags.getFlagFilterKey(flag);
}

function getFlagRankKey(flag) {
    const flagKey = getFlagFilterKey(flag);
    return flagKey ? `flag:${flagKey}` : '';
}

function decodeFilterUrlToken(token) {
    let decoded = String(token || '');
    for (let i = 0; i < 2; i++) {
        try {
            const next = decodeURIComponent(decoded);
            if (next === decoded) break;
            decoded = next;
        } catch (_) {
            break;
        }
    }

    return decoded.trim();
}

function parseFiltersParam(filtersParam) {
    return String(filtersParam || '')
        .split(',')
        .map(decodeFilterUrlToken)
        .filter(Boolean);
}

function serializeFiltersParam(filters) {
    return filters.map(filter => encodeURIComponent(filter)).join(',');
}

function getFlagIconCode(flag) {
    return window.LwcFlags.getFlagIconCode(flag);
}

function renderFlagLabel(flag) {
    return window.LwcFlags.renderFlagLabel(flag);
}

function getFlagRouteSlug(flag) {
    return window.LwcFlags.getFlagRouteSlug(flag);
}

function buildFlagHref(flag) {
    return window.LwcFlags.getFlagHref(flag);
}

function hasBortzRankData(athlete) {
    return athlete && athlete.bortzAgeReduction != null && Number.isFinite(athlete.bortzAgeReduction);
}

function buildFiltersHref(filters) {
    const cleanFilters = (Array.isArray(filters) ? filters : [filters])
        .map(filter => String(filter || '').trim())
        .filter(Boolean);
    return cleanFilters.length > 0
        ? `/?filters=${serializeFiltersParam(cleanFilters)}`
        : '/leaderboard';
}

function buildViewHref(view) {
    return `/league/${encodeURIComponent(String(view || '').trim())}`;
}

function addBestRankCandidate(athlete, candidate) {
    if (!athlete || !candidate) return;

    const rank = Number(candidate.rank);
    if (!Number.isFinite(rank) || rank < 1) return;

    const normalized = {
        rank,
        leagueName: String(candidate.leagueName || candidate.leagueLabel || '').trim(),
        leagueLabel: String(candidate.leagueLabel || candidate.leagueName || '').trim(),
        leagueLabelHtml: candidate.leagueLabelHtml || null,
        leagueType: String(candidate.leagueType || 'other').trim(),
        href: candidate.href || null,
        targetBlank: !!candidate.targetBlank,
        tiePriority: Number.isFinite(candidate.tiePriority) ? candidate.tiePriority : 100
    };

    if (!normalized.leagueName && !normalized.leagueLabel) return;

    if (!Array.isArray(athlete.bestRankCandidates)) {
        athlete.bestRankCandidates = [];
    }

    const key = `${normalized.leagueType}|${normalized.leagueName || normalized.leagueLabel}`;
    const existingIndex = athlete.bestRankCandidates.findIndex(item => item.key === key);
    if (existingIndex >= 0) {
        const existing = athlete.bestRankCandidates[existingIndex];
        if (compareBestRankCandidates(normalized, existing) < 0) {
            athlete.bestRankCandidates[existingIndex] = { ...normalized, key };
        }
        return;
    }

    athlete.bestRankCandidates.push({ ...normalized, key });
}

function compareBestRankCandidates(a, b) {
    if (!a) return 1;
    if (!b) return -1;

    if (a.rank !== b.rank) return a.rank - b.rank;
    if (a.tiePriority !== b.tiePriority) return a.tiePriority - b.tiePriority;
    return String(a.leagueLabel || a.leagueName || '').localeCompare(String(b.leagueLabel || b.leagueName || ''));
}

function assignRankedBestCandidates(items, candidateFactory) {
    (Array.isArray(items) ? items : []).forEach((athlete, index) => {
        const candidate = candidateFactory(athlete, index);
        addBestRankCandidate(athlete, {
            ...candidate,
            rank: index + 1
        });
    });
}

function sortedRankableAthletes(athletes, predicate, comparator) {
    return (Array.isArray(athletes) ? athletes : [])
        .filter(predicate || (() => true))
        .slice()
        .sort(comparator);
}

function assignBestRankCandidates(athletes) {
    const rows = Array.isArray(athletes) ? athletes : [];
    rows.forEach(athlete => {
        athlete.bestRankCandidates = [];
        if (Number.isFinite(athlete.rank)) {
            addBestRankCandidate(athlete, {
                rank: athlete.rank,
                leagueName: 'Ultimate League',
                leagueLabel: 'Ultimate League',
                leagueType: 'ultimate',
                href: '/leaderboard',
                targetBlank: true,
                tiePriority: 0
            });
        }
    });

    assignRankedBestCandidates(
        sortedRankableAthletes(rows, hasBortzRankData, window.compareAthleteRank),
        () => ({
            leagueName: 'Bortz Age',
            leagueLabel: 'Bortz Age leaderboard',
            leagueType: 'bortz',
            href: buildViewHref('bortz'),
            tiePriority: 10
        })
    );

    assignRankedBestCandidates(
        sortedRankableAthletes(rows, athlete => Number.isFinite(athlete.ageReduction), window.compareAthleteRankPhenoOnly),
        () => ({
            leagueName: 'Pheno Age',
            leagueLabel: 'Pheno Age leaderboard',
            leagueType: 'pheno',
            href: buildViewHref('pheno'),
            tiePriority: 11
        })
    );

    assignRankedBestCandidates(
        sortedRankableAthletes(rows, athlete => !hasBortzRankData(athlete), window.compareAthleteRankPhenoOnly),
        () => ({
            leagueName: 'Amateur League',
            leagueLabel: 'Amateur League',
            leagueType: 'amateur',
            href: buildFiltersHref('Amateur'),
            targetBlank: true,
            tiePriority: -1
        })
    );

    assignRankedBestCandidates(
        sortedRankableAthletes(rows, athlete => Number.isFinite(athlete.phenoAgeImprovement), window.compareAthleteRankPhenoImprovement),
        () => ({
            leagueName: 'Pheno Improvement',
            leagueLabel: 'Pheno Improvement leaderboard',
            leagueType: 'pheno-improvement',
            href: buildViewHref('improvement'),
            tiePriority: 12
        })
    );

    assignRankedBestCandidates(
        sortedRankableAthletes(rows, athlete => Number.isFinite(athlete.bortzAgeImprovement), window.compareAthleteRankBortzImprovement),
        () => ({
            leagueName: 'Bortz Improvement',
            leagueLabel: 'Bortz Improvement leaderboard',
            leagueType: 'bortz-improvement',
            href: buildViewHref('bortz-improvement'),
            tiePriority: 12
        })
    );

    assignRankedBestCandidates(
        sortedRankableAthletes(
            rows,
            athlete => athlete.crowdCount >= CROWD_AGE_LEADERBOARD_MINIMUM_GUESS_COUNT && Number.isFinite(athlete.crowdAgeReduction),
            window.compareAthleteRankCrowdAge),
        () => ({
            leagueName: 'Crowd Age',
            leagueLabel: 'Crowd Age leaderboard',
            leagueType: 'crowd',
            href: buildViewHref('crowd'),
            tiePriority: 13
        })
    );

    assignRankedBestCandidates(
        orderByNumberDesc(
            rows.filter(athlete => Number.isFinite(athlete.ageReductionPercent)),
            athlete => athlete.ageReductionPercent),
        () => ({
            leagueName: 'Pheno pace of aging',
            leagueLabel: 'Pheno pace of aging ranking',
            leagueType: 'pheno-pace',
            tiePriority: 14
        })
    );

    assignRankedBestCandidates(
        orderByNumberAsc(
            rows.filter(athlete =>
                athlete.lowestBortzAge != null &&
                athlete.chronoAtLowestBortzAge != null &&
                Number.isFinite(athlete.lowestBortzAge / athlete.chronoAtLowestBortzAge)),
            athlete => athlete.lowestBortzAge / athlete.chronoAtLowestBortzAge),
        () => ({
            leagueName: 'Bortz pace of aging',
            leagueLabel: 'Bortz pace of aging ranking',
            leagueType: 'bortz-pace',
            tiePriority: 14
        })
    );

    const addGroupedRanks = (values, predicate, comparator, candidateFactory) => {
        values.filter(Boolean).forEach(value => {
            assignRankedBestCandidates(
                sortedRankableAthletes(rows, athlete => predicate(athlete, value), comparator),
                athlete => candidateFactory(athlete, value)
            );
        });
    };

    const divisions = [...new Set(rows.map(athlete => athlete.division).filter(Boolean))];
    addGroupedRanks(
        divisions,
        (athlete, division) => athlete.division === division,
        window.compareAthleteRank,
        (_athlete, division) => ({
            leagueName: division,
            leagueLabel: `${division} League`,
            leagueType: 'division',
            href: `/league/${window.slugifyName(division, true)}`,
            tiePriority: 20
        })
    );

    const generations = [...new Set(rows.map(athlete => athlete.generation).filter(Boolean))];
    addGroupedRanks(
        generations,
        (athlete, generation) => athlete.generation === generation,
        window.compareAthleteRank,
        (_athlete, generation) => ({
            leagueName: generation,
            leagueLabel: `${generation} League`,
            leagueType: 'generation',
            href: `/league/${window.slugifyName(generation, true)}`,
            tiePriority: 21
        })
    );

    divisions.forEach(division => {
        generations.forEach(generation => {
            assignRankedBestCandidates(
                sortedRankableAthletes(
                    rows,
                    athlete => athlete.division === division && athlete.generation === generation,
                    window.compareAthleteRank),
                () => ({
                    leagueName: `${generation}_${division}`,
                    leagueLabel: `${generation} ${division} League`,
                    leagueType: 'combination',
                    href: buildFiltersHref([generation, division]),
                    tiePriority: 22
                })
            );
        });
    });

    const exclusiveLeagues = [...new Set(rows.map(athlete => athlete.exclusiveLeague).filter(Boolean))];
    addGroupedRanks(
        exclusiveLeagues,
        (athlete, exclusiveLeague) => athlete.exclusiveLeague === exclusiveLeague,
        window.compareAthleteRank,
        (_athlete, exclusiveLeague) => ({
            leagueName: exclusiveLeague,
            leagueLabel: `${exclusiveLeague} League`,
            leagueType: 'exclusive',
            href: exclusiveLeague === 'Prosperan'
                ? '/league/prosperan'
                : buildFiltersHref(exclusiveLeague),
            tiePriority: 23
        })
    );

    const flagOptions = window.LwcFlags.countFlagUsage(rows, athlete => athlete.canonicalFlag);
    flagOptions.forEach(flagOption => {
        assignRankedBestCandidates(
            sortedRankableAthletes(
                rows,
                athlete => athlete.flagFilterKey === flagOption.key,
                window.compareAthleteRank),
            () => ({
                leagueName: flagOption.name,
                leagueLabel: flagOption.name,
                leagueLabelHtml: renderFlagLabel(flagOption.name),
                leagueType: 'flag',
                href: buildFlagHref(flagOption.name),
                tiePriority: 24
            })
        );
    });

}

function renderBestRankLink(bestRankCandidate) {
    const candidate = bestRankCandidate || {
        rank: null,
        leagueLabel: 'Ultimate League',
        href: '/leaderboard'
    };
    const rank = Number(candidate.rank);
    if (!Number.isFinite(rank)) return '';

    const label = candidate.leagueLabel || candidate.leagueName || 'Ultimate League';
    const labelHtml = candidate.leagueLabelHtml || escapeHtml(label);
    const preposition = candidate.leagueType === 'flag' ? 'for' : 'in the';
    if (!candidate.href) {
        return `#${rank} ${preposition} ${labelHtml}`;
    }

    const targetAttrs = candidate.targetBlank ? ' target="_blank" rel="noopener noreferrer"' : '';
    return `#${rank} ${preposition} <a href="${escapeHtml(candidate.href)}" class="league-link"${targetAttrs}>${labelHtml}</a>`;
}

window.LwcLeaderboardBestRank = {
    addBestRankCandidate,
    assignBestRankCandidates,
    compareBestRankCandidates,
    computeBestLeagueRank,
    renderBestRankLink
};

function getAthletePortraitUrls(athlete) {
    if (!athlete) return [];

    return [
        athlete.profilePicThumb,
        athlete.ProfilePicLeaderboardThumb,
        athlete.ProfilePicThumb,
        athlete.profilePic,
        athlete.ProfilePic
    ].filter((candidate, index, candidates) =>
        typeof candidate === 'string'
        && candidate.trim()
        && candidates.indexOf(candidate) === index);
}

function getAthletePortraitUrl(athlete) {
    return getAthletePortraitUrls(athlete)[0] || defaultProfilePic;
}

function wirePortraitFallback(img, athlete) {
    if (!img) return;

    const candidates = getAthletePortraitUrls(athlete);
    const toAbsoluteUrl = candidate => {
        try {
            return new URL(candidate, window.location.href).href;
        } catch (_) {
            return candidate;
        }
    };
    const currentSrc = toAbsoluteUrl(img.getAttribute('src') || img.src || '');
    const currentIndex = candidates.findIndex(candidate => toAbsoluteUrl(candidate) === currentSrc);
    let nextCandidateIndex = currentIndex >= 0 ? currentIndex + 1 : 0;

    img.onerror = function () {
        const nextCandidate = candidates[nextCandidateIndex++];
        if (nextCandidate) {
            this.src = nextCandidate;
            return;
        }

        this.onerror = null;
        this.src = defaultProfilePic;
        this.classList.add('portrait-fallback');
    };
}

function markAthleteSkippedForGuessMyAge(athlete) {
    if (!athlete) {
        return;
    }

    window.LwcGuessState?.ensureSkipped(athlete);
}

function markAthletesSkippedForGuessMyAgeIfUnset(athletes) {
    if (!Array.isArray(athletes) || athletes.length === 0) {
        return;
    }

    window.LwcGuessState?.ensureSkippedMany(athletes);
}

function getAthleteSlugToSkipFromURL() {
    const params = new URLSearchParams(window.location.search);
    let athleteSlug = null;

    if (params.has('athlete')) {
        athleteSlug = params.get('athlete');
    } else if (/^\/athlete\//.test(window.location.pathname)) {
        athleteSlug = window.location.pathname.split('/athlete/')[1].split('/')[0];
    }

    return athleteSlug !== null && params.get('guessmyage') != '1'
        ? normalizeAthleteSlugForLookup(athleteSlug)
        : '';
}

const leagueFilterRoutes = Object.freeze({
    amateur: { label: 'Amateur', canonical: true },
    professional: { label: 'Professional', canonical: false },
    womens: { label: "Women's", canonical: true },
    mens: { label: "Men's", canonical: true },
    open: { label: 'Open', canonical: true },
    'silent-generation': { label: 'Silent Generation', canonical: true },
    'baby-boomers': { label: 'Baby Boomers', canonical: true },
    'gen-x': { label: 'Gen X', canonical: true },
    millennials: { label: 'Millennials', canonical: true },
    'gen-z': { label: 'Gen Z', canonical: true },
    'gen-alpha': { label: 'Gen Alpha', canonical: true },
    prosperan: { label: 'Prosperan', canonical: true }
});

function getCanonicalLeagueSlugForFilter(filterLabel) {
    const normalizedLabel = String(filterLabel || '').trim().toLowerCase();
    const match = Object.entries(leagueFilterRoutes).find(([, route]) =>
        route.canonical && route.label.toLowerCase() === normalizedLabel);
    return match ? match[0] : null;
}

function getLeagueRouteState(pathname) {
    let normalizedPath = (pathname || '').trim().toLowerCase();
    while (normalizedPath.length > 1 && normalizedPath.endsWith('/')) {
        normalizedPath = normalizedPath.slice(0, -1);
    }

    const leaguePrefix = '/league/';
    if (!normalizedPath.startsWith(leaguePrefix) || normalizedPath.length <= leaguePrefix.length) {
        return { slug: null, filterLabel: null, view: null };
    }

    const slug = normalizedPath.slice(leaguePrefix.length);
    if (slug === 'pheno-improvement') {
        return { slug, filterLabel: null, view: 'improvement' };
    }

    if (slug === 'bortz' || slug === 'pheno' || slug === 'improvement' || slug === 'bortz-improvement' || slug === 'crowd') {
        return { slug, filterLabel: null, view: slug };
    }

    return {
        slug,
        filterLabel: leagueFilterRoutes[slug]?.label || null,
        view: slug === 'ultimate' ? 'ultimate' : null
    };
}

function getFlagRouteState(pathname) {
    let normalizedPath = (pathname || '').trim().toLowerCase();
    while (normalizedPath.length > 1 && normalizedPath.endsWith('/')) {
        normalizedPath = normalizedPath.slice(0, -1);
    }

    const flagPrefix = '/flag/';
    if (!normalizedPath.startsWith(flagPrefix) || normalizedPath.length <= flagPrefix.length) {
        return { slug: null, flagKey: null };
    }

    const slug = decodeFilterUrlToken(normalizedPath.slice(flagPrefix.length));
    return {
        slug,
        flagKey: getFlagFilterKey(slug)
    };
}

function normalizeLegacyLeagueRoute() {
    const url = new URL(window.location.href);
    const route = getLeagueRouteState(url.pathname);
    if (route.slug === 'pheno-improvement') {
        url.pathname = '/league/improvement';
    } else if (route.slug === 'ultimate') {
        url.pathname = originalLeaguelessURL.pathname;
    } else if (route.filterLabel && !leagueFilterRoutes[route.slug]?.canonical) {
        url.pathname = originalLeaguelessURL.pathname;
        if (!url.searchParams.has('filters')) {
            url.searchParams.set('filters', serializeFiltersParam([route.filterLabel.toLowerCase()]));
        }
    } else {
        return;
    }
    history.replaceState(history.state || {}, '', url.toString());
}


if (!isAthleteDialogOnlyRuntime) {
    // Prevent browser auto-restoring scroll on leaderboard history navigation.
    try { if ('scrollRestoration' in history) history.scrollRestoration = 'manual'; } catch { }

    /* ------- ScrollGuard: block untrusted scrolls during anchor jump ------- */
    (function () {
        if (window.__scrollGuardInstalled) return;
        window.__scrollGuardInstalled = true;

        const ORIG = {
            scrollTo: window.scrollTo.bind(window),
            scrollBy: window.scrollBy.bind(window),
            elSIV: Element.prototype.scrollIntoView,
        };
        let guardUntil = 0;
        let anchorTarget = null;

        function blocked() { return Date.now() <= guardUntil; }

        // Public API
        window.ScrollGuard = {
            block(ms = 1600) { guardUntil = Date.now() + ms; },
            trustedScrollTo(opts) { ORIG.scrollTo(opts); },
            trustedScrollIntoView(el, opts) { ORIG.elSIV.call(el, opts); },
            setAnchorTarget(el) { anchorTarget = el || null; }
        };

        // Patch global scroll fns (no-op while blocked)
        window.scrollTo = function (...args) { if (blocked()) return; return ORIG.scrollTo(...args); };
        window.scrollBy = function (...args) { if (blocked()) return; return ORIG.scrollBy(...args); };
        Element.prototype.scrollIntoView = function (...args) {
            if (blocked()) return;
            return ORIG.elSIV.apply(this, args);
        };

        // If *anything* flips the hash during the guard, re-center the intended target
        window.addEventListener('hashchange', () => {
            if (blocked() && anchorTarget) {
                ORIG.elSIV.call(anchorTarget, { behavior: 'auto', block: 'start' });
            }
        }, true);
    })();
}

/* ------- Scroll Performance Optimization ------- */
(function() {
    if (window.__scrollOptimizerInstalled) return;
    window.__scrollOptimizerInstalled = true;

    // Throttle function using requestAnimationFrame
    function throttleRAF(func) {
        let rafId = null;
        return function(...args) {
            if (rafId === null) {
                rafId = requestAnimationFrame(() => {
                    func.apply(this, args);
                    rafId = null;
                });
            }
        };
    }

    // Scroll position memory
    const scrollMemory = {
        store: function(athleteSlug, scrollTop) {
            if (athleteSlug) {
                sessionStorage.setItem(`modalScroll_${athleteSlug}`, String(scrollTop));
            }
        },
        retrieve: function(athleteSlug) {
            if (athleteSlug) {
                const stored = sessionStorage.getItem(`modalScroll_${athleteSlug}`);
                return stored ? parseInt(stored, 10) : null;
            }
            return null;
        },
        clear: function(athleteSlug) {
            if (athleteSlug) {
                sessionStorage.removeItem(`modalScroll_${athleteSlug}`);
            }
        }
    };

    window.ModalScrollOptimizer = {
        throttleRAF: throttleRAF,
        memory: scrollMemory
    };
})();

function canAutoScroll() {
    if (window.ScrollGuard?.isBlocked?.()) return false;
    if (document.body.classList.contains('no-scroll')) return false;
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return false;
    return true;
}

let includePodiumGlobal = true
let maxAthletesGlobal = Infinity;
const CROWD_AGE_LEADERBOARD_MINIMUM_GUESS_COUNT = 100;

function isCompleteBiomarkerSet(set) {
    const values = [
        set.Wbc1000cellsuL,
        set.LymPc,
        set.McvFL,
        set.RdwPc,
        set.AlbGL,
        set.AlpUL,
        set.CreatUmolL,
        set.GluMmolL,
        set.CrpMgL
    ];
    return values.every(Number.isFinite) && set.CrpMgL > 0;
}

function isCompleteBortzBiomarkerSet(entry) {
    if (!entry || !entry.Date) return false;
    const values = [
        entry.AlbGL,
        entry.AlpUL,
        entry.UreaMmolL,
        entry.CholesterolMmolL,
        entry.CreatUmolL,
        entry.CystatinCMgL,
        entry.Hba1cMmolMol,
        entry.CrpMgL,
        entry.GgtUL,
        entry.Rbc10e12L,
        entry.McvFL,
        entry.RdwPc,
        entry.Wbc1000cellsuL,
        entry.MonocytePc,
        entry.NeutrophilPc,
        entry.LymPc,
        entry.AltUL,
        entry.ShbgNmolL,
        entry.VitaminDNmolL,
        entry.GluMmolL,
        entry.MchPg,
        entry.ApoA1GL
    ];
    return values.every(Number.isFinite) && entry.CrpMgL > 0;
}

function ensureRankChangeWrapper(imgEl) {
    let wrapper = imgEl.closest('.rank-change');
    if (!wrapper) {
        wrapper = document.createElement('span');
        wrapper.className = 'rank-change';
        imgEl.replaceWith(wrapper);
        wrapper.appendChild(imgEl);
    }
    return wrapper;
}

function applyRankUpUI(imgEl, placesUp, rowEl) {
    const wrapper = ensureRankChangeWrapper(imgEl);

    // If "new" is active, never show the green arrow
    if (imgEl.classList.contains('is-new')) {
        wrapper.classList.remove('rank-up');
        const existing = wrapper.querySelector('.rank-arrow');
        if (existing) existing.remove();
        if (rowEl) {
            rowEl.classList.remove('is-rank-up-athlete', 'is-rank-up-revealed');
        }
        return;
    }

    // normalize placesUp → at least 1
    const places = Math.max(1, Number(placesUp || 1));
    const msg = places === 1
        ? 'Ranked up 1 place since last month'
        : `Ranked up ${places} places since last month`;

    wrapper.classList.add('rank-up');

    let arrow = wrapper.querySelector('.rank-arrow');
    if (!arrow) {
        arrow = document.createElement('span');
        arrow.className = 'rank-arrow';
        arrow.innerHTML = '<i class="fa fa-arrow-up" aria-hidden="true"></i>';
        wrapper.appendChild(arrow);
    }

    // Tooltip (on bubble *and* on the portrait for reliable hover)
    arrow.title = msg;
    arrow.setAttribute('aria-label', msg);
    imgEl.title = msg; // hovering the portrait shows the same tooltip

    if (rowEl) {
        rowEl.classList.add('is-rank-up-athlete');
        rowEl.classList.remove('is-rank-up-revealed');
        revealRowOnView(rowEl, 'is-rank-up-revealed');
    }
}

function applyNewUI(imgEl, rowEl) {
    const msg = 'New athlete';
    imgEl.title = msg;
    const wrapper = ensureRankChangeWrapper(imgEl);

    let bubble = wrapper.querySelector('.new-bubble');
    if (!bubble) {
        bubble = document.createElement('span');
        bubble.className = 'new-bubble';
        bubble.innerHTML = '<i class="fa fa-arrow-right" aria-hidden="true"></i>';
        wrapper.appendChild(bubble);
    }
    bubble.title = msg;
    bubble.setAttribute('aria-label', msg);

    if (rowEl) {
        rowEl.classList.add('is-new-athlete');
        rowEl.classList.remove('is-new-revealed');
        revealRowOnView(rowEl, 'is-new-revealed');
    }

    // Ensure any green bits are cleared
    wrapper.classList.remove('rank-up');
    const arrow = wrapper.querySelector('.rank-arrow');
    if (arrow) arrow.remove();
}

const rowRevealTargets = new Map();
const rowRevealQueue = [];
let rowRevealFrame = 0;
let rowRevealListening = false;
let rowRevealInProgress = false;
let rowRevealSequence = 0;
const rowRevealStaggerMs = 220;

function revealRowOnView(markerEl, revealedClass) {
    if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;
    rowRevealTargets.set(markerEl, revealedClass);
    if (!rowRevealListening) {
        rowRevealListening = true;
        window.addEventListener('scroll', scheduleRowRevealCheck, { passive: true });
        window.addEventListener('resize', scheduleRowRevealCheck);
    }
    scheduleRowRevealCheck();
}

function scheduleRowRevealCheck() {
    if (rowRevealFrame) return;
    rowRevealFrame = requestAnimationFrame(checkRowReveals);
}

function checkRowReveals() {
    rowRevealFrame = 0;
    const viewportHeight = window.innerHeight || document.documentElement.clientHeight || 0;
    const revealLeadPx = 120;
    rowRevealTargets.forEach((revealedClass, markerEl) => {
        if (!markerEl.isConnected) {
            rowRevealTargets.delete(markerEl);
            return;
        }
        const rect = markerEl.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        if (rect.top < viewportHeight + revealLeadPx && rect.bottom > -revealLeadPx) {
            enqueueRowReveal(markerEl, revealedClass);
            rowRevealTargets.delete(markerEl);
        }
    });
    processRowRevealQueue();
}

function enqueueRowReveal(markerEl, revealedClass) {
    if (markerEl.classList.contains(revealedClass)) return;
    if (rowRevealQueue.some(item => item.markerEl === markerEl && item.revealedClass === revealedClass)) return;
    rowRevealQueue.push({ markerEl, revealedClass });
    rowRevealQueue.sort(compareRowRevealItems);
}

function compareRowRevealItems(a, b) {
    const rankA = Number(a.markerEl.dataset.rank || Number.POSITIVE_INFINITY);
    const rankB = Number(b.markerEl.dataset.rank || Number.POSITIVE_INFINITY);
    if (rankA !== rankB) return rankA - rankB;
    return a.markerEl.sectionRowIndex - b.markerEl.sectionRowIndex;
}

function resetRowRevealQueue() {
    rowRevealTargets.clear();
    rowRevealQueue.length = 0;
    rowRevealInProgress = false;
    rowRevealSequence += 1;
    if (rowRevealFrame) {
        cancelAnimationFrame(rowRevealFrame);
        rowRevealFrame = 0;
    }
}

function processRowRevealQueue() {
    if (rowRevealInProgress) return;
    while (rowRevealQueue.length > 0 && !rowRevealQueue[0].markerEl.isConnected) {
        rowRevealQueue.shift();
    }
    const nextReveal = rowRevealQueue.shift();
    if (!nextReveal) return;

    rowRevealInProgress = true;
    const revealSequence = rowRevealSequence;
    nextReveal.markerEl.classList.add(nextReveal.revealedClass);
    window.setTimeout(() => {
        if (revealSequence !== rowRevealSequence) return;
        rowRevealInProgress = false;
        processRowRevealQueue();
    }, rowRevealStaggerMs);
}

function LoadLeaderboard(includePodium = true, maxAthletes = Infinity) {
    window.initializeSharedPageData();
    includePodiumGlobal = includePodium;
    maxAthletesGlobal = maxAthletes;
    athleteResultsReady = false;
    setLeaderboardLoadingState(includePodium, maxAthletes);

    const currentLoadPromise = window.getSharedAthletes()
        .then(athletes => {
            // Array to store athletes with their lowest PhenoAge and age reduction
            updateViewAllAthletesButton(athletes.length);
            const nowForCrowdAgeUtc = new Date();
            const crowdAgeAsOfUtcDate = new Date(
                nowForCrowdAgeUtc.getUTCFullYear(),
                nowForCrowdAgeUtc.getUTCMonth(),
                nowForCrowdAgeUtc.getUTCDate()
            );
            athleteResults = athletes.map(athlete => {
                // Calculate the chronological age from the athlete's DateOfBirth
                const dob = new Date(
                    athlete.DateOfBirth.Year,
                    athlete.DateOfBirth.Month - 1, // Months are zero-indexed in JavaScript Date
                    athlete.DateOfBirth.Day
                );
                const chronologicalAgeNow = window.calculateAgeAtDate(dob, new Date());
                const chronologicalAgeForCrowdAge = window.calculateAgeAtDate(dob, crowdAgeAsOfUtcDate);

                const crowdAge = athlete.CrowdAge;
                const crowdCount = athlete.CrowdCount;
                const crowdAgeReduction = Number.isFinite(crowdAge) ? crowdAge - chronologicalAgeForCrowdAge : null;
                const completeBiomarkerSets = athlete.Biomarkers.filter(isCompleteBiomarkerSet);

                // Get birth year and generation
                const birthYear = athlete.DateOfBirth.Year;
                const generation = window.getGeneration(birthYear);
                const displayName = (athlete.DisplayName && athlete.DisplayName.trim()) ? athlete.DisplayName.trim() : athlete.Name;


                // Create an object to store the best biomarker values from all sets
                let bestBiomarkers = {
                    Wbc1000cellsuL: null, // White blood cell count (lower is better)
                    LymPc: null,          // Lymphocytes (higher is better)
                    McvFL: null,          // Mean corpuscular volume (lower is better)
                    RdwPc: null,          // Red cell distribution width (lower is better)
                    AlbGL: null,          // Albumin (higher is better)
                    AlpUL: null,          // Alkaline phosphatase (lower is better)
                    CreatUmolL: null,     // Creatinine (lower is better)
                    GluMmolL: null,       // Glucose (lower is better)
                    CrpMgL: null          // CRP (lower is better)
                };

                completeBiomarkerSets.forEach(biomarkerSet => {
                    // White blood cell count: positive coefficient means lower is better
                    if (bestBiomarkers.Wbc1000cellsuL === null || biomarkerSet.Wbc1000cellsuL < bestBiomarkers.Wbc1000cellsuL) {
                        bestBiomarkers.Wbc1000cellsuL = biomarkerSet.Wbc1000cellsuL;
                    }
                    // Lymphocytes: negative coefficient means higher is better
                    if (bestBiomarkers.LymPc === null || biomarkerSet.LymPc > bestBiomarkers.LymPc) {
                        bestBiomarkers.LymPc = biomarkerSet.LymPc;
                    }
                    // Mean corpuscular volume: positive coefficient means lower is better
                    if (bestBiomarkers.McvFL === null || biomarkerSet.McvFL < bestBiomarkers.McvFL) {
                        bestBiomarkers.McvFL = biomarkerSet.McvFL;
                    }
                    // Red cell distribution width: positive coefficient means lower is better
                    if (bestBiomarkers.RdwPc === null || biomarkerSet.RdwPc < bestBiomarkers.RdwPc) {
                        bestBiomarkers.RdwPc = biomarkerSet.RdwPc;
                    }
                    // Albumin: negative coefficient means higher is better
                    if (bestBiomarkers.AlbGL === null || biomarkerSet.AlbGL > bestBiomarkers.AlbGL) {
                        bestBiomarkers.AlbGL = biomarkerSet.AlbGL;
                    }
                    // Alkaline phosphatase: positive coefficient means lower is better
                    if (bestBiomarkers.AlpUL === null || biomarkerSet.AlpUL < bestBiomarkers.AlpUL) {
                        bestBiomarkers.AlpUL = biomarkerSet.AlpUL;
                    }
                    // Creatinine: positive coefficient means lower is better
                    if (bestBiomarkers.CreatUmolL === null || biomarkerSet.CreatUmolL < bestBiomarkers.CreatUmolL) {
                        bestBiomarkers.CreatUmolL = biomarkerSet.CreatUmolL;
                    }
                    // Glucose: positive coefficient means lower is better
                    if (bestBiomarkers.GluMmolL === null || biomarkerSet.GluMmolL < bestBiomarkers.GluMmolL) {
                        bestBiomarkers.GluMmolL = biomarkerSet.GluMmolL;
                    }
                    // CRP: positive coefficient means lower is better
                    if (bestBiomarkers.CrpMgL === null || biomarkerSet.CrpMgL < bestBiomarkers.CrpMgL) {
                        bestBiomarkers.CrpMgL = biomarkerSet.CrpMgL;
                    }
                });

                let phenoAgeDifference = 0;
                let phenoAgeImprovement = null;
                // Only proceed if there are submissions.
                if (completeBiomarkerSets.length > 0) {
                    const firstSubmission = completeBiomarkerSets[0];
                    const lastSubmission  = completeBiomarkerSets[completeBiomarkerSets.length - 1];

                    const ageAtFirst = window.calculateAgeAtDate(dob, new Date(firstSubmission.Date));
                    const ageAtLast  = window.calculateAgeAtDate(dob, new Date(lastSubmission.Date));

                    const firstSubmissionValues = [
                        ageAtFirst,
                        firstSubmission.AlbGL,
                        firstSubmission.CreatUmolL,
                        firstSubmission.GluMmolL,
                        Math.log(firstSubmission.CrpMgL / 10),
                        firstSubmission.Wbc1000cellsuL,
                        firstSubmission.LymPc,
                        firstSubmission.McvFL,
                        firstSubmission.RdwPc,
                        firstSubmission.AlpUL
                    ];

                    const lastSubmissionValues = [
                        ageAtLast,
                        lastSubmission.AlbGL,
                        lastSubmission.CreatUmolL,
                        lastSubmission.GluMmolL,
                        Math.log(lastSubmission.CrpMgL / 10),
                        lastSubmission.Wbc1000cellsuL,
                        lastSubmission.LymPc,
                        lastSubmission.McvFL,
                        lastSubmission.RdwPc,
                        lastSubmission.AlpUL
                    ];

                    const firstPhenoAge = window.PhenoAge.calculatePhenoAge(firstSubmissionValues);
                    const lastPhenoAge = window.PhenoAge.calculatePhenoAge(lastSubmissionValues);

                    phenoAgeDifference = lastPhenoAge - firstPhenoAge;
                }

                if (completeBiomarkerSets.length >= 2) {
                    const phenoSubmissionAges = completeBiomarkerSets.map((submission, index) => {
                        const submittedAt = new Date(submission.Date);
                        const ageAtSubmission = window.calculateAgeAtDate(dob, submittedAt);
                        const values = [
                            ageAtSubmission,
                            submission.AlbGL,
                            submission.CreatUmolL,
                            submission.GluMmolL,
                            Math.log(submission.CrpMgL / 10),
                            submission.Wbc1000cellsuL,
                            submission.LymPc,
                            submission.McvFL,
                            submission.RdwPc,
                            submission.AlpUL
                        ];
                        const phenoAge = values.every(Number.isFinite)
                            ? window.PhenoAge.calculatePhenoAge(values)
                            : null;
                        return {
                            submittedAt,
                            index,
                            phenoAge
                        };
                    }).filter(result => Number.isFinite(result.phenoAge));

                    if (phenoSubmissionAges.length >= 2) {
                        const latestSubmission = phenoSubmissionAges.slice().sort((a, b) => {
                            const timeDiff = a.submittedAt.getTime() - b.submittedAt.getTime();
                            return timeDiff !== 0 ? timeDiff : a.index - b.index;
                        }).at(-1);
                        const worstPhenoAge = Math.max(...phenoSubmissionAges.map(result => result.phenoAge));
                        phenoAgeImprovement = latestSubmission.phenoAge - worstPhenoAge;
                    }
                }

                const submissionCount = completeBiomarkerSets.length;
                // Prepare the biomarker values array in the order required for the PhenoAge calculation
                const bestBiomarkerValues = [
                    bestBiomarkers.AlbGL, bestBiomarkers.CreatUmolL, bestBiomarkers.GluMmolL,
                    bestBiomarkers.CrpMgL, bestBiomarkers.Wbc1000cellsuL, bestBiomarkers.LymPc,
                    bestBiomarkers.McvFL, bestBiomarkers.RdwPc, bestBiomarkers.AlpUL
                ].every(Number.isFinite) && bestBiomarkers.CrpMgL > 0
                    ? [
                        chronologicalAgeNow,
                        bestBiomarkers.AlbGL,
                        bestBiomarkers.CreatUmolL,
                        bestBiomarkers.GluMmolL,
                        Math.log(bestBiomarkers.CrpMgL / 10),
                        bestBiomarkers.Wbc1000cellsuL,
                        bestBiomarkers.LymPc,
                        bestBiomarkers.McvFL,
                        bestBiomarkers.RdwPc,
                        bestBiomarkers.AlpUL
                    ]
                    : null;

                // Compute the actually lowest PhenoAge across real submissions
                let lowestPhenoAge = Infinity;
                let valuesForLowestPhenoAge = null;

                athlete.Biomarkers.forEach(entry => {
                    const entryDate = new Date(entry.Date);
                    const ageAtEntry = window.calculateAgeAtDate(dob, entryDate);

                    const candidateValues = [
                        ageAtEntry,
                        entry.AlbGL,
                        entry.CreatUmolL,
                        entry.GluMmolL,
                        Math.log(entry.CrpMgL / 10),
                        entry.Wbc1000cellsuL,
                        entry.LymPc,
                        entry.McvFL,
                        entry.RdwPc,
                        entry.AlpUL
                    ];

                    // Only consider submissions with complete numeric data
                    if (candidateValues.every(Number.isFinite)) {
                        const ph = window.PhenoAge.calculatePhenoAge(candidateValues);
                        if (ph < lowestPhenoAge) {
                            lowestPhenoAge = ph;
                            valuesForLowestPhenoAge = candidateValues;
                        }
                    }
                });

                const chronoAtLowestPhenoAge =
                    Number.isFinite(lowestPhenoAge) && valuesForLowestPhenoAge
                        ? valuesForLowestPhenoAge[0]
                        : chronologicalAgeNow;

                // Fallback: if nothing valid (should be rare), use the last submission
                if (!Number.isFinite(lowestPhenoAge)) {
                    if (completeBiomarkerSets.length > 0) {
                        const last = completeBiomarkerSets[completeBiomarkerSets.length - 1];
                        const ageAtLast = window.calculateAgeAtDate(dob, new Date(last.Date));
                        valuesForLowestPhenoAge = [
                            ageAtLast,
                            last.AlbGL,
                            last.CreatUmolL,
                            last.GluMmolL,
                            Math.log(last.CrpMgL / 10),
                            last.Wbc1000cellsuL,
                            last.LymPc,
                            last.McvFL,
                            last.RdwPc,
                            last.AlpUL
                        ];
                        lowestPhenoAge = window.PhenoAge.calculatePhenoAge(valuesForLowestPhenoAge);
                    } else {
                        lowestPhenoAge = chronologicalAgeNow; // no complete data → neutral (no reduction/acceleration)
                    }
                }

                // Calculate the age reduction
                const ageReduction = lowestPhenoAge - chronoAtLowestPhenoAge;
                const biologicalAgePercent = lowestPhenoAge / chronoAtLowestPhenoAge;
                const ageReductionPercent = (1 - biologicalAgePercent) * 100;

                // Lowest bortz age and bortz age reduction (only when submissions have full Bortz biomarkers)
                let lowestBortzAge = null;
                let chronoAtLowestBortzAge = null;
                let bortzAgeReduction = null;
                let bortzAgeReductionPercent = null;
                let bortzAgeDifference = null;
                let bortzAgeImprovement = null;
                let bestBortzValues = null;
                if (window.BortzAge && typeof window.BortzAge.calculateBortzAge === 'function') {
                    let bortzMin = Infinity;
                    let chronoAtBortzMin = null;
                    const completeBortzBiomarkerSets = athlete.Biomarkers.filter(isCompleteBortzBiomarkerSet);
                    const buildBortzValues = (entry) => {
                        const entryDate = new Date(entry.Date);
                        const ageAtEntry = window.calculateAgeAtDate(dob, entryDate);
                        const wbc = entry.Wbc1000cellsuL;
                        const monoCount = wbc * entry.MonocytePc / 100;
                        const neutCount = wbc * entry.NeutrophilPc / 100;
                        return {
                            ageAtEntry,
                            values: [
                                ageAtEntry,
                                entry.AlbGL,
                                entry.AlpUL,
                                entry.UreaMmolL,
                                entry.CholesterolMmolL,
                                entry.CreatUmolL,
                                entry.CystatinCMgL,
                                entry.Hba1cMmolMol,
                                entry.CrpMgL,
                                entry.GgtUL,
                                entry.Rbc10e12L,
                                entry.McvFL,
                                entry.RdwPc,
                                monoCount,
                                neutCount,
                                entry.LymPc,
                                entry.AltUL,
                                entry.ShbgNmolL,
                                entry.VitaminDNmolL,
                                entry.GluMmolL,
                                entry.MchPg,
                                entry.ApoA1GL
                            ]
                        };
                    };

                    if (completeBortzBiomarkerSets.length > 0) {
                        const firstBortzSubmission = buildBortzValues(completeBortzBiomarkerSets[0]);
                        const lastBortzSubmission = buildBortzValues(completeBortzBiomarkerSets[completeBortzBiomarkerSets.length - 1]);
                        const firstBortzAge = window.BortzAge.calculateBortzAge(firstBortzSubmission.ageAtEntry, firstBortzSubmission.values);
                        const lastBortzAge = window.BortzAge.calculateBortzAge(lastBortzSubmission.ageAtEntry, lastBortzSubmission.values);
                        if (Number.isFinite(firstBortzAge) && Number.isFinite(lastBortzAge)) {
                            bortzAgeDifference = lastBortzAge - firstBortzAge;
                        }
                    }

                    if (completeBortzBiomarkerSets.length >= 2) {
                        const bortzSubmissionAges = completeBortzBiomarkerSets.map((entry, index) => {
                            const submittedAt = new Date(entry.Date);
                            const { ageAtEntry, values: bortzValues } = buildBortzValues(entry);
                            const bortzAge = window.BortzAge.calculateBortzAge(ageAtEntry, bortzValues);
                            return {
                                submittedAt,
                                index,
                                bortzAge
                            };
                        }).filter(result => Number.isFinite(result.bortzAge));

                        if (bortzSubmissionAges.length >= 2) {
                            const latestSubmission = bortzSubmissionAges.slice().sort((a, b) => {
                                const timeDiff = a.submittedAt.getTime() - b.submittedAt.getTime();
                                return timeDiff !== 0 ? timeDiff : a.index - b.index;
                            }).at(-1);
                            const worstBortzAge = Math.max(...bortzSubmissionAges.map(result => result.bortzAge));
                            bortzAgeImprovement = latestSubmission.bortzAge - worstBortzAge;
                        }
                    }

                    completeBortzBiomarkerSets.forEach(entry => {
                        const { ageAtEntry, values: bortzValues } = buildBortzValues(entry);
                        const bortzAge = window.BortzAge.calculateBortzAge(ageAtEntry, bortzValues);
                        if (Number.isFinite(bortzAge) && bortzAge < bortzMin) {
                            bortzMin = bortzAge;
                            chronoAtBortzMin = ageAtEntry;
                            bestBortzValues = bortzValues;
                        }
                    });
                    if (Number.isFinite(bortzMin) && chronoAtBortzMin != null) {
                        lowestBortzAge = bortzMin;
                        chronoAtLowestBortzAge = chronoAtBortzMin;
                        bortzAgeReduction = lowestBortzAge - chronoAtLowestBortzAge;
                        bortzAgeReductionPercent = (1 - lowestBortzAge / chronoAtLowestBortzAge) * 100;
                    }
                }

                // Placements: [yesterday, last week, last month, last year]
                // Store last month's rank (index 2). If missing or not a number, use null.
                const lastMonthRank =
                    Array.isArray(athlete.Placements) && Number.isFinite(athlete.Placements[2])
                        ? athlete.Placements[2]
                        : null;

                // “Higher rank” = smaller number than last month
                const rankImproved =
                    Number.isFinite(lastMonthRank) && typeof lastMonthRank === 'number'
                        ? (/* current rank is set a bit later */ true) // placeholder; we’ll re-evaluate after rank assignment
                        : false;

                const rawFlag = athlete.Flag || '';
                const canonicalFlag = getCanonicalFlagName(rawFlag);

                return {
                    name: athlete.Name,
                    displayName: displayName,
                    athleteSlug: athlete.AthleteSlug,
                    mediaContact: athlete.MediaContact,
                    dateOfBirth: dob,
                    chronologicalAge: chronologicalAgeNow,
                    crowdAge: crowdAge,
                    crowdCount: crowdCount,
                    crowdAgeReduction: crowdAgeReduction,
                    lowestBortzAge: lowestBortzAge,
                    chronoAtLowestBortzAge: chronoAtLowestBortzAge,
                    bortzAgeReduction: bortzAgeReduction,
                    bortzAgeReductionPercent: bortzAgeReductionPercent,
                    lowestPhenoAge: lowestPhenoAge,
                    chronoAtLowestPhenoAge: chronoAtLowestPhenoAge,
                    ageReduction: ageReduction,
                    ageReductionPercent: ageReductionPercent,
                    bestBiomarkerValues: bestBiomarkerValues,
                    bestBortzValues: bestBortzValues,
                    personalLink: athlete.PersonalLink,
                    profilePic: athlete.ProfilePic,
                    profilePicThumb: athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb || athlete.ProfilePic,
                    profileImageId: athlete.ProfileImageId,
                    flag: rawFlag,
                    canonicalFlag: canonicalFlag,
                    flagFilterKey: getFlagFilterKey(canonicalFlag),
                    why: athlete.Why || '',
                    division: athlete.Division,
                    generation: generation,
                    exclusiveLeague: athlete.ExclusiveLeague,
                    submissionCount: submissionCount,
                    phenoAgeDifference: phenoAgeDifference,
                    phenoAgeImprovement: Number.isFinite(athlete.PhenoAgeImprovementFromWorst)
                        ? athlete.PhenoAgeImprovementFromWorst
                        : phenoAgeImprovement,
                    bortzAgeImprovement: Number.isFinite(athlete.BortzAgeImprovementFromWorst)
                        ? athlete.BortzAgeImprovementFromWorst
                        : bortzAgeImprovement,
                    PhenoAgeDiffFromBaseline: athlete.PhenoAgeDiffFromBaseline,
                    BortzAgeDiffFromBaseline: Number.isFinite(athlete.BortzAgeDiffFromBaseline)
                        ? athlete.BortzAgeDiffFromBaseline
                        : bortzAgeDifference,
                    podcastLink: athlete.PodcastLink,
                    lastMonthRank: lastMonthRank,
                    rankImproved: rankImproved,
                    isNew: !!athlete.IsNew,
                    Badges: athlete.Badges || [],
                };
            });

            const getDisplayAgeReductionValue = (athlete) =>
                (athlete.bortzAgeReduction != null && Number.isFinite(athlete.bortzAgeReduction))
                    ? athlete.bortzAgeReduction
                    : athlete.ageReduction;
            const getDisplayAgeReductionRoundedToTenths = (athlete) => {
                const displayAgeReduction = getDisplayAgeReductionValue(athlete);
                return Number.isFinite(displayAgeReduction) ? displayAgeReduction.toFixed(1) : null;
            };
            const getDisplayAgeReductionDecimals = (athletes, index) => {
                const currentRounded = getDisplayAgeReductionRoundedToTenths(athletes[index]);
                if (currentRounded == null) return 1;

                const previousRounded = index > 0
                    ? getDisplayAgeReductionRoundedToTenths(athletes[index - 1])
                    : null;
                const nextRounded = index < athletes.length - 1
                    ? getDisplayAgeReductionRoundedToTenths(athletes[index + 1])
                    : null;

                return currentRounded === previousRounded || currentRounded === nextRounded ? 2 : 1;
            };

            if (!isAthleteDialogOnlyRuntime) {
                generateDivisionFilters(athleteResults);
                generateFlagFilters(athleteResults);
                generateGenerationFilters(athleteResults);
                generateExclusiveFilters(athleteResults);
                generateLeagueTrackFilters(athleteResults);
            }

            // Sort the athletes by age reduction in ascending order
            athleteResults.sort(window.compareAthleteRank);

            // Assign ranks to each athlete based on the sorted order
            athleteResults.forEach((athlete, index) => {
                athlete.rank = index + 1;
                athlete.ageReductionDisplayDecimals = getDisplayAgeReductionDecimals(athleteResults, index);
            });

            athleteResults.forEach(a => {
                a.rankImproved =
                    Number.isFinite(a.lastMonthRank) && a.rank < a.lastMonthRank;
            });

            // Initialize a 'ranks' object for each athlete to store ranks based on different criteria
            athleteResults.forEach(athlete => { athlete.ranks = {}; });

            // Assign ranks based on divisions
            const divisions = [...new Set(athleteResults.map(athlete => athlete.division))];
            divisions.forEach(division => {
                const athletesInDivision = athleteResults.filter(athlete => athlete.division === division);
                athletesInDivision.sort(window.compareAthleteRank);
                athletesInDivision.forEach((athlete, index) => {
                    athlete.ranks[division] = index + 1;
                });
            });

            // Assign ranks based on generations
            const generations = [...new Set(athleteResults.map(athlete => athlete.generation))];
            generations.forEach(generation => {
                const athletesInGeneration = athleteResults.filter(athlete => athlete.generation === generation);
                athletesInGeneration.sort(window.compareAthleteRank);
                athletesInGeneration.forEach((athlete, index) => {
                    athlete.ranks[generation] = index + 1;
                });
            });

            // Assign ranks based on combinations of division and generation
            divisions.forEach(division => {
                generations.forEach(generation => {
                    const athletesInCombo = athleteResults.filter(
                        athlete => athlete.division === division && athlete.generation === generation
                    );
                    athletesInCombo.sort(window.compareAthleteRank);
                    athletesInCombo.forEach((athlete, index) => {
                        const key = `${generation}_${division}`;
                        athlete.ranks[key] = index + 1;
                    });
                });
            });

            // Assign ranks based on exclusive leagues (e.g. Prosperan)
            const exclusiveLeagues = [...new Set(athleteResults.map(a => a.exclusiveLeague).filter(Boolean))];
            exclusiveLeagues.forEach(exclusiveLeague => {
                const athletesInExclusive = athleteResults.filter(a => a.exclusiveLeague === exclusiveLeague);
                athletesInExclusive.sort(window.compareAthleteRank);
                athletesInExclusive.forEach((athlete, index) => {
                    athlete.ranks[exclusiveLeague] = index + 1;
                });
            });

            // Assign ranks based on flags
            const flagRankKeys = [...new Set(athleteResults.map(a => a.flagFilterKey).filter(Boolean))];
            flagRankKeys.forEach(flagKey => {
                const athletesInFlag = athleteResults.filter(a => a.flagFilterKey === flagKey);
                athletesInFlag.sort(window.compareAthleteRank);
                athletesInFlag.forEach((athlete, index) => {
                    const key = getFlagRankKey(athlete.canonicalFlag);
                    if (key) athlete.ranks[key] = index + 1;
                });
            });

            athletesOrderedByPace = orderByNumberDesc(athleteResults, a => a.ageReductionPercent);
            athletesOrderedByBortzPace = orderByNumberAsc(
                athleteResults.filter(a => a.lowestBortzAge != null && a.chronoAtLowestBortzAge != null && Number.isFinite(a.lowestBortzAge / a.chronoAtLowestBortzAge)),
                a => a.lowestBortzAge / a.chronoAtLowestBortzAge
            );
            assignBestRankCandidates(athleteResults);
            athleteResultsReady = true;

            // Standalone pages reuse the leaderboard's exact dialog and rank
            // derivation, but do not own a leaderboard DOM to render into.
            if (isAthleteDialogOnlyRuntime) {
                return;
            }

            const podiumCount = includePodium ? 3 : 0;

            if (includePodium) {
                // Winners are available now; prize services must not delay their cards.
                // Get the top three athletes from athleteResults
                const topThree = athleteResults.slice(0, 3);

                // Select the podium container and clear existing items
                const podium = document.querySelector('.podium');
                podium.innerHTML = ''; // Clear any existing podium items
                podium.removeAttribute('data-server-rendered');
                podium.setAttribute('aria-busy', 'false');

                // Loop through the top three athletes and create podium items
                topThree.forEach(athlete => {
                    let rankClass, rankEmoji;

                    switch (athlete.rank) {
                        case 1:
                            rankClass = 'first';
                            rankEmoji = '<i class="fa-solid fa-crown" style="color: gold;"></i>';
                            break;
                        case 2:
                            rankClass = 'second';
                            rankEmoji = '<i class="fa-solid fa-medal" style="color: silver;"></i>';
                            break;
                        case 3:
                            rankClass = 'third';
                            rankEmoji = '<i class="fa-solid fa-award" style="color: #cd7f32;"></i>';
                            break;
                        default:
                            return; // Only handle top three
                    }

                    // Create the podium-item div
                    const podiumItem = document.createElement('div');
                    podiumItem.classList.add('podium-item', rankClass);

                    const podiumPortraitUrl = getAthletePortraitUrl(athlete);

                    // Populate the inner HTML of podiumItem
                    podiumItem.innerHTML = `
                        <div class="podium-rank" title="${athlete.rank === 1 ? '1st Place' : athlete.rank === 2 ? '2nd Place' : '3rd Place'}">
                            ${rankEmoji}
                        </div>
                        <a class="athlete-profile-link" href="${escapeHtml(getAthleteProfileHref(athlete))}" aria-label="View stats of ${escapeHtml(athlete.displayName)}"><img src="${podiumPortraitUrl}" alt="${athlete.displayName} portrait" class="podium-portrait" loading="lazy"></a>
                        <div class="name-row">
                            <a class="athlete-name" href="${escapeHtml(getAthleteProfileHref(athlete))}" title="View stats of ${escapeHtml(athlete.displayName)}">${escapeHtml(athlete.displayName)}</a>
                        </div>
                        <div class="podium-link-row">
                            ${renderPersonalLink(athlete.personalLink, athlete.displayName)}
                            ${renderPodcastLink(athlete.podcastLink, athlete.displayName)}
                            ${renderMediaContact(athlete.mediaContact, true, athlete.displayName)}
                        </div>
                        <div><span class="age-reduction">${Math.abs(getDisplayAgeReductionValue(athlete)).toFixed(athlete.ageReductionDisplayDecimals || 1)} years</span> reduced</div>
                        <a class="podium-item-lower" href="#contribute" aria-label="Donate to the prize pool" aria-busy="true">
                            <div class="btc-amount">&nbsp;</div>
                            <div class="prize-money">&mdash;</div>
                        </a>
                    `;

                    // Append the podiumItem to the podium container
                    podium.appendChild(podiumItem);
                    wirePortraitFallback(podiumItem.querySelector('img.podium-portrait'), athlete);

                    // After appending the podium item
                    podiumItem.setAttribute('data-division', athlete.division.toLowerCase());
                    podiumItem.setAttribute('data-generation', athlete.generation.toLowerCase());
                    podiumItem.setAttribute('data-athlete-name', athlete.name);
                    attachAthleteContainerClickListener(podiumItem);

                });

                // Attach click event listeners to athlete names
                attachAthleteNameClickListeners();

                animatePodiumContent();
                addClickListenerToImages('.portrait, .podium-portrait', handleAthleteNameClick);
                addClickListenerToImages('#modalProfilePic', function () {
                    openEnlargedView(this);
                }, img => `Enlarge ${img.alt || 'image'}`);

                updatePodiumPrizes(podium);
            } else {
                // Hide the podium container if not included
                const podium = document.querySelector('.podium');
                if (podium) {
                    podium.style.display = 'none';
                    podium.innerHTML = '';
                    podium.setAttribute('aria-busy', 'false');
                }
            }

            window.computeBadges(athleteResults);

            // Populate the table with athletes ranked >3
            const tableBody = document.querySelector('.leaderboard table tbody');
            tableBody.classList.remove('loading-skeleton');
            tableBody.setAttribute('aria-busy', 'false');
            tableBody.removeAttribute('data-server-rendered');
            tableBody.removeAttribute('data-hydrating');
            const remainingAthletes = athleteResults;

            tableBody.innerHTML = ''; // Clear existing table rows
            resetRowRevealQueue();
            let lastVisibleTier = null; // 'pro' | 'amateur' among rows actually shown in the table
            remainingAthletes.forEach((athlete, index) => {
                const isPro = athlete.bortzAgeReduction != null && Number.isFinite(athlete.bortzAgeReduction);
                const tier = isPro ? 'pro' : 'amateur';
                const rowWillBeHiddenByPodium = includePodium && athlete.rank <= podiumCount;
                const rowWillBeHiddenByMaxAthletes = index >= maxAthletes;
                const rowWillBeVisibleInTable = !rowWillBeHiddenByPodium && !rowWillBeHiddenByMaxAthletes;
                if (rowWillBeVisibleInTable && lastVisibleTier === 'pro' && tier === 'amateur') {
                    const sep = document.createElement('tr');
                    sep.className = 'tier-separator';
                    sep.setAttribute('aria-label', 'Start of Amateur track (Pheno Age) section');
                    sep.innerHTML = '<td colspan="5" class="tier-separator-cell"><span class="tier-separator-label">Amateur track |<a href="https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/pheno-age.pdf" target="_blank" rel="noopener" class="tier-separator-link">Pheno Age</a></span></td>';
                    tableBody.appendChild(sep);
                }
                if (rowWillBeVisibleInTable) {
                    lastVisibleTier = tier;
                }

                const template = document.getElementById('leaderboardRowTemplate');
                const row = template.content.firstElementChild.cloneNode(true);

                row.classList.add('tier-' + tier);
                row.setAttribute('data-tier', tier);
                row.setAttribute('data-rank', athlete.rank);

                row.querySelector('.rank').textContent = athlete.rank;
                const athleteCell = row.querySelector('.athlete-td');
                const portraitImg = athleteCell.querySelector('img.portrait');
                portraitImg.src = getAthletePortraitUrl(athlete);
                portraitImg.alt = `${athlete.displayName} portrait`;
                wirePortraitFallback(portraitImg, athlete);
                if (athlete.isNew) {
                    portraitImg.classList.add('is-new');
                    applyNewUI(portraitImg, row);
                }
                if (athlete.rankImproved) {
                    const placesUp = athlete.lastMonthRank - athlete.rank; // e.g. 12 → 7  => 5 places up
                    applyRankUpUI(portraitImg, placesUp, row);
                }

                const athleteNameSpan = athleteCell.querySelector('.athlete-name');
                athleteNameSpan.href = getAthleteProfileHref(athlete);
                const portraitLink = athleteCell.querySelector('.athlete-profile-link');
                portraitLink.href = getAthleteProfileHref(athlete);
                portraitLink.setAttribute('aria-label', `View stats of ${athlete.displayName}`);
                athleteNameSpan.innerHTML = formatAthleteNameForMobile(athlete.displayName);
                athleteNameSpan.title = `View stats of ${athlete.displayName}`;

                window.setBadges(athlete, athleteCell);

                const displayAgeReduction = getDisplayAgeReductionValue(athlete);
                const displayAgeReductionDecimals = athlete.ageReductionDisplayDecimals || 1;
                const ageReductionSpan = row.querySelector('.age-reduction');
                ageReductionSpan.textContent = (displayAgeReduction > 0 ? '+' : '') + displayAgeReduction.toFixed(displayAgeReductionDecimals) + ' years';

                const mediaContactCell = row.querySelector('.media-contact-td');
                mediaContactCell.innerHTML = renderMediaContact(athlete.mediaContact, false, athlete.displayName);

                row.setAttribute('data-division', athlete.division.toLowerCase().trim());
                row.setAttribute('data-generation', athlete.generation.trim());
                row.setAttribute('data-athlete-name', athlete.name);

                if (rowWillBeHiddenByPodium) {
                    row.style.display = 'none';
                    row.classList.add('podium-athlete');
                }

                if (rowWillBeHiddenByMaxAthletes) {
                    row.style.display = 'none';
                    row.classList.add('extra-athlete');
                }

                row.id = `rank-${athlete.rank}`;
                tableBody.appendChild(row);
                attachAthleteContainerClickListener(row);
                attachAthleteNameClickListeners();
                addClickListenerToImages('#modalProfilePic', function () {
                    openEnlargedView(this);
                }, img => `Enlarge ${img.alt || 'athlete profile picture'}`);
                addClickListenerToImages('.portrait, .podium-portrait', handleAthleteNameClick);
            });
            scheduleLeaderboardTableHeightSync();

            // At the end of the athlete fetch block in LoadLeaderboard, after athleteResults has been populated:
            const leagueRouteState = getLeagueRouteState(window.location.pathname);
            const flagRouteState = getFlagRouteState(window.location.pathname);
            const athleteParam = getInitialAthleteSlug();
            if (athleteParam) {
                const normalizedAthlete = window.slugifyName(athleteParam, false);
                const athleteData = getAthleteData(normalizedAthlete);
                if (athleteData) {
                    fetchFullAthleteData(normalizedAthlete, athleteData, { historyMode: 'replace' });
                } else {
                    // Redirect to the .NET 404 endpoint
                    window.location.href = '/error/404';
                }
            } else {
                let searchOrFilterApplied = false; // Flag to check if filter was applied
                const searchParam = new URLSearchParams(window.location.search).get('search') || '';
                if (searchParam) {
                    // Shared search links carry an extra encoding layer. A
                    // literal percent in a direct URL is also valid search text.
                    let query = searchParam;
                    try {
                        query = decodeURIComponent(searchParam);
                    } catch (_) {
                    }
                    const searchInput = document.getElementById('athleteSearch');
                    searchInput.value = query;
                    searchOrFilterApplied = true;
                }

                const filtersParam = new URLSearchParams(window.location.search).get('filters');
                if (filtersParam) {
                    const filtersList = parseFiltersParam(filtersParam);
                    filtersList.forEach(filterValue => {
                        const normalizedFilter = filterValue.trim().toLowerCase();
                        document.querySelectorAll('input[type="checkbox"]').forEach(checkbox => {
                            const checkboxMatchesFilter = checkbox.name === 'flag'
                                ? getFlagFilterKey(checkbox.value) === getFlagFilterKey(normalizedFilter)
                                : checkbox.value.toLowerCase() === normalizedFilter;
                            if (checkboxMatchesFilter) {
                                checkbox.checked = true;
                            }
                        });
                    });
                    searchOrFilterApplied = true;
                } else if (flagRouteState.flagKey) {
                    document.querySelectorAll('input[name="flag"]').forEach(checkbox => {
                        if (getFlagFilterKey(checkbox.value) === flagRouteState.flagKey) {
                            checkbox.checked = true;
                            searchOrFilterApplied = true;
                        }
                    });
                } else if (leagueRouteState.filterLabel) {
                    const normalizedFilter = leagueRouteState.filterLabel.trim().toLowerCase();
                    document.querySelectorAll('input[type="checkbox"]').forEach(checkbox => {
                        if (checkbox.value.toLowerCase() === normalizedFilter) {
                            checkbox.checked = true;
                        }
                    });
                    searchOrFilterApplied = true;
                }

                const rawViewParam = new URLSearchParams(window.location.search).get('view') || leagueRouteState.view;
                const viewParam = rawViewParam && rawViewParam.toLowerCase() === 'pheno-improvement'
                    ? 'improvement'
                    : rawViewParam;
                if (viewParam && (viewParam.toLowerCase() === 'bortz' || viewParam.toLowerCase() === 'pheno' || viewParam.toLowerCase() === 'improvement' || viewParam.toLowerCase() === 'bortz-improvement' || viewParam.toLowerCase() === 'crowd')) {
                    const viewId = 'view-' + viewParam.toLowerCase();
                    const viewRadio = document.getElementById(viewId);
                    if (viewRadio) {
                        viewRadio.checked = true;
                        searchOrFilterApplied = true;
                    }
                }
                const selectedViewRadio = document.querySelector('input[name="leaderboardView"]:checked');
                updateRankingExplanation(selectedViewRadio ? selectedViewRadio.value : 'ultimate');
                updateLeaderboardTitles(getSelectedLeaderboardState());

                if (searchOrFilterApplied) {
                    toggleClearIcon();

                    // Even here scroll to the search when filter applied because the table kinda starts there conceptually
                    setTimeout(() => {
                        if (canAutoScroll()) {
                            searchInput.scrollIntoView({ behavior: 'smooth', block: 'start' });
                        }
                    }, 1000);
                }
            }

            if (!isAthleteDialogOnlyRuntime) {
                // Apply the complete incoming selection before locating its athlete.
                // Hydration must not remove the rank fragment or rewrite the shared URL.
                performFilter({ updateUrl: false });
                const hash = window.location.hash;
                if (hash && /^#rank-\d+$/.test(hash)) {
                    const target = document.querySelector(hash);
                    if (target?.getClientRects().length) {
                        ScrollGuard.setAnchorTarget(target);
                        ScrollGuard.trustedScrollIntoView(target, { behavior: 'smooth', block: 'start' });
                        ScrollGuard.block(1600);
                    }
                }
            }
            setLeaderboardStatus('Leaderboard loaded.');

            // Pick up dynamically inserted [data-aos] nodes
            if (window.AOS && typeof AOS.refreshHard === 'function') {
                AOS.refreshHard();
            } else if (window.AOS && typeof AOS.refresh === 'function') {
                AOS.refresh();
            }
        })
        .catch(error => {
            athleteResultsReady = false;
            athleteResultsLoadPromise = null;
            const tableBody = document.querySelector('.leaderboard table tbody');
            if (tableBody) {
                tableBody.classList.remove('loading-skeleton');
                tableBody.setAttribute('aria-busy', 'false');
                tableBody.removeAttribute('data-hydrating');
                renderLeaderboardLoadError(tableBody);
            }
            setLeaderboardStatus('Leaderboard could not load.', true);
            updateLeaderboardStructuredData(null);
            const podium = document.querySelector('.podium');
            if (podium && podium.dataset.serverRendered !== 'true') {
                podium.innerHTML = '';
                podium.setAttribute('aria-busy', 'false');
            }
            const serverProfile = document.querySelector('[data-server-rendered-profile]');
            if (serverProfile && modal.style.display === 'block') {
                handleAthleteModalLoadFailure(serverProfile, serverProfile.dataset.athleteSlug, { historyMode: 'replace' }, error);
            }
            console.error('Error fetching athletes:', error);
        });
    athleteResultsLoadPromise = currentLoadPromise;
    return currentLoadPromise;
};

function buildPodiumSkeletonHtml() {
    return `
        <div class="podium-item podium-skeleton-item second" aria-hidden="true">
            <div class="podium-skeleton-portrait skeleton-shimmer"></div>
            <div class="name-row">
                <span class="athlete-name skeleton-shimmer"></span>
            </div>
            <div class="podium-skeleton-line skeleton-shimmer"></div>
        </div>
        <div class="podium-item podium-skeleton-item first" aria-hidden="true">
            <div class="podium-skeleton-portrait skeleton-shimmer"></div>
            <div class="name-row">
                <span class="athlete-name skeleton-shimmer"></span>
            </div>
            <div class="podium-skeleton-line skeleton-shimmer"></div>
        </div>
        <div class="podium-item podium-skeleton-item third" aria-hidden="true">
            <div class="podium-skeleton-portrait skeleton-shimmer"></div>
            <div class="name-row">
                <span class="athlete-name skeleton-shimmer"></span>
            </div>
            <div class="podium-skeleton-line skeleton-shimmer"></div>
        </div>
    `;
}

function buildLeaderboardSkeletonRows(count) {
    return Array.from({ length: count }, () => `
        <tr class="leaderboard-skeleton-row" aria-hidden="true">
            <td data-label="Rank" class="rank-td"><span class="rank skeleton-shimmer"></span></td>
            <td data-label="Athlete" class="athlete-td">
                <span class="portrait-wrapper"><span class="leaderboard-portrait-skeleton skeleton-shimmer"></span></span>
                <span class="athlete-name skeleton-shimmer"></span>
            </td>
            <td data-label="Sponsor" class="sponsor-td"><span class="leaderboard-skeleton-block skeleton-shimmer"></span></td>
            <td data-label="Age reduction" class="age-reduction-td"><span class="age-reduction leaderboard-skeleton-block skeleton-shimmer"></span></td>
            <td data-label="Media contact" class="media-contact-td"><span class="leaderboard-skeleton-block skeleton-shimmer"></span></td>
        </tr>
    `).join('');
}

function hasServerRenderedLeaderboardRows(tableBody) {
    return !!tableBody &&
        tableBody.getAttribute('data-server-rendered') === 'true';
}

function setLeaderboardStatus(message, isError = false) {
    const status = document.getElementById('leaderboardStatus');
    if (!status) return;
    status.setAttribute('role', isError ? 'alert' : 'status');
    status.textContent = message;
}

function renderLeaderboardLoadError(tableBody) {
    tableBody.querySelector('.leaderboard-load-error-row')?.remove();

    const row = document.createElement('tr');
    const cell = document.createElement('td');
    const recovery = document.createElement('div');
    const message = document.createElement('p');
    const retryButton = document.createElement('button');

    row.className = 'no-results leaderboard-load-error-row';
    cell.colSpan = 5;
    recovery.className = 'leaderboard-recovery';
    recovery.setAttribute('role', 'alert');
    message.textContent = 'Leaderboard could not load.';
    retryButton.type = 'button';
    retryButton.className = 'leaderboard-retry-button';
    retryButton.textContent = 'Retry';
    retryButton.addEventListener('click', () => {
        LoadLeaderboard(includePodiumGlobal, maxAthletesGlobal);
    });

    recovery.append(message, retryButton);
    cell.appendChild(recovery);
    row.appendChild(cell);
    if (hasServerRenderedLeaderboardRows(tableBody)) {
        tableBody.appendChild(row);
    } else {
        tableBody.replaceChildren(row);
    }
}

function setLeaderboardLoadingState(includePodium, maxAthletes) {
    setLeaderboardStatus('Loading leaderboard.');
    const podium = document.querySelector('.podium');
    if (podium) {
        if (includePodium) {
            podium.style.display = 'flex';
            if (podium.dataset.serverRendered !== 'true') podium.innerHTML = buildPodiumSkeletonHtml();
            podium.setAttribute('aria-busy', 'true');
        } else {
            podium.style.display = 'none';
            podium.innerHTML = '';
            podium.setAttribute('aria-busy', 'false');
        }
    }

    const tableBody = document.querySelector('.leaderboard table tbody');
    if (!tableBody) return;
    tableBody.querySelector('.leaderboard-load-error-row')?.remove();
    if (hasServerRenderedLeaderboardRows(tableBody)) {
        tableBody.classList.remove('loading-skeleton');
        tableBody.setAttribute('aria-busy', 'true');
        tableBody.setAttribute('data-hydrating', 'true');
        return;
    }

    const skeletonCount = Number.isFinite(maxAthletes)
        ? Math.min(Math.max(maxAthletes, 4), 10)
        : 10;

    tableBody.classList.add('loading-skeleton');
    tableBody.setAttribute('aria-busy', 'true');
    tableBody.innerHTML = buildLeaderboardSkeletonRows(skeletonCount);
}

function updatePodiumPrizes(podium) {
    // Capture these exact panels so a late response cannot replace newer
    // cards, discard focus, or undo a leaderboard filter/refresh.
    const panels = [1, 2, 3].map(rank => ({
        panel: podium.querySelector(`.podium-item.${['', 'first', 'second', 'third'][rank]} .podium-item-lower`),
        share: [0, 0.6, 0.25, 0.15][rank]
    }));
    Promise.all([window.getSharedPrizeFund(), window.fetchPublicJson('/api/bitcoin/btcusd')])
        .then(([data, tickerData]) => {
            const prizeFundBTC = data.totalReceivedSatoshis / 1e8 * 0.9;
            const rate = tickerData.btcToUsdRate;
            if (!Number.isFinite(prizeFundBTC) || prizeFundBTC < 0 || !Number.isFinite(rate) || rate <= 0) {
                throw new Error('Prize data is unavailable');
            }
            panels.forEach(({ panel, share }) => {
                if (!panel?.isConnected) return;
                const btc = prizeFundBTC * share;
                panel.querySelector('.btc-amount').textContent = `(${btc.toFixed(8)} BTC)`;
                panel.querySelector('.prize-money').textContent = `$${(btc * rate).toFixed(2)}`;
            });
        })
        .catch(error => console.error('Error fetching podium prize amounts:', error))
        .finally(() => panels.forEach(({ panel }) => {
            if (panel?.isConnected) panel.setAttribute('aria-busy', 'false');
        }));
}

function animatePodiumContent() {
    const items = document.querySelectorAll('.podium .podium-item');
    if (!items.length) return;

    items.forEach(item => {
        Array.from(item.children).forEach(child => {
            child.classList.add('podium-content-part');
        });
    });

    requestAnimationFrame(() => {
        items.forEach(item => {
            Array.from(item.children).forEach(child => {
                child.style.transitionDelay = '0ms';
                child.classList.add('is-visible');
            });
        });
    });
}

// Usage
addClickListenerToImages('#modalProfilePic', function () {
    openEnlargedView(this);
}, img => `Enlarge ${img.alt || 'athlete profile picture'}`);
addClickListenerToImages('.portrait, .podium-portrait', handleAthleteNameClick);

// Function to attach click event listener to all athlete-name elements
function isPlainLinkActivation(event) {
    return !event.defaultPrevented && event.button === 0 &&
        !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey;
}

function getAthleteProfileHref(athlete) {
    return `/athlete/${encodeURIComponent(normalizeAthleteSlugForLookup(athlete.athleteSlug || athlete.name))}`;
}

function activateAthleteName(event) {
    if (!isPlainLinkActivation(event)) return;
    event.preventDefault();
    event.stopPropagation();
    handleAthleteNameClick(event);
}

function activateAthleteContainer(event) {
    if (!isPlainLinkActivation(event)) return;
    const target = event.target;
    if (!(target instanceof Element)) return;

    // Preserve the generous row/card hit area from the original leaderboard while
    // allowing every nested control to keep its own behavior.
    if (target.closest('a, button, input, select, textarea, [role="button"]')) return;
    if (target.closest('.badge-class, .portrait, .podium-portrait')) return;

    handleAthleteNameClick(event);
}

function attachAthleteContainerClickListener(container) {
    if (!container || container.dataset.athleteContainerClickListener === 'true') return;
    container.addEventListener('click', activateAthleteContainer);
    container.dataset.athleteContainerClickListener = 'true';
}

function attachAthleteNameClickListeners() {
    document.querySelectorAll('a.athlete-name, a.athlete-profile-link').forEach(athleteNameElement => {
        const athleteName = athleteNameElement.textContent.trim() || athleteNameElement.getAttribute('aria-label');
        if (!athleteName) return;

        athleteNameElement.setAttribute('aria-label', athleteNameElement.title || athleteNameElement.getAttribute('aria-label') || `View stats of ${athleteName}`);
        athleteNameElement.removeEventListener('click', activateAthleteName);
        athleteNameElement.addEventListener('click', activateAthleteName);
    });
}

const proofViewerZoomLevels = [1, 1.5, 2, 3];

function centerProofViewer(enlargedElem) {
    const stage = enlargedElem.querySelector('.image-viewer-stage');
    if (!stage || !enlargedElem.isProofGallery || enlargedElem.dataset.imageState !== 'ready') return;

    stage.scrollLeft = Math.max(0, (stage.scrollWidth - stage.clientWidth) / 2);
    stage.scrollTop = 0;
}

function setProofViewerZoom(enlargedElem, requestedIndex, shouldCenter = true) {
    const boundedIndex = Math.min(Math.max(requestedIndex, 0), proofViewerZoomLevels.length - 1);
    const zoom = proofViewerZoomLevels[boundedIndex];
    const zoomOut = enlargedElem.querySelector('.image-zoom-out');
    const zoomIn = enlargedElem.querySelector('.image-zoom-in');
    const fit = enlargedElem.querySelector('.image-zoom-fit');
    const status = enlargedElem.querySelector('.image-zoom-status');
    const hint = enlargedElem.querySelector('.image-viewer-hint');
    const ready = enlargedElem.dataset.imageState === 'ready';

    enlargedElem.proofZoomIndex = boundedIndex;
    enlargedElem.style.setProperty('--proof-viewer-zoom', String(zoom));
    if (zoomOut) zoomOut.disabled = !ready || boundedIndex === 0;
    if (zoomIn) zoomIn.disabled = !ready || boundedIndex === proofViewerZoomLevels.length - 1;
    if (fit) fit.disabled = !ready;
    if (status) status.textContent = `${Math.round(zoom * 100)}%`;
    if (hint) {
        hint.hidden = !enlargedElem.isProofGallery || !ready;
        hint.textContent = zoom > 1
            ? 'Zoomed for readable text. Scroll to inspect the full proof.'
            : 'Use the zoom controls for fine text, then scroll to inspect.';
    }

    if (shouldCenter) {
        requestAnimationFrame(() => centerProofViewer(enlargedElem));
    }
}

function stopEnlargedImageLoad(enlargedElem) {
    clearTimeout(enlargedElem._imageLoadTimer);
    enlargedElem._imageLoadTimer = null;
    const image = enlargedElem.querySelector('.image-viewer-stage img');
    if (image) {
        image.onload = null;
        image.onerror = null;
        // Keep a completed image painted while the reader fades closed.
        if (enlargedElem.dataset.imageState !== 'ready') image.removeAttribute('src');
    }
}

function setEnlargedImageLoadState(enlargedElem, state) {
    const stage = enlargedElem.querySelector('.image-viewer-stage');
    const feedback = enlargedElem.querySelector('.image-load-feedback');
    const retry = enlargedElem.querySelector('.image-load-retry');
    const ready = state === 'ready';
    const failed = state === 'error';
    const subject = enlargedElem.isProofGallery ? 'proof' : 'image';
    // Move focus before hiding the retry control. Subsequent completion must
    // not steal focus from navigation or Close while the request is pending.
    if (!failed && document.activeElement === retry) stage.focus({ preventScroll: true });
    enlargedElem.dataset.imageState = state;
    stage.setAttribute('aria-busy', state === 'loading' ? 'true' : 'false');
    stage.querySelector('img').hidden = !ready;
    feedback.hidden = ready;
    retry.hidden = !failed;
    feedback.querySelector('.image-load-title').textContent = failed
        ? `This ${subject} couldn't load`
        : ready ? '' : `Loading ${subject}…`;
    const message = feedback.querySelector('.image-load-message');
    message.hidden = !failed;
    message.textContent = failed
        ? (enlargedElem.isProofGallery && enlargedElem.galleryImages.length > 1
            ? 'Try again, or move to another proof.'
            : `Try loading this ${subject} again.`)
        : '';
    setProofViewerZoom(enlargedElem, enlargedElem.proofZoomIndex ?? 0, false);
}

function loadEnlargedImage(enlargedElem, sourceImage) {
    stopEnlargedImageLoad(enlargedElem);
    // A separate element per attempt keeps a late response from a previous
    // page from changing the currently selected proof or its loading state.
    const image = document.createElement('img');
    image.alt = sourceImage.alt;
    image.loading = 'eager';
    enlargedElem.querySelector('.image-viewer-stage img').replaceWith(image);
    setEnlargedImageLoadState(enlargedElem, 'loading');
    const finish = state => {
        if (enlargedElem.querySelector('.image-viewer-stage img') !== image
            || enlargedElem.dataset.imageState !== 'loading') return;
        clearTimeout(enlargedElem._imageLoadTimer);
        enlargedElem._imageLoadTimer = null;
        image.onload = null;
        image.onerror = null;
        setEnlargedImageLoadState(enlargedElem, state);
        if (state === 'ready') {
            requestAnimationFrame(() => {
                if (enlargedElem.querySelector('.image-viewer-stage img') === image) centerProofViewer(enlargedElem);
            });
        }
    };
    image.onload = () => finish(image.naturalWidth > 0 ? 'ready' : 'error');
    image.onerror = () => finish('error');
    enlargedElem._imageLoadTimer = setTimeout(() => {
        finish('error');
        image.removeAttribute('src');
    }, 15000);
    // Preserve the original versioned asset URL on every attempt.
    image.src = sourceImage.dataset.fullSrc || sourceImage.src;
    if (image.complete && image.naturalWidth > 0) finish('ready');
}

function updateEnlargedImage(enlargedElem, index) {
    const galleryImages = enlargedElem.galleryImages;
    if (!Array.isArray(galleryImages) || galleryImages.length === 0) return;

    const imageCount = galleryImages.length;
    const boundedIndex = Math.min(Math.max(index, 0), imageCount - 1);
    const sourceImage = galleryImages[boundedIndex];
    const previousButton = enlargedElem.querySelector('.image-nav--previous');
    const nextButton = enlargedElem.querySelector('.image-nav--next');
    const position = enlargedElem.querySelector('.image-position');
    const stage = enlargedElem.querySelector('.image-viewer-stage');
    const zoomControls = enlargedElem.querySelector('.image-zoom-controls');
    const navigation = enlargedElem.querySelector('.image-navigation');
    const isProofGallery = enlargedElem.isProofGallery;
    const canNavigate = isProofGallery && imageCount > 1;

    enlargedElem.currentImageIndex = boundedIndex;
    enlargedElem.returnFocusTo = sourceImage;
    enlargedElem.classList.toggle('is-proof-gallery', isProofGallery);
    loadEnlargedImage(enlargedElem, sourceImage);

    if (history.state?.modal === 'enlarged') {
        history.replaceState({
            ...history.state,
            enlargedImage: getEnlargedImageHistoryState(sourceImage, enlargedElem)
        }, '', window.location.href);
    }

    if (stage) {
        stage.setAttribute('aria-label', isProofGallery
            ? 'Scrollable proof image. Use zoom controls for fine text.'
            : `Enlarged ${sourceImage.alt || 'image'}`);
    }
    if (zoomControls) zoomControls.hidden = !isProofGallery;
    if (navigation) navigation.hidden = !isProofGallery;
    if (!isProofGallery) {
        enlargedElem.style.removeProperty('--proof-viewer-zoom');
    }

    previousButton.hidden = !canNavigate;
    nextButton.hidden = !canNavigate;
    previousButton.disabled = !canNavigate || boundedIndex === 0;
    nextButton.disabled = !canNavigate || boundedIndex === imageCount - 1;
    position.hidden = !isProofGallery;
    if (!isProofGallery) {
        enlargedElem.setAttribute('aria-label', 'Enlarged image');
        return;
    }

    previousButton.setAttribute(
        'aria-label',
        boundedIndex > 0 ? `Previous proof (${boundedIndex} of ${imageCount})` : 'Previous proof');
    nextButton.setAttribute(
        'aria-label',
        boundedIndex < imageCount - 1 ? `Next proof (${boundedIndex + 2} of ${imageCount})` : 'Next proof');
    position.textContent = `Proof ${boundedIndex + 1} of ${imageCount}`;
    enlargedElem.setAttribute('aria-label', `Proof image ${boundedIndex + 1} of ${imageCount}`);
}

function navigateEnlargedImage(enlargedElem, offset) {
    if (!enlargedElem.isProofGallery || enlargedElem.galleryImages.length < 2) return;
    const targetIndex = enlargedElem.currentImageIndex + offset;
    if (targetIndex < 0 || targetIndex >= enlargedElem.galleryImages.length) return;
    updateEnlargedImage(enlargedElem, targetIndex);
}

function trapEnlargedViewFocus(enlargedElem, event) {
    if (event.key !== 'Tab') return;

    const focusableControls = Array.from(enlargedElem.querySelectorAll('.image-viewer-stage[tabindex="0"], button:not([hidden]):not(:disabled)'))
        .filter(control => control.getClientRects().length > 0 && !control.closest('[hidden]'));
    if (focusableControls.length === 0) return;

    const firstControl = focusableControls[0];
    const lastControl = focusableControls[focusableControls.length - 1];
    if (event.shiftKey && document.activeElement === firstControl) {
        event.preventDefault();
        lastControl.focus();
    } else if (!event.shiftKey && document.activeElement === lastControl) {
        event.preventDefault();
        firstControl.focus();
    }
}

function getEnlargedImageHistoryState(imgElement, viewer) {
    const isProfileImage = imgElement.id === 'modalProfilePic';
    return {
        kind: isProfileImage ? 'profile' : 'proof',
        source: imgElement.dataset.fullSrc || imgElement.getAttribute('src') || '',
        index: Number.isInteger(viewer.currentImageIndex) ? viewer.currentImageIndex : 0
    };
}

function restoreEnlargedViewFromHistory(state) {
    const imageState = state?.enlargedImage;
    const modalContent = modal.querySelector('.modal-content');
    if (!imageState
        || modal.style.display !== 'block'
        || modalContent?.dataset.athleteSlug !== state.athlete
        || modalContent?.classList.contains('is-loading')
        || modalContent?.classList.contains('has-load-error')) {
        return false;
    }

    const candidates = imageState.kind === 'profile'
        ? [document.getElementById('modalProfilePic')].filter(Boolean)
        : Array.from(document.querySelectorAll('#proofsGallery .proof-item img'));
    const image = candidates.find(candidate =>
        (candidate.dataset.fullSrc || candidate.getAttribute('src') || '') === imageState.source)
        || candidates[imageState.index]
        || null;
    if (!image) return false;

    openEnlargedView(image, { pushHistory: false });
    return true;
}

function openEnlargedView(imgElement, options) {
    const viewer = document.getElementById('athleteImageViewer');
    const ownerDialog = viewer ? viewer.closest('#detailsModal') : null;
    const modalContent = ownerDialog ? ownerDialog.querySelector('.modal-content') : null;
    if (!viewer || !ownerDialog || !modalContent || ownerDialog.style.display !== 'block') return;

    if (viewer._closeTimer) {
        clearTimeout(viewer._closeTimer);
        viewer._closeTimer = 0;
    }
    if (viewer._showFrame != null) {
        cancelAnimationFrame(viewer._showFrame);
        viewer._showFrame = null;
    }
    viewer._historyClosePending = false;

    const proofGallery = imgElement.closest('.proofs-gallery');
    viewer.isProofGallery = Boolean(proofGallery);
    viewer.galleryImages = proofGallery
        ? Array.from(proofGallery.querySelectorAll('.proof-item img'))
        : [imgElement];
    viewer.proofZoomIndex = viewer.isProofGallery && window.matchMedia('(max-width: 768px)').matches ? 2 : 0;
    const initialIndex = Math.max(0, viewer.galleryImages.indexOf(imgElement));
    updateEnlargedImage(viewer, initialIndex);

    viewer.hidden = false;
    viewer.setAttribute('aria-hidden', 'false');
    modalContent.inert = true;
    ownerDialog.classList.add('image-viewer-open');

    const closeButton = viewer.querySelector('.close-btn');
    closeButton.focus({ preventScroll: true });
    viewer._showFrame = requestAnimationFrame(() => {
        viewer._showFrame = null;
        if (viewer.hidden || viewer.getAttribute('aria-hidden') !== 'false') return;
        viewer.classList.add('show');
    });

    const shouldPushHistory = !options || options.pushHistory !== false;
    if (shouldPushHistory && history.state?.modal !== 'enlarged') {
        history.pushState({
            ...(history.state || {}),
            modal: 'enlarged',
            enlargedImage: getEnlargedImageHistoryState(imgElement, viewer)
        }, '', window.location.href);
    }

    if (viewer.dataset.interactionsBound === 'true') return;
    viewer.dataset.interactionsBound = 'true';

    closeButton.addEventListener('click', () => {
        requestCloseEnlargedView(viewer);
    });

    viewer.querySelector('.image-nav--previous').addEventListener('click', () => {
        navigateEnlargedImage(viewer, -1);
    });
    viewer.querySelector('.image-nav--next').addEventListener('click', () => {
        navigateEnlargedImage(viewer, 1);
    });
    viewer.querySelector('.image-zoom-out').addEventListener('click', () => {
        setProofViewerZoom(viewer, (viewer.proofZoomIndex ?? 0) - 1);
    });
    viewer.querySelector('.image-zoom-in').addEventListener('click', () => {
        setProofViewerZoom(viewer, (viewer.proofZoomIndex ?? 0) + 1);
    });
    viewer.querySelector('.image-zoom-fit').addEventListener('click', () => {
        setProofViewerZoom(viewer, 0);
    });
    viewer.querySelector('.image-load-retry').addEventListener('click', () => {
        const sourceImage = viewer.galleryImages[viewer.currentImageIndex];
        if (sourceImage && viewer.dataset.imageState === 'error') loadEnlargedImage(viewer, sourceImage);
    });

    viewer.addEventListener('keydown', event => {
        if (event.key === 'Escape' || event.key === 'Esc') {
            event.preventDefault();
            event.stopPropagation();
            requestCloseEnlargedView(viewer);
            return;
        }

        trapEnlargedViewFocus(viewer, event);
        if (!viewer.isProofGallery || viewer.galleryImages.length < 2) return;
        if (viewer.dataset.imageState === 'ready' && (viewer.proofZoomIndex ?? 0) > 0 && event.target.closest('.image-viewer-stage')) return;

        if (event.key === 'ArrowLeft') {
            event.preventDefault();
            event.stopPropagation();
            navigateEnlargedImage(viewer, -1);
        } else if (event.key === 'ArrowRight') {
            event.preventDefault();
            event.stopPropagation();
            navigateEnlargedImage(viewer, 1);
        } else if (event.key === 'Home') {
            event.preventDefault();
            event.stopPropagation();
            updateEnlargedImage(viewer, 0);
        } else if (event.key === 'End') {
            event.preventDefault();
            event.stopPropagation();
            updateEnlargedImage(viewer, viewer.galleryImages.length - 1);
        }
    });

    let touchStart = null;
    viewer.addEventListener('touchstart', event => {
        if (event.touches.length !== 1) return;
        touchStart = {
            x: event.touches[0].clientX,
            y: event.touches[0].clientY
        };
    }, { passive: true });
    viewer.addEventListener('touchend', event => {
        if (!touchStart) return;
        const completedTouch = event.changedTouches[0];
        const startingTouch = touchStart;
        touchStart = null;
        if (!completedTouch) return;
        if (viewer.dataset.imageState === 'ready' && (viewer.proofZoomIndex ?? 0) > 0) return;
        const horizontalDistance = completedTouch.clientX - startingTouch.x;
        const verticalDistance = completedTouch.clientY - startingTouch.y;
        if (Math.abs(horizontalDistance) < 50 || Math.abs(horizontalDistance) <= Math.abs(verticalDistance)) return;
        navigateEnlargedImage(viewer, horizontalDistance > 0 ? -1 : 1);
    }, { passive: true });
    viewer.addEventListener('touchcancel', () => {
        touchStart = null;
    }, { passive: true });

    viewer.addEventListener('click', event => {
        if (event.target === viewer) {
            requestCloseEnlargedView(viewer);
        }
    });
}

function requestCloseEnlargedView(enlargedElem) {
    if (!enlargedElem || enlargedElem.hidden || enlargedElem.getAttribute('aria-hidden') !== 'false') return;

    if (history.state?.modal === 'enlarged') {
        if (enlargedElem._historyClosePending) return;
        enlargedElem._historyClosePending = true;
        history.back();
        return;
    }

    closeEnlargedView(enlargedElem);
}

function closeEnlargedView(enlargedElem) {
    if (!enlargedElem || enlargedElem.hidden || enlargedElem.getAttribute('aria-hidden') !== 'false') return;

    stopEnlargedImageLoad(enlargedElem);
    enlargedElem._historyClosePending = false;
    if (enlargedElem._showFrame != null) {
        cancelAnimationFrame(enlargedElem._showFrame);
        enlargedElem._showFrame = null;
    }

    const returnFocusTo = enlargedElem.returnFocusTo;
    const ownerDialog = enlargedElem.closest('#detailsModal');
    const modalContent = ownerDialog ? ownerDialog.querySelector('.modal-content') : null;
    if (modalContent) modalContent.inert = false;
    if (ownerDialog?.style.display === 'block' && returnFocusTo?.isConnected && typeof returnFocusTo.focus === 'function') {
        const proofGallery = returnFocusTo.closest('.proofs-gallery');
        const proofItem = returnFocusTo.closest('.proof-item');
        if (proofItem?.hidden) proofGallery?._setExpanded?.(true);
        returnFocusTo.focus({ preventScroll: true });
        if (proofItem) {
            const headerHeight = ownerDialog.querySelector('.modal-sticky-header')?.getBoundingClientRect().height || 0;
            proofItem.style.scrollMarginTop = `${headerHeight}px`;
            proofItem.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
        }
    }
    enlargedElem.classList.remove('show');
    enlargedElem.setAttribute('aria-hidden', 'true');

    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    enlargedElem._closeTimer = setTimeout(() => {
        enlargedElem.hidden = true;
        enlargedElem._closeTimer = 0;
        if (ownerDialog) ownerDialog.classList.remove('image-viewer-open');
    }, prefersReducedMotion ? 0 : 220);
}

function renderMediaContact(contact, isPodium, athleteName) {
    const parsed = window.parseMediaContact(contact);
    if (!parsed) return '';

    const tooltipText = `${parsed.isEmail ? 'Email' : 'Contact'} ${athleteName}`;
    if (!parsed.href) {
        return `<span class="media-contact-handle" title="${escapeHtml(tooltipText)}">${escapeHtml(contact)}</span>`;
    }
    const className = isPodium ? 'personal-link-icon podium-media-link' : 'media-contact';
    const targetAttributes = parsed.isEmail ? '' : ' target="_blank" rel="noopener"';
    return `<a href="${escapeHtml(parsed.href)}"${targetAttributes} class="${className}" aria-label="${escapeHtml(tooltipText)}" title="${escapeHtml(tooltipText)}">
        ${window.getIcon(contact)}
    </a>`;
}

function renderPersonalLink(link, athleteName) {
    const href = window.normalizeWebUrl(link);
    if (!href) return '';
    return `<a href="${escapeHtml(href)}" target="_blank" rel="noopener" class="personal-link-icon podium-personal-link" aria-label="Visit athlete's personal page" title="${escapeHtml(`Visit personal page of ${athleteName}`)}">
        <i class="fa fa-link"></i>
    </a>`;
}

function renderPodcastLink(link, athleteName) {
    const href = window.normalizeWebUrl(link);
    if (!href) return '';
    const label = escapeHtml(`Listen to ${athleteName}'s podcast appearance`);
    return `<a href="${escapeHtml(href)}" target="_blank" rel="noopener" class="personal-link-icon podium-podcast-link" aria-label="${label}" title="${label}">
        <i class="fa fa-microphone"></i>
    </a>`;
}

const modal = document.getElementById("detailsModal");
const closeBtn = document.getElementById("closeAthleteDetailsModal");
const athleteDetails = document.getElementById("athleteDetails");
let ageChart;
let savedBodyScroll = 0;
let savedBodyScrollRestoration = null;
let savedBodyBackground = '';
let currentAthleteModalRequestId = 0;
let athleteShareState = null;
const chartJsSrc = 'https://cdn.jsdelivr.net/npm/chart.js';

// Direct profile links arrive with the existing dialog already populated.
// Make closing/back navigation available while the data request is pending.
const initialServerProfile = modal.querySelector('[data-server-rendered-profile]');
if (initialServerProfile) {
    initialServerProfile.classList.add('is-loading');
    lockBodyScroll();
    history.replaceState({ ...history.state, modal: 'details', athlete: initialServerProfile.dataset.athleteSlug }, '', window.location.href);
}

window.ensureChartJs = window.ensureChartJs || function () {
    if (typeof window.Chart === 'function') {
        return Promise.resolve(true);
    }

    if (window.__chartJsPromise) {
        return window.__chartJsPromise;
    }

    window.__chartJsPromise = new Promise(resolve => {
        const script = document.createElement('script');
        script.src = chartJsSrc;
        script.async = true;
        script.crossOrigin = 'anonymous';
        script.onload = () => resolve(typeof window.Chart === 'function');
        script.onerror = () => {
            console.error('Failed to load Chart.js');
            resolve(false);
        };
        document.head.appendChild(script);
    }).then(loaded => {
        if (!loaded) {
            window.__chartJsPromise = null;
        }
        return loaded;
    });

    return window.__chartJsPromise;
};

function ensureAthleteModalDeferredSections() {
    if (document.getElementById('events-frame') && document.getElementById('athlete-biomarkers') && document.getElementById('athlete-proofs')) {
        return;
    }

    const modalContent = document.querySelector('.modal-content');
    const template = document.getElementById('athleteModalDeferredSectionsTemplate');
    if (!modalContent || !template) return;

    modalContent.appendChild(template.content.cloneNode(true));

    if (typeof window.__sendThemeToEventsFrame === 'function') {
        const frame = document.getElementById('events-frame');
        if (frame && !frame.dataset.themeBound) {
            frame.addEventListener('load', function () {
                window.__sendThemeToEventsFrame(frame);
            });
            frame.dataset.themeBound = '1';
        }
    }
}

function resetAthleteEventsFrame(athleteSlug) {
    const eventsFrame = document.getElementById('events-frame');
    if (!eventsFrame) return null;

    const freshFrame = eventsFrame.cloneNode(false);
    freshFrame.removeAttribute('src');
    freshFrame.dataset.expectedAthlete = athleteSlug || '';
    freshFrame.style.height = '1px';
    freshFrame.style.visibility = 'hidden';
    eventsFrame.replaceWith(freshFrame);

    if (typeof window.__sendThemeToEventsFrame === 'function') {
        freshFrame.addEventListener('load', function () {
            window.__sendThemeToEventsFrame(freshFrame);
        });
    }

    return freshFrame;
}

function scheduleBiomarkerChartResize() {
    if (window.biomarkerChartResizeFrame) return;
    const requestFrame = window.requestAnimationFrame || function(callback) { return window.setTimeout(callback, 0); };
    window.biomarkerChartResizeFrame = requestFrame(function() {
        window.biomarkerChartResizeFrame = 0;
        if (window.biomarkerChartInstance) window.biomarkerChartInstance.resize();
    });
}

function cancelBiomarkerChartResize() {
    if (!window.biomarkerChartResizeFrame) return;
    if (window.cancelAnimationFrame) window.cancelAnimationFrame(window.biomarkerChartResizeFrame);
    else window.clearTimeout(window.biomarkerChartResizeFrame);
    window.biomarkerChartResizeFrame = 0;
}

function resetModalForLoading(athleteSlug) {
    ensureAthleteModalDeferredSections();
    resetAthleteShareState();
    const viewer = document.querySelector('#athleteImageViewer[aria-hidden="false"]');
    if (viewer) closeEnlargedView(viewer);
    modal._historyClosePending = false;

    if (modal._closeTimer) {
        clearTimeout(modal._closeTimer);
        modal._closeTimer = 0;
    }
    modal.classList.remove('fade-out');

    const modalContent = document.querySelector('.modal-content');
    if (!modalContent) return null;

    if (modalContent.dataset.serverRenderedProfile === athleteSlug) {
        modalContent.classList.add('is-loading');
        modal.style.display = 'block';
        document.getElementById('shareAthleteProfile').disabled = true;
        const loadError = document.getElementById('athleteLoadError');
        if (loadError) loadError.hidden = true;
        lockBodyScroll();
        return modalContent;
    }
    delete modalContent.dataset.serverRenderedProfile;

    modalContent.classList.remove('has-load-error');
    modalContent.classList.add('is-loading');
    modalContent.dataset.athleteSlug = athleteSlug;
    delete modalContent.dataset.profileImageId;
    modalContent.scrollTop = 0;

    const loadError = document.getElementById('athleteLoadError');
    if (loadError) loadError.hidden = true;

    const stickyHeader = document.getElementById('modalStickyHeader');
    if (stickyHeader) {
        stickyHeader.classList.remove('visible');
    }

    const athleteNameElement = document.getElementById('athleteName');
    if (athleteNameElement) athleteNameElement.textContent = 'Loading...';
    const athleteRankings = document.getElementById('athleteRankings');
    if (athleteRankings) {
        athleteRankings.replaceChildren();
        athleteRankings.hidden = true;
    }
    const stickyAthleteName = document.getElementById('stickyAthleteName');
    if (stickyAthleteName) stickyAthleteName.textContent = 'Loading...';

    const profilePic = document.getElementById('modalProfilePic');
    if (profilePic) {
        profilePic.removeAttribute('src');
        profilePic.removeAttribute('data-full-src');
        profilePic.alt = 'Loading athlete profile picture';
    }

    const modalBadgeStrip = document.getElementById('modalBadgeStrip');
    if (modalBadgeStrip) modalBadgeStrip.innerHTML = '';
    const athleteBio = document.getElementById('athleteBio');
    if (athleteBio) athleteBio.textContent = '';
    const personalLinkElement = document.getElementById('personalLink');
    if (personalLinkElement) personalLinkElement.style.display = 'none';
    const mediaContactElement = document.getElementById('mediaContact');
    if (mediaContactElement) mediaContactElement.style.display = 'none';
    const proofsGallery = document.getElementById('proofsGallery');
    if (proofsGallery) proofsGallery.innerHTML = '';

    const eventsFrame = document.getElementById('events-frame');
    if (eventsFrame) {
        resetAthleteEventsFrame(athleteSlug);
    }

    if (window.biomarkerChartInstance) {
        window.biomarkerChartInstance.destroy();
        window.biomarkerChartInstance = null;
    }
    cancelBiomarkerChartResize();
    if (typeof window.destroyAgeRadarChart === 'function') {
        window.destroyAgeRadarChart();
    }

    if (modal.style.display !== "block") {
        modal.style.display = "block";
    }
    lockBodyScroll();

    return modalContent;
}

// Improved body scroll lock for iOS
function lockBodyScroll() {
    if (document.body.classList.contains('no-scroll')) return;

    savedBodyScroll = window.scrollY || window.pageYOffset || document.documentElement.scrollTop;
    if ('scrollRestoration' in history) {
        savedBodyScrollRestoration ??= history.scrollRestoration;
        history.scrollRestoration = 'manual';
    }
    document.body.classList.add('no-scroll');

    // Prevent iOS bounce
    const scrollY = savedBodyScroll;
    document.body.style.position = 'fixed';
    document.body.style.top = `-${scrollY}px`;
    document.body.style.width = '100%';
    savedBodyBackground = document.body.style.backgroundColor;
    document.body.style.backgroundColor = getComputedStyle(document.body).backgroundColor || getComputedStyle(document.documentElement).backgroundColor;
}

function restoreAthleteHistoryScroll() {
    if (savedBodyScrollRestoration === null) return;
    history.scrollRestoration = savedBodyScrollRestoration;
    savedBodyScrollRestoration = null;
}

function restoreBodyScroll() {
    if (!document.body.classList.contains('no-scroll')) return;

    document.body.classList.remove('no-scroll');
    document.body.style.position = '';
    document.body.style.top = '';
    document.body.style.width = '';
    document.body.style.backgroundColor = savedBodyBackground;

    // Restore scroll position
    if (savedBodyScroll !== undefined) {
        window.scrollTo(0, savedBodyScroll);
        savedBodyScroll = 0;
    }
}

// Initialize scroll progress indicator and sticky header
// Store the scroll listener function reference on the element to allow proper removal
function initializeScrollProgress(modalContent) {
    const progressBar = document.getElementById('modalScrollProgress');
    const stickyHeader = document.getElementById('modalStickyHeader');
    if (!modalContent) return;

    // Remove previous listener if it exists (using stored reference)
    if (modalContent._scrollProgressListener) {
        modalContent.removeEventListener('scroll', modalContent._scrollProgressListener);
        modalContent._scrollProgressListener = null;
    }

    // Create the update function
    const updateProgress = window.ModalScrollOptimizer?.throttleRAF(function() {
        const scrollTop = modalContent.scrollTop;
        const scrollHeight = modalContent.scrollHeight - modalContent.clientHeight;
        const progress = scrollHeight > 0 ? (scrollTop / scrollHeight) * 100 : 0;

        if (progressBar) {
            progressBar.style.setProperty('--modal-scroll-progress', String(Math.min(Math.max(progress / 100, 0), 1)));
            progressBar.setAttribute('aria-valuenow', Math.round(progress));

            // Show/hide progress bar
            if (scrollHeight > 50) {
                progressBar.classList.add('visible');
            } else {
                progressBar.classList.remove('visible');
            }
        }

        // Show/hide sticky header (show when athlete name starts scrolling out of view)
        // Don't show in guess-mode
        if (stickyHeader && !modalContent.classList.contains('guess-mode')) {
            const athleteName = document.getElementById('athleteName');
            if (athleteName) {
                // Get the athlete name's position relative to modal content
                const nameOffsetTop = athleteName.offsetTop;
                // Show sticky header when scrolled past the name (with small offset for smooth transition)
                if (scrollTop > nameOffsetTop - 30) {
                    stickyHeader.classList.add('visible');
                } else {
                    stickyHeader.classList.remove('visible');
                }
            }
        } else if (stickyHeader && modalContent.classList.contains('guess-mode')) {
            // Always hide sticky header in guess-mode
            stickyHeader.classList.remove('visible');
        }
    }) || function() {
        // Fallback if optimizer not available
        const scrollTop = modalContent.scrollTop;
        const scrollHeight = modalContent.scrollHeight - modalContent.clientHeight;
        const progress = scrollHeight > 0 ? (scrollTop / scrollHeight) * 100 : 0;

        if (progressBar) {
            progressBar.style.setProperty('--modal-scroll-progress', String(Math.min(Math.max(progress / 100, 0), 1)));
            progressBar.setAttribute('aria-valuenow', Math.round(progress));
            if (scrollHeight > 50) {
                progressBar.classList.add('visible');
            } else {
                progressBar.classList.remove('visible');
            }
        }

        // Don't show in guess-mode
        if (stickyHeader && !modalContent.classList.contains('guess-mode')) {
            const athleteName = document.getElementById('athleteName');
            if (athleteName) {
                // Get the athlete name's position relative to modal content
                const nameOffsetTop = athleteName.offsetTop;
                // Show sticky header when scrolled past the name (with small offset for smooth transition)
                if (scrollTop > nameOffsetTop - 30) {
                    stickyHeader.classList.add('visible');
                } else {
                    stickyHeader.classList.remove('visible');
                }
            }
        } else if (stickyHeader && modalContent.classList.contains('guess-mode')) {
            // Always hide sticky header in guess-mode
            stickyHeader.classList.remove('visible');
        }
    };

    // Store the listener reference on the element for future removal
    modalContent._scrollProgressListener = updateProgress;

    // Add new listener (passive for performance)
    modalContent.addEventListener('scroll', updateProgress, { passive: true });

    // Initial update
    updateProgress();
}

// Scroll-to-section navigation
function setupScrollToSectionNavigation(modalContent) {
    // Optional: Add floating navigation menu for long content
    // This is a lightweight implementation - can be enhanced later
    const sections = [
        { id: 'athlete-profile', label: 'Profile' },
        { id: 'athlete-stats', label: 'Stats' },
        { id: 'athlete-biomarkers', label: 'Biomarkers' },
        { id: 'athlete-proofs', label: 'Proofs' }
    ];

    // Add smooth scroll behavior when clicking section links
    sections.forEach(section => {
        const sectionEl = document.getElementById(section.id);
        if (sectionEl) {
            // Ensure smooth scrolling when programmatically scrolled to
            sectionEl.scrollIntoView = (function(original) {
                return function(options) {
                    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
                    const opts = options || { behavior: prefersReducedMotion ? 'auto' : 'smooth', block: 'start' };
                    return original.call(this, opts);
                };
            })(Element.prototype.scrollIntoView);
        }
    });
}

// Keyboard navigation for modal
function setupModalKeyboardNavigation() {
    document.addEventListener('keydown', function(event) {
        if (modal.style.display !== "block") return;

        const modalContent = document.querySelector('.modal-content');
        if (!modalContent) return;
        if (modalContent.classList.contains('guess-mode')) return;

        // Home key - scroll to top
        if (event.key === 'Home' && !event.ctrlKey && !event.metaKey) {
            event.preventDefault();
            const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            modalContent.scrollTo({
                top: 0,
                behavior: prefersReducedMotion ? 'auto' : 'smooth'
            });
            return;
        }

        // End key - scroll to bottom
        if (event.key === 'End' && !event.ctrlKey && !event.metaKey) {
            event.preventDefault();
            const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            modalContent.scrollTo({
                top: modalContent.scrollHeight,
                behavior: prefersReducedMotion ? 'auto' : 'smooth'
            });
            return;
        }

        // Arrow keys for section navigation (when not in input/textarea)
        if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') &&
            !['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName)) {
            // Only handle if focus is within modal
            if (modal.contains(document.activeElement)) {
                // Let default behavior handle focus navigation
                return;
            }
        }
    });
}

// Initialize keyboard navigation
setupModalKeyboardNavigation();

function isGuessMyAgeDismissBlocked(modalContent) {
    return !!modalContent
        && modalContent.classList.contains('guess-mode')
        && !modalContent.classList.contains('gma-result-ready');
}

// Set up sticky header close button
const stickyCloseBtn = document.getElementById('stickyCloseBtn');
if (stickyCloseBtn) {
    stickyCloseBtn.onclick = function() {
        const modalContent = document.querySelector('.modal-content');
        if (modalContent && modalContent.classList.contains('guess-mode')) return;
        requestCloseAthleteModal();
    };
}

closeBtn.onclick = function () {
    // Keep an unresolved round locked; a confirmed result reuses this standard close control.
    const modalContent = document.querySelector('.modal-content');
    if (isGuessMyAgeDismissBlocked(modalContent)) return;
    requestCloseAthleteModal();
};

window.addEventListener('click', function (event) {
    const modalContent = document.querySelector('.modal-content');
    if (event.target === modal && modalContent && !modalContent.classList.contains('guess-mode')) {
        requestCloseAthleteModal();
    }
});

function requestCloseAthleteModal() {
    if (modal.style.display !== 'block' || modal._historyClosePending) return;
    if (isGuessMyAgeDismissBlocked(modal.querySelector('.modal-content'))) return;
    const depth = history.state?.athleteDialogDepth;
    if (history.state?.modal === 'details' && Number.isInteger(depth) && depth > 0 && depth < history.length) {
        // Return past only the profile visits created above the calling page.
        // Direct profile entries have no such page underneath them.
        modal._historyClosePending = true;
        history.go(-depth);
        return;
    }

    if (getInitialAthleteSlug()) {
        const pageState = { ...(history.state || {}) };
        for (const key of ['modal', 'athlete', 'athleteDialogDepth', 'enlargedImage']) delete pageState[key];
        history.replaceState(pageState, '', originalAthletelessURL);
    }
    closeModal({ fromHistory: true });
}

function closeModal(options) {
    if (!options?.fromHistory) return requestCloseAthleteModal();
    const modalContent = document.querySelector('.modal-content');
    if (!modalContent) return;
    modal._historyClosePending = false;

    if (modalContent.classList.contains('gma-result-ready')) {
        modalContent.classList.remove('guess-mode', 'gma-fast', 'gma-result-ready');
    }

    currentAthleteModalRequestId++;
    const returnFocusElement = athleteDialogReturnFocusElement;
    athleteDialogReturnFocusElement = null;

    resetAthleteShareState();

    const athleteSlug = modalContent.dataset.athleteSlug;

    // Don't store scroll position - always start from top when reopening

    // Hide sticky header
    const stickyHeader = document.getElementById('modalStickyHeader');
    if (stickyHeader) {
        stickyHeader.classList.remove('visible');
    }

    // Remove scroll listener to prevent memory leaks
    if (modalContent._scrollProgressListener) {
        modalContent.removeEventListener('scroll', modalContent._scrollProgressListener);
        modalContent._scrollProgressListener = null;
    }
    if (window.biomarkerChartResizeObserver) {
        window.biomarkerChartResizeObserver.disconnect();
        window.biomarkerChartResizeObserver = null;
    }
    cancelBiomarkerChartResize();
    if (typeof window.destroyAgeRadarChart === 'function') {
        window.destroyAgeRadarChart();
    }

    modal.classList.add('fade-out');
    restoreBodyScroll();
    if (modal._closeTimer) {
        clearTimeout(modal._closeTimer);
    }
    modal._closeTimer = setTimeout(() => {
        modal._closeTimer = 0;
        modal.style.display = "none";
        modal.classList.remove('fade-out');
        restoreAthleteHistoryScroll();

        // Reset scroll position for next open
        if (modalContent) {
            modalContent.scrollTop = 0;
        }

        resetPageTitle();
        if (returnFocusElement?.isConnected) {
            returnFocusElement.focus({ preventScroll: true });
        }
    }, 400);
}

function debounceFilterAthletes() {
    toggleClearIcon();
    debouncedFilter();
}

function toggleClearIcon() {
    const searchInput = document.getElementById('athleteSearch');
    const clearIcon = document.querySelector('.clear-icon');

    if (searchInput.value.trim().length > 0) {
        clearIcon.classList.add('visible');
    } else {
        clearIcon.classList.remove('visible');
    }
}

function clearSearch() {
    const searchInput = document.getElementById('athleteSearch');
    const clearIcon = document.querySelector('.clear-icon');
    const podium = document.querySelector('.podium');

    searchInput.value = '';
    clearIcon.classList.remove('visible');
    window.removeAllHighlights();
    performFilter(); // Reapply filters without search terms

    if (podium) {
        podium.style.display = includePodiumGlobal ? 'flex' : 'none';
    }

    const url = new URL(window.location.href);
    url.searchParams.delete('search');
    history.replaceState(history.state || {}, "", url.toString());
}

// Wrap the filter function with debounce
const debouncedFilter = debounce(() => {
    performFilter();
}, 400);

function debounce(func, delay) {
    let debounceTimer;
    return function (...args) {
        const context = this;
        clearTimeout(debounceTimer);
        debounceTimer = setTimeout(() => func.apply(context, args), delay);
    };
}

function hasActiveLeaderboardState() {
    const searchInput = document.getElementById('athleteSearch');
    const hasSearch = !!(searchInput && searchInput.value.trim().length > 0);
    return hasActiveLeaderboardFilterState() || hasSearch;
}

function hasActiveLeaderboardFilterState() {
    const state = getSelectedLeaderboardState();
    return state.divisions.length > 0 || state.flags.length > 0 || state.generations.length > 0
        || state.exclusiveLeagues.length > 0 || state.leagueTracks.length === 1 || state.view !== 'ultimate';
}

function syncAgingClockFilters(currentLeaderboardView) {
    document.querySelectorAll('input[name="agingClockView"]').forEach(input => {
        const isActive = input.value === currentLeaderboardView;
        input.checked = isActive;
        input.closest('label')?.classList.toggle('active', isActive);
    });
}

function updateActiveFilterSections() {
    document.querySelectorAll('.filter-section input[type="checkbox"]').forEach(input => {
        input.closest('label')?.classList.toggle('active', input.checked);
    });

    const sectionFilters = {
        'aging-clock-filter-section': document.querySelectorAll('input[name="agingClockView"]:checked').length > 0,
        'league-track-filter-section': document.querySelectorAll('input[name="leagueTrack"]:checked').length === 1,
        'division-filter-section': document.querySelectorAll('input[name="division"]:checked').length > 0,
        'flag-filter-section': document.querySelectorAll('input[name="flag"]:checked').length > 0,
        'generation-filter-section': document.querySelectorAll('input[name="generation"]:checked').length > 0,
        'exclusive-filter-section': document.querySelectorAll('input[name="exclusiveLeague"]:checked').length > 0
    };

    Object.entries(sectionFilters).forEach(([sectionId, hasActiveFilter]) => {
        const section = document.getElementById(sectionId);
        if (!section) return;

        section.classList.toggle('has-active-filter', hasActiveFilter);
    });
}

function updateFilterCountLabel(label, count, available) {
    const countSpan = label.querySelector('.filter-count');
    if (countSpan) {
        countSpan.textContent = count;
    }

    const input = label.querySelector('input[type="checkbox"]');
    label.classList.toggle('is-zero-count', !available);
    if (input) {
        input.disabled = !available && !input.checked;
    }

    if (label.hasAttribute('data-flag-key')) {
        const listItem = label.closest('li');
        if (listItem) {
            listItem.hidden = !available && !input?.checked;
        }
    }
}

function getViewAllAthletesButton() {
    // This homepage action intentionally lives outside athleteDialogRuntime.
    // Cross-component controls must bypass the runtime-scoped document proxy.
    return pageDocument.getElementById('viewAllAthletesBtn');
}

function updateViewAllAthletesButton(count) {
    updateLeaderboardSelectionSummary(count);
    const button = getViewAllAthletesButton();
    if (!button) return;

    const hasResults = Number.isFinite(count) && count > 0;
    const totalCount = Array.isArray(athleteResults) && athleteResults.length > 0 ? athleteResults.length : count;
    const label = hasActiveLeaderboardState() && hasResults ? 'VIEW THIS LEADERBOARD' : 'VIEW ALL ATHLETES';
    const displayCount = hasResults ? count : totalCount;
    button.dataset.useDefaultLeaderboardUrl = hasResults ? 'false' : 'true';
    button.textContent = Number.isFinite(displayCount) ? `${label} (${displayCount})` : label;
}

function updateLeaderboardSelectionSummary(count) {
    const summary = document.getElementById('leaderboardSelectionSummary');
    if (!summary || !Number.isFinite(count)) return;
    summary.hidden = false;
    const resultLabel = `${count} ${count === 1 ? 'athlete' : 'athletes'}`;
    document.getElementById('leaderboardResultCount').textContent = resultLabel;
    const showResults = document.getElementById('showLeaderboardResults');
    if (showResults) showResults.textContent = `Show ${resultLabel}`;

    const choices = [];
    const state = getSelectedLeaderboardState();
    const viewLabels = { bortz: 'Bortz age', pheno: 'Pheno age', improvement: 'Pheno improvement', 'bortz-improvement': 'Bortz improvement', crowd: 'Crowd age' };
    if (state.view !== 'ultimate') choices.push({ label: viewLabels[state.view], remove: () => { document.getElementById('view-ultimate').checked = true; } });
    for (const name of ['leagueTrack', 'division', 'generation', 'exclusiveLeague', 'flag']) {
        if (name === 'leagueTrack' && state.leagueTracks.length !== 1) continue;
        document.querySelectorAll(`input[name="${name}"]:checked`).forEach(input => {
            const label = name === 'leagueTrack' ? getLeagueTrackLabel(input.value) : input.value;
            choices.push({ label, remove: () => { input.checked = false; } });
        });
    }
    const toggle = document.querySelector('.sidebar-toggle');
    if (toggle) {
        toggle.dataset.filterCount = String(choices.length);
        toggle.setAttribute('aria-description', `${choices.length} active ${choices.length === 1 ? 'filter' : 'filters'}`);
    }
    const search = document.getElementById('athleteSearch');
    if (search?.value.trim()) choices.push({ label: `Search: ${search.value.trim()}`, remove: () => { search.value = ''; toggleClearIcon(); } });

    const chips = document.getElementById('leaderboardSelectionChips');
    chips.replaceChildren();
    choices.forEach((choice, index) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'leaderboard-selection-chip';
        button.setAttribute('aria-label', `Remove ${choice.label} filter`);
        const label = document.createElement('span');
        label.textContent = choice.label;
        const close = document.createElement('span');
        close.textContent = '×';
        close.setAttribute('aria-hidden', 'true');
        button.append(label, close);
        button.addEventListener('click', () => {
            choice.remove();
            performFilter();
            const remaining = chips.querySelectorAll('button');
            (remaining[Math.min(index, remaining.length - 1)] || toggle)?.focus({ preventScroll: true });
        });
        chips.appendChild(button);
    });
    document.getElementById('clearLeaderboardSelection').hidden = choices.length === 0;
}

function updateLeaderboardStateIndicator() {
    const sidebarToggle = document.querySelector('.sidebar-toggle');
    const sidebar = document.querySelector('.sidebar');

    const hasActiveState = hasActiveLeaderboardFilterState();
    if (sidebarToggle) {
        sidebarToggle.classList.toggle('has-active-state', hasActiveState);
    }
    if (sidebar) {
        sidebar.classList.toggle('has-active-state', hasActiveState);
    }
    const clearFiltersButton = document.getElementById('clearSidebarFiltersBtn');
    if (clearFiltersButton) {
        clearFiltersButton.setAttribute('aria-hidden', hasActiveState ? 'false' : 'true');
        clearFiltersButton.tabIndex = hasActiveState ? 0 : -1;
    }
}

function clearSidebarFilters() {
    document.querySelectorAll('input[name="division"], input[name="flag"], input[name="generation"], input[name="exclusiveLeague"], input[name="leagueTrack"]').forEach(input => {
        input.checked = false;
    });

    const ultimateView = document.getElementById('view-ultimate');
    if (ultimateView) {
        ultimateView.checked = true;
    }

    performFilter();
}

function showAllAthletes() {
    const searchInput = document.getElementById('athleteSearch');
    if (searchInput) {
        searchInput.value = '';
    }

    document.querySelector('.clear-icon')?.classList.remove('visible');
    window.removeAllHighlights();
    clearSidebarFilters();
}

function getSelectedLeaderboardState() {
    const viewRadio = document.querySelector('input[name="leaderboardView"]:checked');
    return {
        divisions: Array.from(document.querySelectorAll('input[name="division"]:checked'))
            .map(checkbox => checkbox.value),
        flags: Array.from(document.querySelectorAll('input[name="flag"]:checked'))
            .map(checkbox => getCanonicalFlagName(checkbox.value)),
        generations: Array.from(document.querySelectorAll('input[name="generation"]:checked'))
            .map(checkbox => checkbox.value),
        exclusiveLeagues: Array.from(document.querySelectorAll('input[name="exclusiveLeague"]:checked'))
            .map(checkbox => checkbox.value),
        leagueTracks: Array.from(document.querySelectorAll('input[name="leagueTrack"]:checked'))
            .map(checkbox => checkbox.value),
        view: viewRadio ? viewRadio.value.toLowerCase() : 'ultimate'
    };
}

function getLeagueTrackLabel(track) {
    return track === 'Professional' ? 'Pro' : track;
}

window.getFullLeaderboardUrlWithCurrentState = function () {
    const button = getViewAllAthletesButton();
    if (button?.dataset.useDefaultLeaderboardUrl === 'true') {
        return buildLeaderboardNavigationUrl({ divisions: [], flags: [], generations: [], exclusiveLeagues: [], leagueTracks: [], view: 'ultimate' }, '');
    }

    return buildLeaderboardNavigationUrl(getSelectedLeaderboardState());
};

function buildLeaderboardNavigationUrl(state, searchQuery = document.getElementById('athleteSearch')?.value.trim() || '') {
    const url = new URL(window.location.href);
    url.pathname = '/leaderboard';
    for (const key of ['athlete', 'guessmyage', 'search', 'filters', 'view']) url.searchParams.delete(key);
    if (searchQuery) {
        url.searchParams.set('search', encodeURIComponent(searchQuery));
    }

    const selectedDivisions = state.divisions.map(value => value.toLowerCase());
    const selectedFlags = state.flags.map(getFlagFilterKey);
    const selectedGenerations = state.generations.map(value => value.toLowerCase());
    const selectedExclusiveLeagues = state.exclusiveLeagues.map(value => value.toLowerCase());
    const selectedLeagueTracks = state.leagueTracks.map(value => value.toLowerCase());

    const filters = [
        ...selectedDivisions,
        ...selectedFlags,
        ...selectedGenerations,
        ...selectedExclusiveLeagues,
        ...(selectedLeagueTracks.length === 1 ? selectedLeagueTracks : [])
    ];
    if (filters.length > 0) {
        url.searchParams.set('filters', serializeFiltersParam(filters));
    }

    const currentLeaderboardView = state.view;
    const canonicalLeagueSlug = selectedFlags.length === 0 && filters.length === 1
        ? getCanonicalLeagueSlugForFilter(filters[0])
        : null;
    if (!searchQuery && filters.length === 1 && selectedFlags.length === 1 && currentLeaderboardView === 'ultimate') {
        url.pathname = buildFlagHref(state.flags[0]);
        url.searchParams.delete('filters');
    } else if (!searchQuery && canonicalLeagueSlug && currentLeaderboardView === 'ultimate') {
        url.pathname = `/league/${canonicalLeagueSlug}`;
        url.searchParams.delete('filters');
    } else if (!searchQuery && filters.length === 0 && currentLeaderboardView !== 'ultimate') {
        url.pathname = `/league/${currentLeaderboardView}`;
    } else if (currentLeaderboardView !== 'ultimate') {
        url.searchParams.set('view', currentLeaderboardView);
    }

    return `${url.pathname}${url.search}${url.hash}`;
}

function updateLeaderboardNavigationLinks() {
    const viewAll = getViewAllAthletesButton();
    if (viewAll) viewAll.href = window.getFullLeaderboardUrlWithCurrentState();

    const state = getSelectedLeaderboardState();
    const fields = { division: 'divisions', flag: 'flags', generation: 'generations', exclusiveLeague: 'exclusiveLeagues', leagueTrack: 'leagueTracks' };
    document.querySelectorAll('.filter-section li').forEach(item => {
        const input = item.querySelector('input[type="checkbox"]');
        if (!input) return;
        const field = fields[input.name];
        const candidate = input.name === 'agingClockView'
            ? { ...state, view: input.value }
            : { ...state, [field]: [...new Set([...state[field], input.value])] };
        let link = item.querySelector('.filter-league-link');
        if (!link) {
            link = document.createElement('a');
            link.className = 'filter-league-link';
            link.innerHTML = '<i class="fas fa-arrow-right" aria-hidden="true"></i>';
            item.appendChild(link);
        }
        link.href = buildLeaderboardNavigationUrl(candidate);
        const label = item.querySelector('.filter-label-copy').textContent.replace(/\s*\(\d+\)\s*$/, '').trim();
        link.title = `Open ${label} leaderboard with current filters`;
        link.setAttribute('aria-label', link.title);
    });
}

function getLeaderboardSelectionKey() {
    return JSON.stringify([getSelectedLeaderboardState(), document.getElementById('athleteSearch')?.value || '']);
}

function updateLeaderboardStructuredData(entries) {
    // This partial also powers homepage and profile dialogs. Only a full ranking page
    // has the list as its main subject; never replace a profile's Person with its backdrop.
    if (!pageDocument.querySelector('main[data-leaderboard-page="full"]')) return;
    const script = pageDocument.getElementById('pageStructuredData');
    if (!script) return;
    try {
        const data = JSON.parse(script.textContent);
        const graph = data['@graph'];
        const page = graph.find(node => node['@type'] === 'CollectionPage');
        if (!page) return;
        if (entries === null) {
            data['@graph'] = graph.filter(node => node['@type'] !== 'ItemList');
            delete page.mainEntity;
            delete page.about;
            script.textContent = JSON.stringify(data).replaceAll('<', '\\u003c');
            return;
        }
        const siteUrl = 'https://longevityworldcup.com';
        const state = getSelectedLeaderboardState();
        const selectedUrl = new URL(window.getFullLeaderboardUrlWithCurrentState(), siteUrl);
        const filters = [...state.divisions, ...state.flags, ...state.generations, ...state.exclusiveLeagues,
            ...(state.leagueTracks.length === 1 ? state.leagueTracks : [])].map(value => value.toLowerCase()).sort();
        const encode = value => encodeURIComponent(value).replace(/[!'()*]/g, char => '%' + char.charCodeAt(0).toString(16).toUpperCase());
        const search = document.getElementById('athleteSearch')?.value.trim() || '';
        if (!search && filters.length === 0) {
            selectedUrl.pathname = state.view === 'ultimate' ? '/leaderboard' : '/league/' + state.view;
        }
        const query = [];
        if (selectedUrl.pathname === '/leaderboard') {
            if (filters.length) query.push('filters=' + encode(filters.join(',')));
            if (search) query.push('search=' + encode(search));
            if (state.view !== 'ultimate') query.push('view=' + state.view);
        }
        const url = siteUrl + selectedUrl.pathname + (query.length ? '?' + query.join('&') : '');
        const id = url + '#ranking';
        const list = {
            '@type': 'ItemList', '@id': id, url,
            name: pageDocument.getElementById('leaderboardTitle')?.textContent.trim() || 'Longevity World Cup rankings',
            itemListOrder: 'https://schema.org/ItemListOrderAscending',
            numberOfItems: entries.length,
            itemListElement: entries.map(({ athlete, rank }) => {
                const athleteUrl = siteUrl + '/athlete/' + encodeURIComponent(athlete.athleteSlug.replaceAll('_', '-').toLowerCase());
                return {
                    '@type': 'ListItem', position: rank,
                    item: { '@type': 'Person', '@id': athleteUrl + '#person', url: athleteUrl, name: athlete.displayName }
                };
            })
        };
        data['@graph'] = graph.filter(node => node['@type'] !== 'ItemList');
        page.mainEntity = { '@id': id };
        page.about = { '@id': id };
        data['@graph'].push(list);
        // textContent never interprets HTML. Escape '<' as well so serialized HTML
        // remains safe if an athlete's public display name contains a script end tag.
        script.textContent = JSON.stringify(data).replaceAll('<', '\\u003c');
    } catch (error) {
        console.warn('Could not update leaderboard structured data.', error);
    }
}

function performFilter({ updateUrl = true } = {}) {
    const selectedState = getSelectedLeaderboardState();
    const selectedDivisions = selectedState.divisions.map(value => value.toLowerCase());
    const selectedFlags = selectedState.flags.map(getFlagFilterKey);
    const selectedGenerations = selectedState.generations.map(value => value.toLowerCase());
    const selectedExclusiveLeagues = selectedState.exclusiveLeagues.map(value => value.toLowerCase());
    const selectedLeagueTracks = selectedState.leagueTracks;
    const leagueTrackFilterActive = selectedLeagueTracks.length === 1;

    // Get leaderboard view (ultimate | bortz | pheno | improvement | bortz-improvement | crowd)
    const currentLeaderboardView = selectedState.view;
    syncAgingClockFilters(currentLeaderboardView);
    updateActiveFilterSections();
    updateRankingExplanation(currentLeaderboardView);

    const hasBortz = (a) => a.bortzAgeReduction != null && Number.isFinite(a.bortzAgeReduction);
    const hasCrowdAge = (a) => a.crowdCount >= CROWD_AGE_LEADERBOARD_MINIMUM_GUESS_COUNT && Number.isFinite(a.crowdAgeReduction);
    const hasPhenoImprovement = (a) => Number.isFinite(a.phenoAgeImprovement);
    const hasBortzImprovement = (a) => Number.isFinite(a.bortzAgeImprovement);

    const getMetricValueForView = (athlete, view) => {
        if (view === 'crowd') {
            return athlete.crowdAgeReduction;
        }

        if (view === 'improvement') {
            return athlete.phenoAgeImprovement;
        }

        if (view === 'bortz-improvement') {
            return athlete.bortzAgeImprovement;
        }

        if (view === 'pheno') {
            return athlete.ageReduction;
        }

        return (athlete.bortzAgeReduction != null && Number.isFinite(athlete.bortzAgeReduction))
            ? athlete.bortzAgeReduction
            : athlete.ageReduction;
    };

    const getMetricDecimalsForView = (athletes, index, view) => {
        const value = getMetricValueForView(athletes[index], view);
        if (!Number.isFinite(value)) return 1;

        const rounded = value.toFixed(1);
        const previousValue = index > 0 ? getMetricValueForView(athletes[index - 1], view) : null;
        const nextValue = index < athletes.length - 1 ? getMetricValueForView(athletes[index + 1], view) : null;
        const previousRounded = Number.isFinite(previousValue) ? previousValue.toFixed(1) : null;
        const nextRounded = Number.isFinite(nextValue) ? nextValue.toFixed(1) : null;

        return rounded === previousRounded || rounded === nextRounded ? 2 : 1;
    };

    const formatMetricForView = (athletes, athlete, index, view) => {
        const value = getMetricValueForView(athlete, view);
        if (!Number.isFinite(value)) return '';

        const decimals = getMetricDecimalsForView(athletes, index, view);
        const prefix = value > 0 ? '+' : '';
        return `${prefix}${value.toFixed(decimals)} years`;
    };

    const metricHeader = document.getElementById('leaderboardMetricHeader');
    const metricLabel = currentLeaderboardView === 'improvement'
        ? 'Improvement'
        : currentLeaderboardView === 'bortz-improvement'
            ? 'Bortz improvement'
            : 'Age reduction';
    if (metricHeader) {
        metricHeader.textContent = metricLabel;
    }
    document.querySelectorAll('.leaderboard table td.age-reduction-td').forEach(cell => {
        cell.setAttribute('data-label', metricLabel);
    });

    // Cache each view's order for both the table and proposed sidebar selections.
    const athletesByView = new Map([['ultimate', athleteResults]]);
    const getAthletesForView = (view) => {
        if (!athletesByView.has(view)) {
            const definitions = {
                bortz: [hasBortz, window.compareAthleteRank],
                pheno: [() => true, window.compareAthleteRankPhenoOnly],
                improvement: [hasPhenoImprovement, window.compareAthleteRankPhenoImprovement],
                'bortz-improvement': [hasBortzImprovement, window.compareAthleteRankBortzImprovement],
                crowd: [hasCrowdAge, window.compareAthleteRankCrowdAge]
            };
            const [eligible, compare] = definitions[view];
            athletesByView.set(view, athleteResults.filter(eligible).sort(compare));
        }
        return athletesByView.get(view);
    };

    const query = window.normalizeString(document.getElementById('athleteSearch').value.trim().toLowerCase());
    const searchTerms = query.split(/\s+/).filter(term => term.length > 0);
    const uniqueSearchTerms = [...new Set(searchTerms)];

    const showAll = uniqueSearchTerms.length === 0;

    const athleteBadgeTextByName = new Map();
    document.querySelectorAll('.leaderboard table tbody tr[data-athlete-name]').forEach(row => {
        const athleteName = row.getAttribute('data-athlete-name');
        if (!athleteName) return;

        const badgeSearchText = Array.from(row.querySelectorAll('.badge-section .badge-class'))
            .map(badge => {
                const title = badge.getAttribute('title') || '';
                const ariaLabel = badge.getAttribute('aria-label') || '';
                const text = badge.textContent || '';
                return `${title} ${ariaLabel} ${text}`.trim();
            })
            .filter(Boolean)
            .join(' ');

        athleteBadgeTextByName.set(athleteName, window.normalizeString(badgeSearchText.toLowerCase()));
    });

    const searchTextByAthlete = new Map(athleteResults.map(athlete => [athlete, [
        athlete.name,
        athleteBadgeTextByName.get(athlete.name) || '',
        athlete.mediaContact || '',
        `${athlete.flag || ''} ${athlete.canonicalFlag || ''}`,
        athlete.why || ''
    ].map(text => window.normalizeString(text.toLowerCase())).join(' ')]));

    const getMatchingEntries = (state) => {
        const divisions = state.divisions.map(value => value.toLowerCase());
        const flags = state.flags.map(getFlagFilterKey);
        const generations = state.generations.map(value => value.toLowerCase());
        const exclusiveLeagues = state.exclusiveLeagues.map(value => value.toLowerCase());
        const trackActive = state.leagueTracks.length === 1;
        const wantPro = state.leagueTracks[0]?.toLowerCase() === 'professional';
        const filtersUsed = divisions.length > 0 || flags.length > 0 || generations.length > 0 || exclusiveLeagues.length > 0 || trackActive || state.view !== 'ultimate';
        const cohort = getAthletesForView(state.view).filter(athlete =>
            (!trackActive || hasBortz(athlete) === wantPro) &&
            (!divisions.length || divisions.includes(athlete.division.toLowerCase())) &&
            (!flags.length || flags.includes(athlete.flagFilterKey)) &&
            (!generations.length || generations.includes(athlete.generation.toLowerCase())) &&
            (!exclusiveLeagues.length || exclusiveLeagues.includes((athlete.exclusiveLeague || '').toLowerCase()))
        );

        // Fix rank and displayed precision before search or the display limit.
        // Keep these on entries so sidebar previews cannot mutate the table's athletes.
        return cohort.map((athlete, index) => ({
            athlete,
            rank: filtersUsed ? index + 1 : athlete.rank,
            metric: formatMetricForView(cohort, athlete, index, state.view)
        })).filter(entry => {
            if (showAll) return true;
            const { athlete, rank, metric } = entry;
            const value = getMetricValueForView(athlete, state.view);
            const percent = state.view === 'pheno'
                ? athlete.ageReductionPercent
                : (state.view === 'improvement' || state.view === 'bortz-improvement')
                    ? null
                    : (Number.isFinite(athlete.bortzAgeReductionPercent) ? athlete.bortzAgeReductionPercent : athlete.ageReductionPercent);
            // Retain the old one-decimal form for existing shared searches as well.
            const metricParts = [metric];
            if (Number.isFinite(value)) metricParts.push(value.toFixed(1));
            if (state.view !== 'crowd' && Number.isFinite(percent)) metricParts.push(percent.toFixed(1));
            if (state.view === 'crowd' && Number.isFinite(athlete.crowdCount)) metricParts.push(String(athlete.crowdCount));
            const metricText = window.normalizeString(metricParts.join(' ').toLowerCase());
            return uniqueSearchTerms.every(term => searchTextByAthlete.get(athlete).includes(term) || metricText.includes(term) || String(rank).includes(term));
        });
    };

    const matchingEntries = getMatchingEntries(selectedState);
    const entriesByAthlete = new Map(matchingEntries.map(entry => [entry.athlete, entry]));
    const unslicedFilteredAthletes = matchingEntries.map(entry => entry.athlete);
    const filteredAthletes = unslicedFilteredAthletes.slice(0, maxAthletesGlobal);
    updateViewAllAthletesButton(unslicedFilteredAthletes.length);

    // First, hide all rows (including tier separator)
    const tableRows = document.querySelectorAll('.leaderboard table tbody tr');
    tableRows.forEach(row => {
        row.style.display = 'none';
    });
    const tierSeparatorRow = document.querySelector('.leaderboard table tbody tr.tier-separator');

    // Remove 'blurred' class from all podium items
    const podiumItems = document.querySelectorAll('.podium-item');
    podiumItems.forEach(item => {
        item.classList.remove('blurred');
    });

    let anyPodiumVisible = false;

    // Show the rows and podium items for the filtered athletes
    filteredAthletes.forEach(athlete => {
        const athleteName = athlete.name;
        const row = document.querySelector(`.leaderboard table tbody tr[data-athlete-name="${athleteName}"]`);
        if (row) {
            row.style.display = '';
            window.showWithDelay(row);

            // Update the rank cell
            const rankCell = row.querySelector('td[data-label="Rank"] .rank');
            if (rankCell) {
                rankCell.textContent = entriesByAthlete.get(athlete).rank;
            }

            const ageReductionCell = row.querySelector('.age-reduction');
            const displayMetric = entriesByAthlete.get(athlete).metric;
            if (ageReductionCell && displayMetric) {
                ageReductionCell.textContent = displayMetric;
            }

        }
        const item = document.querySelector(`.podium-item[data-athlete-name="${athleteName}"]`);
        if (item) {
            item.style.display = '';
            window.showWithDelay(item);
            anyPodiumVisible = true;
        }
    });

    const hidePodiumRowsInTable = includePodiumGlobal && selectedDivisions.length === 0 && selectedFlags.length === 0 && selectedGenerations.length === 0 && selectedExclusiveLeagues.length === 0 &&
        !leagueTrackFilterActive && currentLeaderboardView === 'ultimate' && uniqueSearchTerms.length === 0;
    const tableVisibleAthletes = hidePodiumRowsInTable
        ? filteredAthletes.filter(athlete => athlete.rank > 3)
        : filteredAthletes;
    if (currentLeaderboardView === 'crowd') {
        markAthletesSkippedForGuessMyAgeIfUnset(tableVisibleAthletes);
    }

    // Show tier separator when both pro and amateur rows are visible in the table (first amateur after at least one pro); hide for clock views.
    // Use tableVisibleAthletes order (not DOM order) so homepage podium rows do not keep the separator alive after their table rows are hidden.
    if (tierSeparatorRow) {
        let separatorShouldShow = false;
        if (currentLeaderboardView === 'ultimate') {
            const isPro = (a) => a.bortzAgeReduction != null && Number.isFinite(a.bortzAgeReduction);
            let sawPro = false;
            for (const a of tableVisibleAthletes) {
                if (isPro(a)) sawPro = true;
                else if (sawPro) {
                    separatorShouldShow = true;
                    break;
                }
            }
        }
        tierSeparatorRow.style.display = separatorShouldShow ? '' : 'none';
    }

    // Reorder tbody so visible rows match the athletes actually shown in the table.
    const tableBody = document.querySelector('.leaderboard table tbody');
    const allRows = Array.from(tableBody.querySelectorAll('tr'));
    const visibleOrdered = tableVisibleAthletes.map(a => allRows.find(r => r.getAttribute('data-athlete-name') === a.name)).filter(Boolean);
    const separator = allRows.find(r => r.classList.contains('tier-separator'));
    const hiddenRows = allRows.filter(r => r !== separator && !visibleOrdered.includes(r));
    const fragment = document.createDocumentFragment();
    const isProForOrder = (a) => a.bortzAgeReduction != null && Number.isFinite(a.bortzAgeReduction);
    const firstAmateurIndex = tableVisibleAthletes.findIndex(a => !isProForOrder(a));
    const separatorVisible = tierSeparatorRow && tierSeparatorRow.style.display !== 'none';
    if (separator && separatorVisible && firstAmateurIndex > 0 && firstAmateurIndex < visibleOrdered.length) {
        visibleOrdered.slice(0, firstAmateurIndex).forEach(r => fragment.appendChild(r));
        fragment.appendChild(separator);
        visibleOrdered.slice(firstAmateurIndex).forEach(r => fragment.appendChild(r));
    } else {
        visibleOrdered.forEach(r => fragment.appendChild(r));
        if (separator) fragment.appendChild(separator);
    }
    hiddenRows.forEach(r => fragment.appendChild(r));
    tableBody.appendChild(fragment);

    // Remove existing highlights
    window.removeAllHighlights();

    // Highlight search terms
    document.querySelectorAll('.athlete-name').forEach(athleteNameElement => {
        const athleteNameText = athleteNameElement.textContent;
        const athleteOwner = athleteNameElement.closest('[data-athlete-name]');
        const athleteKey = athleteOwner ? athleteOwner.getAttribute('data-athlete-name') : null;

        // Check if the athlete is in filteredAthletes
        const athleteInFiltered = filteredAthletes.some(athlete => athlete.name === (athleteKey || athleteNameText));
        const athleteInFilteredByDisplayedText = filteredAthletes.some(athlete => athlete.name === athleteNameText);
        if (athleteInFiltered) {
            window.highlightText(athleteNameElement, uniqueSearchTerms);
        }
    });

    // Update the counts in the filter sections
    updateFilterCounts(selectedState, getMatchingEntries, matchingEntries);

    // Add or remove 'blurred' class from podium items based on filtering
    podiumItems.forEach(item => {
        const athleteName = item.getAttribute('data-athlete-name');
        const athleteInFiltered = filteredAthletes.some(athlete => athlete.name === athleteName);
        if (athleteInFiltered) {
            // Remove 'blurred' class
            item.classList.remove('blurred');
        } else {
            // Add 'blurred' class
            item.classList.add('blurred');
        }
    });

    updateLeaderboardTitles(selectedState);

    // Display "No Results Found" if Necessary
    displayNoResults(tableRows, podiumItems, query);
    updateLeaderboardStructuredData(matchingEntries.slice(0, maxAthletesGlobal));

    // Hide podium athletes' rows if no filters/search are applied and includePodiumGlobal is true
    if (hidePodiumRowsInTable) {
        document.querySelectorAll('.leaderboard table tbody tr.podium-athlete').forEach(row => {
            row.style.display = 'none';
        });
    }

    if (typeof AOS !== 'undefined') {
        AOS.refresh();
    }

    updateLeaderboardStateIndicator();
    scheduleLeaderboardTableHeightSync();
    renderedLeaderboardSelection = getLeaderboardSelectionKey();

    // Initial hydration must preserve direct rank/profile fragments and unrelated URL state.
    if (!updateUrl) {
        normalizeLegacyLeagueRoute();
        updateLeaderboardNavigationLinks();
        return;
    }

    const url = new URL(window.location.href);
    url.hash = ""; // <- this line removes #ABCD or any other fragment

    if (query === "") {
        url.searchParams.delete('search');
    } else {
        url.searchParams.set('search', encodeURIComponent(query));
    }

    const selectedFlagKeys = Array.from(document.querySelectorAll('input[name="flag"]:checked'))
        .map(checkbox => getFlagFilterKey(checkbox.value));
    const filters = [
        ...selectedDivisions,
        ...selectedFlagKeys,
        ...selectedGenerations,
        ...selectedExclusiveLeagues,
        ...(leagueTrackFilterActive ? selectedLeagueTracks.map(value => value.toLowerCase()) : [])
    ];
    const canonicalLeagueSlug = filters.length === 1 && selectedFlagKeys.length === 0
        ? getCanonicalLeagueSlugForFilter(filters[0])
        : null;
    const cleanSearch = ([...url.searchParams].length === 0)
        || ([...url.searchParams].length === 1 && (url.searchParams.has('filters') || url.searchParams.has('view')));
    if (filters.length === 1 && selectedFlagKeys.length === 1 && cleanSearch && currentLeaderboardView === 'ultimate') {
        url.searchParams.delete('filters');
        url.pathname = `/flag/${getFlagRouteSlug(selectedState.flags[0])}`;
    } else if (canonicalLeagueSlug && cleanSearch && currentLeaderboardView === 'ultimate') {
        url.searchParams.delete('filters');
        url.pathname = `/league/${canonicalLeagueSlug}`;
    } else if (filters.length === 0 && cleanSearch && (currentLeaderboardView === 'bortz' || currentLeaderboardView === 'pheno' || currentLeaderboardView === 'improvement' || currentLeaderboardView === 'bortz-improvement' || currentLeaderboardView === 'crowd')) {
        url.pathname = `/league/${currentLeaderboardView}`;
        url.searchParams.delete('view');
    } else {
        url.pathname = originalLeaguelessURL.pathname;
        if (filters.length === 0) {
            url.searchParams.delete('filters');
        } else {
            url.searchParams.set('filters', serializeFiltersParam(filters));
        }
    }

    if (currentLeaderboardView === 'ultimate') {
        url.searchParams.delete('view');
    } else if (url.pathname !== `/league/${currentLeaderboardView}`) {
        url.searchParams.set('view', currentLeaderboardView);
    }

    history.replaceState(history.state || {}, "", url.toString());
    updateLeaderboardNavigationLinks();
}

function updateRankingExplanation(currentLeaderboardView) {
    const explanation = document.getElementById('rankingExplanation');
    if (!explanation) return;

    const explanations = {
        ultimate: '<strong><a href="/ruleset#point-system-ranking">Ultimate League</a></strong> ranks Pro athletes before Amateur athletes, then by effective age reduction and tie-breakers within each track.',
        bortz: '<strong><a href="/bortz-age">Bortz age</a></strong> estimates biological age from advanced blood-test markers.',
        pheno: '<strong><a href="/pheno-age">Pheno age</a></strong> estimates biological age from common blood-test markers.',
        improvement: '<strong>Pheno improvement</strong> ranks each athlete’s latest <a href="/pheno-age">pheno age</a> against their worst pheno age.',
        'bortz-improvement': '<strong>Bortz improvement</strong> ranks each athlete’s latest <a href="/bortz-age">bortz age</a> against their worst bortz age.',
        crowd: '<strong>Crowd age</strong> is a visual age estimate from visitors. Athletes qualify once they reach 100 accepted guesses.'
    };

    const html = explanations[currentLeaderboardView] || '';
    explanation.innerHTML = html;
    explanation.classList.toggle('is-visible', html.length > 0);
}

const leaderboardViewPresentations = Object.freeze({
    ultimate: { railText: 'Ultimate League', documentTitle: 'Ultimate League' },
    bortz: { railText: 'Bortz Age League', documentTitle: 'Bortz Age Leaderboard' },
    pheno: { railText: 'Pheno Age League', documentTitle: 'Pheno Age Leaderboard' },
    improvement: { railText: 'Pheno Improvement League', documentTitle: 'Pheno Improvement Leaderboard' },
    'bortz-improvement': { railText: 'Bortz Improvement League', documentTitle: 'Bortz Improvement Leaderboard' },
    crowd: { railText: 'Crowd Age League', documentTitle: 'Crowd Age Leaderboard' }
});

const generationLeagueAliases = Object.freeze({
    'baby boomers|silent generation': 'Heritage',
    'baby boomers|gen x': 'Senior',
    'gen x|millennials': 'Prime',
    'gen z|millennials': 'Rising',
    'gen alpha|gen z': 'Next-gen'
});

function getSelectionKey(values) {
    return values.map(value => value.toLowerCase()).sort().join('|');
}

function getGenerationLeagueLabel(generations) {
    if (generations.length === 0) return '';
    if (generations.length === 1) return generations[0];
    return generationLeagueAliases[getSelectionKey(generations)] || 'Multi-generation';
}

function getDivisionLeagueLabel(divisions) {
    if (divisions.length === 0) return '';
    if (divisions.length === 1) return divisions[0];
    if (divisions.length >= 3) return 'All Divisions';

    const aliases = {
        "men's|women's": 'Mixed',
        "men's|open": "Inclusive Men's",
        "open|women's": "Inclusive Women's"
    };
    return aliases[getSelectionKey(divisions)] || 'Multi-division';
}

function buildLeaderboardPresentation(state) {
    const viewPresentation = leaderboardViewPresentations[state.view] || leaderboardViewPresentations.ultimate;
    if (state.view !== 'ultimate') {
        return { ...viewPresentation, accessibleLabel: viewPresentation.documentTitle };
    }

    const flags = state.flags.filter(Boolean);
    if (flags.length === 1) {
        const documentTitle = `Leaderboard: ${flags[0]}`;
        return { railText: flags[0], documentTitle, accessibleLabel: documentTitle };
    }
    if (flags.length > 1) {
        const documentTitle = `${flags.length} Flags Leaderboard`;
        return { railText: `${flags.length} Flags`, documentTitle, accessibleLabel: documentTitle };
    }

    if (state.exclusiveLeagues.length > 0) {
        const leagueLabel = state.exclusiveLeagues.length === 1
            ? `${state.exclusiveLeagues[0]} League`
            : 'Exclusive Leagues';
        return { railText: leagueLabel, documentTitle: leagueLabel, accessibleLabel: leagueLabel };
    }

    const generationLabel = getGenerationLeagueLabel(state.generations);
    const divisionLabel = getDivisionLeagueLabel(state.divisions);
    if (generationLabel || divisionLabel) {
        const leagueLabel = `${[generationLabel, divisionLabel].filter(Boolean).join(' ')} League`;
        return { railText: leagueLabel, documentTitle: leagueLabel, accessibleLabel: leagueLabel };
    }

    if (state.leagueTracks.length === 1) {
        const trackLabel = getLeagueTrackLabel(state.leagueTracks[0]);
        const leagueLabel = `${trackLabel} League`;
        return { railText: leagueLabel, documentTitle: leagueLabel, accessibleLabel: leagueLabel };
    }

    return { ...viewPresentation, accessibleLabel: viewPresentation.documentTitle };
}

function syncCollapsedTitleHeight() {
    const collapsedTitle = document.querySelector('.collapsed-title');
    if (!collapsedTitle || collapsedTitle.getClientRects().length === 0 || collapsedTitle.clientHeight <= 0) return;

    // Short result sets still need room for the complete league name.
    // Measure the natural title, independently of the current row count.
    const heading = document.querySelector('.sidebar-heading');
    const leaderboard = document.querySelector('.leaderboard');
    if (!heading || !leaderboard) return;
    const minimumHeight = Math.ceil(heading.getBoundingClientRect().height + collapsedTitle.scrollHeight);
    leaderboard.style.setProperty('--leaderboard-title-height', `${minimumHeight}px`);
}

function updateLeaderboardTitles(state) {
    const presentation = buildLeaderboardPresentation(state);
    const collapsedTitle = document.querySelector('.collapsed-title');
    if (collapsedTitle) {
        const fullRailText = presentation.railText.toUpperCase();
        collapsedTitle.dataset.fullRailText = fullRailText;
        collapsedTitle.textContent = fullRailText;
        collapsedTitle.title = presentation.accessibleLabel;
        collapsedTitle.setAttribute('aria-label', presentation.accessibleLabel);
        collapsedTitle.style.opacity = '0';
        requestAnimationFrame(() => {
            syncCollapsedTitleHeight();
            collapsedTitle.style.opacity = '1';
        });
    }

    if (pageDocument.querySelector('[data-leaderboard-page="full"]')) {
        currentLeaderboardDocumentTitle = `${presentation.documentTitle} | Longevity World Cup`;
        const athleteModal = document.getElementById('detailsModal');
        if (!athleteModal || athleteModal.style.display !== 'block') {
            pageDocument.title = currentLeaderboardDocumentTitle;
        }
    }
}

function updateFilterCounts(state, getMatchingEntries, currentEntries) {
    const currentEntriesByAthlete = new Map(currentEntries.map(entry => [entry.athlete, entry]));
    const fields = {
        division: ['divisions', athlete => athlete.division],
        flag: ['flags', athlete => athlete.flagFilterKey],
        generation: ['generations', athlete => athlete.generation],
        exclusiveLeague: ['exclusiveLeagues', athlete => athlete.exclusiveLeague || ''],
        leagueTrack: ['leagueTracks', athlete => Number.isFinite(athlete.bortzAgeReduction) ? 'Professional' : 'Amateur']
    };
    document.querySelectorAll('.filter-section label').forEach(label => {
        const input = label.querySelector('input[type="checkbox"]');
        if (!input) return;
        const isView = input.name === 'agingClockView';
        const field = fields[input.name];
        if (!isView && !field) return;

        // Counts describe this option's members after adding it to its OR group,
        // or switching clocks, with all other selections and the search preserved.
        const candidateState = isView
            ? { ...state, view: input.value }
            : { ...state, [field[0]]: [...state[field[0]], input.value] };
        const entries = input.checked ? currentEntries : getMatchingEntries(candidateState);
        const value = input.name === 'flag' ? getFlagFilterKey(input.value) : input.value.toLowerCase();
        const count = isView ? entries.length : entries.filter(entry => field[1](entry.athlete).toLowerCase() === value).length;
        // Combining leagues can change a matching athlete's rank even when the
        // added option has no matching members of its own. Do not enable empty
        // options that leave both the matching athletes and their scores unchanged.
        const changesResults = entries.some(entry => {
            const current = currentEntriesByAthlete.get(entry.athlete);
            return !current || current.rank !== entry.rank || current.metric !== entry.metric;
        });
        updateFilterCountLabel(label, count, count > 0 || changesResults);
    });
}

function displayNoResults(tableRows, podiumItems, query) {
    // Remove existing no-results message if any
    const existingMessage = document.querySelector('.no-results');
    if (existingMessage) {
        existingMessage.remove();
    }

    // Check if any rows or podium items are visible
    const anyVisible = Array.from(tableRows).some(row => row.style.display !== 'none');

    if (!anyVisible) {
        const tableBody = document.querySelector('.leaderboard table tbody');
        const noResults = document.createElement('tr');
        noResults.classList.add('no-results');
        noResults.innerHTML = `
            <td colspan="6">
                <div class="no-results-empty-state">
                    <p>No athletes match this leaderboard.</p>
                    <button type="button" class="show-all-athletes-btn">Show all athletes</button>
                </div>
            </td>`;
        tableBody.appendChild(noResults);
    }
}

let isInitialClick = true; // Track if it’s the first click after focusing

const searchInput = document.getElementById('athleteSearch');

// Select all text on the first focus
searchInput?.addEventListener('focus', () => {
    isInitialClick = true;
});

// Select all text on the first click, then just place the cursor where clicked on the second click
searchInput?.addEventListener('click', (event) => {
    if (isInitialClick) {
        searchInput.select(); // Select all text
        isInitialClick = false;
    }
});

document.addEventListener('keydown', function (event) {
    const searchInput = document.getElementById('athleteSearch');
    if (document.activeElement === searchInput && event.key === 'Escape' && searchInput.value.trim().length > 0) {
        event.preventDefault();
        event.stopImmediatePropagation();
        clearSearch();
    }
});

// Modify the input event listener
document.getElementById('athleteSearch')?.addEventListener('input', function () {
    const query = this.value.trim();
    if (query === '') {
        clearSearch(); // Run only when input is cleared
    } else {
        debounceFilterAthletes(); // Run debounce function when input is not empty
    }
});

const clearSearchButton = document.querySelector('.clear-icon');
clearSearchButton?.addEventListener('click', function () {
    clearSearch();
    document.getElementById('athleteSearch')?.focus({ preventScroll: true });
});
clearSearchButton?.addEventListener('keydown', function (event) {
    if (event.key === 'Enter' || event.key === ' ' || event.key === 'Spacebar') {
        event.preventDefault();
        clearSearch();
        const searchInput = document.getElementById('athleteSearch');
        try {
            searchInput?.focus({ preventScroll: true, focusVisible: true });
        }
        catch {
            searchInput?.focus({ preventScroll: true });
        }
    }
});

const clearSidebarFiltersButton = document.getElementById('clearSidebarFiltersBtn');
document.getElementById('clearLeaderboardSelection')?.addEventListener('click', () => {
    showAllAthletes();
    document.getElementById('athleteSearch')?.focus({ preventScroll: true });
});
document.getElementById('showLeaderboardResults')?.addEventListener('click', () => closeSidebar({ restoreFocus: true }));
clearSidebarFiltersButton?.addEventListener('click', function (event) {
    event.preventDefault();
    event.stopPropagation();
    clearSidebarFilters();
    if (!isMobileDrawerViewport()) {
        closeSidebar({ restoreFocus: event.detail === 0 });
    } else {
        focusAfterClearingSidebarFilters();
    }
});
function focusAfterClearingSidebarFilters() {
    const sidebar = document.querySelector('.sidebar');
    const sidebarClose = document.querySelector('.sidebar-close');
    const sidebarToggle = document.querySelector('.sidebar-toggle');
    const isMobileDrawer = isMobileDrawerViewport();
    const focusTarget = isMobileDrawer && sidebar?.classList.contains('expanded')
        ? sidebarClose
        : sidebarToggle;

    if (!focusTarget) return;

    requestAnimationFrame(() => {
        focusTarget.classList.add('is-keyboard-focus');
        focusTarget.addEventListener('blur', () => {
            focusTarget.classList.remove('is-keyboard-focus');
        }, { once: true });
        try {
            focusTarget.focus({ preventScroll: true, focusVisible: true });
        }
        catch {
            focusTarget.focus({ preventScroll: true });
        }
    });
}
document.addEventListener('click', function (event) {
    if (!event.target.closest('.show-all-athletes-btn')) return;

    event.preventDefault();
    showAllAthletes();
});

document.querySelectorAll('input[name="leaderboardView"]').forEach(radio => {
    radio.addEventListener('change', function () {
        performFilter();
    });
    radio.addEventListener('keydown', function (event) {
        if ((event.key !== 'Enter' && event.key !== ' ' && event.key !== 'Spacebar') || this.value === 'ultimate' || !this.checked) return;

        event.preventDefault();
        const defaultViewRadio = document.getElementById('view-ultimate');
        if (!defaultViewRadio) return;

        defaultViewRadio.checked = true;
        document.querySelectorAll('input[name="leaderboardView"].is-keyboard-focus').forEach(input => {
            input.classList.remove('is-keyboard-focus');
        });
        defaultViewRadio.classList.add('is-keyboard-focus');
        defaultViewRadio.addEventListener('blur', () => {
            defaultViewRadio.classList.remove('is-keyboard-focus');
        }, { once: true });
        try {
            defaultViewRadio.focus({ preventScroll: true, focusVisible: true });
        }
        catch {
            defaultViewRadio.focus({ preventScroll: true });
        }
        performFilter();
    });
});
document.querySelectorAll('input[name="agingClockView"]').forEach(input => {
    input.addEventListener('change', function (event) {
        event.stopPropagation();
        const targetView = this.checked ? this.value : 'ultimate';
        const viewRadio = document.getElementById(`view-${targetView}`);
        if (viewRadio) {
            viewRadio.checked = true;
        }
        performFilter();
    });
});
// Clicking the active ranking badge again deselects it (none = Ultimate view)
document.querySelectorAll('.leaderboard-view-switcher .view-badge').forEach(label => {
    label.addEventListener('click', function (e) {
        const radio = document.getElementById(label.getAttribute('for'));
        if (radio && radio.checked) {
            e.preventDefault();
            document.getElementById('view-ultimate').checked = true;
            performFilter();
        }
    });
});

function generateDivisionFilters(athletes) {
    const filterSection = document.querySelector('#division-filter-section ul');
    filterSection.innerHTML = ''; // Clear existing filters if any

    // Count occurrences of each division
    const divisionCounts = athletes.reduce((acc, athlete) => {
        acc[athlete.division] = (acc[athlete.division] || 0) + 1;
        return acc;
    }, {});

    // Generate the filter items with icons and counts
    Object.keys(divisionCounts).forEach(division => {
        const count = divisionCounts[division];
        const icon = window.TryGetDivisionFaIcon ? window.TryGetDivisionFaIcon(division) : '';

        const li = document.createElement('li');
        li.innerHTML = `
    <label data-division="${division}">
        <input type="checkbox" name="division" value="${division}">
        <span class="filter-label-copy">${renderFilterIcon(icon)} ${division.charAt(0).toUpperCase() + division.slice(1)} <span class="filter-count-group">(<span class="filter-count">${count}</span>)</span></span>
    </label>
`;
        filterSection.appendChild(li);
        const input = li.querySelector('input[type="checkbox"]');
        input.addEventListener('change', function (event) {
            event.stopPropagation(); // Prevent this event from affecting the sidebar toggle
            performFilter();
        });
    });
}

function generateFlagFilters(athletes) {
    const filterSection = document.querySelector('#flag-filter-section ul');
    filterSection.innerHTML = '';

    window.LwcFlags.countFlagUsage(athletes, athlete => athlete.canonicalFlag)
        .forEach(({ name, count }) => {
            const flagKey = getFlagFilterKey(name);
            const li = document.createElement('li');
            li.innerHTML = `
        <label data-flag="${escapeHtml(name)}" data-flag-key="${escapeHtml(flagKey)}">
            <input type="checkbox" name="flag" value="${escapeHtml(name)}">
            <span class="filter-label-copy">${renderFlagLabel(name)} <span class="filter-count-group">(<span class="filter-count">${count}</span>)</span></span>
        </label>
    `;
            filterSection.appendChild(li);

            const input = li.querySelector('input[type="checkbox"]');
            input.addEventListener('change', function (event) {
                event.stopPropagation();
                performFilter();
            });
        });
}

function generateGenerationFilters(athletes) {
    const filterSection = document.querySelector('#generation-filter-section ul');
    filterSection.innerHTML = ''; // Clear existing filters if any

    // Count occurrences of each generation
    const generationCounts = athletes.reduce((acc, athlete) => {
        acc[athlete.generation] = (acc[athlete.generation] || 0) + 1;
        return acc;
    }, {});

    // Order generations from oldest to youngest
    const orderedGenerations = ["Silent Generation", "Baby Boomers", "Gen X", "Millennials", "Gen Z", "Gen Alpha"];

    orderedGenerations.forEach(generation => {
        const count = generationCounts[generation] || 0;
        if (count > 0) { // Only show generations with at least one athlete
            const icon = window.TryGetGenerationFaIcon ? window.TryGetGenerationFaIcon(generation) : '';

            const li = document.createElement('li');
            li.innerHTML = `
        <label data-generation="${generation}">
            <input type="checkbox" name="generation" value="${generation}">
            <span class="filter-label-copy">${renderFilterIcon(icon)} ${generation} <span class="filter-count-group">(<span class="filter-count">${count}</span>)</span></span>
        </label>
    `;
            filterSection.appendChild(li);
            const input = li.querySelector('input[type="checkbox"]');
            input.addEventListener('change', function (event) {
                event.stopPropagation(); // Prevent this event from affecting the sidebar toggle
                performFilter();
            });
        }
    });
}

function generateExclusiveFilters(athletes) {
    const filterSection = document.querySelector('#exclusive-filter-section ul');
    filterSection.innerHTML = ''; // Clear existing filters if any

    const prosperanCount = athletes.reduce((count, athlete) => {
        return athlete.exclusiveLeague === "Prosperan" ? count + 1 : count;
    }, 0);

    if (prosperanCount > 0) {
        const li = document.createElement('li');
        li.innerHTML = `
        <label data-exclusive-league="Prosperan">
            <input type="checkbox" name="exclusiveLeague" value="Prosperan">
            <span class="filter-label-copy"><i class="fas fa-umbrella-beach filter-icon" aria-hidden="true"></i> Prosperan <span class="filter-count-group">(<span class="filter-count">${prosperanCount}</span>)</span></span>
        </label>
    `;
        filterSection.appendChild(li);

        const input = li.querySelector('input[type="checkbox"]');
        input.addEventListener('change', function (event) {
            event.stopPropagation(); // Prevent this event from affecting the sidebar toggle
            performFilter();
        });
    }
}

function generateLeagueTrackFilters(athletes) {
    const filterSection = document.querySelector('#league-track-filter-section ul');
    filterSection.innerHTML = ''; // Clear existing filters if any

    const isPro = (a) => a.bortzAgeReduction != null && Number.isFinite(a.bortzAgeReduction);
    const amateurCount = athletes.filter(a => !isPro(a)).length;
    const professionalCount = athletes.filter(a => isPro(a)).length;

    const tracks = [
        { value: 'Professional', count: professionalCount },
        { value: 'Amateur', count: amateurCount }
    ];

    tracks.forEach(({ value, count }) => {
        const label = getLeagueTrackLabel(value);
        const icon = window.TryGetLeagueTrackFaIcon ? window.TryGetLeagueTrackFaIcon(value) : '';
        const li = document.createElement('li');
        li.innerHTML = `
            <label data-league-track="${value}">
                <input type="checkbox" name="leagueTrack" value="${value}">
                <span class="filter-label-copy">${renderFilterIcon(icon)} ${label} <span class="filter-count-group">(<span class="filter-count">${count}</span>)</span></span>
            </label>
        `;
        filterSection.appendChild(li);
        const input = li.querySelector('input[type="checkbox"]');
        input.addEventListener('change', function (event) {
            event.stopPropagation();
            performFilter();
        });
    });
}

function escapeHtml(text) {
    return String(text)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function renderFilterIcon(iconClass) {
    return iconClass
        ? `<i class="fas ${escapeHtml(iconClass)} filter-icon" aria-hidden="true"></i>`
        : '';
}

function formatAthleteNameForMobile(name) {
    const safeName = escapeHtml(name);
    if (window.innerWidth >= 480) return safeName;
    return safeName.replace(/([a-z])([A-Z](?=[a-z]))/g, '$1<br>$2');
}

function getModalRankText(rank) {
    const numericRank = Number(rank);
    return Number.isFinite(numericRank) && numericRank > 0 ? `#${numericRank}` : '';
}

function getModalRankMarkup(rank) {
    const rankText = getModalRankText(rank);
    if (!rankText) return '';
    if (Number(rank) === 1) return '<span class="modal-rank-icon" aria-label="1st place"><i class="fa-solid fa-crown" aria-hidden="true"></i></span>';
    if (Number(rank) === 2) return '<span class="modal-rank-icon" aria-label="2nd place"><i class="fa-solid fa-medal" aria-hidden="true"></i></span>';
    if (Number(rank) === 3) return '<span class="modal-rank-icon" aria-label="3rd place"><i class="fa-solid fa-award" aria-hidden="true"></i></span>';
    return ` <span class="modal-rank-text">(${rankText})</span>`;
}

function getModalTitleRankMarkup(rank) {
    const rankText = getModalRankText(rank);
    return rankText ? `<span class="modal-rank-text"> (${rankText})</span>` : '';
}

function renderProfileRankings(athleteData, ultimateRank) {
    const rankings = document.getElementById('athleteRankings');
    const ultimateRankText = getModalRankText(ultimateRank);
    const rankingViews = ['bortz', 'pheno', 'bortz-improvement', 'pheno-improvement', 'crowd'];
    const bestView = (athleteData.bestRankCandidates || [])
        .filter(candidate => rankingViews.includes(candidate.leagueType)
            && candidate.href && getModalRankText(candidate.rank)
            && (!ultimateRankText || candidate.rank < Number(ultimateRank)))
        .sort(compareBestRankCandidates)[0];
    const athleteName = document.getElementById('athleteName');
    athleteName.querySelector('.modal-rank-text')?.remove();
    if (ultimateRankText && !bestView) {
        athleteName.insertAdjacentHTML('beforeend', getModalTitleRankMarkup(ultimateRank));
        rankings.replaceChildren();
        rankings.hidden = true;
        return;
    }
    const candidates = [
        { leagueType: 'ultimate', leagueName: 'Ultimate League', rank: ultimateRank, href: '/leaderboard' },
        bestView
    ].filter(candidate => candidate?.href && getModalRankText(candidate.rank));
    // Every ranking view retains the athlete's canonical row anchor.
    const anchor = ultimateRankText ? `#rank-${ultimateRank}` : '';
    rankings.innerHTML = candidates.map(candidate =>
        `<a class="league-link" data-competition="${candidate.leagueType}" href="${escapeHtml(candidate.href + anchor)}" aria-label="${escapeHtml(candidate.leagueName)}${candidate.leagueType === 'ultimate' ? '' : ' League'} rank ${candidate.rank}"><span>${escapeHtml(candidate.leagueName)}</span><strong>${getModalRankText(candidate.rank)}</strong></a>`
    ).join('');
    rankings.hidden = candidates.length === 0;
}

function getModalDetailRankMarkup(rank) {
    const rankText = getModalRankText(rank);
    return rankText ? ` <span class="detail-muted">(rank: <span class="detail-value">${rankText}</span>)</span>` : '';
}

function renderAthleteFlagDetail(flag, athleteData) {
    const canonicalFlag = getCanonicalFlagName(flag);
    if (!canonicalFlag) return '';

    const flagHref = buildFlagHref(canonicalFlag);
    const flagRank = athleteData && athleteData.ranks
        ? athleteData.ranks[getFlagRankKey(canonicalFlag)]
        : null;

    return `<a href="${escapeHtml(flagHref)}" class="league-link">${renderFlagLabel(canonicalFlag)}</a>${getModalDetailRankMarkup(flagRank)}`;
}

document.addEventListener('keydown', function (event) {
    if (event.key === 'Escape' || event.key === 'Esc') {
        closeOpenComponents();
    }
});
function closeOpenComponents(options) {
    const closeAll = !!(options && options.closeAll);
    const enlargedImg = document.querySelector('#athleteImageViewer[aria-hidden="false"]');
    if (enlargedImg) {
        if (options && options.fromHistory) {
            closeEnlargedView(enlargedImg);
        } else {
            requestCloseEnlargedView(enlargedImg);
        }
        if (!closeAll) return;
    }

    if (closeAthleteShareMenu({ restoreFocus: true })) {
        if (!closeAll) return;
    }

    // Keep the parent details modal locked until the Guess My Age response is confirmed.
    // Its nested image viewer and share menu remain independently dismissible above this guard.
    const modalContent = document.querySelector('.modal-content');
    if (isGuessMyAgeDismissBlocked(modalContent)) return;

    if (modal.style.display === "block") {
        if (options?.fromHistory) closeModal({ fromHistory: true });
        else requestCloseAthleteModal();
    }
}

function handleAthleteNameClick(event) {
    // Determine if it's a table row or podium item
    const athleteRow = getAthleteRow(event.target);
    if (!athleteRow) {
        console.error('Athlete row not found');
        return;
    }

    // Get athlete's name from data attribute
    const athleteNameText = athleteRow.getAttribute('data-athlete-name');

    // Find the athlete's data from athleteResults
    const athleteData = getAthleteData(athleteNameText);
    if (!athleteData) {
        console.error('Athlete data not found');
        return;
    }

    // Fetch the full athlete JSON data
    fetchFullAthleteData(athleteNameText, athleteData);
}

function getAthleteRow(element) {
    return element.closest('tr') || element.closest('.podium-item');
}

function getAthleteData(athleteNameText) {
    return findAthleteDataBySlug(athleteNameText);
}

function normalizeAthleteSlugForLookup(value) {
    const routeSlug = String(value || '').replace(/_/g, '-');
    if (typeof window.slugifyName === 'function') {
        return window.slugifyName(routeSlug, true);
    }

    return routeSlug
        .trim()
        .toLowerCase()
        .normalize('NFKD')
        .replace(/[\u0300-\u036f]/g, '')
        .replace(/\s+/g, '-')
        .replace(/[^a-z0-9-]/g, '')
        .replace(/-+/g, '-')
        .replace(/^-|-$/g, '');
}

function findAthleteDataBySlug(athleteSlug) {
    const normalizedSlug = normalizeAthleteSlugForLookup(athleteSlug);
    if (!normalizedSlug) return null;

    return athleteResults.find(athlete => {
        const canonicalSlug = normalizeAthleteSlugForLookup(athlete.athleteSlug);
        const nameSlug = normalizeAthleteSlugForLookup(athlete.name);
        const displayNameSlug = athlete.displayName ? normalizeAthleteSlugForLookup(athlete.displayName) : '';
        return canonicalSlug === normalizedSlug ||
            nameSlug === normalizedSlug ||
            displayNameSlug === normalizedSlug;
    }) || null;
}

function ensureAthleteResultsReady() {
    return Promise.all([
        Promise.resolve(window.modulesReady),
        Promise.resolve(window.athleteDialogModulesReady)
    ])
        .then(() => {
            if (athleteResultsReady) return athleteResults;
            if (!athleteResultsLoadPromise) {
                LoadLeaderboard(
                    isAthleteDialogOnlyRuntime ? false : includePodiumGlobal,
                    isAthleteDialogOnlyRuntime ? 0 : maxAthletesGlobal);
            }
            return athleteResultsLoadPromise;
        })
        .then(() => {
            if (!athleteResultsReady) {
                throw new Error('Athlete profile index could not be loaded.');
            }
            return athleteResults;
        });
}

function navigateToAthleteFallback(fallbackUrl) {
    currentAthleteModalRequestId++;
    modal.style.display = 'none';
    modal.classList.remove('fade-out');
    restoreBodyScroll();
    restoreAthleteHistoryScroll();
    window.location.assign(fallbackUrl);
}

window.openAthleteModalBySlug = function (athleteSlug, options) {
    const normalizedSlug = normalizeAthleteSlugForLookup(athleteSlug);
    if (!normalizedSlug) return false;

    const athleteData = findAthleteDataBySlug(normalizedSlug);
    if (athleteData) {
        if (options?.returnFocusTo instanceof HTMLElement) {
            athleteDialogReturnFocusElement = options.returnFocusTo;
        }
        fetchFullAthleteData(athleteData.name, athleteData, options);
        return true;
    }

    // Once the index is loaded, an unknown profile must retain ordinary
    // anchor navigation instead of swallowing a potentially valid URL.
    if (athleteResultsReady) {
        return false;
    }

    if (options?.returnFocusTo instanceof HTMLElement) {
        athleteDialogReturnFocusElement = options.returnFocusTo;
    }
    const requestId = ++currentAthleteModalRequestId;
    const modalContent = resetModalForLoading(normalizedSlug);

    ensureAthleteResultsReady()
        .then(() => {
            if (requestId !== currentAthleteModalRequestId) return;
            const loadedAthlete = findAthleteDataBySlug(normalizedSlug);
            if (!loadedAthlete) {
                if (options?.fallbackUrl) {
                    navigateToAthleteFallback(options.fallbackUrl);
                    return;
                }
                throw new Error(`Athlete not found for ${normalizedSlug}.`);
            }
            fetchFullAthleteData(loadedAthlete.name, loadedAthlete, options);
        })
        .catch(error => {
            if (requestId !== currentAthleteModalRequestId) return;
            if (options?.fallbackUrl) {
                navigateToAthleteFallback(options.fallbackUrl);
                return;
            }
            handleAthleteModalLoadFailure(modalContent, normalizedSlug, options, error);
        });

    return true;
};

function getAthleteSlugFromProfileLink(link) {
    if (link.hasAttribute('download')) return '';

    let url;
    try {
        url = new URL(link.getAttribute('href'), window.location.href);
    } catch (error) {
        return '';
    }

    if (url.protocol !== 'http:' && url.protocol !== 'https:') return '';
    const normalizedHostname = url.hostname.toLowerCase().replace(/^www\./, '');
    const isCurrentOrigin = url.origin === window.location.origin;
    if (!isCurrentOrigin && normalizedHostname !== 'longevityworldcup.com') return '';

    const match = url.pathname.match(/^\/athlete\/([^/]+)\/?$/i);
    if (!match) return '';

    try {
        return decodeURIComponent(match[1]);
    } catch (error) {
        return '';
    }
}

pageDocument.addEventListener('click', event => {
    if (!isPlainLinkActivation(event) ||
        !(event.target instanceof Element)) {
        return;
    }

    const link = event.target.closest('a[href]');
    if (!link ||
        link.closest('#detailsModal') ||
        link.closest('[data-athlete-dialog="off"]')) {
        return;
    }

    const athleteSlug = getAthleteSlugFromProfileLink(link);
    if (!athleteSlug) return;

    const beforeOpen = new CustomEvent('lwc:athlete-dialog-before-open', {
        bubbles: true,
        cancelable: true,
        detail: { athleteSlug }
    });
    if (!link.dispatchEvent(beforeOpen)) return;

    const returnFocusTo = link.getClientRects().length > 0
        ? link
        : pageDocument.activeElement instanceof HTMLElement
            ? pageDocument.activeElement
            : null;
    const opened = window.openAthleteModalBySlug(athleteSlug, {
        suppressGuessMyAge: true,
        fallbackUrl: link.href,
        returnFocusTo
    });
    if (opened) {
        event.preventDefault();
    }
});

function getAthletelessURL() {
    const url = new URL(window.location.href);
    if (/^\/athlete\/[^/]+\/?$/i.test(url.pathname)) url.pathname = '/';
    url.searchParams.delete('athlete');
    url.searchParams.delete('guessmyage');
    return url;
}

function getLeaguelessURL() {
    const url = new URL(window.location.href);
    const isCanonicalLeaderboardRoute = /^\/(?:league|flag)\/[^/]+\/?$/i.test(url.pathname);
    if (isCanonicalLeaderboardRoute) {
        url.pathname = pageDocument.querySelector('[data-leaderboard-page="full"]')
            ? '/leaderboard'
            : '/';
    }
    return url;
}

let originalAthletelessURL = getAthletelessURL();
let originalLeaguelessURL = getLeaguelessURL();

function getInitialAthleteSlug() {
    const athleteParam = new URLSearchParams(window.location.search).get('athlete');
    if (athleteParam) {
        return athleteParam;
    }

    const match = window.location.pathname.match(/^\/athlete\/([^/]+)\/?$/i);
    return match ? decodeURIComponent(match[1]) : '';
}

function getAthleteShareOrigin() {
    const hostname = window.location.hostname;
    const isLocalHost = hostname === 'localhost' || hostname === '127.0.0.1' || hostname === '[::1]' || hostname === '::1';
    if (!isLocalHost) {
        return window.location.origin;
    }

    const canonicalLink = pageDocument.querySelector('link[rel="canonical"]');
    try {
        return canonicalLink && canonicalLink.href
            ? new URL(canonicalLink.href).origin
            : 'https://longevityworldcup.com';
    } catch (error) {
        return 'https://longevityworldcup.com';
    }
}

function getCanonicalAthleteShareUrl(athleteSlug) {
    return new URL(`/athlete/${encodeURIComponent(athleteSlug)}`, getAthleteShareOrigin()).toString();
}

function setShareButtonState(button, label, disabled) {
    if (!button) return;
    button.disabled = false;
    button.setAttribute('aria-disabled', String(!!disabled));
    button.setAttribute('aria-busy', String(!!disabled));
    button.innerHTML = `<i class="fa fa-share-nodes" aria-hidden="true"></i> ${escapeHtml(label)}`;
}

function isCurrentAthleteShare(state) {
    return !!state && athleteShareState === state
        && state.requestId === currentAthleteModalRequestId && state.button.isConnected;
}

function resetAthleteShareState() {
    if (athleteShareState?.feedbackTimer) window.clearTimeout(athleteShareState.feedbackTimer);
    athleteShareState = null;
    closeAthleteShareMenu();
    setShareButtonState(document.getElementById('shareAthleteProfile'), 'Share', false);
    const status = document.getElementById('athleteShareStatus');
    if (status) status.textContent = '';
}

function copyTextToClipboard(text) {
    if (navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
        return navigator.clipboard.writeText(text);
    }

    return new Promise((resolve, reject) => {
        const previousFocus = document.activeElement;
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.setAttribute('readonly', '');
        textarea.style.position = 'fixed';
        textarea.style.left = '-9999px';
        textarea.style.top = '0';
        document.body.appendChild(textarea);
        textarea.select();
        try {
            document.execCommand('copy') ? resolve() : reject(new Error('copy failed'));
        } catch (error) {
            reject(error);
        } finally {
            document.body.removeChild(textarea);
            if (previousFocus instanceof HTMLElement && previousFocus.isConnected) previousFocus.focus({ preventScroll: true });
        }
    });
}

function shouldUseNativeAthleteShare(sharePayload) {
    const nav = window.navigator;
    if (!nav || typeof nav.share !== 'function') {
        return false;
    }

    const hasCoarsePointer = window.matchMedia && window.matchMedia('(pointer: coarse)').matches;
    const mobileTouchUserAgent = Number(nav.maxTouchPoints) > 0 && /Android|iPhone|iPad|iPod/i.test(nav.userAgent || '');
    if (!hasCoarsePointer && !mobileTouchUserAgent) {
        return false;
    }

    return typeof nav.canShare !== 'function' || nav.canShare(sharePayload);
}

function setShareMenuCopyState(label, disabled) {
    const copyButton = document.getElementById('copyAthleteProfileLink');
    if (!copyButton) return;
    copyButton.disabled = false;
    copyButton.setAttribute('aria-disabled', String(!!disabled));
    copyButton.setAttribute('aria-busy', String(!!disabled));
    copyButton.innerHTML = `<i class="fa fa-link" aria-hidden="true"></i> ${escapeHtml(label)}`;
}

function closeAthleteShareMenu(options) {
    const menu = document.getElementById('athleteShareMenu');
    if (!menu) return false;
    const wasOpen = !menu.hidden;
    if (athleteShareState) athleteShareState.menuVersion++;

    menu.hidden = true;
    menu.classList.remove('is-above');
    setShareMenuCopyState('Copy link', false);
    const recovery = document.getElementById('athleteShareRecovery');
    if (recovery) recovery.hidden = true;

    const button = document.getElementById('shareAthleteProfile');
    if (button) {
        button.setAttribute('aria-expanded', 'false');
        if (wasOpen && options && options.restoreFocus && !button.disabled) {
            button.focus({ preventScroll: true });
        }
    }

    return wasOpen;
}

function positionAthleteShareMenu(menu) {
    if (!menu || menu.hidden) return;

    menu.classList.remove('is-above');
    const modalContent = document.querySelector('#detailsModal .modal-content');
    const modalRect = modalContent ? modalContent.getBoundingClientRect() : null;
    const viewportBottom = Math.min(window.innerHeight, modalRect ? modalRect.bottom : window.innerHeight);
    const viewportTop = Math.max(0, modalRect ? modalRect.top : 0);
    const padding = 12;
    const belowRect = menu.getBoundingClientRect();

    if (belowRect.bottom <= viewportBottom - padding) return;

    menu.classList.add('is-above');
    const aboveRect = menu.getBoundingClientRect();
    if (aboveRect.top < viewportTop + padding) {
        menu.classList.remove('is-above');
    }
}

function keepAthleteShareMenuInView(menu) {
    if (!menu || menu.hidden) return;
    positionAthleteShareMenu(menu);
    menu.scrollIntoView({ block: 'nearest', inline: 'nearest' });
}

function getAthleteSharePayload(button) {
    return {
        title: button.dataset.shareTitle,
        text: button.dataset.shareText,
        url: button.dataset.shareUrl
    };
}

function configureAthleteShareMenu(button, sharePayload) {
    const menu = document.getElementById('athleteShareMenu');
    if (!menu) return;
    const state = athleteShareState;
    if (!isCurrentAthleteShare(state)) return;

    const text = sharePayload.text || sharePayload.title || '';
    const encodedText = encodeURIComponent(text);
    const encodedTitle = encodeURIComponent(sharePayload.title || 'Longevity World Cup');
    const encodedUrl = encodeURIComponent(sharePayload.url);
    const encodedEmailBody = encodeURIComponent(`${text}\n\n${sharePayload.url}`);

    const xLink = document.getElementById('shareAthleteProfileX');
    const facebookLink = document.getElementById('shareAthleteProfileFacebook');
    const linkedInLink = document.getElementById('shareAthleteProfileLinkedIn');
    const emailLink = document.getElementById('shareAthleteProfileEmail');

    if (xLink) xLink.href = `https://twitter.com/intent/tweet?text=${encodedText}&url=${encodedUrl}`;
    if (facebookLink) facebookLink.href = `https://www.facebook.com/sharer/sharer.php?u=${encodedUrl}`;
    if (linkedInLink) linkedInLink.href = `https://www.linkedin.com/sharing/share-offsite/?url=${encodedUrl}`;
    if (emailLink) emailLink.href = `mailto:?subject=${encodedTitle}&body=${encodedEmailBody}`;

    [xLink, facebookLink, linkedInLink, emailLink].forEach(link => {
        if (link) {
            link.onclick = () => closeAthleteShareMenu();
        }
    });

    const copyButton = document.getElementById('copyAthleteProfileLink');
    if (copyButton) {
        setShareMenuCopyState(state.pending === 'copy' ? 'Copying…' : 'Copy link', state.pending === 'copy');
        copyButton.onclick = async (event) => {
            event.preventDefault();
            event.stopPropagation();
            if (!isCurrentAthleteShare(state) || state.pending) return;
            const menuVersion = state.menuVersion;
            const recovery = document.getElementById('athleteShareRecovery');
            const manualLink = document.getElementById('athleteShareLink');
            if (recovery) recovery.hidden = true;
            state.pending = 'copy';
            setShareMenuCopyState('Copying…', true);

            try {
                await copyTextToClipboard(sharePayload.url);
                if (!isCurrentAthleteShare(state) || state.menuVersion !== menuVersion || menu.hidden) return;
                closeAthleteShareMenu({ restoreFocus: menu.contains(document.activeElement) });
                setShareButtonState(button, 'Copied', false);
                document.getElementById('athleteShareStatus').textContent = 'Link copied.';
                state.feedbackTimer = window.setTimeout(() => {
                    if (!isCurrentAthleteShare(state)) return;
                    state.feedbackTimer = null;
                    setShareButtonState(button, 'Share', false);
                    document.getElementById('athleteShareStatus').textContent = '';
                }, 1600);
            } catch (error) {
                if (!isCurrentAthleteShare(state) || state.menuVersion !== menuVersion || menu.hidden) return;
                if (recovery && manualLink) {
                    manualLink.value = sharePayload.url;
                    recovery.hidden = false;
                    keepAthleteShareMenuInView(menu);
                    if (document.activeElement === copyButton) {
                        manualLink.focus({ preventScroll: true });
                        manualLink.select();
                    }
                }
            } finally {
                if (isCurrentAthleteShare(state)) {
                    state.pending = null;
                    setShareMenuCopyState(recovery && !recovery.hidden ? 'Retry copy' : 'Copy link', false);
                }
            }
        };
    }
}

function openAthleteShareMenu(button, sharePayload) {
    const menu = document.getElementById('athleteShareMenu');
    const state = athleteShareState;
    if (!menu || !isCurrentAthleteShare(state)) {
        return;
    }

    if (!menu.hidden) {
        closeAthleteShareMenu();
        return;
    }

    if (state.feedbackTimer) window.clearTimeout(state.feedbackTimer);
    state.feedbackTimer = null;
    setShareButtonState(button, 'Share', false);
    document.getElementById('athleteShareStatus').textContent = '';
    state.menuVersion++;
    configureAthleteShareMenu(button, sharePayload);
    menu.hidden = false;
    button.setAttribute('aria-expanded', 'true');

    const copyButton = document.getElementById('copyAthleteProfileLink');
    if (copyButton) {
        copyButton.focus({ preventScroll: true });
    }
    keepAthleteShareMenuInView(menu);
    requestAnimationFrame(() => keepAthleteShareMenuInView(menu));
}

function configureAthleteShareButton(displayName, athleteSlug, ultimateRank) {
    const button = document.getElementById('shareAthleteProfile');
    if (!button || !athleteSlug) return;
    resetAthleteShareState();
    const state = { button, requestId: currentAthleteModalRequestId, pending: null, menuVersion: 0, feedbackTimer: null };
    athleteShareState = state;

    const url = getCanonicalAthleteShareUrl(athleteSlug);
    const title = `${displayName} | Longevity World Cup`;
    const text = Number.isFinite(ultimateRank)
        ? `${displayName} is ranked #${ultimateRank} in the Longevity World Cup.`
        : `View ${displayName} in the Longevity World Cup.`;

    button.dataset.shareUrl = url;
    button.dataset.shareTitle = title;
    button.dataset.shareText = text;
    button.setAttribute('aria-label', `Share ${displayName}'s athlete profile`);
    setShareButtonState(button, 'Share', false);

    button.onclick = async (event) => {
        event.preventDefault();
        event.stopPropagation();
        if (!isCurrentAthleteShare(state) || state.pending === 'native') return;
        if (state.feedbackTimer) window.clearTimeout(state.feedbackTimer);
        state.feedbackTimer = null;
        document.getElementById('athleteShareStatus').textContent = '';

        const sharePayload = getAthleteSharePayload(button);

        if (!state.pending && shouldUseNativeAthleteShare(sharePayload)) {
            state.pending = 'native';
            setShareButtonState(button, 'Sharing…', true);
            try {
                await window.navigator.share(sharePayload);
            } catch (error) {
                if (isCurrentAthleteShare(state) && error?.name !== 'AbortError') {
                    openAthleteShareMenu(button, sharePayload);
                }
            } finally {
                if (isCurrentAthleteShare(state)) {
                    state.pending = null;
                    setShareButtonState(button, 'Share', false);
                }
            }
            return;
        }

        openAthleteShareMenu(button, sharePayload);
    };
}

document.addEventListener('click', function(event) {
    const menu = document.getElementById('athleteShareMenu');
    const target = event.target && event.target.closest ? event.target : event.target?.parentElement;
    if (!menu || menu.hidden || !target || !target.closest) return;
    if (target.closest('#athleteShareMenu') || target.closest('#shareAthleteProfile')) return;
    closeAthleteShareMenu();
});

function handleAthleteModalLoadFailure(modalContent, athleteSlug, options, error) {
    window.__sharedAthletesRequest = null;

    // Keep readable server content when enhancement fails. The existing
    // retry control can retry the interactive details without blanking it.
    const preserveServerProfile = modalContent?.dataset.serverRenderedProfile === athleteSlug;

    if (modalContent) {
        modalContent.classList.toggle('has-load-error', !preserveServerProfile);
        modalContent.classList.remove('is-loading');
    }

    const athleteNameElement = document.getElementById('athleteName');
    if (athleteNameElement && !preserveServerProfile) athleteNameElement.textContent = 'Athlete details unavailable';
    const stickyAthleteName = document.getElementById('stickyAthleteName');
    if (stickyAthleteName && !preserveServerProfile) stickyAthleteName.textContent = 'Athlete details unavailable';
    const profilePic = document.getElementById('modalProfilePic');
    if (profilePic && !preserveServerProfile) profilePic.alt = '';
    const loadError = document.getElementById('athleteLoadError');
    if (loadError) loadError.hidden = false;
    const retryButton = document.getElementById('retryAthleteLoad');
    if (retryButton) {
        const retryOptions = options
            ? { ...options, restoreEnlargedState: undefined }
            : undefined;
        retryButton.onclick = () => window.openAthleteModalBySlug(athleteSlug, retryOptions);
    }

    if (options?.restoreEnlargedState && history.state?.modal === 'enlarged') {
        history.back();
    }

    console.error('Error fetching full athlete data:', error);
}

function fetchFullAthleteData(athleteNameText, athleteData, options) {
    window.initializeSharedPageData();
    athleteDialogReturnFocusElement =
        options?.returnFocusTo instanceof HTMLElement
            ? options.returnFocusTo
            : null;

    const athleteSlug = normalizeAthleteSlugForLookup(
        athleteData?.athleteSlug || athleteNameText);
    const normalizedAthleteName = normalizeAthleteSlugForLookup(athleteNameText);
    const suppressGuessMyAge = !!(options && options.suppressGuessMyAge);
    const requestId = ++currentAthleteModalRequestId;
    const modalContent = resetModalForLoading(athleteSlug);

    return window.getSharedAthletes()
        .then(athletes => {
            if (requestId !== currentAthleteModalRequestId) return;
            const fullAthleteData = athletes.find(a => {
                const canonicalSlug = normalizeAthleteSlugForLookup(a.AthleteSlug);
                const nameSlug = normalizeAthleteSlugForLookup(a.Name);
                return canonicalSlug === athleteSlug || nameSlug === normalizedAthleteName;
            });
            if (!fullAthleteData) {
                throw new Error(`Full athlete data not found for ${athleteSlug}`);
            }

            const profileImageId = String(fullAthleteData.ProfileImageId || '').trim().toLowerCase();
            const guessStateIdentity = {
                ...athleteData,
                AthleteSlug: fullAthleteData.AthleteSlug,
                Name: fullAthleteData.Name,
                DisplayName: fullAthleteData.DisplayName,
                ProfileImageId: profileImageId
            };
            athleteData.profileImageId = profileImageId;
            if (modalContent) {
                modalContent.dataset.athleteSlug = athleteSlug;
                if (profileImageId) modalContent.dataset.profileImageId = profileImageId;
                else delete modalContent.dataset.profileImageId;
            }

            const canonicalGuessSlug = window.LwcGuessState?.getAthleteSlug(guessStateIdentity)
                || athleteSlug;
            const initialRouteMatchesGuessIdentity = [
                canonicalGuessSlug,
                normalizeAthleteSlugForLookup(fullAthleteData.Name),
                normalizeAthleteSlugForLookup(fullAthleteData.DisplayName)
            ].includes(initialAthleteSlugToSkipForGuessMyAge);
            if (initialAthleteSlugToSkipForGuessMyAge &&
                initialRouteMatchesGuessIdentity) {
                markAthleteSkippedForGuessMyAge(guessStateIdentity);
                // This intent belongs only to the portrait resolved for the
                // initial direct route, not every later image for this athlete.
                initialAthleteSlugToSkipForGuessMyAge = '';
            } else {
                // Trigger the one-time legacy slug/name migration before badges
                // and modal state read from the content-addressed history.
                window.LwcGuessState?.get(guessStateIdentity);
            }
            const rankSummary = computeBestLeagueRank(athleteData);

            // Populate the modal with athlete data
            populateModal(fullAthleteData, athleteData, rankSummary);
            delete modalContent.dataset.serverRenderedProfile;
            modalContent.querySelectorAll('[data-profile-pending]').forEach(element => {
                element.hidden = false;
                element.removeAttribute('data-profile-pending');
            });

            updatePageTitleForAthlete((fullAthleteData.DisplayName && fullAthleteData.DisplayName.trim()) ? fullAthleteData.DisplayName.trim() : fullAthleteData.Name, rankSummary.ultimateRank);

            // Age visualization (radar + center) and biomarker chart are created after modal is shown so canvases have layout dimensions.
            const bioAge = (athleteData.lowestBortzAge != null && Number.isFinite(athleteData.lowestBortzAge))
                ? athleteData.lowestBortzAge
                : athleteData.lowestPhenoAge;
            const chronoAge = (athleteData.lowestBortzAge != null && Number.isFinite(athleteData.lowestBortzAge))
                ? athleteData.chronoAtLowestBortzAge
                : athleteData.chronoAtLowestPhenoAge;

            // After populating the modal with athlete data but before showing it:
            // Only update originalAthletelessURL when we're not already on an athlete URL, so a second open (e.g. URL sync with ?guessmyage=1) does not overwrite it with "/"
            const pathnameBeforeOpen = window.location.pathname;
            if (!/^\/athlete\/[^/]+\/?$/.test(pathnameBeforeOpen)) {
                originalAthletelessURL = getAthletelessURL();
            }
            const historyMode = options?.historyMode || 'push';
            const previousState = history.state;
            if (historyMode === 'replace' || (historyMode === 'push'
                && previousState?.modal === 'details' && previousState.athlete === athleteSlug)) {
                history.replaceState({ ...previousState, modal: 'details', athlete: athleteSlug }, '', window.location.href);
            } else if (historyMode === 'push') {
                const previousIsProfile = previousState?.modal === 'details' || previousState?.modal === 'enlarged';
                const previousDepth = previousState?.athleteDialogDepth;
                const depth = previousIsProfile
                    ? (Number.isInteger(previousDepth) && previousDepth > 0
                        ? previousDepth + (previousState.modal === 'enlarged' ? 2 : 1)
                        : 0)
                    : 1;
                history.pushState({ modal: 'details', athlete: athleteSlug, athleteDialogDepth: depth }, '', `/athlete/${athleteSlug}`);
            }

            // Biomarker chart is created after modal is shown (see below) so the canvas has layout dimensions.

            // Populate proofs gallery after deferred modal sections are available.
            populateProofsGallery(fullAthleteData);

            // Create biomarker chart after modal is visible so the canvas has non-zero dimensions (fixes chart sizing when created while modal was hidden).
            requestAnimationFrame(function() {
                if (requestId !== currentAthleteModalRequestId) return;

                window.ensureChartJs().then(function(chartLoaded) {
                    if (requestId !== currentAthleteModalRequestId) return;

                    generateAgeVisualization(bioAge, chronoAge, athleteData, athleteResults);
                    if (chartLoaded) {
                        generateBiomarkerChart(fullAthleteData);
                    }

                    // Force Chart.js to re-measure after layout; hover triggers this internally, so do it explicitly (second rAF + ResizeObserver fallback).
                    requestAnimationFrame(function() {
                        if (requestId !== currentAthleteModalRequestId) return;
                        scheduleBiomarkerChartResize();
                    });

                    var chartContainer = document.getElementById('athlete-biomarkers');
                    if (chartContainer && window.biomarkerChartInstance) {
                        if (window.biomarkerChartResizeObserver) window.biomarkerChartResizeObserver.disconnect();
                        if ('ResizeObserver' in window) {
                            window.biomarkerChartResizeObserver = new ResizeObserver(scheduleBiomarkerChartResize);
                            window.biomarkerChartResizeObserver.observe(chartContainer);
                        }
                    }
                });
            });
            if (!modalContent) {
                console.error('Modal content element not found');
                return;
            }
            modalContent.classList.remove('is-loading');

            modalContent.dataset.athleteSlug = athleteSlug;
            if (profileImageId) modalContent.dataset.profileImageId = profileImageId;
            else delete modalContent.dataset.profileImageId;

            // Always start at top when modal opens
            modalContent.scrollTop = 0;

            // Hide sticky header initially
            const stickyHeader = document.getElementById('modalStickyHeader');
            if (stickyHeader) {
                stickyHeader.classList.remove('visible');
            }

            // Initialize scroll progress indicator
            initializeScrollProgress(modalContent);

            // Setup scroll-to-section navigation (optional floating menu for long content)
            setupScrollToSectionNavigation(modalContent);

            var frame = document.getElementById('events-frame');
            if (frame) {
                const frameSrc = `/event-board-embed.html?athlete=${encodeURIComponent(athleteSlug)}&rows=all&viewAll=false&linkNames=false&embed=1&theme=dark`;
                frame.addEventListener('load', function () {
                    if (frame.dataset.expectedAthlete === athleteSlug) {
                        frame.style.visibility = '';
                    }
                }, { once: true });
                frame.src = frameSrc;
            }

            const gmaCard = document.getElementById('guessAgeContainer');

            // always reset the Guess-My-Age UI first:
            modalContent.classList.remove('guess-mode', 'gma-fast', 'gma-result-ready');
            gmaCard.classList.remove('gma-done');
            const athleteGuessState = window.LwcGuessState?.get(guessStateIdentity);
            // check if the user has hit “Skip All”
            const skipAll = localStorage.getItem('gmaSkipAll') === 'true';

            const seen = sessionStorage.getItem('gmaSeen');

            if (suppressGuessMyAge) {
                markAthleteSkippedForGuessMyAge(guessStateIdentity);
                modalContent.classList.remove('guess-mode');
                gmaCard.classList.add('gma-done');
            } else if (!profileImageId) {
                // The game cannot safely accept a guess unless it can prove
                // which portrait the visitor saw.
                modalContent.classList.remove('guess-mode');
                gmaCard.classList.add('gma-done');
            } else if (skipAll) {
                // user opted out globally
                modalContent.classList.remove('guess-mode');
                gmaCard.classList.add('gma-done');
            } else if (athleteGuessState) {
                // already guessed or skipped this athlete
                modalContent.classList.remove('guess-mode');
                gmaCard.classList.add('gma-done');
            } else {
                // first time on *this* athlete
                modalContent.classList.add('guess-mode');
                sessionStorage.setItem('gmaSeen', 'true');
            }

            const gmaSkipAllBtn = document.getElementById('gmaSkipAll');
            if (gmaSkipAllBtn) {
                // reveal “Skip All” once they've skipped at least one athlete,
                // but hide it if they already chose skip-all
                const hasSkippedGuess = (window.LwcGuessState?.getAll() || [])
                    .some(entry => entry.state.skipped === true);
                if (!skipAll && hasSkippedGuess) {
                    gmaSkipAllBtn.style.display = 'inline-block';
                } else {
                    gmaSkipAllBtn.style.display = 'none';
                }
            }

            // stash the slug for your submit/skip handler
            modalContent.dataset.athleteSlug = athleteSlug;
            updateYourGuess();

            if (!suppressGuessMyAge) {
                if (seen) {
                    modalContent.classList.add('gma-fast');
                } else {
                    sessionStorage.setItem('gmaSeen', 'true');
                }
            }

            const bortzPaceOfAgingContainer = document.getElementById('bortzPaceOfAgingContainer');
            const bortzPaceOfAgingValue =
                (athleteData.lowestBortzAge && athleteData.chronoAtLowestBortzAge)
                    ? (athleteData.lowestBortzAge / athleteData.chronoAtLowestBortzAge)
                    : null;
            if (Number.isFinite(bortzPaceOfAgingValue)) {
                document.getElementById('bortzPaceOfAging').textContent = bortzPaceOfAgingValue.toFixed(2);
                bortzPaceOfAgingContainer.style.display = '';
                const bortzPaceOfAgingRank = findAthleteIndexByName(athletesOrderedByBortzPace, athleteData.name) + 1;
                document.getElementById('bortzPaceOfAgingRank').textContent =
                    Number.isFinite(bortzPaceOfAgingRank) && bortzPaceOfAgingRank > 0
                        ? '#' + bortzPaceOfAgingRank
                        : '';
            } else {
                bortzPaceOfAgingContainer.style.display = 'none';
            }

            const paceOfAgingContainer = document.getElementById('paceOfAgingContainer');
            const paceOfAgingValue =
                (athleteData.lowestPhenoAge && athleteData.chronoAtLowestPhenoAge)
                    ? (athleteData.lowestPhenoAge / athleteData.chronoAtLowestPhenoAge)
                    : null;
            if (Number.isFinite(paceOfAgingValue)) {
                document.getElementById('paceOfAging').textContent = paceOfAgingValue.toFixed(2);
                paceOfAgingContainer.style.display = '';

                const paceOfAgingRank = findAthleteIndexByName(athletesOrderedByPace, athleteData.name) + 1;
                document.getElementById('paceOfAgingRank').textContent =
                    Number.isFinite(paceOfAgingRank) && paceOfAgingRank > 0
                        ? '#' + paceOfAgingRank
                        : '';
            } else {
                paceOfAgingContainer.style.display = 'none';
            }

            const currentEnlargedState = history.state;
            if (options?.restoreEnlargedState
                && currentEnlargedState?.modal === 'enlarged'
                && currentEnlargedState.athlete === athleteSlug
                && !restoreEnlargedViewFromHistory(currentEnlargedState)) {
                history.replaceState({ ...history.state, modal: 'details', athlete: athleteSlug }, '', window.location.href);
            }

        })
        .catch(error => {
            if (requestId !== currentAthleteModalRequestId) return;
            handleAthleteModalLoadFailure(modalContent, athleteSlug, options, error);
        });
}

async function refreshAthleteAfterStaleGuess(athleteSlug) {
    const normalizedSlug = normalizeAthleteSlugForLookup(athleteSlug);
    if (!normalizedSlug) return false;

    try {
        const refreshUrl = `/api/data/athletes?profileImageRefresh=${Date.now()}`;
        const response = await fetchWithTimeout(refreshUrl, {
            cache: 'no-store',
            headers: { accept: 'application/json' }
        }, 10000);
        if (!response.ok) return false;

        const athletes = await response.json();
        if (!Array.isArray(athletes)) return false;

        const fullAthleteData = athletes.find(athlete =>
            normalizeAthleteSlugForLookup(athlete.AthleteSlug) === normalizedSlug ||
            normalizeAthleteSlugForLookup(athlete.Name) === normalizedSlug);
        const athleteData = findAthleteDataBySlug(normalizedSlug);
        if (!fullAthleteData || !athleteData) return false;

        // Replace the shared snapshot so the ordinary modal loader consumes
        // the same fresh portrait identity we just fetched.
        window.__sharedAthletesRequest = Promise.resolve(athletes);
        athleteData.profilePic = fullAthleteData.ProfilePic;
        athleteData.profilePicThumb = fullAthleteData.ProfilePicLeaderboardThumb
            || fullAthleteData.ProfilePicThumb
            || fullAthleteData.ProfilePic;
        athleteData.profileImageId = fullAthleteData.ProfileImageId;
        athleteData.Badges = Array.isArray(fullAthleteData.Badges)
            ? fullAthleteData.Badges
            : [];
        updateAthleteCrowdAge(normalizedSlug, fullAthleteData.CrowdAge, fullAthleteData.CrowdCount);

        await fetchFullAthleteData(athleteData.name, athleteData, {
            historyMode: 'preserve'
        });
        const refreshedModalContent = document.querySelector('#detailsModal .modal-content');
        const refreshedProfileImageId = String(fullAthleteData.ProfileImageId || '')
            .trim()
            .toLowerCase();
        return refreshedProfileImageId.length > 0 &&
            refreshedModalContent?.dataset.profileImageId === refreshedProfileImageId &&
            !refreshedModalContent.classList.contains('has-load-error');
    } catch (error) {
        console.error('Error refreshing athlete after profile image change:', error);
        window.__sharedAthletesRequest = null;
        return false;
    }
}

function computeBestLeagueRank(athleteData) {
    if (!athleteData) {
        return {
            ultimateRank: null,
            bestCandidate: null,
            bestLeagueRank: null,
            bestLeagueName: 'Ultimate League',
            bestLeagueType: 'ultimate'
        };
    }

    if (!Array.isArray(athleteData.bestRankCandidates) || athleteData.bestRankCandidates.length === 0) {
        assignBestRankCandidates(athleteResults);
    }

    const ultimateRank = Number.isFinite(athleteData.rank) ? athleteData.rank : null;
    let candidates = Array.isArray(athleteData.bestRankCandidates)
        ? athleteData.bestRankCandidates
        : [];
    if (candidates.length === 0 && Number.isFinite(ultimateRank)) {
        candidates = [{
            rank: ultimateRank,
            leagueName: 'Ultimate League',
            leagueLabel: 'Ultimate League',
            leagueType: 'ultimate',
            href: '/leaderboard',
            targetBlank: true,
            tiePriority: 0
        }];
    }
    const best = candidates.slice().sort(compareBestRankCandidates)[0] || null;

    return {
        ultimateRank,
        bestCandidate: best,
        bestLeagueRank: best ? best.rank : ultimateRank,
        bestLeagueName: best ? best.leagueName : 'Ultimate League',
        bestLeagueType: best ? best.leagueType : 'ultimate'
    };
}

function getCompletedChronologicalAge(fullAthleteData, athleteData) {
    const dob = fullAthleteData && fullAthleteData.DateOfBirth;
    if (dob && typeof window.calculateCompletedYearsAtDate === 'function') {
        return window.calculateCompletedYearsAtDate(
            new Date(dob.Year, dob.Month - 1, dob.Day),
            new Date()
        );
    }

    return Math.floor(athleteData.chronologicalAge);
}

function populateModal(fullAthleteData, athleteData, rankSummary) {
    const ultimateRank = rankSummary ? rankSummary.ultimateRank : athleteData.rank;
    const modalDisplayName = (fullAthleteData.DisplayName && fullAthleteData.DisplayName.trim()) ? fullAthleteData.DisplayName.trim() : fullAthleteData.Name;
    const isPro = athleteData.bortzAgeReduction != null && Number.isFinite(athleteData.bortzAgeReduction);
    const shareSlugSource = String(fullAthleteData.AthleteSlug || athleteData.athleteSlug || athleteData.slug || modalDisplayName).replace(/_/g, '-');
    const shareAthleteSlug = window.slugifyName(shareSlugSource, true);
    const athleteNameElement = document.getElementById('athleteName');
    athleteNameElement.textContent = modalDisplayName;
    renderProfileRankings(athleteData, ultimateRank);
    configureAthleteShareButton(modalDisplayName, shareAthleteSlug, ultimateRank);

    const stickyAthleteName = document.getElementById('stickyAthleteName');
    if (stickyAthleteName) {
        stickyAthleteName.textContent = modalDisplayName;
    }

    const profilePic = document.getElementById('modalProfilePic');
    profilePic.src = getAthletePortraitUrl(fullAthleteData);
    profilePic.dataset.fullSrc = fullAthleteData.ProfilePic || profilePic.src;
    profilePic.alt = `${modalDisplayName} Profile Picture`;
    profilePic.loading = 'lazy';
    profilePic.classList.remove('portrait-fallback');
    wirePortraitFallback(profilePic, fullAthleteData);

    const athleteProfileEl = document.getElementById('athlete-profile');
    if (athleteProfileEl) {
        if (isPro) athleteProfileEl.classList.add('is-pro');
        else athleteProfileEl.classList.remove('is-pro');
    }
    const modalStickyHeader = document.getElementById('modalStickyHeader');
    if (modalStickyHeader) {
        if (isPro) modalStickyHeader.classList.add('is-pro');
        else modalStickyHeader.classList.remove('is-pro');
    }

    document.getElementById('athleteBio').textContent = fullAthleteData.Why;

    document.getElementById('athleteRank').innerHTML = renderBestRankLink(rankSummary && rankSummary.bestCandidate);
    document.getElementById('athleteDivision').innerHTML = `<a href="/league/${window.slugifyName(athleteData.division, true)}" class="league-link">${escapeHtml(athleteData.division)}</a>${getModalTitleRankMarkup(athleteData.ranks[athleteData.division])}`;
    document.getElementById('athleteGeneration').innerHTML = `<a href="/league/${window.slugifyName(athleteData.generation, true)}" class="league-link">${escapeHtml(athleteData.generation)}</a>${getModalTitleRankMarkup(athleteData.ranks[athleteData.generation])}`;
    document.getElementById('athleteFlag').innerHTML = renderAthleteFlagDetail(fullAthleteData.Flag || athleteData.canonicalFlag, athleteData);
    document.getElementById('chronologicalAge').textContent = getCompletedChronologicalAge(fullAthleteData, athleteData).toString();
    updateCrowdContainer(athleteData.crowdAge, athleteData.crowdCount);
    updateYourGuess();
    const lowestBortzAgeEl = document.getElementById('lowestBortzAge');
    const lowestBortzAgeContainer = document.getElementById('lowestBortzAgeContainer');
    if (athleteData.lowestBortzAge != null && Number.isFinite(athleteData.lowestBortzAge)) {
        lowestBortzAgeEl.textContent = athleteData.lowestBortzAge.toFixed(1);
        lowestBortzAgeContainer.style.display = '';
    } else {
        lowestBortzAgeContainer.style.display = 'none';
    }
    document.getElementById('lowestPhenoAge').textContent = athleteData.lowestPhenoAge.toFixed(1);
    const bortzAgeReductionContainer = document.getElementById('bortzAgeReductionContainer');
    const bortzAgeReductionLabel = document.getElementById('bortzAgeReductionAccelerationLabel');
    if (athleteData.bortzAgeReduction != null && Number.isFinite(athleteData.bortzAgeReduction)) {
        bortzAgeReductionLabel.textContent = athleteData.bortzAgeReduction < 0 ? "Bortz Age reduction:" : "Bortz Age acceleration:";
        document.getElementById('bortzAgeReduction').textContent = (athleteData.bortzAgeReduction >= 0 ? "+" : "") + athleteData.bortzAgeReduction.toFixed(1);
        document.getElementById('bortzAgeReductionPercent').textContent = athleteData.bortzAgeReductionPercent.toFixed(1);
        bortzAgeReductionContainer.style.display = '';
    } else {
        bortzAgeReductionContainer.style.display = 'none';
    }
    document.getElementById('ageReductionAccelerationLabel').textContent = athleteData.ageReduction < 0 ? "Pheno Age reduction:" : "Pheno Age acceleration:";
    document.getElementById('ageReduction').textContent = (athleteData.ageReduction >= 0 ? "+" : "") + athleteData.ageReduction.toFixed(1);
    document.getElementById('ageReductionPercent').textContent = athleteData.ageReductionPercent.toFixed(1);
    updateAthleteInfoGrouping();

    // Personal Link
    const personalLinkElement = document.getElementById('personalLink');
    const personalLink = window.normalizeWebUrl(athleteData.personalLink);
    if (personalLink) {
        personalLinkElement.href = personalLink;
        personalLinkElement.style.display = 'flex';
        personalLinkElement.title = `Visit personal page of ${modalDisplayName}`;
    } else {
        personalLinkElement.style.display = 'none';
    }

    // Media Contact
    const mediaContactElement = document.getElementById('mediaContact');
    const mediaContact = window.parseMediaContact(athleteData.mediaContact);
    if (mediaContact) {
        if (mediaContact.href) {
            mediaContactElement.href = mediaContact.href;
            mediaContactElement.innerHTML = `${window.getIcon(athleteData.mediaContact)} Media contact`;
            mediaContactElement.setAttribute('aria-label', 'Contact the athlete');
        } else {
            mediaContactElement.removeAttribute('href');
            mediaContactElement.textContent = athleteData.mediaContact;
            mediaContactElement.removeAttribute('aria-label');
        }
        mediaContactElement.style.display = 'flex';
        mediaContactElement.title = mediaContact.isEmail ? `Email ${modalDisplayName}` : `Contact ${modalDisplayName}`;
    } else {
        mediaContactElement.style.display = 'none';
    }

    // — Add badge strip into the modal —
    const modalBadgeSection = document.getElementById('modalBadgeStrip');
    modalBadgeSection.innerHTML = '';
    // Clone athleteData but disable personalLink for modal badges
    const athleteForBadges = { ...athleteData, personalLink: null };
    window.setBadges(athleteForBadges, {
        querySelector: () => modalBadgeSection
    });

    // wrap each badge in a column with its name
    const badges = Array.from(modalBadgeSection.children);
    modalBadgeSection.innerHTML = '';
    badges.forEach(badge => {
        const fullTitle = badge.getAttribute('title') || '';
        const colonIndex = fullTitle.indexOf(':');
        let titlePart = fullTitle;
        let detailPart = '';
        if (colonIndex !== -1) {
            titlePart = fullTitle.slice(0, colonIndex);       // before the colon
            detailPart = fullTitle.slice(colonIndex + 1).trim(); // after the colon
        }

        const wrapper = document.createElement('div');
        wrapper.classList.add('modal-badge-item');
        wrapper.setAttribute('role', 'button');
        wrapper.setAttribute('tabindex', '0');
        wrapper.setAttribute('aria-expanded', 'false');
        wrapper.setAttribute('aria-label', fullTitle ? `Show badge details: ${fullTitle}` : 'Show badge details');
        wrapper.appendChild(badge);

        const label = document.createElement('div');
        label.classList.add('modal-badge-name');
        if (detailPart) {
            label.innerHTML =
                `<span class="modal-badge-title">${titlePart}</span>` +
                `<span class="modal-badge-detail">${detailPart}</span>`;
        } else {
            label.innerHTML = `<span class="modal-badge-title">${titlePart}</span>`;
        }
        wrapper.appendChild(label);

        const toggleBadgeDetails = (event) => {
            if (event.target.closest('a.badge-podcast')) return;

            const isExpanded = wrapper.classList.toggle('expanded');
            wrapper.setAttribute('aria-expanded', isExpanded ? 'true' : 'false');
        };

        wrapper.addEventListener('click', toggleBadgeDetails);
        wrapper.addEventListener('keydown', (event) => {
            if (event.key !== 'Enter' && event.key !== ' ') return;
            if (event.target.closest('a.badge-podcast')) return;

            event.preventDefault();
            toggleBadgeDetails(event);
        });
        modalBadgeSection.appendChild(wrapper);
    });

}

function generateBiomarkerChart(fullAthleteData) {
    const biomarkerChartCtx = document.getElementById('biomarkerChart').getContext('2d');
    const biomarkerData = fullAthleteData.Biomarkers.sort((a, b) => new Date(a.Date) - new Date(b.Date));
    const dob = new Date(
        fullAthleteData.DateOfBirth.Year,
        fullAthleteData.DateOfBirth.Month - 1,
        fullAthleteData.DateOfBirth.Day
    );

    const chartData = biomarkerData.filter(entry => entry && entry.Date && Object.entries(entry).some(([key, value]) =>
        key !== 'Date' && value != null && value !== '' && Number.isFinite(Number(value))
    ));
    if (chartData.length === 0) {
        if (window.biomarkerChartInstance) {
            window.biomarkerChartInstance.destroy();
        }
        return;
    }

    const labels = chartData.map(entry => entry.Date);
    const phenoAges = [];
    const bortzAges = [];
    const biomarkersRaw = initializeBiomarkersRaw();

    chartData.forEach(entry => {
        const entryDate = new Date(entry.Date);
        const chronoAgeAtEntry = window.calculateAgeAtDate(dob, entryDate);

        // Push in same order as bortz-age.html (WBC, Lymphocytes, Neutrophils, …)
        biomarkersRaw['WBC (10^3 cells/µL)'].push(entry.Wbc1000cellsuL != null ? entry.Wbc1000cellsuL : null);
        biomarkersRaw['Lymphocytes (%)'].push(entry.LymPc != null ? entry.LymPc : null);
        biomarkersRaw['Neutrophils (%)'].push(entry.NeutrophilPc != null ? entry.NeutrophilPc : null);
        biomarkersRaw['Monocytes (%)'].push(entry.MonocytePc != null ? entry.MonocytePc : null);
        biomarkersRaw['RBC (10¹²/L)'].push(entry.Rbc10e12L != null ? entry.Rbc10e12L : null);
        biomarkersRaw['MCV (fL)'].push(entry.McvFL != null ? entry.McvFL : null);
        biomarkersRaw['MCH (pg)'].push(entry.MchPg != null ? entry.MchPg : null);
        biomarkersRaw['RDW (%)'].push(entry.RdwPc != null ? entry.RdwPc : null);
        biomarkersRaw['Albumin (g/L)'].push(entry.AlbGL != null ? entry.AlbGL : null);
        biomarkersRaw['ALT (U/L)'].push(entry.AltUL != null ? entry.AltUL : null);
        biomarkersRaw['ALP (U/L)'].push(entry.AlpUL != null ? entry.AlpUL : null);
        biomarkersRaw['GGT (U/L)'].push(entry.GgtUL != null ? entry.GgtUL : null);
        biomarkersRaw['Urea (mmol/L)'].push(entry.UreaMmolL != null ? entry.UreaMmolL : null);
        biomarkersRaw['Creatinine (µmol/L)'].push(entry.CreatUmolL != null ? entry.CreatUmolL : null);
        biomarkersRaw['Cystatin C (mg/L)'].push(entry.CystatinCMgL != null ? entry.CystatinCMgL : null);
        biomarkersRaw['Glucose (mmol/L)'].push(entry.GluMmolL != null ? entry.GluMmolL : null);
        biomarkersRaw['HbA1c (mmol/mol)'].push(entry.Hba1cMmolMol != null ? entry.Hba1cMmolMol : null);
        biomarkersRaw['Cholesterol (mmol/L)'].push(entry.CholesterolMmolL != null ? entry.CholesterolMmolL : null);
        biomarkersRaw['ApoA1 (g/L)'].push(entry.ApoA1GL != null ? entry.ApoA1GL : null);
        biomarkersRaw['CRP (mg/L)'].push(entry.CrpMgL != null ? entry.CrpMgL : null);
        biomarkersRaw['SHBG (nmol/L)'].push(entry.ShbgNmolL != null ? entry.ShbgNmolL : null);
        biomarkersRaw['Vitamin D (nmol/L)'].push(entry.VitaminDNmolL != null ? entry.VitaminDNmolL : null);

        let phenoAge = null;
        if (isCompleteBiomarkerSet(entry)) {
            const biomarkerValues = prepareBiomarkerValues(entry, chronoAgeAtEntry);
            const computed = window.PhenoAge.calculatePhenoAge(biomarkerValues);
            if (Number.isFinite(computed)) phenoAge = computed.toFixed(1);
        }
        phenoAges.push(phenoAge);

        // Bortz Age (when full Bortz set available)
        let bortzAge = null;
        if (window.BortzAge && typeof window.BortzAge.calculateBortzAge === 'function' && isCompleteBortzBiomarkerSet(entry)) {
            const wbc = entry.Wbc1000cellsuL;
            const monoCount = wbc * entry.MonocytePc / 100;
            const neutCount = wbc * entry.NeutrophilPc / 100;
            const bortzValues = [
                chronoAgeAtEntry,
                entry.AlbGL,
                entry.AlpUL,
                entry.UreaMmolL,
                entry.CholesterolMmolL,
                entry.CreatUmolL,
                entry.CystatinCMgL,
                entry.Hba1cMmolMol,
                entry.CrpMgL,
                entry.GgtUL,
                entry.Rbc10e12L,
                entry.McvFL,
                entry.RdwPc,
                monoCount,
                neutCount,
                entry.LymPc,
                entry.AltUL,
                entry.ShbgNmolL,
                entry.VitaminDNmolL,
                entry.GluMmolL,
                entry.MchPg,
                entry.ApoA1GL
            ];
            const computed = window.BortzAge.calculateBortzAge(chronoAgeAtEntry, bortzValues);
            if (Number.isFinite(computed)) bortzAge = computed.toFixed(1);
        }
        bortzAges.push(bortzAge);
    });

    const biomarkersZScores = calculateBiomarkerZScores(biomarkersRaw);
    const datasets = prepareBiomarkerDatasets(labels, phenoAges, bortzAges, biomarkersRaw, biomarkersZScores);

    if (window.biomarkerChartInstance) {
        window.biomarkerChartInstance.destroy();
    }

    window.biomarkerChartInstance = new Chart(biomarkerChartCtx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: datasets
        },
        options: getBiomarkerChartOptions()
    });
    renderBiomarkerChartControls(window.biomarkerChartInstance);
}

function renderBiomarkerChartControls(chart) {
    const section = document.getElementById('athlete-biomarkers');
    const canvas = document.getElementById('biomarkerChart');
    if (!section || !canvas || !chart) return;

    const existing = section.querySelector('.biomarker-chart-controls');
    if (existing) existing.remove();

    const biomarkerDatasets = chart.data.datasets
        .map((dataset, index) => ({ dataset, index }))
        .filter(item => item.dataset.yAxisID === 'y2');

    if (biomarkerDatasets.length === 0) return;

    const controls = document.createElement('div');
    controls.className = 'biomarker-chart-controls';

    const select = document.createElement('select');
    select.id = 'biomarkerOverlaySelect';
    select.setAttribute('aria-label', 'Compare biomarker');

    const none = document.createElement('option');
    none.value = '';
    none.textContent = 'Select a biomarker';
    select.appendChild(none);

    biomarkerDatasets
        .slice()
        .sort((a, b) => a.dataset.label.localeCompare(b.dataset.label))
        .forEach(item => {
            const option = document.createElement('option');
            option.value = String(item.index);
            option.textContent = item.dataset.label;
            select.appendChild(option);
        });
    select.value = '';
    const initialSelectedIndex = null;
    biomarkerDatasets.forEach(item => {
        chart.setDatasetVisibility(item.index, item.index === initialSelectedIndex);
    });
    if (chart.options.scales && chart.options.scales.y2) {
        chart.options.scales.y2.display = false;
    }
    chart.update('none');

    select.addEventListener('change', function () {
        const selectedIndex = select.value === '' ? null : Number(select.value);
        let state = localStorage.getItem('biomarkerVisibilityState');
        state = state ? JSON.parse(state) : {};

        biomarkerDatasets.forEach(item => {
            const shouldHide = item.index !== selectedIndex;
            chart.setDatasetVisibility(item.index, !shouldHide);
            state[item.dataset.label] = shouldHide;
        });
        if (chart.options.scales && chart.options.scales.y2) {
            chart.options.scales.y2.display = selectedIndex !== null;
        }

        localStorage.setItem('biomarkerVisibilityState', JSON.stringify(state));
        chart.update();
    });

    controls.appendChild(select);
    section.insertBefore(controls, canvas);
}

// Biomarker key order matches bortz-age.html card order (WBC, Lymphocytes, Neutrophils, …)
function initializeBiomarkersRaw() {
    return {
        'WBC (10^3 cells/µL)': [],
        'Lymphocytes (%)': [],
        'Neutrophils (%)': [],
        'Monocytes (%)': [],
        'RBC (10¹²/L)': [],
        'MCV (fL)': [],
        'MCH (pg)': [],
        'RDW (%)': [],
        'Albumin (g/L)': [],
        'ALT (U/L)': [],
        'ALP (U/L)': [],
        'GGT (U/L)': [],
        'Urea (mmol/L)': [],
        'Creatinine (µmol/L)': [],
        'Cystatin C (mg/L)': [],
        'Glucose (mmol/L)': [],
        'HbA1c (mmol/mol)': [],
        'Cholesterol (mmol/L)': [],
        'ApoA1 (g/L)': [],
        'CRP (mg/L)': [],
        'SHBG (nmol/L)': [],
        'Vitamin D (nmol/L)': []
    };
}

function collectBiomarkerValues(entry, biomarkersRaw) {
    function pushOrInherit(key, newValue) {
        // Retrieve the last known value if available
        const lastValue = biomarkersRaw[key].length > 0 ? biomarkersRaw[key][biomarkersRaw[key].length - 1] : undefined;
        // Use newValue if provided; otherwise, inherit lastValue
        const valueToUse = newValue != null ? newValue : lastValue;
        // Update the persistent storage
        biomarkersRaw[key].push(valueToUse);
        // Return the value used so we can update the entry
        return valueToUse;
    }
    entry.Wbc1000cellsuL = pushOrInherit('WBC (10^3 cells/µL)', entry.Wbc1000cellsuL);
    entry.LymPc = pushOrInherit('Lymphocytes (%)', entry.LymPc);
    entry.McvFL = pushOrInherit('MCV (fL)', entry.McvFL);
    entry.RdwPc = pushOrInherit('RDW (%)', entry.RdwPc);
    entry.AlbGL = pushOrInherit('Albumin (g/L)', entry.AlbGL);
    entry.AlpUL = pushOrInherit('ALP (U/L)', entry.AlpUL);
    entry.CreatUmolL = pushOrInherit('Creatinine (µmol/L)', entry.CreatUmolL);
    entry.GluMmolL = pushOrInherit('Glucose (mmol/L)', entry.GluMmolL);
    entry.CrpMgL = pushOrInherit('CRP (mg/L)', entry.CrpMgL);
}

function prepareBiomarkerValues(entry, chronoAgeAtEntry) {
    return [
        chronoAgeAtEntry,
        entry.AlbGL,
        entry.CreatUmolL,
        entry.GluMmolL,
        Math.log(entry.CrpMgL / 10),
        entry.Wbc1000cellsuL,
        entry.LymPc,
        entry.McvFL,
        entry.RdwPc,
        entry.AlpUL
    ];
}

function calculateBiomarkerZScores(biomarkersRaw) {
    const biomarkersZScores = {};
    for (const [key, values] of Object.entries(biomarkersRaw)) {
        const hasNulls = values.some(v => v == null);
        if (hasNulls) {
            const valid = values.filter(v => v != null && Number.isFinite(v));
            const n = valid.length;
            if (n === 0) {
                biomarkersZScores[key] = values.map(() => null);
            } else {
                const mean = valid.reduce((sum, val) => sum + val, 0) / n;
                const variance = valid.reduce((sum, val) => sum + Math.pow(val - mean, 2), 0) / n;
                const stdDev = Math.sqrt(variance);
                biomarkersZScores[key] = values.map(val =>
                    val == null || !Number.isFinite(val) ? null : (stdDev === 0 ? 0 : ((val - mean) / stdDev).toFixed(2))
                );
            }
        } else {
            const mean = values.reduce((sum, val) => sum + val, 0) / values.length;
            const variance = values.reduce((sum, val) => sum + Math.pow(val - mean, 2), 0) / values.length;
            const stdDev = Math.sqrt(variance);
            biomarkersZScores[key] = stdDev === 0
                ? values.map(() => 0)
                : values.map(val => ((val - mean) / stdDev).toFixed(2));
        }
    }
    return biomarkersZScores;
}

function getAthleteModalCssVar(name, fallback) {
    const modal = document.getElementById('detailsModal');
    const value = modal ? window.getComputedStyle(modal).getPropertyValue(name).trim() : '';
    return value || fallback;
}

function prepareBiomarkerDatasets(labels, phenoAges, bortzAges, biomarkersRaw, biomarkersZScores) {
    const hasBortzAge = bortzAges.some(v => v != null && Number.isFinite(Number(v)));
    const bortzColor = getAthleteModalCssVar('--athlete-modal-chart-bortz', 'rgba(154,232,178,.96)');
    const phenoColor = getAthleteModalCssVar('--athlete-modal-chart-pheno', 'rgba(135,229,229,.96)');
    const biomarkerOverlayColor = getAthleteModalCssVar('--athlete-modal-chart-biomarker', 'rgba(255,125,125,.96)');
    const pointBorderColor = 'rgba(8,10,10,.94)';
    const pointHitRadius = window.matchMedia && window.matchMedia('(pointer: coarse)').matches ? 18 : 8;
    const datasets = [];
    if (hasBortzAge) {
        datasets.push({
            label: 'Bortz Age',
            data: bortzAges,
            borderColor: bortzColor,
            backgroundColor: 'rgba(154,232,178,.14)',
            pointBackgroundColor: bortzColor,
            pointBorderColor: pointBorderColor,
            pointBorderWidth: 2,
            pointRadius: 5,
            pointHoverRadius: 7,
            pointHitRadius: pointHitRadius,
            fill: false,
            tension: 0.1,
            borderWidth: 3,
            yAxisID: 'y1',
            spanGaps: true
        });
    }
    datasets.push({
        label: 'Pheno Age',
        data: phenoAges,
        borderColor: phenoColor,
        backgroundColor: 'rgba(135,229,229,.14)',
        pointBackgroundColor: phenoColor,
        pointBorderColor: pointBorderColor,
        pointBorderWidth: 2,
        pointRadius: 5,
        pointHoverRadius: 7,
        pointHitRadius: pointHitRadius,
        fill: false,
        tension: 0.1,
        borderWidth: 3,
        yAxisID: 'y1'
    });

    for (const [key, zScores] of Object.entries(biomarkersZScores)) {
        const actualValues = biomarkersRaw[key];
        const hasAtLeastOneValue = actualValues.some(v => v != null && Number.isFinite(v));
        if (!hasAtLeastOneValue) continue;

        const dataPoints = zScores.map((zScore, index) => ({
            x: labels[index],
            y: zScore,
            actualValue: actualValues[index]
        }));

        // Retrieve saved visibility state from localStorage
        let savedState = localStorage.getItem('biomarkerVisibilityState');
        let isHidden = true; // default value

        if (savedState) {
            const state = JSON.parse(savedState);
            // Use the saved state if it exists for this biomarker key
            if (state.hasOwnProperty(key)) {
                isHidden = state[key];
            }
        }

        datasets.push({
            label: key,
            data: dataPoints,
            borderColor: biomarkerOverlayColor,
            backgroundColor: 'rgba(255,125,125,.16)',
            pointBackgroundColor: biomarkerOverlayColor,
            pointBorderColor: pointBorderColor,
            pointBorderWidth: 2,
            pointRadius: 5,
            pointHoverRadius: 7,
            pointHitRadius: pointHitRadius,
            fill: false,
            tension: 0.1,
            borderWidth: 3,
            yAxisID: 'y2',
            hidden: isHidden,
            spanGaps: true
        });
    }
    return datasets;
}

function getBiomarkerChartOptions() {
    const chartTextColor = getAthleteModalCssVar('--athlete-modal-text', '#333');
    const chartMutedColor = getAthleteModalCssVar('--athlete-modal-muted', '#666');
    const chartGridColor = getAthleteModalCssVar('--athlete-modal-chart-grid', 'rgba(0,0,0,0.08)');
    return {
        responsive: true,
        maintainAspectRatio: true,
        aspectRatio: window.innerWidth < 768 ? 1.05 : 1.6,
        layout: {
            padding: {
                left: window.innerWidth < 768 ? 4 : 12,
                right: window.innerWidth < 768 ? 4 : 12,
                top: 4
            }
        },
        interaction: {
            mode: 'index',
            intersect: false
        },
        stacked: false,
        plugins: {
            tooltip: {
                backgroundColor: 'rgba(5,7,7,.94)',
                titleColor: 'rgba(255,255,255,.96)',
                bodyColor: 'rgba(255,255,255,.9)',
                borderColor: 'rgba(246,244,237,.34)',
                borderWidth: 1,
                cornerRadius: 6,
                padding: 10,
                displayColors: true,
                callbacks: {
                    label: function (context) {
                        const label = context.dataset.label || '';
                        const yValue = context.parsed.y;
                        const actualValue = context.raw && context.raw.actualValue;
                        if (label === 'Pheno Age' || label === 'Bortz Age') {
                            return yValue != null ? `${label}: ${yValue}` : `${label}: N/A`;
                        }
                        const formattedValue = actualValue !== undefined && actualValue != null && !isNaN(actualValue)
                            ? (actualValue % 1 !== 0 ? actualValue.toFixed(2) : actualValue.toString())
                            : 'N/A';
                        return `${label}: ${formattedValue}`;
                    }
                }
            },
            legend: {
                position: 'bottom',
                labels: {
                    color: chartMutedColor,
                    boxWidth: 9,
                    boxHeight: 9,
                    padding: 14,
                    usePointStyle: true,
                    font: {
                        size: window.innerWidth < 768 ? 10 : 12,
                        weight: '600'
                    },
                    filter: function (legendItem, chartData) {
                        const dataset = chartData.datasets[legendItem.datasetIndex];
                        return dataset && dataset.yAxisID === 'y1';
                    }
                },
                onHover: function (event) {
                    event.native.target.style.cursor = 'pointer';
                },
                onLeave: function (event) {
                    event.native.target.style.cursor = 'default';
                },
                onClick: function (e, legendItem, legend) {
                    // Execute the default onClick handler
                    Chart.defaults.plugins.legend.onClick.call(this, e, legendItem, legend);
                    // Get the chart instance and dataset metadata
                    const ci = legend.chart;
                    const index = legendItem.datasetIndex;
                    const meta = ci.getDatasetMeta(index);

                    // Store the biomarker's new visibility status
                    // Retrieve the stored state; if none exists, start with an empty object
                    let state = localStorage.getItem('biomarkerVisibilityState');
                    state = state ? JSON.parse(state) : {};

                    // Update the state with the new key-value pair
                    state[meta.label] = meta.hidden;

                    // Save it back to localStorage as a JSON string
                    localStorage.setItem('biomarkerVisibilityState', JSON.stringify(state));
                }
            }
        },
        scales: {
            y1: {
                type: 'linear',
                display: true,
                position: 'left',
                title: {
                    display: true,
                    text: 'Age (years)',
                    color: chartTextColor,
                    font: {
                        size: window.innerWidth < 768 ? 10 : 14 // Adjust font size for mobile
                    }
                },
                ticks: {
                    color: chartMutedColor
                },
                grid: {
                    drawOnChartArea: true,
                    color: chartGridColor
                }
            },
            y2: {
                type: 'linear',
                display: true,
                position: 'right',
                title: {
                    display: true,
                    text: 'Biomarkers (Z-scores)',
                    color: chartTextColor,
                    font: {
                        size: window.innerWidth < 768 ? 10 : 14
                    }
                },
                ticks: {
                    color: chartMutedColor
                },
                grid: {
                    drawOnChartArea: false,
                    color: chartGridColor
                }
            },
            x: {
                title: {
                    display: true,
                    text: 'Date',
                    color: chartTextColor,
                    font: {
                        size: window.innerWidth < 768 ? 10 : 14
                    }
                },
                ticks: {
                    color: chartMutedColor
                },
                grid: {
                    color: chartGridColor
                }
            }
        }
    };
}

function populateProofsGallery(fullAthleteData) {
    const proofsGallery = document.getElementById('proofsGallery');
    if (!proofsGallery) return;
    proofsGallery.innerHTML = '';

    const proofs = Array.isArray(fullAthleteData && fullAthleteData.Proofs)
        ? fullAthleteData.Proofs.filter(Boolean)
        : [];
    const previewCount = 6;
    const toggle = proofsGallery.closest('.proofs-section')?.querySelector('.proofs-toggle');
    proofsGallery.dataset.proofCount = String(proofs.length);
    const setExpanded = expanded => {
        proofsGallery.querySelectorAll('.proof-item').forEach((item, index) => {
            item.hidden = !expanded && index >= previewCount;
        });
        if (toggle) {
            toggle.hidden = proofs.length <= previewCount;
            toggle.setAttribute('aria-expanded', String(expanded));
            toggle.textContent = expanded ? 'Show fewer' : `Show all ${proofs.length} proofs`;
        }
    };
    proofsGallery._setExpanded = setExpanded;
    if (toggle) toggle.onclick = () => setExpanded(toggle.getAttribute('aria-expanded') !== 'true');
    if (proofs.length === 0) {
        const empty = document.createElement('p');
        empty.className = 'proofs-empty';
        empty.textContent = 'No public proofs available yet.';
        proofsGallery.appendChild(empty);
    }

    proofs.forEach((proofUrl, index) => {
        const proofItem = document.createElement('div');
        proofItem.classList.add('proof-item');
        const img = document.createElement('img');
        img.src = proofUrl;
        img.alt = `Proof image ${index + 1}`;
        img.setAttribute('role', 'button');
        img.setAttribute('tabindex', '0');
        img.setAttribute('aria-label', `Enlarge proof image ${index + 1}`);
        img.loading = 'lazy';
        proofItem.appendChild(img);
        const number = document.createElement('span');
        number.className = 'proof-number';
        number.textContent = `Proof ${index + 1}`;
        number.setAttribute('aria-hidden', 'true');
        proofItem.appendChild(number);
        proofsGallery.appendChild(proofItem);

        const openProofImage = function () {
            openEnlargedView(img);
        };
        proofItem.addEventListener('click', openProofImage);
        img.addEventListener('keydown', function (event) {
            if (event.key !== 'Enter' && event.key !== ' ') return;
            event.preventDefault();
            openProofImage.call(this);
        });
    });
    setExpanded(false);
}

// History navigation restores native form values after popstate. Reconcile
// the rows afterward instead of relying on a later prize request to do it.
let leaderboardHistoryRestoreTimer = 0;
function reconcileRestoredLeaderboard() {
    if (isAthleteDialogOnlyRuntime) return;
    window.clearTimeout(leaderboardHistoryRestoreTimer);
    leaderboardHistoryRestoreTimer = window.setTimeout(() => {
        if (athleteResultsReady && renderedLeaderboardSelection !== getLeaderboardSelectionKey()) {
            performFilter({ updateUrl: false });
        } else if (athleteResultsReady) {
            // Back/Forward and rank-anchor changes can update the destination
            // without changing any filters or requiring new rows.
            updateLeaderboardNavigationLinks();
        }
    }, 0);
}
window.addEventListener('pageshow', reconcileRestoredLeaderboard);
window.addEventListener('popstate', reconcileRestoredLeaderboard);
window.addEventListener('hashchange', reconcileRestoredLeaderboard);

// Ensure modal history cleanup
window.addEventListener('popstate', function (event) {
    if (event.state?.modal === 'enlarged') {
        const viewer = document.getElementById('athleteImageViewer');
        if (viewer?.getAttribute('aria-hidden') === 'false') {
            return;
        }

        const parentCanRestore = modal.style.display === 'block'
            && !modal.classList.contains('fade-out');
        if (parentCanRestore && restoreEnlargedViewFromHistory(event.state)) {
            return;
        }

        const restored = event.state.athlete
            && window.openAthleteModalBySlug(event.state.athlete, {
                historyMode: 'preserve',
                restoreEnlargedState: event.state
            });
        if (!restored) {
            history.replaceState({}, '', originalAthletelessURL);
        }
        return;
    }

    if (event.state?.modal === 'details') {
        const viewer = document.querySelector('#athleteImageViewer[aria-hidden="false"]');
        if (viewer) {
            closeEnlargedView(viewer);
        }

        if (modal.style.display !== 'block' || modal.classList.contains('fade-out')
            || modal.querySelector('.modal-content')?.dataset.athleteSlug !== event.state.athlete) {
            const restored = event.state.athlete
                && window.openAthleteModalBySlug(event.state.athlete, { historyMode: 'preserve' });
            if (!restored) {
                history.replaceState({}, '', originalAthletelessURL);
            }
        }
        return;
    }

    const modalContent = document.querySelector('#detailsModal .modal-content');
    if (modal.style.display === 'block' && isGuessMyAgeDismissBlocked(modalContent)) {
        // Guess My Age deliberately cannot be abandoned mid-round. Undo a
        // Back-menu jump, then let the details-state pop close any viewer.
        history.forward();
        return;
    }

    closeOpenComponents({ fromHistory: true, closeAll: true });
});

const sidebar = document.querySelector('.sidebar');
const sidebarToggle = document.querySelector('.sidebar-toggle');
const sidebarClose = document.querySelector('.sidebar-close');
const hasLeaderboardSidebar = !!(sidebar && sidebarToggle && sidebarClose);
const mobileDrawerMediaQuery = window.matchMedia('(max-width: 768px), (max-width: 932px) and (max-height: 480px) and (orientation: landscape)');
let leaderboardTableHeightFrame = 0;
let leaderboardTableResizeObserver = null;

function isMobileDrawerViewport() {
    return mobileDrawerMediaQuery.matches;
}

function scheduleLeaderboardTableHeightSync() {
    if (leaderboardTableHeightFrame) return;
    leaderboardTableHeightFrame = requestAnimationFrame(syncLeaderboardTableHeight);
}

function syncLeaderboardTableHeight() {
    leaderboardTableHeightFrame = 0;

    const leaderboard = document.querySelector('.leaderboard');
    const table = document.querySelector('.leaderboard > table');
    if (!leaderboard || !table) return;

    if (isMobileDrawerViewport()) {
        leaderboard.style.removeProperty('--leaderboard-table-height');
        leaderboard.style.removeProperty('--leaderboard-title-height');
        return;
    }

    const tableHeight = table.getBoundingClientRect().height;
    if (tableHeight > 0) {
        leaderboard.style.setProperty('--leaderboard-table-height', `${Math.ceil(tableHeight)}px`);
        syncCollapsedTitleHeight();
    }
}

function installLeaderboardTableHeightObserver() {
    const table = document.querySelector('.leaderboard > table');
    if (!table) return;

    if ('ResizeObserver' in window) {
        leaderboardTableResizeObserver = new ResizeObserver(scheduleLeaderboardTableHeightSync);
        leaderboardTableResizeObserver.observe(table);
        const title = document.querySelector('.collapsed-title');
        if (title) leaderboardTableResizeObserver.observe(title);
    }

    scheduleLeaderboardTableHeightSync();
}

sidebarToggle?.addEventListener('click', (event) => {
    toggleSidebar({ focusDrawerClose: event.detail === 0 });
});

sidebarToggle?.addEventListener('mouseenter', () => {
    if (sidebar.classList.contains('expanded')) return;
    sidebar.classList.add('partially-expanded');
});

sidebarToggle?.addEventListener('mouseleave', () => {
    sidebar.classList.remove('partially-expanded');
});

sidebarClose?.addEventListener('click', (event) => {
    closeSidebar({ restoreFocus: event.detail === 0 });
});

sidebar?.addEventListener('change', (event) => {
    if (!event.target.matches('input[type="checkbox"]')) return;

    requestAnimationFrame(pinActiveDesktopSidebar);
}, true);

document.addEventListener('click', (event) => {
    if (!hasLeaderboardSidebar) return;
    const isMobileDrawer = isMobileDrawerViewport();
    if (!sidebar.classList.contains('expanded')) return;
    if (sidebar.contains(event.target) || sidebarToggle.contains(event.target)) return;

    if (isMobileDrawer || hasActiveLeaderboardFilterState()) {
        closeSidebar();
    }
});

document.addEventListener('keydown', (event) => {
    if (!hasLeaderboardSidebar) return;
    if (event.key !== 'Tab') return;
    if (modal.style.display === "block") return;

    const isMobileDrawer = isMobileDrawerViewport();
    if (!isMobileDrawer || !sidebar.classList.contains('expanded')) return;

    const focusableDrawerControls = getSidebarFocusableControls();
    if (focusableDrawerControls.length === 0) return;

    const firstControl = focusableDrawerControls[0];
    const lastControl = focusableDrawerControls[focusableDrawerControls.length - 1];
    const activeElement = document.activeElement;

    if (!sidebar.contains(activeElement)) {
        event.preventDefault();
        focusSidebarControl(firstControl);
    }
    else if (event.shiftKey && activeElement === firstControl) {
        event.preventDefault();
        focusSidebarControl(lastControl);
    }
    else if (!event.shiftKey && activeElement === lastControl) {
        event.preventDefault();
        focusSidebarControl(firstControl);
    }
});

document.addEventListener('keydown', (event) => {
    if (!hasLeaderboardSidebar) return;
    if (event.key !== 'Escape' && event.key !== 'Esc') return;
    if (modal.style.display === "block") return;

    const searchInput = document.getElementById('athleteSearch');
    if (document.activeElement === searchInput && searchInput.value.trim().length > 0) return;

    const isMobileDrawer = isMobileDrawerViewport();
    if (!isMobileDrawer || !sidebar.classList.contains('expanded')) return;

    event.preventDefault();
    closeSidebar({ restoreFocus: true });
});

function syncSidebarDrawerBackdrop() {
    if (!hasLeaderboardSidebar) return;
    const isMobileDrawer = isMobileDrawerViewport();
    const isOpenMobileDrawer = isMobileDrawer && sidebar.classList.contains('expanded');
    document.body.classList.toggle('sidebar-drawer-open', isOpenMobileDrawer);
    sidebar.setAttribute('aria-hidden', isMobileDrawer && !isOpenMobileDrawer ? 'true' : 'false');
}

function getSidebarFocusableControls() {
    if (!hasLeaderboardSidebar) return [];
    return Array.from(sidebar.querySelectorAll('a[href], button, input:not([type="hidden"]), select, textarea, [tabindex]:not([tabindex="-1"])'))
        .filter(element => {
            if (element.disabled || element.getAttribute('aria-hidden') === 'true') return false;

            const style = window.getComputedStyle(element);
            if (style.display === 'none' || style.visibility === 'hidden') return false;

            const rect = element.getBoundingClientRect();
            return rect.width > 0 && rect.height > 0;
        });
}

function focusSidebarControl(element) {
    try {
        element.focus({ focusVisible: true });
    }
    catch {
        element.focus();
    }
}

function toggleSidebar(options = {}) {
    if (!hasLeaderboardSidebar) return;
    if (sidebar.classList.contains('expanded')) {
        closeSidebar();
    }
    else {
        openSidebar(options);
    }
}

function pinActiveDesktopSidebar() {
    if (!hasLeaderboardSidebar) return;
    if (isMobileDrawerViewport()) return;
    if (!hasActiveLeaderboardFilterState()) return;
    if (sidebar.classList.contains('expanded')) return;

    openSidebar({ skipScroll: true });
}

function openSidebar(options = {}) {
    if (!hasLeaderboardSidebar) return;
    const isMobileDrawer = isMobileDrawerViewport();
    const drawerScrollX = window.scrollX;
    const drawerScrollY = window.scrollY;

    scheduleLeaderboardTableHeightSync();
    sidebar.classList.remove('partially-expanded');
    sidebar.classList.remove('collapsed');

    if (!sidebar.classList.contains('expanded')) {
        sidebar.classList.add('expanded');
    }
    sidebar.setAttribute('aria-hidden', 'false');
    if (!sidebarToggle.classList.contains('active')) {
        sidebarToggle.classList.add('active')
    }
    syncSidebarDrawerBackdrop();

    sidebarToggle.title = "Hide league filters";
    sidebarToggle.setAttribute('aria-label', 'Hide league filters');
    sidebarToggle.setAttribute('aria-expanded', 'true');

    if (isMobileDrawer) {
        requestAnimationFrame(() => {
            if (options.focusDrawerClose) {
                try {
                    sidebarClose.focus({ preventScroll: true, focusVisible: true });
                }
                catch {
                    sidebarClose.focus({ preventScroll: true });
                }
            }
            window.scrollTo(drawerScrollX, drawerScrollY);
        });
    }

    // Scroll to the leaderboard table
    if (!isMobileDrawer && !options.skipScroll && canAutoScroll()) {
        document.querySelector('.search-wrapper .sidebar-toggle').scrollIntoView({
            behavior: 'smooth',
            block: 'start',
        });
    }
}

function closeSidebar(options = {}) {
    if (!hasLeaderboardSidebar) return;
    sidebar.classList.remove('expanded');
    sidebar.setAttribute('aria-hidden', 'true');
    sidebarToggle.classList.remove('active');
    sidebar.classList.remove('partially-expanded');
    if (!sidebar.classList.contains('button-collapsed')) {
        sidebar.classList.add('button-collapsed');
    }
    sidebarToggle.title = "Show league filters";
    sidebarToggle.setAttribute('aria-label', 'Show league filters');
    sidebarToggle.setAttribute('aria-expanded', 'false');
    syncSidebarDrawerBackdrop();
    requestAnimationFrame(syncCollapsedTitleHeight);

    if (options.restoreFocus) {
        requestAnimationFrame(() => {
            sidebarToggle.classList.add('is-keyboard-focus');
            sidebarToggle.addEventListener('blur', () => {
                sidebarToggle.classList.remove('is-keyboard-focus');
            }, { once: true });
            try {
                sidebarToggle.focus({ preventScroll: true, focusVisible: true });
            }
            catch {
                sidebarToggle.focus({ preventScroll: true });
            }
        });
    }

    setTimeout(() => {
        sidebar.classList.remove('button-collapsed');
    }, 50);
}

if (hasLeaderboardSidebar) {
    syncSidebarDrawerBackdrop();
    installLeaderboardTableHeightObserver();
    window.addEventListener('resize', () => {
        syncSidebarDrawerBackdrop();
        scheduleLeaderboardTableHeightSync();
    });
    if (typeof mobileDrawerMediaQuery.addEventListener === 'function') {
        mobileDrawerMediaQuery.addEventListener('change', scheduleLeaderboardTableHeightSync);
    } else if (typeof mobileDrawerMediaQuery.addListener === 'function') {
        mobileDrawerMediaQuery.addListener(scheduleLeaderboardTableHeightSync);
    }
}

// Sidebar swipe gesture
let startX = 0;
let endX = 0;
let startY = 0;
let endY = 0;
const SWIPE_THRESHOLD = 50; // Minimum distance to qualify as a swipe
const TAP_THRESHOLD = 10; // Allowable movement for taps

document.addEventListener('touchstart', (e) => {
    if (!hasLeaderboardSidebar) return;
    if (modal.style.display === "block") return;

    startX = e.changedTouches[0].clientX;
    startY = e.changedTouches[0].clientY;
}, false);

document.addEventListener('touchend', (e) => {
    if (!hasLeaderboardSidebar) return;
    if (modal.style.display === "block") return;

    endX = e.changedTouches[0].clientX;
    endY = e.changedTouches[0].clientY;

    const deltaX = endX - startX; // Horizontal movement
    const deltaY = endY - startY; // Vertical movement

    // Check if it's a tap (small movement)
    if (Math.abs(deltaX) < TAP_THRESHOLD && Math.abs(deltaY) < TAP_THRESHOLD) {
        return; // Do nothing, treat as a tap
    }

    // Check for swipe
    if (Math.abs(deltaX) > Math.abs(deltaY) && Math.abs(deltaX) > SWIPE_THRESHOLD) {
        if (deltaX > 0) {
            // Swiped from left to right => open sidebar
            openSidebar();
        } else {
            // Swiped from right to left => close sidebar
            closeSidebar();
        }
    }
}, false);

// Updates the document title for the selected athlete.
function updatePageTitleForAthlete(athleteName, athleteRank) {
    pageDocument.title = `${athleteName} (#${athleteRank}) - Longevity World Cup`;
}

// Restore the latest leaderboard title rather than the title captured before filters changed.
function resetPageTitle() {
    pageDocument.title = currentLeaderboardDocumentTitle;
}
function addClickListenerToImages(selector, callback, accessibleLabel) {
    document.querySelectorAll(selector).forEach(img => {
        if (!img.dataset.listenerAdded) {
            if (img.closest('a[href]')) return;
            if (typeof accessibleLabel === 'function') {
                img.setAttribute('role', 'button');
                img.setAttribute('tabindex', '0');
                img.setAttribute('aria-label', accessibleLabel(img));
                img.addEventListener('keydown', event => {
                    if (event.key !== 'Enter' && event.key !== ' ') return;
                    event.preventDefault();
                    callback.call(img, event);
                });
            }
            img.addEventListener('click', callback);
            img.dataset.listenerAdded = true; // Mark this element as having a listener added
        }
    });
}

function updateAthleteInfoGrouping() {
    const groupedRowIds = [
        'crowdAgeContainer',
        'yourGuessContainer',
        'lowestBortzAgeContainer',
        'lowestPhenoAgeContainer',
        'bortzPaceOfAgingContainer',
        'paceOfAgingContainer',
        'bortzAgeReductionContainer',
        'ageReductionContainer'
    ];
    const groupedRows = groupedRowIds
        .map(id => document.getElementById(id))
        .filter(Boolean);

    groupedRows.forEach(row => row.classList.remove('athlete-info-group-first'));
    const firstVisibleRow = groupedRows.find(row => window.getComputedStyle(row).display !== 'none');
    if (firstVisibleRow) {
        firstVisibleRow.classList.add('athlete-info-group-first');
    }
}

function updateAthleteCrowdAge(athleteSlug, crowdAge, crowdCount) {
    const normalizedSlug = normalizeAthleteSlugForLookup(athleteSlug);
    const athleteData = findAthleteDataBySlug(normalizedSlug);
    const age = Number(crowdAge);
    const count = Number(crowdCount);
    if (athleteData) {
        athleteData.crowdAge = Number.isFinite(age) ? age : 0;
        athleteData.crowdCount = Number.isFinite(count) ? count : 0;
        athleteData.crowdAgeReduction = Number.isFinite(age)
            ? age - athleteData.chronologicalAge
            : null;
        assignBestRankCandidates(athleteResults);
    }

    const modalContent = document.querySelector('#detailsModal .modal-content');
    if (normalizeAthleteSlugForLookup(modalContent?.dataset.athleteSlug) !== normalizedSlug) return;
    updateCrowdContainer(age, count);
    if (athleteData) {
        const rankSummary = computeBestLeagueRank(athleteData);
        renderProfileRankings(athleteData, rankSummary.ultimateRank);
        document.getElementById('athleteRank').innerHTML = renderBestRankLink(rankSummary.bestCandidate);
    }
}

function updateCrowdContainer(crowdAge, crowdCount) {
    const crowdContainer = document.getElementById('crowdAgeContainer');
    const notEmptyCrowdContainer = document.getElementById('notEmptyCrowdAgeContainer');
    const hasCrowdAge = Number.isFinite(crowdAge) && Number.isFinite(crowdCount) && crowdCount > 0;

    if (!hasCrowdAge) {
        crowdContainer.style.display = 'none';
        notEmptyCrowdContainer.style.display = 'none';
    } else {
        crowdContainer.style.display = '';
        notEmptyCrowdContainer.style.display = 'block';
        document.getElementById('crowdAge').textContent = crowdAge.toFixed(0);
        document.getElementById('crowdCount').textContent = crowdCount.toFixed(0);
    }
}

function updateYourGuess() {
    const yourGuessContainer = document.getElementById('yourGuessContainer');
    const yourGuessSpan = document.getElementById('yourGuess');
    const modalContent = document.querySelector('#detailsModal .modal-content');
    const guessData = window.LwcGuessState?.get({
        AthleteSlug: modalContent?.dataset.athleteSlug,
        ProfileImageId: modalContent?.dataset.profileImageId
    });

    if (guessData && guessData.value != null) {
        // show the row (use '' so table layout is preserved; mobile CSS overrides tr to block)
        yourGuessContainer.style.display = '';

        // convert guess to a number
        const guess = Number(guessData.value);

        // grab the displayed chrono & crowd ages
        const chrono = parseInt(document.getElementById('chronologicalAge').textContent, 10);
        const crowd = parseFloat(document.getElementById('crowdAge').textContent);

        // compute how far you and the crowd are from the true age
        const diffYou = Math.abs(guess - chrono);
        const diffCrowd = Math.abs(crowd - chrono);

        // collect any matching icons
        const iconClasses = [];
        if (diffYou === 0) iconClasses.push('fa-bullseye');
        if (diffYou < diffCrowd) iconClasses.push('fa-trophy');
        if (guessData.first) iconClasses.push('fa-medal');

        // build and render
        const iconsHTML = iconClasses.map(c => `<i class="fa ${c}"></i>`).join(' ');
        yourGuessSpan.innerHTML = `${iconsHTML} ${guess} years${guessData.first ? ' (first to guess)' : ''}`;

        // apply stamp to the value span only so the row stays a normal table row
        yourGuessSpan.classList.remove('first-guess-stamp', 'primary-stamp', 'golden-stamp');
        if (diffYou === 0) {
            yourGuessSpan.classList.add('golden-stamp');
        } else if (diffYou < diffCrowd) {
            yourGuessSpan.classList.add('primary-stamp');
        } else if (guessData.first) {
            yourGuessSpan.classList.add('first-guess-stamp');
        }

    } else {
        // hide if no guess
        yourGuessContainer.style.display = 'none';
    }
    updateAthleteInfoGrouping();
}

// Keep the small public surface used by page bootstraps, Guess My Age, and
// existing browser tests while the controller itself remains isolated.
window.LoadLeaderboard = LoadLeaderboard;
window.openEnlargedView = openEnlargedView;
window.closeEnlargedView = closeEnlargedView;
window.populateProofsGallery = populateProofsGallery;
window.closeAthleteShareMenu = closeAthleteShareMenu;
window.closeModal = closeModal;
window.updateAthleteCrowdAge = updateAthleteCrowdAge;
window.updateCrowdContainer = updateCrowdContainer;
window.updateYourGuess = updateYourGuess;
window.refreshAthleteAfterStaleGuess = refreshAthleteAfterStaleGuess;
})();
