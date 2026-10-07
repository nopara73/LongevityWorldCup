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
    ThreadsEventService messageComposer,
    AthleteDataService athletes,
    TimeProvider time)
{
    public const string Subreddit = "LongevityWorldCup";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task PrepareDailyAsync(CancellationToken ct = default)
    {
        _ = events;
        if (!config.RedditEnabled || !reddit.IsActive || !await _gate.WaitAsync(0, ct)) return;
        try
        {
            var now = time.GetUtcNow();
            var pending = deliveries.GetPending(SocialDeliveryStore.Reddit, customOnly: false, now);
            // Finish an earlier prepared announcement before selecting another ordinary Event.
            if (pending.Any(x => x.Event.Type != EventType.CustomEvent && x.RecordJson is not null)) return;
            foreach (var candidate in pending.Where(x => x.Event.Type != EventType.CustomEvent))
            {
                ct.ThrowIfCancellationRequested();
                var request = BuildRequest(candidate, now);
                if (request is null) continue;
                if (!reddit.ReserveDailyDelivery(candidate.Event.Id, now)) return;
                deliveries.Prepare(SocialDeliveryStore.Reddit, candidate.Event.Id, JsonSerializer.Serialize(request), Subject(candidate.Event), now);
                return;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<RedditDeliveryPayload?> GetNextAsync(CancellationToken ct = default)
    {
        _ = events;
        await _gate.WaitAsync(ct);
        try
        {
            reddit.Activate();
            var now = time.GetUtcNow();
            foreach (var candidate in deliveries.GetPending(SocialDeliveryStore.Reddit, customOnly: true, now))
            {
                ct.ThrowIfCancellationRequested();
                if (candidate.HiddenForMissingAthlete)
                {
                    deliveries.Skip(SocialDeliveryStore.Reddit, candidate.Event.Id, "MissingAthlete", now);
                    continue;
                }
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
                return new(key!, candidate.Event.Id, Subreddit, prepared.Title, prepared.Text);
            }
            return null;
        }
        finally { _gate.Release(); }
    }

    private RedditPostRequest? BuildRequest(PendingSocialDelivery candidate, DateTimeOffset now)
    {
        var item = candidate.Event;
        if (candidate.HiddenForMissingAthlete)
        {
            deliveries.Skip(SocialDeliveryStore.Reddit, item.Id, "MissingAthlete", now);
            return null;
        }
        if (SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(item.Type, item.Text, item.OccurredAtUtc,
            candidate.Priority, now.UtcDateTime.AddDays(-7), athletes.HasSingleGlobalPlaceOneBadgeHolder, out var reason))
        {
            deliveries.Skip(SocialDeliveryStore.Reddit, item.Id, reason.ToString(), now);
            return null;
        }
        var subject = Subject(item);
        if (subject is not null && deliveries.IsSubjectOnCooldown(SocialDeliveryStore.Reddit, subject, now)) return null;
        var snapshot = athletes.GetAthletesForX();
        messageComposer.SetAthletesForThreads(snapshot);
        var names = snapshot.ToDictionary(x => x.Slug, x => x.Name, StringComparer.OrdinalIgnoreCase);
        string ResolveName(string slug) => names.TryGetValue(slug, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('_', ' ').Replace('-', ' '));
        var ordinaryText = item.Type == EventType.CustomEvent ? "" : messageComposer.TryBuildMessage(item.Type, item.Text, item.Id, item.VisibleOnWebsite) ?? "";
        var request = RedditPost.Build(item, ordinaryText, ResolveName);
        if (request is null) deliveries.Skip(SocialDeliveryStore.Reddit, item.Id, SocialEventSkipReason.EmptyMessage.ToString(), now);
        return request;
    }

    private static string? Subject(EventItem item) => item.Type is EventType.NewRank or EventType.BadgeAward or EventType.CrowdAgeTop10Change or EventType.AgeImprovementTop10Change
        && EventHelpers.TryExtractSlug(item.Text, out var slug) ? slug.Trim() : null;
}
