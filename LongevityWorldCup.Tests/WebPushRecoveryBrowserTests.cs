using System.Text.Json;
using LongevityWorldCup.Website;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class WebPushRecoveryBrowserTests
{
    [Fact]
    public async Task Bell_ReleasesBusyStateWhenNativePermissionApisThrow()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync();
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/web-push/configuration", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = """{"publicKey":"BA"}"""
        }));
        await context.AddInitScriptAsync("""
            window.permissionRequests = 0;
            Object.defineProperty(Notification, 'permission', { get: () => 'default' });
            Notification.requestPermission = () => { permissionRequests++; throw new Error('Permission unavailable'); };
            navigator.permissions.query = () => { throw new TypeError('Unsupported descriptor'); };
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync(app.BaseAddress + "events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var bell = page.Locator("#webPushToggle");
        await Assertions.Expect(bell).ToBeVisibleAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("permissionRequests"));
        await bell.ClickAsync();
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(bell).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#webPushStatus")).ToHaveTextAsync("Permission unavailable");
        Assert.Equal(1, await page.EvaluateAsync<int>("permissionRequests"));
        Assert.True(await page.EvaluateAsync<bool>("navigator.serviceWorker.getRegistration('/').then(value => !value)"));
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(360)]
    public async Task Bell_ReportsFailedSyncRecoversOnReconnectAndKeepsOptOutAcrossReload(int width)
    {
        await using var app = await BrowserTestApp.StartAsync();
        var config = app.Services.GetRequiredService<Config>();
        var signing = WebPushTests.Configured();
        config.WebPushVapidSubject = signing.WebPushVapidSubject;
        config.WebPushVapidPublicKey = signing.WebPushVapidPublicKey;
        config.WebPushVapidPrivateKey = signing.WebPushVapidPrivateKey;
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        await using var context = await browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = width, Height = 850 }, ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var fixture = JsonSerializer.Serialize(WebPushTests.Subscription(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await context.AddInitScriptAsync("""
            window.permissionRequests = 0;
            Object.defineProperty(Notification, 'permission', { get: () => localStorage.getItem('permission') || 'default' });
            Notification.requestPermission = async () => { permissionRequests++; localStorage.setItem('permission', 'granted'); return 'granted'; };
            // Provider handshakes are fictional; never use real push subscriptions or background writes.
            if (window.SyncManager) SyncManager.prototype.register = async () => {};
            const wrap = value => ({ toJSON: () => value, unsubscribe: async () => { localStorage.removeItem('subscription'); return true; } });
            PushManager.prototype.getSubscription = async () => {
                const saved = localStorage.getItem('subscription');
                return saved ? wrap(JSON.parse(saved)) : null;
            };
            PushManager.prototype.subscribe = async () => {
                const count = Number(localStorage.getItem('providerSubscribes') || 0) + 1;
                localStorage.setItem('providerSubscribes', String(count));
                const value = SUBSCRIPTION;
                value.endpoint += '/' + count;
                localStorage.setItem('subscription', JSON.stringify(value));
                return wrap(value);
            };
            """.Replace("SUBSCRIPTION", fixture));
        var page = await context.NewPageAsync();
        await page.GotoAsync(app.BaseAddress + "events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var bell = page.Locator("#webPushToggle");
        await Assertions.Expect(bell).ToBeVisibleAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("permissionRequests"));
        await bell.ClickAsync();
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "true");
        await context.RouteAsync("**/api/web-push/subscribe", route => route.FulfillAsync(new() { Status = 503 }));

        // Optional matched rendered evidence, using the actual previous entry point.
        var beforePath = Environment.GetEnvironmentVariable("LWC_PUSH_BEFORE_SCRIPT");
        if (beforePath is not null)
        {
            await context.RouteAsync("**/js/web-push.js*", route => route.FulfillAsync(new() { Path = beforePath, ContentType = "application/javascript" }));
            await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "true");
            await Capture(page, width, "before");
            await context.UnrouteAsync("**/js/web-push.js*");
        }
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-label", "Turn off announcement notifications — sync pending");
        await Assertions.Expect(page.Locator("#webPushStatus")).ToHaveTextAsync("Notifications need syncing. Retrying automatically.");
        await Assertions.Expect(bell.Locator(".fa-exclamation-circle")).ToBeVisibleAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("permissionRequests"));
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        await Capture(page, width, "after");

        await context.UnrouteAsync("**/api/web-push/subscribe");
        await page.EvaluateAsync("dispatchEvent(new Event('online'))");
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(page.Locator("#webPushStatus")).ToHaveTextAsync("");
        await context.RouteAsync("**/api/web-push/unsubscribe", route => route.FulfillAsync(new() { Status = 503 }));
        await bell.ClickAsync();
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-label", "Retry notification preferences");
        Assert.Null(await page.EvaluateAsync<string?>("localStorage.getItem('subscription')"));
        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-label", "Retry notification preferences");
        Assert.Equal("1", await page.EvaluateAsync<string>("localStorage.getItem('providerSubscribes')"));
        await context.UnrouteAsync("**/api/web-push/unsubscribe");
        await page.EvaluateAsync("dispatchEvent(new Event('online'))");
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-label", "Get announcement notifications");

        await bell.ClickAsync(); // A fresh provider identity can explicitly opt back in.
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "true");
        await page.EvaluateAsync("localStorage.setItem('permission', 'denied'); dispatchEvent(new Event('online'))");
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-pressed", "false");
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-label", "Notifications blocked — allow them in browser settings");
        await Assertions.Expect(bell).ToBeDisabledAsync();
        await Assertions.Expect(bell).ToHaveAttributeAsync("aria-busy", "false");
        Assert.Null(await page.EvaluateAsync<string?>("localStorage.getItem('subscription')"));
        Assert.Equal(0, await page.EvaluateAsync<int>("permissionRequests"));
    }

    private static async Task Capture(IPage page, int width, string state)
    {
        var directory = Environment.GetEnvironmentVariable("LWC_PUSH_REVIEW_DIR");
        if (directory is null) return;
        Directory.CreateDirectory(directory);
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => setTimeout(resolve, 1000)); scrollTo(0, 0); await new Promise(requestAnimationFrame); }");
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{state}-{width}.png"), Animations = ScreenshotAnimations.Disabled });
    }
}
