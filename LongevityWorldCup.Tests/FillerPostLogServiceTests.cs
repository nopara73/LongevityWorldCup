using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class FillerPostLogServiceTests
{
    private static readonly DateTime PostedAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("X")]
    [InlineData("Threads")]
    [InlineData("Facebook")]
    public void LegacyLogMigrationPreservesHistoryAndSupportsSubjectPosts(string platform)
    {
        using var fixture = new LogFixture(platform);
        fixture.Execute($"CREATE TABLE {fixture.TableName} (PostedAtUtc TEXT NOT NULL, Type INTEGER NOT NULL, Text TEXT NOT NULL);");
        fixture.InsertRaw(PostedAt.ToString("o"), FillerType.Donation, "donation-reminder[original]");

        var log = fixture.CreateLog();
        fixture.CreateLog(); // Reopening an already migrated log must also preserve its rows.
        Assert.True(log.IsUnchanged(FillerType.Donation, "", "donation-reminder[original]"));
        Assert.Equal(1L, fixture.CountRows());

        log.LogSubjectPost(PostedAt, "event", " athlete ");
        Assert.Equal(2L, fixture.CountRows());
        Assert.True(log.IsSubjectOnCooldown("athlete", TimeSpan.FromDays(1), PostedAt));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Threads")]
    [InlineData("Facebook")]
    public void SubjectCooldownUsesLatestMatchingPostAndExpiresAtBoundary(string platform)
    {
        using var fixture = new LogFixture(platform);
        var log = fixture.CreateLog();
        log.LogSubjectPost(PostedAt, "ignored", " ");
        Assert.Equal(0L, fixture.CountRows());

        log.LogSubjectPost(PostedAt, "event", " athlete ");
        log.LogPost(PostedAt.AddDays(2), FillerType.Newcomers, "slugs[athlete]", " athlete ");
        log.LogSubjectPost(PostedAt.AddDays(10), "unrelated", "other");

        var cooldown = TimeSpan.FromDays(3);
        Assert.True(log.IsSubjectOnCooldown(" athlete ", cooldown, PostedAt.AddDays(5).AddTicks(-1)));
        Assert.False(log.IsSubjectOnCooldown("athlete", cooldown, PostedAt.AddDays(5)));
        Assert.False(log.IsSubjectOnCooldown("ATHLETE", cooldown, PostedAt.AddDays(2)));
        Assert.False(log.IsSubjectOnCooldown("athlete", TimeSpan.Zero, PostedAt.AddDays(2)));
        Assert.False(log.IsSubjectOnCooldown(" ", cooldown, PostedAt.AddDays(2)));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Threads")]
    [InlineData("Facebook")]
    public void HistoryComparisonFindsLatestMatchingOptionRatherThanLatestType(string platform)
    {
        using var fixture = new LogFixture(platform);
        var log = fixture.CreateLog();
        const string payload = "league[ultimate]";
        const string latest = "league[ultimate] slugs[new]";
        log.LogPost(PostedAt, FillerType.Top3Leaderboard, "league[ultimate] slugs[old]", null);
        log.LogPost(PostedAt.AddDays(1), FillerType.Top3Leaderboard, latest, null);
        log.LogPost(PostedAt.AddDays(2), FillerType.Top3Leaderboard, "league[amateur] slugs[other]", null);

        Assert.True(log.IsUnchanged(FillerType.Top3Leaderboard, payload, latest));
        Assert.False(log.IsUnchanged(FillerType.Top3Leaderboard, payload, "league[ultimate] slugs[old]"));
        Assert.False(log.IsUnchanged(FillerType.Top3Leaderboard, payload, latest.ToUpperInvariant()));
        Assert.False(log.IsUnchanged(FillerType.Top3Leaderboard, payload, " "));
    }

    [Theory]
    [InlineData("X", true)]
    [InlineData("Threads", false)]
    [InlineData("Facebook", false)]
    public void TokenMatchingRetainsPlatformCaseRules(string platform, bool matchesUppercase)
    {
        using var fixture = new LogFixture(platform);
        var log = fixture.CreateLog();
        const string token = "LEAGUE[ULTIMATE] slugs[new]";
        log.LogPost(PostedAt, FillerType.Top3Leaderboard, token, null);

        Assert.Equal(matchesUppercase, log.IsUnchanged(FillerType.Top3Leaderboard, "league[ultimate]", token));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Threads")]
    [InlineData("Facebook")]
    public void RotationKeepsUnpostedOptionsStableAndIgnoresInvalidHistory(string platform)
    {
        using var fixture = new LogFixture(platform);
        var log = fixture.CreateLog();
        var initial = log.GetSuggestedFillers();
        var postedOption = initial[0];
        log.LogPost(PostedAt, postedOption.Type, postedOption.PayloadText + " slugs[new]", null);
        fixture.InsertRaw("not-a-date", FillerType.Ruleset, "ruleset[invalid-date]");
        fixture.InsertRaw(PostedAt.AddDays(1).ToString("o"), (FillerType)999, "unknown[type]");

        var ordered = log.GetSuggestedFillers();
        Assert.Equal(initial.Skip(1).Append(postedOption), ordered);
    }

    private sealed record LogApi(
        Action<DateTime, FillerType, string, string?> LogPost,
        Action<DateTime, string, string?> LogSubjectPost,
        Func<string, TimeSpan, DateTime?, bool> IsSubjectOnCooldown,
        Func<FillerType, string, string, bool> IsUnchanged,
        Func<IReadOnlyList<(FillerType Type, string PayloadText)>> GetSuggestedFillers);

    private sealed class LogFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "lwc-social-log-" + Guid.NewGuid().ToString("N"));
        private readonly string _platform;
        private readonly DatabaseManager _database;

        public LogFixture(string platform)
        {
            _platform = platform;
            TableName = platform + "FillerPostLog";
            Directory.CreateDirectory(_root);
            _database = new DatabaseManager(dbPath: Path.Combine(_root, "test.db"));
        }

        public string TableName { get; }

        public LogApi CreateLog()
        {
            if (_platform == "X")
            {
                var log = new XFillerPostLogService(_database);
                return new(log.LogPost, log.LogSubjectPost, log.IsSubjectOnCooldown, log.IsUnchangedFromLastForOption, log.GetSuggestedFillersOrdered);
            }
            if (_platform == "Threads")
            {
                var log = new ThreadsFillerPostLogService(_database);
                return new(log.LogPost, log.LogSubjectPost, log.IsSubjectOnCooldown, log.IsUnchangedFromLastForOption, log.GetSuggestedFillersOrdered);
            }
            var facebook = new FacebookFillerPostLogService(_database);
            return new(facebook.LogPost, facebook.LogSubjectPost, facebook.IsSubjectOnCooldown, facebook.IsUnchangedFromLastForOption, facebook.GetSuggestedFillersOrdered);
        }

        public void Execute(string sql) => _database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        });

        public void InsertRaw(string timestamp, FillerType type, string text) => _database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = $"INSERT INTO {TableName} (PostedAtUtc, Type, Text) VALUES (@at, @type, @text);";
            command.Parameters.AddWithValue("@at", timestamp);
            command.Parameters.AddWithValue("@type", (int)type);
            command.Parameters.AddWithValue("@text", text);
            command.ExecuteNonQuery();
        });

        public long CountRows() => _database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM {TableName};";
            return (long)command.ExecuteScalar()!;
        });

        public void Dispose()
        {
            _database.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }
}
