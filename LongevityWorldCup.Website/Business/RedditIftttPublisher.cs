using System.Net.Http.Json;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class RedditIftttPublisher(Config config, RedditAnnouncementService announcements, RedditDeliveryStore deliveries,
    IHttpClientFactory clients, TimeProvider time, ILogger<RedditIftttPublisher> log)
{
    internal async Task DispatchAsync(CancellationToken ct)
    {
        if (!config.RedditUseIfttt || !config.RedditDispatchEnabled) return;

        var post = await announcements.ClaimNextIftttAsync(ct);
        if (post is null) return;

        using var client = clients.CreateClient(nameof(RedditIftttPublisher));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"trigger/{RedditIftttPost.EventName}/with/key/{config.RedditIftttWebhookKey}")
        {
            Content = JsonContent.Create(new { value1 = post.Title, value2 = RedditIftttPost.FormatBody(post.Text) })
        };

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode) return; // The receipt applet verifies the actual Reddit post.
            log.LogWarning("IFTTT returned status {Status} for Reddit Event {EventId}.", (int)response.StatusCode, post.EventId);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // The request URL contains a key. Never include the request or exception in logs.
            log.LogWarning("Could not confirm the IFTTT request for Reddit Event {EventId}.", post.EventId);
        }

        deliveries.RequireReview(post.DeliveryId, time.GetUtcNow());
    }
}
