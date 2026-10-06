using System.Globalization;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace LongevityWorldCup.Website.Business;

public sealed class BlueskyAnnouncementService(
    EventDataService events,
    SocialDeliveryStore deliveries,
    BlueskyApiClient api,
    ThreadsEventService messageComposer,
    AthleteDataService athletes,
    CustomEventImageService images,
    CustomEventLinkPreviewService links,
    AthleteCountMilestoneMemeService memes,
    TimeProvider time,
    ILogger<BlueskyAnnouncementService> log)
{
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);

    public async Task DispatchAsync(bool customOnly, CancellationToken ct = default)
    {
        // Resolve EventDataService first so its schema migration and historical baseline have completed.
        _ = events;
        if (!api.IsConfigured) return;
        if (!await _dispatchGate.WaitAsync(0, ct)) return;
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
                var e = pending.Event;
                if (pending.HiddenForMissingAthlete)
                {
                    deliveries.Skip(e.Id, "MissingAthlete", now);
                    continue;
                }
                string? subject = e.Type is EventType.NewRank or EventType.BadgeAward or EventType.CrowdAgeTop10Change or EventType.AgeImprovementTop10Change
                    && EventHelpers.TryExtractSlug(e.Text, out var slug) ? slug.Trim() : null;
                if (pending.RecordJson is null)
                {
                    if (SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(e.Type, e.Text, e.OccurredAtUtc, pending.Priority,
                        now.UtcDateTime.AddDays(-7), athletes.HasSingleGlobalPlaceOneBadgeHolder, out var reason))
                    {
                        deliveries.Skip(e.Id, reason.ToString(), now);
                        continue;
                    }
                    if (subject is not null && deliveries.IsSubjectOnCooldown(subject, now)) continue;
                }

                try
                {
                    var key = pending.RecordKey;
                    var json = pending.RecordJson;
                    if (json is null)
                    {
                        var record = await BuildRecordAsync(e, now, ResolveName, ct);
                        if (record is null)
                        {
                            deliveries.Skip(e.Id, SocialEventSkipReason.EmptyMessage.ToString(), now);
                            continue;
                        }
                        key = BlueskyPost.NewRecordKey(now);
                        json = record.ToJsonString();
                        (key, json) = deliveries.Prepare(e.Id, key, json, subject, now);
                    }
                    var receipt = await api.SendRecordAsync(key!, JsonNode.Parse(json)!.AsObject(), ct);
                    deliveries.Complete(e.Id, receipt, time.GetUtcNow());
                    log.LogInformation("Bluesky delivered event {EventId}: {PostUri}", e.Id, receipt.Uri);
                    return; // One scheduled announcement, or one custom announcement per queue tick.
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    var errorCode = ex is BlueskyApiException apiError ? apiError.Code : ex.GetType().Name;
                    deliveries.Fail(e.Id, pending.Attempts, errorCode, time.GetUtcNow());
                    log.LogWarning("Bluesky event {EventId} remains pending after {ErrorCode}; retry {Attempt}", e.Id, errorCode, pending.Attempts + 1);
                    return;
                }
            }
        }
        finally { _dispatchGate.Release(); }
    }

    private async Task<JsonObject?> BuildRecordAsync(EventItem e, DateTimeOffset now, Func<string, string> resolveName, CancellationToken ct)
    {
        JsonObject record;
        byte[]? media = null;
        var contentType = "image/png";
        string? alt = null;
        if (e.Type == EventType.CustomEvent)
        {
            var plan = CustomEventSocialComposer.BuildPlan(e.Id, e.Text, BlueskyPost.MaxGraphemes, resolveName,
                includeEventUrl: e.VisibleOnWebsite, textLength: text => new StringInfo(text).LengthInTextElements, truncate: BlueskyPost.Truncate);
            if (string.IsNullOrWhiteSpace(plan.PostText)) return null;
            record = BlueskyPost.CreateRecord(plan.PostText, now);
            if (plan.Mode == CustomEventPostMode.Image)
            {
                using var stream = await images.RenderToStreamAsync(e.Text, resolveName, ct)
                    ?? throw new InvalidOperationException("Custom event image rendering is unavailable.");
                media = stream.ToArray();
                alt = plan.TitleText + "\n\n" + plan.BodyText;
            }
        }
        else
        {
            // Preserve established event copy and athlete names without borrowing X or Threads handles.
            var text = messageComposer.TryBuildMessage(e.Type, e.Text, e.Id, e.VisibleOnWebsite);
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!BlueskyPost.Fits(text))
            {
                using var stream = await images.RenderToStreamAsync(text, resolveName, ct)
                    ?? throw new InvalidOperationException("Announcement image rendering is unavailable.");
                media = stream.ToArray();
                alt = text;
                text = $"Longevity World Cup update\n\n{SocialPostLinks.LeaderboardUrl}";
            }
            record = BlueskyPost.CreateRecord(text, now);
            if (media is null && e.Type == EventType.AthleteCountMilestone && EventHelpers.TryExtractAthleteCount(e.Text, out var count)
                && memes.TryGetMeme(count, out var meme))
            {
                media = await File.ReadAllBytesAsync(meme.FullPath, ct);
                contentType = meme.ContentType;
                alt = text;
            }
        }

        if (media is not null)
        {
            if (media.Length > 2_000_000)
            {
                using var image = Image.Load(media);
                using var jpeg = new MemoryStream();
                await image.SaveAsJpegAsync(jpeg, new JpegEncoder { Quality = 80 }, ct);
                media = jpeg.ToArray();
                contentType = "image/jpeg";
            }
            var blob = await api.UploadImageAsync(media, contentType, ct);
            record["embed"] = new JsonObject
            {
                ["$type"] = "app.bsky.embed.images",
                ["images"] = new JsonArray(new JsonObject { ["image"] = blob, ["alt"] = alt ?? "Longevity World Cup announcement" })
            };
        }
        else if (record["facets"] is JsonArray facets && facets.LastOrDefault()?["features"]?[0]?["uri"]?.GetValue<string>() is { } link)
        {
            var preview = await links.FetchAsync(link, ct);
            record["embed"] = new JsonObject
            {
                ["$type"] = "app.bsky.embed.external",
                ["external"] = new JsonObject
                {
                    ["uri"] = link,
                    ["title"] = preview?.Title ?? new Uri(link).Host,
                    ["description"] = preview?.Description ?? ""
                }
            };
        }
        return record;
    }
}
