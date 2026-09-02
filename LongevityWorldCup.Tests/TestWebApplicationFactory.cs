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

    public TestWebApplicationFactory()
        : this(configure: null)
    {
    }

    internal TestWebApplicationFactory(Action<IWebHostBuilder>? configure)
    {
        _configure = configure;
    }

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
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                if (Directory.Exists(_dbRoot))
                    Directory.Delete(_dbRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for transient test data.
            }
        }
    }
}
