using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed class CycleParticipationDataService
{
    private readonly DatabaseManager _db;
    private readonly ILogger<CycleParticipationDataService> _logger;

    public CycleParticipationDataService(DatabaseManager db, ILogger<CycleParticipationDataService> logger)
    {
        _db = db;
        _logger = logger;

        _db.Run(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS CycleParticipation (
                    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    CycleId           TEXT NOT NULL,
                    AthleteSlug       TEXT NOT NULL,
                    ParticipationType TEXT NOT NULL,
                    SubmittedAt       TEXT NOT NULL,
                    Details           TEXT,
                    AgentToken        TEXT
                )";
            cmd.ExecuteNonQuery();
        });
    }

    /// <summary>
    /// Returns the current cycle/season ID based on the date.
    /// The 2025 season closed on 2026-01-16. Anything after that is 2026.
    /// </summary>
    public static string GetCurrentCycleId()
    {
        var now = DateTime.UtcNow;
        // Season 2025 closed 2026-01-16T07:41:50Z. After that, we're in the 2026 cycle.
        var season2025Close = new DateTime(2026, 1, 16, 7, 41, 50, DateTimeKind.Utc);
        return now >= season2025Close ? "2026" : "2025";
    }

    public void RecordParticipation(string cycleId, string athleteSlug, string participationType, string? details = null, string? agentToken = null)
    {
        var now = DateTime.UtcNow.ToString("o");

        _db.Run(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO CycleParticipation (CycleId, AthleteSlug, ParticipationType, SubmittedAt, Details, AgentToken)
                VALUES (@cycleId, @athleteSlug, @type, @now, @details, @agentToken)";
            cmd.Parameters.AddWithValue("@cycleId", cycleId);
            cmd.Parameters.AddWithValue("@athleteSlug", athleteSlug);
            cmd.Parameters.AddWithValue("@type", participationType);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@details", (object?)details ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@agentToken", (object?)agentToken ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        });

        _logger.LogInformation("Recorded {Type} participation for {Slug} in cycle {Cycle}", participationType, athleteSlug, cycleId);
    }

    public bool HasParticipated(string cycleId, string athleteSlug)
    {
        return _db.Run(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT 1 FROM CycleParticipation
                WHERE CycleId = @cycleId AND AthleteSlug = @athleteSlug
                LIMIT 1";
            cmd.Parameters.AddWithValue("@cycleId", cycleId);
            cmd.Parameters.AddWithValue("@athleteSlug", athleteSlug);
            return cmd.ExecuteScalar() is not null;
        });
    }

    public CycleStats GetCycleStats(string cycleId)
    {
        return _db.Run(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT ParticipationType, COUNT(*) as Cnt
                FROM CycleParticipation
                WHERE CycleId = @cycleId
                GROUP BY ParticipationType";
            cmd.Parameters.AddWithValue("@cycleId", cycleId);

            var stats = new CycleStats { CycleId = cycleId };
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var type = reader.GetString(0);
                var count = reader.GetInt32(1);
                stats.Total += count;
                switch (type)
                {
                    case "new_application": stats.NewApplications += count; break;
                    case "confirmation": stats.Confirmations += count; break;
                    case "data_update": stats.DataUpdates += count; break;
                    case "new_results": stats.NewResults += count; break;
                }
            }
            return stats;
        });
    }

    public List<CycleStats> GetAllCycleStats()
    {
        return _db.Run(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT CycleId, ParticipationType, COUNT(*) as Cnt
                FROM CycleParticipation
                GROUP BY CycleId, ParticipationType
                ORDER BY CycleId";

            var byCycle = new Dictionary<string, CycleStats>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var cycleId = reader.GetString(0);
                var type = reader.GetString(1);
                var count = reader.GetInt32(2);

                if (!byCycle.TryGetValue(cycleId, out var stats))
                {
                    stats = new CycleStats { CycleId = cycleId };
                    byCycle[cycleId] = stats;
                }

                stats.Total += count;
                switch (type)
                {
                    case "new_application": stats.NewApplications += count; break;
                    case "confirmation": stats.Confirmations += count; break;
                    case "data_update": stats.DataUpdates += count; break;
                    case "new_results": stats.NewResults += count; break;
                }
            }

            return byCycle.Values.OrderBy(s => s.CycleId).ToList();
        });
    }

    /// <summary>
    /// One-time backfill: inserts all provided slugs as "new_application" for the 2025 cycle.
    /// Skips slugs that already have a participation record for 2025.
    /// </summary>
    public int BackfillSeason2025(IEnumerable<string> athleteSlugs)
    {
        var inserted = 0;
        var seasonClose = new DateTime(2026, 1, 16, 7, 41, 50, DateTimeKind.Utc).ToString("o");

        _db.Run(conn =>
        {
            using var tx = conn.BeginTransaction();

            foreach (var slug in athleteSlugs)
            {
                using var check = conn.CreateCommand();
                check.Transaction = tx;
                check.CommandText = "SELECT 1 FROM CycleParticipation WHERE CycleId = '2025' AND AthleteSlug = @slug LIMIT 1";
                check.Parameters.AddWithValue("@slug", slug);

                if (check.ExecuteScalar() is not null)
                    continue;

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO CycleParticipation (CycleId, AthleteSlug, ParticipationType, SubmittedAt)
                    VALUES ('2025', @slug, 'new_application', @ts)";
                cmd.Parameters.AddWithValue("@slug", slug);
                cmd.Parameters.AddWithValue("@ts", seasonClose);
                cmd.ExecuteNonQuery();
                inserted++;
            }

            tx.Commit();
        });

        if (inserted > 0)
            _logger.LogInformation("Backfilled {Count} athletes for 2025 season", inserted);

        return inserted;
    }
}

public class CycleStats
{
    public string CycleId { get; set; } = "";
    public int Total { get; set; }
    public int NewApplications { get; set; }
    public int Confirmations { get; set; }
    public int DataUpdates { get; set; }
    public int NewResults { get; set; }
}
