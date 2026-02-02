using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public class XDailyPostJob : IJob
{
    private readonly ILogger<XDailyPostJob> _logger;
    private readonly EventDataService _events;
    private readonly XEventService _xEvents;

    public XDailyPostJob(ILogger<XDailyPostJob> logger, EventDataService events, XEventService xEvents)
    {
        _logger = logger;
        _events = events;
        _xEvents = xEvents;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("XDailyPostJob {ts}", DateTime.UtcNow);

        var toUtc = DateTime.UtcNow;
        var fromUtc = toUtc.AddDays(-7);
        var pending = _events.GetPendingXEvents(fromUtc, toUtc, limit: 20);

        foreach (var (id, type, text, _, _) in pending)
        {
            var msg = _xEvents.TryBuildMessage(type, text);
            if (string.IsNullOrWhiteSpace(msg)) continue;

            await _xEvents.SendAsync(msg);
            _events.MarkEventsXProcessed(new[] { id });
            _logger.LogInformation("XDailyPostJob posted event {Id}", id);
            return;
        }

        _logger.LogInformation("XDailyPostJob no postable event found");
    }
}
