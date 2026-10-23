using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class MediaKitBrowserTests
{
    [Fact]
    public async Task Archive_PreviewsAndDownloadsTheVersionedDeployedBytes()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.UnrouteAsync("**/MediaKit.zip*");
        var page = await context.NewPageAsync();
        var previewResponse = page.WaitForResponseAsync(response =>
            new Uri(response.Url).AbsolutePath == "/assets/media-kit/MediaKit.zip");
        await page.GotoAsync("/media");
        var link = page.Locator(".media-kit-primary-download");
        var href = await link.GetAttributeAsync("href");
        Assert.Matches(@"^/assets/media-kit/MediaKit\.zip\?v=[0-9a-f]{16}$", href);
        await Assertions.Expect(link).ToHaveAttributeAsync("download", "MediaKit.zip");

        var environment = App.Services.GetRequiredService<IWebHostEnvironment>();
        var source = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..",
            "LongevityWorldCup.Documentation", "MediaKit.zip"));
        var original = await File.ReadAllBytesAsync(source);
        Assert.EndsWith(Convert.ToHexString(SHA256.HashData(original)[..8]).ToLowerInvariant(), href);
        var response = await previewResponse;
        Assert.True(response.Ok);
        Assert.Equal("application/zip", response.Headers["content-type"]);
        Assert.Contains("public", response.Headers["cache-control"]);
        Assert.Contains("immutable", response.Headers["cache-control"]);
        Assert.Equal(original, await response.BodyAsync());

        var download = await page.RunAndWaitForDownloadAsync(() => link.ClickAsync());
        Assert.Equal("MediaKit.zip", download.SuggestedFilename);
        Assert.Equal(original, await File.ReadAllBytesAsync((await download.PathAsync())!));
        Assert.Equal(href, await page.EvaluateAsync<string>(
            "() => new URL(performance.getEntriesByType('resource').find(r => r.name.includes('MediaKit.zip')).name).pathname + new URL(performance.getEntriesByType('resource').find(r => r.name.includes('MediaKit.zip')).name).search"));
    }

    [Fact]
    public async Task History_PreservesTheEntryUrlStateAndBackNavigation()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.AddInitScriptAsync("""
            if (location.pathname === '/media')
                history.replaceState({ returnTo: 'press-source' }, '', location.href);
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/about");
        await page.GotoAsync("/media?source=press#media-kit-title");
        await Assertions.Expect(page.Locator(".media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        Assert.EndsWith("/media?source=press#media-kit-title", page.Url);
        Assert.Equal("press-source", await page.EvaluateAsync<string>("history.state.returnTo"));
        Assert.False(await page.EvaluateAsync<bool>("Object.hasOwn(history.state, 'modal')"));
        await page.GoBackAsync();
        Assert.EndsWith("/about", page.Url);
        await page.GoForwardAsync();
        Assert.EndsWith("/media?source=press#media-kit-title", page.Url);
        await Assertions.Expect(page.Locator(".media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
    }

    [Fact]
    public async Task Archive_UsesTheCacheThenReloadsOnlyAfterAReadFailure()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.AddInitScriptAsync("""
            const originalFetch = window.fetch;
            window.archiveRequests = [];
            window.fetch = async (url, options) => {
                if (!String(url).includes('MediaKit.zip')) return originalFetch(url, options);
                archiveRequests.push({ url: String(url), cache: options?.cache ?? 'default' });
                if (archiveRequests.length === 1) throw new Error('Temporary transport failure');
                return originalFetch(url, options);
            };
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media");
        await Assertions.Expect(page.Locator(".media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        Assert.Equal(new[] { "default", "reload" },
            await page.EvaluateAsync<string[]>("archiveRequests.map(r => r.cache)"));
        Assert.True(await page.EvaluateAsync<bool>("archiveRequests[0].url === archiveRequests[1].url"));
        await Assertions.Expect(page.Locator("#fallback")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Archive_AbortsStalledBodiesAndStopsAfterOneRetry()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.AddInitScriptAsync("""
            const originalFetch = window.fetch, originalTimeout = window.setTimeout;
            window.archiveAttempts = 0;
            window.archiveAborts = 0;
            window.setTimeout = (callback, delay, ...args) =>
                originalTimeout(callback, delay === 30000 ? 25 : delay, ...args);
            window.fetch = async (url, options) => {
                if (!String(url).includes('MediaKit.zip')) return originalFetch(url, options);
                archiveAttempts++;
                return new Response(new ReadableStream({ start(body) {
                    options?.signal?.addEventListener('abort', () => {
                        archiveAborts++;
                        body.error(new DOMException('Timed out', 'AbortError'));
                    }, { once: true });
                } }));
            };
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fallback")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#loading")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".media-kit-primary-download")).ToBeVisibleAsync();
        Assert.Equal(2, await page.EvaluateAsync<int>("archiveAttempts"));
        Assert.Equal(2, await page.EvaluateAsync<int>("archiveAborts"));
    }

    [Fact]
    public async Task Archive_KeepsAProgressingTransferAliveBeyondTheIdleDeadline()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.AddInitScriptAsync("""
            const originalFetch = window.fetch, originalTimeout = window.setTimeout, originalClear = window.clearTimeout;
            const deadlines = new Map();
            let now = 0, nextDeadline = -1;
            window.archiveAttempts = 0;
            window.archiveAborts = 0;
            window.setTimeout = (callback, delay, ...args) => {
                if (delay !== 30000) return originalTimeout(callback, delay, ...args);
                const id = nextDeadline--;
                deadlines.set(id, { at: now + delay, callback });
                return id;
            };
            window.clearTimeout = id => deadlines.delete(id) || originalClear(id);
            window.advanceArchiveTime = elapsed => {
                now += elapsed;
                for (const [id, timer] of [...deadlines]) if (timer.at <= now) {
                    deadlines.delete(id);
                    timer.callback();
                }
            };
            window.fetch = async (url, options) => {
                if (!String(url).includes('MediaKit.zip')) return originalFetch(url, options);
                archiveAttempts++;
                return new Response(new ReadableStream({ start(body) {
                    window.archiveBody = body;
                    options.signal.addEventListener('abort', () => {
                        archiveAborts++;
                        body.error(new DOMException('Timed out', 'AbortError'));
                    }, { once: true });
                } }));
            };
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.archiveBody !== undefined");
        for (var chunk = 0; chunk < 3; chunk++)
        {
            await page.EvaluateAsync("""
                async () => {
                    advanceArchiveTime(20000);
                    archiveBody.enqueue(new Uint8Array([42]));
                    await new Promise(resolve => setTimeout(resolve, 0));
                }
                """);
        }
        await page.EvaluateAsync("archiveBody.close()");
        await Assertions.Expect(page.Locator(".media-file-row")).ToHaveCountAsync(KitFiles.Value.Count);
        await Assertions.Expect(page.Locator("#fallback")).ToBeHiddenAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("archiveAttempts"));
        Assert.Equal(0, await page.EvaluateAsync<int>("archiveAborts"));
    }
}
