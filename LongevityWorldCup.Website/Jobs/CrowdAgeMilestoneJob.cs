using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public sealed class CrowdAgeMilestoneJob(EventDataService events) : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        events.PublishPendingCrowdAgeMilestones();
        return Task.CompletedTask;
    }
}
