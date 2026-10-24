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
            : document.activeElement?.matches('.site-menu-toggle')
                ? mainHeader.querySelector('.site-menu-toggle') : mainHeader.querySelector('.header-link');
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
    const toggle = stickyHeader.querySelector('.site-menu-toggle')?.getBoundingClientRect();
    const hasAction = action && action.width > 0;
    const hasToggle = toggle && toggle.width > 0;
    const controls = [hasAction && action, hasToggle && toggle].filter(Boolean);
    const actionHeight = Math.max(0, ...controls.map(rect => rect.height));
    const reserved = controls.length ? document.documentElement.clientWidth - Math.min(...controls.map(rect => rect.left)) + 8 : 0;
    stickyHeader.style.setProperty('--site-sticky-action-space', `${reserved}px`);
    stickyHeader.style.setProperty('--site-sticky-action-height', `${actionHeight}px`);
    stickyHeader.classList.remove('is-stacked');
    const style = getComputedStyle(stickyHeader);
    const link = stickyHeader.querySelector('.site-sticky-header-link');
    const available = stickyHeader.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
    // offsetWidth is the layout box; the arrival animation must not change fitting.
    const stacked = controls.length && link && link.offsetWidth > available + 1;
    stickyHeader.classList.toggle('is-stacked', Boolean(stacked));
    // Let the lockup settle from a larger size only while it stays clear of
    // the action row. The entrance must fit as well as its final layout.
    const fittedStyle = getComputedStyle(stickyHeader);
    const arrivalWidth = stickyHeader.clientWidth - parseFloat(fittedStyle.paddingLeft) - parseFloat(fittedStyle.paddingRight);
    const height = stickyHeader.getBoundingClientRect().height;
    const rowGap = stacked && link ? height - actionHeight - 4 - link.offsetTop - link.offsetHeight : Infinity;
    const arrivalY = Math.min(6, Math.max(0, rowGap - 1));
    const verticalScale = link?.offsetHeight ? 1 + 2 * Math.max(0, rowGap - arrivalY - 1) / link.offsetHeight : 1;
    stickyHeader.style.setProperty('--site-sticky-arrival-scale', String(link?.offsetWidth
        ? Math.max(1, Math.min(1.22, verticalScale, arrivalWidth / link.offsetWidth)) : 1));
    stickyHeader.style.setProperty('--site-sticky-arrival-y', `${arrivalY}px`);
    if (hasAction) {
        scrolledJoinButton.style.top = `${stacked ? height - actionHeight - 4 + (actionHeight - action.height) / 2 : (height - action.height) / 2}px`;
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

    // Body or a removed opener cannot receive focus. Do not leave the old
    // control focused while a discrete display transition retains its paint.
    if (customAlertDialog.contains(document.activeElement)) document.activeElement.blur();
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

    if (loadingDialog.contains(document.activeElement)) document.activeElement.blur();
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

    if (!document.querySelector('link[href*="entry-journey"]')) return;
    const journeyPath = /^\/(?:play|join|select-athlete|dashboard|pheno-age|bortz-age|apply|review)?\/?$/i;
    // Decide at capture time, using the final URL after redirects. A stylesheet
    // change during navigate can miss the browser's transition opt-in snapshot.
    window.addEventListener('pageswap', event => {
        const url = event.activation?.entry?.url;
        if (!event.viewTransition || !url) return;
        const destination = new URL(url);
        const homeBoard = location.pathname === '/' && !location.search && !location.hash
            && !destination.search && !destination.hash && /^\/leaderboard\/?$/i.test(destination.pathname)
            && event.activation?.navigationType !== 'traverse'
            && !matchMedia('(prefers-reduced-motion: reduce)').matches;
        if (destination.origin !== location.origin || (!journeyPath.test(destination.pathname) && !homeBoard)) {
            event.viewTransition.skipTransition();
        }
    });
})();

