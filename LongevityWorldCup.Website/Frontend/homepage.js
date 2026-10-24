let bitcoinDonationAddress = '';
let bitcoinCopyTimer = null;
let cancelBitcoinCopyExit = null;
let newsletterSubmitting = false;
let newsletterRetryEmail = '';
const newsletterForm = document.getElementById('newsletter-form');
const newsletterEmailInput = document.getElementById('emailInput');
const newsletterSubmitButton = newsletterForm.querySelector('button[type="submit"]');
const newsletterStatus = document.getElementById('newsletterStatus');
const bitcoinAddressLink = document.getElementById('btcAddressLink');
const bitcoinCopyButton = document.getElementById('bitcoinCopyButton');
const bitcoinAddressFallback = document.getElementById('bitcoinAddressFallback');
const bitcoinCopyHelp = document.getElementById('bitcoinCopyHelp');

bitcoinAddressFallback.addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    bitcoinAddressFallback.hidden = true;
    bitcoinCopyHelp.hidden = true;
    bitcoinAddressLink.style.display = '';
    bitcoinCopyButton.focus({ preventScroll: true });
});
bitcoinAddressFallback.addEventListener('focus', () => bitcoinAddressFallback.select());

function setBitcoinAddressUnavailable() {
    bitcoinDonationAddress = '';
    bitcoinAddressLink.removeAttribute('href');
    bitcoinAddressLink.removeAttribute('title');
    bitcoinAddressLink.textContent = 'Donation address unavailable right now.';
    bitcoinAddressLink.setAttribute('aria-label', 'Bitcoin donation address unavailable right now');
    bitcoinAddressLink.style.pointerEvents = 'none';
    bitcoinCopyButton.disabled = true;
    bitcoinCopyButton.setAttribute('aria-disabled', 'true');
}

fetch('/api/bitcoin/donation-address')
    .then(r => r.ok ? r.json() : Promise.reject())
    .then(({ address }) => {
        bitcoinDonationAddress = address || '';
        if (bitcoinDonationAddress) {
            bitcoinAddressLink.href = `https://mempool.space/address/${bitcoinDonationAddress}`;
            bitcoinAddressLink.setAttribute('aria-label', 'View the Bitcoin donation address on mempool.space');
            bitcoinAddressLink.style.pointerEvents = '';
            bitcoinCopyButton.disabled = false;
            bitcoinCopyButton.setAttribute('aria-disabled', 'false');
            adjustBitcoinAddressDisplay();
        } else {
            setBitcoinAddressUnavailable();
        }
    })
    .catch(() => {
        setBitcoinAddressUnavailable();
    });

if (!window.callScoped) {
    window.callScoped = function (root, fn) {
        var r = typeof root === 'string' ? document.getElementById(root) : root; if (!r) return;
        var g1 = document.getElementById, g2 = document.querySelector, g3 = document.querySelectorAll;
        document.getElementById = function (id) { return r.querySelector('#' + (window.CSS && CSS.escape ? CSS.escape(id) : id)) }
        document.querySelector = function (sel) { return r.querySelector(sel) }
        document.querySelectorAll = function (sel) { return r.querySelectorAll(sel) }
        try { fn() } finally { document.getElementById = g1; document.querySelector = g2; document.querySelectorAll = g3 }
    }
}

const homepageHighlightsBaseRows = 5;
const homepageHighlightsMaxRows = 12;
const homepageVisitCounterKey = 'lwcHomepageVisitCount:v1';
const homepageHighlightsBeforePodiumVisit = 4;
let homepageHighlightsRows = homepageHighlightsBaseRows;
let homepageHighlightsMeasureTimer = null;
let homepageHighlightsResizeTimer = null;
let homepageHighlightsFitPromise = null;
let homepageHighlightsRequested = false;

function incrementHomepageVisitCount() {
    try {
        const raw = window.localStorage ? window.localStorage.getItem(homepageVisitCounterKey) : null;
        const current = Number.parseInt(raw || '0', 10);
        const next = Math.min((Number.isFinite(current) && current >= 0 ? current : 0) + 1, 1000000);
        window.localStorage.setItem(homepageVisitCounterKey, String(next));
        return next;
    } catch (_) {
        return 1;
    }
}

function isHomepageVisitCounterRoute() {
    const path = (window.location && window.location.pathname ? window.location.pathname : '/').toLowerCase();
    const isHomepagePath = path === '/' || path === '/index.html';
    if (!isHomepagePath) return false;

    try {
        const params = new URLSearchParams(window.location.search || '');
        return !['athlete', 'filters', 'search', 'view'].some(param => params.has(param));
    } catch (_) {
        return true;
    }
}

