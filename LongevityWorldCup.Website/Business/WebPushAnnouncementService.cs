using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class WebPushAnnouncementService(EventDataService events, WebPushStore store, WebPushTransport transport,
    SocialDeliveryStore deliveries, AssetVersionProvider assets, ILogger<WebPushAnnouncementService> log, TimeProvider time)
{
    public async Task DispatchAsync(CancellationToken ct = default)
    {
        _ = events; // Resolve the Event schema and channel baseline before querying delivery storage.
        if (!transport.IsConfigured) return;
        var now = time.GetUtcNow();
        store.Prune(now);
        foreach (var announcement in store.Pending())
        {
            ct.ThrowIfCancellationRequested();
            var payload = announcement.Payload ?? store.Prepare(announcement, BuildPayload(announcement, assets), now);
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

    private void Retry(string id, PushRecipient recipient, string code, TimeSpan? retryAfter, DateTimeOffset now)
    {
        var exhausted = recipient.Attempts >= 7;
        var minutes = Math.Clamp(retryAfter?.TotalMinutes ?? Math.Pow(2, recipient.Attempts + 1), 1, 1440);
        store.Record(id, recipient, exhausted ? "failed" : "pending", code, exhausted ? null : now.AddMinutes(minutes), now);
    }

    internal static string BuildPayload(PushAnnouncement announcement, AssetVersionProvider assets)
    {
        var (titleRaw, contentRaw) = CustomEventMarkup.SplitTitleAndContent(announcement.Text);
        var title = Clamp(CustomEventMarkup.ToPlainText(titleRaw).Trim(), 100, 512);
        var body = Clamp(CustomEventMarkup.ToPlainText(contentRaw).Trim(), 200, 1400);
        return JsonSerializer.Serialize(new
        {
            id = announcement.Id, title, body,
            url = announcement.VisibleOnWebsite ? $"/events?event={announcement.Id}" : "/events",
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
