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
    private readonly AthleteCountMilestoneMemeService _milestoneMemes;

    public ThreadsDailyPostJob(
        ILogger<ThreadsDailyPostJob> logger,
        EventDataService events,
        ThreadsEventService threadsEvents,
        AthleteDataService athletes,
        ThreadsFillerPostLogService fillerLog,
        AthleteCountMilestoneMemeService milestoneMemes)
    {
        _logger = logger;
        _events = events;
        _threadsEvents = threadsEvents;
        _athletes = athletes;
        _fillerLog = fillerLog;
        _milestoneMemes = milestoneMemes;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("ThreadsDailyPostJob {ts}", DateTime.UtcNow);
        await _threadsEvents.EnsureAccessTokenFreshAsync(context.CancellationToken);

        _events.SetAthletesForX(_athletes.GetAthletesForX());
        var pending = _events.GetPendingThreadsEvents();
        var freshCutoff = DateTime.UtcNow.AddDays(-7);

        foreach (var (id, type, text, occurredAtUtc, _, visibleOnWebsite, xPriority) in pending)
        {
            if (SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(
                    type,
                    text,
                    occurredAtUtc,
                    xPriority,
                    freshCutoff,
                    _athletes.HasSingleGlobalPlaceOneBadgeHolder,
                    out var skipReason))
            {
                _events.MarkEventsThreadsSkipped(new[] { (id, skipReason) });
                _logger.LogInformation("ThreadsDailyPostJob marked event {Id} processed with skip reason {SkipReason}", id, skipReason);
                continue;
            }

            var nowUtc = DateTime.UtcNow;
            var subjectSlug = TryGetSubjectSlugForEvent(type, text);
            if (!string.IsNullOrWhiteSpace(subjectSlug) && _fillerLog.IsSubjectOnCooldown(subjectSlug, SubjectCooldown, nowUtc))
            {
                _logger.LogInformation("ThreadsDailyPostJob skipped event {Id} because subject {SubjectSlug} is on cooldown ({CooldownDays}d)", id, subjectSlug, SubjectCooldown.TotalDays);
                continue;
            }

            var msg = _threadsEvents.TryBuildMessage(type, text, id, visibleOnWebsite);
            if (string.IsNullOrWhiteSpace(msg))
            {
                _events.MarkEventsThreadsSkipped(new[] { (id, SocialEventSkipReason.EmptyMessage) });
                _logger.LogInformation("ThreadsDailyPostJob marked event {Id} processed with skip reason {SkipReason}", id, SocialEventSkipReason.EmptyMessage);
                continue;
            }

            _logger.LogInformation(
                "ThreadsDailyPostJob selected event {Id} type {Type} occurredAt {OccurredAtUtc} visibleOnWebsite {VisibleOnWebsite} messageLength {MessageLength}",
                id,
                type,
                occurredAtUtc,
                visibleOnWebsite,
                msg.Length);

            var sent = TryGetMilestoneMemeImageUrl(type, text, out var memeImageUrl)
                ? await _threadsEvents.TrySendImageAsync(msg, memeImageUrl)
                : await _threadsEvents.TrySendEventAsync(type, text, id, visibleOnWebsite);
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

        if (await TryPostHistoryReminderAsync())
            return;

        if (await TryPostPeriodicReminderAsync(FillerType.Ruleset))
            return;

        if (await TryPostPeriodicReminderAsync(FillerType.GitHubRepository))
            return;

        if (await TryPostPeriodicReminderAsync(FillerType.Donation))
            return;

        var fillerCandidates = _fillerLog.GetSuggestedFillersOrdered();
        foreach (var (fillerType, payloadText) in fillerCandidates)
        {
            if (IsPeriodicReminder(fillerType))
                continue;

            var payload = payloadText ?? "";
            var cooldown = GetCooldownForFiller(fillerType);
            var nowUtc = DateTime.UtcNow;
            var onCooldown = IsFillerOnCooldown(fillerType, cooldown, nowUtc);
            if (onCooldown)
            {
                _logger.LogInformation("ThreadsDailyPostJob skipped filler in cooldown {FillerType} {PayloadText} ({Cooldown})", fillerType, payloadText, FormatCooldown(fillerType, cooldown));
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

            if (ShouldCheckUnchangedToken(fillerType) && _fillerLog.IsUnchangedFromLastForOption(fillerType, payload, infoToken))
            {
                _logger.LogInformation("ThreadsDailyPostJob skipped unchanged filler {FillerType} {PayloadText}", fillerType, payloadText);
                continue;
            }

            _logger.LogInformation(
                "ThreadsDailyPostJob selected filler {FillerType} subject {SubjectSlug} messageLength {MessageLength} infoToken {InfoToken}",
                fillerType,
                subjectSlug,
                fillerMsg.Length,
                infoToken);

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

    private bool TryGetMilestoneMemeImageUrl(EventType type, string rawText, out string imageUrl)
    {
        imageUrl = "";
        if (type != EventType.AthleteCountMilestone)
            return false;
        if (!EventHelpers.TryExtractAthleteCount(rawText, out var count))
            return false;
        if (!_milestoneMemes.TryGetMeme(count, out var meme))
            return false;

        imageUrl = meme.PublicUrl;
        return true;
    }

    private async Task<bool> TryPostHistoryReminderAsync()
    {
        return await TryPostPeriodicReminderAsync(FillerType.HistoryDocument);
    }

    private async Task<bool> TryPostPeriodicReminderAsync(FillerType fillerType)
    {
        var nowUtc = DateTime.UtcNow;
        var cooldown = GetCooldownForFiller(fillerType);
        if (IsFillerOnCooldown(fillerType, cooldown, nowUtc))
        {
            _logger.LogInformation(
                "ThreadsDailyPostJob skipped filler in cooldown {FillerType} ({Cooldown})",
                fillerType,
                FormatCooldown(fillerType, cooldown));
            return false;
        }

        var fillerMsg = _threadsEvents.TryBuildFillerMessage(fillerType, "");
        if (string.IsNullOrWhiteSpace(fillerMsg))
            return false;

        var infoToken = TryBuildFillerInfoToken(fillerType, "");
        if (string.IsNullOrWhiteSpace(infoToken))
            return false;

        _logger.LogInformation(
            "ThreadsDailyPostJob selected filler {FillerType} messageLength {MessageLength} infoToken {InfoToken}",
            fillerType,
            fillerMsg.Length,
            infoToken);

        var fillerSent = await _threadsEvents.TrySendAsync(fillerMsg);
        if (!fillerSent)
        {
            _logger.LogWarning("ThreadsDailyPostJob send failed for filler {FillerType}; leaving unlogged", fillerType);
            return true;
        }

        _fillerLog.LogPost(nowUtc, fillerType, infoToken);
        _logger.LogInformation("ThreadsDailyPostJob posted filler {FillerType}", fillerType);
        return true;
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

        if (fillerType == FillerType.HistoryDocument)
            return HistoryDocumentReminderPost.InfoToken;

        if (fillerType == FillerType.Ruleset)
            return RulesetReminderPost.InfoToken;

        if (fillerType == FillerType.GitHubRepository)
            return GitHubRepositoryReminderPost.InfoToken;

        if (fillerType == FillerType.Donation)
            return DonationReminderPost.InfoToken;

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
            FillerType.HistoryDocument => TimeSpan.FromDays(HistoryDocumentReminderPost.MinCooldownDays),
            FillerType.Ruleset => TimeSpan.FromDays(RulesetReminderPost.MinCooldownDays),
            FillerType.GitHubRepository => TimeSpan.FromDays(GitHubRepositoryReminderPost.MinCooldownDays),
            FillerType.Donation => TimeSpan.FromDays(DonationReminderPost.MinCooldownDays),
            _ => TimeSpan.FromDays(7)
        };
    }

    private bool IsFillerOnCooldown(FillerType fillerType, TimeSpan cooldown, DateTime nowUtc)
    {
        if (TryGetRandomizedCooldownDays(fillerType, out var minDays, out var maxDays))
        {
            return _fillerLog.IsOnRandomizedCooldownForType(
                fillerType,
                minDays,
                maxDays,
                nowUtc);
        }

        return _fillerLog.IsOnCooldownForType(fillerType, cooldown, nowUtc);
    }

    private static bool ShouldCheckUnchangedToken(FillerType fillerType)
    {
        return !IsPeriodicReminder(fillerType);
    }

    private static string FormatCooldown(FillerType fillerType, TimeSpan cooldown)
    {
        return TryGetRandomizedCooldownDays(fillerType, out var minDays, out var maxDays)
            ? $"{minDays}-{maxDays}d randomized"
            : $"{cooldown.TotalDays}d";
    }

    private static bool IsPeriodicReminder(FillerType fillerType)
    {
        return fillerType is FillerType.HistoryDocument or FillerType.Ruleset or FillerType.GitHubRepository or FillerType.Donation;
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
