using System.Globalization;
using System.Text.Json;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class MastodonAnnouncementService(
    EventDataService events,
    SocialDeliveryStore deliveries,
    MastodonApiClient api,
    ThreadsEventService messageComposer,
    AthleteDataService athletes,
    CustomEventImageService images,
    AthleteCountMilestoneMemeService memes,
    TimeProvider time,
    ILogger<MastodonAnnouncementService> log)
{
    // Both scheduled jobs share this singleton, including preparation and receipt persistence.
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private static readonly TimeSpan SafeRetryWindow = TimeSpan.FromMinutes(45);

    public async Task DispatchAsync(bool customOnly, CancellationToken ct = default)
    {
        _ = events; // Schema initialization and historical baselining finish before dispatch.
        if (!api.IsConfigured || !await _dispatchGate.WaitAsync(0, ct)) return;
        try
        {
            var now = time.GetUtcNow();
            var snapshot = athletes.GetAthletesForX();
            messageComposer.SetAthletesForThreads(snapshot);
            var names = snapshot.ToDictionary(x => x.Slug, x => x.Name, StringComparer.OrdinalIgnoreCase);
            string ResolveName(string slug) => names.TryGetValue(slug, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('_', ' ').Replace('-', ' '));

            foreach (var pending in deliveries.GetPending(customOnly, now))
            {
                ct.ThrowIfCancellationRequested();
                var item = pending.Event;
                if (pending.FirstAttemptAtUtc is { } firstAttempt && now - firstAttempt >= SafeRetryWindow)
                {
                    // The server forgets idempotency keys after an hour. Keep an uncertain outcome
                    // for operator review rather than risk publishing it again after that window.
                    deliveries.RequireReview(item.Id, now);
                    log.LogWarning("Mastodon event {EventId} needs review because its posting outcome is unconfirmed", item.Id);
                    continue;
                }
                if (pending.HiddenForMissingAthlete)
                {
                    deliveries.Skip(item.Id, "MissingAthlete", now);
                    continue;
                }
                string? subject = item.Type is EventType.NewRank or EventType.BadgeAward or EventType.CrowdAgeTop10Change or EventType.AgeImprovementTop10Change
                    && EventHelpers.TryExtractSlug(item.Text, out var slug) ? slug.Trim() : null;
                if (pending.RecordJson is null)
                {
                    if (SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(item.Type, item.Text, item.OccurredAtUtc, pending.Priority,
                        now.UtcDateTime.AddDays(-7), athletes.HasSingleGlobalPlaceOneBadgeHolder, out var reason))
                    {
                        deliveries.Skip(item.Id, reason.ToString(), now);
                        continue;
                    }
                    if (subject is not null && deliveries.IsSubjectOnCooldown(subject, now)) continue;
                }

                var postStarted = false;
                try
                {
                    await api.VerifyAccountAsync(ct);
                    var key = pending.RecordKey;
                    var json = pending.RecordJson;
                    if (json is null)
                    {
                        var request = await BuildRequestAsync(item, ResolveName, ct);
                        if (request is null)
                        {
                            deliveries.Skip(item.Id, SocialEventSkipReason.EmptyMessage.ToString(), now);
                            continue;
                        }
                        (key, json) = deliveries.Prepare(item.Id, JsonSerializer.Serialize(request), subject, time.GetUtcNow());
                    }
                    var prepared = JsonSerializer.Deserialize<MastodonPostRequest>(json)
                        ?? throw new InvalidOperationException("The saved Mastodon request is invalid.");
                    deliveries.BeginAttempt(item.Id, time.GetUtcNow());
                    postStarted = true;
                    var receipt = await api.PublishAsync(key!, prepared, ct);
                    deliveries.Complete(item.Id, receipt, time.GetUtcNow());
                    log.LogInformation("Mastodon delivered event {EventId}: {PostUrl}", item.Id, receipt.Url);
                    return;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    var error = ex as MastodonApiException;
                    var code = error?.Code ?? ex.GetType().Name;
                    var delay = TimeSpan.FromMinutes(Math.Min(10, Math.Pow(2, Math.Min(pending.Attempts + 1, 4))));
                    if (error?.RetryAfter is { } retry && retry > delay) delay = retry;
                    deliveries.Fail(item.Id, code, delay,
                        clearFirstAttempt: pending.FirstAttemptAtUtc is null && (error is not null ? !error.OutcomeUnknown : !postStarted), time.GetUtcNow());
                    log.LogWarning("Mastodon event {EventId} remains pending after {ErrorCode}; retry {Attempt}", item.Id, code, pending.Attempts + 1);
                    return;
                }
            }
        }
        finally { _dispatchGate.Release(); }
    }

    private async Task<MastodonPostRequest?> BuildRequestAsync(EventItem item, Func<string, string> resolveName, CancellationToken ct)
    {
        string text;
        byte[]? media = null;
        var contentType = "image/png";
        string? alt = null;
        if (item.Type == EventType.CustomEvent)
        {
            var plan = CustomEventSocialComposer.BuildPlan(item.Id, item.Text, MastodonPost.MaxCharacters, resolveName,
                includeEventUrl: item.VisibleOnWebsite, textLength: MastodonPost.Count, truncate: MastodonPost.Truncate);
            text = plan.PostText;
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (plan.Mode == CustomEventPostMode.Image)
            {
                using var stream = await images.RenderToStreamAsync(item.Text, resolveName, ct)
                    ?? throw new InvalidOperationException("Custom event image rendering is unavailable.");
                media = stream.ToArray();
                alt = plan.TitleText + "\n\n" + plan.BodyText;
            }
        }
        else
        {
            text = messageComposer.TryBuildMessage(item.Type, item.Text, item.Id, item.VisibleOnWebsite) ?? "";
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (MastodonPost.Count(text) > MastodonPost.MaxCharacters)
            {
                using var stream = await images.RenderToStreamAsync(text, resolveName, ct)
                    ?? throw new InvalidOperationException("Announcement image rendering is unavailable.");
                media = stream.ToArray();
                alt = text;
                text = $"Longevity World Cup update\n\n{SocialPostLinks.LeaderboardUrl}";
            }
            if (media is null && item.Type == EventType.AthleteCountMilestone && EventHelpers.TryExtractAthleteCount(item.Text, out var count)
                && memes.TryGetMeme(count, out var meme))
            {
                media = await File.ReadAllBytesAsync(meme.FullPath, ct);
                contentType = meme.ContentType;
                alt = text;
            }
        }
        var mediaId = media is null ? null : await api.UploadImageAsync(media, contentType, alt ?? "Longevity World Cup announcement", ct);
        return new(text, mediaId);
    }
}
