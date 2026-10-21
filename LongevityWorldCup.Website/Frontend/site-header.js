const scrolledJoinButton = document.querySelector('.scrolled-button');
const joinGameBtn = document.querySelector('.join-game');
let lastKnownScrollPosition = 0;
let ticking = false;
let measuredStickyHeight = 0;
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

    // Measure the complete bar before it appears, so the first anchor jump
    // and keyboard scroll reserve the same space as subsequent ones.
    // Cache this geometry between layout changes, keeping scrolling cheap.
    if (remeasure || !measuredStickyHeight || lastStickyActionAvailable !== actionAvailable) {
        const visibility = stickyHeader.style.visibility;
        const actionVisibility = scrolledJoinButton?.style.visibility;
        stickyHeader.style.visibility = 'hidden';
        stickyHeader.classList.add('visible');
        if (scrolledJoinButton) {
            scrolledJoinButton.style.visibility = 'hidden';
            scrolledJoinButton.style.display = actionAvailable ? 'inline-flex' : 'none';
        }
        measuredStickyHeight = fitStickyHeader(stickyHeader);
        lastStickyActionAvailable = actionAvailable;
        stickyHeader.style.visibility = visibility;
        if (scrolledJoinButton) scrolledJoinButton.style.visibility = actionVisibility;
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
        scrolledJoinButton.style.display = willBeVisible && actionAvailable ? 'inline-flex' : 'none';
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
        if (customAlertOwnsScrollLock && loadingDialog.hidden) document.body.classList.remove('no-scroll');
        customAlertOwnsScrollLock = false;
    }

    if (restoreFocus && previousFocus?.isConnected && !previousFocus.closest('[hidden], [inert]')
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
    if (/\b(failed|failure|error|interrupted|unsuccessful|rejected)\b/i.test(text)) return 'danger';
    // A success word inside a negation, uncertainty or validation request must never celebrate.
    if (/\b(not|never|no|cannot|can['’]t|couldn['’]t|wasn['’]t|weren['’]t|hasn['’]t|haven['’]t|didn['’]t|unable|may|might|could|should|would|being|if|pending|awaiting|unconfirmed|unknown|missing|invalid|required|must be|unavailable|skipped|try again|enter|select|provide|please complete|change at least)\b/i.test(text)) return 'warning';
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
    lead.append(...splitCustomAlertIds(separator ? text.slice(0, separator.index) : text));
    customAlertMessage.replaceChildren(lead);
    if (separator) {
        // Preserve the exact separator bytes outside the styled detail block.
        customAlertMessage.append(document.createTextNode(separator[0]));
        const detail = document.createElement('span');
        detail.className = 'custom-alert-detail';
        detail.append(...splitCustomAlertIds(text.slice(separator.index + separator[0].length)));
        customAlertMessage.append(detail);
    }
}

function splitCustomAlertIds(text) {
    const nodes = [];
    let last = 0;
    for (const match of text.matchAll(CUSTOM_ALERT_ID_PATTERN)) {
        if (match.index > last) nodes.push(document.createTextNode(text.slice(last, match.index)));
        const id = document.createElement('code');
        id.className = 'custom-alert-id';
        id.textContent = match[0];
        nodes.push(id);
        last = match.index + match[0].length;
    }
    if (last < text.length) nodes.push(document.createTextNode(text.slice(last)));
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
    customAlertPreviousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
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
            && (document.activeElement === customAlertPreviousFocus || document.activeElement === document.body
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
    loadingDialogPreviousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;

    loadingDialog.hidden = false;

    document.body.classList.add('no-scroll');
    loadingDialog.querySelector('.loading-dialog-panel')?.focus();
}

function hideLoading() {
    loadingDialog.hidden = true;

    if (customAlertDialog.hidden) document.body.classList.remove('no-scroll');
    else customAlertOwnsScrollLock = true;

    if (customAlertDialog.hidden && loadingDialogPreviousFocus && typeof loadingDialogPreviousFocus.focus === 'function') {
        loadingDialogPreviousFocus.focus();
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
