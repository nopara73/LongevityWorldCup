using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.Logging;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public class XDailyPostJob : IJob
{
    private readonly ILogger<XDailyPostJob> _logger;
    private readonly EventDataService _events;
    private readonly XEventService _xEvents;
    private readonly AthleteDataService _athletes;
    private readonly XFillerPostLogService _fillerLog;

    public XDailyPostJob(ILogger<XDailyPostJob> logger, EventDataService events, XEventService xEvents, AthleteDataService athletes, XFillerPostLogService fillerLog)
    {
        _logger = logger;
        _events = events;
        _xEvents = xEvents;
        _athletes = athletes;
        _fillerLog = fillerLog;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("XDailyPostJob {ts}", DateTime.UtcNow);

        _events.SetAthletesForX(_athletes.GetAthletesForX());

        var pending = _events.GetPendingXEvents();
        var freshCutoff = DateTime.UtcNow.AddDays(-7);

        foreach (var (id, type, text, occurredAtUtc, _, xPriority) in pending)
        {
            if (type == EventType.BadgeAward)
            {
                if (EventHelpers.TryExtractBadgeLabel(text, out var label))
                {
                    var norm = EventHelpers.NormalizeBadgeLabel(label);
                    if (string.Equals(norm, "Podcast", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var isBestImprovement =
                        string.Equals(norm, "PhenoAge Best Improvement", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(norm, "Bortz Age Best Improvement", StringComparison.OrdinalIgnoreCase);
                    if (isBestImprovement && !_athletes.HasSingleGlobalPlaceOneBadgeHolder(label))
                    {
                        _logger.LogInformation("XDailyPostJob skipped tie Best Improvement badge event {BadgeLabel}", label);
                        continue;
                    }
                }
            }

            if (xPriority <= EventDataService.XPriorityPrimaryMax && occurredAtUtc < freshCutoff)
                continue;

            var msg = _xEvents.TryBuildMessage(type, text);
            if (string.IsNullOrWhiteSpace(msg)) continue;

            var sent = await _xEvents.TrySendAsync(msg);
            if (!sent)
            {
                _logger.LogWarning("XDailyPostJob send failed for event {Id}; leaving unprocessed", id);
                return;
            }
            _events.MarkEventsXProcessed(new[] { id });
            _logger.LogInformation("XDailyPostJob posted event {Id}", id);
            return;
        }

        var fillerCandidates = _fillerLog.GetSuggestedFillersOrdered();
        foreach (var (fillerType, payloadText) in fillerCandidates)
        {
            var payload = payloadText ?? "";
            var cooldown = GetCooldownForFiller(fillerType);
            var nowUtc = DateTime.UtcNow;
            var onCooldown = _fillerLog.IsOnCooldownForType(fillerType, cooldown, nowUtc);
            if (onCooldown)
            {
                _logger.LogInformation("XDailyPostJob skipped filler in cooldown {FillerType} {PayloadText} ({CooldownDays}d)", fillerType, payloadText, cooldown.TotalDays);
                continue;
            }

            var fillerMsg = _xEvents.TryBuildFillerMessage(fillerType, payload);
            if (string.IsNullOrWhiteSpace(fillerMsg))
                continue;

            var infoToken = TryBuildFillerInfoToken(fillerType, payload);
            if (string.IsNullOrWhiteSpace(infoToken))
                continue;

            if (_fillerLog.IsUnchangedFromLastForOption(fillerType, payload, infoToken))
            {
                _logger.LogInformation("XDailyPostJob skipped unchanged filler {FillerType} {PayloadText}", fillerType, payloadText);
                continue;
            }

            var fillerSent = await _xEvents.TrySendAsync(fillerMsg);
            if (!fillerSent)
            {
                _logger.LogWarning("XDailyPostJob send failed for filler {FillerType}; leaving unlogged", fillerType);
                return;
            }
            _fillerLog.LogPost(nowUtc, fillerType, infoToken);
            _logger.LogInformation("XDailyPostJob posted filler {FillerType}", fillerType);
            return;
        }

        _logger.LogInformation("XDailyPostJob no postable event found");
    }

    private string? TryBuildFillerInfoToken(FillerType fillerType, string payloadText)
    {
        static string Norm(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim().ToLowerInvariant();

        if (fillerType == FillerType.Top3Leaderboard)
        {
            if (!EventHelpers.TryExtractLeague(payloadText, out var leagueSlug) || string.IsNullOrWhiteSpace(leagueSlug))
                return null;
            var top3 = _athletes.GetTop3SlugsForLeague(leagueSlug.Trim()).Take(3).Select(Norm).Where(s => s.Length > 0).ToList();
            if (top3.Count == 0) return null;
            return $"league[{Norm(leagueSlug)}] slugs[{string.Join(", ", top3)}]";
        }

        if (fillerType == FillerType.CrowdGuesses)
        {
            var podium = _athletes.GetCrowdLowestAgeBadgePodiumForX();
            var placeTokens = podium
                .OrderBy(x => x.Place)
                .Select(x =>
                {
                    var slugs = x.Slugs
                        .Select(Norm)
                        .Where(s => s.Length > 0)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(s => s, StringComparer.Ordinal)
                        .ToList();
                    return slugs.Count == 0 ? null : $"{x.Place}:{string.Join(",", slugs)}";
                })
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            if (placeTokens.Count == 0) return null;
            return $"podium[{string.Join(" | ", placeTokens)}]";
        }

        if (fillerType == FillerType.Newcomers)
        {
            var slugs = _athletes.GetRecentNewcomersForX().Select(Norm).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
            if (slugs.Count == 0) return null;
            return $"slugs[{string.Join(", ", slugs)}]";
        }

        if (fillerType == FillerType.DomainTop)
        {
            if (!EventHelpers.TryExtractDomain(payloadText, out var domainKey) || string.IsNullOrWhiteSpace(domainKey))
                return null;
            var winner = _athletes.GetBestDomainWinnerSlug(domainKey.Trim());
            if (string.IsNullOrWhiteSpace(winner)) return null;
            return $"domain[{Norm(domainKey)}] winner[{Norm(winner)}]";
        }

        return null;
    }

    private static TimeSpan GetCooldownForFiller(FillerType fillerType)
    {
        return fillerType switch
        {
            FillerType.CrowdGuesses => TimeSpan.FromDays(10),
            _ => TimeSpan.FromDays(7)
        };
    }
}
