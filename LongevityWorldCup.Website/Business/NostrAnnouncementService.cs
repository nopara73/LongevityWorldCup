using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class NostrAnnouncementService(
    EventDataService events,
    SocialDeliveryStore deliveries,
    NostrRelayClient relays,
    ThreadsEventService messageComposer,
    AthleteDataService athletes,
    CustomEventImageService images,
    AthleteCountMilestoneMemeService memes,
    TimeProvider time,
    ILogger<NostrAnnouncementService> log)
{
    private readonly SemaphoreSlim _dispatchGate = new(1, 1);
    private const string Platform = SocialDeliveryStore.Nostr;

    public async Task DispatchAsync(bool customOnly, CancellationToken ct = default)
    {
        _ = events;
        if (!relays.IsConfigured || !await _dispatchGate.WaitAsync(0, ct)) return;
        try
        {
            var now = time.GetUtcNow();
            var snapshot = athletes.GetAthletesForX();
            messageComposer.SetAthletesForThreads(snapshot);
            var names = snapshot.ToDictionary(x => x.Slug, x => x.Name, StringComparer.OrdinalIgnoreCase);
            string ResolveName(string slug) => names.TryGetValue(slug, out var name) && !string.IsNullOrWhiteSpace(name)
                ? name : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('_', ' ').Replace('-', ' '));

            foreach (var pending in deliveries.GetPending(Platform, customOnly, now))
            {
                ct.ThrowIfCancellationRequested();
                var item = pending.Event;
                if (pending.HiddenForMissingAthlete)
                {
                    deliveries.Skip(Platform, item.Id, "MissingAthlete", now);
                    continue;
                }
                string? subject = item.Type is EventType.NewRank or EventType.BadgeAward or EventType.CrowdAgeTop10Change or EventType.AgeImprovementTop10Change
                    && EventHelpers.TryExtractSlug(item.Text, out var slug) ? slug.Trim() : null;
                if (pending.RecordJson is null)
                {
                    if (SocialEventSkipPolicy.TryGetXOrThreadsTerminalSkipReason(item.Type, item.Text, item.OccurredAtUtc, pending.Priority,
                        now.UtcDateTime.AddDays(-7), athletes.HasSingleGlobalPlaceOneBadgeHolder, out var reason))
                    {
                        deliveries.Skip(Platform, item.Id, reason.ToString(), now);
                        continue;
                    }
                    if (subject is not null && deliveries.IsSubjectOnCooldown(Platform, subject, now)) continue;
                }
                try
                {
                    relays.VerifyAccount();
                    var json = pending.RecordJson;
                    if (json is null)
                    {
                        var prepared = await BuildEventAsync(item, ResolveName, ct);
                        if (prepared is null)
                        {
                            deliveries.Skip(Platform, item.Id, SocialEventSkipReason.EmptyMessage.ToString(), now);
                            continue;
                        }
                        (_, json) = deliveries.Prepare(Platform, item.Id, JsonSerializer.Serialize(prepared), subject, time.GetUtcNow());
                    }
                    var saved = JsonSerializer.Deserialize<NostrEvent>(json)
                        ?? throw new NostrRelayException("NostrInvalidPreparedEvent");
                    deliveries.BeginAttempt(Platform, item.Id, time.GetUtcNow());
                    var receipt = await relays.PublishAsync(saved, ct);
                    deliveries.Complete(Platform, item.Id, receipt, time.GetUtcNow());
                    log.LogInformation("Nostr delivered event {EventId}: {NostrEventId}", item.Id, receipt.Id);
                    return;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    var code = (ex as NostrRelayException)?.Code ?? ex.GetType().Name;
                    var delay = TimeSpan.FromMinutes(Math.Min(10, Math.Pow(2, Math.Min(pending.Attempts + 1, 4))));
                    // Nostr IDs cover signed content. Resend the saved event indefinitely;
                    // generating a fresh timestamp or signature here would create another note.
                    deliveries.Fail(Platform, item.Id, code, delay, false, time.GetUtcNow());
                    log.LogWarning("Nostr event {EventId} remains pending after {ErrorCode}; retry {Attempt}", item.Id, code, pending.Attempts + 1);
                    return;
                }
            }
        }
        finally { _dispatchGate.Release(); }
    }

    private async Task<NostrEvent?> BuildEventAsync(EventItem item, Func<string, string> resolveName, CancellationToken ct)
    {
        string text;
        string? imageUrl = null;
        string? alt = null;
        var tags = new List<string[]> { new[] { "t", "longevity" }, new[] { "t", "geroscience" } };
        if (item.Type == EventType.CustomEvent)
        {
            var plan = CustomEventSocialComposer.BuildPlan(item.Id, item.Text, NostrProtocol.MaxCharacters, resolveName,
                includeEventUrl: item.VisibleOnWebsite);
            text = plan.PostText;
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (plan.Mode == CustomEventPostMode.Image)
            {
                alt = plan.TitleText + "\n\n" + plan.BodyText;
                // A public image already referenced by a signed note must not change when
                // another platform renders the same Event using different mention handles.
                var imageKey = "nostr-" + item.Id + "-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(alt)))[..16];
                var image = await images.RenderAsync(imageKey, item.Text, resolveName, ct)
                    ?? throw new InvalidOperationException("NostrImageUnavailable");
                imageUrl = image.PublicUrl;
            }
        }
        else
        {
            text = messageComposer.TryBuildMessage(item.Type, item.Text, item.Id, item.VisibleOnWebsite) ?? "";
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (item.Type == EventType.AthleteCountMilestone && EventHelpers.TryExtractAthleteCount(item.Text, out var count)
                && memes.TryGetMeme(count, out var meme))
            {
                imageUrl = meme.PublicUrl;
                alt = text;
            }
        }
        if (text.Length > NostrProtocol.MaxCharacters) throw new NostrRelayException("NostrContentTooLong");
        if (imageUrl is not null)
        {
            text += "\n\n" + imageUrl;
            tags.Add(["imeta", "url " + imageUrl, "alt " + alt]);
        }
        return relays.Prepare(text, time.GetUtcNow(), tags: tags.ToArray());
    }
}
