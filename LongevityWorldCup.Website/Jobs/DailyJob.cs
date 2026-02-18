using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.Logging;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public class DailyJob : IJob
{
    private readonly ILogger<DailyJob> _logger;
    private readonly AthleteDataService _athletes;
    private readonly AgentApplicationDataService _agentAppService;

    public DailyJob(ILogger<DailyJob> logger, AthleteDataService athletes, AgentApplicationDataService agentAppService)
    {
        _logger = logger;
        _athletes = athletes;
        _agentAppService = agentAppService;
    }

    public Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("DailyJob {ts}", DateTime.UtcNow);

        var ranked = _athletes.GetRankingsOrder();
        var updated = 0;

        for (var i = 0; i < ranked.Count; i++)
        {
            var obj = ranked[i]?.AsObject();
            if (obj is null) continue;

            var slug = obj["AthleteSlug"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(slug)) continue;

            var rank = i + 1;
            try { _athletes.UpdatePlacements(slug, yesterday: rank); updated++; }
            catch (Exception ex) { _logger.LogError(ex, "Daily placement update failed for {Slug}", slug); }
        }

        _logger.LogInformation("Daily placements stored for {count} athletes", updated);

        try
        {
            _agentAppService.CleanupOld();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old agent application tokens");
        }

        return Task.CompletedTask;
    }

}