function stabilizeHomepageFragmentTarget() {
    const requestedHash = window.location.hash;
    if (!requestedHash || requestedHash === '#') return;

    let targetId;
    try {
        targetId = decodeURIComponent(requestedHash.slice(1));
    } catch (_) {
        return;
    }

    const target = document.getElementById(targetId);
    const main = document.querySelector('main');
    const section = target ? target.closest('.section-container') : null;
    if (!target || !main || !section || !main.contains(section)) return;

    let stopped = false;
    let firstFrame = 0;
    let secondFrame = 0;
    let stopTimer = 0;
    const interactionEvents = ['wheel', 'touchstart', 'pointerdown', 'keydown'];

    const alignTarget = function () {
        firstFrame = 0;
        secondFrame = 0;
        if (stopped || window.location.hash !== requestedHash) return;
        target.scrollIntoView({ block: 'start', inline: 'nearest', behavior: 'auto' });
    };

    const queueAlignment = function () {
        if (stopped) return;
        window.cancelAnimationFrame(firstFrame);
        window.cancelAnimationFrame(secondFrame);
        firstFrame = window.requestAnimationFrame(function () {
            firstFrame = 0;
            secondFrame = window.requestAnimationFrame(alignTarget);
        });
    };

    const resizeObserver = typeof ResizeObserver === 'function'
        ? new ResizeObserver(queueAlignment)
        : null;

    const stopTracking = function () {
        if (stopped) return;
        stopped = true;
        resizeObserver?.disconnect();
        window.clearTimeout(stopTimer);
        window.cancelAnimationFrame(firstFrame);
        window.cancelAnimationFrame(secondFrame);
        window.removeEventListener('load', queueAlignment);
        window.removeEventListener('hashchange', stopTracking);
        interactionEvents.forEach(eventName => window.removeEventListener(eventName, stopTracking));
    };

    resizeObserver?.observe(main);
    window.addEventListener('load', queueAlignment, { once: true });
    window.addEventListener('hashchange', stopTracking, { once: true });
    interactionEvents.forEach(eventName =>
        window.addEventListener(eventName, stopTracking, { once: true, passive: true }));

    queueAlignment();
    stopTimer = window.setTimeout(function () {
        alignTarget();
        stopTracking();
    }, 5000);
}

function placeHomepageHighlightsForVisit() {
    const main = document.querySelector('main');
    if (!isHomepageVisitCounterRoute()) {
        if (main) main.dataset.homepageVisitLayout = 'default';
        return;
    }

    const visitCount = incrementHomepageVisitCount();
    if (visitCount < homepageHighlightsBeforePodiumVisit) {
        if (main) main.dataset.homepageVisitLayout = 'default';
        return;
    }

    const highlights = document.querySelector('.lwc-highlights-countdown-wrapper');
    const leaderboard = document.querySelector('.main-container');
    if (!highlights || !leaderboard || !leaderboard.parentNode) {
        if (main) main.dataset.homepageVisitLayout = 'default';
        return;
    }

    leaderboard.parentNode.insertBefore(highlights, leaderboard);
    if (main) main.dataset.homepageVisitLayout = 'repeat';
}

function loadHomepageHighlights(rowCount) {
    const board = document.querySelector('#events-root-index .events-board');
    // Layout changes must not restart pending data or consume manual recovery.
    if (homepageHighlightsRequested && board
        && (board.getAttribute('aria-busy') === 'true' || board.querySelector('.events-recovery'))) return;
    homepageHighlightsRequested = true;
    homepageHighlightsRows = rowCount;
    let pending;
    callScoped('events-root-index', function () {
        pending = loadEventsTable(rowCount, true, null, true, {
            preserveExistingRows: true,
            useSharedEvents: true,
            layoutOnly: true
        });
    });
    return pending;
}

function homepageHighlightsTargetHeight() {
    // The paired card stretches to the taller column. Measure its content,
    // so fitting never uses a height that Highlights itself has inflated.
    const card = document.getElementById('lwc-countdown-box');
    const body = card?.querySelector('.lwc-merch-body');
    if (!card || !body) return 0;
    const children = [...body.children].filter(child => child.getClientRects().length);
    const bottom = Math.max(body.getBoundingClientRect().top, ...children.map(child => child.getBoundingClientRect().bottom));
    return bottom - card.getBoundingClientRect().top
        + (parseFloat(getComputedStyle(body).paddingBottom) || 0)
        + (parseFloat(getComputedStyle(card).borderBottomWidth) || 0);
}

async function fitHomepageHighlightsNow() {
    const highlightsRoot = document.getElementById('events-root-index');
    const highlightsBoard = highlightsRoot ? highlightsRoot.querySelector('.events-board') : null;
    const highlightsTable = highlightsRoot ? highlightsRoot.querySelector('#eventsTable') : null;
    const highlightsBody = highlightsRoot ? highlightsRoot.querySelector('#eventsTable tbody') : null;
    if (!highlightsBoard || !highlightsTable || !highlightsBody) return;
    if (homepageHighlightsRequested) {
        if (highlightsBoard.querySelector('.events-recovery')) return;
        if (highlightsBoard.getAttribute('aria-busy') === 'true') {
            homepageHighlightsMeasureTimer = window.setTimeout(fitHomepageHighlights, 180);
            return;
        }
    }
    if (!homepageHighlightsRequested) await loadHomepageHighlights(homepageHighlightsBaseRows);

    // Shared snapshots are already resolved after the first load. Finish the
    // bounded fit in this microtask turn, before painting any intermediate list.
    const visited = new Set();
    for (let attempt = 0; attempt < homepageHighlightsMaxRows; attempt++) {
        if (highlightsBoard.querySelector('.events-recovery')) return;
        const rows = [...highlightsBody.querySelectorAll('tr.main-row')];
        if (!rows.length) return;
        const readingRows = rows.reduce((count, row, index) =>
            row.matches('.is-open, :focus-within') || row.nextElementSibling?.matches('.details-row:focus-within')
                ? index + 1 : count, 0);
        let desired = Math.max(homepageHighlightsBaseRows, readingRows);
        if (window.innerWidth > 991) {
            desired = rows.length;
            const target = homepageHighlightsTargetHeight();
            const height = highlightsTable.getBoundingClientRect().height;
            const last = rows.at(-1);
            const details = last.nextElementSibling?.matches('.details-row') ? last.nextElementSibling : null;
            const lastHeight = last.getBoundingClientRect().height + (details?.getBoundingClientRect().height || 0);
            if (height < target - 28 && rows.length >= homepageHighlightsRows) desired++;
            else if (height - lastHeight >= target - 28) desired--;
            desired = Math.max(1, readingRows, Math.min(homepageHighlightsMaxRows, desired));
        }
        if (desired === rows.length || (desired === homepageHighlightsRows && rows.length < desired) || visited.has(desired)) return;
        visited.add(rows.length);
        await loadHomepageHighlights(desired);
    }
}

