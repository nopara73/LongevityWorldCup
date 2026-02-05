using LongevityWorldCup.Website.Business;
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

            await _xEvents.SendAsync(msg);
            _events.MarkEventsXProcessed(new[] { id });
            _logger.LogInformation("XDailyPostJob posted event {Id}", id);
            return;
        }

        var (fillerType, payloadText) = _fillerLog.GetSuggestedNextFiller();
        var fillerMsg = _xEvents.TryBuildFillerMessage(fillerType, payloadText);
        if (!string.IsNullOrWhiteSpace(fillerMsg))
        {
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

            await _xEvents.SendAsync(fillerMsg, mediaIds);
            _fillerLog.LogPost(DateTime.UtcNow, fillerType, payloadText ?? "");
            _logger.LogInformation("XDailyPostJob posted filler {FillerType}", fillerType);
            return;
        }

        _logger.LogInformation("XDailyPostJob no postable event found");
    }
}
