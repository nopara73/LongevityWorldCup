// Media that arrives after its box is painted fades in; anything already
// decoded (cache, bfcache, hydration swaps) appears at once. Without
// scripts nothing is hidden.
(function () {
    var root = document.documentElement;
    if (!window.MutationObserver || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    root.classList.add('lwc-media-fade');
    var selector = 'img.podium-portrait, img.portrait, .events-board img.avatar';
    function settle(img, instant) { img.setAttribute('data-lwc-media', instant ? 'instant' : 'ready'); }
    function track(img) {
        if (img.hasAttribute('data-lwc-media') && img.getAttribute('data-lwc-media') !== 'pending') return;
        if (img.complete) { settle(img, true); return; }
        img.setAttribute('data-lwc-media', 'pending');
    }
    function scan(node) {
        if (node.nodeType !== 1) return;
        if (node.matches(selector)) track(node);
        if (node.firstElementChild) node.querySelectorAll(selector).forEach(track);
    }
    function finish(event) {
        var img = event.target;
        if (img && img.tagName === 'IMG' && img.getAttribute('data-lwc-media') === 'pending') settle(img);
    }
    document.addEventListener('load', finish, true);
    document.addEventListener('error', finish, true);
    new MutationObserver(function (records) {
        for (var i = 0; i < records.length; i++) {
            var record = records[i];
            if (record.type === 'attributes') {
                // A new source on a painted portrait keeps the old one until it decodes.
                if (record.target.matches(selector)) track(record.target);
                continue;
            }
            for (var j = 0; j < record.addedNodes.length; j++) scan(record.addedNodes[j]);
        }
    }).observe(root, { childList: true, subtree: true, attributes: true, attributeFilter: ['src', 'srcset'] });
})();

const scrolledJoinButton = document.querySelector('.scrolled-button');
const joinGameBtn = document.querySelector('.join-game');
let lastKnownScrollPosition = 0;
let ticking = false;
let measuredStickyHeight = 0;
let measuredRootFontSize = '';
let lastStickyActionAvailable = null;

window.addEventListener('scroll', function () {
    lastKnownScrollPosition = window.scrollY;

    if (!ticking) {
        window.requestAnimationFrame(function () {
            handleScroll(lastKnownScrollPosition);
            ticking = false;
        });

        ticking = true;
    }
});
handleScroll(window.scrollY);

// Text and loaded fonts can change both headers without a scroll event.
const headerResizeObserver = new ResizeObserver(() => {
    measuredStickyHeight = 0;
    if (!ticking) {
        ticking = true;
        requestAnimationFrame(() => {
            handleScroll(window.scrollY, true);
            ticking = false;
        });
    }
});
for (const element of [document.querySelector('header[role="banner"]'),
    document.querySelector('header[role="banner"] .header-link'),
    document.getElementById('site-sticky-header'), joinGameBtn, scrolledJoinButton]) {
    if (element) headerResizeObserver.observe(element);
}
window.addEventListener('resize', () => handleScroll(window.scrollY, true));

function handleScroll(scrollPos, remeasure = false) {
    const compactLandscape = window.matchMedia('(max-width: 932px) and (max-height: 480px) and (orientation: landscape)').matches;
    const mainJoinButtonVisible = joinGameBtn && window.getComputedStyle(joinGameBtn).display !== 'none';
    const mainHeader = document.querySelector('header[role="banner"]');
    const stickyHeader = document.getElementById('site-sticky-header');
    if (!mainHeader || !stickyHeader) return;
    const actionAvailable = Boolean(mainJoinButtonVisible || compactLandscape);
    // Text enlargement can reach a scroll frame before resize callbacks run.
    const rootFontSize = getComputedStyle(document.documentElement).fontSize;

    // Measure the complete bar before it appears, so the first anchor jump
    // and keyboard scroll reserve the same space as subsequent ones.
    // Cache this geometry between layout changes, keeping scrolling cheap.
    if (remeasure || !measuredStickyHeight || lastStickyActionAvailable !== actionAvailable
        || measuredRootFontSize !== rootFontSize) {
        // Only a hidden bar is briefly shown to measure; a visible one keeps
        // any entrance that a resize callback lands in the middle of.
        const measuring = !stickyHeader.classList.contains('visible');
        if (measuring) {
            stickyHeader.classList.add('is-measuring');
            scrolledJoinButton?.classList.add('is-measuring');
        }
        const visibility = stickyHeader.style.visibility;
        const actionVisibility = scrolledJoinButton?.style.visibility;
        const actionDisplay = scrolledJoinButton?.style.display;
        stickyHeader.style.visibility = 'hidden';
        stickyHeader.classList.add('visible');
        if (scrolledJoinButton) {
            scrolledJoinButton.style.visibility = 'hidden';
            scrolledJoinButton.style.display = actionAvailable ? 'inline-flex' : 'none';
        }
        measuredStickyHeight = fitStickyHeader(stickyHeader);
        measuredRootFontSize = rootFontSize;
        lastStickyActionAvailable = actionAvailable;
        stickyHeader.style.visibility = visibility;
        if (scrolledJoinButton) scrolledJoinButton.style.visibility = actionVisibility;
        if (measuring) {
            // Restore the hidden state before starting a real entrance. A font
            // or resize callback can invalidate geometry on that same scroll.
            stickyHeader.classList.remove('visible');
            if (scrolledJoinButton) scrolledJoinButton.style.display = actionDisplay;
            stickyHeader.getBoundingClientRect();
            stickyHeader.classList.remove('is-measuring');
            scrolledJoinButton?.classList.remove('is-measuring');
        }
    }
    // A bar taller than the original banner must wait until that banner leaves
    // the viewport; otherwise enlarged text can cover the primary links at 0.
    const threshold = mainHeader.offsetHeight > measuredStickyHeight
        ? mainHeader.offsetHeight - measuredStickyHeight
        : mainHeader.offsetHeight;
    const willBeVisible = scrollPos >= threshold;
    // A focused sticky link must not disappear from the keyboard's location
    // when scrolling or resizing brings the original banner back into view.
    if (!willBeVisible && (stickyHeader.contains(document.activeElement) || document.activeElement === scrolledJoinButton)) {
        const target = document.activeElement === scrolledJoinButton && mainJoinButtonVisible
            ? joinGameBtn
            : mainHeader.querySelector('.header-link');
        target?.focus({ preventScroll: true });
    }
    stickyHeader.classList.toggle('visible', willBeVisible);
    stickyHeader.inert = !willBeVisible;
    stickyHeader.setAttribute('aria-hidden', String(!willBeVisible));
    if (scrolledJoinButton) {
        const actionVisible = willBeVisible && actionAvailable;
        scrolledJoinButton.inert = !actionVisible;
        scrolledJoinButton.setAttribute('aria-hidden', String(!actionVisible));
        scrolledJoinButton.style.display = actionVisible ? 'inline-flex' : 'none';
    }
}

function fitStickyHeader(stickyHeader) {
    const action = scrolledJoinButton?.getBoundingClientRect();
    const hasAction = action && action.width > 0;
    const right = hasAction ? innerWidth - action.right : 0;
    stickyHeader.style.setProperty('--site-sticky-action-space', hasAction ? `${action.width + right + 8}px` : '0px');
    stickyHeader.style.setProperty('--site-sticky-action-height', hasAction ? `${action.height}px` : '0px');
    stickyHeader.classList.remove('is-stacked');
    const style = getComputedStyle(stickyHeader);
    const link = stickyHeader.querySelector('.site-sticky-header-link');
    const available = stickyHeader.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
    const stacked = hasAction && link && link.getBoundingClientRect().width > available + 1;
    stickyHeader.classList.toggle('is-stacked', Boolean(stacked));
    const height = stickyHeader.getBoundingClientRect().height;
    if (hasAction) {
        scrolledJoinButton.style.top = `${stacked ? height - action.height - 4 : (height - action.height) / 2}px`;
    }
    document.documentElement.style.setProperty('--site-sticky-height', `${height}px`);
    return height;
}

const customAlertDialog = document.getElementById('custom-alert');
const customAlertMessage = document.getElementById('custom-alert-message');
const customAlertCloseButton = document.getElementById('custom-alert-close');
const loadingDialog = document.getElementById('loading-dialog');
const customAlertQueue = [];
let customAlertActive = null;
let customAlertPreviousFocus = null;
let customAlertOwnsScrollLock = false;
let loadingDialogPreviousFocus = null;
let loadingDialogOwnsScrollLock = false;

function closeCustomAlert() {
    if (customAlertDialog.hidden) {
        return;
    }

    const dismissed = customAlertActive;
    const restoreFocus = customAlertDialog.contains(document.activeElement) || document.activeElement === document.body;
    const previousFocus = customAlertPreviousFocus;
    customAlertActive = null;
    customAlertDialog.hidden = true;
    customAlertPreviousFocus = null;

    if (!customAlertQueue.length) {
        if (customAlertOwnsScrollLock) {
            if (loadingDialog.hidden) document.body.classList.remove('no-scroll');
            else loadingDialogOwnsScrollLock = true;
        }
        customAlertOwnsScrollLock = false;
    }

    if (!loadingDialog.hidden) {
        loadingDialog.querySelector('.loading-dialog-panel')?.focus({ preventScroll: true });
    } else if (restoreFocus && previousFocus?.isConnected && !previousFocus.closest('[hidden], [inert]')
        && !previousFocus.disabled && previousFocus.getClientRects().length) {
        previousFocus.focus({ preventScroll: true });
    }

    dismissed?.resolve();
    // Existing .then callbacks (including field focus/navigation) run before the next alert opens.
    queueMicrotask(showNextCustomAlert);
}

customAlertDialog.addEventListener('keydown', function (event) {
    if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        closeCustomAlert();
        return;
    }

    trapFocusWithin(customAlertDialog, event);
    if (event.key === 'Tab') event.stopPropagation();
});

