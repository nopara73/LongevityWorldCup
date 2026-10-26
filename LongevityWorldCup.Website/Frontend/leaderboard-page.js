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
let leaderboardOrderingSnapshot = null;
let athletesOrderedByPace;
let athletesOrderedByBortzPace;
const isAthleteDialogOnlyRuntime = athleteDialogRuntime.dataset.athleteDialogOnly === 'true';
let athleteResultsReady = false;
let athleteResultsLoadPromise = null;
let renderedLeaderboardSelection = '';
let renderedStandingsComparable = false;
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
    const isDefaultArtwork = source => toAbsoluteUrl(source).split('?')[0] === toAbsoluteUrl(defaultProfilePic).split('?')[0];
    img.dataset.lwcImageRole = isDefaultArtwork(currentSrc) ? 'brand' : 'athlete';
    const currentIndex = candidates.findIndex(candidate => toAbsoluteUrl(candidate) === currentSrc);
    let nextCandidateIndex = currentIndex >= 0 ? currentIndex + 1 : 0;

    img.onerror = function () {
        const nextCandidate = candidates[nextCandidateIndex++];
        if (nextCandidate) {
            this.dataset.lwcImageRole = isDefaultArtwork(nextCandidate) ? 'brand' : 'athlete';
            this.src = nextCandidate;
            return;
        }

        this.onerror = null;
        this.dataset.lwcImageRole = 'brand';
        this.src = defaultProfilePic;
        this.classList.add('portrait-fallback');
    };
}

function initializeAthletePortraitMotion() {
    const podium = document.querySelector('.podium');
    const leaderboard = document.querySelector('.leaderboard table tbody');
    const profile = document.getElementById('modalProfilePic');
    const profileModal = document.getElementById('detailsModal');
    const profileContent = profileModal?.querySelector('.modal-content');

    // Each clip belongs to one exact photograph, not to future profile updates.
    const approvedMotions = [
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/benjamin_garden(?:_thumb_(?:sm|md))?_6683dd38d8317bb5a68b3053ca8dae7c53aad86cd1c6df1c1662e16fcbe83345\.webp$/,
            clip: '/assets/athlete-motion/benjamin-garden-wink-f9f888014d01'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/valerie_orsoni(?:_thumb_(?:sm|md))?_e280d5d1d7278450a5d37ba7e16eee54719d0acb125f54c18b0b9a9a43c41976\.webp$/,
            clip: '/assets/athlete-motion/valerie-orsoni-double-blink-ac418cfd20b2'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/anna_buky(?:_thumb_(?:sm|md))?_f36cf66df3a3f67ef88c000c32c0ae4093a7c174aaae81bbb2c0691c178fc1ec\.webp$/,
            clip: '/assets/athlete-motion/anna-buky-brow-a23ae4dbcd12'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/martin_helstab(?:_thumb_(?:sm|md))?_03fd37f6337cd4f2d8ecdfa1b824bb970ed8c83be1eeaf29db078d25e02a7966\.webp$/,
            clip: '/assets/athlete-motion/martin-helstab-breath-14f6a65f37ae'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/wen_z(?:_thumb_(?:sm|md))?_9d0f884164da2166825661b263042d63bbebda217511f97a72eec8887853590d\.webp$/,
            clip: '/assets/athlete-motion/wen-z-hair-bfadd735074b'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/tibor_konkoly(?:_thumb_(?:sm|md))?_d563d8ade182b2286dbbeb6519e54f88e48ad04c40a3111649c4b598b605e501\.webp$/,
            clip: '/assets/athlete-motion/tibor-konkoly-water-long-6c4b157156e0'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/charlotte_wong(?:_thumb_(?:sm|md))?_708c527def914308e9da42eaac2cde572770f4fe5c06fbb162b1c250c34b3391\.webp$/,
            clip: '/assets/athlete-motion/charlotte-wong-flex-eb91ac025e86'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/slamovits_tibor(?:_thumb_(?:sm|md))?_f1ed3c0f28481d62d0df3f463a5077dacad31c9927894f9fe6ef8dbc87d85b78\.webp$/,
            clip: '/assets/athlete-motion/slamovits-tibor-finger-tap-3f339e3278ca'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/qolars(?:_thumb_(?:sm|md))?_da84f1750d8a7b55779f34832c9990de28d12638ad0a2c6fa6102b33061e87f7\.webp$/,
            clip: '/assets/athlete-motion/qolars-lamp-glow-c5a88f188dd6'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/markus_mattiasson(?:_thumb_(?:sm|md))?_405a0d4169a2aab78e2a7a0fd4569cc92633f0b202a0f37a7abb2ef431662593\.webp$/,
            clip: '/assets/athlete-motion/markus-mattiasson-towel-sway-f3cafa2c6744'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/inka_land(?:_thumb_(?:sm|md))?_89251966a304b5b721333bd3a3c02d456f76a230e6549bdbb34c210a60814cca\.webp$/,
            clip: '/assets/athlete-motion/inka-land-earring-swing-bd59700f80ce'
        },
        {
            portrait: /^\/generated\/(?:thumbs|profiles)\/athletes\/tamas_toth(?:_thumb_(?:sm|md))?_613ab3be683feb657f8b3e3a6db17ecac97211f90f3d18b16f91bf50735c8781\.webp$/,
            clip: '/assets/athlete-motion/tamas-toth-racket-settle-4aad19063da3'
        }
    ];
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const wired = new WeakSet();
    const active = new Map();
    const podiumPlayed = new Set();
    let profilePlayed = null;

    function motionFor(img) {
        try {
            const url = new URL(img.src, window.location.href);
            return url.origin === window.location.origin
                ? approvedMotions.find(motion => motion.portrait.test(url.pathname))
                : null;
        } catch (_) {
            return null;
        }
    }

    function play(img) {
        const motion = motionFor(img);
        if (active.has(img) || reducedMotion.matches || !img.isConnected || !motion) return false;
        const wrapper = img.parentElement;
        if (!wrapper || !img.naturalWidth || !img.checkVisibility()) return false;

        if (podium?.contains(img)) {
            podiumPlayed.add(motion);
            visiblePortraits?.unobserve(img);
        }
        if (img === profile) profilePlayed = motion;

        const video = document.createElement('video');
        video.className = 'athlete-portrait-motion';
        video.setAttribute('aria-hidden', 'true');
        video.tabIndex = -1;
        video.muted = true;
        video.defaultMuted = true;
        video.playsInline = true;
        video.disablePictureInPicture = true;
        video.preload = 'auto';
        // Table portraits can sit inside a rank-change wrapper and have different
        // margins at each breakpoint. Follow the image's untransformed content box.
        const sizeOverlay = () => {
            video.style.left = `${img.offsetLeft + img.clientLeft}px`;
            video.style.top = `${img.offsetTop + img.clientTop}px`;
            video.style.width = `${img.clientWidth}px`;
            video.style.height = `${img.clientHeight}px`;
        };
        const rowResize = leaderboard?.contains(img) ? new ResizeObserver(sizeOverlay) : null;
        const stop = () => {
            if (active.get(img)?.stop !== stop) return;
            active.delete(img);
            rowResize?.disconnect();
            video.pause();
            video.remove();
            wrapper.classList.remove('portrait-motion');
        };
        active.set(img, { motion, stop });
        video.addEventListener('playing', () => video.classList.add('is-playing'), { once: true });
        video.addEventListener('ended', stop, { once: true });
        video.addEventListener('error', stop, { once: true });
        for (const [extension, type] of [['webm', 'video/webm'], ['mp4', 'video/mp4']]) {
            const source = document.createElement('source');
            source.src = `${motion.clip}.${extension}`;
            source.type = type;
            // Source failures do not bubble to the video element. The final
            // fallback failing must also remove the overlay.
            source.addEventListener('error', () => {
                if (source === video.lastElementChild) stop();
            }, { once: true });
            video.appendChild(source);
        }
        wrapper.classList.add('portrait-motion');
        img.after(video);
        if (rowResize) {
            sizeOverlay();
            rowResize.observe(img);
        }
        video.play().catch(stop);
        return true;
    }

    function wire(img) {
        if (wired.has(img) || !motionFor(img)) return;
        wired.add(img);
        let pointerInside = false;
        const link = img.closest('a');
        img.addEventListener('pointerenter', event => {
            pointerInside = event.pointerType !== 'touch';
            if (pointerInside) play(img);
        });
        img.addEventListener('pointerleave', () => { pointerInside = false; });
        img.addEventListener('focus', () => play(img));
        link?.addEventListener('focus', () => play(img));
        // Lazy table photos may finish loading after the first pointer entry.
        // Replay then only if that interaction is still active.
        img.addEventListener('load', () => {
            if (pointerInside || document.activeElement === img || (link && document.activeElement === link)) play(img);
        });
    }

    const visiblePortraits = window.IntersectionObserver && new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting || entry.intersectionRatio < .5) continue;
            visiblePortraits.unobserve(entry.target);
            const img = entry.target;
            const start = () => {
                const motion = motionFor(img);
                if (img === profile) {
                    if (!profileReady(motion) || profilePlayed === motion) return;
                } else if (podiumPlayed.has(motion)) return;
                play(img);
            };
            if (img.complete) start();
            else img.addEventListener('load', start, { once: true });
        }
    }, { threshold: .5 });

    function profileReady(motion) {
        return profileModal.style.display === 'block'
            && !profileModal.classList.contains('fade-out')
            && !profileContent.inert
            && !profileContent.classList.contains('is-loading')
            && motion;
    }

    function scan(records) {
        for (const [img, playback] of active) {
            if (!img.isConnected || motionFor(img) !== playback.motion) playback.stop();
        }
        // Paging changes the profile, not the hundreds of portraits behind it.
        // Only a board mutation (or the initial scan) needs to recheck those photos.
        if (!records || records.some(record => !profileModal?.contains(record.target))) {
            // Server-rendered rows also work while hydration is pending or fails.
            leaderboard?.querySelectorAll('img.portrait').forEach(wire);
            // Hydration replaces podium cards; only their final portraits autoplay.
            if (athleteResultsReady) {
                podium?.querySelectorAll('img.podium-portrait').forEach(img => {
                    wire(img);
                    const motion = motionFor(img);
                    if (motion && !podiumPlayed.has(motion) && !reducedMotion.matches) visiblePortraits?.observe(img);
                });
            }
        }

        if (!profile) return;
        const profileMotion = motionFor(profile);
        if (!profileReady(profileMotion)) {
            visiblePortraits?.unobserve(profile);
            profilePlayed = null;
            active.get(profile)?.stop();
            return;
        }
        wire(profile);
        // The portrait is already visible. Start its decorative clip from the
        // browser's visibility report instead of forcing the rebuilt card to
        // resolve layout synchronously inside every pager click.
        if (profilePlayed !== profileMotion) visiblePortraits?.observe(profile);
    }

    const updates = new MutationObserver(scan);
    if (podium) updates.observe(podium, { childList: true, subtree: true, attributes: true, attributeFilter: ['src'] });
    if (leaderboard) updates.observe(leaderboard, { childList: true, subtree: true, attributes: true, attributeFilter: ['src'] });
    if (profile) {
        updates.observe(profile, { attributes: true, attributeFilter: ['src'] });
        updates.observe(profileModal, { attributes: true, attributeFilter: ['class', 'style'] });
        updates.observe(profileContent, { attributes: true, attributeFilter: ['class', 'inert'] });
        profile.addEventListener('load', () => scan([]));
    }
    reducedMotion.addEventListener('change', () => {
        if (reducedMotion.matches) for (const playback of active.values()) playback.stop();
    });
    scan();
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

function removeDefaultLeaderboardParameters(url) {
    const route = getLeagueRouteState(url.pathname);
    const view = url.searchParams.get('view');
    if (view === '' || view?.toLowerCase() === (route.view || 'ultimate')) {
        url.searchParams.delete('view');
    }
    const search = url.searchParams.get('search');
    if (search !== null && search.trim() === '') url.searchParams.delete('search');
    const group = url.searchParams.get('group');
    if (group !== null && group.toLowerCase() !== 'flags') url.searchParams.delete('group');

    const filtersParam = url.searchParams.get('filters');
    if (filtersParam === null) return;
    const filters = parseFiltersParam(filtersParam);
    if (filters.length === 0) {
        url.searchParams.delete('filters');
    } else if (!route.filterLabel && !getFlagRouteState(url.pathname).flagKey) {
        // Selecting both tracks means all athletes. Keep any other selected filters.
        const tracks = new Set(filters.map(filter => filter.toLowerCase()));
        if (tracks.has('amateur') && tracks.has('professional')) {
            const selectedFilters = filters.filter(filter => !['amateur', 'professional'].includes(filter.toLowerCase()));
            if (selectedFilters.length === 0) url.searchParams.delete('filters');
            else url.searchParams.set('filters', serializeFiltersParam(selectedFilters));
        }
    }
}

function normalizeLeaderboardUrl() {
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
    }
    removeDefaultLeaderboardParameters(url);
    if (url.href !== window.location.href) history.replaceState(history.state, '', url.href);
}


