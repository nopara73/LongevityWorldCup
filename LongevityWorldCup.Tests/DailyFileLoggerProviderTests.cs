using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class DailyFileLoggerProviderTests
{
    [Fact]
    public void Constructor_CleansOnlyExpiredDateNamedLogs()
    {
        using var temp = new TempDirectory();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expiredLog = Path.Combine(temp.Path, $"{today.AddDays(-2):yyyy-MM-dd}.log");
        var retainedLog = Path.Combine(temp.Path, $"{today.AddDays(-1):yyyy-MM-dd}.log");
        var invalidNameLog = Path.Combine(temp.Path, "manual-note.log");
        File.WriteAllText(expiredLog, "expired");
        File.WriteAllText(retainedLog, "retained");
        File.WriteAllText(invalidNameLog, "manual");

        using var provider = new DailyFileLoggerProvider(temp.Path, retentionDays: 2);

        Assert.False(File.Exists(expiredLog));
        Assert.True(File.Exists(retainedLog));
        Assert.True(File.Exists(invalidNameLog));
    }

    [Fact]
    public void Logger_WritesCurrentUtcDayLog()
    {
        using var temp = new TempDirectory();
        using var provider = new DailyFileLoggerProvider(temp.Path);
        var logger = provider.CreateLogger("Tests.Category");

        logger.LogInformation("Hello {Name}", "Alice");

        var todayLog = Path.Combine(temp.Path, $"{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.log");
        var text = File.ReadAllText(todayLog);
        Assert.Contains("[Information] Tests.Category - Hello Alice", text, StringComparison.Ordinal);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lwc-daily-log-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
