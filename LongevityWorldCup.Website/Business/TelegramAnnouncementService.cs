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
    TimeProvider time,
    ILogger<TelegramAnnouncementService> log)
{
    internal const int MaxLength = 4096;
    private const string Platform = SocialDeliveryStore.Telegram;

    internal async Task DispatchAsync(bool customOnly, CancellationToken ct = default)
    {
        _ = events; // The Event schema and Telegram baseline are initialized before dispatch.
        if (config.Telegram is null) return;

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

            var json = candidate.RecordJson;
            if (json is null)
            {
                if (SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(item.Type, item.Text, item.OccurredAtUtc,
                        candidate.Priority, now.UtcDateTime.AddDays(-7), athletes.HasSingleGlobalPlaceOneBadgeHolder, out var reason))
                {
                    deliveries.Skip(Platform, item.Id, reason.ToString(), now);
                    continue;
                }

                string? subject = item.Type is EventType.NewRank or EventType.BadgeAward or EventType.CrowdAgeTop10Change or EventType.AgeImprovementTop10Change
                    && EventHelpers.TryExtractSlug(item.Text, out var slug) ? slug.Trim() : null;
                if (subject is not null && deliveries.IsSubjectOnCooldown(Platform, subject, now)) continue;

                var text = item.Type == EventType.CustomEvent
                    ? BuildCustomPost(item, ResolveName)
                    : Truncate(messageComposer.TryBuildMessage(item.Type, item.Text, item.Id, item.VisibleOnWebsite, ResolveName) ?? "", MaxLength);
                if (string.IsNullOrWhiteSpace(text))
                {
                    deliveries.Skip(Platform, item.Id, SocialEventSkipReason.EmptyMessage.ToString(), now);
                    continue;
                }
                (_, json) = deliveries.Prepare(Platform, item.Id, JsonSerializer.Serialize(text), subject, now);
            }

            var prepared = JsonSerializer.Deserialize<string>(json)
                ?? throw new InvalidOperationException("The saved Telegram post is invalid.");
            if (!deliveries.BeginAttempt(Platform, item.Id, time.GetUtcNow(), onlyIfUnattempted: true)) continue;
            try
            {
                var receipt = await api.PublishAsync(prepared, ct);
                deliveries.Complete(Platform, item.Id, receipt, time.GetUtcNow());
                log.LogInformation("Telegram delivered Event {EventId} to {PostUrl}", item.Id, receipt.Url);
            }
            catch (TelegramRejectedException error) when (error.Code == 429)
            {
                var delay = TimeSpan.FromSeconds(Math.Clamp(error.RetryAfterSeconds ?? 60, 60, 86400));
                deliveries.Fail(Platform, item.Id, "HTTP429", delay, clearFirstAttempt: true, time.GetUtcNow());
                log.LogWarning("Telegram rate limited Event {EventId}; retry in {Delay}", item.Id, delay);
            }
            catch (TelegramRejectedException error)
            {
                deliveries.Reject(Platform, item.Id, $"HTTP{error.Code}", time.GetUtcNow());
                log.LogWarning("Telegram rejected Event {EventId} with HTTP {Status}", item.Id, error.Code);
            }
            catch (Exception error) when (!ct.IsCancellationRequested)
            {
                // The Bot API includes the token in the URL. Record only the exception type.
                RequireReview(item.Id, error.GetType().Name);
            }
            return;
        }
    }

    private void RequireReview(string eventId, string reason)
    {
        deliveries.RequireReview(Platform, eventId, time.GetUtcNow());
        log.LogWarning("Telegram Event {EventId} needs review because its delivery could not be confirmed ({Reason})", eventId, reason);
    }

    internal static string BuildCustomPost(EventItem item, Func<string, string> resolveName)
    {
        var plan = CustomEventSocialComposer.BuildPlan(item.Id, item.Text, int.MaxValue, resolveName, item.VisibleOnWebsite);
        if (plan.PostText.Length <= MaxLength) return plan.PostText;

        var text = plan.PostText;
        var link = plan.EventUrl;
        var suffix = "\n\n" + link;
        if (link.Length > 0 && text.EndsWith(suffix, StringComparison.Ordinal))
            text = text[..^suffix.Length];
        if (suffix.Length >= MaxLength)
            link = item.VisibleOnWebsite ? CustomEventSocialComposer.BuildEventUrl(item.Id) : "";
        if (link.Length == 0) return Truncate(text, MaxLength);

        suffix = "\n\n" + link;
        return Truncate(text, MaxLength - suffix.Length) + suffix;
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var end = StringInfo.ParseCombiningCharacters(text).Last(start => start < maxLength);
        return text[..end].TrimEnd() + "…";
    }
}