function fitHomepageHighlights() {
    window.clearTimeout(homepageHighlightsMeasureTimer);
    if (!homepageHighlightsFitPromise) {
        homepageHighlightsFitPromise = fitHomepageHighlightsNow()
            .finally(() => { homepageHighlightsFitPromise = null; });
    }
    return homepageHighlightsFitPromise;
}

function watchHomepageHighlightsLayout() {
    const highlights = document.getElementById('events-root-index');
    const swag = document.getElementById('lwc-countdown-box');
    const body = swag?.querySelector('.lwc-merch-body');
    if (!highlights || !body) return;

    // Observe the inputs to fitting, rather than the stretched card heights.
    // Appending a highlight changes both card heights; using those heights as
    // inputs would continually reset the rows we just fitted.
    const signature = function () {
        const row = highlights.querySelector('.event-message');
        const rowStyle = row ? getComputedStyle(row) : null;
        const bodyStyle = getComputedStyle(body);
        const content = [swag.querySelector('.lwc-countdown-header-row'), ...body.children];
        return JSON.stringify([
            window.innerWidth > 991,
            highlights.getBoundingClientRect().width,
            rowStyle?.fontSize, rowStyle?.lineHeight, rowStyle?.fontFamily,
            bodyStyle.paddingBlockStart, bodyStyle.paddingBlockEnd, bodyStyle.rowGap,
            ...content.map(element => element ? [
                element.getBoundingClientRect().width, element.getBoundingClientRect().height
            ] : [])
        ]);
    };
    let fittedLayout = signature();
    const queueFit = function (force = false) {
        const layout = signature();
        if (!force && layout === fittedLayout) return;
        fittedLayout = layout;
        window.clearTimeout(homepageHighlightsResizeTimer);
        homepageHighlightsResizeTimer = window.setTimeout(fitHomepageHighlights, 180);
    };
    const resizeObserver = new ResizeObserver(() => queueFit());
    resizeObserver.observe(highlights);
    resizeObserver.observe(body);
    resizeObserver.observe(swag.querySelector('.lwc-countdown-header-row'));
    const observeContent = function () {
        [...body.children].forEach(element => resizeObserver.observe(element));
        queueFit();
    };
    observeContent();
    new MutationObserver(observeContent).observe(body, { childList: true, subtree: true, characterData: true });
    window.addEventListener('resize', () => queueFit());
    document.fonts.ready.then(() => queueFit(true));
    document.fonts.addEventListener('loadingdone', () => queueFit(true));
}

document.addEventListener('DOMContentLoaded', function () {
    placeHomepageHighlightsForVisit();
    fitHomepageHighlights();
    LoadLeaderboard(true, 10);
    stabilizeHomepageFragmentTarget();

    watchHomepageHighlightsLayout();
});

