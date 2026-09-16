const scrolledJoinButton = document.querySelector('.scrolled-button');
const joinGameBtn = document.querySelector('.join-game');
let lastKnownScrollPosition = 0;
let ticking = false;

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

function handleScroll(scrollPos) {
    const compactLandscape = window.matchMedia('(max-width: 932px) and (max-height: 480px) and (orientation: landscape)').matches;
    const mainJoinButtonVisible = joinGameBtn && window.getComputedStyle(joinGameBtn).display !== 'none';
    const mainHeader = document.querySelector('header[role="banner"]');
    const stickyHeader = document.getElementById('site-sticky-header');
    /* Show when scroll header would completely cover the main header: main header is entirely under the sticky bar. Sticky height when visible; fallback 52 if not yet measured. */
    const stickyHeight = (stickyHeader && stickyHeader.offsetHeight) || 52;
    const threshold = mainHeader ? Math.max(0, mainHeader.offsetHeight - stickyHeight) : -1;
    const willBeVisible = mainHeader && stickyHeader ? scrollPos >= threshold : false;
    if (scrolledJoinButton && willBeVisible && (mainJoinButtonVisible || compactLandscape)) {
        scrolledJoinButton.style.display = 'inline-flex';
    } else if (scrolledJoinButton) {
        scrolledJoinButton.style.display = 'none';
    }
    if (mainHeader && stickyHeader) {
        if (willBeVisible) {
            stickyHeader.classList.add('visible');
            stickyHeader.setAttribute('aria-hidden', 'false');
        } else {
            stickyHeader.classList.remove('visible');
            stickyHeader.setAttribute('aria-hidden', 'true');
        }
    }
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
