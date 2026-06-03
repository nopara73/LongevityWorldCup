using LongevityWorldCup.Website.Business;
using Quartz;

namespace LongevityWorldCup.Website.Jobs;

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
    {
        var now = DateTimeOffset.UtcNow;
        _challenge.TrySelectCallSlots(now);

        foreach (var start in _challenge.GetChallengeStartCandidates(now))
        {
            try
            {
                await _email.SendChallengeStartAsync(
                    start,
                    _challenge.BuildAccessUrl(start.AccessToken),
                    _challenge.BuildStopUrl(start.StopToken),
                    context.CancellationToken).ConfigureAwait(false);
                _challenge.MarkChallengeStartSent(start.ParticipantId, now);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Longevitymaxxing challenge start email failed for participant {ParticipantId}", start.ParticipantId);
            }
        }

        foreach (var reminder in _challenge.GetDailyReminderCandidates(now))
        {
            try
            {
                await _email.SendDailyReminderAsync(
                    reminder,
                    _challenge.BuildAccessUrl(reminder.AccessToken),
                    _challenge.BuildStopUrl(reminder.StopToken),
                    context.CancellationToken).ConfigureAwait(false);
                _challenge.MarkDailyReminderSent(reminder.ParticipantId, reminder.ChallengeDay, now);
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
                    context.CancellationToken).ConfigureAwait(false);
                _challenge.MarkCallReminderSent(reminder.ParticipantId, reminder.CallKey, reminder.ReminderKind, now);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Longevitymaxxing call reminder failed for participant {ParticipantId} call {CallKey} {ReminderKind}", reminder.ParticipantId, reminder.CallKey, reminder.ReminderKind);
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
}