(function initializeMerchCarousel() {
    const merchCarousel = document.getElementById('lwc-merch-mobile-carousel');
    if (!merchCarousel) {
        return;
    }

    const slides = Array.from(merchCarousel.querySelectorAll('.lwc-merch-mobile-slide'));
    const dots = Array.from(merchCarousel.querySelectorAll('.lwc-merch-mobile-dot'));
    if (slides.length <= 1) {
        return;
    }

    const track = merchCarousel.querySelector('.lwc-merch-mobile-track');
    if (!track) return;
    const grid = merchCarousel.parentElement.querySelector('.lwc-merch-grid');
    const gridLinks = Array.from(grid.querySelectorAll('.lwc-merch-item'));
    const phone = window.matchMedia('(max-width: 576px)');
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let activeIndex = 0;
    let pendingIndex = null;
    let hiddenLayoutFocus = null;

    // CSS can hide the old layout and blur its control before matchMedia's
    // change event runs. Retain only that involuntary blur for the handoff.
    function rememberHiddenLayoutFocus(event) {
        hiddenLayoutFocus = !event.relatedTarget && !event.target.getClientRects().length
            ? event.target : null;
    }
    grid.addEventListener('focusout', rememberHiddenLayoutFocus);
    merchCarousel.addEventListener('focusout', rememberHiddenLayoutFocus);

    function setActiveSlide(index) {
        const focusedSlide = slides.findIndex(slide => slide.contains(document.activeElement));
        activeIndex = (index + slides.length) % slides.length;

        // Expose and focus the new product before hiding the previous one.
        // This also covers a native swipe while a product has keyboard focus.
        slides[activeIndex].setAttribute('aria-hidden', 'false');
        slides[activeIndex].tabIndex = 0;
        if (focusedSlide >= 0 && focusedSlide !== activeIndex && phone.matches) {
            slides[activeIndex].focus({ preventScroll: true });
        }

        slides.forEach((slide, slideIndex) => {
            const isActive = slideIndex === activeIndex;
            slide.classList.toggle('is-active', isActive);
            slide.setAttribute('aria-hidden', String(!isActive));
            slide.tabIndex = isActive ? 0 : -1;
        });

        dots.forEach((dot, dotIndex) => {
            dot.classList.toggle('is-active', dotIndex === activeIndex);
            dot.setAttribute('aria-pressed', String(dotIndex === activeIndex));
        });
    }

    function slideScrollLeft(index) {
        // Slides are positioned relative to the track. The final snap point
        // is clamped to the end so the last product remains fully reachable.
        return Math.min(slides[index].offsetLeft, track.scrollWidth - track.clientWidth);
    }

    function scrollToSlide(index, behavior) {
        pendingIndex = index;
        track.scrollTo({ left: slideScrollLeft(index), behavior });
        if (Math.abs(track.scrollLeft - slideScrollLeft(index)) <= 1) pendingIndex = null;
    }

    function slideNearestScroll() {
        let nearest = 0;
        let nearestDistance = Infinity;
        slides.forEach((slide, slideIndex) => {
            const distance = Math.abs(slideScrollLeft(slideIndex) - track.scrollLeft);
            if (distance < nearestDistance) {
                nearest = slideIndex;
                nearestDistance = distance;
            }
        });
        return nearest;
    }

    dots.forEach((dot, dotIndex) => {
        dot.addEventListener('click', function (event) {
            setActiveSlide(dotIndex);
            scrollToSlide(dotIndex, reduceMotion.matches ? 'instant' : 'smooth');
            if (event.detail === 0) {
                slides[activeIndex].focus({ preventScroll: true });
            }
        });
    });

    let touchScrolling = false;

    function syncScrolledSlide() {
        if (!phone.matches) return;
        if (pendingIndex !== null && Math.abs(track.scrollLeft - slideScrollLeft(pendingIndex)) > 1) return;
        pendingIndex = null;
        const nearest = slideNearestScroll();
        if (nearest !== activeIndex) setActiveSlide(nearest);
    }

    let scrollFrame = 0;
    track.addEventListener('scroll', function () {
        if (scrollFrame) return;
        scrollFrame = requestAnimationFrame(function () {
            scrollFrame = 0;
            syncScrolledSlide();
        });
    }, { passive: true });
    function finishScroll() {
        if (!touchScrolling) syncScrolledSlide();
    }

    // Intermediate smooth-scroll frames must not undo a dot selection.
    track.addEventListener('scrollend', finishScroll);
    track.addEventListener('touchstart', function () {
        touchScrolling = true;
        // A focused snap target can anchor native panning. Keep focus on its
        // visible selector while touch input moves that product out of view.
        const focusedSlide = slides.indexOf(document.activeElement);
        if (focusedSlide >= 0) dots[focusedSlide].focus({ preventScroll: true });
    }, { passive: true });
    window.addEventListener('touchend', function () { touchScrolling = false; }, { passive: true });
    window.addEventListener('touchcancel', function () { touchScrolling = false; }, { passive: true });
    // A released or interrupted gesture can stop between native snap points
    // without a scrollend event. Finish only after horizontal movement is idle.
    let settleTimer = 0;
    const queueScrollEnd = function () {
        window.clearTimeout(settleTimer);
        settleTimer = window.setTimeout(function () {
            if (!phone.matches || touchScrolling) return;
            syncScrolledSlide();
            if (pendingIndex === null && Math.abs(track.scrollLeft - slideScrollLeft(activeIndex)) > 1) {
                scrollToSlide(activeIndex, reduceMotion.matches ? 'instant' : 'smooth');
            }
        }, 150);
    };
    track.addEventListener('scroll', queueScrollEnd, { passive: true });
    window.addEventListener('touchend', queueScrollEnd, { passive: true });
    window.addEventListener('touchcancel', queueScrollEnd, { passive: true });

    function interruptScroll() {
        if (pendingIndex === null) return;
        pendingIndex = null;
        track.scrollTo({ left: track.scrollLeft, behavior: 'instant' });
        syncScrolledSlide();
    }
    track.addEventListener('pointerdown', interruptScroll, { passive: true });
    track.addEventListener('wheel', interruptScroll, { passive: true });
    track.addEventListener('keydown', function (event) {
        if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) interruptScroll();
    });
    slides.forEach((slide, index) => {
        // A visible peek remains a usable product link. Expose it before mouse
        // focus, and also cover native touch focus and direct focus restoration.
        slide.addEventListener('pointerdown', function (event) {
            if (event.pointerType !== 'touch' && index !== activeIndex) setActiveSlide(index);
        });
        slide.addEventListener('focus', function () {
            if (index !== activeIndex) setActiveSlide(index);
        });
        slide.addEventListener('click', function () {
            setActiveSlide(index);
            scrollToSlide(index, reduceMotion.matches ? 'instant' : 'smooth');
        });
    });

    function syncLayout() {
        if (phone.matches) {
            // Reserve photo space above wrapped captions without changing the
            // normal-size card geometry. Measure first, then update all slides.
            const captions = slides.map(slide => {
                const style = getComputedStyle(slide);
                const inset = ['paddingTop', 'paddingBottom', 'borderTopWidth', 'borderBottomWidth', 'rowGap']
                    .reduce((sum, property) => sum + (parseFloat(style[property]) || 0), 0);
                return { slide, space: slide.querySelector('.lwc-merch-item-copy').getBoundingClientRect().height + inset };
            });
            captions.forEach(({ slide, space }) => slide.style.setProperty('--lwc-merch-caption-space', `${space}px`));
        }
        if (phone.matches && touchScrolling) return;
        const focused = document.activeElement === document.body && hiddenLayoutFocus
            ? hiddenLayoutFocus : document.activeElement;
        hiddenLayoutFocus = null;
        const focusedGridIndex = gridLinks.indexOf(focused);
        const focusedSlideIndex = slides.indexOf(focused);
        const focusedDotIndex = dots.indexOf(focused);
        pendingIndex = null;
        if (phone.matches) {
            merchCarousel.inert = false;
            setActiveSlide(focusedGridIndex >= 0 ? focusedGridIndex : activeIndex);
            scrollToSlide(activeIndex, 'instant');
            if (focusedGridIndex >= 0) slides[activeIndex].focus({ preventScroll: true });
            grid.inert = true;
        } else {
            grid.inert = false;
            if (focusedSlideIndex >= 0 || focusedDotIndex >= 0) {
                gridLinks[focusedSlideIndex >= 0 ? focusedSlideIndex : activeIndex].focus({ preventScroll: true });
            }
            merchCarousel.inert = true;
        }
    }

    setActiveSlide(0);
    merchCarousel.hidden = false;
    merchCarousel.parentElement.classList.add('has-merch-carousel');
    syncLayout();
    phone.addEventListener('change', syncLayout);
    let layoutFrame = 0;
    const layoutObserver = new ResizeObserver(function () {
        if (layoutFrame) return;
        layoutFrame = requestAnimationFrame(function () {
            layoutFrame = 0;
            syncLayout();
        });
    });
    layoutObserver.observe(track);
    slides.forEach(slide => layoutObserver.observe(slide.querySelector('.lwc-merch-item-copy')));
    document.fonts.ready.then(syncLayout);
})();


