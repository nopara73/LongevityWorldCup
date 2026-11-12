using System.Globalization;
using System.Text.Json;

namespace LongevityWorldCup.Website.Business;

public sealed partial class EventDataService
{
    private sealed record PhenoAgeSnapshot(
        IReadOnlyList<BiologicalAgeLeaderboardEntry> Standings,
        IReadOnlyDictionary<string, string> Signatures);

    // The first snapshot is a silent baseline, including on an existing installation.
    // Persist the next snapshot and its events together so reloads cannot replay a move.
    internal void SyncPhenoAgeTop10Events(
        IReadOnlyList<BiologicalAgeLeaderboardEntry> standings,
        IReadOnlyDictionary<string, string> signatures,
        IReadOnlySet<string> correctedSlugs,
        DateTime occurredAtUtc)
    {
        var created = _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();
            using var command = sqlite.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS PhenoAgeRankSnapshot (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    Standings TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            command.CommandText = "SELECT Standings FROM PhenoAgeRankSnapshot WHERE Id = 1;";
            var previousJson = command.ExecuteScalar() as string;
            var currentJson = JsonSerializer.Serialize(new PhenoAgeSnapshot(standings, signatures));
            if (previousJson == currentJson)
            {
                tx.Commit();
                return 0;
            }

            var count = 0;
            if (previousJson is not null)
            {
                var previous = JsonSerializer.Deserialize<PhenoAgeSnapshot>(previousJson)
                    ?? throw new InvalidDataException("Invalid pheno age rank snapshot.");
                var bySlug = previous.Standings.ToDictionary(row => row.Slug, StringComparer.OrdinalIgnoreCase);
                var byRank = previous.Standings.ToDictionary(row => row.Rank);
                foreach (var row in standings.Where(row => row.Rank is >= 1 and <= 10))
                {
                    if (correctedSlugs.Contains(row.Slug) ||
                        !signatures.TryGetValue(row.Slug, out var signature) ||
                        (previous.Signatures.TryGetValue(row.Slug, out var oldSignature) && oldSignature == signature))
                        continue;
                    bySlug.TryGetValue(row.Slug, out var before);
                    if (before is not null && row.Rank >= before.Rank) continue;

                    var parts = new List<string>
                    {
                        $"slug[{NormalizeEventToken(row.Slug)}]",
                        $"place[{row.Rank}]",
                        $"ageReduction[{row.AgeReduction.ToString("0.##", CultureInfo.InvariantCulture)}]"
                    };
                    if (before?.Rank is >= 1 and <= 10)
                        parts.Add($"prevPlace[{before.Rank}]");
                    if (byRank.TryGetValue(row.Rank, out var holder) &&
                        !string.Equals(holder.Slug, row.Slug, StringComparison.OrdinalIgnoreCase))
                        parts.Add($"prev[{NormalizeEventToken(holder.Slug)}]");

                    command.Parameters.Clear();
                    command.CommandText = """
                        INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance)
                        VALUES (@id, @type, @text, @at, 10);
                        """;
                    command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                    command.Parameters.AddWithValue("@type", (int)EventType.PhenoAgeTop10Change);
                    command.Parameters.AddWithValue("@text", string.Join(" ", parts));
                    command.Parameters.AddWithValue("@at", EnsureUtc(occurredAtUtc).ToString("o"));
                    command.ExecuteNonQuery();
                    count++;
                }
            }

            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO PhenoAgeRankSnapshot (Id, Standings) VALUES (1, @standings)
                ON CONFLICT(Id) DO UPDATE SET Standings = excluded.Standings;
                """;
            command.Parameters.AddWithValue("@standings", currentJson);
            command.ExecuteNonQuery();
            tx.Commit();
            return count;
        });
        if (created > 0) ReloadIntoCache();
    }
}
