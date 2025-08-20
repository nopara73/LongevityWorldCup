using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public class YearlyJob : IJob
{
    private readonly ILogger<YearlyJob> _logger;
    private readonly AthleteDataService _athletes;

    public YearlyJob(ILogger<YearlyJob> logger, AthleteDataService athletes)
    {
        _logger = logger;
        _athletes = athletes;
    }

    public Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("YearlyJob {ts}", DateTime.UtcNow);

        var ranked = _athletes.ComputeAgeDifferencesUtc();
        var updated = 0;

        for (var i = 0; i < ranked.Count; i++)
        {
            var obj = ranked[i]?.AsObject();
            if (obj is null) continue;

            var slug = obj["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;

            var rank = i + 1;
            try { _athletes.UpdatePlacements(slug, yearly: rank); updated++; }
            catch (Exception ex) { _logger.LogError(ex, "Yearly placement update failed for {Slug}", slug); }
        }

        _logger.LogInformation("Yearly placements stored for {count} athletes", updated);
        return Task.CompletedTask;
    }

}