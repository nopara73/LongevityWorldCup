using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed class AgentApplicationDataService
{
    private readonly DatabaseManager _db;
    private readonly ILogger<AgentApplicationDataService> _logger;

    public AgentApplicationDataService(DatabaseManager db, ILogger<AgentApplicationDataService> logger)
    {
        _db = db;
        _logger = logger;

        _db.Run(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS AgentApplications (
                    Token      TEXT PRIMARY KEY,
                    Name       TEXT NOT NULL,
                    Email      TEXT,
                    Status     TEXT NOT NULL DEFAULT 'pending',
                    WebhookUrl TEXT,
                    CreatedAt  TEXT NOT NULL,
                    UpdatedAt  TEXT NOT NULL
                )";
            cmd.ExecuteNonQuery();
        });
    }

    public string CreateApplication(string name, string? email, string? webhookUrl)
    {
        var token = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow.ToString("o");

        _db.Run(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AgentApplications (Token, Name, Email, Status, WebhookUrl, CreatedAt, UpdatedAt)
                VALUES (@token, @name, @email, 'pending', @webhookUrl, @now, @now)";
            cmd.Parameters.AddWithValue("@token", token);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@webhookUrl", (object?)webhookUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.ExecuteNonQuery();
        });

        return token;
    }

    public (string? Token, string? Name, string? Status)? GetStatus(string token)
    {
        return _db.Run(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Token, Name, Status FROM AgentApplications WHERE Token = @token";
            cmd.Parameters.AddWithValue("@token", token);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2)
                );
            }
            return ((string?, string?, string?)?)null;
        });
    }

    public (string? Token, string? Status)? LookupByName(string sanitizedName)
    {
        return _db.Run(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Token, Status FROM AgentApplications
                WHERE LOWER(REPLACE(REPLACE(Name, ' ', '_'), '-', '_')) = @name
                   OR Name = @name
                ORDER BY CreatedAt DESC LIMIT 1";
            cmd.Parameters.AddWithValue("@name", sanitizedName.ToLowerInvariant());

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return (reader.GetString(0), reader.GetString(1));
            }
            return ((string?, string?)?)null;
        });
    }

    public (bool Updated, string? WebhookUrl) UpdateStatus(string token, string status)
    {
        var now = DateTime.UtcNow.ToString("o");

        return _db.Run(conn =>
        {
            // Get webhook URL before updating
            string? webhookUrl = null;
            using (var getCmd = conn.CreateCommand())
            {
                getCmd.CommandText = "SELECT WebhookUrl FROM AgentApplications WHERE Token = @token";
                getCmd.Parameters.AddWithValue("@token", token);
                webhookUrl = getCmd.ExecuteScalar() as string;
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE AgentApplications SET Status = @status, UpdatedAt = @now
                WHERE Token = @token";
            cmd.Parameters.AddWithValue("@status", status);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@token", token);

            var rows = cmd.ExecuteNonQuery();
            return (rows > 0, webhookUrl);
        });
    }

    public int CleanupOld(int days = 90)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days).ToString("o");

        return _db.Run(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM AgentApplications WHERE CreatedAt < @cutoff";
            cmd.Parameters.AddWithValue("@cutoff", cutoff);
            var deleted = cmd.ExecuteNonQuery();

            if (deleted > 0)
            {
                _logger.LogInformation("Cleaned up {Count} old agent application tokens", deleted);
            }

            return deleted;
        });
    }
}
