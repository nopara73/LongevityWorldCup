using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

internal static class RedditEventPolicy
{
    internal const string RoutineUpdate = "RedditRoutineUpdate";

    internal static string? SkipReason(EventType type, string text, DateTime occurredAtUtc, DateTimeOffset now)
    {
        // Custom Events have already been deliberately selected for this destination.
        if (type == EventType.CustomEvent) return null;
        if (type != EventType.NewRank) return RoutineUpdate;
        if (!EventHelpers.TryExtractRank(text, out var rank) || rank < 1 ||
            !EventHelpers.TryExtractSlug(text, out var slug) || string.IsNullOrWhiteSpace(slug))
            return nameof(SocialEventSkipReason.UnsupportedEventPayload);
        // NewRank is emitted from the full Ultimate League ranking. Category wins
        // and other leaderboard changes are separate Event types, not overall leads.
        if (rank != 1) return RoutineUpdate;
        return occurredAtUtc < now.UtcDateTime.AddDays(-7)
            ? nameof(SocialEventSkipReason.StalePrimaryEvent)
            : null;
    }
}
