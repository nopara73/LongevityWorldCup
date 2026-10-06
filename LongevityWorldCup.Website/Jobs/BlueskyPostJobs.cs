using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public sealed class BlueskyDailyPostJob(BlueskyAnnouncementService announcements) : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => new(announcements.DispatchAsync(customOnly: false, cancellationToken));
}

[DisallowConcurrentExecution]
public sealed class BlueskyCustomPostJob(BlueskyAnnouncementService announcements) : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => new(announcements.DispatchAsync(customOnly: true, cancellationToken));
}
