using System.Threading.Tasks;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Quartz;

namespace LongevityWorldCup.Website.Jobs
{
    public class DatabaseBackupJob : IJob
    {
        private readonly DatabaseManager _db;

        public DatabaseBackupJob(DatabaseManager db)
        {
            _db = db;
        }

        public Task Execute(IJobExecutionContext context)
        {
            try
            {
                var dir = System.IO.Path.Combine(EnvironmentHelpers.GetDataDir(), "Backups");
                _db.BackupDatabase(dir);
            }
            catch
            {
            }

            return Task.CompletedTask;
        }
    }
}