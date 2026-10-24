(function () {
    const script = document.currentScript;
    const button = document.getElementById('webPushToggle');
    const status = document.getElementById('webPushStatus');
    if (!button || !window.isSecureContext || !('serviceWorker' in navigator)
        || !('PushManager' in window) || !('Notification' in window) || window.top !== window.self) return;
    const workerUrl = script?.dataset.workerUrl;
    const push = window.LwcWebPush;
    if (!workerUrl || !push) return;
    let publicKey = null;
    let worker = null;
    let preference = { enabled: null, current: null, pending: [], synced: '' };
    let busy = false;
    let failure = '';
    let retryTimer;

    function render(message) {
        const blocked = Notification.permission === 'denied';
        const enabled = !blocked && Notification.permission === 'granted' && preference.enabled
            && preference.current && preference.synced === push.identity(preference.current) && !failure;
        const recovery = !!failure || preference.pending.length > 0 || (preference.enabled && !enabled);
        button.setAttribute('aria-pressed', enabled ? 'true' : 'false');
        const label = busy ? 'Updating announcement notifications'
            : blocked ? 'Notifications blocked — allow them in browser settings'
            : preference.enabled ? (recovery ? 'Turn off announcement notifications — sync pending' : 'Turn off announcement notifications')
            : recovery ? 'Retry notification preferences' : 'Get announcement notifications';
        button.setAttribute('aria-label', label);
        button.title = label;
        button.disabled = busy || blocked;
        button.setAttribute('aria-busy', busy ? 'true' : 'false');
        button.querySelector('i').className = busy ? 'fas fa-spinner fa-spin'
            : blocked ? 'fas fa-bell-slash' : recovery ? 'fas fa-exclamation-circle' : 'fas fa-bell';
        if (status) status.textContent = failure && preference.enabled
            ? 'Notifications need syncing. Retrying automatically.'
            : failure || message || (recovery ? 'Notification preferences need syncing.' : '');
    }

    async function registration() {
        const value = await push.deadline(navigator.serviceWorker.register(workerUrl, { scope: '/', updateViaCache: 'none' }),
            10000, 'Notifications could not start. Try again.');
        if (!value.active) await push.deadline(navigator.serviceWorker.ready, 10000, 'Notifications could not start. Try again.');
        worker = value;
        return value;
    }

    async function finish(error, message) {
        failure = error ? (error instanceof Error ? error.message : 'Notifications could not be updated. Try again.') : '';
        try { preference = await push.state(); } catch { failure ||= 'Notification preferences are unavailable. Try again.'; }
        busy = false;
        render(message);
        clearTimeout(retryTimer);
        // Also recover on reload, reconnect and returning to the page. This bounded
        // foreground retry works in browsers without Background Sync.
        if (failure) retryTimer = setTimeout(() => refresh(), 60000);
    }

    async function refresh() {
        if (busy || document.visibilityState === 'hidden') return;
        busy = true;
        render();
        try {
            preference = await push.state();
            const existing = await push.deadline(navigator.serviceWorker.getRegistration('/'), 10000, 'Notifications could not start. Try again.');
            if (existing?.active && new URL(existing.active.scriptURL).pathname === '/js/web-push-worker.js') {
                await registration();
                preference = await push.sync(worker);
            }
            publicKey = (await push.request('configuration')).publicKey;
            if (workerUrl && (publicKey || preference.enabled || preference.pending.length)) button.hidden = false;
            await finish(null);
        } catch (error) {
            if (workerUrl) button.hidden = false;
            await finish(error);
        }
    }

    button.addEventListener('click', async () => {
        if (busy) return;
        const restoreFocus = document.activeElement === button;
        busy = true;
        let message;
        try {
            // Safari requires the permission request directly in the explicit click.
            const permission = !preference.enabled && !failure && publicKey && Notification.permission === 'default'
                ? Notification.requestPermission() : Promise.resolve(Notification.permission);
            render();
            if (preference.enabled) {
                await push.disable(worker || registration());
                message = 'Announcement notifications off.';
            } else if (failure || preference.pending.length) {
                publicKey = (await push.request('configuration')).publicKey;
                await push.sync(worker || await registration());
            } else if (await permission === 'granted') {
                await push.enable(worker || await registration(), publicKey);
                message = 'Announcement notifications on.';
            } else {
                message = 'Announcement notifications were not enabled.';
            }
            await finish(null, message);
        } catch (error) { await finish(error); }
        finally {
            if (restoreFocus && !button.disabled && document.activeElement === document.body) button.focus({ preventScroll: true });
        }
    });

    window.addEventListener('online', refresh);
    push.onChange(refresh);
    window.addEventListener('pageshow', refresh);
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') refresh(); });
    try {
        navigator.permissions?.query({ name: 'notifications' }).then(permission => {
            permission.addEventListener('change', refresh);
        }).catch(() => {});
    } catch { /* Some push-capable browsers do not support this permission descriptor. */ }
    refresh();
})();
