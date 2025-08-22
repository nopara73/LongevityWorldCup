using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public record EventItem(string Id, EventType Type, string Text, DateTime OccurredAtUtc, double Relevance)
{
    public JsonObject ToJson() => new()
    {
        ["Id"] = Id,
        ["Type"] = (int)Type,
        ["Text"] = Text,
        ["OccurredAt"] = OccurredAtUtc.ToString("o"),
        ["Relevance"] = Relevance
    };
}

public enum EventType
{
    General = 0,
    Joined = 1,
    NewRank = 2,
}

public sealed class EventDataService : IDisposable
{
    private const string DatabaseFileName = "LongevityWorldCup.db";
    private const double DefaultRelevanceJoined = 5d;
    private const double DefaultRelevanceNewRank = 10d;
    private readonly SqliteConnection _sqlite;

    public JsonArray Events { get; private set; } = [];

    public EventDataService(IWebHostEnvironment env)
    {
        _ = env;

        var dataDir = EnvironmentHelpers.GetDataDir();
        Directory.CreateDirectory(dataDir);

        var dbPath = Path.Combine(dataDir, DatabaseFileName);
        _sqlite = new SqliteConnection($"Data Source={dbPath}");
        _sqlite.Open();

        using (var cmd = _sqlite.CreateCommand())
        {
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS Events (
                    Id         TEXT PRIMARY KEY,
                    Type       INTEGER NOT NULL,
                    Text       TEXT NOT NULL,
                    OccurredAt TEXT NOT NULL,
                    Relevance  REAL  NOT NULL DEFAULT 5
                );
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Events_OccurredAt ON Events(OccurredAt);";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Events_Type_OccurredAt ON Events(Type, OccurredAt);";
            cmd.ExecuteNonQuery();

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Events_Relevance ON Events(Relevance);";
            cmd.ExecuteNonQuery();
        }

        ReloadIntoCache();
    }

    public void CreateNewRankEvents(
        IEnumerable<(string AthleteSlug, DateTime OccurredAtUtc, int Rank)> items,
        bool skipIfExists = true,
        double defaultRelevance = DefaultRelevanceNewRank)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        int created = 0;

        lock (_sqlite)
        {
            using var tx = _sqlite.BeginTransaction();

            using var existsCmd = _sqlite.CreateCommand();
            existsCmd.Transaction = tx;
            existsCmd.CommandText = "SELECT 1 FROM Events WHERE Type=@t AND Text=@txt LIMIT 1;";
            var exType = existsCmd.Parameters.Add("@t", SqliteType.Integer);
            var exText = existsCmd.Parameters.Add("@txt", SqliteType.Text);

            using var insertCmd = _sqlite.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance) VALUES (@id, @type, @text, @occ, @rel);";
            var pId = insertCmd.Parameters.Add("@id", SqliteType.Text);
            var pType = insertCmd.Parameters.Add("@type", SqliteType.Integer);
            var pText = insertCmd.Parameters.Add("@text", SqliteType.Text);
            var pOcc = insertCmd.Parameters.Add("@occ", SqliteType.Text);
            var pRel = insertCmd.Parameters.Add("@rel", SqliteType.Real);

            foreach (var (slug, occurredAtUtc, rank) in items)
            {
                if (string.IsNullOrWhiteSpace(slug)) continue;
                if (rank < 1 || rank > 10) continue;

                var occurredAt = EnsureUtc(occurredAtUtc).ToString("o");
                var text = $"slug[{slug}] rank[{rank}]";

                var shouldInsert = true;
                if (skipIfExists)
                {
                    exType.Value = (int)EventType.NewRank;
                    exText.Value = text;
                    shouldInsert = existsCmd.ExecuteScalar() == null;
                }
                if (!shouldInsert) continue;

                pId.Value = Guid.NewGuid().ToString("N");
                pType.Value = (int)EventType.NewRank;
                pText.Value = text;
                pOcc.Value = occurredAt;
                pRel.Value = defaultRelevance;
                insertCmd.ExecuteNonQuery();
                created++;
            }