document.addEventListener('keydown', function (event) {
    if (!loadingDialog.hidden && customAlertDialog.hidden && event.key === 'Tab') {
        event.preventDefault();
        loadingDialog.querySelector('.loading-dialog-panel')?.focus({ preventScroll: true });
        return;
    }
    if (customAlertDialog.hidden || customAlertDialog.contains(event.target)) return;
    if (event.key === 'Escape') {
        event.preventDefault();
        event.stopPropagation();
        closeCustomAlert();
    } else trapFocusWithin(customAlertDialog, event);
});

loadingDialog.addEventListener('keydown', function (event) {
    if (event.key !== 'Tab') {
        return;
    }

    event.preventDefault();
    loadingDialog.querySelector('.loading-dialog-panel')?.focus();
});

const CUSTOM_ALERT_TONE_ICONS = {
    success: 'fa-check',
    warning: 'fa-exclamation',
    danger: 'fa-xmark',
    info: 'fa-info'
};
const CUSTOM_ALERT_ID_PATTERN = /\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/gi;

// The tone only picks the medallion and accent; every caller keeps its own wording.
function inferCustomAlertTone(message) {
    const text = String(message);
    if (/\b(fail(?:ed|ure|ing|s)?|error|interrupted|unsuccessful(?:ly)?|rejected|denied|declined|timeout|timed out)\b/i.test(text)) return 'danger';
    // A success word inside a negation, uncertainty or validation request must never celebrate.
    if (/\b(not|never|no|nothing|none|neither|cannot|unable|may|maybe|might|perhaps|possibly|could|should|would|being|if|pending|awaiting|unconfirmed|unverified|uncertain|unclear|unknown|missing|invalid|required|must be|unavailable|skipped|cancelled|canceled|expired|unsent|try again|enter|select|provide|please complete|change at least)\b|\b\w+n['’]t\b/i.test(text)) return 'warning';
    const future = /\b(will|can|when|once)\b/i.exec(text);
    if (future && /\b(received|submitted|sent|saved|copied)\b/i.test(text.slice(future.index + future[0].length))) return 'warning';
    if (text.includes('?')) return 'info';
    if (/\b(received|submitted|sent|saved|copied)\b/i.test(text)) return 'success';
    return 'info';
}

function renderCustomAlertMessage(message) {
    const text = String(message);
    customAlertMessage.tabIndex = -1;
    customAlertMessage.scrollTop = 0;
    const separator = /\r?\n[\t ]*\r?\n/.exec(text);
    const lead = document.createElement('span');
    lead.className = 'custom-alert-lead';
    lead.append(splitCustomAlertIds(separator ? text.slice(0, separator.index) : text));
    customAlertMessage.replaceChildren(lead);
    if (separator) {
        // Preserve the exact separator bytes outside the styled detail block.
        customAlertMessage.append(document.createTextNode(separator[0]));
        const detail = document.createElement('span');
        detail.className = 'custom-alert-detail';
        detail.append(splitCustomAlertIds(text.slice(separator.index + separator[0].length)));
        customAlertMessage.append(detail);
    }
}

function splitCustomAlertIds(text) {
    const nodes = document.createDocumentFragment();
    let last = 0;
    for (const match of text.matchAll(CUSTOM_ALERT_ID_PATTERN)) {
        if (match.index > last) nodes.append(document.createTextNode(text.slice(last, match.index)));
        const id = document.createElement('code');
        id.className = 'custom-alert-id';
        id.textContent = match[0];
        nodes.append(id);
        last = match.index + match[0].length;
    }
    if (last < text.length) nodes.append(document.createTextNode(text.slice(last)));
    return nodes;
}

function customAlert(message, options) {
    return new Promise(resolve => {
        customAlertQueue.push({ message: String(message), options, resolve });
        showNextCustomAlert();
    });
}

function showNextCustomAlert() {
    if (customAlertActive || !customAlertQueue.length) return;
    const entry = customAlertQueue.shift();
    customAlertActive = entry;
    const activeElement = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    customAlertPreviousFocus = !loadingDialog.hidden && loadingDialog.contains(activeElement)
        ? loadingDialogPreviousFocus : activeElement;
    const { message, options } = entry;
    const requestedTone = options && options.tone;
    const tone = Object.prototype.hasOwnProperty.call(CUSTOM_ALERT_TONE_ICONS, requestedTone)
        ? requestedTone
        : inferCustomAlertTone(message);
    customAlertDialog.dataset.tone = tone;
    const mark = customAlertDialog.querySelector('.custom-alert-mark i');
    if (mark) mark.className = `fas ${CUSTOM_ALERT_TONE_ICONS[tone]}`;
    renderCustomAlertMessage(message);

    customAlertDialog.hidden = false;
    customAlertOwnsScrollLock ||= !document.body.classList.contains('no-scroll');
    document.body.classList.add('no-scroll');
    requestAnimationFrame(() => {
        updateCustomAlertMessageFocus();
        if (customAlertActive === entry && !customAlertDialog.hidden
            && (document.activeElement === activeElement || document.activeElement === document.body
                || customAlertDialog.contains(document.activeElement))) {
            customAlertCloseButton.focus({ preventScroll: true });
        }
    });
}

function updateCustomAlertMessageFocus() {
    if (!customAlertDialog.hidden) {
        customAlertMessage.tabIndex = customAlertMessage.scrollHeight > customAlertMessage.clientHeight + 1 ? 0 : -1;
    }
}

const customAlertMessageResizeObserver = new ResizeObserver(updateCustomAlertMessageFocus);
customAlertMessageResizeObserver.observe(customAlertMessage);

function showLoading() {
    if (!loadingDialog.hidden) return;
    const activeElement = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    loadingDialogPreviousFocus = !customAlertDialog.hidden && customAlertDialog.contains(activeElement)
        ? customAlertPreviousFocus : activeElement;
    loadingDialogOwnsScrollLock = !document.body.classList.contains('no-scroll');

    loadingDialog.hidden = false;

    document.body.classList.add('no-scroll');
    if (customAlertDialog.hidden) loadingDialog.querySelector('.loading-dialog-panel')?.focus({ preventScroll: true });
}

function hideLoading() {
    if (loadingDialog.hidden) return;
    const restoreFocus = loadingDialog.contains(document.activeElement) || document.activeElement === document.body;
    loadingDialog.hidden = true;

    if (loadingDialogOwnsScrollLock) {
        if (customAlertDialog.hidden) document.body.classList.remove('no-scroll');
        else customAlertOwnsScrollLock = true;
    }
    loadingDialogOwnsScrollLock = false;

    if (customAlertDialog.hidden && restoreFocus && loadingDialogPreviousFocus?.isConnected
        && !loadingDialogPreviousFocus.closest('[hidden], [inert]') && !loadingDialogPreviousFocus.disabled
        && loadingDialogPreviousFocus.getClientRects().length) {
        loadingDialogPreviousFocus.focus({ preventScroll: true });
    }

    loadingDialogPreviousFocus = null;
}

function trapFocusWithin(container, event) {
    if (event.key !== 'Tab') {
        return;
    }

    const focusable = Array.from(container.querySelectorAll('a[href], button, input, select, textarea, [tabindex]:not([tabindex="-1"])'))
        .filter(element => !element.disabled && element.offsetParent !== null);

    if (!focusable.length) {
        event.preventDefault();
        container.querySelector('[tabindex="-1"]')?.focus();
        return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement;

    if (!container.contains(active) || !focusable.includes(active)) {
        event.preventDefault();
        (event.shiftKey ? last : first).focus();
    } else if (event.shiftKey && active === first) {
        event.preventDefault();
        last.focus();
    } else if (!event.shiftKey && active === last) {
        event.preventDefault();
        first.focus();
    }
}

customAlertCloseButton.addEventListener('click', function () {
    closeCustomAlert();
});

function fetchWithTimeout(url, options = {}, timeout = 10000) {
    const timeoutController = typeof AbortController !== 'undefined' && !options.signal
        ? new AbortController()
        : null;
    const fetchOptions = timeoutController
        ? { ...options, signal: timeoutController.signal }
        : options;

    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
            if (timeoutController) timeoutController.abort();
            reject(new Error('Request timed out'));
        }, timeout);

        fetch(url, fetchOptions)
            .then(response => {
                clearTimeout(timer);
                resolve(response);
            })
            .catch(err => {
                clearTimeout(timer);
                reject(err && err.name === 'AbortError' ? new Error('Request timed out') : err);
            });
    });
}

