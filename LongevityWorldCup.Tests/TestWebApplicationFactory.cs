using System.Runtime.ExceptionServices;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace LongevityWorldCup.Tests;

/// <summary>
/// Owns one isolated application host and database. xUnit class or collection
/// fixtures dispose the host at their explicit fixture boundary.
/// </summary>
public sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbRoot = Path.Combine(
        Path.GetTempPath(),
        "LongevityWorldCup.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly Action<IWebHostBuilder>? _configure;
    private HostDisposalSignal? _hostDisposalSignal;

    public TestWebApplicationFactory()
        : this(configure: null)
    {
    }

    internal TestWebApplicationFactory(Action<IWebHostBuilder>? configure)
    {
        _configure = configure;
    }

    internal string WorkingDirectory => _dbRoot;
    internal bool HostDisposalTrackingActive => _hostDisposalSignal is not null;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dbRoot);
        var dbPath = Path.Combine(_dbRoot, "test.db");
        builder.UseSetting("EnableScheduledJobs", "false");
        builder.UseSetting("EnableStartupBadgeRefresh", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<DeterministicExternalHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(serviceProvider =>
                serviceProvider.GetRequiredService<DeterministicExternalHttpClientFactory>());
            services.RemoveAll<DatabaseManager>();
            services.AddSingleton(_ => new DatabaseManager(dbPath: dbPath));
            services.RemoveAll<ApplicationSubmissionRetryStore>();
            services.AddSingleton(serviceProvider => new ApplicationSubmissionRetryStore(
                serviceProvider.GetRequiredService<IMemoryCache>(),
                Path.Combine(_dbRoot, "application-submission-responses"),
                TimeProvider.System));
        });
        _configure?.Invoke(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostLifetime>();
            services.AddSingleton<IHostLifetime>(_ =>
            {
                // IHostLifetime is resolved while the host itself is built,
                // before lazily created application services. DI disposes
                // singletons in reverse activation order, so this signal marks
                // the complete provider-disposal boundary independently of
                // any service a test later replaces.
                var signal = new HostDisposalSignal();
                _hostDisposalSignal = signal;
                return signal;
            });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        Exception? disposalFailure = null;
        try
        {
            await base.DisposeAsync();

            if (_hostDisposalSignal is not null)
                await _hostDisposalSignal.Disposed.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            disposalFailure = ex;
        }

        DeleteWorkingDirectoryAndRethrow(disposalFailure);
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            base.Dispose(disposing);
            return;
        }

        Exception? disposalFailure = null;
        try
        {
            base.Dispose(disposing);

            if (_hostDisposalSignal is not null)
                _hostDisposalSignal.Disposed.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            disposalFailure = ex;
        }

        DeleteWorkingDirectoryAndRethrow(disposalFailure);
    }

    private void DeleteWorkingDirectoryAndRethrow(Exception? disposalFailure)
    {
        Exception? cleanupFailure = null;
        try
        {
            Directory.Delete(_dbRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Disposal is idempotent, and configuring the host is lazy.
        }
        catch (Exception ex)
        {
            cleanupFailure = ex;
        }

        if (disposalFailure is not null && cleanupFailure is not null)
        {
            throw new AggregateException(
                "The test host failed to dispose and its isolated workspace also failed to delete.",
                disposalFailure,
                cleanupFailure);
        }

        if (disposalFailure is not null)
            ExceptionDispatchInfo.Capture(disposalFailure).Throw();

        if (cleanupFailure is not null)
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    private sealed class HostDisposalSignal : IHostLifetime, IDisposable
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed => _disposed.Task;

        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose() => _disposed.TrySetResult();
    }
}
