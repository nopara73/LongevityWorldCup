using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class WebPushAnnouncementService(EventDataService events, WebPushStore store, WebPushTransport transport,
    SocialDeliveryStore deliveries, AssetVersionProvider assets, ILogger<WebPushAnnouncementService> log, TimeProvider time,
    AthleteDataService athletes, ThreadsEventService messageComposer)
{
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);

    public async Task DispatchAsync(CancellationToken ct = default)
    {
        _ = events; // Resolve the Event schema and channel baseline before querying delivery storage.
        if (!transport.IsConfigured) return;
        if (!await _dispatchGate.WaitAsync(0, ct)) return;
        try
        {
            var now = time.GetUtcNow();
            store.Prune(now);
            store.EnableAutomaticAnnouncements(now);
            var directory = athletes.GetAthletesForX();
            messageComposer.SetAthletesForThreads(directory);
            var names = directory.ToDictionary(a => a.Slug, a => a.Name, StringComparer.OrdinalIgnoreCase);
            string ResolveName(string slug) => names.TryGetValue(slug, out var name) ? name : slug.Replace('_', ' ');
            foreach (var candidate in deliveries.GetPending(SocialDeliveryStore.WebPush, false, now))
            {
                var item = candidate.Event;
                if (item.Type == EventType.CustomEvent || candidate.RecordJson is not null) continue;
                if (candidate.HiddenForMissingAthlete)
                {
                    deliveries.Skip(SocialDeliveryStore.WebPush, item.Id, "MissingAthlete", now);
                    continue;
                }
                if (SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(item.Type, item.Text, item.OccurredAtUtc,
                        candidate.Priority, now.UtcDateTime.AddDays(-7), athletes.HasSingleGlobalPlaceOneBadgeHolder, out var reason))
                {
                    deliveries.Skip(SocialDeliveryStore.WebPush, item.Id, reason.ToString(), now);
                    continue;
                }
                var subject = item.Type is EventType.NewRank or EventType.BadgeAward or EventType.CrowdAgeTop10Change or EventType.AgeImprovementTop10Change
                    && EventHelpers.TryExtractSlug(item.Text, out var slug) ? slug : null;
                if (subject is not null && (store.HasPendingSubject(subject)
                        || deliveries.IsSubjectOnCooldown(SocialDeliveryStore.WebPush, subject, now))) continue;
                if (string.IsNullOrWhiteSpace(messageComposer.TryBuildMessage(item.Type, item.Text, item.Id, item.VisibleOnWebsite, ResolveName)))
                {
                    deliveries.Skip(SocialDeliveryStore.WebPush, item.Id, "EmptyMessage", now);
                    continue;
                }
                store.QueueAutomatic(item, subject, now);
                break; // One automatic announcement per tick; retain per-athlete cooldowns.
            }
            foreach (var announcement in store.Pending())
            {
                ct.ThrowIfCancellationRequested();
                var payload = announcement.Payload ?? store.Prepare(announcement, BuildPayload(announcement, assets,
                    announcement.Type == EventType.CustomEvent ? null : messageComposer.TryBuildMessage(
                        announcement.Type, announcement.Text, announcement.Id, announcement.VisibleOnWebsite, ResolveName)), now);
                var topic = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(announcement.Id)))[..32];
                foreach (var recipient in store.Recipients(announcement.Id, now))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var result = await transport.SendAsync(recipient.Subscription, payload, topic, now, ct);
                        if ((int)result.Status is >= 200 and < 300)
                            store.Record(announcement.Id, recipient, "sent", null, null, now);
                        else if (result.Status is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                            store.Unsubscribe(recipient.Subscription);
                        else if (result.Status == HttpStatusCode.TooManyRequests || (int)result.Status >= 500)
                            Retry(announcement.Id, recipient, "HTTP" + (int)result.Status, result.RetryAfter, now);
                        else
                            store.Record(announcement.Id, recipient, "failed", "HTTP" + (int)result.Status, null, now);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or CryptographicException or ArgumentException)
                    {
                        // Endpoint URLs and encryption keys are capabilities: do not include exception messages in logs.
                        Retry(announcement.Id, recipient, ex.GetType().Name, null, now);
                    }
                }
                var progress = store.Progress(announcement.Id);
                if (progress.Pending > 0) continue;
                if (progress.Accepted == 0) deliveries.Skip(SocialDeliveryStore.WebPush, announcement.Id,
                    progress.Total == 0 ? "NoSubscribers" : "NoAcceptedDeliveries", now);
                else deliveries.Complete(SocialDeliveryStore.WebPush, announcement.Id,
                    new($"accepted:{progress.Accepted}", $"https://longevityworldcup.com/events?event={announcement.Id}"), now);
                log.LogInformation("Website push Event {EventId}: {Accepted} provider acceptances out of {Total} remaining subscription records.",
                    announcement.Id, progress.Accepted, progress.Total);
            }
        }
        finally { _dispatchGate.Release(); }
    }

    private void Retry(string id, PushRecipient recipient, string code, TimeSpan? retryAfter, DateTimeOffset now)
    {
        var exhausted = recipient.Attempts >= 7;
        var minutes = Math.Clamp(retryAfter?.TotalMinutes ?? Math.Pow(2, recipient.Attempts + 1), 1, 1440);
        store.Record(id, recipient, exhausted ? "failed" : "pending", code, exhausted ? null : now.AddMinutes(minutes), now);
    }

    internal static string BuildPayload(PushAnnouncement announcement, AssetVersionProvider assets, string? message = null)
    {
        var (titleRaw, contentRaw) = CustomEventMarkup.SplitTitleAndContent(message ?? announcement.Text);
        var title = Clamp(CustomEventMarkup.ToPlainText(titleRaw).Trim(), 100, 512);
        var body = Clamp(CustomEventMarkup.ToPlainText(contentRaw).Trim(), 200, 1400);
        var artwork = announcement.Type == EventType.CustomEvent ? CustomEventArtwork.ForLinkedPage(announcement.Text) : null;
        var url = announcement.Type == EventType.DonationReceived ? "/contribute"
            : announcement.Type != EventType.CustomEvent && EventHelpers.TryExtractSlug(announcement.Text, out var slug)
                ? "/athlete/" + slug.Replace('_', '-')
                : artwork is not null && Uri.TryCreate(CustomEventMarkup.GetSingleHyperlink(announcement.Text), UriKind.Absolute, out var linkedPage)
                    && linkedPage.Host is "longevityworldcup.com" or "www.longevityworldcup.com" ? linkedPage.PathAndQuery
                    : announcement.VisibleOnWebsite ? $"/events?event={announcement.Id}" : "/events";
        return JsonSerializer.Serialize(new
        {
            id = announcement.Id, title, body,
            url,
            image = artwork is null ? null : assets.AppendVersion("/" + artwork.AssetPath),
            icon = assets.AppendVersion("/assets/favicon-192x192.png")
        }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static string Clamp(string text, int limit, int bytes)
    {
        var positions = StringInfo.ParseCombiningCharacters(text);
        if (positions.Length <= limit && Encoding.UTF8.GetByteCount(text) <= bytes) return text;
        var count = Math.Min(limit - 1, positions.Length);
        while (count > 0)
        {
            var end = count < positions.Length ? positions[count] : text.Length;
            if (Encoding.UTF8.GetByteCount(text.AsSpan(0, end)) + 3 <= bytes) return text[..end] + "…";
            count--;
        }
        return "…";
    }
}
