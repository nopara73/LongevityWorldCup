using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class FillerPostLogScheduleTests
{
    [Fact]
    public void IsOnRandomizedCooldownForType_ReturnsFalseWhenTypeHasNoRows()
    {
        using var fixture = TempDatabaseFixture.Create();
        fixture.CreateLogTable("FillerLog");

        var result = FillerPostLogSchedule.IsOnRandomizedCooldownForType(
            fixture.Database,
            "FillerLog",
            FillerType.Donation,
            minDays: 30,
            maxDays: 30,
            nowUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.False(result);
    }

    [Fact]
    public void IsOnRandomizedCooldownForType_UsesLatestRowForRequestedType()
    {
        using var fixture = TempDatabaseFixture.Create();
        fixture.CreateLogTable("FillerLog");
        fixture.InsertLogRow("FillerLog", FillerType.Donation, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        fixture.InsertLogRow("FillerLog", FillerType.Donation, new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc));
        fixture.InsertLogRow("FillerLog", FillerType.Ruleset, new DateTime(2026, 2, 20, 0, 0, 0, DateTimeKind.Utc));

        var result = FillerPostLogSchedule.IsOnRandomizedCooldownForType(
            fixture.Database,
            "FillerLog",
            FillerType.Donation,
            minDays: 30,
            maxDays: 30,
            nowUtc: new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(result);
    }

    [Fact]
    public void IsOnRandomizedCooldownForType_ExpiresAtFixedCooldownBoundary()
    {
        using var fixture = TempDatabaseFixture.Create();
        fixture.CreateLogTable("FillerLog");
        var postedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fixture.InsertLogRow("FillerLog", FillerType.Donation, postedAt);

        Assert.True(FillerPostLogSchedule.IsOnRandomizedCooldownForType(
            fixture.Database,
            "FillerLog",
            FillerType.Donation,
            minDays: 30,
            maxDays: 30,
            nowUtc: postedAt.AddDays(29).AddHours(23)));
        Assert.False(FillerPostLogSchedule.IsOnRandomizedCooldownForType(
            fixture.Database,
            "FillerLog",
            FillerType.Donation,
            minDays: 30,
            maxDays: 30,
            nowUtc: postedAt.AddDays(30)));
    }

    [Fact]
    public void IsOnRandomizedCooldownForType_IgnoresMalformedTimestamps()
    {
        using var fixture = TempDatabaseFixture.Create();
        fixture.CreateLogTable("FillerLog");
        fixture.InsertRawLogRow("FillerLog", FillerType.Donation, "not-a-date");

        var result = FillerPostLogSchedule.IsOnRandomizedCooldownForType(
            fixture.Database,
            "FillerLog",
            FillerType.Donation,
            minDays: 30,
            maxDays: 30,
            nowUtc: new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.False(result);
    }

    [Fact]
    public void IsOnRandomizedCooldownForType_NormalizesNegativeAndInvertedBounds()
    {
        using var fixture = TempDatabaseFixture.Create();
        fixture.CreateLogTable("FillerLog");
        var postedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fixture.InsertLogRow("FillerLog", FillerType.Donation, postedAt);

        var result = FillerPostLogSchedule.IsOnRandomizedCooldownForType(
            fixture.Database,
            "FillerLog",
            FillerType.Donation,
            minDays: -10,
            maxDays: -20,
            nowUtc: postedAt);

        Assert.False(result);
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
            var root = Path.Combine(Path.GetTempPath(), "lwc-filler-schedule-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TempDatabaseFixture(root, new DatabaseManager(dbPath: Path.Combine(root, "test.db")));
        }

        public void CreateLogTable(string tableName)
        {
            Database.Run(sqlite =>
            {
                using var command = sqlite.CreateCommand();
                command.CommandText = $"""
                    CREATE TABLE {tableName} (
                        PostedAtUtc TEXT NOT NULL,
                        Type INTEGER NOT NULL
                    );
                    """;
                command.ExecuteNonQuery();
            });
        }

        public void InsertLogRow(string tableName, FillerType type, DateTime postedAtUtc)
        {
            InsertRawLogRow(tableName, type, postedAtUtc.ToString("o"));
        }

        public void InsertRawLogRow(string tableName, FillerType type, string postedAtUtc)
        {
            Database.Run(sqlite =>
            {
                using var command = sqlite.CreateCommand();
                command.CommandText = $"INSERT INTO {tableName} (PostedAtUtc, Type) VALUES (@postedAtUtc, @type);";
                command.Parameters.AddWithValue("@postedAtUtc", postedAtUtc);
                command.Parameters.AddWithValue("@type", (int)type);
                command.ExecuteNonQuery();
            });
        }

        public void Dispose()
        {
            Database.Dispose();
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
