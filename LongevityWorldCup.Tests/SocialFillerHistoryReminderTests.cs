using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialFillerHistoryReminderTests
{
    [Fact]
    public void HistoryDocumentReminderBuildsSimplePlatformSafeCopy()
    {
        var shared = HistoryDocumentReminderPost.BuildText(new Random(2));

        Assert.Contains(shared, AllowedHistoryReminderTexts());
        Assert.Equal(
            new[]
            {
                "How longevity became a sport:",
                "For anyone new here:",
                "A short history of longevity as a sport:",
                "The backstory:",
                "Longevity as a sport did not appear out of nowhere:"
            },
            HistoryDocumentReminderPost.LeadLines);
        foreach (var lead in HistoryDocumentReminderPost.LeadLines)
        {
            var variant = $"{lead}\n{HistoryDocumentReminderPost.Url}";
            Assert.True(variant.Length <= 280);
            Assert.True(variant.Length <= 500);
        }
    }

    [Fact]
    public void RulesetReminderBuildsSimplePlatformSafeCopy()
    {
        var shared = RulesetReminderPost.BuildText(new Random(2));

        Assert.Contains(shared, AllowedRulesetReminderTexts());
        Assert.Equal(
            new[]
            {
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
            },
            RulesetReminderPost.LeadLines);
        foreach (var lead in RulesetReminderPost.LeadLines)
        {
            var variant = $"{lead}\n{RulesetReminderPost.Url}";
            Assert.True(variant.Length <= 280);
            Assert.True(variant.Length <= 500);
        }
    }

    [Fact]
    public void GitHubRepositoryReminderBuildsSimplePlatformSafeCopy()
    {
        var shared = GitHubRepositoryReminderPost.BuildText(new Random(2));

        Assert.Contains(shared, AllowedGitHubRepositoryReminderTexts());
        Assert.Equal(
            new[]
            {
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
            },
            GitHubRepositoryReminderPost.LeadLines);
        foreach (var lead in GitHubRepositoryReminderPost.LeadLines)
        {
            var variant = $"{lead}\n{GitHubRepositoryReminderPost.Url}";
            Assert.True(variant.Length <= 280);
            Assert.True(variant.Length <= 500);
        }
    }

    [Fact]
    public void DonationReminderBuildsSimplePlatformSafeCopy()
    {
        var shared = DonationReminderPost.BuildText(new Random(2));

        Assert.Contains(shared, AllowedDonationReminderTexts());
        Assert.Equal(
            new[]
            {
                "Help fund the Longevity World Cup prize pool:",
                "Bitcoin donations fund the prize pool:",
                "Support the prize pool:",
                "Want to support the competition?",
                "Help keep the prize pool growing:",
                "90% of Bitcoin donations go to the prize pool:",
                "Contribute to the Bitcoin-funded prize pool:",
                "Donations support prizes and operating costs:"
            },
            DonationReminderPost.LeadLines);
        foreach (var lead in DonationReminderPost.LeadLines)
        {
            var variant = $"{lead}\n{DonationReminderPost.Url}";
            Assert.True(variant.Length <= 280);
            Assert.True(variant.Length <= 500);
        }
    }

    [Fact]
    public void HistoryDocumentReminderPlatformBuildersUseAllowedVariant()
    {
        var x = XMessageBuilder.ForFiller(FillerType.HistoryDocument, "", slug => slug);
        var threads = ThreadsMessageBuilder.ForFiller(FillerType.HistoryDocument, "", slug => slug);

        Assert.Contains(x, AllowedHistoryReminderTexts());
        Assert.Contains(threads, AllowedHistoryReminderTexts());
    }

    [Fact]
    public void RulesetReminderPlatformBuildersUseAllowedVariant()
    {
        var x = XMessageBuilder.ForFiller(FillerType.Ruleset, "", slug => slug);
        var threads = ThreadsMessageBuilder.ForFiller(FillerType.Ruleset, "", slug => slug);

        Assert.Contains(x, AllowedRulesetReminderTexts());
        Assert.Contains(threads, AllowedRulesetReminderTexts());
    }

    [Fact]
    public void GitHubRepositoryReminderPlatformBuildersUseAllowedVariant()
    {
        var x = XMessageBuilder.ForFiller(FillerType.GitHubRepository, "", slug => slug);
        var threads = ThreadsMessageBuilder.ForFiller(FillerType.GitHubRepository, "", slug => slug);

        Assert.Contains(x, AllowedGitHubRepositoryReminderTexts());
        Assert.Contains(threads, AllowedGitHubRepositoryReminderTexts());
    }

    [Fact]
    public void DonationReminderPlatformBuildersUseAllowedVariant()
    {
        var x = XMessageBuilder.ForFiller(FillerType.Donation, "", slug => slug);
        var threads = ThreadsMessageBuilder.ForFiller(FillerType.Donation, "", slug => slug);

        Assert.Contains(x, AllowedDonationReminderTexts());
        Assert.Contains(threads, AllowedDonationReminderTexts());
    }

    [Fact]
    public void PeriodicRemindersAppearInEachPlatformFillerRotation()
    {
        using var fixture = TempDatabaseFixture.Create();
        var x = new XFillerPostLogService(fixture.Database);
        var threads = new ThreadsFillerPostLogService(fixture.Database);
        var facebook = new FacebookFillerPostLogService(fixture.Database);

        Assert.Contains(x.GetSuggestedFillersOrdered(), item => item.Type == FillerType.HistoryDocument);
        Assert.Contains(x.GetSuggestedFillersOrdered(), item => item.Type == FillerType.Ruleset);
        Assert.Contains(x.GetSuggestedFillersOrdered(), item => item.Type == FillerType.GitHubRepository);
        Assert.Contains(x.GetSuggestedFillersOrdered(), item => item.Type == FillerType.Donation);
        Assert.Contains(threads.GetSuggestedFillersOrdered(), item => item.Type == FillerType.HistoryDocument);
        Assert.Contains(threads.GetSuggestedFillersOrdered(), item => item.Type == FillerType.Ruleset);
        Assert.Contains(threads.GetSuggestedFillersOrdered(), item => item.Type == FillerType.GitHubRepository);
        Assert.Contains(threads.GetSuggestedFillersOrdered(), item => item.Type == FillerType.Donation);
        Assert.Contains(facebook.GetSuggestedFillersOrdered(), item => item.Type == FillerType.HistoryDocument);
        Assert.Contains(facebook.GetSuggestedFillersOrdered(), item => item.Type == FillerType.Ruleset);
        Assert.Contains(facebook.GetSuggestedFillersOrdered(), item => item.Type == FillerType.GitHubRepository);
        Assert.Contains(facebook.GetSuggestedFillersOrdered(), item => item.Type == FillerType.Donation);
    }

    [Fact]
    public void HistoryDocumentReminderUsesTwoToFourMonthRandomizedCooldown()
    {
        using var fixture = TempDatabaseFixture.Create();
        var log = new XFillerPostLogService(fixture.Database);
        var postedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        log.LogPost(postedAt, FillerType.HistoryDocument, HistoryDocumentReminderPost.InfoToken);

        Assert.True(log.IsOnRandomizedCooldownForType(
            FillerType.HistoryDocument,
            HistoryDocumentReminderPost.MinCooldownDays,
            HistoryDocumentReminderPost.MaxCooldownDays,
            postedAt.AddDays(59)));
        Assert.False(log.IsOnRandomizedCooldownForType(
            FillerType.HistoryDocument,
            HistoryDocumentReminderPost.MinCooldownDays,
            HistoryDocumentReminderPost.MaxCooldownDays,
            postedAt.AddDays(121)));
    }

    [Fact]
    public void RulesetReminderUsesTwoToFourMonthRandomizedCooldown()
    {
        using var fixture = TempDatabaseFixture.Create();
        var log = new XFillerPostLogService(fixture.Database);
        var postedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        log.LogPost(postedAt, FillerType.Ruleset, RulesetReminderPost.InfoToken);

        Assert.True(log.IsOnRandomizedCooldownForType(
            FillerType.Ruleset,
            RulesetReminderPost.MinCooldownDays,
            RulesetReminderPost.MaxCooldownDays,
            postedAt.AddDays(59)));
        Assert.False(log.IsOnRandomizedCooldownForType(
            FillerType.Ruleset,
            RulesetReminderPost.MinCooldownDays,
            RulesetReminderPost.MaxCooldownDays,
            postedAt.AddDays(121)));
    }

    [Fact]
    public void GitHubRepositoryReminderUsesTwoToFourMonthRandomizedCooldown()
    {
        using var fixture = TempDatabaseFixture.Create();
        var log = new XFillerPostLogService(fixture.Database);
        var postedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        log.LogPost(postedAt, FillerType.GitHubRepository, GitHubRepositoryReminderPost.InfoToken);

        Assert.True(log.IsOnRandomizedCooldownForType(
            FillerType.GitHubRepository,
            GitHubRepositoryReminderPost.MinCooldownDays,
            GitHubRepositoryReminderPost.MaxCooldownDays,
            postedAt.AddDays(59)));
        Assert.False(log.IsOnRandomizedCooldownForType(
            FillerType.GitHubRepository,
            GitHubRepositoryReminderPost.MinCooldownDays,
            GitHubRepositoryReminderPost.MaxCooldownDays,
            postedAt.AddDays(121)));
    }

    [Fact]
    public void DonationReminderUsesOneToThreeMonthRandomizedCooldown()
    {
        using var fixture = TempDatabaseFixture.Create();
        var log = new XFillerPostLogService(fixture.Database);
        var postedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        log.LogPost(postedAt, FillerType.Donation, DonationReminderPost.InfoToken);

        Assert.True(log.IsOnRandomizedCooldownForType(
            FillerType.Donation,
            DonationReminderPost.MinCooldownDays,
            DonationReminderPost.MaxCooldownDays,
            postedAt.AddDays(29)));
        Assert.False(log.IsOnRandomizedCooldownForType(
            FillerType.Donation,
            DonationReminderPost.MinCooldownDays,
            DonationReminderPost.MaxCooldownDays,
            postedAt.AddDays(91)));
    }

    private sealed class TempDatabaseFixture : IDisposable
    {
        private readonly string _root;

        private TempDatabaseFixture(string root, DatabaseManager database)
        {
            _root = root;
            Database = database;
        }

        public DatabaseManager Database { get; }

        public static TempDatabaseFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "lwc-social-filler-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TempDatabaseFixture(root, new DatabaseManager(dbPath: Path.Combine(root, "test.db")));
        }

        public void Dispose()
        {
            Database.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static IReadOnlyList<string> AllowedHistoryReminderTexts()
    {
        return HistoryDocumentReminderPost.LeadLines
            .Select(lead => $"{lead}\n{HistoryDocumentReminderPost.Url}")
            .ToArray();
    }

    private static IReadOnlyList<string> AllowedRulesetReminderTexts()
    {
        return RulesetReminderPost.LeadLines
            .Select(lead => $"{lead}\n{RulesetReminderPost.Url}")
            .ToArray();
    }

    private static IReadOnlyList<string> AllowedGitHubRepositoryReminderTexts()
    {
        return GitHubRepositoryReminderPost.LeadLines
            .Select(lead => $"{lead}\n{GitHubRepositoryReminderPost.Url}")
            .ToArray();
    }

    private static IReadOnlyList<string> AllowedDonationReminderTexts()
    {
        return DonationReminderPost.LeadLines
            .Select(lead => $"{lead}\n{DonationReminderPost.Url}")
            .ToArray();
    }
}