function setNewsletterStatus(message, isError = false) {
    newsletterStatus.textContent = message;
    newsletterStatus.classList.toggle('is-error', isError);
}

function renderNewsletterAction() {
    const retry = newsletterRetryEmail && newsletterRetryEmail === newsletterEmailInput.value.trim();
    newsletterSubmitButton.setAttribute('aria-disabled', String(newsletterSubmitting));
    newsletterSubmitButton.setAttribute('aria-busy', String(newsletterSubmitting));
    newsletterForm.setAttribute('aria-busy', String(newsletterSubmitting));
    newsletterSubmitButton.innerHTML = newsletterSubmitting
        ? '<i class="fas fa-spinner fa-spin" aria-hidden="true"></i> Subscribing…'
        : retry
            ? '<i class="fas fa-redo" aria-hidden="true"></i> Retry'
            : '<i class="fas fa-paper-plane" aria-hidden="true"></i> Subscribe';
}

function showNewsletterFailure(email, message) {
    newsletterRetryEmail = email;
    setNewsletterStatus(newsletterEmailInput.value.trim() === email ? message : `${email}: ${message}`, true);
}

newsletterEmailInput.addEventListener('input', () => {
    newsletterRetryEmail = '';
    newsletterEmailInput.removeAttribute('aria-invalid');
    setNewsletterStatus('');
    renderNewsletterAction();
});

newsletterForm.addEventListener('submit', async function (e) {
    e.preventDefault();
    if (newsletterSubmitting) return;

    const email = newsletterEmailInput.value.trim();
    newsletterEmailInput.value = email;
    if (!newsletterEmailInput.checkValidity()) {
        setNewsletterStatus(email ? 'Enter a valid email address.' : 'Enter your email address.', true);
        newsletterEmailInput.setAttribute('aria-invalid', 'true');
        newsletterEmailInput.focus();
        return;
    }

    newsletterEmailInput.removeAttribute('aria-invalid');
    newsletterRetryEmail = '';
    setNewsletterStatus('');
    newsletterSubmitting = true;
    renderNewsletterAction();
    try {
        const response = await fetchWithTimeout('/api/home/subscribe', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ email })
        }, 10000);
        const message = (await response.text()).trim();
        const alreadySubscribed = response.status === 400 && message === 'This email is already subscribed.';
        if (!response.ok && !alreadySubscribed) {
            const explanation = response.status === 400 && response.headers.get('content-type')?.includes('text/plain') && message
                ? message
                : 'Couldn’t confirm the subscription. Try again.';
            showNewsletterFailure(email, explanation);
            return;
        }

        // A response confirms its submitted address, not a later edit in the field.
        if (newsletterEmailInput.value.trim() === email) newsletterEmailInput.value = '';
        setNewsletterStatus(alreadySubscribed ? `${email} is already subscribed.` : `Subscribed with ${email}.`);
    } catch {
        showNewsletterFailure(email, 'Couldn’t confirm the subscription. Try again.');
    } finally {
        newsletterSubmitting = false;
        renderNewsletterAction();
    }
});

