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
        await page.GotoAsync(app.BaseAddress + "events");
        var bell = page.Locator("#webPushToggle");
        await Assertions.Expect(bell).ToBeVisibleAsync();
        Assert.Equal("default", await page.EvaluateAsync<string>("Notification.permission"));
        Assert.Equal(0, await page.EvaluateAsync<int>("window.pushPermissionRequests"));
        Assert.Equal("false", await bell.GetAttributeAsync("aria-pressed"));
        var bounds = await bell.BoundingBoxAsync();
        Assert.True(bounds!.Width >= 44 && bounds.Height >= 44);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        Assert.Equal(["X", "Nostr", "Reddit", "Threads", "YouTube", "Instagram"], await page.Locator(".footer-column:last-child .footer-link").AllTextContentsAsync().ContinueWith(t => t.Result.Select(s => s.Trim()).ToArray()));
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
            Object.defineProperty(Notification, 'permission', { get: () => 'granted' });
            Notification.requestPermission = async () => 'granted';
            const documentSubscription = {
                toJSON: () => (SUBSCRIPTION),
                unsubscribe: async () => { window.testUnsubscribeCount++; return true; }
            };
            PushManager.prototype.getSubscription = async () => null;
            PushManager.prototype.subscribe = () => {
                window.testSubscribeStarted = true;
                return new Promise(resolve => setTimeout(() => resolve(documentSubscription), DELAY));
            };
            """.Replace("SUBSCRIPTION", subscription).Replace("DELAY", delay.ToString()));
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync(app.BaseAddress + "events");
        var bell = page.Locator("#webPushToggle");
        await Assertions.Expect(bell).ToBeVisibleAsync();
        await bell.ClickAsync();
        await page.WaitForFunctionAsync("window.testSubscribeStarted");
        await Assertions.Expect(bell).ToBeDisabledAsync();
        await Assertions.Expect(bell.Locator(".fa-spinner.fa-spin")).ToBeVisibleAsync();
        await page.Clock.FastForwardAsync(delay);
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
        }
        await page.WaitForFunctionAsync("window.testUnsubscribeCount === 1");
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
        await page.GotoAsync(app.BaseAddress + "internal/custom-event-designer.html");
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
        await page.GotoAsync(app.BaseAddress + "events");
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
                new Function('self', source)(scope);
                const push = () => handlers.push({ data: { json: () => ({ id: 'same-event', title: 'A sport for time', body: 'Hello', url: 'https://unrelated.example/' }) }, waitUntil: p => waits.push(p) });
                push(); push(); await Promise.all(waits);
                handlers.notificationclick({ notification: { close() {}, data: { url: 'https://unrelated.example/' } }, waitUntil: p => waits.push(p) });
                await Promise.all(waits);
                return { shown, opened, draftNavigated };
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
