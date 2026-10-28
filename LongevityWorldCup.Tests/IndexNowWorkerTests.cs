using LongevityWorldCup.Website.Business.IndexNow;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class IndexNowWorkerTests
{
    [Fact]
    public async Task SlowStartupWaitsForListeningAndShutdownCancelsThatWait()
    {
        using var lifetime = new StartupLifetime();
        var runningServices = new ScanObserver();
        var stoppingServices = new ScanObserver();
        var runningEnvironment = new ProductionEnvironment();
        var stoppingEnvironment = new ProductionEnvironment();
        using var running = CreateWorker(runningServices, runningEnvironment, lifetime);
        using var stopping = CreateWorker(stoppingServices, stoppingEnvironment, lifetime);
        try
        {
            await running.StartAsync(default);
            await stopping.StartAsync(default);
            await Task.WhenAll(runningEnvironment.Checked.Task, stoppingEnvironment.Checked.Task)
                .WaitAsync(TimeSpan.FromSeconds(10));

            // Both workers have entered ExecuteAsync. Keep startup held beyond the old
            // 30-second grace period: scanning here can submit before the key is served.
            var graceElapsed = Task.Delay(TimeSpan.FromSeconds(32));
            Assert.Same(graceElapsed, await Task.WhenAny(graceElapsed,
                runningServices.Requested.Task, stoppingServices.Requested.Task));

            await stopping.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(stopping.ExecuteTask!.IsCompletedSuccessfully);
            Assert.False(stoppingServices.Requested.Task.IsCompleted);

            lifetime.Start();
            await runningServices.Requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(stoppingServices.Requested.Task.IsCompleted);
        }
        finally
        {
            await running.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
            await stopping.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static IndexNowWorker CreateWorker(IServiceProvider services, IHostEnvironment environment,
        IHostApplicationLifetime lifetime) => new(services, environment, lifetime,
        Options.Create(new IndexNowOptions { Enabled = true }), NullLogger<IndexNowWorker>.Instance);

    private sealed class ScanObserver : IServiceProvider
    {
        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IndexNowContentSnapshot)) Requested.TrySetResult();
            return null;
        }
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public TaskCompletionSource Checked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string EnvironmentName
        {
            get { Checked.TrySetResult(); return Environments.Production; }
            set => throw new NotSupportedException();
        }
        public string ApplicationName { get; set; } = nameof(IndexNowWorkerTests);
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class StartupLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => default;
        public CancellationToken ApplicationStopped => default;
        public void Start() => _started.Cancel();
        public void StopApplication() { }
        public void Dispose() => _started.Dispose();
    }
}
