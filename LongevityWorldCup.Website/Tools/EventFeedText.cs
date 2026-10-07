using System.Globalization;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

internal static class EventFeedText
{
    internal static string? Describe(EventItem item, Func<string, string> athleteName)
    {
        var raw = item.Text;
        EventHelpers.TryExtractSlug(raw, out var slug);
        var name = string.IsNullOrWhiteSpace(slug) ? "" : athleteName(slug);
        EventHelpers.TryExtractPrev(raw, out var previousSlug);
        var previous = string.IsNullOrWhiteSpace(previousSlug) ? null : athleteName(previousSlug);

        switch (item.Type)
        {
            case EventType.General:
            case EventType.CustomEvent:
                return CustomEventMarkup.ToPlainText(raw, mentionResolver: athleteName);
            case EventType.Joined when name.Length > 0:
                return $"{name} joined Longevity World Cup.";
            case EventType.NewRank when name.Length > 0:
                if (!EventHelpers.TryExtractRank(raw, out var rank) || rank is < 1 or > 10) return null;
                return previous is null
                    ? $"{name} took {Ordinal(rank)} place in Ultimate League."
                    : $"{name} took {Ordinal(rank)} place in Ultimate League from {previous}.";
            case EventType.BecamePro when name.Length > 0:
                return $"{name} went Pro.";
            case EventType.BiologicalAgeImproved when name.Length > 0:
                if (!EventHelpers.TryExtractBiologicalAgeImprovement(raw, out var clock, out var from, out var to)) return null;
                return SocialPostText.BuildBiologicalAgeImprovementLine(name, clock, from, to, "").TrimEnd();
            case EventType.CrowdAgeTop10Change when name.Length > 0:
                if (!EventHelpers.TryExtractCrowdAgeTop10Change(raw, out var crowdPlace, out var crowdPrevious, out var age, out var count)) return null;
                return SocialPostText.BuildCrowdAgeTop10Line(name, crowdPlace, crowdPrevious, previous, age, count, null, "").TrimEnd();
            case EventType.AgeImprovementTop10Change when name.Length > 0:
                if (!EventHelpers.TryExtractAgeImprovementTop10Change(raw, out var improvementClock, out var place, out var previousPlace, out var improvement, out _)) return null;
                return SocialPostText.BuildAgeImprovementTop10Line(name, improvementClock, place, previousPlace, previous, improvement, "").TrimEnd();
            case EventType.DonationReceived:
                return DonationReceivedPost.BuildText(raw);
            case EventType.AthleteCountMilestone:
                return XMessageBuilder.ForEventText(item.Type, raw, athleteName);
            case EventType.BadgeAward when name.Length > 0:
                if (!EventHelpers.TryExtractBadgeLabel(raw, out var badge)) return null;
                if (badge.Equals("Podcast", StringComparison.OrdinalIgnoreCase))
                    return $"{name} was featured in a new episode of our podcast.";
                EventHelpers.TryExtractCategory(raw, out var category);
                EventHelpers.TryExtractValue(raw, out var value);
                var league = SocialPostLinks.LeagueDisplay(category, value);
                var context = string.IsNullOrEmpty(league) ? "" : $" in {league}";
                if (EventHelpers.TryExtractPlace(raw, out var badgePlace) && badgePlace > 0)
                    context += $" ({Ordinal(badgePlace)} place)";
                if (EventHelpers.TryExtractSolo(raw, out var solo) && solo)
                    return $"{name} now holds {badge}{context} alone.";
                return previous is null
                    ? $"{name} adds {badge}{context} to the collection."
                    : $"{name} claims {badge}{context} from {previous}.";
            case EventType.LongevitymaxxingChallengeResult:
                if (!EventHelpers.TryExtractPlace(raw, out var challengePlace) || challengePlace < 1) return null;
                if (name.Length == 0) name = Token(raw, "name") ?? "A participant";
                var details = new List<string>();
                if (TryInt(raw, "checkedIn", out var checkedIn) && TryInt(raw, "days", out var days))
                    details.Add($"{checkedIn}/{days} checked-in days");
                if (TryInt(raw, "points", out var points)) details.Add($"{points} points");
                var summary = details.Count == 0 ? "" : " with " + string.Join(" and ", details);
                return $"{name} finished {Ordinal(challengePlace)} in the Longevitymaxxing Challenge{summary}.";
            default:
                // Profile-only result history and malformed structured payloads are not shared announcements.
                return null;
        }
    }

    internal static Func<string, string> AthleteNames(JsonArray athletes)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var athlete in athletes.OfType<JsonObject>())
        {
            var slug = athlete["AthleteSlug"]?.GetValue<string>();
            var name = athlete["DisplayName"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name)) name = athlete["Name"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(slug) && !string.IsNullOrWhiteSpace(name))
                names[AthleteSlug.Normalize(slug)] = name.Trim();
        }
        return slug => names.GetValueOrDefault(AthleteSlug.Normalize(slug))
            ?? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(slug.Replace('_', ' ').Replace('-', ' '));
    }

    private static string? Token(string raw, string key)
    {
        var start = raw.IndexOf(key + "[", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += key.Length + 1;
        var end = raw.IndexOf(']', start);
        return end < 0 ? null : raw[start..end];
    }

    private static bool TryInt(string raw, string key, out int value) =>
        int.TryParse(Token(raw, key), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static string Ordinal(int value) => value.ToString(CultureInfo.InvariantCulture) +
        ((value % 100) is 11 or 12 or 13 ? "th" : (value % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}
