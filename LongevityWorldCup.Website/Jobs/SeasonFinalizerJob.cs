using Quartz;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Jobs
{
    public sealed class SeasonFinalizerJob : IJob
    {
        private readonly SeasonFinalizerService _seasonFinalizer;

        public SeasonFinalizerJob(SeasonFinalizerService seasonFinalizer)
        {
            _seasonFinalizer = seasonFinalizer ?? throw new ArgumentNullException(nameof(seasonFinalizer));
        }

        public Task Execute(IJobExecutionContext context)
        {
            _seasonFinalizer.TryFinalizeActiveSeason(DateTime.UtcNow);
            return Task.CompletedTask;
        }
    }
}