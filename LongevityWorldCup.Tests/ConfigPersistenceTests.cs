using System.Text.Json;
using LongevityWorldCup.Website;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ConfigPersistenceTests
{
    [Fact]
    public async Task InitializeDefaultConfig_CanRunConcurrentlyForSameMissingFile()
    {
        using var temp = new TempDirectory();
        var configPath = Path.Combine(temp.Path, "config.json");

        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => Program.InitializeDefaultConfig(configPath)))
            .ToArray();

        await Task.WhenAll(tasks);

        var config = await Config.LoadAsync(configPath, Path.Combine(temp.Path, "runtime-config.json"));
        Assert.Equal("hi@longevityworldcup.com", config.EmailFrom);
        Assert.Equal("smtp.gmail.com", config.SmtpServer);
        Assert.NotNull(config.LongevitymaxxingChallenge);
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_AppliesRuntimeTokenConfig_WhenRuntimeConfigIsCurrent()
    {
        using var temp = new TempDirectory();
        var configPath = Path.Combine(temp.Path, "config.json");
        var runtimeConfigPath = Path.Combine(temp.Path, "runtime-config.json");

        await WriteConfigAsync(configPath, new Config
        {
            EmailFrom = "hi@example.com",
            XAccessToken = "old-x-access",
            XRefreshToken = "old-x-refresh",
            ThreadsAccessToken = "old-threads",
            ThreadsAccessTokenExpiresAtUtc = "2026-06-01T00:00:00.0000000Z",
            ThreadsAccessTokenLastRefreshAttemptAtUtc = "2026-06-01T12:00:00.0000000Z",
            FacebookUserAccessToken = "old-facebook-user",
            FacebookPageAccessToken = "old-facebook-page"
        });
        await WriteRuntimeConfigAsync(runtimeConfigPath, new
        {
            XAccessToken = "new-x-access",
            XRefreshToken = "new-x-refresh",
            ThreadsAccessToken = "new-threads",
            ThreadsAccessTokenExpiresAtUtc = "2026-07-01T00:00:00.0000000Z",
            ThreadsAccessTokenLastRefreshAttemptAtUtc = "2026-06-03T12:00:00.0000000Z",
            FacebookUserAccessToken = "new-facebook-user",
            FacebookPageAccessToken = "new-facebook-page"
        });

        File.SetLastWriteTimeUtc(configPath, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(runtimeConfigPath, DateTime.UtcNow);

        var config = await Config.LoadAsync(configPath, runtimeConfigPath);

        Assert.Equal("hi@example.com", config.EmailFrom);
        Assert.Equal("new-x-access", config.XAccessToken);
        Assert.Equal("new-x-refresh", config.XRefreshToken);
        Assert.Equal("new-threads", config.ThreadsAccessToken);
        Assert.Equal("2026-07-01T00:00:00.0000000Z", config.ThreadsAccessTokenExpiresAtUtc);
        Assert.Equal("2026-06-03T12:00:00.0000000Z", config.ThreadsAccessTokenLastRefreshAttemptAtUtc);
        Assert.Equal("new-facebook-user", config.FacebookUserAccessToken);
        Assert.Equal("new-facebook-page", config.FacebookPageAccessToken);
    }

    [Fact]
    public async Task LoadAsync_IgnoresRuntimeTokenConfig_WhenPrimaryConfigIsNewer()
    {
        using var temp = new TempDirectory();
        var configPath = Path.Combine(temp.Path, "config.json");
        var runtimeConfigPath = Path.Combine(temp.Path, "runtime-config.json");

        await WriteConfigAsync(configPath, new Config
        {
            XAccessToken = "manual-x-access",
            XRefreshToken = "manual-x-refresh",
            ThreadsAccessToken = "manual-threads"
        });
        await WriteRuntimeConfigAsync(runtimeConfigPath, new
        {
            XAccessToken = "old-sidecar-x-access",
            XRefreshToken = "old-sidecar-x-refresh",
            ThreadsAccessToken = "old-sidecar-threads"
        });

        File.SetLastWriteTimeUtc(runtimeConfigPath, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(configPath, DateTime.UtcNow);

        var config = await Config.LoadAsync(configPath, runtimeConfigPath);

        Assert.Equal("manual-x-access", config.XAccessToken);
        Assert.Equal("manual-x-refresh", config.XRefreshToken);
        Assert.Equal("manual-threads", config.ThreadsAccessToken);
    }

    [Fact]
    public async Task SaveAsync_FallsBackToRuntimeTokenConfig_WhenPrimaryConfigIsReadOnly()
    {
        using var temp = new TempDirectory();
        var configPath = Path.Combine(temp.Path, "config.json");
        var runtimeConfigPath = Path.Combine(temp.Path, "runtime-config.json");

        await File.WriteAllTextAsync(configPath, "{}");
        File.SetAttributes(configPath, File.GetAttributes(configPath) | FileAttributes.ReadOnly);

        try
        {
            var config = new Config
            {
                XAccessToken = "saved-x-access",
                XRefreshToken = "saved-x-refresh",
                ThreadsAccessToken = "saved-threads",
                ThreadsAccessTokenExpiresAtUtc = "2026-07-01T00:00:00.0000000Z",
                ThreadsAccessTokenLastRefreshAttemptAtUtc = "2026-06-03T12:00:00.0000000Z",
                FacebookUserAccessToken = "saved-facebook-user",
                FacebookPageAccessToken = "saved-facebook-page"
            }.UseFilePathsForTesting(configPath, runtimeConfigPath);

            await config.SaveAsync();
        }
        finally
        {
            File.SetAttributes(configPath, FileAttributes.Normal);
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(runtimeConfigPath));
        var root = doc.RootElement;

        Assert.Equal("saved-x-access", root.GetProperty("XAccessToken").GetString());
        Assert.Equal("saved-x-refresh", root.GetProperty("XRefreshToken").GetString());
        Assert.Equal("saved-threads", root.GetProperty("ThreadsAccessToken").GetString());
        Assert.Equal("2026-07-01T00:00:00.0000000Z", root.GetProperty("ThreadsAccessTokenExpiresAtUtc").GetString());
        Assert.Equal("2026-06-03T12:00:00.0000000Z", root.GetProperty("ThreadsAccessTokenLastRefreshAttemptAtUtc").GetString());
        Assert.Equal("saved-facebook-user", root.GetProperty("FacebookUserAccessToken").GetString());
        Assert.Equal("saved-facebook-page", root.GetProperty("FacebookPageAccessToken").GetString());
    }

    private static async Task WriteConfigAsync(string path, Config config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private static async Task WriteRuntimeConfigAsync(string path, object config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lwc-config-{Guid.NewGuid():N}");

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
