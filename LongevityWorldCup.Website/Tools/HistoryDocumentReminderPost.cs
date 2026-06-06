namespace LongevityWorldCup.Website.Tools;

public static class HistoryDocumentReminderPost
{
    public const string Url = "https://longevityworldcup.com/history";
    public const string InfoToken = "history-document[v1] url[https://longevityworldcup.com/history]";
    public const int MinCooldownDays = 60;
    public const int MaxCooldownDays = 120;
    private static readonly string[] LeadLineOptions =
    [
        "How longevity became a sport:",
        "For anyone new here:",
        "A short history of longevity as a sport:",
        "The backstory:",
        "Longevity as a sport did not appear out of nowhere:"
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
