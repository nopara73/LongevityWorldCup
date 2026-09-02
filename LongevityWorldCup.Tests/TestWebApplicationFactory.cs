using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dbRoot);
        var dbPath = Path.Combine(_dbRoot, "test.db");
        builder.UseSetting("EnableScheduledJobs", "false");
        builder.UseSetting("EnableStartupBadgeRefresh", "false");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(_ =>
            {
                var signal = new HostDisposalSignal();
                _hostDisposalSignal = signal;
                return signal;
            });
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<DeterministicExternalHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(serviceProvider =>
                serviceProvider.GetRequiredService<DeterministicExternalHttpClientFactory>());
            services.RemoveAll<DatabaseManager>();
            services.AddSingleton(serviceProvider =>
            {
                // Resolve the signal before the database so reverse-order DI
                // disposal completes it only after the database is closed.
                _ = serviceProvider.GetRequiredService<HostDisposalSignal>();
                return new DatabaseManager(dbPath: dbPath);
            });
            services.RemoveAll<ApplicationSubmissionRetryStore>();
            services.AddSingleton(serviceProvider => new ApplicationSubmissionRetryStore(
                serviceProvider.GetRequiredService<IMemoryCache>(),
                Path.Combine(_dbRoot, "application-submission-responses"),
                TimeProvider.System));
        });
        _configure?.Invoke(builder);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_hostDisposalSignal is not null)
            await _hostDisposalSignal.Disposed.WaitAsync(TimeSpan.FromSeconds(10));

        DeleteWorkingDirectory();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        if (_hostDisposalSignal is not null)
            _hostDisposalSignal.Disposed.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

        DeleteWorkingDirectory();
    }

    private void DeleteWorkingDirectory()
    {
        try
        {
            Directory.Delete(_dbRoot, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Disposal is idempotent, and configuring the host is lazy.
        }
    }

    private sealed class HostDisposalSignal : IDisposable
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Disposed => _disposed.Task;

        public void Dispose() => _disposed.TrySetResult();
    }
}
