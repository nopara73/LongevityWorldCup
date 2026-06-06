namespace LongevityWorldCup.Website.Tools;

public static class RulesetReminderPost
{
    public const string Url = "https://longevityworldcup.com/ruleset";
    public const string InfoToken = "ruleset[v1] url[https://longevityworldcup.com/ruleset]";
    public const int MinCooldownDays = 60;
    public const int MaxCooldownDays = 120;
    private static readonly string[] LeadLineOptions =
    [
        "How the competition works:",
        "The Ruleset:",
        "For anyone wondering how rankings work:",
        "How athletes are ranked:",
        "The rules behind the leaderboard:",
        "What counts, what does not:",
        "Pro, Amateur, seasons, clocks, prizes:",
        "The current Longevity World Cup Ruleset:",
        "Start here if the leaderboard looks confusing:",
        "The scoring rules are public:",
        "The ranking rules are public:",
        "The Longevity World Cup rulebook:",
        "How the Ultimate League works:",
        "What the competition measures:",
        "The rules of longevity as a sport:",
        "How results become rankings:",
        "The heart of a game lies within its rules",
        "Great play emerges from clear constraints"
    ];
    public static IReadOnlyList<string> LeadLines => LeadLineOptions;

    public static string BuildText()
    {
        return BuildText(Random.Shared);
    }

    public static string BuildText(Random random)
    {
        if (random is null)
            throw new ArgumentNullException(nameof(random));

        var lead = LeadLineOptions[random.Next(LeadLineOptions.Length)];
        return $"{lead}\n{Url}";
    }
}
