using System.Globalization;

namespace LongevityWorldCup.Website.Tools;

internal static class SocialPostText
{
    internal static string BuildBiologicalAgeImprovementLine(
        string athleteName,
        string clock,
        double fromAge,
        double toAge,
        string athleteUrl)
    {
        var clockLabel = string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase)
            ? "Bortz Age"
            : "pheno age";
        var fromText = fromAge.ToString("0.##", CultureInfo.InvariantCulture);
        var toText = toAge.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{athleteName} improved their {clockLabel} from {fromText} to {toText} years.\n\n{athleteUrl}";
    }

    private static string FormatSignedYears(double years)
    {
        var text = years.ToString("0.#", CultureInfo.InvariantCulture);
        return years > 0 ? $"+{text}" : text;
    }

    internal static string BuildCrowdAgeTop10Line(
        string athleteName,
        int place,
        int? previousPlace,
        string? previousAthlete,
        double crowdAge,
        int crowdCount,
        double? chronologicalAge,
        string athleteUrl)
    {
        var crowdAgeText = crowdAge.ToString("0.#", CultureInfo.InvariantCulture);
        var countText = crowdCount.ToString("N0", CultureInfo.InvariantCulture);
        var movement = BuildCrowdAgeMovement(place, previousPlace);
        var signal = BuildCrowdAgeSignal(crowdAge, chronologicalAge);
        var metricLine = !string.IsNullOrWhiteSpace(signal)
            ? $"{athleteName}'s Crowd Age is {crowdAgeText}, {signal}."
            : $"{athleteName}'s Crowd Age is {crowdAgeText}.";

        return $"{athleteName} {movement} in Crowd Age with {countText} guesses.\n{metricLine}\n\n{athleteUrl}";
    }

    private static string BuildCrowdAgeMovement(int place, int? previousPlace)
    {
        var placeText = CrowdOrdinal(place);
        return previousPlace.HasValue
            ? previousPlace.Value > place
                ? $"climbed from {CrowdOrdinal(previousPlace.Value)} to {placeText}"
                : $"moved from {CrowdOrdinal(previousPlace.Value)} to {placeText}"
            : $"just entered the top 10 at {placeText}";
    }

    private static string? BuildCrowdAgeSignal(double crowdAge, double? chronologicalAge)
    {
        if (!chronologicalAge.HasValue || !double.IsFinite(chronologicalAge.Value))
            return null;

        var difference = crowdAge - chronologicalAge.Value;
        if (!double.IsFinite(difference))
            return null;

        if (Math.Abs(difference) < 0.05)
            return "about the same age as their chronological age";

        var years = Math.Abs(difference).ToString("0.#", CultureInfo.InvariantCulture);
        return difference < 0
            ? $"{years} years below chronological age"
            : $"{years} years above chronological age";
    }

    internal static string BuildAgeImprovementTop10Line(
        string athleteName,
        string clock,
        int place,
        int? previousPlace,
        string? previousAthlete,
        double improvement,
        string athleteUrl)
    {
        var placeText = CrowdOrdinal(place);
        var movement = previousPlace.HasValue
            ? previousPlace.Value > place ? $"climbed from {CrowdOrdinal(previousPlace.Value)} to {placeText}" : $"moved from {CrowdOrdinal(previousPlace.Value)} to {placeText}"
            : place == 1 ? $"took {placeText} place" : $"entered the top 10 at {placeText}";
        var leaderboardName = string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase)
            ? "Bortz Improvement"
            : "Pheno Improvement";
        var improvementText = FormatSignedYears(improvement);

        var lead = !string.IsNullOrWhiteSpace(previousAthlete)
            ? $"{athleteName} {movement} in the {leaderboardName} leaderboard, ahead of {previousAthlete}."
            : $"{athleteName} {movement} in the {leaderboardName} leaderboard.";

        return $"{lead}\n\nImprovement: {improvementText} years from worst to latest eligible result.\n\n{athleteUrl}";
    }

    private static string CrowdOrdinal(int n)
    {
        var suffix = (n % 100) is 11 or 12 or 13
            ? "th"
            : (n % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        return $"{n}{suffix}";
    }
}
