using LongevityWorldCup.Website.Business;
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

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        => await ExecuteAtAsync(DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);

    internal async Task ExecuteAtAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
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
                _challenge.MarkDailyReminderSent(reminder, now);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Longevitymaxxing daily reminder failed for participant {ParticipantId} day {ChallengeDay}", reminder.ParticipantId, reminder.ChallengeDay);
            }
        }

        try
        {
            _events.UpsertLongevitymaxxingChallengeResults(_challenge.GetFinalResultEventRows(now));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Longevitymaxxing challenge result highlights failed.");
        }
    }
}