// The tiny head hook observes the incoming native transition before blocking
// resources: a slow arrival can be skipped before pagereveal, which then has
// no transition. Keep later lifecycle handling here, out of every HTML page.
(function () {
    const observed = new WeakSet();
    function observe(transition) {
        if (!transition || observed.has(transition)) return;
        observed.add(transition);
        transition.ready.catch(error => {
            if (!(error instanceof DOMException)
                || !['AbortError', 'InvalidStateError', 'TimeoutError'].includes(error.name)) throw error;
        });
    }
    for (const name of ['pageswap', 'pagereveal']) {
        window.addEventListener(name, event => observe(event.viewTransition));
    }

    if (!document.querySelector('link[href*="entry-journey"]') || !window.navigation) return;
    const journeyPath = /^\/(?:play|join|select-athlete|dashboard|pheno-age|bortz-age|apply|review)?\/?$/i;
    const optOut = document.createElement('style');
    optOut.media = 'not all';
    optOut.textContent = '@view-transition { navigation: none; }';
    document.head.append(optOut);
    const restore = () => { optOut.media = 'not all'; };
    window.navigation.addEventListener('navigate', event => {
        if (event.destination.sameDocument) return;
        const destination = new URL(event.destination.url);
        optOut.media = destination.origin === location.origin && journeyPath.test(destination.pathname)
            ? 'not all' : 'all';
        event.signal.addEventListener('abort', restore, { once: true });
    });
    window.addEventListener('pageshow', restore);
})();
