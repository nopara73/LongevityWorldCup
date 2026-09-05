namespace LongevityWorldCup.Website.Business;

internal static class FillerPostLogStore
{
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
}
