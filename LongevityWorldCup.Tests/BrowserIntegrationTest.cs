using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

/// <summary>
/// Owns the Chromium process shared by the semantic browser-test collection.
/// Every test still creates an isolated browser context, while collection
/// teardown deterministically closes the process.
/// </summary>
public sealed class PlaywrightBrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
            await Browser.CloseAsync();
        _playwright?.Dispose();
    }
}

/// <summary>
/// Owns one production-equivalent Kestrel application for one browser-test
/// class. Tests that mutate server state explicitly opt into per-test hosts.
/// </summary>
public sealed class BrowserTestAppFixture : IAsyncLifetime
{
    public BrowserTestApp App { get; private set; } = null!;

    public async Task InitializeAsync()
        => App = await BrowserTestApp.StartAsync();

    public async Task DisposeAsync()
    {
        if (App is not null)
            await App.DisposeAsync();
    }
}

/// <summary>
/// Exposes the collection-owned browser and class-owned application. Browser
/// contexts remain explicitly test-owned through <c>await using</c>.
/// </summary>
public abstract class BrowserIntegrationTest(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : IAsyncLifetime, IClassFixture<BrowserTestAppFixture>
{
    protected BrowserTestApp App => appFixture.App;
    protected IBrowser Browser => browserFixture.Browser;

    public virtual Task InitializeAsync() => Task.CompletedTask;
    public virtual Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Exposes the collection-owned browser while giving every test its own
/// application host. Use this boundary for scenarios that mutate real server
/// state instead of routing the request in the browser context.
/// </summary>
public abstract class IsolatedBrowserIntegrationTest(PlaywrightBrowserFixture browserFixture)
    : IAsyncLifetime
{
    private BrowserTestApp? _app;

    protected BrowserTestApp App => _app
        ?? throw new InvalidOperationException("The isolated browser application has not started.");
    protected IBrowser Browser => browserFixture.Browser;

    public async Task InitializeAsync()
        => _app = await BrowserTestApp.StartAsync();

    public async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
