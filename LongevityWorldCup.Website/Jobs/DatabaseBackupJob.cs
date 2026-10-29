using System.Threading.Tasks;
using LongevityWorldCup.Website.Business;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Quartz;

namespace LongevityWorldCup.Website.Jobs
{
    [DisallowConcurrentExecution]
    public class DatabaseBackupJob : IJob
    {
        private readonly RecoveryBackupService _backups;
        private readonly ILogger<DatabaseBackupJob> _logger;

        public DatabaseBackupJob(RecoveryBackupService backups, ILogger<DatabaseBackupJob> logger)
        {
            _backups = backups;
            _logger = logger;
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var backup = await _backups.CreateVerifiedAsync(cancellationToken);
                var triggerKey = new TriggerKey("DatabaseBackupTrigger");
                var trigger = await context.Scheduler.GetTrigger(triggerKey, cancellationToken) as ICronTrigger
                    ?? throw new InvalidOperationException("The daily recovery backup trigger is missing.");
                var state = await context.Scheduler.GetTriggerState(triggerKey, cancellationToken);
                var statusPath = Path.Combine(_backups.BackupDirectory, "schedule-status.json");
                var temporary = statusPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await using (var stream = RecoveryBundle.CreatePrivateFile(temporary))
                    {
                        await JsonSerializer.SerializeAsync(stream, new
                        {
                            JobId = context.JobDetail.Key.Name, TriggerId = triggerKey.Name,
                            Status = state is TriggerState.Normal or TriggerState.Blocked &&
                                context.Scheduler.Status == SchedulerStatus.Running ? "ACTIVE" : state.ToString(),
                            Cron = trigger.CronExpressionString, Timezone = trigger.TimeZone.Id,
                            NextRunUtc = trigger.NextFireTimeUtc, LastVerifiedAtUtc = DateTimeOffset.UtcNow,
                            Backup = backup
                        }, cancellationToken: cancellationToken);
                        await stream.FlushAsync(cancellationToken);
                        stream.Flush(flushToDisk: true);
                    }
                    File.Move(temporary, statusPath, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                _logger.LogInformation(
                    "Recovery backup verified at {BackupPath}. Files={FileCount} Bytes={Bytes} Sha256={Sha256} DailyCopies={DailyCopies}",
                    backup.Path, backup.FileCount, backup.UncompressedBytes, backup.Sha256, backup.RetainedDailyCopies);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recovery backup failed; previous verified daily copies remain available.");
                throw new JobExecutionException(ex);
            }
        }
    }
}
