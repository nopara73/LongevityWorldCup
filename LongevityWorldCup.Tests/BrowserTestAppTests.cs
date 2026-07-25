using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BrowserTestAppTests
{
    [Fact]
    public async Task ParallelStartsUseUniqueKestrelAssignedPorts()
    {
        var starts = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(BrowserTestApp.StartAsync))
            .ToArray();

        try
        {
            var apps = await Task.WhenAll(starts);
            Assert.All(apps, app => Assert.True(app.BaseAddress.IsLoopback));
            Assert.Equal(apps.Length, apps.Select(app => app.BaseAddress.Port).Distinct().Count());
        }
        finally
        {
            var startedApps = starts
                .Where(start => start.IsCompletedSuccessfully)
                .Select(start => start.Result);

            await Task.WhenAll(startedApps.Select(app => app.DisposeAsync().AsTask()));
        }
    }
}
