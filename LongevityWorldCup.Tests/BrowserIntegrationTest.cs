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
/// Exposes the collection-owned browser while giving every test its own
/// production-equivalent application, database, rate limiters, and caches.
/// Browser contexts remain explicitly test-owned through <c>await using</c>.
/// </summary>
public abstract class BrowserIntegrationTest(PlaywrightBrowserFixture browserFixture)
    : IAsyncLifetime
{
    private BrowserTestApp? _app;

    protected BrowserTestApp App => _app
        ?? throw new InvalidOperationException("The browser test application has not started.");
    protected IBrowser Browser => browserFixture.Browser;

    public virtual async Task InitializeAsync()
        => _app = await BrowserTestApp.StartAsync();

    public virtual async Task DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
