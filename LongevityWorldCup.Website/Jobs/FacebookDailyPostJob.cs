using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
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
        var skippedEvents = new List<(string Id, SocialEventSkipReason Reason)>();

        foreach (var (id, type, text, occurredAtUtc, _, visibleOnWebsite, _) in pending)
        {
            if (SocialEventSkipPolicy.TryGetFacebookTerminalSkipReason(type, out var skipReason))
            {
                skippedEvents.Add((id, skipReason));
                continue;
            }

            var msg = _facebookEvents.TryBuildMessage(type, text, id, visibleOnWebsite);
            if (string.IsNullOrWhiteSpace(msg))
            {
                skippedEvents.Add((id, SocialEventSkipReason.EmptyMessage));
                continue;
            }

            FlushSkippedEvents(skippedEvents);

            _logger.LogInformation(
                "FacebookDailyPostJob selected event {Id} type {Type} occurredAt {OccurredAtUtc} visibleOnWebsite {VisibleOnWebsite} messageLength {MessageLength}",
                id,
                type,
                occurredAtUtc,
                visibleOnWebsite,
                msg.Length);

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

        FlushSkippedEvents(skippedEvents);

        var fillerCandidates = _fillerLog.GetSuggestedFillersOrdered();
        foreach (var (fillerType, payloadText) in fillerCandidates)
        {
            if (!TryGetPeriodicInfoToken(fillerType, out var infoToken) ||
                !TryGetRandomizedCooldownDays(fillerType, out var minCooldownDays, out var maxCooldownDays))
                continue;

            var payload = payloadText ?? "";
            var nowUtc = DateTime.UtcNow;
            if (_fillerLog.IsOnRandomizedCooldownForType(
                    fillerType,
                    minCooldownDays,
                    maxCooldownDays,
                    nowUtc))
            {
                _logger.LogInformation(
                    "FacebookDailyPostJob skipped filler in cooldown {FillerType} {PayloadText} ({MinCooldownDays}-{MaxCooldownDays}d randomized)",
                    fillerType,
                    payloadText,
                    minCooldownDays,
                    maxCooldownDays);
                continue;
            }

            var fillerMsg = _facebookEvents.TryBuildFillerMessage(fillerType, payload);
            if (string.IsNullOrWhiteSpace(fillerMsg))
                continue;

            _logger.LogInformation(
                "FacebookDailyPostJob selected filler {FillerType} messageLength {MessageLength} infoToken {InfoToken}",
                fillerType,
                fillerMsg.Length,
                infoToken);

            var fillerSent = await _facebookEvents.TrySendAsync(fillerMsg);
            if (!fillerSent)
            {
                _logger.LogWarning("FacebookDailyPostJob send failed for filler {FillerType}; leaving unlogged", fillerType);
                return;
            }

            _fillerLog.LogPost(nowUtc, fillerType, infoToken);
            _logger.LogInformation("FacebookDailyPostJob posted filler {FillerType}", fillerType);
            return;
        }

        _logger.LogInformation("FacebookDailyPostJob no postable event found");
    }

    private void FlushSkippedEvents(List<(string Id, SocialEventSkipReason Reason)> skippedEvents)
    {
        if (skippedEvents.Count == 0)
            return;

        _events.MarkEventsFacebookSkipped(skippedEvents);
        _logger.LogInformation(
            "FacebookDailyPostJob marked {Count} event(s) processed with skip reasons: {SkipReasonSummary}",
            skippedEvents.Count,
            FormatSkipReasonSummary(skippedEvents));
        skippedEvents.Clear();
    }

    private static string FormatSkipReasonSummary(IEnumerable<(string Id, SocialEventSkipReason Reason)> skippedEvents)
    {
        return string.Join(", ", skippedEvents
            .GroupBy(x => x.Reason)
            .OrderBy(x => x.Key.ToString(), StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Count()}"));
    }

    private static bool TryGetPeriodicInfoToken(FillerType fillerType, out string infoToken)
    {
        if (fillerType == FillerType.HistoryDocument)
        {
            infoToken = HistoryDocumentReminderPost.InfoToken;
            return true;
        }

        if (fillerType == FillerType.Ruleset)
        {
            infoToken = RulesetReminderPost.InfoToken;
            return true;
        }

        if (fillerType == FillerType.GitHubRepository)
        {
            infoToken = GitHubRepositoryReminderPost.InfoToken;
            return true;
        }

        if (fillerType == FillerType.Donation)
        {
            infoToken = DonationReminderPost.InfoToken;
            return true;
        }

        infoToken = "";
        return false;
    }

    private static bool TryGetRandomizedCooldownDays(FillerType fillerType, out int minDays, out int maxDays)
    {
        if (fillerType == FillerType.HistoryDocument)
        {
            minDays = HistoryDocumentReminderPost.MinCooldownDays;
            maxDays = HistoryDocumentReminderPost.MaxCooldownDays;
            return true;
        }

        if (fillerType == FillerType.Ruleset)
        {
            minDays = RulesetReminderPost.MinCooldownDays;
            maxDays = RulesetReminderPost.MaxCooldownDays;
            return true;
        }

        if (fillerType == FillerType.GitHubRepository)
        {
            minDays = GitHubRepositoryReminderPost.MinCooldownDays;
            maxDays = GitHubRepositoryReminderPost.MaxCooldownDays;
            return true;
        }

        if (fillerType == FillerType.Donation)
        {
            minDays = DonationReminderPost.MinCooldownDays;
            maxDays = DonationReminderPost.MaxCooldownDays;
            return true;
        }

        minDays = 0;
        maxDays = 0;
        return false;
    }
}
