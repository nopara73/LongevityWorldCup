using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class WebPushWorkerLifecycleTests
{
    [Theory]
    [InlineData("400")]
    [InlineData("503")]
    [InlineData("network")]
    [InlineData("provider")]
    [InlineData("timeout")]
    [InlineData("configuration-503")]
    [InlineData("configuration-body-timeout")]
    public async Task Renewal_RejectsFailuresAndRecoversAfterWorkerRestart(string failure)
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { BypassCSP = true });
        await page.GotoAsync(app.BaseAddress + "health");
        await page.Clock.InstallAsync();
        var sources = await Sources(app);
        await page.EvaluateAsync("""
            ({ shared, worker, failure }) => {
                const old = { endpoint: 'https://fcm.googleapis.com/send/old', keys: { p256dh: 'old-key', auth: 'old-auth' } };
                const fresh = { endpoint: 'https://fcm.googleapis.com/send/new', keys: { p256dh: 'new-key', auth: 'new-auth' } };
                const subscription = data => ({ toJSON: () => data, unsubscribe: async () => { native = null; return true; } });
                let native = failure.startsWith('configuration') ? null : subscription(fresh);
                let mode = failure, handlers, scope;
                window.calls = [];
                window.retryRegistrations = 0;
                window.aborted = false;
                const registration = {
                    sync: { register: async () => { window.retryRegistrations++; } },
                    pushManager: { getSubscription: async () => {
                        if (mode === 'provider') { window.providerFailed = true; throw new Error('Provider unavailable'); }
                        return native;
                    }, subscribe: async () => (native = subscription(fresh)) }
                };
                const fetch = async (url, options) => {
                    calls.push({ url, body: options.body && JSON.parse(options.body) });
                    if (mode.includes('timeout')) {
                        options.signal.addEventListener('abort', () => { window.aborted = true; });
                        if (mode === 'configuration-body-timeout') return { ok: true, json: () => new Promise(() => {}) };
                        return new Promise(() => {});
                    }
                    if (mode === 'network') throw new TypeError('offline');
                    if (mode !== 'success') return { ok: false, status: Number.parseInt(mode.replace('configuration-', '')) };
                    return { ok: true, json: async () => ({ publicKey: 'BA' }) };
                };
                const restart = () => {
                    handlers = {};
                    scope = { addEventListener: (name, handler) => { handlers[name] = handler; }, registration };
                    new Function('self', 'fetch', 'Notification', shared)(scope, fetch, { permission: 'granted' });
                    new Function('self', 'LwcWebPush', 'importScripts', worker)(scope, scope.LwcWebPush, () => {});
                };
                restart();
                window.failedBackgroundRetry = async () => {
                    let done;
                    handlers.sync({ tag: 'lwc-web-push', waitUntil: promise => { done = promise; } });
                    return done.then(() => false, () => true);
                };
                window.recover = async () => {
                    const before = await scope.LwcWebPush.state();
                    mode = 'success';
                    restart(); // Same IndexedDB, no in-memory renewal event.
                    let done;
                    handlers.sync({ tag: 'lwc-web-push', waitUntil: promise => { done = promise; } });
                    await done;
                    return { before, after: await scope.LwcWebPush.state(), calls, retries: retryRegistrations, aborted };
                };
                let done;
                handlers.pushsubscriptionchange({ oldSubscription: subscription(old), newSubscription: native,
                    waitUntil: promise => { done = promise; } });
                window.failed = done.then(() => false, () => true);
            }
            """, new { sources.shared, sources.worker, failure });
        await page.WaitForFunctionAsync("() => calls.length > 0 || window.providerFailed");
        if (failure.Contains("timeout")) await page.Clock.FastForwardAsync(10001);
        Assert.True(await page.EvaluateAsync<bool>("window.failed"));
        if (failure == "503") Assert.True(await page.EvaluateAsync<bool>("window.failedBackgroundRetry()"));
        var result = await page.EvaluateAsync<JsonElement>("window.recover()");
        Assert.True(result.GetProperty("before").GetProperty("enabled").GetBoolean());
        Assert.Equal("", result.GetProperty("before").GetProperty("synced").GetString());
        Assert.NotEmpty(result.GetProperty("before").GetProperty("pending").EnumerateArray());
        Assert.NotEmpty(result.GetProperty("after").GetProperty("synced").GetString()!);
        Assert.Empty(result.GetProperty("after").GetProperty("pending").EnumerateArray());
        Assert.Equal(1, result.GetProperty("retries").GetInt32());
        Assert.Equal(failure.Contains("timeout"), result.GetProperty("aborted").GetBoolean());
        var calls = result.GetProperty("calls").EnumerateArray().ToArray();
        Assert.DoesNotContain(calls, item => item.GetProperty("url").GetString()!.EndsWith("unsubscribe"));
        var replacement = calls.Last(item => item.GetProperty("url").GetString()!.EndsWith("subscribe")).GetProperty("body");
        Assert.Equal("https://fcm.googleapis.com/send/old", replacement.GetProperty("previous")[0].GetProperty("endpoint").GetString());
    }

    [Theory]
    [InlineData("new-only", 0, 0, 1)]
    [InlineData("null-both", 0, 0, 0)]
    [InlineData("expired", 1, 1, 1)]
    [InlineData("already-inactive", 1, 1, 1)]
    [InlineData("old-only", 1, 1, 1)]
    [InlineData("revoked", 0, 1, 0)]
    [InlineData("opted-out", 0, 1, 0)]
    [InlineData("same-endpoint", 0, 0, 1)]
    public async Task Renewal_RespectsProviderStatePermissionAndDurableOptOut(string scenario, int created, int removed, int saved)
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { BypassCSP = true });
        await page.GotoAsync(app.BaseAddress + "health");
        var sources = await Sources(app);
        var result = await page.EvaluateAsync<JsonElement>("""
            async ({ shared, worker, scenario }) => {
                const handlers = {}, calls = [];
                let created = 0, removed = 0, native = null;
                const subscription = (name, endpoint = name) => ({ expirationTime: null,
                    toJSON: () => ({ endpoint: 'https://fcm.googleapis.com/send/' + endpoint, keys: { p256dh: name, auth: name } }),
                    unsubscribe: async () => { removed++; native = null; return scenario !== 'already-inactive'; }
                });
                const old = subscription('old');
                const fresh = subscription('new', scenario === 'same-endpoint' ? 'old' : 'new');
                const scope = {
                    addEventListener: (type, handler) => { handlers[type] = handler; },
                    registration: { pushManager: { getSubscription: async () => native, subscribe: async () => { created++; return native = fresh; } } }
                };
                new Function('self', 'fetch', 'Notification', shared)(scope, async (url, options) => {
                    calls.push({ url, body: options.body && JSON.parse(options.body) });
                    return { ok: true, json: async () => ({ publicKey: 'BA' }) };
                }, { permission: scenario === 'revoked' ? 'denied' : 'granted' });
                new Function('self', 'LwcWebPush', 'importScripts', worker)(scope, scope.LwcWebPush, () => {});
                if (scenario === 'opted-out') await scope.LwcWebPush.disable(scope.registration);
                if (['expired', 'already-inactive'].includes(scenario)) { old.expirationTime = Date.now() - 1; native = old; }
                if (scenario === 'old-only') native = old;
                if (scenario === 'same-endpoint') native = old; // Event's new keys must beat its stale old handle.
                let done;
                handlers.pushsubscriptionchange({
                    oldSubscription: ['new-only', 'null-both'].includes(scenario) ? null : old,
                    newSubscription: ['expired', 'already-inactive', 'old-only', 'null-both'].includes(scenario) ? null : fresh,
                    waitUntil: promise => { done = promise; }
                });
                await done;
                return { created, removed, calls, state: await scope.LwcWebPush.state() };
            }
            """, new { sources.shared, sources.worker, scenario });
        Assert.Equal(created, result.GetProperty("created").GetInt32());
        Assert.Equal(removed, result.GetProperty("removed").GetInt32());
        Assert.Equal(saved, result.GetProperty("calls").EnumerateArray().Count(item => item.GetProperty("url").GetString() == "/api/web-push/subscribe"));
        if (scenario is "revoked" or "opted-out") Assert.False(result.GetProperty("state").GetProperty("enabled").GetBoolean());
        if (scenario == "same-endpoint") Assert.Equal("new", result.GetProperty("state").GetProperty("current").GetProperty("keys").GetProperty("auth").GetString());
    }

    [Fact]
    public async Task OptOut_DuringReplacementWinsAndFailedCleanupSurvivesReload()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { BypassCSP = true });
        await page.GotoAsync(app.BaseAddress + "health");
        var sources = await Sources(app);
        var result = await page.EvaluateAsync<JsonElement>("""
            async ({ shared, worker }) => {
                const calls = [], handlers = {};
                let release, started, native, cleanupFails = true, created = 0;
                const startedPromise = new Promise(resolve => { started = resolve; });
                const data = { endpoint: 'https://fcm.googleapis.com/send/new', keys: { p256dh: 'key', auth: 'auth' } };
                native = { toJSON: () => data, unsubscribe: async () => { native = null; return true; } };
                const registration = { pushManager: {
                    getSubscription: async () => native, subscribe: async () => { created++; throw new Error('Unexpected subscribe'); }
                } };
                const fetch = async (url, options) => {
                    calls.push(url);
                    if (url.endsWith('/subscribe')) { started(); return new Promise(resolve => { release = () => resolve({ ok: true }); }); }
                    return { ok: !cleanupFails, status: 503 };
                };
                const scope = { registration, addEventListener: (name, handler) => { handlers[name] = handler; } };
                new Function('self', 'fetch', 'Notification', shared)(scope, fetch, { permission: 'granted' });
                new Function('self', 'LwcWebPush', 'importScripts', worker)(scope, scope.LwcWebPush, () => {});
                let renewal;
                handlers.pushsubscriptionchange({ newSubscription: native, waitUntil: promise => { renewal = promise.catch(() => {}); } });
                await startedPromise;
                const optOut = scope.LwcWebPush.disable(registration).catch(() => {});
                while ((await scope.LwcWebPush.state()).enabled !== false) await new Promise(resolve => setTimeout(resolve, 0));
                release();
                await Promise.all([renewal, optOut]);
                const before = await scope.LwcWebPush.state();
                const restarted = {};
                new Function('self', 'fetch', 'Notification', shared)(restarted, fetch, { permission: 'granted' });
                cleanupFails = false;
                await restarted.LwcWebPush.sync(registration);
                return { before, after: await restarted.LwcWebPush.state(), created, native, calls };
            }
            """, new { sources.shared, sources.worker });
        Assert.False(result.GetProperty("before").GetProperty("enabled").GetBoolean());
        Assert.NotEmpty(result.GetProperty("before").GetProperty("pending").EnumerateArray());
        Assert.False(result.GetProperty("after").GetProperty("enabled").GetBoolean());
        Assert.Empty(result.GetProperty("after").GetProperty("pending").EnumerateArray());
        Assert.Equal(0, result.GetProperty("created").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("native").ValueKind);
        Assert.Equal(1, result.GetProperty("calls").EnumerateArray().Count(item => item.GetString() == "/api/web-push/subscribe"));
    }

    private static async Task<(string shared, string worker)> Sources(BrowserTestApp app)
    {
        using var client = app.CreateClient();
        return (await client.GetStringAsync("/js/web-push-sync.js"), await client.GetStringAsync("/js/web-push-worker.js"));
    }
}
