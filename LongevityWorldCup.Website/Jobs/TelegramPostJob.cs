using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public sealed class TelegramPostJob(TelegramAnnouncementService announcements) : IJob
{
    internal const string CustomOnlyKey = "telegramCustomOnly";

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) =>
        new(announcements.DispatchAsync(context.MergedJobDataMap.GetBoolean(CustomOnlyKey), cancellationToken));
}
