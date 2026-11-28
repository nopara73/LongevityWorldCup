using System.Globalization;
using System.Text.Json;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class InstagramAnnouncementService(
    EventDataService events,
    SocialDeliveryStore deliveries,
    InstagramApiClient api,
    InstagramPublisher publisher,
    AthleteDataService athletes,
    InstagramImageService images,
    AthleteCountMilestoneMemeService milestoneMemes,
    TimeProvider time,
    ILogger<InstagramAnnouncementService> log)
{
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);

    public async Task DispatchAsync(bool customOnly, CancellationToken ct = default)
    {
        _ = events; // Finish schema initialization and historical baselining first.
        if (!api.IsConfigured || !await _dispatchGate.WaitAsync(0, ct)) return;
        try
        {
            await api.EnsureAccessTokenFreshAsync(ct);
            var now = time.GetUtcNow();
            var snapshot = athletes.GetAthletesForX();
            var names = snapshot.ToDictionary(x => x.Slug, x => x.Name, StringComparer.OrdinalIgnoreCase);
            string ResolveName(string slug) => names.TryGetValue(slug, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('_', ' ').Replace('-', ' '));
            var mentions = snapshot.ToDictionary(x => x.Slug, x => x.MentionFor(SocialPlatform.Instagram), StringComparer.OrdinalIgnoreCase);
            string ResolveMention(string slug) => mentions.GetValueOrDefault(slug) ?? ResolveName(slug);

            foreach (var pending in athletes.ReadPublishedEvents(() => deliveries.GetPending(SocialDeliveryStore.Instagram, customOnly, now)))
            {
                ct.ThrowIfCancellationRequested();
                var item = pending.Event;
                // Recheck even prepared containers until media_publish starts. Once attempted,
                // keep the original request available for receipt recovery.
                if (pending.FirstAttemptAtUtc is null)
                {
                    var skip = pending.HiddenForMissingAthlete ? "MissingAthlete" :
                        SocialEventSkipPolicy.InstagramSkipReason(item, pending.Headline, now)?.ToString();
                    if (skip is not null)
                    {
                        deliveries.SkipUnattempted(SocialDeliveryStore.Instagram, item.Id, skip, now);
                        continue;
                    }
                }
                var subject = pending.Headline.Subject;
                if (pending.RecordJson is null && subject is not null &&
                    deliveries.IsSubjectOnCooldown(SocialDeliveryStore.Instagram, subject, now)) continue;

                try
                {
                    var json = pending.RecordJson;
                    if (json is null)
                    {
                        var request = await BuildRequestAsync(item, ResolveName, ResolveMention, ct);
                        if (request is null)
                        {
                            deliveries.Skip(SocialDeliveryStore.Instagram, item.Id, SocialEventSkipReason.EmptyMessage.ToString(), now);
                            continue;
                        }
                        (_, json) = deliveries.Prepare(SocialDeliveryStore.Instagram, item.Id, JsonSerializer.Serialize(request), subject, time.GetUtcNow());
                    }
                    var prepared = JsonSerializer.Deserialize<InstagramPostRequest>(json)
                        ?? throw new InvalidOperationException("The saved Instagram request is invalid.");
                    // An attempted publish can only recover its receipt or enter review.
                    // Keep that recovery path available even after ownership changes.
                    if (pending.FirstAttemptAtUtc is null && deliveries.SuppressIfSuperseded(
                            SocialDeliveryStore.Instagram, item.Id, athletes.SupersededAnnouncementReason(item.Type, item.Text), time.GetUtcNow())) continue;
                    var receipt = await publisher.PublishAsync(pending, prepared, ct);
                    deliveries.Complete(SocialDeliveryStore.Instagram, item.Id, receipt, time.GetUtcNow());
                    log.LogInformation("Instagram delivered event {EventId}: {PostUrl}", item.Id, receipt.Url);
                    return;
                }
                catch (InstagramApiException ex) when (ex.Code == "DeliverySuppressed")
                {
                    continue;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    var error = ex as InstagramApiException;
                    var code = error?.Code ?? ex.GetType().Name;
                    if (error?.OutcomeUnknown == true)
                    {
                        deliveries.RequireReview(SocialDeliveryStore.Instagram, item.Id, time.GetUtcNow());
                        log.LogWarning("Instagram event {EventId} needs review after {ErrorCode}", item.Id, code);
                    }
                    else
                    {
                        var delay = code == "ContainerProcessing" ? TimeSpan.FromMinutes(1)
                            : TimeSpan.FromMinutes(Math.Min(10, Math.Pow(2, Math.Min(pending.Attempts + 1, 4))));
                        deliveries.Fail(SocialDeliveryStore.Instagram, item.Id, code, delay,
                            clearFirstAttempt: pending.FirstAttemptAtUtc is null && error is not null, time.GetUtcNow());
                        log.LogWarning("Instagram event {EventId} remains pending after {ErrorCode}; retry {Attempt}", item.Id, code, pending.Attempts + 1);
                    }
                    return;
                }
            }
        }
        finally { _dispatchGate.Release(); }
    }

    internal async Task<InstagramPostRequest?> BuildRequestAsync(EventItem item, Func<string, string> resolveName,
        Func<string, string> resolveMention, CancellationToken ct)
    {
        string caption;
        InstagramVisual visual;
        string alt;
        string? artworkPath = null;
        if (item.Type == EventType.CustomEvent)
        {
            var plan = InstagramPost.BuildPlan(item.Id, item.Text, resolveMention, item.VisibleOnWebsite);
            caption = plan.PostText;
            visual = InstagramVisual.ForCustom(item.Text, resolveName);
            var readable = InstagramPost.BuildPlan(item.Id, item.Text, resolveName, item.VisibleOnWebsite);
            alt = readable.TitleText + "\n\n" + readable.BodyText;
        }
        else
        {
            var text = InstagramPost.HighlightText(item, resolveMention);
            if (string.IsNullOrWhiteSpace(text)) return null;
            caption = InstagramPost.Truncate(text, InstagramPost.MaxCaptionLength);
            alt = InstagramPost.HighlightText(item, resolveName);
            visual = InstagramVisual.ForEvent(item.Type, item.Text, alt, resolveName);
            if (item.Type == EventType.AthleteCountMilestone && EventHelpers.TryExtractAthleteCount(item.Text, out var count)
                && milestoneMemes.TryGetMeme(count, out var meme))
                artworkPath = meme.FullPath;
        }
        if (string.IsNullOrWhiteSpace(caption)) return null;
        var imageUrl = await images.RenderAsync(visual, artworkPath, ct);
        return new(caption, imageUrl, InstagramPost.Truncate(alt, InstagramPost.MaxAltTextLength));
    }
}
