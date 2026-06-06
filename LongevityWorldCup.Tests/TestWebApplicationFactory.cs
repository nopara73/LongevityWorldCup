using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LongevityWorldCup.Tests;

internal sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbRoot = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", Guid.NewGuid().ToString("N"));
    private readonly Action<IWebHostBuilder>? _configure;

    public TestWebApplicationFactory(Action<IWebHostBuilder>? configure = null)
    {
        _configure = configure;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var dbPath = Path.Combine(_dbRoot, "test.db");
        builder.UseSetting("EnableScheduledJobs", "false");
        builder.UseSetting("EnableStartupBadgeRefresh", "false");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DatabaseManager>();
            services.AddSingleton(_ => new DatabaseManager(dbPath: dbPath));
        });
        _configure?.Invoke(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        try
        {
            if (Directory.Exists(_dbRoot))
                Directory.Delete(_dbRoot, recursive: true);
        }
        catch
        {
        }
    }
}
