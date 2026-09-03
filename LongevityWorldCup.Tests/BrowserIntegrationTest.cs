using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

/// <summary>
/// Leases the single test-process Chromium instance to one bounded browser-work
/// collection. Collections overlap through independent physical contexts
/// without multiplying renderer/browser processes.
/// </summary>
public sealed class PlaywrightBrowserFixture : IAsyncLifetime
{
    private SharedPlaywrightBrowser.Lease? _lease;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _lease = await SharedPlaywrightBrowser.AcquireAsync();
        Browser = _lease.Browser;
    }

    public async Task DisposeAsync()
    {
        if (_lease is not null)
            await _lease.DisposeAsync();
    }
}

/// <summary>
/// Reference-counts Chromium across xUnit collection fixtures. The last active
/// collection closes the browser deterministically; a later filtered collection
/// can safely create a new generation after that boundary.
/// </summary>
internal static class SharedPlaywrightBrowser
{
    private static readonly object Sync = new();
    private static Task<State>? s_stateTask;
    private static int s_leaseCount;

    public static async Task<Lease> AcquireAsync()
    {
        Task<State> stateTask;
        lock (Sync)
        {
            s_leaseCount++;
            stateTask = s_stateTask ??= State.StartAsync();
        }

        try
        {
            return new Lease(stateTask, await stateTask);
        }
        catch
        {
            ReleaseFailedAcquisition(stateTask);
            throw;
        }
    }

    private static void ReleaseFailedAcquisition(Task<State> stateTask)
    {
        lock (Sync)
        {
            s_leaseCount--;
            if (s_leaseCount == 0 && ReferenceEquals(s_stateTask, stateTask))
                s_stateTask = null;
        }
    }

    private static async ValueTask ReleaseAsync(Task<State> stateTask, State state)
    {
        State? stateToDispose = null;
        lock (Sync)
        {
            s_leaseCount--;
            if (s_leaseCount == 0 && ReferenceEquals(s_stateTask, stateTask))
            {
                s_stateTask = null;
                stateToDispose = state;
            }
        }

        if (stateToDispose is not null)
            await stateToDispose.DisposeAsync();
    }

    internal sealed class Lease(Task<State> stateTask, State state) : IAsyncDisposable
    {
        private int _disposed;

        public IBrowser Browser => state.Browser;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                await ReleaseAsync(stateTask, state);
        }
    }

    internal sealed class State(IPlaywright playwright, IBrowser browser) : IAsyncDisposable
    {
        public IBrowser Browser { get; } = browser;

        public static async Task<State> StartAsync()
        {
            var playwright = await Playwright.CreateAsync();
            try
            {
                var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true
                });
                return new State(playwright, browser);
            }
            catch
            {
                playwright.Dispose();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Browser.CloseAsync();
            }
            finally
            {
                playwright.Dispose();
            }
        }
    }
}

/// <summary>
/// Owns one production-equivalent application for one bounded browser-work
/// collection. Tests that mutate shared domain state opt into their own app.
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
/// Exposes the collection-owned browser and application. Each test
/// remains responsible for disposing its own isolated browser contexts.
/// </summary>
public abstract class BrowserIntegrationTest(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : IAsyncLifetime
{
    private BrowserTestApp? _isolatedApp;

    protected BrowserTestApp App => _isolatedApp ?? appFixture.App;
    protected IBrowser Browser => browserFixture.Browser;
    protected virtual bool UseIsolatedApplicationPerTest => false;

    public virtual async Task InitializeAsync()
    {
        if (UseIsolatedApplicationPerTest)
            _isolatedApp = await BrowserTestApp.StartAsync();
    }

    public virtual async Task DisposeAsync()
    {
        if (_isolatedApp is not null)
            await _isolatedApp.DisposeAsync();
    }
}
