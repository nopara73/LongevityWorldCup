using System.Globalization;

namespace LongevityWorldCup.Website.Business;

public static class FillerPostLogSchedule
{
    public static bool IsOnRandomizedCooldownForType(
        DatabaseManager db,
        string tableName,
        FillerType type,
        int minDays,
        int maxDays,
        DateTime? nowUtc = null)
    {
        if (db is null)
            throw new ArgumentNullException(nameof(db));
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentNullException(nameof(tableName));

        minDays = Math.Max(0, minDays);
        maxDays = Math.Max(minDays, maxDays);

        var lastAt = GetLastPostedAtForType(db, tableName, type);
        if (!lastAt.HasValue)
            return false;

        var cooldownDays = DetermineCooldownDays(tableName, type, lastAt.Value, minDays, maxDays);
        var now = nowUtc ?? DateTime.UtcNow;
        return now - lastAt.Value < TimeSpan.FromDays(cooldownDays);
    }

    private static DateTime? GetLastPostedAtForType(DatabaseManager db, string tableName, FillerType type)
    {
        return db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"""
                SELECT PostedAtUtc
                FROM {tableName}
                WHERE Type = @type
                ORDER BY PostedAtUtc DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@type", (int)type);
            var raw = cmd.ExecuteScalar();
            if (raw is string s &&
                DateTime.TryParse(s, null, DateTimeStyles.RoundtripKind, out var dt))
                return (DateTime?)dt;
            return null;
        });
    }

    private static int DetermineCooldownDays(string tableName, FillerType type, DateTime lastAtUtc, int minDays, int maxDays)
    {
        if (maxDays <= minDays)
            return minDays;

        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{tableName}|{(int)type}|{lastAtUtc.ToUniversalTime():O}");
        var range = maxDays - minDays + 1;
        return minDays + (int)(Fnv1A32(key) % (uint)range);
    }

    private static uint Fnv1A32(string text)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash;
    }
}
