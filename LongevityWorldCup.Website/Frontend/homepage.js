let bitcoinDonationAddress = '';
let newsletterSubmitting = false;
let newsletterRetryEmail = '';
const newsletterForm = document.getElementById('newsletter-form');
const newsletterEmailInput = document.getElementById('emailInput');
const newsletterSubmitButton = newsletterForm.querySelector('button[type="submit"]');
const newsletterStatus = document.getElementById('newsletterStatus');
const bitcoinAddressLink = document.getElementById('btcAddressLink');
const bitcoinCopyButton = document.getElementById('bitcoinCopyButton');

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
let homepageHighlightsWaitAttempts = 0;

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

function loadHomepageHighlights(rowCount, reason = 'unknown') {
    homepageHighlightsRows = rowCount;
    if (reason === 'fit') {
        window.__homepageLastRenderIds = [];
    }
    callScoped('events-root-index', function () {
        loadEventsTable(rowCount, true, null, true, {
            preserveExistingRows: reason === 'grow',
            useSharedEvents: true
        });
    });
}

function maybeGrowHomepageHighlights() {
    const highlightsRoot = document.getElementById('events-root-index');
    const highlightsBoard = highlightsRoot ? highlightsRoot.querySelector('.events-board') : null;
    const highlightsTable = highlightsRoot ? highlightsRoot.querySelector('#eventsTable') : null;
    const highlightsBody = highlightsRoot ? highlightsRoot.querySelector('#eventsTable tbody') : null;
    const swagCard = document.getElementById('lwc-countdown-box');
    if (window.innerWidth <= 991) {
        return;
    }
    if (!highlightsBoard || !highlightsTable || !highlightsBody || !swagCard) {
        return;
    }

    const renderedRows = highlightsBody.querySelectorAll('tr.main-row').length;
    if (!renderedRows) {
        homepageHighlightsWaitAttempts += 1;
        homepageHighlightsMeasureTimer = window.setTimeout(maybeGrowHomepageHighlights, 180);
        return;
    }

    if (renderedRows < homepageHighlightsRows) {
        if (homepageHighlightsWaitAttempts < 8) {
            homepageHighlightsWaitAttempts += 1;
            homepageHighlightsMeasureTimer = window.setTimeout(maybeGrowHomepageHighlights, 180);
        }
        return;
    }

    homepageHighlightsWaitAttempts = 0;

    const highlightsContentHeight = Math.round(highlightsTable.getBoundingClientRect().height);
    const highlightsBoardHeight = Math.round(highlightsBoard.getBoundingClientRect().height);
    const swagHeight = Math.round(swagCard.getBoundingClientRect().height);
    const heightGap = swagHeight - highlightsContentHeight;
    const canRequestMoreRows = renderedRows >= homepageHighlightsRows && homepageHighlightsRows < homepageHighlightsMaxRows;
    if (heightGap > 28 && canRequestMoreRows) {
        loadHomepageHighlights(homepageHighlightsRows + 1, 'grow');
        homepageHighlightsWaitAttempts = 0;
        homepageHighlightsMeasureTimer = window.setTimeout(maybeGrowHomepageHighlights, 220);
    }
}

function fitHomepageHighlights() {
    window.clearTimeout(homepageHighlightsMeasureTimer);
    homepageHighlightsWaitAttempts = 0;
    const initialRows = window.innerWidth > 991 ? 1 : homepageHighlightsBaseRows;
    loadHomepageHighlights(initialRows, 'fit');
    homepageHighlightsMeasureTimer = window.setTimeout(maybeGrowHomepageHighlights, 220);
}

document.addEventListener('DOMContentLoaded', function () {
    placeHomepageHighlightsForVisit();
    fitHomepageHighlights();
    LoadLeaderboard(true, 10);
    stabilizeHomepageFragmentTarget();

    window.addEventListener('resize', function () {
        window.clearTimeout(homepageHighlightsResizeTimer);
        homepageHighlightsResizeTimer = window.setTimeout(fitHomepageHighlights, 180);
    });
});

document.addEventListener('DOMContentLoaded', function () {
    const merchCarousel = document.getElementById('lwc-merch-mobile-carousel');
    if (!merchCarousel) {
        return;
    }

    const slides = Array.from(merchCarousel.querySelectorAll('.lwc-merch-mobile-slide'));
    const dots = Array.from(merchCarousel.querySelectorAll('.lwc-merch-mobile-dot'));
    if (slides.length <= 1) {
        return;
    }

    let activeIndex = 0;

    function setActiveSlide(index) {
        activeIndex = (index + slides.length) % slides.length;

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

    dots.forEach((dot, dotIndex) => {
        dot.addEventListener('click', function () {
            setActiveSlide(dotIndex);
        });
    });

    setActiveSlide(0);
});


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
    const container = btcAddressLink.parentElement;

    // Reset to full address initially
    btcAddressLink.textContent = fullAddress;

    // Measure overflow
    if (btcAddressLink.scrollWidth > container.offsetWidth) {
        // Truncate and add ellipsis if the address overflows
        const truncatedAddress = `${fullAddress.slice(0, 6)}...${fullAddress.slice(-6)}`;
        btcAddressLink.textContent = truncatedAddress;
    }

    // Always ensure the tooltip shows the full address
    btcAddressLink.title = fullAddress;
}

// Copy function
function copyBitcoinAddress() {
    if (!bitcoinDonationAddress) {
        customAlert('Bitcoin donation address is unavailable right now.');
        return;
    }

    navigator.clipboard.writeText(bitcoinDonationAddress).then(function () {
        // Hide the BTC address
        document.getElementById('btcAddressLink').style.display = 'none';
        // Show the 'Copied' text
        const copiedTextElement = document.getElementById('copiedText');
        copiedTextElement.style.display = 'inline';

        // Restore the address after the concise text confirmation.
        setTimeout(function () {
            // Hide 'Copied' text
            copiedTextElement.style.display = 'none';
            // Show BTC address
            document.getElementById('btcAddressLink').style.display = 'inline';
        }, 2000);
    }, function (err) {
        customAlert('Failed to copy the address');
    });
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
