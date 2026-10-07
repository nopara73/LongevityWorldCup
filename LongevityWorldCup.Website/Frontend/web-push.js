(function () {
    const script = document.currentScript;
    const button = document.getElementById('webPushToggle');
    const status = document.getElementById('webPushStatus');
    if (!button || !window.isSecureContext || !('serviceWorker' in navigator)
        || !('PushManager' in window) || !('Notification' in window) || window.top !== window.self) return;
    const workerUrl = script?.dataset.workerUrl;
    let publicKey = null;
    let subscription = null;
    let busy = false;

    function render(message = '') {
        const blocked = Notification.permission === 'denied';
        button.setAttribute('aria-pressed', subscription ? 'true' : 'false');
        const label = busy ? 'Updating announcement notifications'
            : blocked ? 'Notifications blocked — allow them in browser settings'
            : subscription ? 'Turn off announcement notifications' : 'Get announcement notifications';
        button.setAttribute('aria-label', label);
        button.title = label;
        button.disabled = busy || blocked;
        button.setAttribute('aria-busy', busy ? 'true' : 'false');
        button.querySelector('i').className = busy ? 'fas fa-spinner fa-spin' : blocked ? 'fas fa-bell-slash' : 'fas fa-bell';
        if (status) status.textContent = message;
    }

    async function save(action, value) {
        const response = await fetch('/api/web-push/' + action, {
            method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-LWC-Push': '1' },
            body: JSON.stringify(value.toJSON()), signal: AbortSignal.timeout(10000)
        });
        if (!response.ok) throw new Error('Notification preference was not saved. Try again.');
    }

    function keyBytes(value) {
        const raw = atob(value.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - value.length % 4) % 4));
        return Uint8Array.from(raw, c => c.charCodeAt(0));
    }

    async function registration() {
        const worker = await navigator.serviceWorker.register(workerUrl, { scope: '/', updateViaCache: 'none' });
        if (!worker.active) {
            await Promise.race([
                navigator.serviceWorker.ready,
                new Promise((_, reject) => setTimeout(() => reject(new Error('Notifications could not start. Try again.')), 10000))
            ]);
        }
        return worker;
    }

    button.addEventListener('click', async () => {
        if (busy) return;
        busy = true;
        // Request permission directly within the click: Safari requires a user gesture.
        try {
            const permission = subscription ? Promise.resolve('granted') : Notification.requestPermission();
            render();
            if (subscription) {
                const previous = subscription;
                await previous.unsubscribe();
                subscription = null;
                await save('unsubscribe', previous);
                render('Announcement notifications off.');
            } else if (await permission === 'granted') {
                const worker = await registration();
                const pending = worker.pushManager.getSubscription().then(existing => existing
                    || worker.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(publicKey) }));
                let candidate;
                try {
                    candidate = await Promise.race([
                        pending,
                        new Promise((_, reject) => setTimeout(() => reject(new Error('Notifications could not connect. Try again.')), 120000))
                    ]);
                } catch (error) {
                    pending.then(late => late.unsubscribe()).catch(() => {});
                    throw error;
                }
                try { await save('subscribe', candidate); }
                catch (error) { await candidate.unsubscribe(); throw error; }
                subscription = candidate;
                render('Announcement notifications on.');
            } else {
                render('Announcement notifications were not enabled.');
            }
        } catch (error) {
            render(error instanceof DOMException ? 'Notifications are unavailable in this browser window.'
                : error instanceof Error ? error.message : 'Notifications could not be updated. Try again.');
        } finally {
            busy = false;
            const message = status?.textContent || '';
            render(message);
        }
    });

    (async () => {
        try {
            const response = await fetch('/api/web-push/configuration', { signal: AbortSignal.timeout(10000) });
            if (!response.ok) return;
            publicKey = (await response.json()).publicKey;
            if (!publicKey || !workerUrl) return;
            const existing = await navigator.serviceWorker.getRegistration('/');
            if (existing?.active && new URL(existing.active.scriptURL).pathname === '/js/web-push-worker.js') {
                const worker = await registration();
                subscription = await worker.pushManager.getSubscription();
                if (subscription && Notification.permission === 'granted') {
                    try { await save('subscribe', subscription); }
                    catch { if (status) status.textContent = 'Notification preferences could not sync. Try again.'; }
                }
            }
            button.hidden = false;
            render();
        } catch { /* The event board remains usable while push is unavailable. */ }
    })();
})();
