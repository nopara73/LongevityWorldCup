using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class WebPushBrowserTests
{
    [Theory]
    [InlineData(1280, false)]
    [InlineData(360, false)]
    [InlineData(1280, true)]
    [InlineData(360, true)]
    public async Task Bell_RequiresClickPersistsOptInAndCanUnsubscribe(int width, bool dark)
    {
        await using var app = await BrowserTestApp.StartAsync();
        Configure(app);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = width, Height = 850 }, Permissions = ["notifications"], ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await context.GrantPermissionsAsync(["notifications"], new() { Origin = app.BaseAddress.GetLeftPart(UriPartial.Authority) });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var subscription = JsonSerializer.Serialize(WebPushTests.Subscription(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        // The bundled headless shell reports Notification.permission as denied even when CDP grants it.
        // Keep the real worker, HTTP API and UI; simulate permission and the provider subscription handshake.
        // Installed Chrome is used separately for the live permission/provider verification.
        await context.AddInitScriptAsync("""
            window.pushPermissionRequests = 0;
            Object.defineProperty(Notification, 'permission', { get: () => localStorage.getItem('testPushPermission') || 'default' });
            Notification.requestPermission = () => {
                window.pushPermissionRequests++;
                localStorage.setItem('testPushPermission', 'granted');
                return Promise.resolve('granted');
            };
            const documentSubscription = {
                toJSON: () => (SUBSCRIPTION),
                unsubscribe: async () => { localStorage.removeItem('testPushSubscribed'); return true; }
            };
            PushManager.prototype.getSubscription = async () => localStorage.getItem('testPushSubscribed') ? documentSubscription : null;
            PushManager.prototype.subscribe = async () => { localStorage.setItem('testPushSubscribed', '1'); return documentSubscription; };
            """.Replace("SUBSCRIPTION", subscription));
        var page = await context.NewPageAsync();
        await page.GotoAsync(app.BaseAddress + "events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var bell = page.Locator("#webPushToggle");
        await Assertions.Expect(bell).ToBeVisibleAsync();
        Assert.Equal("default", await page.EvaluateAsync<string>("Notification.permission"));
        Assert.Equal(0, await page.EvaluateAsync<int>("window.pushPermissionRequests"));
        Assert.Equal("false", await bell.GetAttributeAsync("aria-pressed"));
        var bounds = await bell.BoundingBoxAsync();
        Assert.True(bounds!.Width >= 44 && bounds.Height >= 44);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        Assert.Equal(["Instagram", "YouTube", "X", "Nostr", "Reddit", "Threads", "Mastodon"], await page.Locator(".footer-follow-links .footer-link").AllTextContentsAsync().ContinueWith(t => t.Result.Select(s => s.Trim()).ToArray()));
        var footerShortcut = page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Get browser notifications on Highlights", Exact = true });
        Assert.Null(await footerShortcut.GetAttributeAsync("aria-current"));
        await footerShortcut.ClickAsync();
        await Assertions.Expect(page.Locator("#eventBoardTitle")).ToBeInViewportAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.pushPermissionRequests"));
        await bell.ClickAsync();
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal(1, await page.EvaluateAsync<int>("window.pushPermissionRequests"));
        await page.ReloadAsync();
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "true");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.pushPermissionRequests"));
        await bell.ClickAsync();
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(page.Locator("#webPushStatus")).ToHaveTextAsync("Announcement notifications off.");
        using var client = app.CreateClient();
        using var worker = await client.GetAsync("/js/web-push-worker.js?v=test");
        Assert.Equal("/", Assert.Single(worker.Headers.GetValues("Service-Worker-Allowed")));
        Assert.Equal("no-cache", worker.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData(75000, true)]
    [InlineData(125000, false)]
    public async Task Bell_WaitsForSlowNativeRegistrationAndRemovesSubscriptionsAfterItsDeadline(int delay, bool enabled)
    {
        await using var app = await BrowserTestApp.StartAsync();
        Configure(app);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync();
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var subscription = JsonSerializer.Serialize(WebPushTests.Subscription(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await context.AddInitScriptAsync("""
            window.testUnsubscribeCount = 0;
            window.testSubscribeStarted = false;
            window.testSubscription = null;
            // Deliver this document's own preference broadcasts after the
            // timeout has rendered, reproducing delayed message dispatch.
            const NativeBroadcastChannel = window.BroadcastChannel;
            const pushChannels = [];
            const pendingPushMessages = [];
            window.BroadcastChannel = class extends NativeBroadcastChannel {
                constructor(name) {
                    super(name);
                    if (name === 'lwc-web-push') pushChannels.push(this);
                }
                postMessage(message) {
                    if (this.name !== 'lwc-web-push') return super.postMessage(message);
                    pendingPushMessages.push(() => pushChannels.filter(channel => channel !== this)
                        .forEach(channel => channel.dispatchEvent(new MessageEvent('message', { data: message }))));
                }
            };
            window.flushOwnPushChanges = () => pendingPushMessages.splice(0).forEach(deliver => deliver());
            window.sendExternalPushChange = () => {
                const sender = new NativeBroadcastChannel('lwc-web-push');
                sender.postMessage('changed');
                sender.close();
            };
            Object.defineProperty(Notification, 'permission', { get: () => 'granted' });
            Notification.requestPermission = async () => 'granted';
            const documentSubscription = {
                toJSON: () => (SUBSCRIPTION),
                unsubscribe: async () => { window.testUnsubscribeCount++; window.testSubscription = null; return true; }
            };
            PushManager.prototype.getSubscription = async () => window.testSubscription;
            PushManager.prototype.subscribe = () => {
                window.testSubscribeStarted = true;
                return new Promise(resolve => setTimeout(() => { window.testSubscription = documentSubscription; resolve(documentSubscription); }, DELAY));
            };
            """.Replace("SUBSCRIPTION", subscription).Replace("DELAY", delay.ToString()));
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync(app.BaseAddress + "events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var bell = page.Locator("#webPushToggle");
        await Assertions.Expect(bell).ToBeVisibleAsync();
        await bell.ClickAsync();
        await page.WaitForFunctionAsync("() => window.testSubscribeStarted");
        await Assertions.Expect(bell).ToBeDisabledAsync();
        await Assertions.Expect(bell.Locator(".fa-spinner.fa-spin")).ToBeVisibleAsync();
        // Observe the deadline before releasing a late provider result; jumping
        // over both timers at once can coalesce them in the browser's fake clock.
        await page.Clock.FastForwardAsync(enabled ? delay : 120001);
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", enabled ? "true" : "false");
        await Assertions.Expect(bell).ToBeEnabledAsync();
        if (enabled)
        {
            Assert.Equal(0, await page.EvaluateAsync<int>("window.testUnsubscribeCount"));
            await bell.ClickAsync();
            await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "false");
        }
        else
        {
            await Assertions.Expect(page.Locator("#webPushStatus")).ToHaveTextAsync("Notifications could not connect. Try again.");
            await page.EvaluateAsync("() => window.flushOwnPushChanges()");
            await Assertions.Expect(bell).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("#webPushStatus")).ToHaveTextAsync("Notifications could not connect. Try again.");
            await page.Clock.FastForwardAsync(delay - 120001);
        }
        await page.WaitForFunctionAsync("() => window.testUnsubscribeCount === 1");
        if (!enabled)
        {
            // A different sender must still refresh this document's preference.
            await page.EvaluateAsync("() => window.sendExternalPushChange()");
            await Assertions.Expect(page.Locator("#webPushStatus")).ToBeEmptyAsync();
            await Assertions.Expect(bell).ToBeEnabledAsync();
            await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "false");
        }
    }

    [Fact]
    public async Task Bell_EarlyPressIsNotMovedByTheDeferredOpeningScroll()
    {
        await using var app = await BrowserTestApp.StartAsync();
        Configure(app);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 1280, Height = 720 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var subscription = JsonSerializer.Serialize(WebPushTests.Subscription(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await context.AddInitScriptAsync(
            """
            window.testSubscribeStarted = false;
            Object.defineProperty(Notification, 'permission', { get: () => 'granted' });
            Notification.requestPermission = async () => 'granted';
            const documentSubscription = {
                toJSON: () => (SUBSCRIPTION),
                unsubscribe: async () => true
            };
            PushManager.prototype.getSubscription = async () => null;
            PushManager.prototype.subscribe = async () => {
                window.testSubscribeStarted = true;
                return documentSubscription;
            };
            """.Replace("SUBSCRIPTION", subscription));
        var page = await context.NewPageAsync();
        var clockTime = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc);
        await page.Clock.InstallAsync(new() { Time = clockTime.ToString("O") });
        await page.Clock.PauseAtAsync(clockTime.AddMinutes(1));
        await page.GotoAsync(app.BaseAddress + "events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var bell = page.Locator("#webPushToggle");
        await Assertions.Expect(bell).ToBeVisibleAsync();
        await page.EvaluateAsync("() => document.fonts.ready");
        var before = Assert.IsType<LocatorBoundingBoxResult>(await bell.BoundingBoxAsync());
        await page.Mouse.MoveAsync(before.X + before.Width / 2, before.Y + before.Height / 2);
        await page.Mouse.DownAsync();
        // Cross the opening-scroll deadline while the native press is held.
        await page.Clock.FastForwardAsync(600);
        var after = Assert.IsType<LocatorBoundingBoxResult>(await bell.BoundingBoxAsync());
        await page.Mouse.UpAsync();

        Assert.InRange(Math.Abs(before.Y - after.Y), 0, 0.5);
        await page.Clock.ResumeAsync();
        await page.WaitForFunctionAsync("() => window.testSubscribeStarted");
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "true");
    }

    [Fact]
    public async Task Designer_OffersIndependentUncheckedPushDestinationWithNotificationPreview()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new() { ViewportSize = new() { Width = 360, Height = 850 } });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync(app.BaseAddress + "internal/custom-event-designer.html", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var checkbox = page.Locator("#sendWebPush");
        await Assertions.Expect(checkbox).Not.ToBeCheckedAsync();
        await page.Locator("#titleInput").FillAsync("[bold](A sport for time) 🏆");
        await page.Locator("#contentInput").FillAsync("[strong](The next round) is here.");
        await checkbox.CheckAsync();
        await Assertions.Expect(page.Locator("#webPushPreview .push-preview-title")).ToHaveTextAsync("A sport for time 🏆");
        await Assertions.Expect(page.Locator("#webPushPreview .push-preview-body")).ToHaveTextAsync("The next round is here.");
        Assert.True(await page.EvaluateAsync<bool>("buildEventPayload().sendToWebPush"));
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        await page.ReloadAsync();
        await Assertions.Expect(checkbox).ToBeCheckedAsync();
    }

    [Fact]
    public async Task Worker_ShowsStableTaggedNotificationAndProtectsClickDestination()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { BypassCSP = true });
        await page.GotoAsync(app.BaseAddress + "events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        using var client = app.CreateClient();
        var source = await client.GetStringAsync("/js/web-push-worker.js");
        var result = await page.EvaluateAsync<JsonElement>("""
            async source => {
                const handlers = {}, shown = [], opened = [], waits = [];
                let draftNavigated = false;
                const scope = {
                    location: { origin: location.origin },
                    addEventListener: (type, callback) => { handlers[type] = callback; },
                    registration: { showNotification: async (title, options) => shown.push({ title, ...options }) },
                    clients: {
                        matchAll: async () => [{ url: location.origin + '/internal/custom-event-designer.html', navigate: async () => { draftNavigated = true; }, focus: async () => {} }],
                        openWindow: async url => opened.push(url)
                    }
                };
                new Function('self', 'importScripts', 'LwcWebPush', source)(scope, () => {}, { state: async () => ({ enabled: true }) });
                const push = () => handlers.push({ data: { json: () => ({ id: 'same-event', title: 'A sport for time', body: 'Hello', url: 'https://unrelated.example/' }) }, waitUntil: p => waits.push(p) });
                push(); push(); await Promise.all(waits);
                handlers.notificationclick({ notification: { close() {}, data: { url: 'https://unrelated.example/' } }, waitUntil: p => waits.push(p) });
                await Promise.all(waits);
                const result = { shown: [...shown], opened: [...opened], draftNavigated };
                handlers.push({ data: { json: () => ({ id: 'campaign', title: 'Coming from the Rejuvenation Olympics?', url: '/rejuvenation-olympics', image: '/assets/social/rejuvenation-olympics.png' }) }, waitUntil: p => waits.push(p) });
                await Promise.all(waits);
                handlers.notificationclick({ notification: { close() {}, data: shown.at(-1).data }, waitUntil: p => waits.push(p) });
                await Promise.all(waits);
                return { ...result, campaign: shown.at(-1), campaignOpened: opened.at(-1) };
            }
            """, source);
        Assert.Equal(2, result.GetProperty("shown").GetArrayLength());
        Assert.All(result.GetProperty("shown").EnumerateArray(), item =>
        {
            Assert.Equal("lwc-same-event", item.GetProperty("tag").GetString());
            Assert.False(item.GetProperty("renotify").GetBoolean());
            Assert.Equal(new Uri(app.BaseAddress, "events").ToString(), item.GetProperty("data").GetProperty("url").GetString());
        });
        Assert.Equal(new Uri(app.BaseAddress, "events").ToString(), result.GetProperty("opened")[0].GetString());
        Assert.False(result.GetProperty("draftNavigated").GetBoolean());
        Assert.Equal(new Uri(app.BaseAddress, "rejuvenation-olympics").ToString(), result.GetProperty("campaignOpened").GetString());
        Assert.Equal(new Uri(app.BaseAddress, "assets/social/rejuvenation-olympics.png").ToString(), result.GetProperty("campaign").GetProperty("image").GetString());
    }

    private static void Configure(BrowserTestApp app)
    {
        var runtime = app.Services.GetRequiredService<Config>();
        var test = WebPushTests.Configured();
        runtime.WebPushVapidSubject = test.WebPushVapidSubject;
        runtime.WebPushVapidPublicKey = test.WebPushVapidPublicKey;
        runtime.WebPushVapidPrivateKey = test.WebPushVapidPrivateKey;
    }
}
