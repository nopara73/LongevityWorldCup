using LongevityWorldCup.Website.Business;
using System.Globalization;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

[DisallowConcurrentExecution]
public sealed class LongevitymaxxingReminderJob(
    LongevitymaxxingChallengeService challenge,
    EventDataService events,
    ILongevitymaxxingEmailSender email,
    ILogger<LongevitymaxxingReminderJob> logger) : IJob
{
    private readonly LongevitymaxxingChallengeService _challenge = challenge;
    private readonly EventDataService _events = events;
    private readonly ILongevitymaxxingEmailSender _email = email;
    private readonly ILogger<LongevitymaxxingReminderJob> _logger = logger;

    public async Task Execute(IJobExecutionContext context)
        => await ExecuteAtAsync(DateTimeOffset.UtcNow, context.CancellationToken).ConfigureAwait(false);

    internal async Task ExecuteAtAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        _challenge.TrySelectCallSlots(now);

        foreach (var start in _challenge.GetChallengeStartCandidates(now))
        {
            try
            {
                await _email.SendChallengeStartAsync(
                    start,
                    _challenge.BuildAccessUrl(start.AccessToken),
                    _challenge.BuildStopUrl(start.StopToken),
                    cancellationToken).ConfigureAwait(false);
                _challenge.MarkChallengeStartSent(start.ParticipantId, now);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Longevitymaxxing challenge start email failed for participant {ParticipantId}", start.ParticipantId);
            }
        }

        _challenge.ApplyDailyReminderStopRules(now);

        foreach (var reminder in _challenge.GetDailyReminderCandidates(now))
        {
            try
            {
                await _email.SendDailyReminderAsync(
                    reminder,
                    _challenge.BuildAccessUrl(reminder.AccessToken),
                    _challenge.BuildStopUrl(reminder.StopToken),
                    cancellationToken).ConfigureAwait(false);
                if (reminder.IsCommitmentPaymentReminder)
                    _challenge.MarkCommitmentPaymentReminderSent(reminder.ParticipantId, reminder.ChallengeDay, now);
                else
                    _challenge.MarkDailyReminderSent(reminder.ParticipantId, reminder.ChallengeDay, now);
                if (reminder.IncludeCallScheduleUpdate)
                    _challenge.MarkCallScheduleUpdateNoticeSent(reminder.ParticipantId, now);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Longevitymaxxing daily reminder failed for participant {ParticipantId} day {ChallengeDay}", reminder.ParticipantId, reminder.ChallengeDay);
            }
        }

        foreach (var reminder in _challenge.GetCallReminderCandidates(now))
        {
            try
            {
                await _email.SendCallReminderAsync(
                    reminder,
                    _challenge.BuildAccessUrl(reminder.AccessToken),
                    _challenge.BuildStopUrl(reminder.StopToken),
                    cancellationToken).ConfigureAwait(false);
                _challenge.MarkCallReminderSent(reminder.ParticipantId, reminder.CallKey, reminder.ReminderKind, now);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Longevitymaxxing call reminder failed for participant {ParticipantId} call {CallKey} {ReminderKind}", reminder.ParticipantId, reminder.CallKey, reminder.ReminderKind);
            }
        }

        foreach (var announcement in _challenge.GetCallAnnouncementCandidates(now))
        {
            try
            {
                var eventId = _events.CreateCustomEvent(
                    BuildCallAnnouncementTitle(announcement),
                    BuildCallAnnouncementContent(announcement),
                    now.UtcDateTime,
                    visibleOnWebsite: false,
                    deliveryTargets: new CustomEventDeliveryTargets(
                        SendToWebpage: false,
                        SendToSlack: false,
                        SendToX: true,
                        SendToThreads: true,
                        SendToFacebook: true));
                _challenge.MarkCallAnnouncementQueued(announcement.CallKey, announcement.ReminderKind, eventId, now);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Longevitymaxxing call announcement failed for call {CallKey} {ReminderKind}", announcement.CallKey, announcement.ReminderKind);
            }
        }

        try
        {
            _events.UpsertLongevitymaxxingChallengeResults(_challenge.GetFinalResultEventRows(now));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Longevitymaxxing challenge result highlights failed.");
        }
    }

    internal static string BuildCallAnnouncementTitle(LongevitymaxxingCallAnnouncementCandidate announcement)
    {
        var label = string.IsNullOrWhiteSpace(announcement.CallLabel)
            ? "community call"
            : announcement.CallLabel.Trim().ToLowerInvariant();
        if (!DateTimeOffset.TryParse(announcement.StartsAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var startsAt))
            return $"Longevitymaxxing {label} starts soon";

        return $"Longevitymaxxing {label} starts at {startsAt.ToUniversalTime():HH:mm} UTC";
    }

    internal static string BuildCallAnnouncementContent(LongevitymaxxingCallAnnouncementCandidate announcement)
        => $"Participation is open. Join here:\n{announcement.VideoCallUrl.Trim()}";
}
