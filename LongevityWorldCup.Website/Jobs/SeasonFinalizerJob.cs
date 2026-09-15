using System.Threading;
using Quartz;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Jobs
{
    [DisallowConcurrentExecution]
    public sealed class SeasonFinalizerJob : IJob
    {
        private readonly SeasonFinalizerService _seasonFinalizer;
        private readonly BadgeDataService _badges;

        private static int _badgesRefreshedAfterSeasonClose;

        public SeasonFinalizerJob(SeasonFinalizerService seasonFinalizer, BadgeDataService badges)
        {
            _seasonFinalizer = seasonFinalizer ?? throw new ArgumentNullException(nameof(seasonFinalizer));
            _badges = badges ?? throw new ArgumentNullException(nameof(badges));
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var r = _seasonFinalizer.TryFinalizeActiveSeason(DateTime.UtcNow);

            if (!r.IsDue)
                return ValueTask.CompletedTask;

            if (!r.AlreadyFinalized)
            {
                _badges.ComputeAndPersistAwards();
                Interlocked.Exchange(ref _badgesRefreshedAfterSeasonClose, 1);
                return ValueTask.CompletedTask;
            }

            if (Interlocked.CompareExchange(ref _badgesRefreshedAfterSeasonClose, 1, 0) == 0)
                _badges.ComputeAndPersistAwards();

            return ValueTask.CompletedTask;
        }
    }
}
