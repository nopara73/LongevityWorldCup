using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class DatabaseFileWatcherTests
{
    [Fact]
    public async Task SlowSubscriber_DoesNotOverlapAndReceivesChangesMadeWhileItWasBlocked()
    {
        var root = Path.Combine(Path.GetTempPath(), "lwc-db-watcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "test.db");
        await File.WriteAllTextAsync(path, "initial");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lastObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var overlap = 0;
        var calls = 0;
        try
        {
            using var watcher = new DatabaseFileWatcher(path, TimeSpan.FromMilliseconds(50));
            watcher.DatabaseChanged += () =>
            {
                if (Interlocked.Increment(ref active) != 1)
                    Interlocked.Exchange(ref overlap, 1);
                var call = Interlocked.Increment(ref calls);
                try
                {
                    var value = File.ReadAllText(path);
                    if (call == 1)
                    {
                        entered.TrySetResult();
                        release.Wait(TimeSpan.FromSeconds(10));
                    }
                    else if (value == "last")
                    {
                        lastObserved.TrySetResult();
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                    if (call == 1) firstExited.TrySetResult();
                }
            };

            try
            {
                await File.WriteAllTextAsync(path, "first");
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await File.WriteAllTextAsync(path, "second");
                await Task.Delay(400);
                await File.WriteAllTextAsync(path, "last");
                // Exercise further debounce/poll ticks while the first subscriber is held.
                await Task.Delay(1200);
                Assert.Equal(1, Volatile.Read(ref calls));
                Assert.Equal(0, Volatile.Read(ref overlap));
                release.Set();
                await firstExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await lastObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(0, Volatile.Read(ref overlap));
            }
            finally
            {
                release.Set();
                if (entered.Task.IsCompleted)
                    await firstExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