// The navigation dialog mirrors the footer. Native modality keeps background
// links out of keyboard and assistive-technology navigation, including late content.
(function initializeSiteNavigation() {
    // Parsing an HTML fragment during the main parse can release Chromium's
    // pending rel=expect links. Build only once the receiving page is complete,
    // so native transitions still capture the server-rendered board.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initializeSiteNavigation, { once: true });
        return;
    }
    // Script-only controls stay in the shared asset, keeping page HTML small.
    const primary = document.createElement('button');
    primary.type = 'button';
    primary.className = 'site-menu-toggle';
    primary.hidden = true;
    primary.setAttribute('aria-label', 'Menu');
    primary.setAttribute('aria-expanded', 'false');
    primary.setAttribute('aria-controls', 'site-menu');
    primary.setAttribute('aria-haspopup', 'dialog');
    primary.innerHTML = '<span class="site-menu-toggle-bars" aria-hidden="true"><span></span><span></span><span></span></span>';
    document.querySelector('.site-header-actions')?.append(primary);
    const stickyToggle = primary.cloneNode(true);
    stickyToggle.classList.add('site-sticky-menu-toggle');
    document.getElementById('site-sticky-header')?.append(stickyToggle);
    const dialog = document.createElement('dialog');
    dialog.id = 'site-menu';
    dialog.className = 'site-menu';
    dialog.hidden = true;
    dialog.setAttribute('aria-label', 'Site menu');
    dialog.innerHTML = `<div class="site-menu-backdrop" data-site-menu-close></div>
        <nav class="site-menu-panel" aria-label="Site" tabindex="-1">
        <div class="site-menu-toolbar"><span class="site-menu-grip" aria-hidden="true"></span><button type="button" class="site-menu-close" aria-label="Close menu" data-site-menu-close><span aria-hidden="true"></span></button></div>
        <div class="site-menu-body"></div></nav>`;
    document.body.append(dialog);
    const stickyHeader = document.getElementById('site-sticky-header');
    const menu = document.getElementById('site-menu');
    const panel = menu?.querySelector('.site-menu-panel');
    const body = menu?.querySelector('.site-menu-body');
    const toggles = Array.from(document.querySelectorAll('.site-menu-toggle'));
    const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)');
    const sheetQuery = matchMedia('(max-width: 640px)');
    let lastY = window.scrollY;
    let travel = 0;
    let anchorScroll = false;
    let anchorTimer = 0;
    function setTucked(tucked) {
        if (!stickyHeader) return;
        if (tucked && (stickyHeader.contains(document.activeElement)
            || document.activeElement === scrolledJoinButton || menu?.open || anchorScroll)) tucked = false;
        stickyHeader.classList.toggle('is-tucked', tucked);
        scrolledJoinButton?.classList.toggle('is-tucked', tucked);
    }
    function reveal() { travel = 0; lastY = window.scrollY; setTucked(false); }
    function revealAnchor() {
        anchorScroll = true;
        reveal();
        clearTimeout(anchorTimer);
        anchorTimer = setTimeout(() => { anchorScroll = false; }, 250);
    }
    window.addEventListener('scroll', () => {
        const y = window.scrollY;
        const delta = y - lastY;
        lastY = y;
        if (anchorScroll) { revealAnchor(); return; }
        if (Math.abs(delta) > 600) { reveal(); return; }
        if (!stickyHeader?.classList.contains('visible') || y < 1) { reveal(); return; }
        travel = Math.sign(delta) === Math.sign(travel) ? travel + delta : delta;
        if (travel > 48) setTucked(true);
        else if (travel < -12) setTucked(false);
    }, { passive: true });
    document.addEventListener('click', event => {
        const link = event.target instanceof Element && event.target.closest('a[href]');
        if (link && link.origin === location.origin && link.pathname === location.pathname && link.hash) revealAnchor();
    }, true);
    window.addEventListener('hashchange', revealAnchor);
    window.addEventListener('pageshow', reveal);
    stickyHeader?.addEventListener('focusin', reveal);
    scrolledJoinButton?.addEventListener('focus', reveal);

    if (!menu || !panel || !body || !toggles.length) return;
    let builtFrom = null;
    function build() {
        const footer = document.querySelector('.footer');
        if (!footer || builtFrom === footer) return Boolean(footer);
        builtFrom = footer;
        const nodes = [];
        for (const [selector, className] of [['.footer-column--explore', 'site-menu-pages'], ['.footer-column--follow', 'site-menu-follow']]) {
            const source = footer.querySelector(selector);
            if (!source) continue;
            const copy = source.cloneNode(true);
            copy.className = source.className + ' ' + className;
            copy.querySelectorAll('[id], [data-label-dismissed]').forEach(element => {
                element.removeAttribute('id');
                element.removeAttribute('data-label-dismissed');
            });
            copy.querySelectorAll('.nav-glide').forEach(element => element.remove());
            nodes.push(copy);
        }
        body.replaceChildren(...nodes);
        body.querySelectorAll('.footer-link').forEach((link, index) => link.style.setProperty('--site-menu-i', String(index)));
        window.LwcNavGlide?.(body.querySelector('.site-menu-pages'));
        return true;
    }
    let navigationAvailable = null;
    const initialize = () => {
        const footer = document.querySelector('.footer');
        const available = Boolean(footer && getComputedStyle(footer).display !== 'none');
        if (available === navigationAvailable) return;
        navigationAvailable = available;
        document.body.classList.toggle('has-site-menu', available);
        toggles.forEach(toggle => { toggle.hidden = !available; });
        if (available) {
            document.querySelector('.site-header-actions')?.append(primary);
            stickyHeader?.append(stickyToggle);
            document.body.append(menu);
        } else {
            close(false, true);
            toggles.forEach(toggle => toggle.remove());
            menu.remove();
        }
        handleScroll(window.scrollY, true);
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize, { once: true });
    else initialize();
    new MutationObserver(initialize).observe(document.body, { attributes: true, attributeFilter: ['class'] });

    let opener = null;
    let closeTimer = 0;
    let drag = null;
    const isOpen = () => menu.open && !menu.classList.contains('is-closing');
    function resetDrag() {
        if (drag && panel.hasPointerCapture(drag.id)) panel.releasePointerCapture(drag.id);
        drag = null;
        panel.classList.remove('is-dragging');
        panel.style.removeProperty('translate');
        menu.style.removeProperty('--site-menu-drag');
    }
    function open(fromToggle) {
        if (isOpen() || !customAlertDialog.hidden || !loadingDialog.hidden || !build()) return;
        clearTimeout(closeTimer);
        opener = fromToggle || document.activeElement;
        resetDrag();
        menu.classList.remove('is-closing');
        menu.dataset.layout = sheetQuery.matches ? 'sheet' : 'panel';
        positionPanel(opener);
        menu.hidden = false;
        menu.toggleAttribute('data-page-scrollbar', innerWidth > document.documentElement.clientWidth);
        document.body.classList.add('site-menu-open');
        menu.showModal();
        toggles.forEach(toggle => toggle.setAttribute('aria-expanded', 'true'));
        reveal();
        panel.getBoundingClientRect();
        menu.classList.add('is-open');
        const current = body.querySelector('.site-menu-pages [aria-current="page"]') || body.querySelector('.footer-link');
        (current || panel).focus({ preventScroll: true });
        if (current) {
            const link = current.getBoundingClientRect(), viewport = body.getBoundingClientRect();
            body.scrollTop += Math.max(0, link.bottom - viewport.bottom) + Math.min(0, link.top - viewport.top);
        }
    }
    function finishClose(restoreFocus) {
        clearTimeout(closeTimer);
        const previous = opener;
        opener = null;
        menu.close();
        menu.hidden = true;
        menu.classList.remove('is-open', 'is-closing');
        document.body.classList.remove('site-menu-open');
        resetDrag();
        const usable = element => {
            if (!element?.isConnected || element.closest('[hidden], [inert]')) return false;
            const rect = element.getBoundingClientRect();
            return rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.top < innerHeight;
        };
        if (restoreFocus) {
            reveal();
            const target = usable(previous) ? previous : toggles.find(usable);
            target?.focus({ preventScroll: true });
        }
        if (menu.contains(document.activeElement)) document.activeElement.blur();
    }
    function close(restoreFocus = true, immediately = false) {
        if (!menu.open) return;
        resetDrag();
        menu.classList.remove('is-open');
        menu.classList.add('is-closing');
        toggles.forEach(toggle => toggle.setAttribute('aria-expanded', 'false'));
        clearTimeout(closeTimer);
        if (immediately || reduceMotion.matches) finishClose(restoreFocus);
        else closeTimer = setTimeout(() => finishClose(restoreFocus), 240);
    }
    function positionPanel(toggle) {
        const rect = (toggle || toggles[0]).getBoundingClientRect();
        // A short landscape or enlarged banner can leave no room beneath its
        // toggle. Keep a usable viewport-contained panel in that case.
        const top = Math.max(12, Math.min(rect.bottom + 10, innerHeight - 240));
        menu.style.setProperty('--site-menu-top', Math.round(top) + 'px');
        menu.style.setProperty('--site-menu-right', Math.max(12, Math.round(innerWidth - rect.right)) + 'px');
        menu.style.setProperty('--site-menu-origin', Math.round(rect.width / 2) + 'px');
    }
    function fitLabels() {
        if (!isOpen()) return;
        const viewport = body.getBoundingClientRect();
        for (const label of body.querySelectorAll('.site-menu-follow .footer-link-label')) {
            label.style.removeProperty('--footer-label-shift');
            label.classList.remove('is-below');
            if (getComputedStyle(label).clipPath !== 'none') continue;
            let rect = label.getBoundingClientRect();
            if (rect.top < viewport.top + 3) {
                label.classList.add('is-below');
                rect = label.getBoundingClientRect();
            }
            const shift = Math.max(viewport.left + 3 - rect.left, Math.min(0, viewport.right - 3 - rect.right));
            label.style.setProperty('--footer-label-shift', `${shift}px`);
        }
    }
    for (const eventName of ['focusin', 'pointerover', 'scroll']) {
        body.addEventListener(eventName, () => requestAnimationFrame(fitLabels), { passive: true });
    }
    window.addEventListener('resize', () => requestAnimationFrame(fitLabels));
    toggles.forEach(toggle => toggle.addEventListener('click', () => isOpen() ? close() : open(toggle)));
    menu.addEventListener('click', event => {
        if (!(event.target instanceof Element)) return;
        if (event.target.closest('[data-site-menu-close]')) close();
        else if (event.target.closest('a[href]')) close(true, true);
    });
    menu.addEventListener('cancel', event => { event.preventDefault(); close(); });
    menu.addEventListener('keydown', event => {
        if (event.key === 'Escape') { event.preventDefault(); event.stopPropagation(); close(); }
        else trapFocusWithin(panel, event);
    });
    sheetQuery.addEventListener('change', () => close(true, true));
    window.addEventListener('pagehide', () => close(false, true));
    window.addEventListener('resize', () => { if (isOpen()) { resetDrag(); positionPanel(opener); } });
    reduceMotion.addEventListener('change', () => {
        resetDrag();
        if (menu.classList.contains('is-closing')) finishClose(true);
    });
    // Alert/loading panels can arrive asynchronously while navigation is open.
    const overlayObserver = new MutationObserver(() => {
        if (menu.open && (!customAlertDialog.hidden || !loadingDialog.hidden)) {
            const returnFocus = opener;
            close(false, true);
            if (menu.contains(customAlertPreviousFocus)) customAlertPreviousFocus = returnFocus;
            if (menu.contains(loadingDialogPreviousFocus)) loadingDialogPreviousFocus = returnFocus;
            if (!customAlertDialog.hidden) customAlertCloseButton.focus({ preventScroll: true });
            else loadingDialog.querySelector('.loading-dialog-panel')?.focus({ preventScroll: true });
        }
    });
    for (const overlay of [customAlertDialog, loadingDialog]) overlayObserver.observe(overlay, { attributes: true, attributeFilter: ['hidden'] });

    // Only the grip owns a drag. The link area always keeps native vertical
    // scrolling; touch-action on an ancestor must not suppress that scrolling.
    panel.addEventListener('pointerdown', event => {
        if (menu.dataset.layout !== 'sheet' || event.pointerType === 'mouse' || !isOpen()
            || !event.target.closest('.site-menu-grip') || !event.isPrimary) return;
        drag = { id: event.pointerId, y: event.clientY, t: event.timeStamp, dy: 0 };
        panel.setPointerCapture(event.pointerId);
        panel.classList.add('is-dragging');
    });
    panel.addEventListener('pointermove', event => {
        if (!drag || event.pointerId !== drag.id) return;
        drag.dy = Math.max(0, event.clientY - drag.y);
        event.preventDefault();
        panel.style.translate = '0 ' + drag.dy + 'px';
        menu.style.setProperty('--site-menu-drag', String(Math.min(1, drag.dy / panel.offsetHeight)));
    });
    panel.addEventListener('pointerup', event => {
        if (!drag || event.pointerId !== drag.id) return;
        const { dy, t } = drag;
        const dismiss = dy > panel.offsetHeight * 0.3 || dy > 24 && dy / Math.max(1, event.timeStamp - t) > 0.6;
        resetDrag();
        if (dismiss) close();
    });
    panel.addEventListener('pointercancel', resetDrag);
    panel.addEventListener('lostpointercapture', resetDrag);
})();

// Name only the receiving half of a home-to-board handoff. Other native
// transitions on the full leaderboard must not capture its table.
window.addEventListener('pagereveal', event => {
    const from = window.navigation?.activation?.from?.url;
    if (!event.viewTransition || !from || window.navigation.activation.navigationType === 'traverse'
        || matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const source = new URL(from);
    if (source.origin !== location.origin || source.pathname !== '/' || source.search || source.hash
        || location.search || location.hash || !/^\/leaderboard\/?$/i.test(location.pathname)) return;
    const board = document.querySelector('main[data-leaderboard-page="full"] #athleteLeaderboard');
    if (!board) return;
    board.style.viewTransitionName = 'lwc-board';
    const clear = () => { board.style.viewTransitionName = ''; };
    event.viewTransition.finished.then(clear, clear);
});
