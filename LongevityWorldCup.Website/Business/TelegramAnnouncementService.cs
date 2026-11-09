using System.Globalization;
using System.Text.Json;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class TelegramAnnouncementService(
    Config config,
    EventDataService events,
    SocialDeliveryStore deliveries,
    TelegramApiClient api,
    ThreadsEventService messageComposer,
    AthleteDataService athletes,
    CustomEventImageService images,
    AthleteCountMilestoneMemeService memes,
    TimeProvider time,
    ILogger<TelegramAnnouncementService> log)
{
    internal const int MaxLength = 4096;
    internal const int CaptionLength = 1024;
    private const string Platform = SocialDeliveryStore.Telegram;
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);

    internal async Task DispatchAsync(bool customOnly, CancellationToken ct = default)
    {
        _ = events; // The Event schema and Telegram baseline are initialized before dispatch.
        if (config.Telegram is null || !await _dispatchGate.WaitAsync(0, ct)) return;
        try
        {
            await DispatchPendingAsync(customOnly, ct);
        }
        finally { _dispatchGate.Release(); }
    }

    private async Task DispatchPendingAsync(bool customOnly, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var directory = athletes.GetAthletesForX();
        messageComposer.SetAthletesForThreads(directory);
        var names = directory.ToDictionary(athlete => athlete.Slug, athlete => athlete.Name, StringComparer.OrdinalIgnoreCase);
        string ResolveName(string slug) => names.TryGetValue(slug, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('_', ' ').Replace('-', ' '));

        foreach (var candidate in deliveries.GetPending(Platform, customOnly, now))
        {
            ct.ThrowIfCancellationRequested();
            var item = candidate.Event;
            if (!customOnly && item.Type == EventType.CustomEvent) continue;
            if (candidate.FirstAttemptAtUtc is { } firstAttempt)
            {
                // Telegram has no sendMessage idempotency key. Allow an in-flight request to finish
                // and never resend a post whose outcome could not be recorded.
                if (now - firstAttempt >= TimeSpan.FromMinutes(1))
                    RequireReview(item.Id, "InterruptedRequest");
                continue;
            }
            if (candidate.HiddenForMissingAthlete || (item.Type != EventType.CustomEvent && !item.VisibleOnWebsite))
            {
                deliveries.Skip(Platform, item.Id, candidate.HiddenForMissingAthlete ? "MissingAthlete" : "HiddenEvent", now);
                continue;
            }

            var postStarted = false;
            try
            {
                var json = candidate.RecordJson;
                if (json is null)
                {
                    if (SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(item.Type, item.Text, item.OccurredAtUtc,
                            candidate.Priority, now.UtcDateTime.AddDays(-7), athletes.HasSingleGlobalPlaceOneBadgeHolder, out var reason))
                    {
                        deliveries.Skip(Platform, item.Id, reason.ToString(), now);
                        continue;
                    }

                    string? subject = item.Type is EventType.NewRank or EventType.BadgeAward or EventType.CrowdAgeTop10Change or EventType.AgeImprovementTop10Change or EventType.PhenoAgeTop10Change
                        && EventHelpers.TryExtractSlug(item.Text, out var slug) ? slug.Trim() : null;
                    if (subject is not null && deliveries.IsSubjectOnCooldown(Platform, subject, now)) continue;

                    var post = await BuildRequestAsync(item, ResolveName, ct);
                    if (string.IsNullOrWhiteSpace(post.Text))
                    {
                        deliveries.Skip(Platform, item.Id, SocialEventSkipReason.EmptyMessage.ToString(), now);
                        continue;
                    }
                    (_, json) = deliveries.Prepare(Platform, item.Id, JsonSerializer.Serialize(post), subject, now);
                }

                // Preserve text requests prepared before media support was deployed.
                using var saved = JsonDocument.Parse(json);
                var prepared = saved.RootElement.ValueKind == JsonValueKind.String
                    ? new TelegramPostRequest(saved.RootElement.GetString()!)
                    : saved.RootElement.Deserialize<TelegramPostRequest>()
                        ?? throw new InvalidOperationException("The saved Telegram post is invalid.");
                if (!deliveries.BeginAttempt(Platform, item.Id, time.GetUtcNow(), onlyIfUnattempted: true)) continue;
                postStarted = true;
                var receipt = await api.PublishAsync(prepared, ct);
                deliveries.Complete(Platform, item.Id, receipt, time.GetUtcNow());
                log.LogInformation("Telegram delivered Event {EventId} to {PostUrl}", item.Id, receipt.Url);
            }
            catch (TelegramRejectedException error) when (error.Code == 429)
            {
                var delay = TimeSpan.FromSeconds(Math.Max(error.RetryAfterSeconds ?? 60, 60));
                deliveries.Fail(Platform, item.Id, "HTTP429", delay, clearFirstAttempt: true, time.GetUtcNow());
                log.LogWarning("Telegram rate limited Event {EventId}; retry in {Delay}", item.Id, delay);
            }
            catch (TelegramRejectedException error)
            {
                deliveries.Reject(Platform, item.Id, $"HTTP{error.Code}", time.GetUtcNow());
                log.LogError("Telegram rejected Event {EventId} with HTTP {Status}", item.Id, error.Code);
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                // The Bot API includes the token in the URL. Record only the exception type.
                if (postStarted)
                    RequireReview(item.Id, error.GetType().Name);
                else
                {
                    var delay = TimeSpan.FromMinutes(Math.Min(10, Math.Pow(2, Math.Min(candidate.Attempts + 1, 4))));
                    deliveries.Fail(Platform, item.Id, error.GetType().Name, delay, clearFirstAttempt: true, time.GetUtcNow());
                    log.LogError("Telegram could not prepare Event {EventId} ({Reason}); retry in {Delay}", item.Id, error.GetType().Name, delay);
                }
            }
            return;
        }
    }

    private void RequireReview(string eventId, string reason)
    {
        deliveries.RequireReview(Platform, eventId, time.GetUtcNow());
        log.LogError("Telegram Event {EventId} needs review because its delivery could not be confirmed ({Reason})", eventId, reason);
    }

    private async Task<TelegramPostRequest> BuildRequestAsync(EventItem item, Func<string, string> resolveName, CancellationToken ct)
    {
        if (item.Type == EventType.CustomEvent)
        {
            var plan = BuildCustomPlan(item, resolveName);
            if (plan.Mode == CustomEventPostMode.Text) return new(plan.PostText);
            // Keep Telegram's image separate from other channels' mention resolution and layout.
            var asset = await images.RenderAsync(item.Id + "-telegram", item.Text, resolveName, ct,
                useLinkedArtwork: plan.Mode == CustomEventPostMode.Artwork)
                ?? throw new InvalidOperationException("Telegram announcement image rendering is unavailable.");
            return new(plan.PostText, asset.PublicUrl);
        }

        var text = messageComposer.TryBuildMessage(item.Type, item.Text, item.Id, item.VisibleOnWebsite, resolveName) ?? "";
        if (text.Length <= CaptionLength && item.Type == EventType.AthleteCountMilestone
            && EventHelpers.TryExtractAthleteCount(item.Text, out var count) && memes.TryGetMeme(count, out var meme))
            return new(text, meme.PublicUrl);
        return new(Truncate(text, MaxLength));
    }

    internal static CustomEventSocialPlan BuildCustomPlan(EventItem item, Func<string, string> resolveName)
    {
        var plan = CustomEventSocialComposer.BuildPlan(item.Id, item.Text, MaxLength, resolveName, item.VisibleOnWebsite, truncate: Truncate);
        // Telegram accepts 4096 characters in messages, but only 1024 in photo captions.
        if (plan.Mode == CustomEventPostMode.Text) return plan;
        var caption = CustomEventSocialComposer.BuildPlan(item.Id, item.Text, CaptionLength, resolveName, item.VisibleOnWebsite, truncate: Truncate);
        if (caption.PostText.Length <= CaptionLength) return caption;
        var link = item.VisibleOnWebsite ? CustomEventSocialComposer.BuildEventUrl(item.Id) : "";
        var suffix = link.Length == 0 ? "" : "\n\n" + link;
        return caption with { PostText = Truncate(caption.TitleText, CaptionLength - suffix.Length) + suffix };
    }

    private static string Truncate(string text, int maxLength)
    {
        if (maxLength <= 0) return "";
        if (text.Length <= maxLength) return text;
        var end = StringInfo.ParseCombiningCharacters(text).Last(start => start < maxLength);
        return text[..end].TrimEnd() + "…";
    }
}
