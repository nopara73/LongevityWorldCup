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
    stickyHeader.classList.toggle('visible', willBeVisible);
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
let customAlertResolve = null;
let customAlertPreviousFocus = null;
let loadingDialogPreviousFocus = null;

function closeCustomAlert() {
    if (customAlertDialog.hidden) {
        return;
    }

    customAlertDialog.hidden = true;
    document.body.classList.remove('no-scroll');

    if (customAlertPreviousFocus && typeof customAlertPreviousFocus.focus === 'function') {
        customAlertPreviousFocus.focus();
    }

    customAlertPreviousFocus = null;

    if (customAlertResolve) {
        const resolve = customAlertResolve;
        customAlertResolve = null;
        resolve();
    }
}

customAlertDialog.addEventListener('keydown', function (event) {
    if (event.key === 'Escape') {
        event.preventDefault();
        closeCustomAlert();
        return;
    }

    trapFocusWithin(customAlertDialog, event);
});

loadingDialog.addEventListener('keydown', function (event) {
    if (event.key !== 'Tab') {
        return;
    }

    event.preventDefault();
    loadingDialog.querySelector('.loading-dialog-panel')?.focus();
});

function customAlert(message) {
    customAlertPreviousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    customAlertMessage.textContent = message;

    return new Promise(resolve => {
        customAlertResolve = resolve;

        customAlertDialog.hidden = false;

        document.body.classList.add('no-scroll');
        requestAnimationFrame(() => customAlertCloseButton.focus());
    });
}

function showLoading() {
    loadingDialogPreviousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;

    loadingDialog.hidden = false;

    document.body.classList.add('no-scroll');
    loadingDialog.querySelector('.loading-dialog-panel')?.focus();
}

function hideLoading() {
    loadingDialog.hidden = true;

    document.body.classList.remove('no-scroll');

    if (loadingDialogPreviousFocus && typeof loadingDialogPreviousFocus.focus === 'function') {
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

    if (!container.contains(active)) {
        event.preventDefault();
        first.focus();
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
