using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public class FacebookDailyPostJob : IJob
{
    private readonly ILogger<FacebookDailyPostJob> _logger;
    private readonly EventDataService _events;
    private readonly AthleteDataService _athletes;
    private readonly FacebookEventService _facebookEvents;
    private readonly FacebookFillerPostLogService _fillerLog;

    public FacebookDailyPostJob(ILogger<FacebookDailyPostJob> logger, EventDataService events, AthleteDataService athletes, FacebookEventService facebookEvents, FacebookFillerPostLogService fillerLog)
    {
        _logger = logger;
        _events = events;
        _athletes = athletes;
        _facebookEvents = facebookEvents;
        _fillerLog = fillerLog;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("FacebookDailyPostJob {ts}", DateTime.UtcNow);
        _facebookEvents.SetAthletesForFacebook(_athletes.GetAthletesForX());
        var pending = _events.GetPendingFacebookEvents();

        foreach (var (id, type, text, _, _, visibleOnWebsite, _) in pending)
        {
            var msg = _facebookEvents.TryBuildMessage(type, text, id, visibleOnWebsite);
            if (string.IsNullOrWhiteSpace(msg))
                continue;

            var sent = await _facebookEvents.TrySendEventAsync(type, text, id, visibleOnWebsite);
            if (!sent)
            {
                _logger.LogWarning("FacebookDailyPostJob send failed for event {Id}; leaving unprocessed", id);
                return;
            }

            _events.MarkEventsFacebookProcessed(new[] { id });
            _logger.LogInformation("FacebookDailyPostJob posted event {Id}", id);
            return;
        }

        _ = _fillerLog.GetSuggestedFillersOrdered();
        _logger.LogInformation("FacebookDailyPostJob no postable event found");
    }
}
