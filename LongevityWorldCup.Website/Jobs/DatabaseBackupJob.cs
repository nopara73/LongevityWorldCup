using System.Threading.Tasks;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.Logging;
using Quartz;

namespace LongevityWorldCup.Website.Jobs
{
    [DisallowConcurrentExecution]
    public class DatabaseBackupJob : IJob
    {
        private readonly DatabaseManager _db;
        private readonly ILogger<DatabaseBackupJob> _logger;

        public DatabaseBackupJob(DatabaseManager db, ILogger<DatabaseBackupJob> logger)
        {
            _db = db;
            _logger = logger;
        }

        public Task Execute(IJobExecutionContext context)
        {
            try
            {
                var dir = System.IO.Path.Combine(EnvironmentHelpers.GetDataDir(), "Backups");
                var backupPath = _db.BackupDatabase(dir);
                _logger.LogInformation("Database backup completed at {BackupPath}", backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database backup job failed.");
            }

            return Task.CompletedTask;
        }
    }
}
