using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public sealed class MastodonDailyPostJob(MastodonAnnouncementService announcements) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        => await announcements.DispatchAsync(customOnly: false, cancellationToken);
}

[DisallowConcurrentExecution]
public sealed class MastodonCustomPostJob(MastodonAnnouncementService announcements) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        => await announcements.DispatchAsync(customOnly: true, cancellationToken);
}
