/* Shared by the page and push worker. No permission prompts or page caching here. */
(function () {
    const syncTag = 'lwc-web-push';
    const identity = value => value ? JSON.stringify([value.endpoint, value.keys.p256dh, value.keys.auth]) : '';
    const json = value => value ? (value.toJSON ? value.toJSON() : value) : null;
    const unique = values => [...new Map(values.filter(Boolean).map(value => [identity(value), value])).values()];
    let queue = Promise.resolve();
    const changes = typeof BroadcastChannel === 'function' ? new BroadcastChannel(syncTag) : null;

    function deadline(promise, milliseconds, message, abort) {
        let timer;
        return Promise.race([promise, new Promise((_, reject) => {
            timer = setTimeout(() => { abort?.(); reject(new Error(message)); }, milliseconds);
        })]).finally(() => clearTimeout(timer));
    }

    function state(change) {
        let cancelled = false;
        let transaction;
        return deadline(new Promise((resolve, reject) => {
            const open = indexedDB.open('lwc-web-push', 1);
            open.onupgradeneeded = () => open.result.createObjectStore('state');
            open.onerror = () => reject(open.error);
            open.onblocked = () => reject(new Error('Notification preferences are unavailable. Try again.'));
            open.onsuccess = () => {
                const db = open.result;
                if (cancelled) { db.close(); return; }
                const tx = db.transaction('state', change ? 'readwrite' : 'readonly');
                transaction = tx;
                const store = tx.objectStore('state');
                const get = store.get('preference');
                let value;
                get.onsuccess = () => {
                    value = get.result || { enabled: null, revision: 0, current: null, pending: [], synced: '' };
                    if (change) { change(value); store.put(value, 'preference'); }
                };
                tx.oncomplete = () => { db.close(); resolve(value); };
                tx.onabort = tx.onerror = () => { db.close(); reject(tx.error || new Error('Notification preferences were not saved.')); };
            };
        }), 10000, 'Notification preferences are unavailable. Try again.', () => {
            cancelled = true;
            try { transaction?.abort(); } catch {}
        });
    }

    function serial(work) {
        // Web Locks coordinate tabs and the worker. Revision checks and server-side
        // revocations also guard older browsers and requests that outlive an abort.
        const run = () => navigator.locks ? navigator.locks.request(syncTag, work) : work();
        const result = queue.then(run, run);
        queue = result.catch(() => {});
        return result;
    }

    async function request(path, body) {
        const controller = new AbortController();
        return deadline((async () => {
            const response = await fetch('/api/web-push/' + path, {
                method: body ? 'POST' : 'GET', credentials: 'same-origin', cache: 'no-store',
                headers: body ? { 'Content-Type': 'application/json', 'X-LWC-Push': '1' } : {},
                body: body ? JSON.stringify(body) : undefined, signal: controller.signal
            });
            if (!response.ok) {
                const error = new Error('Notification preferences could not sync. Try again.');
                error.status = response.status;
                throw error;
            }
            return path === 'configuration' ? await response.json() : null;
        })(), 10000, 'Notification preferences could not sync. Try again.', () => controller.abort());
    }

    function keyBytes(value) {
        const raw = atob(value.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - value.length % 4) % 4));
        return Uint8Array.from(raw, c => c.charCodeAt(0));
    }

    async function remember(values) {
        return state(value => { value.pending = unique([...value.pending, ...values.map(json)]); });
    }

    async function intent(enabled) {
        const saved = await state(value => { value.enabled = enabled; value.revision++; value.synced = ''; });
        changes?.postMessage('changed');
        return saved;
    }

    async function removeNative(subscription) {
        // false means the subscription was already deactivated, not a failure.
        if (subscription) await deadline(subscription.unsubscribe(), 10000, 'Notifications could not disconnect. Try again.');
    }

    async function cleanup() {
        for (const previous of (await state()).pending) {
            const latest = await state();
            if (latest.enabled && identity(latest.current) === identity(previous)) continue;
            await request('unsubscribe', previous);
            await state(value => { value.pending = value.pending.filter(item => identity(item) !== identity(previous)); });
        }
    }

    async function reconcile(registration, options = {}) {
        let saved = await state();
        const revision = saved.revision;
        await remember([options.oldSubscription, options.newSubscription, saved.current]);
        if (saved.enabled === null && Notification.permission === 'granted' && (options.oldSubscription || options.newSubscription)) {
            saved = await state(value => { if (value.revision === revision) value.enabled = true; });
        }
        const native = await deadline(registration.pushManager.getSubscription(), 10000, 'Notifications could not connect. Try again.');
        // The live provider state wins over a delayed change event.
        let candidate = options.newSubscription && (!native || identity(json(native)) === identity(json(options.oldSubscription)))
            ? options.newSubscription : native;
        if (Notification.permission !== 'granted' || saved.enabled === false) {
            if (saved.enabled !== false) await intent(false);
            await remember([candidate, options.newSubscription]);
            await removeNative(native);
            if (options.newSubscription && options.newSubscription !== native) await removeNative(options.newSubscription);
            await state(value => { if (!value.enabled) { value.current = null; value.synced = ''; } });
            await cleanup();
            return state();
        }
        if (saved.enabled === null && (candidate || options.oldSubscription)) {
            // Remember prior consent before configuration/provider work can fail.
            saved = await state(value => { if (value.revision === revision) value.enabled = true; });
        }
        const retiredByEvent = options.oldSubscription && !options.newSubscription
            && identity(json(candidate)) === identity(json(options.oldSubscription));
        if (candidate && (retiredByEvent || (candidate.expirationTime && candidate.expirationTime <= Date.now()))) {
            await remember([candidate]);
            await removeNative(candidate);
            candidate = null;
        }
        if (!candidate && (saved.enabled === true || options.oldSubscription)) {
            const config = options.publicKey ? { publicKey: options.publicKey } : await request('configuration');
            if (!config.publicKey) throw new Error('Announcement notifications are unavailable. Try again.');
            if ((await state()).revision !== revision) return reconcile(registration);
            const pending = registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(config.publicKey) });
            let timedOut = false;
            try { candidate = await deadline(pending, 120000, 'Notifications could not connect. Try again.', () => { timedOut = true; }); }
            catch (error) {
                // A timed-out native operation cannot be cancelled. Keep late results
                // out of delivery, even after the initiating page has stopped waiting.
                if (timedOut && options.explicit) await intent(false);
                pending.then(late => serial(async () => {
                    const latest = await state();
                    if (latest.enabled && identity(latest.current) === identity(json(late))) return;
                    if (latest.enabled && latest.revision === revision) return reconcile(registration);
                    await remember([late]);
                    await removeNative(late);
                    await cleanup();
                })).catch(() => {});
                throw error;
            }
        }
        if (!candidate) return state(); // No prior opt-in: never silently subscribe.
        const current = json(candidate);
        saved = await state(value => {
            if (value.revision !== revision) { value.pending = unique([...value.pending, current]); return; }
            value.enabled = true;
            value.current = current;
            value.synced = '';
            value.pending = value.pending.filter(item => identity(item) !== identity(current));
        });
        if (saved.revision !== revision || Notification.permission !== 'granted') return reconcile(registration);
        // Save and replace in one transaction. The old proof permits same-endpoint
        // key rotation; a stale cleanup only revokes that exact old key pair.
        const previous = saved.pending.slice(0, 8);
        try { await request('subscribe', { ...current, previous }); }
        catch (error) {
            if (error.status === 409) {
                // A revoked/obsolete provider identity must be reacquired, not replayed forever.
                await remember([candidate]);
                await removeNative(candidate);
                await state(value => { if (value.revision === revision) value.current = null; });
            }
            throw error;
        }
        saved = await state(value => {
            if (value.revision !== revision) return;
            value.synced = identity(current);
            value.pending = value.pending.filter(item => !previous.some(old => identity(old) === identity(item)));
        });
        if (saved.revision !== revision || Notification.permission !== 'granted') return reconcile(registration);
        await cleanup();
        return state();
    }

    async function retryLater(registration) {
        try { if (registration.sync) await deadline(registration.sync.register(syncTag), 10000, ''); } catch {}
    }

    async function run(registration, options = {}) {
        try { return await serial(() => reconcile(registration, options)); }
        catch (error) { if (!options.background) await retryLater(registration); throw error; }
    }

    self.LwcWebPush = {
        state, identity, deadline, request, syncTag,
        // Reuse the sender: BroadcastChannel does not echo a message to itself.
        // A second channel in this document can receive our own completed change
        // late and erase its error/status while starting a redundant refresh.
        onChange: listener => changes?.addEventListener('message', listener),
        sync: run,
        enable: async (registration, publicKey) => {
            // Finish any durable opt-out before acquiring a fresh provider identity.
            if ((await state()).enabled === false) await run(registration);
            await intent(true);
            return run(registration, { publicKey, explicit: true });
        },
        disable: async registration => {
            // Persist intent before waiting for an in-flight renewal or network request.
            await intent(false);
            return run(await registration);
        }
    };
})();
