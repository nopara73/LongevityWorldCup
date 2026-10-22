using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MimeKit;

namespace LongevityWorldCup.Website.Business;

// Private, backed-up application database only. No historical submissions are imported.
// The response and its receipt are one FULL-synchronous write, before the response is cached.
public sealed class SubmissionConfirmationStore
{
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    internal const int MaximumAttempts = 8;
    private readonly DatabaseManager _database;

    public SubmissionConfirmationStore(DatabaseManager database)
    {
        _database = database;
        Write(db =>
        {
            using var command = db.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS SubmissionConfirmations (
                    SubmissionKey TEXT PRIMARY KEY,
                    State TEXT NOT NULL,
                    NextAttemptUtc INTEGER NOT NULL,
                    LeaseToken TEXT,
                    LeaseUntilUtc INTEGER,
                    Data TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS IX_SubmissionConfirmations_Due
                    ON SubmissionConfirmations (State, NextAttemptUtc);
                """;
            command.ExecuteNonQuery();
            return 0;
        });
    }

    internal void Complete(string submissionId, string fingerprint, ApplicationSubmissionResponse response,
        MimeMessage? message, DateTimeOffset now)
    {
        var key = Key(submissionId);
        if (message is not null) message.MessageId = $"lwc-submission-{key}@longevityworldcup.com";
        var receipt = new SubmissionConfirmation(key, fingerprint, response,
            now.Add(ApplicationSubmissionRetryStore.CompletedSubmissionLifetime), message?.ToString(),
            message is null ? "Skipped" : "Pending", now);
        Write(db =>
        {
            using var command = db.CreateCommand();
            // Repeated completion cannot change the recipient, response, content or delivery state.
            // Retain the delivery tombstone even after response-cache expiry. Reusing an old
            // submission ID must never reset Sent/Uncertain/Exhausted and resend its receipt.
            command.CommandText = """
                INSERT INTO SubmissionConfirmations (SubmissionKey, State, NextAttemptUtc, Data)
                VALUES ($key, $state, $next, $data)
                ON CONFLICT(SubmissionKey) DO NOTHING;
                """;
            Bind(command, receipt);
            return command.ExecuteNonQuery();
        });
    }

    internal SubmissionConfirmation? Get(string submissionId) => GetByKey(Key(submissionId));

    internal SubmissionConfirmation? GetByKey(string key) => _database.Run(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT Data FROM SubmissionConfirmations WHERE SubmissionKey=$key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() is string json ? Deserialize(json) : null;
    });

    internal IReadOnlyList<string> GetDue(DateTimeOffset now) => _database.Run(db =>
    {
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT SubmissionKey FROM SubmissionConfirmations
            WHERE State IN ('Pending', 'Sending') AND NextAttemptUtc <= $now
                AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc <= $now)
            ORDER BY NextAttemptUtc, SubmissionKey LIMIT 25;
            """;
        command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        using var reader = command.ExecuteReader();
        var keys = new List<string>();
        while (reader.Read()) keys.Add(reader.GetString(0));
        return (IReadOnlyList<string>)keys;
    });

    internal SubmissionConfirmationLease? TryClaim(string key, DateTimeOffset now) => Write(db =>
    {
        using var command = db.CreateCommand();
        var token = Guid.NewGuid().ToString("N");
        command.CommandText = """
            UPDATE SubmissionConfirmations SET LeaseToken=$token, LeaseUntilUtc=$until
            WHERE SubmissionKey=$key AND State IN ('Pending', 'Sending') AND NextAttemptUtc <= $now
                AND (LeaseUntilUtc IS NULL OR LeaseUntilUtc <= $now)
            RETURNING Data;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$token", token);
        command.Parameters.AddWithValue("$until", now.Add(LeaseDuration).ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
        return command.ExecuteScalar() is string json ? new SubmissionConfirmationLease(token, Deserialize(json)) : null;
    });

    internal void Save(SubmissionConfirmationLease lease, SubmissionConfirmation receipt, DateTimeOffset now, bool release = true)
        => Write(db =>
        {
            using var command = db.CreateCommand();
            command.CommandText = """
                UPDATE SubmissionConfirmations SET State=$state, NextAttemptUtc=$next, Data=$data,
                    LeaseToken=CASE WHEN $release THEN NULL ELSE LeaseToken END,
                    LeaseUntilUtc=CASE WHEN $release THEN NULL ELSE LeaseUntilUtc END
                WHERE SubmissionKey=$key AND LeaseToken=$token AND LeaseUntilUtc > $now;
                """;
            Bind(command, receipt);
            command.Parameters.AddWithValue("$release", release);
            command.Parameters.AddWithValue("$token", lease.Token);
            command.Parameters.AddWithValue("$now", now.ToUnixTimeMilliseconds());
            if (command.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Submission confirmation lease expired or changed.");
            return 0;
        });

    private T Write<T>(Func<SqliteConnection, T> work) => _database.Run(db =>
    {
        using var pragma = db.CreateCommand();
        pragma.CommandText = "PRAGMA synchronous;";
        var previous = Convert.ToInt32(pragma.ExecuteScalar());
        pragma.CommandText = "PRAGMA synchronous=FULL;";
        pragma.ExecuteNonQuery();
        try { return work(db); }
        finally
        {
            pragma.CommandText = $"PRAGMA synchronous={previous};";
            pragma.ExecuteNonQuery();
        }
    });

    private static void Bind(SqliteCommand command, SubmissionConfirmation receipt)
    {
        command.Parameters.AddWithValue("$key", receipt.SubmissionKey);
        command.Parameters.AddWithValue("$state", receipt.State);
        command.Parameters.AddWithValue("$next", receipt.NextAttemptUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(receipt));
    }

    private static string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
    private static SubmissionConfirmation Deserialize(string json) => JsonSerializer.Deserialize<SubmissionConfirmation>(json)
        ?? throw new InvalidDataException("Invalid submission confirmation record.");
}

internal sealed record SubmissionConfirmation(string SubmissionKey, string RequestFingerprint,
    ApplicationSubmissionResponse Response, DateTimeOffset ExpiresAtUtc, string? Message,
    string State, DateTimeOffset NextAttemptUtc, int Attempts = 0, string? LastError = null, DateTimeOffset? SentAtUtc = null);

internal sealed record SubmissionConfirmationLease(string Token, SubmissionConfirmation Receipt);