function adjustBitcoinAddressDisplay() {
    const btcAddressLink = document.getElementById("btcAddressLink");
    if (!btcAddressLink) return;
    const fullAddress = bitcoinDonationAddress;
    if (!fullAddress) return;
    // Reset to full address initially
    btcAddressLink.textContent = fullAddress;

    // Measure overflow
    if (btcAddressLink.scrollWidth > btcAddressLink.clientWidth) {
        // Truncate and add ellipsis if the address overflows
        const truncatedAddress = `${fullAddress.slice(0, 6)}...${fullAddress.slice(-6)}`;
        btcAddressLink.textContent = truncatedAddress;
    }

    // Always ensure the tooltip shows the full address
    btcAddressLink.title = fullAddress;
}

async function copyBitcoinAddress() {
    if (!bitcoinDonationAddress) {
        customAlert('Bitcoin donation address is unavailable right now.');
        return;
    }

    const copiedTextElement = document.getElementById('copiedText');
    clearTimeout(bitcoinCopyTimer);
    cancelBitcoinCopyExit?.();
    cancelBitcoinCopyExit = null;
    copiedTextElement.hidden = true;
    copiedTextElement.textContent = '';
    bitcoinCopyHelp.hidden = true;
    let copied = false;
    try {
        if (typeof navigator.clipboard?.writeText === 'function') {
            await navigator.clipboard.writeText(bitcoinDonationAddress);
            copied = true;
        }
    } catch (_) { /* Try the selection-based fallback below. */ }

    if (!copied) {
        // Keep this entry point independent of the shared dialog's load order.
        const previousFocus = document.activeElement;
        const textarea = document.createElement('textarea');
        textarea.value = bitcoinDonationAddress;
        textarea.readOnly = true;
        textarea.style.position = 'fixed';
        textarea.style.left = '-9999px';
        textarea.style.top = '0';
        document.body.appendChild(textarea);
        try {
            textarea.focus({ preventScroll: true });
            textarea.select();
            copied = document.execCommand('copy');
        } catch (_) { /* Manual selection remains available. */ }
        finally {
            textarea.remove();
            if (previousFocus instanceof HTMLElement) previousFocus.focus({ preventScroll: true });
        }
    }

    bitcoinAddressLink.style.display = 'none';
    bitcoinAddressFallback.hidden = copied;
    if (copied) {
        copiedTextElement.textContent = 'Copied';
        copiedTextElement.hidden = false;
        if (document.activeElement === bitcoinAddressFallback) bitcoinCopyButton.focus({ preventScroll: true });
        bitcoinCopyTimer = setTimeout(function () {
            copiedTextElement.hidden = true;
            // The address returns to the toast's place once the toast has left it.
            const restoreAddress = function () {
                if (!copiedTextElement.hidden) return;
                copiedTextElement.textContent = '';
                bitcoinAddressLink.style.display = '';
                adjustBitcoinAddressDisplay();
            };
            if (window.LwcMotion) cancelBitcoinCopyExit = window.LwcMotion.afterExit(copiedTextElement, restoreAddress);
            else restoreAddress();
        }, 2000);
    } else {
        bitcoinAddressFallback.value = bitcoinDonationAddress;
        bitcoinCopyHelp.hidden = false;
        bitcoinAddressFallback.focus({ preventScroll: true });
        bitcoinAddressFallback.select();
    }
}

// Fetch actual BTC data and update the progress
window.getSharedPrizeFund()
    .then(data => {
        const totalReceivedSatoshis = data.totalReceivedSatoshis;
        const totalReceivedBTC = totalReceivedSatoshis / 1e8;
        const prizeFundBTC = totalReceivedBTC * 0.9; // 90% of total received

        // Determine the goal dynamically
        let goalBTC;
        if (prizeFundBTC < 0.01) {
            goalBTC = 0.01;
        } else if (prizeFundBTC < 0.1) {
            goalBTC = 0.1;
        } else if (prizeFundBTC < 1) {
            goalBTC = 1;
        } else if (prizeFundBTC < 10) {
            goalBTC = 10;
        } else if (prizeFundBTC < 100) {
            goalBTC = 100;
        } else {
            goalBTC = Math.pow(10, Math.ceil(Math.log10(prizeFundBTC)));
        }

        // Calculate the percentage
        const progressPercentage = (prizeFundBTC / goalBTC) * 100;

        // Update the prize-pool progress fill
        document.querySelector('.btc-status-fill').style.width = progressPercentage + '%';

        // Update the BTC status with appropriate decimal places
        let displayPrizeFundBTC;
        if (prizeFundBTC >= 10) {
            displayPrizeFundBTC = prizeFundBTC.toFixed(0);
            goalBTC = goalBTC.toFixed(0);
        } else if (prizeFundBTC >= 1) {
            displayPrizeFundBTC = prizeFundBTC.toFixed(1);
            goalBTC = goalBTC.toFixed(1);
        } else if (prizeFundBTC >= 0.1) {
            displayPrizeFundBTC = prizeFundBTC.toFixed(2);
            goalBTC = goalBTC.toFixed(2);
        } else if (prizeFundBTC >= 0.01) {
            displayPrizeFundBTC = prizeFundBTC.toFixed(3);
            goalBTC = goalBTC.toFixed(3);
        } else if (prizeFundBTC >= 0.001) {
            displayPrizeFundBTC = prizeFundBTC.toFixed(4);
            goalBTC = goalBTC.toFixed(4);
        } else if (prizeFundBTC >= 0.0001) {
            displayPrizeFundBTC = prizeFundBTC.toFixed(5);
            goalBTC = goalBTC.toFixed(5);
        }
        else {
            displayPrizeFundBTC = prizeFundBTC.toFixed(6);
            goalBTC = goalBTC.toFixed(6);
        }

        document.getElementById('current-btc').textContent = displayPrizeFundBTC;
        document.getElementById('goal-btc').textContent = goalBTC;
        const btcStatusTrack = document.querySelector('.btc-status-track');
        btcStatusTrack.setAttribute('aria-valuenow', Math.round(progressPercentage));
        btcStatusTrack.setAttribute('aria-valuetext', `${displayPrizeFundBTC} of ${goalBTC} BTC`);
    })
    .catch(error => {
        console.error('Error fetching BTC data:', error);
    });


