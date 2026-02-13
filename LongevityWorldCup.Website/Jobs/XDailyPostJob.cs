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
    private readonly XImageService _images;
    private readonly XApiClient _xApiClient;

    public XDailyPostJob(ILogger<XDailyPostJob> logger, EventDataService events, XEventService xEvents, AthleteDataService athletes, XFillerPostLogService fillerLog, XImageService images, XApiClient xApiClient)
    {
        _logger = logger;
        _events = events;
        _xEvents = xEvents;
        _athletes = athletes;
        _fillerLog = fillerLog;
        _images = images;
        _xApiClient = xApiClient;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("XDailyPostJob {ts}", DateTime.UtcNow);

        _events.SetAthletesForX(_athletes.GetAthletesForX());
        if (await XDailyPostJobTempTestHelper.TryPostTemporaryDomainTopTestAsync(_events, _athletes, _xEvents, _images, _xApiClient, _logger))
            return;

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
                }
            }

            if (xPriority <= EventDataService.XPriorityPrimaryMax && occurredAtUtc < freshCutoff)
                continue;

            var msg = _xEvents.TryBuildMessage(type, text);
            if (string.IsNullOrWhiteSpace(msg)) continue;

            var mediaIds = await XDailyPostMediaHelper.TryBuildMediaIdsAsync(type, text, _images, _xApiClient);
            await _xEvents.SendAsync(msg, mediaIds);
            _events.MarkEventsXProcessed(new[] { id });
            _logger.LogInformation("XDailyPostJob posted event {Id}", id);
            return;
        }

        var fillerCandidates = _fillerLog.GetSuggestedFillersOrdered();
        foreach (var (fillerType, payloadText) in fillerCandidates)
        {
            var payload = payloadText ?? "";
            var cooldown = GetCooldownForFiller(fillerType);
            if (_fillerLog.IsOnCooldownForOption(fillerType, payload, cooldown, DateTime.UtcNow))
            {
                _logger.LogInformation("XDailyPostJob skipped filler in cooldown {FillerType} {PayloadText} ({CooldownDays}d)", fillerType, payloadText, cooldown.TotalDays);
                continue;
            }

            if (fillerType == FillerType.PvpBiomarkerDuel)
            {
                var (ok, pvpInfoToken) = await _xEvents.TrySendPvpDuelThreadWithInfoTokenAsync(
                    null,
                    token => _fillerLog.IsUnchangedFromLastForOption(fillerType, payload, token));
                if (ok && !string.IsNullOrWhiteSpace(pvpInfoToken))
                {
                    _fillerLog.LogPost(DateTime.UtcNow, fillerType, pvpInfoToken);
                    _logger.LogInformation("XDailyPostJob posted filler {FillerType}", fillerType);
                    return;
                }

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

            IReadOnlyList<string>? mediaIds = null;
            if (fillerType == FillerType.Newcomers)
            {
                await using var imageStream = await _images.BuildNewcomersImageAsync();
                if (imageStream != null)
                {
                    var mediaId = await _xApiClient.UploadMediaAsync(imageStream, "image/png");
                    if (!string.IsNullOrWhiteSpace(mediaId))
                        mediaIds = new[] { mediaId };
                }
            }
            else if (fillerType == FillerType.Top3Leaderboard)
            {
                if (EventHelpers.TryExtractLeague(payload, out var leagueSlug) && !string.IsNullOrWhiteSpace(leagueSlug))
                {
                    var top3 = _athletes.GetTop3SlugsForLeague(leagueSlug).Take(3).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    if (top3.Count > 0)
                    {
                        await using var imageStream = await _images.BuildTop3LeaderboardPodiumImageAsync(top3);
                        if (imageStream != null)
                        {
                            var mediaId = await _xApiClient.UploadMediaAsync(imageStream, "image/png");
                            if (!string.IsNullOrWhiteSpace(mediaId))
                                mediaIds = new[] { mediaId };
                        }
                    }
                }
            }
            else if (fillerType == FillerType.DomainTop)
            {
                if (EventHelpers.TryExtractDomain(payload, out var domainKey) && !string.IsNullOrWhiteSpace(domainKey))
                {
                    var winnerSlug = _athletes.GetBestDomainWinnerSlug(domainKey.Trim());
                    if (!string.IsNullOrWhiteSpace(winnerSlug))
                    {
                        await using var imageStream = await _images.BuildSingleAthleteImageAsync(winnerSlug);
                        if (imageStream != null)
                        {
                            var mediaId = await _xApiClient.UploadMediaAsync(imageStream, "image/png");
                            if (!string.IsNullOrWhiteSpace(mediaId))
                                mediaIds = new[] { mediaId };
                        }
                    }
                }
            }

            await _xEvents.SendAsync(fillerMsg, mediaIds);
            _fillerLog.LogPost(DateTime.UtcNow, fillerType, infoToken);
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
            var top3 = _athletes.GetCrowdLowestAgeTop3().Take(3).Select(x => Norm(x.Slug)).Where(s => s.Length > 0).ToList();
            if (top3.Count == 0) return null;
            return $"slugs[{string.Join(", ", top3)}]";
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
