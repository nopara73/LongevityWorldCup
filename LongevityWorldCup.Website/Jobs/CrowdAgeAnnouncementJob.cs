using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public sealed class CrowdAgeAnnouncementJob(EventDataService events) : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        events.PublishPendingCrowdAgeAnnouncements();
        return ValueTask.CompletedTask;
    }
}