window.addEventListener('DOMContentLoaded', adjustBitcoinAddressDisplay);
window.addEventListener('resize', adjustBitcoinAddressDisplay);

// ===== Living scoreboard =====
// A fresh visit plays the standings like a scoreboard lighting up: podium
// years count up to their totals, then the preview rows settle in canonical
// rank order as the board scrolls into view. A press or key finishes everything at
// once, so motion never stands between the visitor and a click. Restored,
// linked, and reduced-motion visits stay still.
(function () {
    const main = document.querySelector('main.homepage-main');
    if (!main) return;
    const root = document.documentElement;
    const reduce = matchMedia('(prefers-reduced-motion: reduce)');
    const navigation = performance.getEntriesByType('navigation')[0];
    const query = new URLSearchParams(location.search);
    let cancelled = reduce.matches
        || !!location.hash
        || (navigation && navigation.type === 'back_forward')
        || ['athlete', 'filters', 'search', 'view'].some(key => query.has(key))
        || document.visibilityState === 'hidden'
        || !Element.prototype.animate;
    const allowBoardTransition = !cancelled;

    const easeOut = t => 1 - Math.pow(1 - t, 3);
    const running = new Set();
    const counted = new WeakSet();
    const settleAnimations = [];
    const cleanup = [];
    const board = main.querySelector('#athleteLeaderboard');
    const canRun = () => !cancelled && !reduce.matches && !document.hidden && !location.hash;

    function parseYears(text) {
        const match = /\d+(?:\.\d+)?/.exec(text);
        if (!match) return null;
        const value = parseFloat(match[0]);
        if (!(value > 0)) return null;
        return {
            value,
            decimals: (match[0].split('.')[1] || '').length,
            before: text.slice(0, match.index),
            after: text.slice(match.index + match[0].length)
        };
    }

    // Counts one figure up. The clock starts on the first frame that actually
    // paints, so a busy main thread cannot swallow the count; a card replaced
    // during hydration continues from the value its predecessor showed.
    const shown = new Map();
    function countUp(element, delay, duration, key) {
        if (!canRun() || counted.has(element) || !CSS.supports('content', 'attr(data-home-count) / ""')) return;
        counted.add(element);
        const finalText = element.textContent;
        const parsed = parseYears(finalText);
        if (!parsed) return;
        const from = key && shown.has(key) ? Math.min(shown.get(key), parsed.value) : 0;
        if (key && shown.has(key)) delay = 0;
        let start = null;
        let frameId = 0;
        let timer = 0;
        let textObserver;
        const track = {};
        const write = value => {
            if (key) shown.set(key, value);
            element.dataset.homeCount = parsed.before + value.toFixed(parsed.decimals) + parsed.after;
        };
        const stop = () => {
            running.delete(track);
            cancelAnimationFrame(frameId);
            clearTimeout(timer);
            textObserver?.disconnect();
            delete element.dataset.homeCount;
            element.style.removeProperty('--home-count-color');
            element.classList.remove('is-counting');
        };
        track.finish = () => {
            stop();
            if (key) shown.set(key, parsed.value);
            const host = element.closest('.podium-item, tr');
            if (canRun() && host && element.isConnected && element.textContent === finalText) host.classList.add('is-landed');
        };
        running.add(track);
        textObserver = new MutationObserver(() => {
            if (element.textContent !== finalText) stop();
        });
        textObserver.observe(element, { childList: true, characterData: true, subtree: true });
        element.style.setProperty('--home-count-color', element.style.getPropertyValue('--home-count-color') || getComputedStyle(element).color);
        element.classList.add('is-counting');
        const frame = now => {
            if (!running.has(track)) return;
            // Someone else (a ranking refresh) rewrote the figure: theirs wins.
            if (!canRun() || !element.isConnected || element.textContent !== finalText) { stop(); return; }
            if (start === null) start = now;
            const progress = Math.min(1, Math.max(0, (now - start - delay) / duration));
            write(from + (parsed.value - from) * easeOut(progress));
            if (progress >= 1) track.finish(); else frameId = requestAnimationFrame(frame);
        };
        write(from);
        frameId = requestAnimationFrame(frame);
        // Frames pause in background tabs; the figure must still end correct.
        timer = setTimeout(() => { if (running.has(track)) track.finish(); }, delay + duration + 2500);
    }

    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') finishAll(); });

    function finishAll() {
        cancelled = true;
        main.classList.remove('home-scoreboard-live');
        cleanup.splice(0).forEach(stop => stop());
        [...running].forEach(track => track.finish());
        settleAnimations.splice(0).forEach(animation => animation.cancel());
        board?.classList.remove('home-board-settling');
        main.querySelectorAll('.is-landed').forEach(element => element.classList.remove('is-landed'));
    }

    const realRows = () => [...main.querySelectorAll('#athleteLeaderboard tbody tr')]
        .filter(row => !row.matches('.tier-separator, .no-results, .leaderboard-skeleton-row') && row.getClientRects().length);

    // Fade rows in canonical order without moving their hitboxes. Cancelling
    // on pointer-down must not move a link away before pointer-up can click it.
    function settleBoard() {
        if (!canRun()) return false;
        const rows = realRows().slice(0, 12);
        if (rows.length < 3) return false;
        const easing = getComputedStyle(root).getPropertyValue('--lwc-ease-standard').trim() || 'cubic-bezier(0.2, 0, 0, 1)';
        const board = main.querySelector('#athleteLeaderboard');
        board.classList.add('home-board-settling');
        try { rows.forEach((row, index) => {
            const delay = index * 45;
            const animation = row.animate([
                { opacity: 0.65 },
                { opacity: 1 }
            ], { duration: 560, delay, easing, fill: 'backwards' });
            animation.id = 'home-board-settle';
            settleAnimations.push(animation);
            const figure = row.querySelector('.age-reduction-td .age-reduction');
            if (figure) countUp(figure, delay + 140, 640);
        }); } catch (_) { finishAll(); return true; }
        Promise.all(settleAnimations.map(animation => animation.finished.catch(() => {})))
            .then(() => board.classList.remove('home-board-settling'));
        return true;
    }

    if (!cancelled) {
        main.classList.add('home-scoreboard-live');
        const podiumDelay = { first: 120, second: 240, third: 360 };
        const countPodium = () => main.querySelectorAll('.podium-item:not(.podium-skeleton-item)').forEach(item => {
            const figure = item.querySelector('.podium-score .age-reduction');
            const place = ['first', 'second', 'third'].find(name => item.classList.contains(name)) || 'third';
            const identity = item.querySelector('a.athlete-name')?.getAttribute('href');
            if (figure) countUp(figure, podiumDelay[place], 1100, identity ? `${identity}:${figure.textContent}` : null);
        });
        countPodium();
        // Wait for the actual leaderboard-ready signal, not a hydration timer.
        let boardCheck = () => {};
        const podium = main.querySelector('.podium');
        if (podium) {
            const podiumObserver = new MutationObserver(() => { if (canRun()) countPodium(); });
            podiumObserver.observe(podium, { childList: true });
            cleanup.push(() => podiumObserver.disconnect());
        }

        const board = main.querySelector('#athleteLeaderboard');
        if (board && 'IntersectionObserver' in window) {
            let boardDone = false;
            const stopWatching = () => { boardDone = true; boardObserver.disconnect(); rowObserver.disconnect(); };
            const boardObserver = new IntersectionObserver(entries => {
                if (!canRun() || boardDone || document.getElementById('leaderboardStatus')?.textContent !== 'Leaderboard loaded.'
                    || !entries.some(entry => entry.isIntersecting)) return;
                if (settleBoard()) stopWatching();
            }, { threshold: 0.2 });
            boardCheck = () => { if (!canRun() || boardDone) return; boardObserver.unobserve(board); boardObserver.observe(board); };
            // Rows may arrive while the board is already on screen.
            const rowObserver = new MutationObserver(() => boardCheck());
            boardObserver.observe(board);
            rowObserver.observe(board, { childList: true, subtree: true });
            const status = document.getElementById('leaderboardStatus');
            if (status) rowObserver.observe(status, { childList: true, subtree: true });
            cleanup.push(stopWatching);
        }

        ['pointerdown', 'keydown', 'focusin', 'hashchange', 'popstate', 'pagehide'].forEach(name => {
            window.addEventListener(name, finishAll, { capture: true, passive: true });
            cleanup.push(() => window.removeEventListener(name, finishAll, true));
        });
        reduce.addEventListener('change', finishAll);
        cleanup.push(() => reduce.removeEventListener('change', finishAll));
    }

    // The board carries over to the full leaderboard: it stays on screen and
    // grows into its place on the next page instead of cutting away.
    window.addEventListener('pageswap', event => {
        finishAll();
        const url = event.activation && event.activation.entry && event.activation.entry.url;
        if (!allowBoardTransition || reduce.matches || location.hash || event.activation?.navigationType === 'traverse'
            || !event.viewTransition || !url) return;
        const destination = new URL(url);
        if (destination.origin !== location.origin || destination.search || destination.hash || !/^\/leaderboard\/?$/i.test(destination.pathname)) return;
        const named = [];
        [['#athleteLeaderboard', 'lwc-board']].forEach(([selector, name]) => {
            const element = main.querySelector(selector);
            const box = element && element.getBoundingClientRect();
            if (!box || box.bottom < 0 || box.top > innerHeight) return;
            element.style.viewTransitionName = name;
            named.push(element);
        });
        const clearNames = () => named.forEach(element => { element.style.viewTransitionName = ''; });
        event.viewTransition.finished.then(clearNames, clearNames);
    });
})();
