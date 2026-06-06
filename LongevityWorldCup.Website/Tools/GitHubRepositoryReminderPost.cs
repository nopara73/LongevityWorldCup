namespace LongevityWorldCup.Website.Tools;

public static class GitHubRepositoryReminderPost
{
    public const string Url = "https://github.com/nopara73/LongevityWorldCup";
    public const string InfoToken = "github-repository[v1] url[https://github.com/nopara73/LongevityWorldCup]";
    public const int MinCooldownDays = 60;
    public const int MaxCooldownDays = 120;
    private static readonly string[] LeadLineOptions =
    [
        "The Longevity World Cup website is free and open source.",
        "The leaderboard is public. The code is too.",
        "Longevity World Cup is open source.",
        "The Longevity World Cup website can be inspected, forked, and improved.",
        "The code behind Longevity World Cup is public.",
        "Free and open source, including the website.",
        "The Longevity World Cup website is open for contributions.",
        "Want to see how the site works? The code is public.",
        "Longevity World Cup is built in the open.",
        "The website is free software. Fork it, modify it, contribute.",
        "The project is open source for anyone who wants to inspect or improve it.",
        "Public competition, public rules, public code.",
        "The Longevity World Cup website is available to read, fork, and contribute to.",
        "The website code is open source.",
        "You can fork the Longevity World Cup website.",
        "The code is public because the competition should be inspectable.",
        "Longevity World Cup is not a black box. The website code is public.",
        "The website is open source. Contributions are welcome.",
        "The Longevity World Cup project is free to fork and modify.",
        "The code behind the website lives on GitHub.",
        "Open rules. Open leaderboard. Open source website."
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
