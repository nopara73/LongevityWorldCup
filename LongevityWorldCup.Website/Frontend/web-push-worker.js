/* Push-only worker: no fetch handler or page cache. Each message displays a visible notification. */
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('push', event => {
    event.waitUntil((async () => {
        let payload;
        try { payload = event.data?.json(); } catch { payload = null; }
        if (!payload || typeof payload.id !== 'string' || typeof payload.title !== 'string') return;
        const destination = new URL(payload.url || '/events', self.location.origin);
        const url = destination.origin === self.location.origin && destination.pathname === '/events'
            ? destination.href : new URL('/events', self.location.origin).href;
        await self.registration.showNotification(payload.title, {
            body: typeof payload.body === 'string' ? payload.body : '',
            icon: payload.icon,
            tag: 'lwc-' + payload.id,
            renotify: false,
            data: { url }
        });
    })());
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    event.waitUntil((async () => {
        const candidate = new URL(event.notification.data?.url || '/events', self.location.origin);
        const url = candidate.origin === self.location.origin && candidate.pathname === '/events'
            ? candidate.href : new URL('/events', self.location.origin).href;
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const client of windows) {
            if (new URL(client.url).origin !== self.location.origin) continue;
            await client.navigate(url);
            await client.focus();
            return;
        }
        await self.clients.openWindow(url);
    })());
});

self.addEventListener('pushsubscriptionchange', event => {
    event.waitUntil((async () => {
        const oldSubscription = event.oldSubscription;
        if (oldSubscription) await fetch('/api/web-push/unsubscribe', {
            method: 'POST', headers: { 'Content-Type': 'application/json', 'X-LWC-Push': '1' },
            body: JSON.stringify(oldSubscription.toJSON())
        });
        if (Notification.permission !== 'granted') return;
        const config = await (await fetch('/api/web-push/configuration')).json();
        if (!config.publicKey) return;
        const raw = atob(config.publicKey.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - config.publicKey.length % 4) % 4));
        const subscription = event.newSubscription || await self.registration.pushManager.subscribe({
            userVisibleOnly: true, applicationServerKey: Uint8Array.from(raw, c => c.charCodeAt(0))
        });
        await fetch('/api/web-push/subscribe', {
            method: 'POST', headers: { 'Content-Type': 'application/json', 'X-LWC-Push': '1' },
            body: JSON.stringify(subscription.toJSON())
        });
    })());
});
