using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public class FacebookDailyPostJob : IJob
{
    private readonly ILogger<FacebookDailyPostJob> _logger;
    private readonly AthleteDataService _athletes;
    private readonly FacebookEventService _facebookEvents;
    private readonly FacebookFillerPostLogService _fillerLog;

    public FacebookDailyPostJob(ILogger<FacebookDailyPostJob> logger, AthleteDataService athletes, FacebookEventService facebookEvents, FacebookFillerPostLogService fillerLog)
    {
        _logger = logger;
        _athletes = athletes;
        _facebookEvents = facebookEvents;
        _fillerLog = fillerLog;
    }

    public Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("FacebookDailyPostJob {ts}", DateTime.UtcNow);
        _facebookEvents.SetAthletesForFacebook(_athletes.GetAthletesForX());
        _ = _fillerLog.GetSuggestedFillersOrdered();
        _logger.LogInformation("FacebookDailyPostJob initialized athlete directory and filler log; content policy is not implemented yet.");
        return Task.CompletedTask;
    }
}
