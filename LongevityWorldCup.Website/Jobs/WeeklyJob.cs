using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public class WeeklyJob : IJob
{
    private readonly ILogger<WeeklyJob> _logger;
    private readonly AthleteDataService _athletes;

    public WeeklyJob(ILogger<WeeklyJob> logger, AthleteDataService athletes)
    {
        _logger = logger;
        _athletes = athletes;
    }

    public Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("WeeklyJob {ts}", DateTime.UtcNow);

        var ranked = _athletes.ComputeAgeDifferencesUtc();
        var updated = 0;

        for (var i = 0; i < ranked.Count; i++)
        {
            var obj = ranked[i]?.AsObject();
            if (obj is null) continue;

            var slug = obj["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;

            var rank = i + 1;
            try { _athletes.UpdatePlacements(slug, weekly: rank); updated++; }
            catch (Exception ex) { _logger.LogError(ex, "Weekly placement update failed for {Slug}", slug); }
        }

        _logger.LogInformation("Weekly placements stored for {count} athletes", updated);
        return Task.CompletedTask;
    }

}