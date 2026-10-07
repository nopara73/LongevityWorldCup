using System.Globalization;
using System.Text.Json;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class InstagramAnnouncementService(
    EventDataService events,
    SocialDeliveryStore deliveries,
    InstagramApiClient api,
    InstagramPublisher publisher,
    ThreadsEventService messages,
    AthleteDataService athletes,
    InstagramImageService images,
    AthleteCountMilestoneMemeService memes,
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
            messages.SetAthletesForThreads(snapshot);
            var names = snapshot.ToDictionary(x => x.Slug, x => x.Name, StringComparer.OrdinalIgnoreCase);
            string ResolveName(string slug) => names.TryGetValue(slug, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('_', ' ').Replace('-', ' '));

            foreach (var pending in deliveries.GetPending(SocialDeliveryStore.Instagram, customOnly, now))
            {
                ct.ThrowIfCancellationRequested();
                var item = pending.Event;
                if (pending.HiddenForMissingAthlete)
                {
                    deliveries.Skip(SocialDeliveryStore.Instagram, item.Id, "MissingAthlete", now);
                    continue;
                }
                string? subject = item.Type is EventType.NewRank or EventType.BadgeAward or EventType.CrowdAgeTop10Change or EventType.AgeImprovementTop10Change
                    && EventHelpers.TryExtractSlug(item.Text, out var slug) ? slug.Trim() : null;
                if (pending.RecordJson is null)
                {
                    if (SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(item.Type, item.Text, item.OccurredAtUtc, pending.Priority,
                        now.UtcDateTime.AddDays(-7), athletes.HasSingleGlobalPlaceOneBadgeHolder, out var reason))
                    {
                        deliveries.Skip(SocialDeliveryStore.Instagram, item.Id, reason.ToString(), now);
                        continue;
                    }
                    if (subject is not null && deliveries.IsSubjectOnCooldown(SocialDeliveryStore.Instagram, subject, now)) continue;
                }

                try
                {
                    var json = pending.RecordJson;
                    if (json is null)
                    {
                        var request = await BuildRequestAsync(item, ResolveName, ct);
                        if (request is null)
                        {
                            deliveries.Skip(SocialDeliveryStore.Instagram, item.Id, SocialEventSkipReason.EmptyMessage.ToString(), now);
                            continue;
                        }
                        (_, json) = deliveries.Prepare(SocialDeliveryStore.Instagram, item.Id, JsonSerializer.Serialize(request), subject, time.GetUtcNow());
                    }
                    var prepared = JsonSerializer.Deserialize<InstagramPostRequest>(json)
                        ?? throw new InvalidOperationException("The saved Instagram request is invalid.");
                    var receipt = await publisher.PublishAsync(pending, prepared, ct);
                    deliveries.Complete(SocialDeliveryStore.Instagram, item.Id, receipt, time.GetUtcNow());
                    log.LogInformation("Instagram delivered event {EventId}: {PostUrl}", item.Id, receipt.Url);
                    return;
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

    private async Task<InstagramPostRequest?> BuildRequestAsync(EventItem item, Func<string, string> resolveName, CancellationToken ct)
    {
        string caption;
        string imageText;
        string alt;
        string? meme = null;
        if (item.Type == EventType.CustomEvent)
        {
            var plan = InstagramPost.BuildPlan(item.Id, item.Text, resolveName, item.VisibleOnWebsite);
            caption = plan.PostText;
            imageText = item.Text;
            alt = plan.TitleText + "\n\n" + plan.BodyText;
        }
        else
        {
            var text = messages.TryBuildMessage(item.Type, item.Text, item.Id, item.VisibleOnWebsite) ?? "";
            if (string.IsNullOrWhiteSpace(text)) return null;
            caption = InstagramPost.Truncate(text, InstagramPost.MaxCaptionLength);
            imageText = alt = text;
            if (item.Type == EventType.AthleteCountMilestone && EventHelpers.TryExtractAthleteCount(item.Text, out var count)
                && memes.TryGetMeme(count, out var media)) meme = media.FullPath;
        }
        if (string.IsNullOrWhiteSpace(caption)) return null;
        var imageUrl = await images.RenderAsync(imageText, resolveName, meme, ct);
        return new(caption, imageUrl, InstagramPost.Truncate(alt, InstagramPost.MaxAltTextLength));
    }
}
