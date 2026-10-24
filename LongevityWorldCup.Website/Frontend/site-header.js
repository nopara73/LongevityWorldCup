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
        if (destination.origin !== location.origin || !journeyPath.test(destination.pathname)) {
            event.viewTransition.skipTransition();
        }
    });
})();

// Getting around: the sticky bar tucks away while reading down and returns on
// the first scroll up, and one menu (a sheet on phones, a panel on wider
// screens) carries the footer's pages and platforms to the top of every page.
(function () {
    const stickyHeader = document.getElementById('site-sticky-header');
    const menu = document.getElementById('site-menu');
    const panel = menu?.querySelector('.site-menu-panel');
    const body = menu?.querySelector('.site-menu-body');
    const toggles = Array.from(document.querySelectorAll('.site-menu-toggle'));
    const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)');
    const sheetQuery = matchMedia('(max-width: 640px)');

    // Headroom: direction is judged over a short distance so trackpad jitter
    // never flickers the bar, and long programmatic jumps (anchors, restored
    // scroll) leave it as it was.
    let lastY = window.scrollY;
    let travel = 0;
    function setTucked(tucked) {
        if (!stickyHeader) return;
        if (tucked && (stickyHeader.contains(document.activeElement)
            || document.activeElement === scrolledJoinButton || (menu && !menu.hidden))) tucked = false;
        stickyHeader.classList.toggle('is-tucked', tucked);
        scrolledJoinButton?.classList.toggle('is-tucked', tucked);
    }
    window.addEventListener('scroll', () => {
        const y = window.scrollY;
        const delta = y - lastY;
        lastY = y;
        if (Math.abs(delta) > 600) { travel = 0; return; }
        travel = Math.sign(delta) === Math.sign(travel) ? travel + delta : delta;
        if (!stickyHeader?.classList.contains('visible')) { setTucked(false); return; }
        if (travel > 48) setTucked(true);
        else if (travel < -12) setTucked(false);
    }, { passive: true });
    stickyHeader?.addEventListener('focusin', () => setTucked(false));
    scrolledJoinButton?.addEventListener('focus', () => setTucked(false));

    if (!menu || !panel || !body || !toggles.length) return;

    // The menu mirrors the footer, so each page and platform is named once.
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
            copy.querySelectorAll('[id]').forEach(element => element.removeAttribute('id'));
            copy.querySelectorAll('.nav-glide').forEach(element => element.remove());
            nodes.push(copy);
        }
        body.replaceChildren(...nodes);
        body.querySelectorAll('.footer-link').forEach((link, index) => link.style.setProperty('--site-menu-i', String(index)));
        window.LwcNavGlide?.(body.querySelector('.site-menu-pages'));
        return true;
    }

    let opener = null;
    let closeTimer = 0;
    const isOpen = () => !menu.hidden && !menu.classList.contains('is-closing');

    function open(fromToggle) {
        if (isOpen() || !build()) return;
        clearTimeout(closeTimer);
        opener = fromToggle || document.activeElement;
        menu.classList.remove('is-closing');
        menu.dataset.layout = sheetQuery.matches ? 'sheet' : 'panel';
        positionPanel(fromToggle);
        panel.style.removeProperty('translate');
        menu.hidden = false;
        document.body.classList.add('site-menu-open');
        toggles.forEach(toggle => toggle.setAttribute('aria-expanded', 'true'));
        setTucked(false);
        panel.getBoundingClientRect();
        menu.classList.add('is-open');
        const current = body.querySelector('.site-menu-pages [aria-current="page"]') || body.querySelector('.footer-link');
        (current || panel).focus({ preventScroll: true });
    }

    function close(restoreFocus = true) {
        if (!isOpen()) return;
        panel.style.removeProperty('translate');
        menu.classList.remove('is-open');
        menu.classList.add('is-closing');
        document.body.classList.remove('site-menu-open');
        toggles.forEach(toggle => toggle.setAttribute('aria-expanded', 'false'));
        const finish = () => {
            menu.hidden = true;
            menu.classList.remove('is-closing');
            panel.style.removeProperty('translate');
        };
        if (reduceMotion.matches) finish();
        else closeTimer = setTimeout(finish, 240);
        const usable = element => element?.isConnected && element.getClientRects().length && !element.closest('[inert]');
        const target = usable(opener) ? opener : toggles.find(usable);
        if (restoreFocus && menu.contains(document.activeElement)) target?.focus({ preventScroll: true });
        opener = null;
    }

    // Wider screens drop the panel from the toggle that opened it.
    function positionPanel(toggle) {
        const rect = (toggle || toggles[0]).getBoundingClientRect();
        menu.style.setProperty('--site-menu-top', Math.round(rect.bottom + 10) + 'px');
        menu.style.setProperty('--site-menu-right', Math.max(12, Math.round(innerWidth - rect.right)) + 'px');
        menu.style.setProperty('--site-menu-origin', Math.round(rect.width / 2) + 'px');
    }

    toggles.forEach(toggle => toggle.addEventListener('click', () => (isOpen() ? close() : open(toggle))));
    menu.addEventListener('click', event => {
        if (event.target instanceof Element && event.target.closest('[data-site-menu-close]')) close();
    });
    document.addEventListener('pointerdown', event => {
        if (isOpen() && menu.dataset.layout === 'panel' && event.target instanceof Element
            && !panel.contains(event.target) && !event.target.closest('.site-menu-toggle')) close(false);
    });
    menu.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            close();
        } else if (typeof trapFocusWithin === 'function') trapFocusWithin(panel, event);
    });
    sheetQuery.addEventListener('change', () => close(false));
    window.addEventListener('pagehide', () => close(false));
    window.addEventListener('resize', () => { if (isOpen() && menu.dataset.layout === 'panel') positionPanel(opener); });

    // Swipe the sheet down to close it: it follows the finger, the backdrop
    // clears with it, and a short or slow pull springs back.
    let drag = null;
    panel.addEventListener('pointerdown', event => {
        if (menu.dataset.layout !== 'sheet' || event.pointerType === 'mouse' || !isOpen()) return;
        if (body.scrollTop > 0 && !event.target.closest('.site-menu-grip')) return;
        drag = { id: event.pointerId, y: event.clientY, t: event.timeStamp, dy: 0, active: false };
    });
    panel.addEventListener('pointermove', event => {
        if (!drag || event.pointerId !== drag.id) return;
        drag.dy = Math.max(0, event.clientY - drag.y);
        if (!drag.active && drag.dy > 6) {
            drag.active = true;
            panel.setPointerCapture(event.pointerId);
            panel.classList.add('is-dragging');
        }
        if (drag.active) {
            event.preventDefault();
            panel.style.translate = '0 ' + drag.dy + 'px';
            menu.style.setProperty('--site-menu-drag', String(Math.min(1, drag.dy / panel.offsetHeight)));
        }
    });
    function endDrag(event) {
        if (!drag || event.pointerId !== drag.id) return;
        const { dy, t, active } = drag;
        drag = null;
        panel.classList.remove('is-dragging');
        menu.style.removeProperty('--site-menu-drag');
        if (!active) return;
        panel.dataset.dragged = '1';
        setTimeout(() => delete panel.dataset.dragged, 80);
        const velocity = dy / Math.max(1, event.timeStamp - t);
        if (dy > panel.offsetHeight * 0.3 || velocity > 0.6) close();
        else panel.style.removeProperty('translate');
    }
    panel.addEventListener('pointerup', endDrag);
    panel.addEventListener('pointercancel', endDrag);
    // A drag that starts on a link must not also follow it.
    panel.addEventListener('click', event => {
        if (panel.dataset.dragged) { event.preventDefault(); delete panel.dataset.dragged; }
    }, true);
})();
