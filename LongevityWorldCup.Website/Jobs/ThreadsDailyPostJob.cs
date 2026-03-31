using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.Logging;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public class ThreadsDailyPostJob : IJob
{
    private static readonly TimeSpan SubjectCooldown = TimeSpan.FromDays(2);
    private readonly ILogger<ThreadsDailyPostJob> _logger;
    private readonly EventDataService _events;
    private readonly ThreadsEventService _threadsEvents;
    private readonly AthleteDataService _athletes;
    private readonly ThreadsFillerPostLogService _fillerLog;

    public ThreadsDailyPostJob(ILogger<ThreadsDailyPostJob> logger, EventDataService events, ThreadsEventService threadsEvents, AthleteDataService athletes, ThreadsFillerPostLogService fillerLog)
    {
        _logger = logger;
        _events = events;
        _threadsEvents = threadsEvents;
        _athletes = athletes;
        _fillerLog = fillerLog;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("ThreadsDailyPostJob {ts}", DateTime.UtcNow);

        _events.SetAthletesForX(_athletes.GetAthletesForX());
        var pending = _events.GetPendingThreadsEvents();
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

                    var isSingleWinnerBadge =
                        string.Equals(norm, "PhenoAge - Lowest", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(norm, "PhenoAge Best Improvement", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(norm, "Bortz Age - Lowest", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(norm, "Bortz Age Best Improvement", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(norm, "Chronological Age - Oldest", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(norm, "Chronological Age - Youngest", StringComparison.OrdinalIgnoreCase);
                    if (isSingleWinnerBadge &&
                        (!EventHelpers.TryExtractPlace(text, out var badgePlace) || badgePlace != 1))
                        continue;

                    var isBestImprovement =
                        string.Equals(norm, "PhenoAge Best Improvement", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(norm, "Bortz Age Best Improvement", StringComparison.OrdinalIgnoreCase);
                    if (isBestImprovement && !_athletes.HasSingleGlobalPlaceOneBadgeHolder(label))
                    {
                        _logger.LogInformation("ThreadsDailyPostJob skipped tie Best Improvement badge event {BadgeLabel}", label);
                        continue;
                    }
                }
            }

            if (xPriority <= EventDataService.XPriorityPrimaryMax && occurredAtUtc < freshCutoff)
                continue;

            var nowUtc = DateTime.UtcNow;
            var subjectSlug = TryGetSubjectSlugForEvent(type, text);
            if (!string.IsNullOrWhiteSpace(subjectSlug) && _fillerLog.IsSubjectOnCooldown(subjectSlug, SubjectCooldown, nowUtc))
            {
                _logger.LogInformation("ThreadsDailyPostJob skipped event {Id} because subject {SubjectSlug} is on cooldown ({CooldownDays}d)", id, subjectSlug, SubjectCooldown.TotalDays);
                continue;
            }

            var msg = _threadsEvents.TryBuildMessage(type, text);
            if (string.IsNullOrWhiteSpace(msg)) continue;

            var sent = await _threadsEvents.TrySendAsync(msg);
            if (!sent)
            {
                _logger.LogWarning("ThreadsDailyPostJob send failed for event {Id}; leaving unprocessed", id);
                return;
            }
            _events.MarkEventsThreadsProcessed(new[] { id });
            _fillerLog.LogSubjectPost(nowUtc, $"event[{id}] type[{type}]", subjectSlug);
            _logger.LogInformation("ThreadsDailyPostJob posted event {Id}", id);
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
                _logger.LogInformation("ThreadsDailyPostJob skipped filler in cooldown {FillerType} {PayloadText} ({CooldownDays}d)", fillerType, payloadText, cooldown.TotalDays);
                continue;
            }

            var fillerMsg = _threadsEvents.TryBuildFillerMessage(fillerType, payload);
            if (string.IsNullOrWhiteSpace(fillerMsg))
                continue;

            var infoToken = TryBuildFillerInfoToken(fillerType, payload);
            if (string.IsNullOrWhiteSpace(infoToken))
                continue;

            var subjectSlug = TryGetSubjectSlugForFiller(fillerType, payload);
            if (!string.IsNullOrWhiteSpace(subjectSlug) && _fillerLog.IsSubjectOnCooldown(subjectSlug, SubjectCooldown, nowUtc))
            {
                _logger.LogInformation("ThreadsDailyPostJob skipped filler {FillerType} {PayloadText} because subject {SubjectSlug} is on cooldown ({CooldownDays}d)", fillerType, payloadText, subjectSlug, SubjectCooldown.TotalDays);
                continue;
            }

            if (_fillerLog.IsUnchangedFromLastForOption(fillerType, payload, infoToken))
            {
                _logger.LogInformation("ThreadsDailyPostJob skipped unchanged filler {FillerType} {PayloadText}", fillerType, payloadText);
                continue;
            }

            var fillerSent = await _threadsEvents.TrySendAsync(fillerMsg);
            if (!fillerSent)
            {
                _logger.LogWarning("ThreadsDailyPostJob send failed for filler {FillerType}; leaving unlogged", fillerType);
                return;
            }
            _fillerLog.LogPost(nowUtc, fillerType, infoToken, subjectSlug);
            _logger.LogInformation("ThreadsDailyPostJob posted filler {FillerType}", fillerType);
            return;
        }

        _logger.LogInformation("ThreadsDailyPostJob no postable event found");
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

    private static string? TryGetSubjectSlugForEvent(EventType type, string rawText)
    {
        if (type == EventType.NewRank || type == EventType.BadgeAward)
        {
            if (EventHelpers.TryExtractSlug(rawText, out var slug) && !string.IsNullOrWhiteSpace(slug))
                return slug.Trim();
        }

        return null;
    }

    private string? TryGetSubjectSlugForFiller(FillerType fillerType, string payloadText)
    {
        if (fillerType == FillerType.DomainTop)
        {
            if (!EventHelpers.TryExtractDomain(payloadText, out var domainKey) || string.IsNullOrWhiteSpace(domainKey))
                return null;

            var winner = _athletes.GetBestDomainWinnerSlug(domainKey.Trim());
            return string.IsNullOrWhiteSpace(winner) ? null : winner.Trim();
        }

        return null;
    }
}