            tx.Commit();
        }

        if (created > 0)
            ReloadIntoCache();
    }

    public void CreateJoinedEventsForAthletes(
        IEnumerable<(string AthleteSlug, DateTime JoinedAtUtc, int? CurrentRank)> athletes,
        bool skipIfExists = true,
        double defaultRelevance = DefaultRelevanceJoined)
    {
        if (athletes is null) throw new ArgumentNullException(nameof(athletes));

        int created = 0;

        lock (_sqlite)
        {
            using var tx = _sqlite.BeginTransaction();

            using var existsCmd = _sqlite.CreateCommand();
            existsCmd.Transaction = tx;
            existsCmd.CommandText = "SELECT 1 FROM Events WHERE Type=@t AND Text=@txt LIMIT 1;";
            var exType = existsCmd.Parameters.Add("@t", SqliteType.Integer);
            var exText = existsCmd.Parameters.Add("@txt", SqliteType.Text);

            using var insertCmd = _sqlite.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance) VALUES (@id, @type, @text, @occ, @rel);";
            var pId = insertCmd.Parameters.Add("@id", SqliteType.Text);
            var pType = insertCmd.Parameters.Add("@type", SqliteType.Integer);
            var pText = insertCmd.Parameters.Add("@text", SqliteType.Text);
            var pOcc = insertCmd.Parameters.Add("@occ", SqliteType.Text);
            var pRel = insertCmd.Parameters.Add("@rel", SqliteType.Real);

            foreach (var (slug, joinedAtUtc, currentRank) in athletes)
            {
                if (string.IsNullOrWhiteSpace(slug))
                    continue;

                var occurredAt = EnsureUtc(joinedAtUtc).ToString("o");
                var joinedText = $"slug[{slug}]";

                var insertJoined = true;
                if (skipIfExists)
                {
                    exType.Value = (int)EventType.Joined;
                    exText.Value = joinedText;
                    insertJoined = existsCmd.ExecuteScalar() == null;
                }
                if (insertJoined)
                {
                    pId.Value = Guid.NewGuid().ToString("N");
                    pType.Value = (int)EventType.Joined;
                    pText.Value = joinedText;
                    pOcc.Value = occurredAt;
                    pRel.Value = defaultRelevance;
                    insertCmd.ExecuteNonQuery();
                    created++;
                }

                if (currentRank.HasValue && currentRank.Value >= 1 && currentRank.Value <= 10)
                {
                    var rankText = $"slug[{slug}] rank[{currentRank.Value}]";
                    var insertRank = true;
                    if (skipIfExists)
                    {
                        exType.Value = (int)EventType.NewRank;
                        exText.Value = rankText;
                        insertRank = existsCmd.ExecuteScalar() == null;
                    }
                    if (insertRank)
                    {
                        pId.Value = Guid.NewGuid().ToString("N");
                        pType.Value = (int)EventType.NewRank;
                        pText.Value = rankText;
                        pOcc.Value = occurredAt;
                        pRel.Value = DefaultRelevanceNewRank;
                        insertCmd.ExecuteNonQuery();
                        created++;
                    }
                }
            }

            tx.Commit();
        }

        if (created > 0)
            ReloadIntoCache();
    }

    public IReadOnlyList<EventItem> GetEvents(
        EventType? type = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int? limit = null,
        int offset = 0,
        bool newestFirst = true)
    {
        var filters = new List<string>();
        var parameters = new List<SqliteParameter>();

        if (type.HasValue)
        {
            filters.Add("Type = @type");
            parameters.Add(new SqliteParameter("@type", (int)type.Value));
        }

        if (fromUtc.HasValue)
        {
            filters.Add("OccurredAt >= @from");
            parameters.Add(new SqliteParameter("@from", EnsureUtc(fromUtc.Value).ToString("o")));
        }

        if (toUtc.HasValue)
        {
            filters.Add("OccurredAt <= @to");
            parameters.Add(new SqliteParameter("@to", EnsureUtc(toUtc.Value).ToString("o")));
        }

        var sql =
            "SELECT Id, Type, Text, OccurredAt, Relevance FROM Events" +
            (filters.Count > 0 ? " WHERE " + string.Join(" AND ", filters) : "") +
            $" ORDER BY OccurredAt {(newestFirst ? "DESC" : "ASC")}" +
            (limit.HasValue ? " LIMIT @limit OFFSET @offset" : "");

        lock (_sqlite)
        {
            using var cmd = _sqlite.CreateCommand();
            cmd.CommandText = sql;
            foreach (var p in parameters) cmd.Parameters.Add(p);
            if (limit.HasValue)
            {
                cmd.Parameters.AddWithValue("@limit", limit.Value);
                cmd.Parameters.AddWithValue("@offset", Math.Max(0, offset));
            }

            var list = new List<EventItem>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Map(r));
            return list;
        }
    }

    public void ReloadIntoCache()
    {
        var arr = new JsonArray();
        foreach (var e in GetEvents())
            arr.Add(e.ToJson());
        Events = arr;
    }

    private static EventItem Map(SqliteDataReader r)
    {
        var id = r.GetString(0);
        var typeInt = r.GetInt32(1);
        var text = r.GetString(2);
        var occurred = DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind);
        var relevance = r.GetDouble(4);

        var type = Enum.IsDefined(typeof(EventType), typeInt) ? (EventType)typeInt : EventType.General;
        return new EventItem(id, type, text, occurred, relevance);
    }

    private static DateTime EnsureUtc(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);

    public void Dispose()
    {
        _sqlite.Close();
        _sqlite.Dispose();
        GC.SuppressFinalize(this);
    }
}
