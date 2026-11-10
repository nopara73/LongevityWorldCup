using System.Globalization;
using System.Text.Json;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed record RedditDeliveryPayload(string DeliveryId, string EventId, string Subreddit, string Title, string Text);

public sealed class RedditAnnouncementService(
    Config config,
    EventDataService events,
    SocialDeliveryStore deliveries,
    RedditDeliveryStore reddit,
    AthleteDataService athletes,
    TimeProvider time)
{
    public const string Subreddit = "LongevityWorldCup";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PrepareDailyAsync(CancellationToken ct = default)
    {
        _ = events;
        if (!config.RedditDispatchEnabled || !reddit.IsActive || !await _gate.WaitAsync(0, ct)) return;
        try
        {
            var now = time.GetUtcNow();
            var pending = deliveries.GetPending(SocialDeliveryStore.Reddit, customOnly: false, now);
            foreach (var candidate in pending.Where(x => x.Event.Type != EventType.CustomEvent))
            {
                ct.ThrowIfCancellationRequested();
                if (SkipIneligible(candidate, now)) continue;
                // Recheck old prepared copy before it can block today's selection.
                if (candidate.RecordJson is not null) return;
                var request = BuildRequest(candidate, now);
                if (request is null) continue;
                if (!reddit.ReserveDailyDelivery(candidate.Event.Id, now)) return;
                deliveries.Prepare(SocialDeliveryStore.Reddit, candidate.Event.Id, JsonSerializer.Serialize(request), Subject(candidate.Event), now);
                return;
            }
        }
        finally { _gate.Release(); }
    }

    public Task<RedditDeliveryPayload?> GetNextAsync(CancellationToken ct = default) => GetNextAsync(false, ct);

    internal Task<RedditDeliveryPayload?> ClaimNextIftttAsync(CancellationToken ct) => GetNextAsync(true, ct);

    private async Task<RedditDeliveryPayload?> GetNextAsync(bool ifttt, CancellationToken ct)
    {
        _ = events;
        await _gate.WaitAsync(ct);
        try
        {
            reddit.Activate();
            var now = time.GetUtcNow();
            if (ifttt) reddit.ExpireIftttAttempts(now);
            foreach (var candidate in deliveries.GetPending(SocialDeliveryStore.Reddit, customOnly: true, now))
            {
                ct.ThrowIfCancellationRequested();
                if (ifttt && candidate.FirstAttemptAtUtc.HasValue) continue;
                if (SkipIneligible(candidate, now)) continue;
                var key = candidate.RecordKey;
                var json = candidate.RecordJson;
                if (json is null)
                {
                    var request = BuildRequest(candidate, now);
                    if (request is null) continue;
                    (key, json) = deliveries.Prepare(SocialDeliveryStore.Reddit, candidate.Event.Id,
                        JsonSerializer.Serialize(request), Subject(candidate.Event), now);
                }
                var prepared = JsonSerializer.Deserialize<RedditPostRequest>(json)
                    ?? throw new InvalidOperationException("The saved Reddit request is invalid.");
                if (ifttt && reddit.Begin(key!, now, RedditIftttPost.Fingerprint(prepared.Title, prepared.Text),
                    reddit.GetBlockedLinks(config.RedditBlockedLinks)) != RedditBeginResult.Started)
                    continue;
                return new(key!, candidate.Event.Id, Subreddit, prepared.Title, prepared.Text);
            }
            return null;
        }
        finally { _gate.Release(); }
    }

    private RedditPostRequest? BuildRequest(PendingSocialDelivery candidate, DateTimeOffset now)
    {
        var item = candidate.Event;
        var subject = Subject(item);
        if (subject is not null && deliveries.IsSubjectOnCooldown(SocialDeliveryStore.Reddit, subject, now)) return null;
        var snapshot = athletes.GetAthletesForX();
        var names = snapshot.ToDictionary(x => x.Slug, x => x.Name, StringComparer.OrdinalIgnoreCase);
        string ResolveName(string slug) => names.TryGetValue(slug, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('_', ' ').Replace('-', ' '));
        var ordinaryText = item.Type == EventType.CustomEvent ? "" : RedditPost.BuildLeaderText(item, snapshot) ?? "";
        var request = RedditPost.Build(item, ordinaryText, ResolveName);
        if (request is null)
        {
            reddit.SkipUnstarted(item.Id, nameof(SocialEventSkipReason.EmptyMessage), now);
            return null;
        }
        var blockedLinks = reddit.GetBlockedLinks(config.RedditBlockedLinks);
        var blockedLink = item.Type == EventType.CustomEvent
            ? RedditLinkPolicy.FindBlockedLink(item.Text, blockedLinks)
            : null;
        blockedLink ??= RedditLinkPolicy.FindBlockedPostLink(request, blockedLinks);
        if (blockedLink is not null)
        {
            reddit.SkipBlockedLink(item.Id, blockedLink, now);
            return null;
        }
        return request;
    }

    private bool SkipIneligible(PendingSocialDelivery candidate, DateTimeOffset now)
    {
        // A submission already started must keep its immutable copy and receipt path.
        if (candidate.FirstAttemptAtUtc.HasValue) return false;
        var item = candidate.Event;
        var reason = candidate.HiddenForMissingAthlete ? "MissingAthlete"
            : RedditEventPolicy.SkipReason(item.Type, item.Text, item.OccurredAtUtc, now);
        if (reason is null && item.Type == EventType.NewRank &&
            (reddit.HasNewerLeaderEvent(item.OccurredAtUtc) || !athletes.GetAthletesForX().Any(a => a.CurrentRank == 1 &&
                EventHelpers.TryExtractSlug(item.Text, out var slug) &&
                string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase))))
            reason = "SupersededLeader";
        if (reason is null) return false;
        reddit.SkipUnstarted(item.Id, reason, now);
        return true;
    }

    private static string? Subject(EventItem item) => item.Type == EventType.NewRank
        && EventHelpers.TryExtractSlug(item.Text, out var slug) ? slug.Trim() : null;
}
