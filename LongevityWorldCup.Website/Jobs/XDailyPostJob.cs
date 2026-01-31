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

    public Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("XDailyPostJob {ts}", DateTime.UtcNow);

        // TODO: get pending X events (e.g. _events.GetPendingXEvents)
        // TODO: pick best event to post (selection logic)
        // TODO: build tweet text for chosen event
        // TODO: _xEvents.SendAsync(msg)
        // TODO: _events.MarkEventsXProcessed([id])

        return Task.CompletedTask;
    }
}
