/* Push-only worker: no fetch handler or page cache. Each message displays a visible notification. */
importScripts('/js/web-push-sync.js');
self.addEventListener('install', event => event.waitUntil(self.skipWaiting()));
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

function notificationDestination(value) {
    try {
        const destination = new URL(value || '/events', self.location.origin);
        if (destination.origin === self.location.origin && ['/events', '/rejuvenation-olympics'].includes(destination.pathname))
            return destination.href;
    } catch {}
    return new URL('/events', self.location.origin).href;
}

function notificationImage(value) {
    if (typeof value !== 'string') return undefined;
    try {
        const image = new URL(value, self.location.origin);
        if (image.origin === self.location.origin && image.pathname.startsWith('/assets/social/')) return image.href;
    } catch {}
    return undefined;
}

self.addEventListener('push', event => {
    event.waitUntil((async () => {
        let payload;
        try { payload = event.data?.json(); } catch { payload = null; }
        if (!payload || typeof payload.id !== 'string' || typeof payload.title !== 'string') return;
        if ((await LwcWebPush.state()).enabled === false) return;
        const url = notificationDestination(payload.url);
        await self.registration.showNotification(payload.title, {
            body: typeof payload.body === 'string' ? payload.body : '',
            icon: payload.icon,
            image: notificationImage(payload.image),
            tag: 'lwc-' + payload.id,
            renotify: false,
            data: { url }
        });
    })());
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    event.waitUntil((async () => {
        const url = notificationDestination(event.notification.data?.url);
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const client of windows) {
            const current = new URL(client.url);
            // Reuse Highlights only; other pages may contain an unsaved application or admin draft.
            if (current.origin !== self.location.origin || current.pathname !== '/events') continue;
            await client.navigate(url);
            await client.focus();
            return;
        }
        await self.clients.openWindow(url);
    })());
});

self.addEventListener('pushsubscriptionchange', event => {
    event.waitUntil(LwcWebPush.sync(self.registration, {
        oldSubscription: event.oldSubscription, newSubscription: event.newSubscription
    }));
});

self.addEventListener('sync', event => {
    // Reject failed attempts so the browser applies its own bounded sync retry policy.
    if (event.tag === LwcWebPush.syncTag) event.waitUntil(LwcWebPush.sync(self.registration, { background: true }));
});
