using System.Globalization;

namespace LongevityWorldCup.Website.Business;

public sealed record InstagramPublishProgress(string ContainerId, DateTimeOffset CreatedAtUtc, string? MediaId);

public sealed class InstagramPublishingStore
{
    private readonly DatabaseManager _db;

    public InstagramPublishingStore(DatabaseManager db)
    {
        _db = db;
        db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS InstagramPublishing (
                    EventId TEXT PRIMARY KEY, ContainerId TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL, MediaId TEXT);
                """;
            cmd.ExecuteNonQuery();
        });
    }

    internal InstagramPublishProgress? Get(string eventId) => _db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "SELECT ContainerId, CreatedAtUtc, MediaId FROM InstagramPublishing WHERE EventId = @id;";
        cmd.Parameters.AddWithValue("@id", eventId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new InstagramPublishProgress(reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture), reader.IsDBNull(2) ? null : reader.GetString(2)) : null;
    });

    internal void SaveContainer(string eventId, string containerId, DateTimeOffset now) => _db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "INSERT INTO InstagramPublishing (EventId, ContainerId, CreatedAtUtc) VALUES (@id, @container, @now);";
        cmd.Parameters.AddWithValue("@id", eventId);
        cmd.Parameters.AddWithValue("@container", containerId);
        cmd.Parameters.AddWithValue("@now", now.UtcDateTime.ToString("o"));
        cmd.ExecuteNonQuery();
    });

    internal void SaveMedia(string eventId, string containerId, string mediaId) => _db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "UPDATE InstagramPublishing SET MediaId = @media WHERE EventId = @id AND ContainerId = @container AND MediaId IS NULL;";
        cmd.Parameters.AddWithValue("@id", eventId);
        cmd.Parameters.AddWithValue("@container", containerId);
        cmd.Parameters.AddWithValue("@media", mediaId);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Instagram publishing progress changed unexpectedly.");
    });
}
