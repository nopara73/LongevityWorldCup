using Microsoft.Extensions.Options;

namespace LongevityWorldCup.Website.Business.IndexNow;

public sealed class IndexNowWorker(
    IServiceProvider services,
    IHostEnvironment environment,
    IOptions<IndexNowOptions> options,
    ILogger<IndexNowWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IndexNowOptions.CanSubmit(environment, options.Value)) return;

        // Startup must finish publishing athlete, badge and event snapshots and listening for
        // key verification before the first submission. No outbound calls run on requests.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            using var timer = new PeriodicTimer(IndexNowOptions.ScanInterval);
            do
            {
                try
                {
                    var source = services.GetRequiredService<IndexNowContentSnapshot>();
                    var submitter = services.GetRequiredService<IndexNowSubmitter>();
                    var content = source.Build(stoppingToken);
                    var now = DateTimeOffset.UtcNow;
                    submitter.Reconcile(content, now);
                    await submitter.SubmitNextBatchAsync(now, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "IndexNow scan/submission failed; retaining the previous ledger for the next scan.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
