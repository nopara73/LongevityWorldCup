using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

internal static class FillerPostLogStore
{
    internal static void EnsureCreated(DatabaseManager db, string tableName)
    {
        db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {tableName} (
                    PostedAtUtc TEXT NOT NULL,
                    Type       INTEGER NOT NULL,
                    Text       TEXT NOT NULL,
                    SubjectSlug TEXT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            cmd.CommandText = $"ALTER TABLE {tableName} ADD COLUMN SubjectSlug TEXT NULL;";
            try
            {
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
            }

            cmd.CommandText = $"CREATE INDEX IF NOT EXISTS IX_{tableName}_Type_PostedAtUtc ON {tableName}(Type, PostedAtUtc);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = $"CREATE INDEX IF NOT EXISTS IX_{tableName}_SubjectSlug_PostedAtUtc ON {tableName}(SubjectSlug, PostedAtUtc);";
            cmd.ExecuteNonQuery();
        });
    }

    internal static void LogPost(DatabaseManager db, string tableName, DateTime postedAtUtc, int type, string text, string? subjectSlug)
    {
        var infoToken = text ?? "";
        db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"INSERT INTO {tableName} (PostedAtUtc, Type, Text, SubjectSlug) VALUES (@at, @type, @text, @subjectSlug)";
            cmd.Parameters.AddWithValue("@at", postedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@type", type);
            cmd.Parameters.AddWithValue("@text", infoToken);
            cmd.Parameters.AddWithValue("@subjectSlug", string.IsNullOrWhiteSpace(subjectSlug) ? DBNull.Value : subjectSlug.Trim());
            cmd.ExecuteNonQuery();
        });
    }

    internal static bool IsUnchangedFromLastForOption(
        DatabaseManager db,
        string tableName,
        FillerType type,
        string payloadText,
        string infoToken,
        Func<FillerType, string, string, bool> tokenBelongsToOption)
    {
        var payload = payloadText ?? "";
        var token = infoToken ?? "";
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var candidates = db.Run(sqlite =>
        {
            var rows = new List<string>();
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"""
                SELECT Text
                FROM {tableName}
                WHERE Type = @type
                ORDER BY PostedAtUtc DESC
                """;
            cmd.Parameters.AddWithValue("@type", (int)type);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var text = r.IsDBNull(0) ? "" : r.GetString(0);
                rows.Add(text);
            }
            return rows;
        });

        var last = candidates.FirstOrDefault(text => tokenBelongsToOption(type, payload, text));
        if (string.IsNullOrWhiteSpace(last))
            return false;
        return string.Equals(last, token, StringComparison.Ordinal);
    }
}
