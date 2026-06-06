namespace LongevityWorldCup.Website.Tools;

public static class DonationReminderPost
{
    public const string Url = "https://longevityworldcup.com/#donation-section";
    public const string InfoToken = "donation-reminder[v1] url[https://longevityworldcup.com/#donation-section]";
    public const int MinCooldownDays = 30;
    public const int MaxCooldownDays = 90;
    private static readonly string[] LeadLineOptions =
    [
        "Help fund the Longevity World Cup prize pool:",
        "Bitcoin donations fund the prize pool:",
        "Support the prize pool:",
        "Want to support the competition?",
        "Help keep the prize pool growing:",
        "90% of Bitcoin donations go to the prize pool:",
        "Contribute to the Bitcoin-funded prize pool:",
        "Donations support prizes and operating costs:"
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
