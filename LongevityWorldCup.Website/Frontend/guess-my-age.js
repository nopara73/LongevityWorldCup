const guessMyAgeAssets = document.currentScript.dataset;
/* === Guess-My-Age bindings for the new widget === */
const gmaRange = document.getElementById('gmaRange');
const gmaDefaultValue = gmaRange.value;

const gmaBubble = document.getElementById('gmaBubble');
const gmaCard = document.querySelector('.gma-card');
const gmaSliderWrap = gmaCard.querySelector('.gma-slider-wrap');

const heading = gmaCard.querySelector('.gma-heading');
const chronoHeading = gmaCard.querySelector('.chrono-age-heading');
const gmaStatus = document.getElementById('gmaStatus');

const modalContent = document.querySelector('#detailsModal .modal-content');
const gmaActions = gmaCard.querySelector('.gma-actions');
const gmaPayoffRegion = document.getElementById('gmaPayoffRegion');
const GMA_RESULT_PRELUDE_MS = 1980;
const GMA_MAX_TRAVEL_MS = 7000;
const GMA_EXACT_JACKPOT_MS = 15000;
const GMA_EXACT_WAVE_COUNT = 30;
const GMA_EXACT_CONFETTI_CAP = 2200;
const GMA_EXACT_TIME_ICON_CAP = 420;
const GMA_EXACT_CANVAS_PIXEL_CAP = 3000000;
const GMA_EXACT_WAVE_DELAYS_MS = Object.freeze([
    0, 1300, 2400, 3350, 4200,
    4950, 5620, 6230, 6790, 7300, 7770, 8210, 8620,
    9000, 9360, 9700, 10020, 10320, 10600, 10870, 11120, 11360, 11590, 11810,
    12020, 12220, 12410, 12590, 12760, 13000
]);
const GMA_TROLL_REDIRECT_MS = 2000;
const GMA_TROLL_RECOVERY_MS = 2000;
let gmaWasGuessMode = modalContent.classList.contains('guess-mode');
let gmaObservedAthleteSlug = modalContent.dataset.athleteSlug || '';
let gmaObservedProfileImageId = modalContent.dataset.profileImageId || '';
let gmaPresentationGeneration = 0;
let cancelCurrentGmaReveal = null;
let gmaExitTimer = 0;
let gmaExactJackpotController = null;
let gmaTrollController = null;

function cancelGmaPresentation() {
    gmaPresentationGeneration += 1;
    cancelGmaExactJackpot();
    cancelGmaTrollTakeover();

    const cancelReveal = cancelCurrentGmaReveal;
    cancelCurrentGmaReveal = null;
    if (cancelReveal) cancelReveal();

    if (gmaExitTimer) {
        window.clearTimeout(gmaExitTimer);
        gmaExitTimer = 0;
    }
}

function beginGmaPresentation(athleteSlug, profileImageId) {
    cancelGmaPresentation();
    return {
        athleteSlug,
        profileImageId,
        generation: gmaPresentationGeneration
    };
}

function isCurrentGmaPresentation(presentation) {
    return presentation.generation === gmaPresentationGeneration
        && modalContent.classList.contains('guess-mode')
        && modalContent.dataset.athleteSlug === presentation.athleteSlug
        && modalContent.dataset.profileImageId === presentation.profileImageId;
}

// whenever the modal’s class list changes, if we just opened “guess-mode”,
// remove any lingering hide classes so buttons reappear
const resetObserver = new MutationObserver(mutations => {
    const isGuessMode = modalContent.classList.contains('guess-mode');
    const athleteSlug = modalContent.dataset.athleteSlug || '';
    const profileImageId = modalContent.dataset.profileImageId || '';
    const guessModeWasAbsentDuringBatch = mutations.some(mutation =>
        mutation.attributeName === 'class'
        && !String(mutation.oldValue || '').split(/\s+/).includes('guess-mode'));
    const openedOrChangedAthlete = isGuessMode
        && (!gmaWasGuessMode
            || athleteSlug !== gmaObservedAthleteSlug
            || profileImageId !== gmaObservedProfileImageId
            || guessModeWasAbsentDuringBatch);

    if (openedOrChangedAthlete) {
        cancelGmaPresentation();
        setGuessMyAgeParam(true);
        // hide the chrono prompt again
        chronoHeading.style.display = 'none';
        // restore the original prompt
        heading.style.display = '';
        chronoHeading.classList.remove('is-revealing');
        gmaStatus.hidden = true;
        gmaStatus.classList.remove('gma-status--semantic');
        gmaStatus.textContent = '';
        gmaCard.removeAttribute('aria-busy');
        gmaActions.classList.remove('gma-actions-hide');
        gmaActions.inert = false;
        modalContent.classList.remove('gma-result-ready');
        gmaCard.classList.remove('gma-done');
        gmaCard.style.removeProperty('--gma-exit-height');
        gmaCard.classList.remove('is-submitting', 'is-revealing');

        gmaBubble.classList.remove('gma-bubble-inactive');

        const oldRealBubble = document.getElementById('gmaRealBubble');
        if (oldRealBubble) oldRealBubble.remove();

        gmaRange.value = gmaDefaultValue;
        gmaRange.disabled = false;
        gmaRange.style.pointerEvents = '';
        setGmaRangeResultState(false);
        gmaSubmitBtn.disabled = false;
        syncRange();
        gmaRange.focus({ preventScroll: true });

        gmaCard.classList.remove('celebrate-exact', 'celebrate-better');
        clearGmaEphemera();
    } else if (!isGuessMode && gmaWasGuessMode) {
        cancelGmaPresentation();
        clearGmaEphemera();
        setGuessMyAgeParam(false);
    }

    gmaWasGuessMode = isGuessMode;
    gmaObservedAthleteSlug = athleteSlug;
    gmaObservedProfileImageId = profileImageId;
});

resetObserver.observe(modalContent, { attributes: true, attributeOldValue: true });

function gmaGeometryForAge(age) {
    const min = +gmaRange.min;
    const max = +gmaRange.max;
    const clampedAge = clampGmaAge(age);
    const ratio = max === min ? 0 : (clampedAge - min) / (max - min);
    const rangeRect = gmaRange.getBoundingClientRect();
    const wrapRect = gmaSliderWrap.getBoundingClientRect();
    const thumbSize = Number.parseFloat(
        getComputedStyle(gmaRange).getPropertyValue('--gma-thumb-size')) || 64;

    if (rangeRect.width <= 0 || wrapRect.width <= 0) {
        return {
            bubbleLeft: null,
            fillPercent: ratio * 100
        };
    }

    const thumbCenter = (thumbSize / 2)
        + (ratio * Math.max(0, rangeRect.width - thumbSize));
    return {
        bubbleLeft: (rangeRect.left - wrapRect.left) + thumbCenter,
        fillPercent: (thumbCenter / rangeRect.width) * 100
    };
}

function positionGmaBubble(element, age) {
    const geometry = gmaGeometryForAge(age);
    element.dataset.age = String(age);
    element.style.left = geometry.bubbleLeft == null
        ? `${geometry.fillPercent}%`
        : `${geometry.bubbleLeft}px`;
    return geometry;
}