if (!isAthleteDialogOnlyRuntime) {
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
let athleteDialogBoardSequence = [];
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

function LoadLeaderboard(includePodium = true, maxAthletes = Infinity, refresh = false) {
    window.initializeSharedPageData();
    includePodiumGlobal = includePodium;
    maxAthletesGlobal = maxAthletes;
    athleteResultsReady = false;
    if (!refresh) setLeaderboardLoadingState(includePodium, maxAthletes);
    const fullBoard = pageDocument.querySelector('main[data-leaderboard-page="full"]');
    if (fullBoard && !fullBoard.hasAttribute('data-lwc-refresh-ready')) {
        fullBoard.setAttribute('data-lwc-refresh-ready', '');
        fullBoard.addEventListener('lwc:pull-refresh', event => {
            // Resolve fresh public data before touching the visible selection.
            event.detail.waitUntil(window.getSharedAthletes(true).then(() =>
                LoadLeaderboard(includePodiumGlobal, maxAthletesGlobal, true)).catch(() => {
                    setLeaderboardStatus('Leaderboard could not load.', true);
                }));
        });
        window.LwcTouch?.refresh();
    }

    const currentLoadPromise = window.getSharedAthletes()
        .then(athletes => {
            // A pull refresh can finish before the initial request. Only the
            // latest load owns the visible board, including failure handling.
            if (athleteResultsLoadPromise !== currentLoadPromise) return;
            const selectedFilters = refresh ? new Set(Array.from(document.querySelectorAll('.sidebar input:checked'))
                .map(input => `${input.name}\n${input.value}`)) : null;
            const refreshFocus = refresh ? document.activeElement : null;
            const refreshScroll = refresh ? window.scrollY : null;
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

                    const ageAtFirst = window.calculateAgeAtDate(dob, getBiomarkerTestDate(firstSubmission.Date));
                    const ageAtLast  = window.calculateAgeAtDate(dob, getBiomarkerTestDate(lastSubmission.Date));

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
                        const submittedAt = getBiomarkerTestDate(submission.Date);
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
                    const entryDate = getBiomarkerTestDate(entry.Date);
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
                        const ageAtLast = window.calculateAgeAtDate(dob, getBiomarkerTestDate(last.Date));
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
                        const entryDate = getBiomarkerTestDate(entry.Date);
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

                const result = {
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
                    hasPhenoRankData: Number.isFinite(lowestPhenoAge) && valuesForLowestPhenoAge != null,
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
                    placements: athlete.Placements || [],
                    rankImproved: rankImproved,
                    isNew: !!athlete.IsNew,
                    Badges: athlete.Badges || [],
                };
                result.orderValues = window.LwcLeaderboardOrder.values(athlete, result);
                return result;
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
                if (selectedFilters) {
                    document.querySelectorAll('.sidebar input').forEach(input => {
                        input.checked = selectedFilters.has(`${input.name}\n${input.value}`);
                    });
                }
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
                        <a class="athlete-profile-link" href="${escapeHtml(getAthleteProfileHref(athlete))}" aria-label="View stats of ${escapeHtml(athlete.displayName)}"><img src="${podiumPortraitUrl}" alt="${athlete.displayName} portrait" class="podium-portrait" loading="eager" decoding="async"${athlete.rank === 1 ? ' fetchpriority="high"' : ''}></a>
                        <div class="name-row">
                            <a class="athlete-name" href="${escapeHtml(getAthleteProfileHref(athlete))}" title="View stats of ${escapeHtml(athlete.displayName)}">${escapeHtml(athlete.displayName)}</a>
                        </div>
                        <div class="podium-link-row">
                            ${renderPersonalLink(athlete.personalLink, athlete.displayName)}
                            ${renderPodcastLink(athlete.podcastLink, athlete.displayName)}
                            ${renderMediaContact(athlete.mediaContact, true, athlete.displayName)}
                        </div>
                        <div class="podium-score"><span class="age-reduction">${Math.abs(getDisplayAgeReductionValue(athlete)).toFixed(athlete.ageReductionDisplayDecimals || 1)} years</span> reduced</div>
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

                setDisplayedRank(row.querySelector('.rank'), athlete.rank);
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
                athleteNameSpan.innerHTML = `<span class="athlete-name-content">${escapeHtml(athlete.displayName)}</span>`;
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
            if (refresh) {
                // Current inputs remain authoritative during a refresh.
            } else if (athleteParam) {
                // A profile selected while the index was loading takes priority
                // over the containing page's initial URL (including close/back).
                if (currentAthleteModalRequestId === 0) {
                    const normalizedAthlete = window.slugifyName(athleteParam, false);
                    const athleteData = getAthleteData(normalizedAthlete);
                    if (athleteData) {
                        fetchFullAthleteData(normalizedAthlete, athleteData, { historyMode: 'replace' });
                    } else {
                        // Redirect to the .NET 404 endpoint
                        window.location.href = '/error/404';
                    }
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

                document.getElementById('group-flags').checked = new URLSearchParams(window.location.search).get('group')?.toLowerCase() === 'flags';
                syncFlagFilters(document.getElementById('group-flags').checked);
                const rawViewParam = new URLSearchParams(window.location.search).get('view') || leagueRouteState.view;
                const viewParam = rawViewParam && rawViewParam.toLowerCase() === 'pheno-improvement'
                    ? 'improvement'
                    : rawViewParam;
                if (viewParam && ['bortz', 'pheno', 'improvement', 'bortz-improvement', 'crowd'].includes(viewParam.toLowerCase())) {
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

                    // Position a new incoming selection at its controls without
                    // overwriting the reader's native Back/Forward restoration.
                    const incomingScrollY = window.scrollY;
                    setTimeout(() => {
                        const filtersPanel = document.querySelector('.sidebar');
                        const returning = performance.getEntriesByType('navigation')[0]?.type === 'back_forward';
                        if (!returning && canAutoScroll() && window.scrollY === incomingScrollY &&
                            !filtersPanel?.matches(':hover, .expanded') && document.activeElement !== searchInput) {
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
                        ScrollGuard.trustedScrollIntoView(target, { behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth', block: 'start' });
                        ScrollGuard.block(1600);
                    }
                }
            }
            setLeaderboardStatus('Leaderboard loaded.');
            if (refreshFocus && !refreshFocus.isConnected) {
                const replacement = Array.from(document.querySelectorAll('.sidebar input, .leaderboard a')).find(element =>
                    refreshFocus instanceof HTMLInputElement
                        ? element.name === refreshFocus.name && element.value === refreshFocus.value
                        : refreshFocus instanceof HTMLAnchorElement && element.getAttribute('href') === refreshFocus.getAttribute('href'));
                replacement?.focus({ preventScroll: true });
            }
            if (refreshScroll !== null) window.scrollTo({ top: refreshScroll, behavior: 'instant' });

            // Pick up dynamically inserted [data-aos] nodes
            if (window.AOS && typeof AOS.refreshHard === 'function') {
                AOS.refreshHard();
            } else if (window.AOS && typeof AOS.refresh === 'function') {
                AOS.refresh();
            }
        })
        .catch(error => {
            if (athleteResultsLoadPromise !== currentLoadPromise) return;
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
            const serverDialog = getServerRenderedAthleteDialog();
            if (serverDialog && modal.style.display === 'block') {
                handleAthleteModalLoadFailure(serverDialog, serverDialog.dataset.athleteSlug, { historyMode: 'replace' }, error);
            }
            console.error('Error fetching athletes:', error);
        });
    athleteResultsLoadPromise = currentLoadPromise;
    return currentLoadPromise;
};

function buildPodiumSkeletonHtml() {
    return ['second', 'first', 'third'].map(rank => `
        <div class="podium-item podium-skeleton-item ${rank}" aria-hidden="true">
            <div class="podium-skeleton-portrait skeleton-shimmer"></div>
            <div class="name-row">
                <span class="athlete-name skeleton-shimmer">&nbsp;</span>
            </div>
            <div class="podium-link-row">
                <span class="podium-skeleton-link skeleton-shimmer"></span>
                <span class="podium-skeleton-link skeleton-shimmer"></span>
                <span class="podium-skeleton-link skeleton-shimmer"></span>
            </div>
            <div class="podium-score">
                <span class="age-reduction skeleton-shimmer">&nbsp;</span>
                <span class="podium-skeleton-caption skeleton-shimmer">&nbsp;</span>
            </div>
            <div class="podium-item-lower podium-skeleton-prize">
                <div class="btc-amount">&nbsp;</div>
                <div class="prize-money">&nbsp;</div>
            </div>
        </div>
    `).join('');
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
    const flags = document.getElementById('flagLeaderboard');
    if (getSelectedLeaderboardState().byFlag && flags) {
        flags.querySelector('.leaderboard-recovery')?.remove();
        flags.querySelector('.flag-empty')?.remove();
        flags.appendChild(recovery);
        return;
    }
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
            if (podium.dataset.serverRendered !== 'true') {
                const params = new URLSearchParams(window.location.search);
                const hasIncomingSelection = !!(params.get('search')?.trim() || params.get('filters')?.trim() || params.get('sort')?.trim())
                    || params.get('group')?.toLowerCase() === 'flags'
                    || ['bortz', 'pheno', 'improvement', 'pheno-improvement', 'bortz-improvement', 'crowd'].includes(params.get('view')?.toLowerCase());
                podium.style.display = hasIncomingSelection ? 'none' : 'flex';
                podium.innerHTML = buildPodiumSkeletonHtml();
            }
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

// Usage
addClickListenerToImages('#modalProfilePic', function () {
    openEnlargedView(this);
}, img => `Enlarge ${img.alt || 'athlete profile picture'}`);
addClickListenerToImages('.portrait, .podium-portrait', handleAthleteNameClick);

initializeAthletePortraitMotion();

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
    enlargedElem.dataset.proofZoom = String(zoom);
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
    const pendingImage = enlargedElem._pendingImage;
    enlargedElem._pendingImage = null;
    if (pendingImage) {
        pendingImage.onload = null;
        pendingImage.onerror = null;
        pendingImage.removeAttribute('src');
    }
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
    image.fetchPriority = 'high';
    // A portrait is already useful at thumbnail resolution. Keep it visible
    // while its full-size version downloads, including if that upgrade fails.
    // Proofs still require the selected page to load before it can be read.
    const preview = !enlargedElem.isProofGallery && !sourceImage.classList.contains('portrait-fallback')
        && !sourceImage.hasAttribute('data-lwc-portrait-fallback')
        && sourceImage.complete && sourceImage.naturalWidth > 0
        ? document.createElement('img')
        : null;
    if (preview) {
        preview.alt = sourceImage.alt;
        preview.src = sourceImage.currentSrc || sourceImage.src;
    }
    enlargedElem.querySelector('.image-viewer-stage img').replaceWith(preview || image);
    enlargedElem._pendingImage = image;
    setEnlargedImageLoadState(enlargedElem, preview ? 'ready' : 'loading');
    const finish = state => {
        if (enlargedElem._pendingImage !== image) return;
        enlargedElem._pendingImage = null;
        clearTimeout(enlargedElem._imageLoadTimer);
        enlargedElem._imageLoadTimer = null;
        image.onload = null;
        image.onerror = null;
        if (preview && state === 'ready') preview.replaceWith(image);
        if (!preview || state === 'ready') setEnlargedImageLoadState(enlargedElem, state);
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
    viewer.inert = false;
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
    enlargedElem.inert = true;

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
let athleteDialogSnapshotRequest = null;
let athleteDialogSnapshot = null;
let athleteShareState = null;
let athleteShareLayoutCleanup = null;
const chartJsSrc = 'https://cdn.jsdelivr.net/npm/chart.js@4.6.1';
const athleteModalIsolation = new Map();
const athleteModalFrames = new WeakSet();
const athleteModalIsolationObserver = new MutationObserver(isolateAthleteModalBackground);
let athleteModalFocusRevealFrame = 0;
let athleteModalEntryFocusFrame = 0;
const athleteModalFrameResizeObserver = typeof ResizeObserver === 'function' ? new ResizeObserver(entries => {
    entries.forEach(entry => revealAthleteModalFrameFocus(entry.target));
}) : null;

function revealAthleteModalFrameFocus(frame) {
    if (pageDocument.activeElement !== frame) return;
    cancelAnimationFrame(athleteModalFocusRevealFrame);
    athleteModalFocusRevealFrame = requestAnimationFrame(() => {
        athleteModalFocusRevealFrame = requestAnimationFrame(() => {
            athleteModalFocusRevealFrame = 0;
            if (!frame.isConnected || modal.style.display !== 'block' || pageDocument.activeElement !== frame) return;
            const focused = frame.contentDocument?.activeElement;
            if (!focused || focused === frame.contentDocument.body) return;
            focused.scrollIntoView({ block: 'nearest', inline: 'nearest', behavior: 'instant' });
            const scroller = frame.closest('.modal-content');
            if (!scroller) return;
            const viewport = scroller.getBoundingClientRect();
            const frameRect = frame.getBoundingClientRect();
            const target = focused.getBoundingClientRect();
            const header = scroller.querySelector('.modal-sticky-header.visible');
            const top = Math.max(0, viewport.top) + (header ? header.getBoundingClientRect().height : 0) + 8;
            const bottom = Math.min(innerHeight, viewport.bottom) - 8;
            const delta = frameRect.top + target.bottom > bottom ? frameRect.top + target.bottom - bottom
                : frameRect.top + target.top < top ? frameRect.top + target.top - top : 0;
            if (delta) scroller.scrollTo({ top: scroller.scrollTop + delta, behavior: 'instant' });
        });
    });
}

function isolateAthleteModalBackground() {
    // The dialog is embedded in page content. Isolate sibling branches,
    // never an ancestor that would also disable the dialog.
    for (let branch = modal; branch?.parentElement; branch = branch.parentElement) {
        if (branch === document.body) break;
        for (const sibling of branch.parentElement.children) {
            if (sibling === branch || !(sibling instanceof HTMLElement)) continue;
            if (!athleteModalIsolation.has(sibling)) athleteModalIsolation.set(sibling, sibling.inert);
            if (!sibling.inert) sibling.inert = true;
        }
    }
}

function activateAthleteModal() {
    isolateAthleteModalBackground();
    for (let ancestor = modal.parentElement; ancestor; ancestor = ancestor.parentElement) {
        athleteModalIsolationObserver.observe(ancestor, { childList: true });
        if (ancestor === document.body) break;
    }
    modal.querySelectorAll('iframe').forEach(bindAthleteModalFrame);
    if (!modal.contains(document.activeElement)) {
        cancelAnimationFrame(athleteModalEntryFocusFrame);
        athleteModalEntryFocusFrame = requestAnimationFrame(() => {
            athleteModalEntryFocusFrame = 0;
            if (modal.style.display === 'block' && !modal.classList.contains('fade-out')
                && !modal.contains(document.activeElement)) closeBtn.focus({ preventScroll: true });
        });
    }
}

function rememberAthleteModalOpener(options) {
    const candidate = options?.returnFocusTo instanceof HTMLElement ? options.returnFocusTo : pageDocument.activeElement;
    if (candidate instanceof HTMLElement && candidate !== pageDocument.body
        && !modal.contains(candidate) && !candidate.closest('[hidden], [inert]')) {
        athleteDialogReturnFocusElement = candidate;
    }
}

function releaseAthleteModal() {
    cancelAnimationFrame(athleteModalEntryFocusFrame);
    athleteModalEntryFocusFrame = 0;
    athleteModalIsolationObserver.disconnect();
    athleteModalFrameResizeObserver?.disconnect();
    cancelAnimationFrame(athleteModalFocusRevealFrame);
    athleteModalFocusRevealFrame = 0;
    athleteModalIsolation.forEach((wasInert, element) => { element.inert = wasInert; });
    athleteModalIsolation.clear();
}

function getAthleteModalFocusables(root = modal) {
    return Array.from(root.querySelectorAll('a[href], button, input, select, textarea, iframe, [tabindex]'))
        .filter(element => element.tabIndex >= 0 && !element.disabled && !element.closest('[inert], [hidden], [aria-hidden="true"]')
            && element.getClientRects().length && getComputedStyle(element).visibility === 'visible')
        .flatMap(element => {
            if (element.tagName !== 'IFRAME') return [element];
            try {
                const controls = element.contentDocument && getAthleteModalFocusables(element.contentDocument);
                // An empty same-origin frame has no keyboard destination.
                return controls ?? [element];
            } catch { return [element]; }
        });
}

function containAthleteModalFocus(event) {
    if (event.key !== 'Tab' || event.defaultPrevented || modal.style.display !== 'block') return;
    if (modal.querySelector('#athleteImageViewer[aria-hidden="false"]')) return;
    // The athlete share sheet lives inside this dialog, independently of the
    // site's shared sheet. Keep its keyboard loop above the profile controls.
    const focusRoot = modal.querySelector('#athleteShareMenu:not([hidden])') || modal;
    const controls = getAthleteModalFocusables(focusRoot);
    const active = event.target.ownerDocument.activeElement;
    const first = controls[0];
    const last = controls[controls.length - 1];
    if (!first) {
        event.preventDefault();
        modal.focus({ preventScroll: true });
    } else if (!controls.includes(active) || (event.shiftKey ? active === first : active === last)) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
    }
}

function bindAthleteModalFrame(frame) {
    athleteModalFrameResizeObserver?.observe(frame);
    try {
        const frameDocument = frame.contentDocument;
        if (!frameDocument || athleteModalFrames.has(frameDocument)) return;
        athleteModalFrames.add(frameDocument);
        frameDocument.addEventListener('keydown', containAthleteModalFocus);
        frameDocument.addEventListener('focusin', () => revealAthleteModalFrameFocus(frame));
    } catch { /* External frames retain their native keyboard handling. */ }
}
modal.addEventListener('load', event => {
    if (!(event.target instanceof HTMLIFrameElement)) return;
    bindAthleteModalFrame(event.target);
    // A navigating frame can discard its focused document. Keep entry focus
    // inside the open profile rather than leaving it on the page body.
    if (modal.style.display === 'block' && !modal.classList.contains('fade-out')
        && pageDocument.activeElement === pageDocument.body) closeBtn.focus({ preventScroll: true });
}, true);
document.addEventListener('keydown', containAthleteModalFocus);

// Direct profile/game links arrive with the requested dialog already open.
// Make closing/back navigation available while the data request is pending.
function getServerRenderedAthleteDialog() {
    return modal.querySelector('[data-server-rendered-profile], [data-server-rendered-loading]');
}

const initialServerDialog = getServerRenderedAthleteDialog();
if (initialServerDialog) {
    initialServerDialog.classList.add('is-loading');
    lockBodyScroll();
    activateAthleteModal();
    history.replaceState({ ...history.state, modal: 'details', athlete: initialServerDialog.dataset.athleteSlug }, '', window.location.href);
}

function adoptSharedCharts(loaded) {
    const ready = !window.LwcCharts && document.readyState === 'loading'
        ? new Promise(resolve => document.addEventListener('DOMContentLoaded', resolve, { once: true }))
        : Promise.resolve();
    return ready.then(() => {
        if (loaded) window.LwcCharts?.install(window.Chart);
        return loaded;
    });
}

window.ensureChartJs = window.ensureChartJs || function () {
    if (typeof window.Chart === 'function') {
        return adoptSharedCharts(true);
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
        return adoptSharedCharts(loaded);
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
    athleteModalFrameResizeObserver?.unobserve(eventsFrame);
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

function resetModalForLoading(athleteSlug, allowPaging = false) {
    const modalContent = document.querySelector('.modal-content');
    if (!modalContent) return null;
    // Reset the old scroll before changing content/styles; writing scrollTop
    // after those changes synchronously laid out the whole new profile.
    if (modalContent.scrollTop !== 0) modalContent.scrollTo({ top: 0, behavior: 'instant' });
    // Read the page's scrollbar/background before rebuilding the card. Doing
    // this after showing the loading state forced an intermediate full layout.
    lockBodyScroll();
    modal.dispatchEvent(new CustomEvent('athlete-dialog-loading', { detail: { slug: athleteSlug } }));
    ensureAthleteModalDeferredSections();
    resetAthleteShareState();
    if (!allowPaging) modal.querySelectorAll('.athlete-step').forEach(button => { button.disabled = true; });
    const viewer = document.querySelector('#athleteImageViewer[aria-hidden="false"]');
    if (viewer) closeEnlargedView(viewer);
    modal._historyClosePending = false;

    if (modal._closeTimer) {
        clearTimeout(modal._closeTimer);
        modal._closeTimer = 0;
    }
    modal.classList.remove('fade-out');

    modalContent.inert = false;

    if (modalContent.dataset.serverRenderedProfile === athleteSlug) {
        modalContent.classList.add('is-loading');
        modal.style.display = 'block';
        document.getElementById('shareAthleteProfile').disabled = true;
        const loadError = document.getElementById('athleteLoadError');
        if (loadError) loadError.hidden = true;
        activateAthleteModal();
        return modalContent;
    }
    delete modalContent.dataset.serverRenderedProfile;

    modalContent.classList.remove('has-load-error', 'guess-mode', 'gma-fast', 'gma-result-ready');
    modalContent.classList.add('is-loading');
    modalContent.dataset.athleteSlug = athleteSlug;
    delete modalContent.dataset.profileImageId;

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
    const stickyPortrait = document.getElementById('stickyAthletePortrait');
    if (stickyPortrait) {
        stickyPortrait.removeAttribute('src');
        stickyPortrait.hidden = true;
    }
    renderPortraitFlagMark('');

    const profilePic = document.getElementById('modalProfilePic');
    if (profilePic) {
        profilePic.removeAttribute('src');
        profilePic.removeAttribute('data-full-src');
        profilePic.alt = 'Loading athlete profile picture';
    }

    const modalBadgeStrip = document.getElementById('modalBadgeStrip');
    if (modalBadgeStrip) {
        setModalBadgeExplainer(modalBadgeStrip, null);
        modalBadgeStrip.replaceChildren();
    }
    const athleteBio = document.getElementById('athleteBio');
    if (athleteBio) {
        athleteBio.textContent = '';
        athleteBio.hidden = true;
        athleteBio.removeAttribute('data-leading-quote');
    }
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

    disposeBiomarkerHistory();
    const historySection = document.getElementById('athlete-biomarkers');
    if (historySection) {
        historySection._biomarkerHistory = null;
        historySection.querySelector('.biomarker-overview-table tbody')?.replaceChildren();
        const range = historySection.querySelector('.biomarker-overview-range');
        if (range) range.textContent = '';
    }
    if (typeof window.destroyAgeRadarChart === 'function') {
        window.destroyAgeRadarChart();
    }

    if (modal.style.display !== "block") {
        modal.style.display = "block";
    }
    activateAthleteModal();
    if (allowPaging) updateAthleteDialogSteps();

    return modalContent;
}

// Improved body scroll lock for iOS
function getLockedBodyWidth(previousWidth) {
    // Compensate only for scrollbar space actually released by the lock.
    // Reserving a root gutter also shrinks fixed overlays and action docks.
    const releasedWidth = Math.max(0, window.innerWidth - previousWidth);
    return `calc(100% - ${releasedWidth}px)`;
}

function lockBodyScroll() {
    if (document.body.classList.contains('no-scroll')) return;

    const bodyWidth = document.body.clientWidth;
    const bodyBackground = getComputedStyle(document.body).backgroundColor || getComputedStyle(document.documentElement).backgroundColor;
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
    document.body.style.width = getLockedBodyWidth(bodyWidth);
    savedBodyBackground = document.body.style.backgroundColor;
    document.body.style.backgroundColor = bodyBackground;
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
        if (event.defaultPrevented || modal.querySelector('#athleteImageViewer[aria-hidden="false"], #athleteShareMenu:not([hidden])')) return;
        if (event.target.closest('input, textarea, select, [contenteditable="true"]')) return;
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

        // Left and Right step to the neighbouring athlete, unless a control
        // inside the profile uses them itself.
        if ((event.key === 'ArrowLeft' || event.key === 'ArrowRight')
            && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey
            && !event.target.closest('[role="tablist"], [role="radiogroup"], [role="slider"], [role="listbox"], [role="menu"], [role="grid"], .age-clock-switch, canvas')) {
            if (stepAthleteDialog(event.key === 'ArrowLeft' ? 'previous' : 'next')) event.preventDefault();
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

// Neighbouring athletes follow the selected board order before its display
// limit, or the overall ranking for a profile outside that selection.
function getAthleteDialogBoardSequence() {
    if (isAthleteDialogOnlyRuntime) return [];
    if (getSelectedLeaderboardState().byFlag) {
        // The ordinary table is hidden in the flag view. Only the expanded,
        // filtered team links form the sequence the visitor is browsing.
        const bySlug = new Map(athleteResults.map(athlete => [normalizeAthleteSlugForLookup(athlete.athleteSlug || athlete.name), athlete]));
        return [...pageDocument.querySelectorAll('#flagLeaderboard .flag-athlete-link')]
            .filter(link => link.checkVisibility())
            .map(link => bySlug.get(getAthleteSlugFromProfileLink(link)))
            .filter(Boolean);
    }
    // Retain the selected order before the page's display limit, including
    // podium athletes whose duplicate rows are hidden on the homepage.
    return athleteDialogBoardSequence;
}

function getAthleteDialogNeighbours(slug = modal.querySelector('.modal-content')?.dataset.athleteSlug || '') {
    if (!slug) return { previous: null, next: null };
    const matches = athlete => normalizeAthleteSlugForLookup(athlete.athleteSlug || athlete.name) === slug;
    for (const sequence of [getAthleteDialogBoardSequence(), athleteResults]) {
        const index = sequence.findIndex(matches);
        if (index >= 0) return { previous: sequence[index - 1] || null, next: sequence[index + 1] || null };
    }
    return { previous: null, next: null };
}

function focusAvailableAthleteStep(neighbours) {
    const focused = pageDocument.activeElement;
    if (!focused?.matches('.athlete-step') || !modal.contains(focused) || neighbours[focused.dataset.step]) return;
    const replacement = focused.closest('.athlete-steps')?.querySelector(`.athlete-step:not([data-step="${focused.dataset.step}"])`);
    if (!replacement || !neighbours[replacement.dataset.step]) return;
    if (replacement.disabled) replacement.disabled = false;
    replacement.focus({ preventScroll: true });
}

function updateAthleteDialogSteps() {
    const neighbours = getAthleteDialogNeighbours();
    focusAvailableAthleteStep(neighbours);
    const available = !!(neighbours.previous || neighbours.next);
    modal.querySelectorAll('.athlete-steps').forEach(group => { group.hidden = !available; });
    modal.querySelectorAll('.athlete-step').forEach(button => {
        const blocked = !neighbours[button.dataset.step];
        button.disabled = blocked;
    });
}

const athleteNeighbourPortraits = new Map();
function preloadAthleteDialogNeighbours() {
    const sources = new Set(Object.values(getAthleteDialogNeighbours()).filter(Boolean).map(getAthletePortraitUrl));
    for (const source of athleteNeighbourPortraits.keys()) {
        if (!sources.has(source)) athleteNeighbourPortraits.delete(source);
    }
    for (const source of sources) {
        if (athleteNeighbourPortraits.has(source)) continue;
        const image = new Image();
        image.decoding = 'async';
        image.src = source;
        athleteNeighbourPortraits.set(source, image);
        // Optional preparation never gates paging or changes the current image.
        image.decode?.().catch(() => {});
    }
}

function stepAthleteDialog(direction) {
    const modalContent = modal.querySelector('.modal-content');
    if (!modalContent || modal.style.display !== 'block' || modal._historyClosePending
        || modalContent.matches('.guess-mode, .has-load-error, [data-server-rendered-profile], [data-server-rendered-loading]')
        || (modalContent.classList.contains('is-loading') && !modalContent.dataset.athleteStep)
        || modal.classList.contains('fade-out')
        || modal.querySelector('#athleteImageViewer[aria-hidden="false"], #athleteShareMenu:not([hidden])')) return false;
    const target = getAthleteDialogNeighbours()[direction];
    if (!target) return false;
    // Transfer boundary focus before hydration invalidates the card's layout.
    focusAvailableAthleteStep(getAthleteDialogNeighbours(normalizeAthleteSlugForLookup(target.athleteSlug || target.name)));
    modalContent.dataset.athleteStep = direction;
    fetchFullAthleteData(target.name, target, { suppressGuessMyAge: true, historyMode: 'step' });
    return true;
}

modal.querySelectorAll('.athlete-step').forEach(button => {
    button.addEventListener('click', () => stepAthleteDialog(button.dataset.step));
});

window.addEventListener('click', function (event) {
    const modalContent = document.querySelector('.modal-content');
    if (event.target === modal && modalContent && !modalContent.classList.contains('guess-mode')) {
        requestCloseAthleteModal();
    }
});

function requestCloseAthleteModal() {
    if (modal.classList.contains('fade-out')) return;
    if (modal.style.display !== 'block' || modal._historyClosePending) return;
    if (isGuessMyAgeDismissBlocked(modal.querySelector('.modal-content'))) return;
    modal.dispatchEvent(new Event('athlete-dialog-close-requested'));
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

function finishAfterTransition(element, property, complete) {
    const transition = element.getAnimations().find(animation => animation.transitionProperty === property);
    const remaining = transition
        ? Math.max(0, transition.effect.getComputedTiming().endTime - (transition.currentTime || 0))
        : 0;
    return window.setTimeout(complete, Math.ceil(remaining));
}

function closeModal(options) {
    if (!options?.fromHistory) return requestCloseAthleteModal();
    if (modal.classList.contains('fade-out')) return;
    const modalContent = document.querySelector('.modal-content');
    if (!modalContent) return;
    modal._historyClosePending = false;
    const badgeShelf = document.getElementById('modalBadgeStrip');
    if (badgeShelf) setModalBadgeExplainer(badgeShelf, null);

    currentAthleteModalRequestId++;
    // Resolve the return synchronously, before measuring the closing transition.
    // Motion can choose the visible athlete's row without racing scroll unlock.
    const closeReturn = { scrollTop: savedBodyScroll, returnFocusTo: athleteDialogReturnFocusElement };
    modal.dispatchEvent(new CustomEvent('athlete-dialog-closing', { detail: closeReturn }));
    savedBodyScroll = closeReturn.scrollTop;

    // Keep the card and the page behind it still until their shared fade ends.
    modalContent.inert = true;
    modal.classList.add('fade-out');
    if (modal._closeTimer) {
        clearTimeout(modal._closeTimer);
    }
    modal._closeTimer = finishAfterTransition(modal, 'opacity', () => {
        modal._closeTimer = 0;
        if (!modal.classList.contains('fade-out')) return;
        athleteDialogReturnFocusElement = null;
        modal.style.display = "none";
        modal.classList.remove('fade-out');
        modalContent.inert = false;
        releaseAthleteModal();
        restoreBodyScroll();
        restoreAthleteHistoryScroll();

        if (modalContent.classList.contains('gma-result-ready')) {
            modalContent.classList.remove('guess-mode', 'gma-fast', 'gma-result-ready');
        }
        resetAthleteShareState();
        document.getElementById('modalStickyHeader')?.classList.remove('visible');
        if (modalContent._scrollProgressListener) {
            modalContent.removeEventListener('scroll', modalContent._scrollProgressListener);
            modalContent._scrollProgressListener = null;
        }
        disposeBiomarkerHistory();
        if (typeof window.destroyAgeRadarChart === 'function') window.destroyAgeRadarChart();
        // resetModalForLoading resets the scroll on the next open. Touching the
        // hidden card here forces a layout of the newly unlocked page before
        // focus can return, adding a long pause after the fade has finished.

        resetPageTitle();
        const returnFocusElement = closeReturn.returnFocusTo;
        const focusTarget = returnFocusElement?.isConnected && !returnFocusElement.closest('[inert], [hidden]')
            && returnFocusElement.getClientRects().length ? returnFocusElement : pageDocument.querySelector('a.header-link');
        focusTarget?.focus({ preventScroll: true });
        modal.dispatchEvent(new Event('athlete-dialog-closed'));
    });
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
        podium.style.display = includePodiumGlobal && !hasActiveLeaderboardState() ? 'flex' : 'none';
    }

    const url = new URL(window.location.href);
    url.searchParams.delete('search');
    history.replaceState(history.state || {}, "", url.toString());
}

// Coalesce typing within a frame without leaving the previous results on
// screen after the visitor has already entered their next query.
const debouncedFilter = (() => {
    let filterFrame = 0;
    const debounced = function () {
        if (filterFrame) return;
        filterFrame = requestAnimationFrame(() => {
            filterFrame = 0;
            performFilter();
        });
    };
    debounced.cancel = () => {
        cancelAnimationFrame(filterFrame);
        filterFrame = 0;
    };
    return debounced;
})();

function hasActiveLeaderboardState() {
    const searchInput = document.getElementById('athleteSearch');
    const hasSearch = !!(searchInput && searchInput.value.trim().length > 0);
    return hasActiveLeaderboardFilterState() || hasSearch || (!getSelectedLeaderboardState().byFlag && window.LwcLeaderboardOrder.getState().sort !== 'rank');
}

function hasActiveLeaderboardFilterState() {
    const state = getSelectedLeaderboardState();
    return state.divisions.length > 0 || state.flags.length > 0 || state.generations.length > 0
        || state.exclusiveLeagues.length > 0 || state.leagueTracks.length === 1 || state.view !== 'ultimate' || state.byFlag;
}

function syncAgingClockFilters(currentLeaderboardView) {
    document.querySelectorAll('input[name="agingClockView"]').forEach(input => {
        const isActive = input.value === currentLeaderboardView;
        input.checked = isActive;
        input.closest('label')?.classList.toggle('active', isActive);
    });
}

function syncFlagFilters(byFlag) {
    const section = document.getElementById('flag-filter-section');
    if (!section) return;
    section.hidden = byFlag;
    section.querySelectorAll('input[name="flag"]').forEach(input => {
        if (byFlag) input.checked = false;
        input.disabled = byFlag;
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

function updateViewAllAthletesButton(count, updateSummary = true) {
    if (updateSummary) updateLeaderboardSelectionSummary(count);
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
    const isFlags = getSelectedLeaderboardState().byFlag;
    const resultLabel = `${count} ${isFlags ? (count === 1 ? 'flag' : 'flags') : (count === 1 ? 'athlete' : 'athletes')}`;
    const resultCount = document.getElementById('leaderboardResultCount');
    if (resultCount.textContent !== resultLabel) resultCount.textContent = resultLabel;
    const showResults = document.getElementById('showLeaderboardResults');
    if (showResults) showResults.textContent = `Show ${resultLabel}`;

    const choices = [];
    const state = getSelectedLeaderboardState();
    const viewLabels = { bortz: 'Bortz age', pheno: 'Pheno age', improvement: 'Pheno improvement', 'bortz-improvement': 'Bortz improvement', crowd: 'Crowd age' };
    if (state.byFlag) choices.push({ label: 'By Flag', remove: () => { document.getElementById('group-flags').checked = false; } });
    if (state.view !== 'ultimate') choices.push({ label: viewLabels[state.view], remove: () => { document.getElementById('view-ultimate').checked = true; } });
    for (const name of ['leagueTrack', 'division', 'generation', 'exclusiveLeague', 'flag']) {
        if (name === 'flag' && state.byFlag) continue;
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

    document.getElementById('group-flags').checked = false;
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
    const byFlag = document.getElementById('group-flags')?.checked || false;
    return {
        byFlag,
        ordering: window.LwcLeaderboardOrder.getState(),
        divisions: Array.from(document.querySelectorAll('input[name="division"]:checked'))
            .map(checkbox => checkbox.value),
        flags: byFlag ? [] : Array.from(document.querySelectorAll('input[name="flag"]:checked'))
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

    if (state.byFlag) url.searchParams.set('group', 'flags');
    else url.searchParams.delete('group');
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

    window.LwcLeaderboardOrder.writeUrl(url, state.ordering || { sort: 'rank' });
    return `${url.pathname}${url.search}${url.hash}`;
}

function updateFullLeaderboardLink() {
    const viewAll = getViewAllAthletesButton();
    if (viewAll) viewAll.href = window.getFullLeaderboardUrlWithCurrentState();
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
        if (state.byFlag) query.push('group=flags');
        const orderedUrl = new URL(siteUrl + selectedUrl.pathname + (query.length ? '?' + query.join('&') : ''));
        window.LwcLeaderboardOrder.writeUrl(orderedUrl, state.ordering);
        const url = orderedUrl.href;
        const id = url + '#ranking';
        const list = {
            '@type': 'ItemList', '@id': id, url,
            name: pageDocument.getElementById('leaderboardTitle')?.textContent.trim() || 'Longevity World Cup rankings',
            itemListOrder: 'https://schema.org/ItemListOrderAscending',
            numberOfItems: entries.length,
            itemListElement: entries.map(({ athlete, rank, flag }, index) => {
                if (flag) return {
                    '@type': 'ListItem', position: index + 1,
                    item: { '@type': 'Thing', name: flag, url: url + '#flag-team-' + getFlagRouteSlug(flag) }
                };
                const athleteUrl = siteUrl + '/athlete/' + encodeURIComponent(athlete.athleteSlug.replaceAll('_', '-').toLowerCase());
                return {
                    '@type': 'ListItem', position: state.ordering.sort === 'rank' ? rank : index + 1,
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

function buildFlagTeams(entries, view) {
    const groups = new Map();
    entries.forEach(entry => {
        const key = getFlagRouteSlug(entry.athlete.flag);
        if (!key) return;
        if (!groups.has(key)) groups.set(key, { key, flag: getCanonicalFlagName(entry.athlete.flag), entries: [] });
        groups.get(key).entries.push(entry);
    });
    const definitions = {
        bortz: [a => a.bortzAgeReduction, a => Number.isFinite(a.bortzAgeReduction), window.compareAthleteRank],
        pheno: [a => a.ageReduction, a => a.hasPhenoRankData && Number.isFinite(a.ageReduction), window.compareAthleteRankPhenoOnly],
        crowd: [a => a.crowdAgeReduction, a => a.crowdCount >= 100 && Number.isFinite(a.crowdAgeReduction), window.compareAthleteRankCrowdAge],
        improvement: [a => a.phenoAgeImprovement, a => Number.isFinite(a.phenoAgeImprovement), window.compareAthleteRankPhenoImprovement],
        'bortz-improvement': [a => a.bortzAgeImprovement, a => Number.isFinite(a.bortzAgeImprovement), window.compareAthleteRankBortzImprovement]
    };
    const teams = [...groups.values()].map(group => {
        const candidates = (view === 'ultimate' ? ['bortz', 'pheno'] : [view]).map(clock => {
            const [metric, eligible, compare] = definitions[clock];
            const eligibleEntries = group.entries.filter(entry => eligible(entry.athlete));
            const top = eligibleEntries.map(entry => entry.athlete).sort(compare).slice(0, 3);
            return { ...group, eligibleEntries, view: clock, top, values: top.map(metric), score: top.reduce((sum, athlete) => sum + metric(athlete), 0) };
        });
        return candidates.sort((a, b) => b.top.length - a.top.length || (a.view === 'bortz' ? -1 : 1))[0];
    }).filter(team => team.top.length > 0).sort((a, b) =>
        b.top.length - a.top.length ||
        (view === 'ultimate' ? Number(b.view === 'bortz') - Number(a.view === 'bortz') : 0) ||
        a.score - b.score || (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
    let rank = null;
    teams.forEach((team, index) => {
        if (team.top.length < 3) { team.rank = null; return; }
        if (!index || teams[index - 1].view !== team.view || teams[index - 1].score !== team.score) rank = index + 1;
        team.rank = rank;
    });
    return teams;
}

function renderFlagTeams(teams, view) {
    const container = document.getElementById('flagLeaderboard');
    const focused = document.activeElement;
    const focusedTeam = focused?.closest?.('.flag-team');
    const focusedHref = focused?.closest?.('.flag-athlete-link')?.getAttribute('href');
    const existingTeams = new Map([...container.querySelectorAll('.flag-team')].map(element => [element.id, element]));
    const existingHeadings = new Map([...container.querySelectorAll('.flag-section-heading')].map(element => [element.textContent, element]));
    const open = new Set([...container.querySelectorAll('details[open]')].map(element => element.id));
    const labels = { bortz: 'Bortz Age', pheno: 'Pheno Age', crowd: 'Crowd Age', improvement: 'Pheno Improvement', 'bortz-improvement': 'Bortz Improvement' };
    const signed = (number, decimals) => `${number > 0 ? '+' : ''}${number.toFixed(decimals)}`;
    let previousSection = '';
    const template = document.createElement('template');
    template.innerHTML = teams.map(team => {
        const id = `flag-team-${team.key}`;
        const icon = getFlagIconCode(team.flag);
        const section = team.rank ? view === 'ultimate' ? team.view : '' : 'incomplete';
        const isAmateur = section === 'pheno';
        const heading = isAmateur && previousSection === 'bortz'
            ? '<h3 class="flag-section-heading tier-separator tier-separator-cell" aria-label="Start of Amateur track (Pheno Age) section"><span class="tier-separator-label">Amateur track |<a href="https://github.com/nopara73/LongevityWorldCup/blob/master/LongevityWorldCup.Documentation/pheno-age.pdf" target="_blank" rel="noopener" class="tier-separator-link">Pheno Age</a></span></h3>'
            : section === 'incomplete' && section !== previousSection ? '<h3 class="flag-section-heading">Incomplete teams</h3>' : '';
        previousSection = section;
        return `${heading}<details class="flag-team${isAmateur ? ' tier-amateur' : ''}" id="${escapeHtml(id)}" data-clock="${team.view}" data-flag="${escapeHtml(team.flag)}" ${open.has(id) ? 'open' : ''}>
            <summary title="${labels[team.view]}"><span class="flag-team-rank" data-rank="${team.rank || ''}" aria-label="${team.rank ? `Rank ${team.rank}` : 'Unranked'}">${team.rank || '—'}</span>
            <span class="flag-team-name"><span class="flag-team-icon" aria-hidden="true">${icon ? `<span class="fi fi-${icon}"></span>` : '<i class="fas fa-flag"></i>'}</span><span>${escapeHtml(team.flag)}</span></span>
            <span class="flag-team-portraits" aria-hidden="true">${team.top.map(a => `<img src="${escapeHtml(getAthletePortraitUrl(a))}" data-athlete-name="${escapeHtml(a.displayName)}" alt="" loading="lazy">`).join('')}</span>
            <span class="flag-team-score">${team.rank ? signed(team.score, 2) : `${team.top.length}/3`} <small>${team.rank ? 'years total' : 'to qualify'}</small></span><span class="flag-team-chevron" aria-hidden="true">⌄</span></summary>
            <ol class="flag-team-athletes" aria-label="Top ${labels[team.view]} athletes for ${escapeHtml(team.flag)}">${team.top.map((a, index) => `<li><a class="flag-athlete-link" href="${escapeHtml(getAthleteProfileHref(a))}"><span class="flag-athlete-rank" title="Rank within this team">${index + 1}</span><img src="${escapeHtml(getAthletePortraitUrl(a))}" alt="" loading="lazy"><span class="flag-athlete-name">${escapeHtml(a.displayName)}<small>${hasBortzRankData(a) ? 'Pro' : 'Amateur'}</small></span><span class="flag-athlete-metric">${signed(team.values[index], 1)} <small>years</small></span></a></li>`).join('')}</ol></details>`;
    }).join('') || '<p class="flag-empty">No flags match this leaderboard.</p>';
    const nextChildren = [...template.content.children].map(incoming => {
        if (incoming.classList.contains('flag-empty')) {
            const existing = container.querySelector('.flag-empty:not([aria-busy="true"])');
            return existing?.textContent.trim() === incoming.textContent.trim() ? existing : incoming;
        }
        if (incoming.classList.contains('flag-section-heading')) {
            return existingHeadings.get(incoming.textContent) || incoming;
        }
        const existing = existingTeams.get(incoming.id);
        if (!existing) {
            incoming._flagMarkup = incoming.innerHTML;
            return incoming;
        }
        // Keep the disclosure and summary themselves stable during hydration,
        // retries and filtering. Unchanged cards also retain portrait fallbacks.
        const nextMarkup = incoming.innerHTML;
        if (existing._flagMarkup !== nextMarkup) {
            for (const selector of ['summary', '.flag-team-athletes']) {
                const currentPart = existing.querySelector(selector);
                const nextPart = incoming.querySelector(selector);
                for (const attribute of nextPart.attributes) currentPart.setAttribute(attribute.name, attribute.value);
                currentPart.replaceChildren(...nextPart.childNodes);
            }
            existing._flagMarkup = nextMarkup;
        }
        existing.className = incoming.className;
        existing.dataset.clock = incoming.dataset.clock;
        existing.dataset.flag = incoming.dataset.flag;
        return existing;
    });
    nextChildren.forEach((element, index) => {
        if (container.children[index] !== element) container.insertBefore(element, container.children[index] || null);
    });
    while (container.children.length > nextChildren.length) container.lastElementChild.remove();
    if (focusedTeam && document.activeElement !== focused) {
        const retained = existingTeams.get(focusedTeam.id);
        const matchingLink = retained?.isConnected && focusedHref
            ? [...retained.querySelectorAll('.flag-athlete-link')].find(link => link.getAttribute('href') === focusedHref)
            : null;
        const focusTarget = focused.isConnected ? focused : matchingLink || (retained?.isConnected ? retained.querySelector('summary') : document.getElementById('athleteSearch'));
        focusTarget?.focus({ preventScroll: true });
    }
    container.querySelectorAll('.flag-team').forEach((element, index) => {
        element.addEventListener('toggle', scheduleLeaderboardTableHeightSync);
        const team = teams[index];
        element.querySelectorAll('.flag-team-portraits img').forEach((img, i) => wirePortraitFallback(img, team.top[i]));
        element.querySelectorAll('.flag-athlete-link img').forEach((img, i) => wirePortraitFallback(img, team.top[i]));
    });
}

const leaderboardRowAnimations = new Map();
const leaderboardReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
function finishLeaderboardRowMotion() {
    leaderboardRowAnimations.forEach(animation => animation.cancel());
    leaderboardRowAnimations.clear();
}
leaderboardReducedMotion.addEventListener('change', finishLeaderboardRowMotion);
window.addEventListener('pagehide', finishLeaderboardRowMotion);

function captureLeaderboardRowMotion() {
    if (!window.LwcMotion?.enabled() || document.hidden) {
        finishLeaderboardRowMotion();
        return null;
    }
    const rows = [...document.querySelectorAll('.leaderboard table tbody tr')]
        .filter(row => row.style.display !== 'none');
    // Measure the old viewport before changing any labels, ranks or filters.
    // Large ranking changes get a short directional cue in their new slot:
    // crossing whole rows (or leaving copies over new results) obscures names.
    const style = getComputedStyle(pageDocument.documentElement);
    const durationText = style.getPropertyValue('--lwc-motion-base').trim();
    const duration = parseFloat(durationText) * (durationText.endsWith('ms') ? 1 : 1000) || 200;
    const easing = style.getPropertyValue('--lwc-ease-standard').trim() || 'ease';
    const before = new Map(rows.map((row, index) => [row, index]));
    const visibleSlots = rows.flatMap((row, index) => {
        const box = row.getBoundingClientRect();
        return box.height && box.bottom > 0 && box.top < innerHeight ? [index] : [];
    });
    finishLeaderboardRowMotion();
    return { before, duration, easing, first: visibleSlots[0], last: visibleSlots.at(-1) };
}

function moveLeaderboardRows(mutate, motion) {
    const next = mutate().filter(row => row.style.display !== 'none');
    if (!motion || motion.first === undefined) return;
    // Slot order supplies the direction without forcing layout halfway through
    // the update. The browser can lay out rows, counts and the rail together.
    const { before, duration, easing, first, last } = motion;
    for (let index = first; index <= Math.min(last, next.length - 1); index++) {
        const row = next[index];
        if (typeof row.animate !== 'function') continue;
        const previous = before.get(row);
        if (previous === index) continue;
        const offset = previous === undefined ? 4 : Math.sign(previous - index) * 4;
        const animation = row.animate([
            { translate: `0 ${offset}px`, opacity: previous === undefined ? .8 : 1 },
            { translate: '0 0', opacity: 1 }
        ], { duration, easing });
        leaderboardRowAnimations.set(row, animation);
        const release = () => {
            if (leaderboardRowAnimations.get(row) === animation) leaderboardRowAnimations.delete(row);
        };
        void animation.finished.then(release, release);
    }
}

function performFilter({ updateUrl = true, orderingOnly = false } = {}) {
    // Immediate filter changes already consume the current query. A stale timer
    // must not rewrite a profile URL opened from those freshly filtered rows.
    debouncedFilter.cancel();
    const rowMotion = captureLeaderboardRowMotion();
    const selectedState = getSelectedLeaderboardState();
    const query = window.normalizeString(document.getElementById('athleteSearch').value.trim().toLowerCase());
    const filterKey = JSON.stringify({ ...selectedState, ordering: undefined, search: document.getElementById('athleteSearch').value });
    const orderingSnapshot = orderingOnly && !selectedState.byFlag && leaderboardOrderingSnapshot?.athletes === athleteResults
        && leaderboardOrderingSnapshot.key === filterKey ? leaderboardOrderingSnapshot : null;
    if (!orderingSnapshot) syncFlagFilters(selectedState.byFlag);
    const selectedDivisions = selectedState.divisions.map(value => value.toLowerCase());
    const selectedFlags = selectedState.flags.map(getFlagFilterKey);
    const selectedGenerations = selectedState.generations.map(value => value.toLowerCase());
    const selectedExclusiveLeagues = selectedState.exclusiveLeagues.map(value => value.toLowerCase());
    const selectedLeagueTracks = selectedState.leagueTracks;
    const leagueTrackFilterActive = selectedLeagueTracks.length === 1;

    // Get leaderboard view (ultimate | bortz | pheno | improvement | bortz-improvement | crowd)
    const currentLeaderboardView = selectedState.view;
    if (document.querySelector('.leaderboard--flags') && !selectedState.byFlag) {
        document.querySelector('.podium')?.style.setProperty('display', includePodiumGlobal && !hasActiveLeaderboardState() ? 'flex' : 'none');
    }
    document.querySelector('.leaderboard')?.classList.toggle('leaderboard--flags', selectedState.byFlag);
    const searchInput = document.getElementById('athleteSearch');
    searchInput.placeholder = selectedState.byFlag ? 'Search flags or athletes' : 'Search athletes';
    searchInput.setAttribute('aria-label', searchInput.placeholder);
    document.querySelector('label[for="athleteSearch"]').textContent = searchInput.placeholder;
    if (!orderingSnapshot) {
        syncAgingClockFilters(currentLeaderboardView);
        updateActiveFilterSections();
        updateRankingExplanation(currentLeaderboardView, selectedState.byFlag);
    }

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
    if (metricHeader && metricHeader.textContent !== metricLabel) {
        metricHeader.textContent = metricLabel;
    }
    window.LwcLeaderboardOrder.sync(selectedState.ordering, selectedState.byFlag, metricLabel);
    document.querySelector('.podium')?.style.setProperty('display', includePodiumGlobal && !hasActiveLeaderboardState() ? 'flex' : 'none');
    if (!orderingSnapshot) document.querySelectorAll('.leaderboard table td.age-reduction-td').forEach(cell => {
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

    const searchTerms = query.split(/\s+/).filter(term => term.length > 0);
    const uniqueSearchTerms = [...new Set(searchTerms)];

    const showAll = uniqueSearchTerms.length === 0;

    const athleteBadgeTextByName = new Map();
    if (!orderingSnapshot) document.querySelectorAll('.leaderboard table tbody tr[data-athlete-name]').forEach(row => {
        const athleteName = row.getAttribute('data-athlete-name');
        if (!athleteName) return;

        const badgeSearchText = Array.from(row.querySelectorAll('.badge-section .badge-class, .athlete-name-links .badge-class'))
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

    const searchTextByAthlete = orderingSnapshot ? null : new Map(athleteResults.map(athlete => [athlete, [
        athlete.name,
        athlete.displayName,
        athleteBadgeTextByName.get(athlete.name) || '',
        athlete.mediaContact || '',
        `${athlete.flag || ''} ${athlete.canonicalFlag || ''}`,
        athlete.why || ''
    ].map(text => window.normalizeString(text.toLowerCase())).join(' ')]));

    const getMatchingEntries = (state, applySearch = true) => {
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
            metricValue: getMetricValueForView(athlete, state.view),
            metric: formatMetricForView(cohort, athlete, index, state.view)
        })).filter(entry => {
            if (showAll || !applySearch) return true;
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

    if (selectedState.byFlag) {
        const matchingTeamsByState = new Map();
        const getMatchingFlagTeams = state => {
            const key = JSON.stringify(state);
            if (!matchingTeamsByState.has(key)) {
                const teams = buildFlagTeams(getMatchingEntries(state, false), state.view);
                matchingTeamsByState.set(key, teams.filter(team => uniqueSearchTerms.every(term =>
                    window.normalizeString(team.flag.toLowerCase()).includes(term) ||
                    team.entries.some(entry => searchTextByAthlete.get(entry.athlete).includes(term)))));
            }
            return matchingTeamsByState.get(key);
        };
        const getMatchingFlagEntries = state => getMatchingFlagTeams(state).flatMap(team => team.eligibleEntries);
        const matchingTeams = getMatchingFlagTeams(selectedState);
        renderFlagTeams(matchingTeams.slice(0, maxAthletesGlobal), currentLeaderboardView);
        updateViewAllAthletesButton(matchingTeams.length);
        updateFilterCounts(selectedState, getMatchingFlagEntries, getMatchingFlagEntries(selectedState));
        updateLeaderboardTitles(selectedState);
        document.querySelector('.podium')?.style.setProperty('display', 'none');
        updateLeaderboardStructuredData(matchingTeams.slice(0, maxAthletesGlobal));
        finishLeaderboardFilter(selectedState, query, updateUrl);
        return;
    }

    const cohortEntries = orderingSnapshot ? orderingSnapshot.entries : getMatchingEntries(selectedState);
    leaderboardOrderingSnapshot = { key: filterKey, athletes: athleteResults, entries: cohortEntries };
    const matchingEntries = window.LwcLeaderboardOrder.sort(cohortEntries, selectedState.ordering);
    const tableBody = document.querySelector('.leaderboard table tbody');
    tableBody.querySelector('.leaderboard-load-error-row')?.remove();
    const allRows = Array.from(tableBody.querySelectorAll('tr:not(.no-results)'));
    const rowsByName = new Map(allRows.filter(row => row.dataset.athleteName).map(row => [row.dataset.athleteName, row]));
    window.LwcLeaderboardOrder.renderValues(matchingEntries, selectedState.ordering, rowsByName);
    const entriesByAthlete = new Map(matchingEntries.map(entry => [entry.athlete, entry]));
    const unslicedFilteredAthletes = matchingEntries.map(entry => entry.athlete);
    athleteDialogBoardSequence = unslicedFilteredAthletes;
    const filteredAthletes = unslicedFilteredAthletes.slice(0, maxAthletesGlobal);
    updateViewAllAthletesButton(unslicedFilteredAthletes.length, !orderingSnapshot);

    const tableRows = allRows;
    const tierSeparatorRow = document.querySelector('.leaderboard table tbody tr.tier-separator');

    // Remove 'blurred' class from all podium items
    const podiumItems = document.querySelectorAll('.podium-item');
    const podiumByName = new Map(Array.from(podiumItems, item => [item.dataset.athleteName, item]));
    podiumItems.forEach(item => {
        item.classList.remove('blurred');
    });

    let anyPodiumVisible = false;

    // Show the rows and podium items for the filtered athletes
    filteredAthletes.forEach(athlete => {
        const athleteName = athlete.name;
        const row = rowsByName.get(athleteName);
        if (row) {
            // Update the rank cell
            const rankCell = row.querySelector('td[data-label="Rank"] .rank');
            if (rankCell) {
                setDisplayedRank(rankCell, entriesByAthlete.get(athlete).rank);
            }

            const ageReductionCell = row.querySelector('.age-reduction');
            const displayMetric = entriesByAthlete.get(athlete).metric;
            if (ageReductionCell && displayMetric && ageReductionCell.textContent !== displayMetric) {
                ageReductionCell.textContent = displayMetric;
            }

        }
        const item = podiumByName.get(athleteName);
        if (item) {
            if (item.style.display) item.style.display = '';
            anyPodiumVisible = true;
        }
    });

    const hidePodiumRowsInTable = includePodiumGlobal && selectedState.ordering.sort === 'rank' && selectedDivisions.length === 0 && selectedFlags.length === 0 && selectedGenerations.length === 0 && selectedExclusiveLeagues.length === 0 &&
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
        if (currentLeaderboardView === 'ultimate' && selectedState.ordering.sort === 'rank') {
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
    const visibleOrdered = tableVisibleAthletes.map(a => rowsByName.get(a.name)).filter(Boolean);
    const visibleRows = new Set(visibleOrdered);
    // Rows keep their identity, so a reorder or filter glides instead of jumping.
    moveLeaderboardRows(() => {
        rowsByName.forEach(row => {
            const display = visibleRows.has(row) ? '' : 'none';
            if (row.style.display !== display) row.style.display = display;
        });
        const separator = allRows.find(r => r.classList.contains('tier-separator'));
        const hiddenRows = allRows.filter(r => r !== separator && !visibleRows.has(r));
        const orderedRows = [...visibleOrdered];
        const isProForOrder = (a) => a.bortzAgeReduction != null && Number.isFinite(a.bortzAgeReduction);
        const firstAmateurIndex = tableVisibleAthletes.findIndex(a => !isProForOrder(a));
        const separatorVisible = tierSeparatorRow && tierSeparatorRow.style.display !== 'none';
        if (separator && separatorVisible && firstAmateurIndex > 0 && firstAmateurIndex < visibleOrdered.length) {
            orderedRows.splice(firstAmateurIndex, 0, separator);
        } else {
            if (separator) orderedRows.push(separator);
        }
        orderedRows.push(...hiddenRows);
        let nextRow = tableBody.firstElementChild;
        for (const row of orderedRows) {
            if (row === nextRow) nextRow = nextRow.nextElementSibling;
            else tableBody.insertBefore(row, nextRow);
        }
        return orderedRows;
    }, rowMotion);

    // An ordering change cannot alter the search or sidebar membership. Keep
    // their DOM and computed counts intact instead of rebuilding them per click.
    if (!orderingSnapshot) {
        if (document.querySelector('.athlete-name .highlight')) window.removeAllHighlights();
        if (uniqueSearchTerms.length) {
            const filteredNames = new Set(matchingEntries.map(entry => entry.athlete.name));
            document.querySelectorAll('.athlete-name').forEach(element => {
                if (filteredNames.has(element.closest('[data-athlete-name]')?.dataset.athleteName)) window.highlightText(element, uniqueSearchTerms);
            });
        }
        updateFilterCounts(selectedState, getMatchingEntries, matchingEntries);
    }

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

    if (!orderingSnapshot) updateLeaderboardTitles(selectedState);

    // Display "No Results Found" if Necessary
    displayNoResults(tableRows, podiumItems, query);
    updateLeaderboardStructuredData(matchingEntries.slice(0, maxAthletesGlobal));

    // Hide podium athletes' rows if no filters/search are applied and includePodiumGlobal is true
    if (hidePodiumRowsInTable) {
        document.querySelectorAll('.leaderboard table tbody tr.podium-athlete').forEach(row => {
            row.style.display = 'none';
        });
    }

    if (!orderingSnapshot && typeof AOS !== 'undefined') {
        AOS.refresh();
    }

    finishLeaderboardFilter(selectedState, query, updateUrl);
}

function finishLeaderboardFilter(selectedState, query, updateUrl) {
    const currentLeaderboardView = selectedState.view;
    const selectedDivisions = selectedState.divisions.map(value => value.toLowerCase());
    const selectedGenerations = selectedState.generations.map(value => value.toLowerCase());
    const selectedExclusiveLeagues = selectedState.exclusiveLeagues.map(value => value.toLowerCase());
    const selectedLeagueTracks = selectedState.leagueTracks;
    const leagueTrackFilterActive = selectedLeagueTracks.length === 1;
    updateLeaderboardStateIndicator();
    scheduleLeaderboardTableHeightSync();
    renderedLeaderboardSelection = getLeaderboardSelectionKey();
    // All row cues compare the same committed selection. Reading every filter
    // again for each athlete turns a single input into thousands of DOM queries.
    renderedStandingsComparable = !hasActiveLeaderboardState();
    pageDocument.dispatchEvent(new Event('lwc:standings-ready'));

    // Initial hydration must preserve direct rank/profile fragments and unrelated URL state.
    if (!updateUrl) {
        normalizeLeaderboardUrl();
        updateFullLeaderboardLink();
        return;
    }

    const url = new URL(window.location.href);
    window.LwcLeaderboardOrder.writeUrl(url, selectedState.ordering);

    if (query === "") {
        url.searchParams.delete('search');
    } else {
        url.searchParams.set('search', encodeURIComponent(query));
    }

    const selectedFlagKeys = selectedState.flags.map(getFlagFilterKey);
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
    const routeParams = [...url.searchParams.keys()].filter(key => !['group', 'sort', 'dir', 'then', 'thenDir'].includes(key));
    const cleanSearch = routeParams.length === 0 || (routeParams.length === 1 && (url.searchParams.has('filters') || url.searchParams.has('view')));
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

    if (selectedState.byFlag) url.searchParams.set('group', 'flags');
    else url.searchParams.delete('group');
    history.replaceState(history.state || {}, "", url.toString());
    updateFullLeaderboardLink();
}

function updateRankingExplanation(currentLeaderboardView, byFlag = getSelectedLeaderboardState().byFlag) {
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

    const html = byFlag
        ? (currentLeaderboardView === 'ultimate'
            ? 'Top-three total age reduction. Complete Bortz teams first, then complete Pheno teams.'
            : 'Top-three total years. Lower wins.')
        : explanations[currentLeaderboardView] || '';
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
    if (state.byFlag) {
        let cohortLabel = [getGenerationLeagueLabel(state.generations), getDivisionLeagueLabel(state.divisions)].filter(Boolean).join(' ');
        if (state.exclusiveLeagues.length > 0) cohortLabel = state.exclusiveLeagues.length === 1 ? state.exclusiveLeagues[0] : 'Exclusive Leagues';
        else if (!cohortLabel && state.leagueTracks.length === 1) cohortLabel = getLeagueTrackLabel(state.leagueTracks[0]);
        const clockLabel = state.view === 'ultimate' && cohortLabel ? '' : viewPresentation.railText.replace(/ League$/, '');
        const railText = [cohortLabel, clockLabel, state.view === 'ultimate' ? 'Flag League' : 'by Flag'].filter(Boolean).join(' ');
        return { railText, documentTitle: railText, accessibleLabel: railText };
    }
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
    scheduleLeaderboardTableHeightSync();
}

function measureCollapsedTitleHeight() {
    const collapsedTitle = document.querySelector('.collapsed-title');
    if (!collapsedTitle || collapsedTitle.getClientRects().length === 0 || collapsedTitle.clientHeight <= 0) return null;

    // Short result sets still need room for the complete league name.
    // Measure the visible rail, independently of the current row count. Its
    // icon follows the table head and can be taller than the drawer heading.
    const heading = document.querySelector('.sidebar-rail-icon') || document.querySelector('.sidebar-heading');
    if (!heading) return null;
    return Math.ceil(heading.getBoundingClientRect().height + collapsedTitle.scrollHeight);
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
        requestAnimationFrame(syncCollapsedTitleHeight);
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
        if (state.byFlag && input.name === 'flag') return;
        const isView = input.name === 'agingClockView';
        const field = fields[input.name];
        if (!isView && !field) return;

        // Counts use the result unit (athletes or flags) after adding this option to its OR group,
        // or switching clocks, with all other selections and the search preserved.
        const candidateState = isView
            ? { ...state, view: input.value }
            : { ...state, [field[0]]: [...state[field[0]], input.value] };
        const entries = input.checked ? currentEntries : getMatchingEntries(candidateState);
        const value = input.name === 'flag' ? getFlagFilterKey(input.value) : input.value.toLowerCase();
        const members = isView ? entries : entries.filter(entry => field[1](entry.athlete).toLowerCase() === value);
        const count = state.byFlag ? new Set(members.map(entry => entry.athlete.flagFilterKey)).size : members.length;
        const countLabel = `${count} ${state.byFlag ? (count === 1 ? 'flag' : 'flags') : (count === 1 ? 'athlete' : 'athletes')}`;
        label.querySelector('.filter-count')?.setAttribute('aria-label', countLabel);
        label.querySelector('.filter-count')?.setAttribute('title', countLabel);
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
    const existingMessage = document.querySelector('.no-results');

    // Check if any rows or podium items are visible
    const anyVisible = Array.from(tableRows).some(row => row.style.display !== 'none');

    if (anyVisible) {
        existingMessage?.remove();
        return;
    }
    if (existingMessage) {
        const host = existingMessage.querySelector('.no-results-empty-state');
        if (host) window.LwcEmptyState?.decorate(host, query.trim() ? 'search' : 'filter');
        return;
    }
    if (!anyVisible) {
        const tableBody = document.querySelector('.leaderboard table tbody');
        const noResults = document.createElement('tr');
        noResults.classList.add('no-results');
        noResults.innerHTML = `
            <td colspan="${document.querySelectorAll('.leaderboard table thead th').length}">
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

window.LwcLeaderboardOrder.bind(() => performFilter({ updateUrl: false, orderingOnly: true }));
document.getElementById('group-flags')?.addEventListener('change', () => performFilter());

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

// Shows a rank and marks the top three places of the current view for their medal styling.
function setDisplayedRank(rankElement, rank) {
    if (rankElement.textContent !== String(rank)) rankElement.textContent = rank;
    const place = Number(rank);
    if (place >= 1 && place <= 3) {
        rankElement.dataset.place = String(place);
    } else {
        delete rankElement.dataset.place;
    }
}

function renderFilterIcon(iconClass) {
    return iconClass
        ? `<i class="fas ${escapeHtml(iconClass)} filter-icon" aria-hidden="true"></i>`
        : '';
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

// The flag rides on the portrait's edge, opposite the PRO badge, and leads to the flag's team.
function renderPortraitFlagMark(flag) {
    const mark = document.getElementById('modalFlagMark');
    if (!mark) return;
    // Loading can open before the flag module arrives; clearing the old mark
    // must not depend on the module that resolves the eventual athlete's flag.
    const canonicalFlag = flag ? getCanonicalFlagName(flag) : '';
    const icon = canonicalFlag ? window.LwcFlags.renderFlagIcon(canonicalFlag, 'portrait-flag-icon') : '';
    mark.innerHTML = icon;
    mark.hidden = !icon;
    if (icon) {
        mark.href = buildFlagHref(canonicalFlag);
        mark.setAttribute('aria-label', canonicalFlag);
        mark.title = canonicalFlag;
    } else {
        mark.removeAttribute('href');
        mark.removeAttribute('aria-label');
        mark.removeAttribute('title');
    }
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

    const badgeShelf = document.getElementById('modalBadgeStrip');
    if (badgeShelf?.querySelector('.modal-badge-item.expanded')) {
        setModalBadgeExplainer(badgeShelf, null);
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

    // Return to the activated control, or the name link for a row/card click.
    const returnFocusTo = event.target.closest('a.athlete-name, a.athlete-profile-link')
        || athleteRow.querySelector('a.athlete-name')
        || athleteRow.querySelector('a.athlete-profile-link');
    fetchFullAthleteData(athleteNameText, athleteData, { returnFocusTo });
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
    releaseAthleteModal();
    restoreBodyScroll();
    restoreAthleteHistoryScroll();
    window.location.assign(fallbackUrl);
}

window.openAthleteModalBySlug = function (athleteSlug, options) {
    const normalizedSlug = normalizeAthleteSlugForLookup(athleteSlug);
    if (!normalizedSlug) return false;
    rememberAthleteModalOpener(options);

    const athleteData = findAthleteDataBySlug(normalizedSlug);
    if (athleteData) {
        fetchFullAthleteData(athleteData.name, athleteData, options);
        return true;
    }

    // Once the index is loaded, an unknown profile must retain ordinary
    // anchor navigation instead of swallowing a potentially valid URL.
    if (athleteResultsReady) {
        return false;
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
    const icon = label === 'Copied' ? 'fa-check' : 'fa-share-nodes';
    button.classList.toggle('is-copied', label === 'Copied');
    button.innerHTML = `<i class="fa ${icon}" aria-hidden="true"></i> ${escapeHtml(label)}`;
}

async function loadAthleteSharePreview(state, shareUrl) {
    const previous = document.getElementById('athleteSharePreview');
    const figure = previous?.closest('.athlete-share-preview');
    const menu = document.getElementById('athleteShareMenu');
    if (!previous || !figure || !menu) return;

    // A distinct element binds load/error events to this opening, including when
    // the same profile is reopened while an older image request is still in flight.
    const image = document.createElement('img');
    image.id = 'athleteSharePreview';
    image.alt = '';
    image.width = 1200;
    image.height = 630;
    previous.replaceWith(image);
    figure.classList.remove('is-loaded');
    figure.hidden = false;
    const menuVersion = state.menuVersion;
    const current = () => isCurrentAthleteShare(state) && state.menuVersion === menuVersion
        && !menu.hidden && image.isConnected;
    const recovery = document.getElementById('athleteShareRecovery');
    image.onload = () => {
        if (!current() || (recovery && !recovery.hidden)) return;
        figure.classList.add('is-loaded');
        keepAthleteShareMenuInView(menu);
    };
    image.onerror = () => {
        if (!current()) return;
        figure.hidden = true;
        keepAthleteShareMenuInView(menu);
    };

    const abort = new AbortController();
    state.previewAbort = abort;
    const deadline = window.setTimeout(() => abort.abort(), 10000);
    try {
        // Sharing always uses the canonical athlete URL. The containing page's
        // og:image may belong to another athlete, clock, league or data revision.
        const slug = new URL(shareUrl).pathname.match(/^\/athlete\/([^/]+)$/)?.[1];
        if (!slug) throw new Error('Missing profile');
        const response = await fetch(`/og/athlete/${slug}/preview`, { cache: 'no-store', signal: abort.signal });
        if (!response.ok) throw new Error('Preview unavailable');
        const preview = await response.json();
        if (!current()) return;
        const url = new URL(preview.imageUrl, window.location.origin);
        if (url.origin !== window.location.origin || url.pathname !== `/og/athlete/${slug}.png`)
            throw new Error('Invalid preview');
        image.src = url.toString();
    } catch (error) {
        if (current()) figure.hidden = true;
    } finally {
        window.clearTimeout(deadline);
        if (state.previewAbort === abort) state.previewAbort = null;
    }
}

function isCurrentAthleteShare(state) {
    return !!state && athleteShareState === state
        && state.requestId === currentAthleteModalRequestId && state.button.isConnected;
}

function resetAthleteShareState() {
    if (athleteShareState?.feedbackTimer) window.clearTimeout(athleteShareState.feedbackTimer);
    closeAthleteShareMenu();
    athleteShareState = null;
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
    athleteShareLayoutCleanup?.();
    athleteShareLayoutCleanup = null;
    const menu = document.getElementById('athleteShareMenu');
    if (!menu) return false;
    const wasOpen = !menu.hidden;
    if (athleteShareState) athleteShareState.menuVersion++;
    athleteShareState?.previewAbort?.abort();
    const image = document.getElementById('athleteSharePreview');
    if (image) {
        image.onload = null;
        image.onerror = null;
        image.removeAttribute('src');
        const figure = image.closest('.athlete-share-preview');
        if (figure) {
            figure.hidden = true;
            figure.classList.remove('is-loaded');
        }
    }

    menu.hidden = true;
    menu.classList.remove('is-above');
    menu.style.removeProperty('--athlete-share-available-height');
    menu.style.removeProperty('--athlete-share-offset');
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
    const parent = menu.offsetParent;
    if (!parent) return;

    const scrollTop = menu.scrollTop;
    menu.style.removeProperty('--athlete-share-available-height');
    menu.style.removeProperty('--athlete-share-offset');
    menu.classList.remove('is-above');
    const modalContent = document.querySelector('#detailsModal .modal-content');
    const modalRect = modalContent ? modalContent.getBoundingClientRect() : null;
    const viewportBottom = Math.min(window.innerHeight, modalRect ? modalRect.bottom : window.innerHeight);
    const chrome = [...(modalContent?.querySelectorAll('#closeAthleteDetailsModal, :scope > .athlete-steps button, .modal-sticky-header.visible') || [])]
        .map(element => element.getBoundingClientRect())
        .filter(rect => rect.width > 0 && rect.height > 0 && rect.top < viewportBottom && rect.bottom > 0);
    const viewportTop = Math.max(0, modalRect ? modalRect.top : 0, ...chrome.map(rect => rect.bottom));
    const padding = 12;
    // Entry motion belongs to the menu's paint, not its placement. Measure its
    // layout box through the parent so the scale/translation cannot leave a
    // stale offset when the animation finishes.
    const measure = () => {
        const rect = parent.getBoundingClientRect();
        const scale = parent.offsetWidth > 0 ? rect.width / parent.offsetWidth : 1;
        const top = rect.top + (parent.clientTop + menu.offsetTop - parent.scrollTop) * scale;
        const height = menu.offsetHeight * scale;
        return { top, bottom: top + height, height, scale };
    };
    const belowRect = measure();
    const below = Math.max(0, viewportBottom - Math.max(viewportTop, belowRect.top) - padding);
    menu.classList.add('is-above');
    const aboveRect = measure();
    const above = Math.max(0, Math.min(viewportBottom, aboveRect.bottom) - viewportTop - padding);
    const useAbove = belowRect.height > below && above > below;
    menu.classList.toggle('is-above', useAbove);
    // A short menu can fit the viewport even when neither side of its trigger
    // has enough room. Shift it within the usable viewport before introducing
    // a native scroller for genuinely taller content.
    const top = viewportTop + padding;
    const bottom = viewportBottom - padding;
    menu.style.setProperty('--athlete-share-available-height', `${Math.max(0, Math.floor((bottom - top) / belowRect.scale))}px`);
    const placed = measure();
    const offset = placed.top < top ? top - placed.top : Math.min(0, bottom - placed.bottom);
    menu.style.setProperty('--athlete-share-offset', `${offset / placed.scale}px`);
    menu.scrollTop = scrollTop;
}

function keepAthleteShareMenuInView(menu) {
    if (!menu || menu.hidden) return;
    positionAthleteShareMenu(menu);
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
                    // A failed copy needs the room more than the picture does.
                    const preview = menu.querySelector('.athlete-share-preview');
                    if (preview) preview.hidden = true;
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
    menu.scrollTop = 0;
    void loadAthleteSharePreview(state, sharePayload.url);
    button.setAttribute('aria-expanded', 'true');

    const copyButton = document.getElementById('copyAthleteProfileLink');
    if (copyButton) {
        copyButton.focus({ preventScroll: true });
    }
    keepAthleteShareMenuInView(menu);
    athleteShareLayoutCleanup?.();
    let layoutFrame = 0;
    const scheduleLayout = () => {
        if (layoutFrame) return;
        layoutFrame = requestAnimationFrame(() => {
            layoutFrame = 0;
            if (isCurrentAthleteShare(state) && !menu.hidden) keepAthleteShareMenuInView(menu);
        });
    };
    const observer = new ResizeObserver(scheduleLayout);
    // Observe the anchors, not the menu whose height the callback adjusts.
    observer.observe(button.parentElement);
    const modalContent = menu.closest('.modal-content');
    if (modalContent) {
        observer.observe(modalContent);
        const header = modalContent.querySelector('.modal-sticky-header');
        if (header) observer.observe(header);
        modalContent.addEventListener('scroll', scheduleLayout, { passive: true });
    }
    scheduleLayout();
    athleteShareLayoutCleanup = () => {
        observer.disconnect();
        cancelAnimationFrame(layoutFrame);
        modalContent?.removeEventListener('scroll', scheduleLayout);
    };
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
    if (preserveServerProfile && athleteNameElement) {
        // Sharing the readable server profile needs only its name and canonical
        // slug; a failed enhancement must not leave this action disabled.
        configureAthleteShareButton(athleteNameElement.textContent.trim(), athleteSlug);
    }
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

// Record navigation intent before mutating the profile. History snapshots can
// otherwise force a layout of half-hydrated content. Each newer intent replaces
// the pending step immediately; only its request may commit profile content.
function syncAthleteDialogHistory(athleteSlug, options) {
    const pathnameBeforeOpen = window.location.pathname;
    if (!/^\/athlete\/[^/]+\/?$/.test(pathnameBeforeOpen)) {
        originalAthletelessURL = getAthletelessURL();
    }
    const historyMode = options?.historyMode || 'push';
    const previousState = history.state;
    if (historyMode === 'replace' || (historyMode === 'push'
        && previousState?.modal === 'details' && previousState.athlete === athleteSlug)) {
        history.replaceState({ ...previousState, modal: 'details', athlete: athleteSlug }, '', window.location.href);
    } else if (historyMode === 'step') {
        // Stepping swaps the profile in place: Back and Close still return
        // to the page the dialog was opened from.
        history.replaceState({ ...previousState, modal: 'details', athlete: athleteSlug }, '', `/athlete/${athleteSlug}`);
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

}

function fetchFullAthleteData(athleteNameText, athleteData, options) {
    window.initializeSharedPageData();
    rememberAthleteModalOpener(options);

    const athleteSlug = normalizeAthleteSlugForLookup(
        athleteData?.athleteSlug || athleteNameText);
    const normalizedAthleteName = normalizeAthleteSlugForLookup(athleteNameText);
    const suppressGuessMyAge = !!(options && options.suppressGuessMyAge);
    const requestId = ++currentAthleteModalRequestId;
    syncAthleteDialogHistory(athleteSlug, options);
    const modalContent = resetModalForLoading(athleteSlug, options?.historyMode === 'step');
    if (modalContent && options?.historyMode !== 'step') delete modalContent.dataset.athleteStep;

    const athletesRequest = window.getSharedAthletes();
    const hydrate = athletes => {
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

            // Resolve the result with its profile; canvas work can follow the first paint.
            const bioAge = (athleteData.lowestBortzAge != null && Number.isFinite(athleteData.lowestBortzAge))
                ? athleteData.lowestBortzAge
                : athleteData.lowestPhenoAge;
            const chronoAge = (athleteData.lowestBortzAge != null && Number.isFinite(athleteData.lowestBortzAge))
                ? athleteData.chronoAtLowestBortzAge
                : athleteData.chronoAtLowestPhenoAge;

            populateProofsGallery(fullAthleteData);
            const biomarkerHistory = buildBiomarkerHistory(fullAthleteData);
            renderBiomarkerHistory(biomarkerHistory, athletes);

            // Commit result text and controls with this identity. Only the
            // canvas dependency and construction wait until after its first paint.
            generateAgeVisualization(bioAge, chronoAge, athleteData, athleteResults, biomarkerHistory.clockResults,
                () => window.ensureChartJs());
            if (!modalContent) {
                console.error('Modal content element not found');
                return;
            }
            modalContent.removeAttribute('data-server-rendered-loading');
            modalContent.classList.remove('is-loading');

            modalContent.dataset.athleteSlug = athleteSlug;
            if (profileImageId) modalContent.dataset.profileImageId = profileImageId;
            else delete modalContent.dataset.profileImageId;

            // Hide sticky header initially
            const stickyHeader = document.getElementById('modalStickyHeader');
            if (stickyHeader) {
                stickyHeader.classList.remove('visible');
            }

            // Initialize scroll progress indicator
            initializeScrollProgress(modalContent);
            updateAthleteDialogSteps();

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
            const skipAll = window.LwcGuessState?.shouldSkipAll();

            const seen = window.LwcGuessState?.hasSeenPrompt();

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
                window.LwcGuessState?.markPromptSeen();
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
                    window.LwcGuessState?.markPromptSeen();
                }
            }

            const currentEnlargedState = history.state;
            if (options?.restoreEnlargedState
                && currentEnlargedState?.modal === 'enlarged'
                && currentEnlargedState.athlete === athleteSlug
                && !restoreEnlargedViewFromHistory(currentEnlargedState)) {
                history.replaceState({ ...history.state, modal: 'details', athlete: athleteSlug }, '', window.location.href);
            }

            modal.dispatchEvent(new Event('athlete-dialog-ready'));
            if (!modalContent.classList.contains('guess-mode')) preloadAthleteDialogNeighbours();

        };
    const failed = error => {
            if (requestId !== currentAthleteModalRequestId) return;
            handleAthleteModalLoadFailure(modalContent, athleteSlug, options, error);
        };
    // Reuse only the exact shared request's resolved snapshot. Refreshes and
    // image-identity updates replace that promise, so they cannot reuse stale
    // data. Cached steps commit in one turn instead of exposing a reset state
    // to mutation observers before an already-resolved promise resumes.
    if (athleteDialogSnapshotRequest === athletesRequest && athleteDialogSnapshot) {
        try { hydrate(athleteDialogSnapshot); }
        catch (error) { failed(error); }
        return Promise.resolve();
    }
    return athletesRequest.then(athletes => {
        if (requestId !== currentAthleteModalRequestId) return;
        athleteDialogSnapshotRequest = athletesRequest;
        athleteDialogSnapshot = athletes;
        hydrate(athletes);
    }).catch(failed);
}

async function refreshAthleteAfterStaleGuess(athleteSlug) {
    const normalizedSlug = normalizeAthleteSlugForLookup(athleteSlug);
    if (!normalizedSlug) return false;
    const requestId = currentAthleteModalRequestId;

    try {
        const refreshUrl = `/api/data/athletes?profileImageRefresh=${Date.now()}`;
        const response = await fetchWithTimeout(refreshUrl, {
            cache: 'no-store',
            headers: { accept: 'application/json' }
        }, 10000);
        if (!response.ok) return false;

        const athletes = await response.json();
        if (!Array.isArray(athletes)) return false;
        if (requestId !== currentAthleteModalRequestId
            || modal.style.display !== 'block' || modal.classList.contains('fade-out')) return false;

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

function populateProfilePace(id, lowestAge, chronologicalAge, orderedAthletes, name) {
    const pace = Number.isFinite(lowestAge) && Number.isFinite(chronologicalAge) && chronologicalAge > 0
        ? lowestAge / chronologicalAge : null;
    const available = Number.isFinite(pace);
    document.getElementById(id + 'Container').hidden = !available;
    document.getElementById(id).textContent = available ? pace.toFixed(2) : '';
    const rank = available ? findAthleteIndexByName(orderedAthletes || [], name) + 1 : 0;
    document.getElementById(id + 'Rank').textContent = rank > 0 ? '#' + rank : '';
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
    // This control survives paging and also appears in hidden-identity rounds.
    profilePic.setAttribute('aria-label', 'Enlarge athlete profile picture');
    profilePic.loading = 'eager';
    profilePic.classList.remove('portrait-fallback');
    wirePortraitFallback(profilePic, fullAthleteData);
    const stickyPortrait = document.getElementById('stickyAthletePortrait');
    if (stickyPortrait) {
        stickyPortrait.dataset.athleteName = modalDisplayName;
        stickyPortrait.dataset.lwcImageRole = profilePic.dataset.lwcImageRole || 'athlete';
        stickyPortrait.src = profilePic.src;
        stickyPortrait.hidden = !profilePic.src;
        stickyPortrait.onerror = () => { stickyPortrait.hidden = true; };
    }

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

    const athleteBio = document.getElementById('athleteBio');
    const why = String(fullAthleteData.Why ?? '');
    athleteBio.textContent = why;
    // Match .NET TrimStart without changing any athlete-authored characters.
    const whyStart = why.replace(/^\p{White_Space}+/u, '');
    athleteBio.hidden = whyStart.length === 0;
    athleteBio.toggleAttribute('data-leading-quote', /^["'“‘„«‹「『]/u.test(whyStart));

    document.getElementById('athleteRank').innerHTML = renderBestRankLink(rankSummary && rankSummary.bestCandidate);
    document.getElementById('athleteDivision').innerHTML = `<a href="/league/${window.slugifyName(athleteData.division, true)}" class="league-link">${escapeHtml(athleteData.division)}</a>${getModalTitleRankMarkup(athleteData.ranks[athleteData.division])}`;
    document.getElementById('athleteGeneration').innerHTML = `<a href="/league/${window.slugifyName(athleteData.generation, true)}" class="league-link">${escapeHtml(athleteData.generation)}</a>${getModalTitleRankMarkup(athleteData.ranks[athleteData.generation])}`;
    document.getElementById('athleteFlag').innerHTML = renderAthleteFlagDetail(fullAthleteData.Flag || athleteData.canonicalFlag, athleteData);
    renderPortraitFlagMark(fullAthleteData.Flag || athleteData.canonicalFlag);
    const completedAge = getCompletedChronologicalAge(fullAthleteData, athleteData);
    document.getElementById('chronologicalAge').textContent = Number.isFinite(completedAge) ? completedAge.toString() : '';
    document.getElementById('chronologicalAgeContainer').hidden = !Number.isFinite(completedAge);
    updateCrowdContainer(athleteData.crowdAge, athleteData.crowdCount);
    updateYourGuess();
    const lowestBortzAgeEl = document.getElementById('lowestBortzAge');
    const lowestBortzAgeContainer = document.getElementById('lowestBortzAgeContainer');
    if (athleteData.lowestBortzAge != null && Number.isFinite(athleteData.lowestBortzAge)) {
        lowestBortzAgeEl.textContent = athleteData.lowestBortzAge.toFixed(1);
        lowestBortzAgeContainer.hidden = false;
    } else {
        lowestBortzAgeEl.textContent = '';
        lowestBortzAgeContainer.hidden = true;
    }
    const hasPhenoAge = Number.isFinite(athleteData.lowestPhenoAge);
    document.getElementById('lowestPhenoAge').textContent = hasPhenoAge ? athleteData.lowestPhenoAge.toFixed(1) : '';
    document.getElementById('lowestPhenoAgeContainer').hidden = !hasPhenoAge;
    const bortzAgeReductionContainer = document.getElementById('bortzAgeReductionContainer');
    const bortzAgeReductionLabel = document.getElementById('bortzAgeReductionAccelerationLabel');
    if (athleteData.bortzAgeReduction != null && Number.isFinite(athleteData.bortzAgeReduction)) {
        bortzAgeReductionLabel.textContent = athleteData.bortzAgeReduction < 0 ? "Bortz Age reduction" : "Bortz Age acceleration";
        document.getElementById('bortzAgeReduction').textContent = (athleteData.bortzAgeReduction >= 0 ? "+" : "") + athleteData.bortzAgeReduction.toFixed(1);
        document.getElementById('bortzAgeReductionPercent').textContent = Number.isFinite(athleteData.bortzAgeReductionPercent) ? athleteData.bortzAgeReductionPercent.toFixed(1) : '';
        document.getElementById('bortzAgeReductionPercent').parentElement.hidden = !Number.isFinite(athleteData.bortzAgeReductionPercent);
        bortzAgeReductionContainer.hidden = false;
    } else {
        bortzAgeReductionContainer.hidden = true;
    }
    document.getElementById('ageReductionAccelerationLabel').textContent = athleteData.ageReduction < 0 ? "Pheno Age reduction" : "Pheno Age acceleration";
    document.getElementById('ageReduction').textContent = Number.isFinite(athleteData.ageReduction)
        ? (athleteData.ageReduction >= 0 ? "+" : "") + athleteData.ageReduction.toFixed(1) : '';
    document.getElementById('ageReductionContainer').hidden = !Number.isFinite(athleteData.ageReduction);
    document.getElementById('ageReductionPercent').textContent = Number.isFinite(athleteData.ageReductionPercent) ? athleteData.ageReductionPercent.toFixed(1) : '';
    document.getElementById('ageReductionPercent').parentElement.hidden = !Number.isFinite(athleteData.ageReductionPercent);
    populateProfilePace('bortzPaceOfAging', athleteData.lowestBortzAge, athleteData.chronoAtLowestBortzAge, athletesOrderedByBortzPace, athleteData.name);
    populateProfilePace('paceOfAging', athleteData.lowestPhenoAge, athleteData.chronoAtLowestPhenoAge, athletesOrderedByPace, athleteData.name);
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
    setModalBadgeExplainer(modalBadgeSection, null);
    modalBadgeSection.replaceChildren();
    // Clone athleteData but disable personalLink for modal badges
    const athleteForBadges = { ...athleteData, personalLink: null };
    window.setBadges(athleteForBadges, {
        querySelector: () => modalBadgeSection
    });

    // wrap each badge in a column with its name
    const badges = Array.from(modalBadgeSection.children);
    modalBadgeSection.replaceChildren();
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
        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'modal-badge-toggle';
        toggle.setAttribute('aria-expanded', 'false');
        toggle.setAttribute('aria-label', fullTitle ? `Show badge details: ${fullTitle}` : 'Show badge details');
        badge.removeAttribute('title');
        if (!badge.matches('a')) {
            badge.removeAttribute('tabindex');
            badge.removeAttribute('aria-label');
            badge.removeAttribute('role');
            badge.removeAttribute('aria-haspopup');
            badge.setAttribute('aria-hidden', 'true');
            toggle.appendChild(badge);
        } else wrapper.appendChild(badge);

        const label = document.createElement('span');
        label.classList.add('modal-badge-name');
        const title = document.createElement('span');
        title.className = 'modal-badge-title';
        title.textContent = titlePart;
        label.appendChild(title);
        if (detailPart) {
            const detail = document.createElement('span');
            detail.className = 'modal-badge-detail';
            detail.textContent = detailPart;
            label.appendChild(detail);
        }
        toggle.appendChild(label);
        wrapper.appendChild(toggle);

        toggle.addEventListener('click', () => {
            setModalBadgeExplainer(modalBadgeSection, wrapper.classList.contains('expanded') ? null : wrapper);
        });
        wrapper.addEventListener('keydown', (event) => {
            if (event.key === 'Escape' && wrapper.classList.contains('expanded')) {
                event.stopPropagation();
                setModalBadgeExplainer(modalBadgeSection, null);
                return;
            }
        });
        modalBadgeSection.appendChild(wrapper);
    });

}

// The shelf keeps its grid still: a chosen badge's explanation opens as one
// panel under that badge's row, with a caret pointing back at the badge.
let modalBadgeExplainerObserver = null;
let modalBadgeExplainerFrame = 0;

function setModalBadgeExplainer(shelf, item) {
    modalBadgeExplainerObserver?.disconnect();
    modalBadgeExplainerObserver = null;
    cancelAnimationFrame(modalBadgeExplainerFrame);
    modalBadgeExplainerFrame = 0;
    shelf.querySelectorAll('.modal-badge-item.expanded').forEach(open => {
        open.classList.remove('expanded');
        const toggle = open.querySelector('.modal-badge-toggle');
        toggle?.setAttribute('aria-expanded', 'false');
        toggle?.removeAttribute('aria-controls');
    });
    shelf.querySelector('.modal-badge-explainer')?.remove();
    if (!item) return;

    const detail = item.querySelector('.modal-badge-detail');
    const title = item.querySelector('.modal-badge-title');
    if (!detail && !title) return;

    item.classList.add('expanded');
    const toggle = item.querySelector('.modal-badge-toggle');
    toggle.setAttribute('aria-expanded', 'true');

    const panel = document.createElement('div');
    panel.className = 'modal-badge-explainer';
    panel.id = 'modalBadgeExplainer';
    panel.setAttribute('role', 'region');
    const heading = document.createElement('strong');
    heading.className = 'modal-badge-explainer-title';
    heading.id = 'modalBadgeExplainerTitle';
    panel.setAttribute('aria-labelledby', heading.id);
    heading.textContent = title ? title.textContent : '';
    panel.appendChild(heading);
    if (detail) {
        const text = document.createElement('span');
        text.className = 'modal-badge-explainer-detail';
        text.textContent = detail.textContent;
        panel.appendChild(text);
    }
    toggle.setAttribute('aria-controls', panel.id);

    const placePanel = () => {
        if (!shelf.isConnected || !item.isConnected || !shelf.getClientRects().length) {
            setModalBadgeExplainer(shelf, null);
            return;
        }
        // Measure natural rows without the old panel reserving a grid row.
        panel.remove();
        const items = Array.from(shelf.querySelectorAll('.modal-badge-item'));
        // Hover/mint transforms move painted bounds, not the shelf's grid row.
        const top = item.offsetTop;
        const rowEnd = items.filter(other => Math.abs(other.offsetTop - top) < 1).pop() || item;
        rowEnd.after(panel);
        const panelRect = panel.getBoundingClientRect();
        const badgeRect = (item.querySelector('.badge-class') || item).getBoundingClientRect();
        panel.style.setProperty('--explainer-caret', `${Math.round(badgeRect.left + badgeRect.width / 2 - panelRect.left)}px`);
    };
    placePanel();
    // Container and font changes can alter row placement without window resize.
    modalBadgeExplainerObserver = new ResizeObserver(() => {
        cancelAnimationFrame(modalBadgeExplainerFrame);
        modalBadgeExplainerFrame = requestAnimationFrame(placePanel);
    });
    modalBadgeExplainerObserver.observe(shelf);
    shelf.querySelectorAll('.modal-badge-item').forEach(badge => modalBadgeExplainerObserver.observe(badge));
}

function getBiomarkerTestDate(value) {
    // Lab dates are calendar dates; ISO midnight must not become the
    // previous day for visitors in timezones west of UTC.
    const date = new Date(value);
    return new Date(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate());
}

function buildBiomarkerHistory(fullAthleteData) {
    const dob = new Date(
        fullAthleteData.DateOfBirth.Year,
        fullAthleteData.DateOfBirth.Month - 1,
        fullAthleteData.DateOfBirth.Day
    );

    // Filter before sorting: a NaN comparator from an invalid date can leave
    // valid readings out of order and change which reading is called latest.
    const chartData = (Array.isArray(fullAthleteData.Biomarkers) ? fullAthleteData.Biomarkers : [])
        .filter(entry => entry && Number.isFinite(biomarkerHistoryTimestamp(entry.Date)) && Object.entries(entry).some(([key, value]) =>
            key !== 'Date' && isBiomarkerHistoryValue(value)))
        .sort((a, b) => biomarkerHistoryTimestamp(a.Date) - biomarkerHistoryTimestamp(b.Date));

    const labels = chartData.map(entry => entry.Date);
    const phenoAges = [];
    const bortzAges = [];
    const chronologicalAges = [];
    const biomarkersRaw = initializeBiomarkersRaw();
    const clockResults = { pheno: [], bortz: [] };

    chartData.forEach(entry => {
        const entryDate = getBiomarkerTestDate(entry.Date);
        const chronoAgeAtEntry = window.calculateAgeAtDate(dob, entryDate);
        chronologicalAges.push(chronoAgeAtEntry);

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
            if (Number.isFinite(computed)) {
                phenoAge = computed;
                clockResults.pheno.push({ date: entry.Date, bio: computed, chrono: chronoAgeAtEntry, values: biomarkerValues });
            }
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
            if (Number.isFinite(computed)) {
                bortzAge = computed;
                clockResults.bortz.push({ date: entry.Date, bio: computed, chrono: chronoAgeAtEntry, values: bortzValues });
            }
        }
        bortzAges.push(bortzAge);
    });

    return { labels, phenoAges, bortzAges, chronologicalAges, biomarkersRaw, clockResults,
        athleteKey: normalizeAthleteSlugForLookup(fullAthleteData.AthleteSlug || fullAthleteData.Name),
        athleteName: fullAthleteData.DisplayName || fullAthleteData.Name };

}

const biomarkerHistoryDateFormat = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' });

function biomarkerHistoryTimestamp(value) {
    if (typeof value !== 'string') return NaN;
    const calendarDate = /^(\d{4}-\d{2}-\d{2})(?:$|T)/.exec(value)?.[1];
    const timestamp = Date.parse(value);
    if (!calendarDate || !Number.isFinite(timestamp)) return NaN;
    const midnight = Date.parse(`${calendarDate}T00:00:00Z`);
    return Number.isFinite(midnight) && new Date(midnight).toISOString().slice(0, 10) === calendarDate ? timestamp : NaN;
}

function formatResultDate(date) {
    return biomarkerHistoryDateFormat.format(new Date(date));
}

function isBiomarkerHistoryValue(value) {
    return (typeof value === 'number' || typeof value === 'string' && value.trim() !== '') && Number.isFinite(Number(value));
}

function resultDecimalPlaces(value) {
    const [significand, exponent = '0'] = String(value).toLowerCase().split('e');
    return Math.max(0, (significand.split('.')[1] || '').length - Number(exponent));
}

function resultDisplayDecimals(value, measure) {
    if (measure.clock) return 1;
    const decimals = resultDecimalPlaces(value);
    if (typeof value !== 'number' || decimals < 12) return decimals;
    // Legacy records retain canonical values, not the input unit or precision.
    // Abbreviate only long tails reproduced EXACTLY by a supported calculator
    // conversion of a short lab reading. Other high precision (including tiny
    // measurements and explicitly supplied decimal strings) stays intact.
    const factors = {
        'Albumin (g/L)': [0.1], 'Creatinine (µmol/L)': [0.0113], 'Glucose (mmol/L)': [18.016],
        'CRP (mg/L)': [0.1], 'Cystatin C (mg/L)': [0.1], 'Vitamin D (nmol/L)': [0.4],
        'ApoA1 (g/L)': [100], 'Urea (mmol/L)': [6, 2.801], 'Cholesterol (mmol/L)': [38.67], 'SHBG (nmol/L)': [0.0347]
    }[measure.key] || [];
    for (const factor of factors) {
        const inverse = 1 / factor;
        for (let precision = 0; precision <= 4; precision++) {
            const input = Number((value * factor).toFixed(precision));
            if (input !== 0 && (input / factor === value || input * inverse === value))
                return resultDecimalPlaces(Number(value.toFixed(2)));
            // Older canonical -> alternate unit -> canonical round trips can
            // add a binary tail too. Keep the short canonical value's own
            // precision rather than imposing two decimals on these readings.
            const canonical = Number(value.toFixed(precision));
            if (canonical !== 0 && [canonical * factor / factor, canonical / factor * factor,
                canonical * inverse / inverse, canonical / inverse * inverse].includes(value))
                return resultDecimalPlaces(canonical);
        }
    }
    return decimals;
}

function formatResultValue(value, measure) {
    if (!isBiomarkerHistoryValue(value)) return '—';
    if (measure.clock) return Number(value).toFixed(1);
    const decimals = resultDisplayDecimals(value, measure);
    return decimals < resultDecimalPlaces(value) ? String(Number(Number(value).toFixed(decimals))) : String(value);
}

function resultUnit(text) {
    const span = document.createElement('span');
    span.className = 'biomarker-history-unit';
    span.textContent = text;
    return span;
}

function disposeBiomarkerHistory() {
    const history = document.getElementById('athlete-biomarkers')?._biomarkerHistory;
    history?.expanded?.chart?.destroy();
    history?.clockHistoryObserver?.disconnect();
    if (history) history.expanded = null;
}

function renderBiomarkerHistory(history, athletes) {
    const section = document.getElementById('athlete-biomarkers');
    if (!section) return;
    disposeBiomarkerHistory();
    section._biomarkerHistory = history;
    history.sourceAthletes = athletes;
    const table = section.querySelector('.biomarker-overview-table');
    table.caption.textContent = `${history.athleteName}'s clock results and biomarker history. The latest available value is shown for each result. Missing readings are dashes. Expand a result to inspect its dated measurements.`;
    const firstDate = history.labels[0];
    const lastDate = history.labels.at(-1);
    section.querySelector('.biomarker-overview-range').textContent = firstDate
        ? (firstDate === lastDate ? formatResultDate(firstDate) : `${formatResultDate(firstDate)} → ${formatResultDate(lastDate)}`) : '';
    const measures = [
        { key: 'bortz', name: 'Bortz Age', unit: 'years', clock: 'bortz', values: history.bortzAges },
        { key: 'pheno', name: 'Pheno Age', unit: 'years', clock: 'pheno', values: history.phenoAges },
        ...Object.keys(history.biomarkersRaw)
            .sort((a, b) => a.localeCompare(b))
            .map(label => {
                const split = label.lastIndexOf(' (');
                return { key: label, name: label.slice(0, split), unit: label.slice(split + 2, -1), values: history.biomarkersRaw[label] };
            })
    ].filter(measure => measure.values.some(isBiomarkerHistoryValue));
    const firstBiomarkerIndex = measures.findIndex(measure => !measure.clock);
    const rows = document.createDocumentFragment();
    measures.forEach((measure, measureIndex) => {
        const measuredIndices = measure.values.flatMap((value, index) => isBiomarkerHistoryValue(value) ? [index] : []);
        const latestIndex = measuredIndices.at(-1);
        const latest = measure.values[latestIndex];
        const row = document.createElement('tr');
        row.className = 'result-summary' + (measure.clock ? ' result-clock'
            : measureIndex === firstBiomarkerIndex && firstBiomarkerIndex > 0 ? ' result-first-biomarker' : '');
        row.dataset.measure = measure.key;
        const accent = measure.clock === 'bortz' ? 'var(--athlete-modal-chart-bortz)' : 'var(--athlete-modal-chart-pheno)';
        row.style.setProperty('--result-accent', accent);
        const detail = document.createElement('tr');
        detail.id = `biomarkerResultDetail-${measureIndex}`;
        detail.className = 'result-detail-row';
        detail.hidden = true;
        detail.style.setProperty('--result-accent', accent);
        const heading = document.createElement('th');
        heading.scope = 'row';
        const button = document.createElement('button');
        button.type = 'button';
        button.textContent = measure.name;
        button.appendChild(resultUnit(measure.unit));
        button.setAttribute('aria-expanded', 'false');
        button.setAttribute('aria-controls', detail.id);
        button.title = `Compare ${measure.name} across LWC`;
        button.disabled = latestIndex == null;
        if (latestIndex != null && history.labels[latestIndex] !== lastDate) {
            const time = document.createElement('time');
            time.className = 'result-measurement-date';
            time.dateTime = history.labels[latestIndex];
            time.textContent = formatResultDate(history.labels[latestIndex]);
            button.appendChild(time);
        }
        button.onclick = () => toggleResultDetail(history, measure, row, detail, button);
        button.onkeydown = event => {
            if (event.key !== 'Escape' || history.expanded?.button !== button) return;
            event.preventDefault();
            event.stopPropagation();
            toggleResultDetail(history, measure, row, detail, button);
        };
        row.onclick = event => {
            if (button.disabled || button.contains(event.target) || !window.getSelection().isCollapsed) return;
            button.focus({ preventScroll: true });
            button.click();
        };
        heading.appendChild(button);
        const sparkline = document.createElement('td');
        sparkline.className = 'biomarker-overview-history';
        if (measure.clock) {
            sparkline.appendChild(createClockHistory(history, measure, measuredIndices));
        } else {
            sparkline.setAttribute('aria-hidden', 'true');
            sparkline.appendChild(createBiomarkerSparkline(history.labels, measure.values));
        }
        const result = document.createElement('td');
        result.className = 'biomarker-overview-result';
        const current = document.createElement('span');
        current.className = 'biomarker-overview-current';
        current.textContent = formatResultValue(latest, measure);
        const arrow = document.createElement('span');
        arrow.className = 'biomarker-overview-arrow';
        arrow.setAttribute('aria-hidden', 'true');
        result.append(current);
        const change = createResultChange(measure, measuredIndices);
        if (change) result.appendChild(change);
        result.appendChild(arrow);
        row.append(heading, sparkline, result);
        rows.append(row, detail);
    });
    table.tBodies[0].replaceChildren(rows);
    const strips = table.querySelectorAll('.clock-history-scroll');
    if (strips.length) {
        history.clockHistoryObserver = new ResizeObserver(entries => entries.forEach(({ target }) =>
            updateClockHistoryOverflow(target.classList.contains('clock-history-scroll') ? target : target.parentElement)));
        strips.forEach(strip => {
            strip.onscroll = () => updateClockHistoryOverflow(strip);
            history.clockHistoryObserver.observe(strip);
            history.clockHistoryObserver.observe(strip.firstElementChild);
        });
    }
}

// Calculate from unrounded canonical readings; format at their displayed precision.
function createResultChange(measure, indices) {
    if (indices.length < 2) return null;
    const first = Number(measure.values[indices[0]]);
    const latest = Number(measure.values[indices.at(-1)]);
    if (!Number.isFinite(first) || !Number.isFinite(latest) || !Number.isFinite(latest - first)) return null;
    const precision = Math.max(resultDisplayDecimals(measure.values[indices[0]], measure), resultDisplayDecimals(measure.values[indices.at(-1)], measure));
    const delta = precision <= 100 ? Number((latest - first).toFixed(precision)) : latest - first;
    const change = document.createElement('span');
    change.className = 'biomarker-overview-change';
    change.dataset.direction = delta > 0 ? 'up' : delta < 0 ? 'down' : 'flat';
    const magnitude = precision <= 12 ? Math.abs(delta).toFixed(precision) : String(Math.abs(delta));
    change.textContent = delta === 0 ? '±0' : `${delta > 0 ? '+' : '−'}${magnitude}`;
    change.setAttribute('role', 'img');
    change.setAttribute('aria-label', `Change since first reading: ${change.textContent} ${measure.unit}`);
    return change;
}

function updateClockHistoryOverflow(strip) {
    const overflow = strip.scrollWidth - strip.clientWidth;
    strip.style.setProperty('--clock-history-left-fade', overflow + strip.scrollLeft > 1 ? '8px' : '0px');
    strip.style.setProperty('--clock-history-right-fade', strip.scrollLeft < -1 ? '8px' : '0px');
    strip.tabIndex = overflow > 1 ? 0 : -1;
    // Keep complete figures at the edge of the scroll window. Opacity retains
    // every dated value in the accessible history while avoiding half a number.
    const bounds = strip.getBoundingClientRect();
    strip.querySelectorAll('.clock-history-reading').forEach(reading => {
        const value = reading.getBoundingClientRect();
        reading.classList.toggle('is-clipped', value.left < bounds.left - .5 || value.right > bounds.right + .5);
    });
}

function createClockHistory(history, measure, indices) {
    const strip = document.createElement('div');
    strip.className = 'clock-history-scroll';
    strip.setAttribute('role', 'group');
    strip.setAttribute('aria-label', `${measure.name} history, oldest to newest`);
    const track = document.createElement('div');
    track.className = 'clock-history-track';
    track.setAttribute('role', 'list');
    indices.forEach((index, position) => {
        const reading = document.createElement('span');
        reading.className = 'clock-history-reading' + (position === indices.length - 1 ? ' is-latest' : '');
        reading.setAttribute('role', 'listitem');
        reading.dataset.date = history.labels[index];
        reading.title = formatResultDate(history.labels[index]);
        reading.textContent = formatResultValue(measure.values[index], measure);
        reading.setAttribute('aria-label', `${reading.title}: ${reading.textContent} years`);
        track.appendChild(reading);
    });
    strip.appendChild(track);
    return strip;
}

function toggleResultDetail(history, measure, row, detail, button) {
    const previous = history.expanded;
    if (previous) {
        previous.chart?.destroy();
        previous.detail.hidden = true;
        previous.button.setAttribute('aria-expanded', 'false');
        previous.row.classList.remove('is-expanded');
        history.expanded = null;
    }
    if (previous?.detail === detail) return;
    detail.hidden = false;
    button.setAttribute('aria-expanded', 'true');
    row.classList.add('is-expanded');
    const cell = document.createElement('td');
    cell.colSpan = 3;
    const body = document.createElement('div');
    body.className = 'result-detail';
    const title = document.createElement('h4');
    title.textContent = `${measure.name} across LWC`;
    body.appendChild(title);
    const points = getResultComparisonPoints(history, measure);
    const legend = document.createElement('p');
    legend.className = 'result-plot-legend';
    const own = document.createElement('span');
    own.className = 'result-legend-athlete';
    own.textContent = history.athleteName;
    const others = document.createElement('span');
    others.className = 'result-legend-others';
    others.textContent = 'Other athletes';
    legend.append(own, others);
    if (measure.clock) {
        const equal = document.createElement('span');
        equal.className = 'result-legend-equal';
        equal.textContent = 'Same age';
        legend.appendChild(equal);
    }
    body.appendChild(legend);
    const count = document.createElement('p');
    count.className = 'result-field-count';
    const athleteCount = new Set(points.map(point => point.athleteKey)).size;
    count.textContent = `${points.length} result${points.length === 1 ? '' : 's'} · ${athleteCount} athlete${athleteCount === 1 ? '' : 's'}`;
    body.appendChild(count);
    const plot = document.createElement('div');
    plot.className = 'result-plot';
    body.append(plot, createResultHistoryTable(history, measure));
    cell.appendChild(body);
    detail.replaceChildren(cell);
    const state = { history, measure, row, detail, button, plot, points, chart: null };
    history.expanded = state;
    drawResultPlot(state);
    body.onkeydown = event => {
        if (event.key !== 'Escape') return;
        event.stopPropagation();
        event.preventDefault();
        toggleResultDetail(history, measure, row, detail, button);
        button.focus({ preventScroll: true });
    };
}

function createResultHistoryTable(history, measure) {
    const table = document.createElement('table');
    table.className = 'biomarker-history-table';
    const caption = table.createCaption();
    caption.textContent = `${history.athleteName}'s ${measure.name} history in ${measure.unit}. Missing results are shown as a dash.`;
    const heading = table.createTHead().insertRow();
    ['Test date', `${measure.name} (${measure.unit})`].forEach(text => {
        const cell = document.createElement('th');
        cell.scope = 'col';
        cell.textContent = text;
        heading.appendChild(cell);
    });
    const body = table.createTBody();
    const latest = measure.values.findLastIndex(isBiomarkerHistoryValue);
    history.labels.forEach((date, index) => {
        const row = body.insertRow();
        row.classList.toggle('is-latest', index === latest);
        const cell = document.createElement('th');
        cell.scope = 'row';
        const time = document.createElement('time');
        time.dateTime = date;
        time.textContent = formatResultDate(date);
        cell.appendChild(time);
        row.appendChild(cell);
        const result = row.insertCell();
        result.textContent = formatResultValue(measure.values[index], measure);
        if (!isBiomarkerHistoryValue(measure.values[index])) result.setAttribute('aria-label', 'Not available');
    });
    return table;
}

function getResultComparisonPoints(history, measure) {
    history.comparisonHistories ||= history.sourceAthletes.map(buildBiomarkerHistory);
    const points = [];
    history.comparisonHistories.forEach(data => {
        const values = measure.clock === 'bortz' ? data.bortzAges
            : measure.clock === 'pheno' ? data.phenoAges : data.biomarkersRaw[measure.key];
        data.labels.forEach((date, index) => {
            const value = values[index];
            if (!isBiomarkerHistoryValue(value) || !Number.isFinite(data.chronologicalAges[index])) return;
            points.push({ date, x: data.chronologicalAges[index], y: Number(value),
                name: data.athleteName, athleteKey: data.athleteKey, isAthlete: data.athleteKey === history.athleteKey, index });
        });
    });
    return points.sort((a, b) => a.x - b.x || a.date.localeCompare(b.date));
}

function historySvgNode(parent, tag, attributes, text) {
    const node = document.createElementNS('http://www.w3.org/2000/svg', tag);
    Object.entries(attributes).forEach(([key, value]) => node.setAttribute(key, String(value)));
    if (text != null) node.textContent = text;
    parent.appendChild(node);
    return node;
}

function createBiomarkerSparkline(labels, values) {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 120 40');
    const dates = labels.map((date, index) => ({ index, timestamp: biomarkerHistoryTimestamp(date) }))
        .filter(date => Number.isFinite(date.timestamp)).sort((a, b) => a.timestamp - b.timestamp);
    const measured = dates.filter(date => isBiomarkerHistoryValue(values[date.index]));
    const numbers = measured.map(date => Number(values[date.index]));
    const min = numbers.length ? Math.min(...numbers) : 0;
    const max = numbers.length ? Math.max(...numbers) : 0;
    const valueScale = Math.max(Math.abs(min), Math.abs(max), 1);
    const start = dates[0]?.timestamp ?? 0;
    const duration = (dates.at(-1)?.timestamp ?? start) - start;
    const xAt = date => duration ? 8 + (date.timestamp - start) / duration * 104 : 60;
    const latest = measured.at(-1)?.index;
    // The whole test timeline sits under every trend, so a short series reads as recent, not misplaced.
    historySvgNode(svg, 'line', { class: 'sparkline-track', x1: 8, y1: 36, x2: 112, y2: 36 });
    dates.forEach(date => {
        const x = xAt(date);
        historySvgNode(svg, 'line', { class: 'sparkline-tick', x1: x, y1: 34, x2: x, y2: 38 });
    });
    const areas = historySvgNode(svg, 'g', { class: 'sparkline-area' });
    const lines = historySvgNode(svg, 'g', {});
    let run = [];
    const closeRun = () => {
        if (run.length > 1) historySvgNode(areas, 'path', {
            d: `M${run[0].x},36 ${run.map(point => `L${point.x},${point.y}`).join(' ')} L${run.at(-1).x},36Z`
        });
        run = [];
    };
    dates.forEach(date => {
        const index = date.index;
        const value = values[index];
        if (!isBiomarkerHistoryValue(value)) { closeRun(); return; }
        const x = xAt(date);
        const y = min === max ? 18 : 30 - (Number(value) / valueScale - min / valueScale) / (max / valueScale - min / valueScale) * 24;
        const previous = run.at(-1);
        if (previous) historySvgNode(lines, 'line', { x1: previous.x, y1: previous.y, x2: x, y2: y });
        historySvgNode(svg, 'circle', { cx: x, cy: y, r: index === latest ? 4 : 2.5,
            class: index === latest ? 'is-latest' : index === measured[0]?.index ? 'is-baseline' : '' });
        run.push({ x, y });
    });
    closeRun();
    return svg;
}

function drawResultPlot(state) {
    const { history, measure, plot, points } = state;
    const canvas = document.createElement('canvas');
    canvas.setAttribute('role', 'img');
    canvas.setAttribute('aria-label', measure.clock
        ? `${measure.name} across LWC: chronological age at the test versus biological age, in years. Highlighted points are ${history.athleteName}'s results. The dashed line is equal biological and chronological age. Exact dated results are in the table below.`
        : `${measure.name} across LWC: chronological age at the test in years versus ${measure.name} in ${measure.unit}. Highlighted points are ${history.athleteName}'s results. Exact dated results are in the table below.`);
    canvas.dataset.clock = measure.clock || '';
    plot.replaceChildren(canvas);
    plot.setAttribute('aria-busy', 'true');
    window.ensureChartJs().then(loaded => {
        if (history.expanded !== state) return;
        plot.removeAttribute('aria-busy');
        if (!loaded) {
            const retry = document.createElement('button');
            retry.type = 'button';
            retry.textContent = 'Retry chart';
            retry.onclick = () => drawResultPlot(state);
            plot.replaceChildren(retry);
            return;
        }
        const style = getComputedStyle(plot);
        const color = token => style.getPropertyValue(token).trim();
        const accent = color(measure.clock === 'bortz' ? '--athlete-modal-chart-bortz' : '--athlete-modal-chart-pheno');
        const tooltip = color('--athlete-modal-chart-tooltip');
        const muted = color('--athlete-modal-muted');
        const ink = color('--athlete-modal-text');
        const own = points.filter(point => point.isAthlete);
        const latestIndex = measure.values.findLastIndex(isBiomarkerHistoryValue);
        const ownStyle = {
            borderColor: accent, borderWidth: 2, pointBorderColor: accent, pointBorderWidth: 2,
            pointBackgroundColor: context => context.raw?.index === latestIndex ? accent : tooltip,
            pointRadius: context => context.raw?.index === latestIndex ? 5.5 : 4,
            pointHoverRadius: 7, pointHoverBorderColor: ink, pointHitRadius: 12, clip: false
        };
        const datasets = [
            { label: history.athleteName, data: own, ...ownStyle, order: 0 },
            { label: 'Other athletes', data: points.filter(point => !point.isAthlete),
                pointBackgroundColor: color('--athlete-modal-subtle'), pointBorderWidth: 0, pointRadius: 2.5, pointHoverRadius: 5,
                pointHoverBorderWidth: 2, pointHoverBorderColor: ink, pointHitRadius: 8, order: 1 }
        ];
        const ages = points.map(point => point.x);
        const low = Math.max(0, Math.floor(Math.min(...ages) / 10) * 10);
        const high = Math.max(low + 10, Math.ceil(Math.max(...ages) / 10) * 10);
        let clockBounds;
        if (measure.clock) {
            clockBounds = {
                min: Math.floor(Math.min(low, ...points.map(point => point.y)) / 10) * 10,
                max: Math.ceil(Math.max(high, ...points.map(point => point.y)) / 10) * 10
            };
            datasets.push({ label: 'Same age', type: 'line', data: [{ x: low, y: low }, { x: high, y: high }],
                borderColor: muted, borderWidth: 1, borderDash: [4, 5], pointRadius: 0, pointHitRadius: 0, order: 2 });
        }
        const compact = () => plot.clientWidth < 360;
        const axis = { type: 'linear', border: { display: false },
            grid: { color: color('--athlete-modal-chart-grid'), drawTicks: false },
            ticks: { color: muted, padding: 8, font: { family: 'Roboto', size: 11 }, maxRotation: 0 },
            title: { display: true, color: muted, font: { family: 'Roboto', size: 11 } } };
        const wrapPlotText = (text, available, font = '600 12px Roboto') => {
            // Browser zoom can leave less room than the usual phone layout.
            // Measure the bold title font (also safely bounds body text) rather
            // than guessing a character count or reducing readable type.
            const context = canvas.getContext('2d');
            context.save();
            context.font = font;
            const lines = [];
            let line = '';
            for (const word of String(text).split(/\s+/)) {
                if (line && context.measureText(line + ' ' + word).width > available) {
                    lines.push(line);
                    line = '';
                }
                for (const character of (line ? ' ' : '') + word) {
                    if (line && context.measureText(line + character).width > available) {
                        lines.push(line.trimEnd());
                        line = '';
                    }
                    line += character;
                }
            }
            if (line) lines.push(line);
            context.restore();
            return lines;
        };
        const titleLines = text => wrapPlotText(text, Math.max(1, plot.clientWidth - 32));
        state.chart = new window.Chart(canvas, {
            type: 'scatter', data: { datasets },
            options: {
                responsive: true, maintainAspectRatio: false, animation: false,
                interaction: { mode: 'nearest', axis: 'xy', intersect: false },
                layout: { padding: { top: 10, right: 10 } },
                plugins: {
                    legend: { display: false },
                    tooltip: { enabled: true, position: 'nearest', displayColors: false,
                        backgroundColor: tooltip, titleColor: ink, bodyColor: ink, borderColor: accent, borderWidth: 1,
                        padding: 10, cornerRadius: 6, caretPadding: 7, titleFont: { family: 'Roboto', size: 12, weight: '600' },
                        bodyFont: { family: 'Roboto', size: 12 },
                        filter: item => item.dataset.label !== 'Same age',
                        callbacks: {
                            title: items => items.length ? titleLines(items[0].raw.name) : '',
                            beforeBody: items => items.length ? formatResultDate(items[0].raw.date) : '',
                            label: item => [
                                ...titleLines(`${measure.name}: ${formatResultValue(item.raw.y, measure)} ${measure.unit}`),
                                ...titleLines(`Chronological age: ${Number(item.raw.x).toFixed(1)} years`)
                            ]
                        }
                    }
                },
                scales: {
                    x: { ...axis, grid: { display: false },
                        min: low, max: high,
                        title: { ...axis.title, text: context => context.chart.width < 180
                            ? ['Chronological', 'age (years)'] : 'Chronological age (years)' },
                        ticks: { ...axis.ticks, maxTicksLimit: compact() ? 3 : 5 } },
                    y: { ...axis, ...(measure.clock ? clockBounds : { grace: '8%' }), ticks: { ...axis.ticks, maxTicksLimit: 6 },
                        title: { ...axis.title, text: `${measure.name} (${measure.unit})` } }
                }
            }
        });
    });
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
        // Keep other image sources working for older data and embedded callers.
        const previewUrl = /^\/athletes\/[\w-]+\/proof_[\w.-]+\.(?:webp|png|jpe?g|bmp|gif)(?:\?|$)/i.test(proofUrl)
            ? proofUrl.replace('/athletes/', '/api/proof-previews/')
            : proofUrl;
        if (previewUrl !== proofUrl) {
            img.dataset.fullSrc = proofUrl;
            img.onerror = () => {
                img.onerror = null;
                img.src = proofUrl;
            };
        }
        img.src = previewUrl;
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
        window.LwcLeaderboardOrder.restore();
        if (athleteResultsReady && renderedLeaderboardSelection !== getLeaderboardSelectionKey()) {
            performFilter({ updateUrl: false });
        } else if (athleteResultsReady) {
            // Back/Forward and rank-anchor changes can update the destination
            // without changing any filters or requiring new rows.
            updateFullLeaderboardLink();
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
const sidebarPanel = sidebar?.querySelector('.sidebar-panel');
const sidebarToggle = document.querySelector('.sidebar-toggle');
const sidebarClose = document.querySelector('.sidebar-close');
const hasLeaderboardSidebar = !!(sidebar && sidebarToggle && sidebarClose);
const mobileDrawerMediaQuery = window.matchMedia('(max-width: 768px), (max-width: 932px) and (max-height: 480px) and (orientation: landscape)');
let leaderboardTableHeightFrame = 0;
let leaderboardTableResizeObserver = null;
let sidebarDrawerCloseTimer = 0;

function isMobileDrawerViewport() {
    return mobileDrawerMediaQuery.matches;
}

function syncSidebarPanelInteractivity() {
    if (!sidebarPanel) return;
    // An explicit opening is interactive immediately, including while enlarged
    // text makes container queries refit the panel's computed layout.
    const open = sidebar.classList.contains('expanded')
        || (!isMobileDrawerViewport()
            && getComputedStyle(sidebarPanel).getPropertyValue('--sidebar-panel-open').trim() === '1');
    sidebarPanel.inert = !open;
    sidebarPanel.setAttribute('aria-hidden', open ? 'false' : 'true');
}

function scheduleLeaderboardTableHeightSync() {
    if (leaderboardTableHeightFrame) return;
    leaderboardTableHeightFrame = requestAnimationFrame(syncLeaderboardTableHeight);
}

function syncLeaderboardTableHeight() {
    leaderboardTableHeightFrame = 0;
    syncSidebarPanelInteractivity();

    const leaderboard = document.querySelector('.leaderboard');
    const table = document.querySelector('.leaderboard--flags > .flag-leaderboard') || document.querySelector('.leaderboard > table');
    if (!leaderboard || !table) return;

    // Complete geometry reads before writing styles. Sidebar measurements must
    // not invalidate every athlete and badge through inherited board variables.
    const headHeight = table.tHead?.getBoundingClientRect().height;
    if (isMobileDrawerViewport()) {
        leaderboard.style.removeProperty('min-height');
        sidebar?.style.removeProperty('--leaderboard-table-height');
        sidebar?.style.removeProperty('--leaderboard-title-height');
        sidebar?.style.removeProperty('--leaderboard-sidebar-viewport-top');
        sidebar?.style.removeProperty('--leaderboard-head-height');
        return;
    }
    const tableHeight = table.getBoundingClientRect().height;
    // Hover/expansion temporarily hides the rail title. Keep its measured
    // footprint so opening the overlay cannot shorten the board beneath it.
    const titleHeight = measureCollapsedTitleHeight()
        ?? (parseFloat(sidebar.style.getPropertyValue('--leaderboard-title-height')) || 0);
    const sidebarTop = Math.max(0, Math.ceil(sidebar.getBoundingClientRect().top));
    if (headHeight > 0) sidebar.style.setProperty('--leaderboard-head-height', `${Math.ceil(headHeight)}px`);
    else sidebar.style.removeProperty('--leaderboard-head-height');
    if (tableHeight > 0) {
        sidebar.style.setProperty('--leaderboard-table-height', `${Math.ceil(tableHeight)}px`);
        sidebar.style.setProperty('--leaderboard-title-height', `${titleHeight}px`);
        leaderboard.style.minHeight = `${Math.max(Math.ceil(tableHeight), titleHeight)}px`;
    }
    // A newly opened panel may start below its sticky position. Size its scroll
    // area to the space actually on screen, then refit as the page scrolls.
    sidebar.style.setProperty('--leaderboard-sidebar-viewport-top', `${sidebarTop}px`);
}

function installLeaderboardTableHeightObserver() {
    const table = document.querySelector('.leaderboard > table');
    if (!table) return;

    if ('ResizeObserver' in window) {
        leaderboardTableResizeObserver = new ResizeObserver(scheduleLeaderboardTableHeightSync);
        leaderboardTableResizeObserver.observe(table);
        if (table.tHead) leaderboardTableResizeObserver.observe(table.tHead);
        const flags = document.getElementById('flagLeaderboard');
        if (flags) leaderboardTableResizeObserver.observe(flags);
        if (sidebarPanel) leaderboardTableResizeObserver.observe(sidebarPanel);
        const title = document.querySelector('.collapsed-title');
        if (title) leaderboardTableResizeObserver.observe(title);
    }

    scheduleLeaderboardTableHeightSync();
}

sidebarToggle?.addEventListener('click', (event) => {
    toggleSidebar({ focusDrawerClose: event.detail === 0 });
});

sidebar?.addEventListener('mouseleave', () => {
    // An explicit close wins over hover until the pointer leaves the rail.
    sidebar.classList.remove('button-collapsed');
    syncSidebarPanelInteractivity();
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
    clearTimeout(sidebarDrawerCloseTimer);
    sidebarDrawerCloseTimer = 0;
    sidebar.inert = isMobileDrawer && !isOpenMobileDrawer;
    if (isOpenMobileDrawer) {
        const wasOpen = document.body.classList.contains('sidebar-drawer-open');
        const bodyWidth = document.body.clientWidth;
        document.body.classList.add('sidebar-drawer-open');
        if (!wasOpen) document.body.style.setProperty('--sidebar-body-width', getLockedBodyWidth(bodyWidth));
        document.body.classList.remove('sidebar-drawer-closing');
    } else if (isMobileDrawer && document.body.classList.contains('sidebar-drawer-open')) {
        document.body.classList.add('sidebar-drawer-closing');
        sidebarDrawerCloseTimer = finishAfterTransition(sidebar, 'transform', () => {
            sidebarDrawerCloseTimer = 0;
            document.body.classList.remove('sidebar-drawer-open', 'sidebar-drawer-closing');
            document.body.style.removeProperty('--sidebar-body-width');
        });
    } else {
        document.body.classList.remove('sidebar-drawer-open', 'sidebar-drawer-closing');
        document.body.style.removeProperty('--sidebar-body-width');
    }
    sidebar.setAttribute('aria-hidden', isMobileDrawer && !isOpenMobileDrawer ? 'true' : 'false');
    syncSidebarPanelInteractivity();
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

    openSidebar();
}

function openSidebar(options = {}) {
    if (!hasLeaderboardSidebar) return;
    const isMobileDrawer = isMobileDrawerViewport();
    const drawerScrollX = window.scrollX;
    const drawerScrollY = window.scrollY;

    scheduleLeaderboardTableHeightSync();
    sidebar.classList.remove('button-collapsed', 'collapsed');

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
            window.scrollTo({ left: drawerScrollX, top: drawerScrollY, behavior: 'instant' });
        });
    }
}

function closeSidebar(options = {}) {
    if (!hasLeaderboardSidebar) return;
    sidebar.classList.remove('expanded');
    sidebar.setAttribute('aria-hidden', 'true');
    sidebarToggle.classList.remove('active');
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
}

if (hasLeaderboardSidebar) {
    syncSidebarDrawerBackdrop();
    installLeaderboardTableHeightObserver();
    sidebar.addEventListener('mouseenter', () => {
        syncSidebarPanelInteractivity();
        scheduleLeaderboardTableHeightSync();
    });
    window.addEventListener('scroll', scheduleLeaderboardTableHeightSync, { passive: true });
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
let sidebarSwipeEligible = false;

document.addEventListener('touchstart', (e) => {
    sidebarSwipeEligible = false;
    if (!hasLeaderboardSidebar) return;
    if (modal.style.display === "block") return;
    if (e.touches.length !== 1) return;
    // Horizontal scrollers own their gestures, including the Swag carousel.
    // A page-level right swipe must not open the filter drawer over them.
    if (e.composedPath().some(node => node instanceof HTMLElement
        && node.scrollWidth > node.clientWidth + 1
        && ['auto', 'scroll'].includes(getComputedStyle(node).overflowX))) return;

    sidebarSwipeEligible = true;
    startX = e.changedTouches[0].clientX;
    startY = e.changedTouches[0].clientY;
}, false);

document.addEventListener('touchend', (e) => {
    if (!sidebarSwipeEligible) return;
    sidebarSwipeEligible = false;
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

document.addEventListener('touchcancel', () => { sidebarSwipeEligible = false; }, { passive: true });

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

    // These rows are controlled by their native hidden state. Reading computed
    // styles here flushed the entire profile three times during each hydration.
    const firstVisibleRow = groupedRows.find(row => !row.hidden);
    groupedRows.forEach(row => row.classList.toggle('athlete-info-group-first', row === firstVisibleRow));
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
        // Guess submissions update this object in place, including eligibility.
        // Do not reuse the prior Crowd cohort or its displayed ordering values.
        leaderboardOrderingSnapshot = null;
        if (athleteData.orderValues) {
            const values = athleteData.orderValues;
            const hasCrowd = athleteData.crowdCount > 0 && Number.isFinite(age);
            values.crowd = hasCrowd ? age : null;
            values.crowdCount = athleteData.crowdCount;
            values.crowdReduction = hasCrowd && Number.isFinite(values.age) ? age - values.age : null;
        }
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
        crowdContainer.hidden = true;
        notEmptyCrowdContainer.hidden = true;
        document.getElementById('crowdAge').textContent = '';
        document.getElementById('crowdCount').textContent = '0';
    } else {
        crowdContainer.hidden = false;
        notEmptyCrowdContainer.hidden = false;
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

    const guess = guessData?.value != null ? Number(guessData.value) : NaN;
    if (Number.isFinite(guess)) {
        yourGuessContainer.hidden = false;

        // grab the displayed chrono & crowd ages
        const chrono = parseInt(document.getElementById('chronologicalAge').textContent, 10);
        const crowd = parseFloat(document.getElementById('crowdAge').textContent);

        // compute how far you and the crowd are from the true age
        const diffYou = Math.abs(guess - chrono);
        const diffCrowd = Math.abs(crowd - chrono);

        // collect any matching icons
        const iconClasses = [];
        if (diffYou === 0) iconClasses.push('fa-bullseye');
        if (!document.getElementById('crowdAgeContainer').hidden && diffYou < diffCrowd) iconClasses.push('fa-trophy');
        if (guessData.first) iconClasses.push('fa-medal');

        // build and render
        const iconsHTML = iconClasses.map(c => `<i class="fa ${c}" aria-hidden="true"></i>`).join(' ');
        yourGuessSpan.innerHTML = `${iconsHTML} <span class="age-board-figure">${guess}</span> <span class="unit">years</span>${guessData.first ? '<span class="age-board-guess-note"> (first to guess)</span>' : ''}`;

        // apply stamp to the value span only so the row stays a normal table row
        yourGuessSpan.classList.remove('first-guess-stamp', 'primary-stamp', 'golden-stamp');
        if (diffYou === 0) {
            yourGuessSpan.classList.add('golden-stamp');
        } else if (!document.getElementById('crowdAgeContainer').hidden && diffYou < diffCrowd) {
            yourGuessSpan.classList.add('primary-stamp');
        } else if (guessData.first) {
            yourGuessSpan.classList.add('first-guess-stamp');
        }

    } else {
        // hide if no guess
        yourGuessContainer.hidden = true;
    }
    updateAthleteInfoGrouping();
}

// Keep the small public surface used by page bootstraps, Guess My Age, and
// existing browser tests while the controller itself remains isolated.
window.LoadLeaderboard = LoadLeaderboard;
window.getLeaderboardPeek = name => {
    const athlete = athleteResults.find(item => item.name === name);
    if (!athlete) return null;
    return Object.freeze({
        chronologicalAge: athlete.chronologicalAge,
        lowestBortzAge: athlete.lowestBortzAge,
        chronoAtLowestBortzAge: athlete.chronoAtLowestBortzAge,
        lowestPhenoAge: athlete.lowestPhenoAge,
        chronoAtLowestPhenoAge: athlete.chronoAtLowestPhenoAge,
        crowdAge: athlete.crowdAge,
        crowdCount: athlete.crowdCount,
        placements: athlete.placements
    });
};
// The podium stage and rank-change cues read the same standing the row shows.
window.getLeaderboardStanding = name => {
    const athlete = athleteResults.find(item => item.name === name);
    if (!athlete) return null;
    return Object.freeze({
        rank: athlete.rank,
        lastMonthRank: Number.isFinite(athlete.lastMonthRank) ? athlete.lastMonthRank : null,
        isNew: Boolean(athlete.isNew),
        comparable: athleteResultsReady && renderedStandingsComparable,
        flag: athlete.canonicalFlag || ''
    });
};
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