function setGmaRangeProgress(age) {
    const geometry = gmaGeometryForAge(age);
    gmaRange.dataset.visualAge = String(age);
    gmaRange.style.setProperty('--percent', `${geometry.fillPercent}%`);
}

function syncRange() {
    const val = +gmaRange.value;
    gmaBubble.textContent = val;
    positionGmaBubble(gmaBubble, val);
    setGmaRangeProgress(val);
}

function setGmaRangeResultState(isResult) {
    if (isResult) {
        gmaRange.setAttribute('aria-hidden', 'true');
        gmaRange.inert = true;
        return;
    }

    gmaRange.removeAttribute('aria-hidden');
    gmaRange.inert = false;
}

gmaRange.addEventListener('input', syncRange);
syncRange();   // initialise bubble

function prefersReducedGmaMotion() {
    return typeof window.matchMedia === 'function'
        && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

function clampGmaAge(age) {
    return Math.min(Math.max(age, +gmaRange.min), +gmaRange.max);
}

function positionGmaRevealBubble(realBubble, age) {
    const clampedAge = clampGmaAge(age);
    const displayedAge = Math.round(clampedAge);
    realBubble.textContent = displayedAge;
    positionGmaBubble(realBubble, clampedAge);
    gmaRange.value = displayedAge;
    setGmaRangeProgress(clampedAge);
}

function syncGmaGeometry() {
    const submittedAge = Number.parseFloat(gmaBubble.dataset.age || gmaBubble.textContent);
    if (Number.isFinite(submittedAge)) positionGmaBubble(gmaBubble, submittedAge);

    const visualAge = Number.parseFloat(gmaRange.dataset.visualAge || gmaRange.value);
    if (Number.isFinite(visualAge)) setGmaRangeProgress(visualAge);

    const realBubble = document.getElementById('gmaRealBubble');
    if (!realBubble) return;
    const revealedAge = Number.parseFloat(realBubble.dataset.age || realBubble.textContent);
    if (Number.isFinite(revealedAge)) positionGmaBubble(realBubble, revealedAge);
}

window.addEventListener('resize', () => window.requestAnimationFrame(syncGmaGeometry), { passive: true });

function startGmaResultPrelude(presentation) {
    heading.style.display = 'none';
    chronoHeading.style.display = 'inline-block';
    chronoHeading.classList.remove('is-revealing');
    void chronoHeading.offsetWidth;
    chronoHeading.classList.add('is-revealing');

    const isImmediate = prefersReducedGmaMotion()
        || (typeof window.matchMedia === 'function'
            && window.matchMedia('(max-width: 768px)').matches);
    const duration = isImmediate
        ? 0
        : modalContent.classList.contains('gma-fast')
            ? 300
            : GMA_RESULT_PRELUDE_MS;
    if (duration === 0) return Promise.resolve(isCurrentGmaPresentation(presentation));

    return new Promise(resolve => {
        let preludeTimer = 0;
        let isFinished = false;
        const motionQuery = typeof window.matchMedia === 'function'
            ? window.matchMedia('(prefers-reduced-motion: reduce)')
            : null;

        function clearMotionListener() {
            if (!motionQuery) return;
            if (typeof motionQuery.removeEventListener === 'function') {
                motionQuery.removeEventListener('change', handleMotionChange);
            } else if (typeof motionQuery.removeListener === 'function') {
                motionQuery.removeListener(handleMotionChange);
            }
        }

        function finishPrelude(shouldComplete) {
            if (isFinished) return;
            isFinished = true;
            if (preludeTimer) window.clearTimeout(preludeTimer);
            clearMotionListener();
            if (cancelCurrentGmaReveal === cancelPrelude) cancelCurrentGmaReveal = null;
            resolve(shouldComplete && isCurrentGmaPresentation(presentation));
        }

        function cancelPrelude() {
            finishPrelude(false);
        }

        function handleMotionChange(event) {
            if (event.matches) finishPrelude(true);
        }

        cancelCurrentGmaReveal = cancelPrelude;
        if (motionQuery) {
            if (typeof motionQuery.addEventListener === 'function') {
                motionQuery.addEventListener('change', handleMotionChange);
            } else if (typeof motionQuery.addListener === 'function') {
                motionQuery.addListener(handleMotionChange);
            }
        }
        preludeTimer = window.setTimeout(() => finishPrelude(true), duration);
    });
}

function animateActualAgeReveal(realBubble, startAge, actualAge, presentation) {
    if (!isCurrentGmaPresentation(presentation)) return Promise.resolve(false);

    const roundedStart = Math.round(clampGmaAge(startAge));
    const roundedActual = Math.round(clampGmaAge(actualAge));
    positionGmaRevealBubble(realBubble, roundedStart);
    realBubble.dataset.revealPhase = 'staged';

    if (prefersReducedGmaMotion()) {
        positionGmaRevealBubble(realBubble, roundedActual);
        realBubble.classList.add('is-settled');
        realBubble.dataset.revealPhase = 'settled';
        return Promise.resolve(true);
    }

    const distance = Math.abs(roundedActual - roundedStart);
    const isExact = distance === 0;
    let detourAge = roundedActual;
    if (isExact) {
        const roomBelow = roundedActual - +gmaRange.min;
        const roomAbove = +gmaRange.max - roundedActual;
        detourAge = clampGmaAge(roundedActual + (roomAbove >= roomBelow ? 50 : -50));
    } else if (distance < 50) {
        const detourDistance = 50 - distance;
        detourAge = clampGmaAge(
            roundedStart + (Math.sign(roundedActual - roundedStart) * detourDistance));
    }

    const targets = detourAge !== roundedActual
        ? [Math.round(detourAge), roundedActual]
        : [roundedActual];
    const segments = targets.map((target, index) => ({
        start: index === 0 ? roundedStart : targets[index - 1],
        end: target,
        phase: index === 0 && targets.length > 1 ? 'detour' : targets.length > 1 ? 'return' : 'direct'
    }));
    const totalSteps = segments.reduce(
        (total, segment) => total + Math.abs(segment.end - segment.start),
        0);
    const travelBudget = Math.min(
        GMA_MAX_TRAVEL_MS,
        Math.max(3200, 1900 + (totalSteps * 55)));
    const delayWeights = Array.from(
        { length: Math.max(totalSteps, 1) },
        (_, index) => 0.3 + (0.7 * Math.pow((index + 1) / Math.max(totalSteps, 1), 2)));
    const weightTotal = delayWeights.reduce((total, weight) => total + weight, 0);

    realBubble.dataset.travelSteps = String(totalSteps);
    realBubble.dataset.travelBudget = String(travelBudget);

    return new Promise(resolve => {
        let revealTimer = 0;
        let revealFallbackTimer = 0;
        let isFinished = false;
        let segmentIndex = 0;
        let currentAge = roundedStart;
        let completedSteps = 0;
        const motionQuery = typeof window.matchMedia === 'function'
            ? window.matchMedia('(prefers-reduced-motion: reduce)')
            : null;

        function clearMotionListener() {
            if (!motionQuery) return;
            if (typeof motionQuery.removeEventListener === 'function') {
                motionQuery.removeEventListener('change', handleMotionChange);
            } else if (typeof motionQuery.removeListener === 'function') {
                motionQuery.removeListener(handleMotionChange);
            }
        }

        function finishReveal(shouldSettle) {
            if (isFinished) return;
            isFinished = true;
            if (revealTimer) window.clearTimeout(revealTimer);
            if (revealFallbackTimer) window.clearTimeout(revealFallbackTimer);
            clearMotionListener();
            if (cancelCurrentGmaReveal === cancelReveal) cancelCurrentGmaReveal = null;

            const canSettle = shouldSettle && isCurrentGmaPresentation(presentation);
            if (canSettle) {
                positionGmaRevealBubble(realBubble, roundedActual);
                realBubble.classList.remove('is-travelling', 'is-returning');
                realBubble.classList.add('is-settled');
                realBubble.dataset.revealPhase = 'settled';
            }
            resolve(canSettle);
        }

        function cancelReveal() {
            finishReveal(false);
        }

        function handleMotionChange(event) {
            if (event.matches) finishReveal(true);
        }

        function scheduleNextTick() {
            if (!isCurrentGmaPresentation(presentation)) {
                finishReveal(false);
                return;
            }
            if (prefersReducedGmaMotion()) {
                finishReveal(true);
                return;
            }

            const segment = segments[segmentIndex];
            if (!segment) {
                finishReveal(true);
                return;
            }
            if (currentAge === segment.end) {
                segmentIndex += 1;
                scheduleNextTick();
                return;
            }

            const step = Math.sign(segment.end - currentAge);
            const delay = travelBudget * (delayWeights[completedSteps] / weightTotal);
            revealTimer = window.setTimeout(() => {
                revealTimer = 0;
                currentAge += step;
                completedSteps += 1;
                realBubble.dataset.revealPhase = segment.phase;
                realBubble.classList.toggle('is-returning', segment.phase === 'return');
                positionGmaRevealBubble(realBubble, currentAge);
                scheduleNextTick();
            }, delay);
        }

        cancelCurrentGmaReveal = cancelReveal;
        if (motionQuery) {
            if (typeof motionQuery.addEventListener === 'function') {
                motionQuery.addEventListener('change', handleMotionChange);
            } else if (typeof motionQuery.addListener === 'function') {
                motionQuery.addListener(handleMotionChange);
            }
        }
        realBubble.classList.add('is-travelling');
        realBubble.dataset.revealPhase = segments[0]?.phase || 'direct';
        revealFallbackTimer = window.setTimeout(
            () => finishReveal(true),
            GMA_MAX_TRAVEL_MS + 200);
        scheduleNextTick();
    });
}

function showGmaReaction(reaction) {
    if (prefersReducedGmaMotion()) return;
    const portrait = document.getElementById('modalProfilePic');
    if (!portrait) return;

    const rect = portrait.getBoundingClientRect();
    const reactionElement = document.createElement('div');
    reactionElement.className = `gma-reaction gma-reaction--${reaction}`;
    reactionElement.setAttribute('aria-hidden', 'true');
    reactionElement.textContent = reaction === 'exact' ? '👀' : reaction === 'younger' ? '🙌' : '😭';
    const isNarrowPhone = window.innerWidth <= 420;
    const desiredX = isNarrowPhone
        ? rect.left - Math.min(30, rect.width * 0.12)
        : rect.right + Math.min(96, rect.width * 0.26);
    const desiredY = rect.top + (rect.height * (isNarrowPhone ? 0.28 : 0.44));
    reactionElement.dataset.placement = 'beside-portrait';
    document.body.appendChild(reactionElement);

    let reactionX = Math.min(window.innerWidth - 72, desiredX);
    let reactionY = desiredY;
    if (isNarrowPhone) {
        // Clamp the complete animated glyph, not just its anchor. The reaction reaches
        // 1.16x scale with a small rotation, so 1.25x is a safe visual envelope.
        const horizontalInset = Math.min(
            window.innerWidth / 2,
            (reactionElement.offsetWidth * 1.25 / 2) + 8);
        const verticalInset = Math.min(
            window.innerHeight / 2,
            (reactionElement.offsetHeight * 1.25) + 8);
        reactionX = Math.min(
            window.innerWidth - horizontalInset,
            Math.max(horizontalInset, desiredX));
        reactionY = Math.min(
            window.innerHeight - verticalInset,
            Math.max(verticalInset, desiredY));
    }

    reactionElement.style.setProperty('--gma-reaction-x', `${reactionX}px`);
    reactionElement.style.setProperty('--gma-reaction-y', `${reactionY}px`);
    reactionElement.addEventListener('animationend', () => reactionElement.remove(), { once: true });
    window.setTimeout(() => reactionElement.remove(), 2200);
}

function cancelGmaTrollTakeover() {
    gmaTrollController?.cancel();
}

function gmaTriggerTrollAnimation() {
    const presentation = beginGmaPresentation(
        modalContent.dataset.athleteSlug,
        modalContent.dataset.profileImageId);
    const previousPosition = modalContent.style.position;
    const athleteProfile = document.getElementById('athlete-profile');
    const profileWasInert = athleteProfile?.inert ?? false;
    const cardWasInert = gmaCard.inert;
    let redirectTimer = 0;
    let recoveryTimer = 0;
    let cancelled = false;

    modalContent.style.position = 'relative';
    modalContent.classList.remove('gma-result-ready');
    if (athleteProfile) athleteProfile.inert = true;
    gmaCard.inert = true;

    const trollDiv = document.createElement('div');
    trollDiv.id = 'gmaTrollfaceContainer';
    trollDiv.className = 'gma-trollface-container';
    trollDiv.setAttribute('role', 'status');
    trollDiv.setAttribute('aria-label', 'Trollface. Redirecting to the Rickroll.');
    trollDiv.tabIndex = -1;

    const trollImg = document.createElement('img');
    trollImg.src = guessMyAgeAssets.trollface;
    trollImg.alt = 'Trollface';
    trollImg.setAttribute('aria-hidden', 'true');
    trollDiv.appendChild(trollImg);
    modalContent.appendChild(trollDiv);
    trollDiv.focus({ preventScroll: true });

    const keepTrollFocus = event => {
        if (event.key !== 'Tab') return;
        event.preventDefault();
        trollDiv.focus({ preventScroll: true });
    };
    trollDiv.addEventListener('keydown', keepTrollFocus);

    const cancel = () => {
        if (cancelled) return;
        cancelled = true;
        if (redirectTimer) window.clearTimeout(redirectTimer);
        if (recoveryTimer) window.clearTimeout(recoveryTimer);
        trollDiv.removeEventListener('keydown', keepTrollFocus);
        trollDiv.remove();
        if (athleteProfile) athleteProfile.inert = profileWasInert;
        gmaCard.inert = cardWasInert;
        modalContent.style.position = previousPosition;
        if (gmaTrollController?.cancel === cancel) gmaTrollController = null;
    };

    gmaTrollController = { cancel };
    redirectTimer = window.setTimeout(() => {
        redirectTimer = 0;
        if (!isCurrentGmaPresentation(presentation)) {
            cancel();
            return;
        }

        window.location.href = 'https://www.youtube.com/watch?v=dQw4w9WgXcQ';
        recoveryTimer = window.setTimeout(() => {
            if (!isCurrentGmaPresentation(presentation)) {
                cancel();
                return;
            }

            cancel();
            gmaRange.focus({ preventScroll: true });
        }, GMA_TROLL_RECOVERY_MS);
    }, GMA_TROLL_REDIRECT_MS);
}

function gmaJackpotUnit(index, salt) {
    const value = Math.sin(((index + 1) * 91.713) + (salt * 37.119)) * 43758.5453;
    return value - Math.floor(value);
}

function cancelGmaExactJackpot() {
    gmaExactJackpotController?.cancel();
}

function launchGmaExactJackpot(presentation) {
    if (document.hidden
        || prefersReducedGmaMotion()
        || !isCurrentGmaPresentation(presentation)) return;

    cancelGmaExactJackpot();

    const overlay = document.createElement('div');
    overlay.className = 'gma-exact-jackpot';
    overlay.setAttribute('aria-hidden', 'true');
    overlay.dataset.gmaKind = 'exact-jackpot';
    overlay.dataset.waveCount = String(GMA_EXACT_WAVE_COUNT);
    overlay.dataset.wavesFired = '0';
    overlay.dataset.duration = String(GMA_EXACT_JACKPOT_MS);
    overlay.dataset.phase = 'opening';

    const canvas = document.createElement('canvas');
    canvas.className = 'gma-exact-confetti-canvas';
    canvas.setAttribute('aria-hidden', 'true');
    canvas.dataset.gmaMotif = 'confetti';
    canvas.dataset.particleCap = String(GMA_EXACT_CONFETTI_CAP);
    canvas.dataset.activeParticles = '0';
    canvas.dataset.totalSpawned = '0';
    canvas.dataset.burstsFired = '0';
    overlay.appendChild(canvas);

    const topMarquee = document.createElement('div');
    topMarquee.className = 'gma-exact-jackpot-marquee gma-exact-jackpot-marquee--top';
    topMarquee.dataset.gmaMotif = 'victory-ribbon';
    topMarquee.textContent = '🎯 PERFECT GUESS • ZERO YEARS OFF • TEMPORAL GENIUS • PERFECT GUESS • ZERO YEARS OFF • TEMPORAL GENIUS • ';
    overlay.appendChild(topMarquee);

    const bottomMarquee = document.createElement('div');
    bottomMarquee.className = 'gma-exact-jackpot-marquee gma-exact-jackpot-marquee--bottom';
    bottomMarquee.dataset.gmaMotif = 'victory-ribbon';
    bottomMarquee.textContent = '⏱️ ABSOLUTE TIME LORD • CONFETTI CANNONS ENGAGED • NO NOTES • ABSOLUTE TIME LORD • CONFETTI CANNONS ENGAGED • ';
    overlay.appendChild(bottomMarquee);

    const copy = document.createElement('div');
    copy.className = 'gma-exact-jackpot-copy';
    copy.dataset.gmaMotif = 'jackpot-copy';

    const kicker = document.createElement('span');
    kicker.className = 'gma-exact-jackpot-kicker';
    kicker.textContent = '🎯 perfect guess 🎯';
    const title = document.createElement('strong');
    title.className = 'gma-exact-jackpot-title';
    title.textContent = 'BULLSEYE!!!';
    const subtitle = document.createElement('span');
    subtitle.className = 'gma-exact-jackpot-subtitle';
    subtitle.textContent = 'ZERO YEARS OFF · ABSOLUTE TIME LORD';
    copy.append(kicker, title, subtitle);
    overlay.appendChild(copy);

    const clockNames = [
        'clock',
        'stopwatch',
        'history',
        'hourglass-start',
        'hourglass-half',
        'hourglass-end',
        'calendar',
        'calendar-check',
        'calendar-alt'
    ];
    const clockColors = ['#ffd83d', '#fff7b2', '#ff4e91', '#19c3d1', '#ff8a00'];
    const timeIconCount = window.innerWidth <= 540
        ? Math.min(240, GMA_EXACT_TIME_ICON_CAP)
        : GMA_EXACT_TIME_ICON_CAP;
    const iconFragment = document.createDocumentFragment();
    for (let index = 0; index < timeIconCount; index++) {
        const icon = document.createElement('i');
        icon.className = `gma-exact-time-icon fas fa-${clockNames[index % clockNames.length]}`;
        icon.setAttribute('aria-hidden', 'true');
        icon.dataset.gmaMotif = 'clock-rain';
        const iconWave = index % GMA_EXACT_WAVE_COUNT;
        icon.dataset.gmaWave = String(iconWave);
        icon.style.setProperty('--gma-time-x', `${2 + (gmaJackpotUnit(index, 1) * 96)}%`);
        icon.style.setProperty('--gma-time-size', `${1.15 + (gmaJackpotUnit(index, 2) * 2.65)}rem`);
        icon.style.setProperty('--gma-time-drift', `${-12 + (gmaJackpotUnit(index, 3) * 24)}vw`);
        icon.style.setProperty('--gma-time-rotation', `${index % 2 === 0 ? 420 : -520}deg`);
        icon.style.setProperty('--gma-time-color', clockColors[index % clockColors.length]);
        icon.style.setProperty(
            '--gma-time-duration',
            `calc(var(--lwc-duration-normal, 220ms) * ${14 + Math.floor(gmaJackpotUnit(index, 4) * 10)})`);
        icon.style.setProperty(
            '--gma-time-delay',
            `${Math.round(GMA_EXACT_WAVE_DELAYS_MS[iconWave] + (gmaJackpotUnit(index, 5) * 380))}ms`);
        iconFragment.appendChild(icon);
    }
    overlay.appendChild(iconFragment);

    const targetElements = [];
    const targetFragment = document.createDocumentFragment();
    const targetCount = 60;
    for (let index = 0; index < targetCount; index++) {
        const target = document.createElement('span');
        target.className = 'gma-exact-target';
        target.setAttribute('aria-hidden', 'true');
        target.dataset.gmaMotif = 'target-burst';
        const targetWave = index % GMA_EXACT_WAVE_COUNT;
        target.dataset.gmaWave = String(targetWave);
        target.dataset.angle = String(((Math.PI * 2) / targetCount) * index);
        target.dataset.radius = String(0.7 + ((index % 4) * 0.1));
        target.textContent = index % 5 === 0 ? '🏆' : '🎯';
        target.style.setProperty('--gma-target-size', `${2 + ((index % 5) * 0.38)}rem`);
        target.style.setProperty(
            '--gma-target-delay',
            `${GMA_EXACT_WAVE_DELAYS_MS[targetWave] + ((index % 3) * 90)}ms`);
        targetElements.push(target);
        targetFragment.appendChild(target);
    }
    overlay.appendChild(targetFragment);

    modalContent.appendChild(overlay);

    const context = canvas.getContext('2d', { alpha: true });
    if (!context) {
        overlay.remove();
        return;
    }

    const particles = [];
    const waveTimers = [];
    const palette = ['#ffd83d', '#fff7b2', '#ff4e91', '#19c3d1', '#ff8a00', '#b98cff', '#8cff7a'];
    const detailsModal = modalContent.closest('#detailsModal');
    const portrait = document.getElementById('modalProfilePic');
    const portraitWasInert = Boolean(portrait?.inert);
    const motionQuery = window.matchMedia?.('(prefers-reduced-motion: reduce)');
    let frameId = 0;
    let cleanupTimer = 0;
    let lastFrameAt = performance.now();
    let seed = 0x9e3779b9;
    let isCancelled = false;

    function random() {
        seed = ((seed * 1664525) + 1013904223) >>> 0;
        return seed / 4294967296;
    }

    function layoutTargets() {
        const radiusX = Math.min(window.innerWidth * 0.47, 650);
        const radiusY = Math.min(window.innerHeight * 0.43, 480);
        targetElements.forEach(target => {
            const angle = Number(target.dataset.angle);
            const radius = Number(target.dataset.radius);
            target.style.setProperty('--gma-target-dx', `${Math.cos(angle) * radiusX * radius}px`);
            target.style.setProperty('--gma-target-dy', `${Math.sin(angle) * radiusY * radius}px`);
        });
    }

    function resizeCanvas() {
        const cssWidth = Math.max(1, window.innerWidth);
        const cssHeight = Math.max(1, window.innerHeight);
        const pixelRatioCap = Math.sqrt(GMA_EXACT_CANVAS_PIXEL_CAP / (cssWidth * cssHeight));
        const pixelRatio = Math.min(window.devicePixelRatio || 1, 2, pixelRatioCap);
        canvas.width = Math.max(1, Math.floor(cssWidth * pixelRatio));
        canvas.height = Math.max(1, Math.floor(cssHeight * pixelRatio));
        canvas.style.width = `${cssWidth}px`;
        canvas.style.height = `${cssHeight}px`;
        canvas.dataset.backingPixels = String(canvas.width * canvas.height);
        context.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
        layoutTargets();
    }

    function removeMotionListener() {
        if (!motionQuery) return;
        if (typeof motionQuery.removeEventListener === 'function') {
            motionQuery.removeEventListener('change', handleMotionChange);
        } else if (typeof motionQuery.removeListener === 'function') {
            motionQuery.removeListener(handleMotionChange);
        }
    }

    function cancel() {
        if (isCancelled) return;
        isCancelled = true;
        waveTimers.forEach(timer => window.clearTimeout(timer));
        if (cleanupTimer) window.clearTimeout(cleanupTimer);
        if (frameId) window.cancelAnimationFrame(frameId);
        window.removeEventListener('resize', resizeCanvas);
        document.removeEventListener('visibilitychange', handleVisibilityChange);
        removeMotionListener();
        particles.length = 0;
        overlay.remove();
        if (portrait) portrait.inert = portraitWasInert;
        detailsModal?.classList.remove('gma-exact-takeover-active');
        modalContent.classList.remove('gma-exact-takeover');
        if (gmaExactJackpotController === controller) gmaExactJackpotController = null;
    }

    function handleMotionChange(event) {
        if (event.matches) cancel();
    }

    function handleVisibilityChange() {
        if (document.hidden) cancel();
    }

    const controller = { cancel };
    gmaExactJackpotController = controller;
    if (portrait) portrait.inert = true;
    detailsModal?.classList.add('gma-exact-takeover-active');
    modalContent.classList.add('gma-exact-takeover');

    function phaseForWave(waveIndex) {
        if (waveIndex < 5) return 'opening';
        if (waveIndex < 13) return 'escalation';
        if (waveIndex < 24) return 'frenzy';
        return 'finale';
    }

    function publishPhase(waveIndex) {
        const phase = phaseForWave(waveIndex);
        if (overlay.dataset.phase === phase) return;
        overlay.dataset.phase = phase;
        if (phase === 'escalation') {
            kicker.textContent = '🎯 confetti cannons armed 🎯';
            subtitle.textContent = 'THE CLOCK STORM IS JUST GETTING STARTED';
        } else if (phase === 'frenzy') {
            kicker.textContent = '⏱️ temporal overload ⏱️';
            subtitle.textContent = 'MORE CLOCKS · MORE CONFETTI · NO RESTRAINT';
        } else if (phase === 'finale') {
            kicker.textContent = '🏆 maximum bullseye achieved 🏆';
            subtitle.textContent = 'ABSOLUTE TIME LORD · JACKPOT UNLOCKED';
        }
        window.dispatchEvent(new CustomEvent('gma:exact-phase', {
            detail: { phase, wave: waveIndex }
        }));
    }

    function addParticle(originX, originY, waveIndex, particleIndex) {
        if (particles.length >= GMA_EXACT_CONFETTI_CAP) return false;
        const phase = phaseForWave(waveIndex);
        const phaseStrength = phase === 'opening'
            ? 0
            : phase === 'escalation'
                ? 1
                : phase === 'frenzy'
                    ? 2
                    : 3;
        const comesFromBottom = originY > window.innerHeight * 0.62;
        const baseAngle = comesFromBottom ? -Math.PI / 2 : random() * Math.PI * 2;
        const spread = comesFromBottom ? (random() - 0.5) * 1.9 : 0;
        const angle = baseAngle + spread;
        const speed = 6 + (phaseStrength * 0.8) + (random() * (12 + phaseStrength));
        const life = 2100 + (phaseStrength * 260) + (random() * 1100);
        const shapeCount = 4 + Math.min(2, phaseStrength);
        particles.push({
            x: originX,
            y: originY,
            vx: Math.cos(angle) * speed,
            vy: Math.sin(angle) * speed,
            gravity: 0.1 + (random() * 0.12),
            drag: 0.986 + (random() * 0.01),
            rotation: random() * Math.PI * 2,
            rotationSpeed: (random() - 0.5) * 0.32,
            size: 4 + (phaseStrength * 0.45) + (random() * 9),
            age: 0,
            bornAt: performance.now(),
            life,
            color: palette[(waveIndex + particleIndex) % palette.length],
            shape: particleIndex % shapeCount
        });
        return true;
    }

    function discardExpiredParticles(now) {
        let writeIndex = 0;
        for (let index = 0; index < particles.length; index++) {
            const particle = particles[index];
            if (now - particle.bornAt >= particle.life) continue;
            particles[writeIndex++] = particle;
        }
        particles.length = writeIndex;
    }

    function fireWave(waveIndex) {
        if (isCancelled
            || prefersReducedGmaMotion()
            || !isCurrentGmaPresentation(presentation)) {
            cancel();
            return;
        }

        publishPhase(waveIndex);
        const origins = [
            [0.06, 0.9],
            [0.94, 0.9],
            [0.5, 0.82],
            [0.08, 0.32],
            [0.92, 0.32],
            [0.5, 0.12]
        ];
        const phase = phaseForWave(waveIndex);
        const particlesPerWaveByPhase = window.innerWidth <= 540
            ? { opening: 75, escalation: 130, frenzy: 190, finale: 280 }
            : { opening: 110, escalation: 190, frenzy: 290, finale: 460 };
        const particlesPerWave = particlesPerWaveByPhase[phase];
        const originCount = phase === 'finale' ? 3 : phase === 'frenzy' ? 2 : 1;
        const waveOrigins = [];
        for (let originIndex = 0; originIndex < originCount; originIndex++) {
            waveOrigins.push(origins[(waveIndex + originIndex * 2) % origins.length]);
        }
        discardExpiredParticles(performance.now());
        const roomForThisWave = GMA_EXACT_CONFETTI_CAP - particlesPerWave;
        if (particles.length > roomForThisWave) {
            // Retire the oldest pieces so every cannon firing produces a visible new wave,
            // even when a loaded renderer has not painted often enough to age them out.
            particles.splice(0, particles.length - roomForThisWave);
        }
        let spawnedThisWave = 0;
        for (let particleIndex = 0; particleIndex < particlesPerWave; particleIndex++) {
            const origin = waveOrigins[particleIndex % waveOrigins.length];
            if (addParticle(
                origin[0] * window.innerWidth,
                origin[1] * window.innerHeight,
                waveIndex,
                particleIndex)) {
                spawnedThisWave += 1;
            }
        }

        overlay.dataset.lastWave = String(waveIndex);
        overlay.dataset.wavesFired = String(waveIndex + 1);
        canvas.dataset.burstsFired = String(waveIndex + 1);
        canvas.dataset.totalSpawned = String(
            Number(canvas.dataset.totalSpawned || 0) + spawnedThisWave);
        window.dispatchEvent(new CustomEvent('gma:exact-wave', {
            detail: { wave: waveIndex, spawned: spawnedThisWave }
        }));
    }

    function drawFrame(now) {
        if (isCancelled) return;
        if (prefersReducedGmaMotion() || !isCurrentGmaPresentation(presentation)) {
            cancel();
            return;
        }

        const frameScale = Math.min(2.2, Math.max(0.35, (now - lastFrameAt) / (1000 / 60)));
        lastFrameAt = now;
        context.clearRect(0, 0, window.innerWidth, window.innerHeight);

        let writeIndex = 0;
        for (let index = 0; index < particles.length; index++) {
            const particle = particles[index];
            particle.age = Math.max(0, now - particle.bornAt);
            if (particle.age >= particle.life) continue;

            particle.vx *= Math.pow(particle.drag, frameScale);
            particle.vy = (particle.vy * Math.pow(particle.drag, frameScale))
                + (particle.gravity * frameScale);
            particle.x += particle.vx * frameScale;
            particle.y += particle.vy * frameScale;
            particle.rotation += particle.rotationSpeed * frameScale;
            particles[writeIndex++] = particle;

            const progress = particle.age / particle.life;
            const alpha = Math.min(1, (1 - progress) * 2.4);
            context.save();
            context.globalAlpha = alpha;
            context.translate(particle.x, particle.y);
            context.rotate(particle.rotation);
            context.fillStyle = particle.color;
            context.strokeStyle = particle.color;
            context.lineWidth = Math.max(2, particle.size * 0.28);
            if (particle.shape === 0) {
                context.fillRect(-particle.size / 2, -particle.size / 3, particle.size, particle.size * 0.66);
            } else if (particle.shape === 1) {
                context.beginPath();
                context.arc(0, 0, particle.size * 0.45, 0, Math.PI * 2);
                context.fill();
            } else if (particle.shape === 2) {
                context.beginPath();
                context.moveTo(-particle.size, 0);
                context.quadraticCurveTo(0, particle.size, particle.size, 0);
                context.stroke();
            } else if (particle.shape === 3) {
                // A cheap canvas clock face lets the historical time-icon storm reach
                // thousands of visible pieces without allocating thousands of DOM nodes.
                context.beginPath();
                context.arc(0, 0, particle.size * 0.58, 0, Math.PI * 2);
                context.stroke();
                context.beginPath();
                context.moveTo(0, 0);
                context.lineTo(0, -particle.size * 0.38);
                context.moveTo(0, 0);
                context.lineTo(particle.size * 0.31, particle.size * 0.12);
                context.stroke();
            } else if (particle.shape === 4) {
                context.beginPath();
                for (let point = 0; point < 10; point++) {
                    const radius = point % 2 === 0 ? particle.size : particle.size * 0.42;
                    const angle = (-Math.PI / 2) + (point * Math.PI / 5);
                    const x = Math.cos(angle) * radius;
                    const y = Math.sin(angle) * radius;
                    if (point === 0) context.moveTo(x, y);
                    else context.lineTo(x, y);
                }
                context.closePath();
                context.fill();
            } else {
                context.beginPath();
                context.arc(0, 0, particle.size * 0.62, 0, Math.PI * 2);
                context.arc(0, 0, particle.size * 0.32, 0, Math.PI * 2, true);
                context.fill();
            }
            context.restore();
        }
        particles.length = writeIndex;
        canvas.dataset.activeParticles = String(particles.length);
        frameId = window.requestAnimationFrame(drawFrame);
    }

    resizeCanvas();
    window.addEventListener('resize', resizeCanvas);
    document.addEventListener('visibilitychange', handleVisibilityChange);
    if (motionQuery) {
        if (typeof motionQuery.addEventListener === 'function') {
            motionQuery.addEventListener('change', handleMotionChange);
        } else if (typeof motionQuery.addListener === 'function') {
            motionQuery.addListener(handleMotionChange);
        }
    }
    frameId = window.requestAnimationFrame(drawFrame);
    for (let waveIndex = 0; waveIndex < GMA_EXACT_WAVE_COUNT; waveIndex++) {
        waveTimers.push(window.setTimeout(
            () => fireWave(waveIndex),
            GMA_EXACT_WAVE_DELAYS_MS[waveIndex]));
    }
    cleanupTimer = window.setTimeout(cancel, GMA_EXACT_JACKPOT_MS);
}

function spawnGmaCelebration(kind, presentation) {
    if (kind === 'exact') {
        launchGmaExactJackpot(presentation);
        return;
    }
    if (prefersReducedGmaMotion()) return;

    const isFirst = kind === 'first';
    const origin = document.getElementById('gmaRealBubble') || gmaCard;
    const rect = origin.getBoundingClientRect();
    const modalRect = modalContent.getBoundingClientRect();
    const centerX = rect.left + (rect.width / 2);
    const centerY = rect.top + (rect.height / 2);
    const icons = isFirst
        ? ['medal', 'flag-checkered', 'stopwatch', 'calendar-check']
        : ['clock', 'stopwatch', 'hourglass-half', 'calendar-check'];
    const sparkCount = isFirst ? 28 : 48;
    const radiusX = Math.min(360, modalRect.width * 0.46);
    const radiusY = Math.min(420, modalRect.height * 0.38);

    for (let index = 0; index < sparkCount; index++) {
        const angle = ((Math.PI * 2) / sparkCount) * index - (Math.PI / 2);
        const distanceScale = 0.68 + ((index % 5) * 0.08);
        const deltaX = Math.cos(angle) * radiusX * distanceScale;
        const deltaY = Math.sin(angle) * radiusY * distanceScale;
        const spark = document.createElement('i');
        spark.className = `gma-celebration-spark fas fa-${icons[index % icons.length]}`;
        spark.setAttribute('aria-hidden', 'true');
        spark.dataset.quadrant = `${deltaX >= 0 ? 'right' : 'left'}-${deltaY >= 0 ? 'bottom' : 'top'}`;
        spark.style.setProperty('--gma-spark-x', `${centerX}px`);
        spark.style.setProperty('--gma-spark-y', `${centerY}px`);
        spark.style.setProperty('--gma-spark-dx', `${deltaX}px`);
        spark.style.setProperty('--gma-spark-dy', `${deltaY}px`);
        spark.style.setProperty('--gma-spark-rotation', `${index % 2 === 0 ? 140 : -140}deg`);
        spark.style.setProperty('--gma-spark-size', `${1.3 + ((index % 4) * 0.2)}rem`);
        spark.style.animationDelay = `${(index % 2) * 140}ms`;
        if (isFirst) spark.style.setProperty('--gma-spark-color', '#9a6700');
        document.body.appendChild(spark);
        spark.addEventListener('animationend', () => spark.remove(), { once: true });
        window.setTimeout(() => spark.remove(), 3400);
    }
}

function clearGmaEphemera() {
    cancelGmaExactJackpot();
    cancelGmaTrollTakeover();
    document.querySelectorAll('.gma-reaction, .gma-celebration-spark, .gma-pop-banner, .gma-exact-jackpot')
        .forEach(element => element.remove());
}

function persistGmaGuessState(athleteSlug, profileImageId, guessState) {
    try {
        if (!athleteSlug || !profileImageId || !window.LwcGuessState) return false;
        window.LwcGuessState.set({
            AthleteSlug: athleteSlug,
            ProfileImageId: profileImageId
        }, guessState);
        return true;
    } catch (error) {
        console.warn('Could not persist Guess My Age state.', error);
        return false;
    }
}

function scheduleGmaExit(athleteSlug, profileImageId, presentationGeneration, dwell) {
    if (gmaExitTimer) window.clearTimeout(gmaExitTimer);

    const canExit = () => modalContent.classList.contains('guess-mode')
        && modalContent.dataset.athleteSlug === athleteSlug
        && modalContent.dataset.profileImageId === profileImageId
        && (presentationGeneration == null || presentationGeneration === gmaPresentationGeneration);

    const beginExit = () => {
        gmaExitTimer = 0;
        if (!canExit()) return;

        gmaCard.style.setProperty(
            '--gma-exit-height',
            `${Math.ceil(gmaCard.getBoundingClientRect().height)}px`);
        gmaCard.classList.add('gma-done');
        const exitDelay = prefersReducedGmaMotion()
            ? 0
            : modalContent.classList.contains('gma-fast')
                ? 180
                : 260;
        gmaExitTimer = window.setTimeout(() => {
            gmaExitTimer = 0;
            if (!canExit()) return;

            clearGmaEphemera();
            modalContent.classList.remove('guess-mode', 'gma-result-ready');
            modalContent.scrollTop = 0;
            modalContent.scrollLeft = 0;
            document.getElementById('closeAthleteDetailsModal')?.focus({ preventScroll: true });
        }, exitDelay);
    };

    if (dwell > 0) {
        gmaExitTimer = window.setTimeout(beginExit, dwell);
    } else {
        beginExit();
    }
}

function restoreGuessControls(message, focusTarget) {
    gmaCard.removeAttribute('aria-busy');
    gmaCard.classList.remove('is-submitting', 'is-revealing');
    heading.style.display = '';
    chronoHeading.style.display = 'none';
    chronoHeading.classList.remove('is-revealing');
    gmaStatus.classList.remove('gma-status--semantic');
    gmaStatus.hidden = !message;
    gmaStatus.textContent = message || '';
    gmaActions.classList.remove('gma-actions-hide');
    gmaActions.inert = false;
    modalContent.classList.remove('gma-result-ready');
    gmaRange.disabled = false;
    gmaRange.style.pointerEvents = '';
    setGmaRangeResultState(false);
    gmaSubmitBtn.disabled = false;
    gmaBubble.classList.remove('gma-bubble-inactive');
    document.getElementById('gmaRealBubble')?.remove();
    clearGmaEphemera();
    syncRange();
    (focusTarget || gmaRange).focus();
}

const gmaSubmitBtn = gmaCard.querySelector('.gma-btn--primary');
gmaSubmitBtn.addEventListener('click', async function () {
    const userGuess = +gmaRange.value;
    const submittedAthleteSlug = modalContent.dataset.athleteSlug || '';
    const submittedProfileImageId = modalContent.dataset.profileImageId || '';

    if (userGuess === +gmaRange.min || userGuess === +gmaRange.max) {
        gmaTriggerTrollAnimation();
        return;
    }

    if (!submittedAthleteSlug || !submittedProfileImageId) {
        restoreGuessControls('We could not verify which profile picture you saw. Please reopen this profile.', gmaSubmitBtn);
        return;
    }

    clearGmaEphemera();
    const presentation = beginGmaPresentation(submittedAthleteSlug, submittedProfileImageId);
    const preludePromise = startGmaResultPrelude(presentation);

    gmaCard.setAttribute('aria-busy', 'true');
    gmaCard.classList.add('is-submitting');
    gmaStatus.classList.remove('gma-status--semantic');
    gmaStatus.hidden = false;
    gmaStatus.textContent = 'Submitting your guess…';
    gmaStatus.focus({ preventScroll: true });
    gmaActions.classList.add('gma-actions-hide');
    gmaActions.inert = true;
    gmaRange.disabled = true;
    gmaRange.style.pointerEvents = 'none';
    setGmaRangeResultState(true);
    gmaSubmitBtn.disabled = true;

    try {
        const query = new URLSearchParams({
            athleteName: presentation.athleteSlug,
            profileImageId: presentation.profileImageId,
            ageGuess: String(userGuess)
        });
        const response = await fetch(`/api/Guess/athlete-age?${query}`, { method: 'POST' });

        if (response.status === 409) {
            // The server must not disclose actual age for a portrait that is
            // no longer current. Reload the athlete snapshot and let the
            // visitor judge the replacement image before retrying.
            cancelGmaPresentation();
            clearGmaEphemera();
            const refreshed = typeof window.refreshAthleteAfterStaleGuess === 'function'
                && await window.refreshAthleteAfterStaleGuess(presentation.athleteSlug);
            if (!refreshed) {
                restoreGuessControls('The profile picture changed. Reopen this profile and try again.', gmaSubmitBtn);
            }
            return;
        }

        if (!response.ok) {
            throw new Error(`Guess submission failed with status ${response.status}`);
        }

        const result = await response.json();
        const realAge = +result.actualAge;
        if (!Number.isFinite(realAge)) {
            throw new Error('Guess response did not include a valid actual age');
        }

        // GuessAccepted describes inclusion in the public Crowd Age aggregate, not whether
        // the player completed the game. Always reveal a successful response: asking the
        // player to revise after the server has disclosed the real age creates a dead end
        // and would let a filtered guess be replaced with a knowingly correct one.
        const crowdCountBeforeGuess = result.crowdCount === 1 ? 0 : result.crowdCount;
        const isFirstGuess = result.guessAccepted === true && crowdCountBeforeGuess === 0;
        const userError = Math.abs(realAge - userGuess);
        const crowdError = result.crowdAge === 0 || isFirstGuess
            ? null
            : Math.abs(realAge - result.crowdAge);
        const guessState = {
            value: userGuess,
            skipped: false,
            first: isFirstGuess,
            exact: userError === 0
        };

        // The accepted result is durable before any presentation work. A reload, tab switch,
        // or newly opened athlete must never lose or misattribute this completed round.
        persistGmaGuessState(presentation.athleteSlug, presentation.profileImageId, guessState);
        if (userError === 0
            && window.proDiscounts
            && typeof window.proDiscounts.setPerfectGuessMarker === 'function') {
            try {
                window.proDiscounts.setPerfectGuessMarker();
            } catch (error) {
                console.warn('Could not persist the perfect Guess My Age marker.', error);
            }
        }

        if (!isCurrentGmaPresentation(presentation)) return;
        updateAthleteCrowdAge(presentation.athleteSlug, result.crowdAge, result.crowdCount);
        try {
            updateYourGuess();
        } catch (profileRefreshError) {
            // The profile comparison row is optional presentation. Storage may become
            // unavailable after the accepted result was persisted best-effort; that must
            // not turn a successful server response into a retryable submission failure.
            console.warn('Could not refresh the Guess My Age profile row.', profileRefreshError);
        }

        gmaCard.classList.remove('is-submitting');
        gmaCard.classList.add('is-revealing');
        const reactionKind = userGuess === realAge
            ? 'exact'
            : userGuess < realAge
                ? 'younger'
                : 'older';
        const reactionCopy = reactionKind === 'exact'
            ? 'Right on the nose.'
            : reactionKind === 'younger'
                ? 'You guessed younger — high five.'
                : 'You guessed older — oof.';
        const outcomeCopy = userError === 0
            ? ' Bullseye!'
            : isFirstGuess
                ? ' First accepted guess!'
                : crowdError !== null && userError < crowdError
                    ? ' You beat the crowd!'
                    : '';
        gmaStatus.classList.add('gma-status--semantic');
        gmaStatus.textContent = `Actual age: ${realAge}. Your guess: ${userGuess}. ${reactionCopy}${outcomeCopy}`;
        gmaCard.removeAttribute('aria-busy');
        modalContent.classList.add('gma-result-ready');
        showGmaReaction(reactionKind);

        const preludeCompleted = await preludePromise;
        if (!preludeCompleted || !isCurrentGmaPresentation(presentation)) return;

        const realBubble = document.createElement('span');
        realBubble.id = 'gmaRealBubble';
        realBubble.className = 'gma-real-bubble';
        realBubble.setAttribute('aria-hidden', 'true');
        gmaSliderWrap.appendChild(realBubble);

        gmaBubble.classList.add('gma-bubble-inactive');
        const revealCompleted = await animateActualAgeReveal(
            realBubble,
            userGuess,
            realAge,
            presentation);
        if (!revealCompleted || !isCurrentGmaPresentation(presentation)) return;

        document.querySelectorAll('.gma-reaction').forEach(element => element.remove());
        document.getElementById('closeAthleteDetailsModal')?.focus({ preventScroll: true });

        const outcomeDwell = userError === 0 && !prefersReducedGmaMotion()
            ? 16000
            : 5000;
        try {
            if (userError === 0) {
                gmaCard.classList.add('celebrate-exact');
                popBanner(true, false, outcomeDwell);
                spawnGmaCelebration('exact', presentation);
            } else if (isFirstGuess) {
                popBanner(false, true, outcomeDwell);
                spawnGmaCelebration('first');
            } else if (crowdError !== null && userError < crowdError) {
                gmaCard.classList.add('celebrate-better');
                popBanner(false, false, outcomeDwell);
                spawnGmaCelebration('crowd');
            }
        } catch (presentationError) {
            // The server result and local completion are already durable. A decorative
            // renderer failure must never turn that success back into a retryable error.
            cancelGmaExactJackpot();
            document.querySelectorAll('.gma-celebration-spark, .gma-exact-jackpot')
                .forEach(element => element.remove());
            modalContent.classList.remove('gma-exact-takeover');
            console.warn('Could not play the Guess My Age celebration.', presentationError);
        }

        scheduleGmaExit(
            presentation.athleteSlug,
            presentation.profileImageId,
            presentation.generation,
            outcomeDwell);
    } catch (error) {
        if (!isCurrentGmaPresentation(presentation)) return;
        cancelGmaPresentation();
        console.error('Error submitting guess:', error);
        restoreGuessControls('We could not submit your guess. Please try again.', gmaSubmitBtn);
    }
});

// bind hideCard to _all_ ghost buttons (Skip & Skip All)
gmaActions.querySelectorAll('.gma-btn--ghost')
    .forEach(btn => btn.addEventListener('click', hideCard));
function hideCard(e) {
    // if they clicked “Skip All”, set global flag
    const isSkipAll = e.currentTarget.id === 'gmaSkipAll';
    if (isSkipAll) {
        try {
            localStorage.setItem('gmaSkipAll', 'true');
        } catch (error) {
            console.warn('Could not persist the Guess My Age skip-all preference.', error);
        }
    }

    const modalContent = document.querySelector('#detailsModal .modal-content');
    const athleteSlug = modalContent.dataset.athleteSlug;
    const profileImageId = modalContent.dataset.profileImageId;
    cancelGmaPresentation();
    persistGmaGuessState(athleteSlug, profileImageId, {
        value: null,
        skipped: true,
        first: false,
        exact: false
    });
    updateYourGuess();
    scheduleGmaExit(athleteSlug, profileImageId, null, 0);
}

function popBanner(isExact, isFirst, outcomeDwell) {
    const b = document.createElement('div');
    b.setAttribute('aria-hidden', 'true');
    b.classList.add('gma-pop-banner');
    if (isExact) {
        b.classList.add('gma-pop-banner--golden');
    }

    const bannerCopy = document.createElement('span');
    bannerCopy.textContent = isExact
        ? 'Bullseye!'
        : isFirst
            ? 'First accepted guess!'
            : 'You beat the crowd!';
    const leadingEmoji = isExact ? '🎯🎯🎯 ' : isFirst ? '🥇 ' : '🎉 ';
    const trailingEmoji = isExact ? ' 🎯🎯🎯' : isFirst ? ' 🥇' : ' 🏆';
    b.append(document.createTextNode(leadingEmoji), bannerCopy, document.createTextNode(trailingEmoji));

    gmaPayoffRegion.replaceChildren(b);
    requestAnimationFrame(() => b.classList.add('show'));

    const bannerDwell = prefersReducedGmaMotion()
        ? 1200
        : Math.max(2000, outcomeDwell - 600);
    setTimeout(() => {
        b.classList.add('hide');
        b.addEventListener('animationend', () => b.remove(), { once: true });
        setTimeout(() => b.remove(), 600);
    }, bannerDwell);

}

function setGuessMyAgeParam(active) {
    const url = new URL(window.location.href);
    if (active) url.searchParams.set('guessmyage', '1');
    else url.searchParams.delete('guessmyage');
    history.replaceState(history.state || {}, "", url.toString());
}
