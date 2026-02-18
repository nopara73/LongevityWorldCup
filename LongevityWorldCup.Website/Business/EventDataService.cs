using System.Globalization;
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
    DonationReceived = 3,
    AthleteCountMilestone = 4,
    BadgeAward = 5,
    CustomEvent = 6,
    SeasonFinalResult = 7
}

public sealed class EventDataService : IDisposable
{
    private const double DefaultRelevanceJoined = 5d;
    private const double DefaultRelevanceNewRank = 10d;
    private const double DefaultRelevanceDonation = 9d;
    private const double DefaultRelevanceAthleteMilestone = 8d;
    private const double DefaultRelevanceBadgeAward = 8d;

    private readonly DatabaseManager _db;
    private readonly SlackEventService _slackEvents;

    public JsonArray Events { get; private set; } = [];

    public EventDataService(IWebHostEnvironment env, SlackEventService slackEvents, DatabaseManager db)
    {
        _ = env;
        _slackEvents = slackEvents;
        _db = db ?? throw new ArgumentNullException(nameof(db));

        var dataDir = EnvironmentHelpers.GetDataDir();
        Directory.CreateDirectory(dataDir);

        _db.Run(sqlite =>
        {
            using (var cmd = sqlite.CreateCommand())
            {
                cmd.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS Events (
                        Id             TEXT PRIMARY KEY,
                        Type           INTEGER NOT NULL,
                        Text           TEXT NOT NULL,
                        OccurredAt     TEXT NOT NULL,
                        Relevance      REAL  NOT NULL DEFAULT 5,
                        SlackProcessed INTEGER NOT NULL DEFAULT 0
                    );
                    """;
                cmd.ExecuteNonQuery();

                var addedSlackProcessed = false;
                cmd.CommandText = "ALTER TABLE Events ADD COLUMN SlackProcessed INTEGER NOT NULL DEFAULT 0;";
                try
                {
                    cmd.ExecuteNonQuery();
                    addedSlackProcessed = true;
                }
                catch
                {
                }

                if (addedSlackProcessed)
                {
                    cmd.CommandText = "UPDATE Events SET SlackProcessed = 1;";
                    cmd.ExecuteNonQuery();
                }

                cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Events_OccurredAt ON Events(OccurredAt);";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Events_Type_OccurredAt ON Events(Type, OccurredAt);";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Events_Relevance ON Events(Relevance);";
                cmd.ExecuteNonQuery();
            }
        });

        ProcessPendingSlackEvents();
        ReloadIntoCache();

        _db.DatabaseChanged += OnDatabaseChanged;
    }

    private void OnDatabaseChanged()
    {
        try
        {
            ProcessPendingSlackEvents();
            ReloadIntoCache();
        }
        catch
        {
        }
    }

    private void ProcessPendingSlackEvents()
    {
        var notify = new List<(EventType Type, string RawText)>();

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var selectCmd = sqlite.CreateCommand();
            selectCmd.Transaction = tx;
            selectCmd.CommandText =
                "SELECT Id, Type, Text FROM Events " +
                "WHERE SlackProcessed = 0 " +
                "ORDER BY OccurredAt ASC;";

            using var r = selectCmd.ExecuteReader();
            var pending = new List<(string Id, int TypeInt, string Text)>();
            while (r.Read())
                pending.Add((r.GetString(0), r.GetInt32(1), r.GetString(2)));

            if (pending.Count > 0)
            {
                using var claimCmd = sqlite.CreateCommand();
                claimCmd.Transaction = tx;
                claimCmd.CommandText = "UPDATE Events SET SlackProcessed = 1 WHERE Id = @id AND SlackProcessed = 0;";
                var pId = claimCmd.Parameters.Add("@id", SqliteType.Text);

                foreach (var (id, typeInt, text) in pending)
                {
                    pId.Value = id;
                    var affected = claimCmd.ExecuteNonQuery();
                    if (affected != 1) continue;

                    var type = Enum.IsDefined(typeof(EventType), typeInt) ? (EventType)typeInt : EventType.General;
                    notify.Add((type, text));
                }
            }

            tx.Commit();
        });

        foreach (var n in notify) FireAndForgetSlack(n.Type, n.RawText);
    }

    public void SetAthleteDirectory(IReadOnlyList<(string Slug, string Name, int? CurrentRank)> items)
    {
        _slackEvents.SetAthleteDirectory(items);
    }

    public void SetPodcastLinks(IReadOnlyList<(string Slug, string PodcastLink)> items)
    {
        _slackEvents.SetPodcastLinks(items);
    }

    public void CreateNewRankEvents(
        IEnumerable<(string AthleteSlug, DateTime OccurredAtUtc, int Rank, string? ReplacedSlug)> items,
        bool skipIfExists = false,
        double defaultRelevance = DefaultRelevanceNewRank)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        int created = 0;

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var existsCmd = sqlite.CreateCommand();
            existsCmd.Transaction = tx;
            existsCmd.CommandText = "SELECT 1 FROM Events WHERE Type=@t AND Text=@txt AND OccurredAt=@occ LIMIT 1;";
            var exType = existsCmd.Parameters.Add("@t", SqliteType.Integer);
            var exText = existsCmd.Parameters.Add("@txt", SqliteType.Text);
            var exOcc = existsCmd.Parameters.Add("@occ", SqliteType.Text);

            using var insertCmd = sqlite.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance) VALUES (@id, @type, @text, @occ, @rel);";
            var pId = insertCmd.Parameters.Add("@id", SqliteType.Text);
            var pType = insertCmd.Parameters.Add("@type", SqliteType.Integer);
            var pText = insertCmd.Parameters.Add("@text", SqliteType.Text);
            var pOcc = insertCmd.Parameters.Add("@occ", SqliteType.Text);
            var pRel = insertCmd.Parameters.Add("@rel", SqliteType.Real);

            foreach (var (slug, occurredAtUtc, rank, replacedSlug) in items)
            {
                if (string.IsNullOrWhiteSpace(slug)) continue;
                if (rank < 1) continue;

                var occurredAt = EnsureUtc(occurredAtUtc).ToString("o");
                var textBase = $"slug[{slug}] rank[{rank}]";
                var text = string.IsNullOrWhiteSpace(replacedSlug) ? textBase : $"{textBase} prev[{replacedSlug}]";

                var shouldInsert = true;
                if (skipIfExists)
                {
                    exType.Value = (int)EventType.NewRank;
                    exText.Value = text;
                    exOcc.Value = occurredAt;
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
        });

        if (created > 0)
        {
            ReloadIntoCache();
        }
    }

    public void CreateJoinedEventsForAthletes(
        IEnumerable<(string AthleteSlug, DateTime JoinedAtUtc, int? CurrentRank, string? ReplacedSlug)> athletes,
        bool skipIfExists = true,
        double defaultRelevance = DefaultRelevanceJoined)
    {
        if (athletes is null) throw new ArgumentNullException(nameof(athletes));

        int created = 0;

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var existsCmd = sqlite.CreateCommand();
            existsCmd.Transaction = tx;
            existsCmd.CommandText =
                "SELECT 1 FROM Events WHERE Type=@t AND Text=@txt AND OccurredAt=@occ LIMIT 1;";
            var exType = existsCmd.Parameters.Add("@t", SqliteType.Integer);
            var exText = existsCmd.Parameters.Add("@txt", SqliteType.Text);
            var exOcc = existsCmd.Parameters.Add("@occ", SqliteType.Text);

            using var insertCmd = sqlite.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance) VALUES (@id, @type, @text, @occ, @rel);";
            var pId = insertCmd.Parameters.Add("@id", SqliteType.Text);
            var pType = insertCmd.Parameters.Add("@type", SqliteType.Integer);
            var pText = insertCmd.Parameters.Add("@text", SqliteType.Text);
            var pOcc = insertCmd.Parameters.Add("@occ", SqliteType.Text);
            var pRel = insertCmd.Parameters.Add("@rel", SqliteType.Real);

            foreach (var (slug, joinedAtUtc, currentRank, replacedSlug) in athletes)
            {
                if (string.IsNullOrWhiteSpace(slug)) continue;

                var occurredAt = EnsureUtc(joinedAtUtc).ToString("o");

                var joinedText = $"slug[{slug}]";
                var insertJoined = true;
                if (skipIfExists)
                {
                    exType.Value = (int)EventType.Joined;
                    exText.Value = joinedText;
                    exOcc.Value = occurredAt;
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

                if (currentRank is int r && r >= 1)
                {
                    var rankText = !string.IsNullOrWhiteSpace(replacedSlug)
                        ? $"slug[{slug}] rank[{r}] prev[{replacedSlug}]"
                        : $"slug[{slug}] rank[{r}]";

                    var insertRank = true;
                    if (skipIfExists)
                    {
                        exType.Value = (int)EventType.NewRank;
                        exText.Value = rankText;
                        exOcc.Value = occurredAt;
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
        });

        if (created > 0)
        {
            ReloadIntoCache();
        }
    }

    public void CreateDonationReceivedEvents(
        IEnumerable<(string TxId, DateTime OccurredAtUtc, long AmountSatoshis)> items,
        bool skipIfExists = true,
        double defaultRelevance = DefaultRelevanceDonation)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        int created = 0;

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var existsCmd = sqlite.CreateCommand();
            existsCmd.Transaction = tx;
            existsCmd.CommandText = "SELECT 1 FROM Events WHERE Type=@t AND Text=@txt LIMIT 1;";
            var exType = existsCmd.Parameters.Add("@t", SqliteType.Integer);
            var exText = existsCmd.Parameters.Add("@txt", SqliteType.Text);

            using var insertCmd = sqlite.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance) " +
                "VALUES (@id, @type, @text, @occ, @rel);";
            var pId = insertCmd.Parameters.Add("@id", SqliteType.Text);
            var pTyp = insertCmd.Parameters.Add("@type", SqliteType.Integer);
            var pTxt = insertCmd.Parameters.Add("@text", SqliteType.Text);
            var pOcc = insertCmd.Parameters.Add("@occ", SqliteType.Text);
            var pRel = insertCmd.Parameters.Add("@rel", SqliteType.Real);

            foreach (var (txId, occurredAtUtc, amount) in items)
            {
                if (string.IsNullOrWhiteSpace(txId)) continue;
                if (amount <= 0) continue;

                var occurredAt = EnsureUtc(occurredAtUtc).ToString("o");
                var text = $"tx[{txId}] sats[{amount}]";

                var shouldInsert = true;
                if (skipIfExists)
                {
                    exType.Value = (int)EventType.DonationReceived;
                    exText.Value = text;
                    shouldInsert = existsCmd.ExecuteScalar() == null;
                }

                if (!shouldInsert) continue;

                pId.Value = Guid.NewGuid().ToString("N");
                pTyp.Value = (int)EventType.DonationReceived;
                pTxt.Value = text;
                pOcc.Value = occurredAt;
                pRel.Value = defaultRelevance;

                insertCmd.ExecuteNonQuery();
                created++;
            }

            tx.Commit();
        });

        if (created > 0)
        {
            ReloadIntoCache();
        }
    }

    public void CreateAthleteCountMilestoneEvents(
        IEnumerable<(int Count, DateTime OccurredAtUtc)> items,
        bool skipIfExists = true,
        double defaultRelevance = DefaultRelevanceAthleteMilestone)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        int created = 0;

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var existsCmd = sqlite.CreateCommand();
            existsCmd.Transaction = tx;
            existsCmd.CommandText = "SELECT 1 FROM Events WHERE Type=@t AND Text=@txt LIMIT 1;";
            var exType = existsCmd.Parameters.Add("@t", SqliteType.Integer);
            var exText = existsCmd.Parameters.Add("@txt", SqliteType.Text);

            using var insertCmd = sqlite.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance) VALUES (@id, @type, @text, @occ, @rel);";
            var pId = insertCmd.Parameters.Add("@id", SqliteType.Text);
            var pTyp = insertCmd.Parameters.Add("@type", SqliteType.Integer);
            var pTxt = insertCmd.Parameters.Add("@text", SqliteType.Text);
            var pOcc = insertCmd.Parameters.Add("@occ", SqliteType.Text);
            var pRel = insertCmd.Parameters.Add("@rel", SqliteType.Real);

            foreach (var (count, occurredAtUtc) in items)
            {
                if (count <= 0) continue;

                var occurredAt = EnsureUtc(occurredAtUtc).ToString("o");
                var text = $"athletes[{count}]";

                var shouldInsert = true;
                if (skipIfExists)
                {
                    exType.Value = (int)EventType.AthleteCountMilestone;
                    exText.Value = text;
                    shouldInsert = existsCmd.ExecuteScalar() == null;
                }

                if (!shouldInsert) continue;

                pId.Value = Guid.NewGuid().ToString("N");
                pTyp.Value = (int)EventType.AthleteCountMilestone;
                pTxt.Value = text;
                pOcc.Value = occurredAt;
                pRel.Value = defaultRelevance;

                insertCmd.ExecuteNonQuery();
                created++;
            }

            tx.Commit();
        });

        if (created > 0)
        {
            ReloadIntoCache();
        }
    }

    public void CreateBadgeAwardEvents(
        IEnumerable<(string AthleteSlug, DateTime OccurredAtUtc, string BadgeLabel, string LeagueCategory, string? LeagueValue, int? Place, bool BecameSoloOwner, string? ReplacedSlug, IReadOnlyList<string>? ReplacedSlugs)> items,
        bool skipIfExists = true,
        double defaultRelevance = DefaultRelevanceBadgeAward)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        int created = 0;

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var existsCmd = sqlite.CreateCommand();
            existsCmd.Transaction = tx;
            existsCmd.CommandText = "SELECT 1 FROM Events WHERE Type=@t AND Text=@txt AND OccurredAt=@occ LIMIT 1;";
            var exType = existsCmd.Parameters.Add("@t", SqliteType.Integer);
            var exText = existsCmd.Parameters.Add("@txt", SqliteType.Text);
            var exOcc = existsCmd.Parameters.Add("@occ", SqliteType.Text);

            using var insertCmd = sqlite.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance) VALUES (@id, @type, @text, @occ, @rel);";
            var pId = insertCmd.Parameters.Add("@id", SqliteType.Text);
            var pType = insertCmd.Parameters.Add("@type", SqliteType.Integer);
            var pText = insertCmd.Parameters.Add("@text", SqliteType.Text);
            var pOcc = insertCmd.Parameters.Add("@occ", SqliteType.Text);
            var pRel = insertCmd.Parameters.Add("@rel", SqliteType.Real);

            foreach (var (slug, occurredAtUtc, badgeLabel, leagueCategory, leagueValue, place, becameSoloOwner, replacedSlug, replacedSlugs) in items)
            {
                if (string.IsNullOrWhiteSpace(slug)) continue;
                if (string.IsNullOrWhiteSpace(badgeLabel)) continue;
                if (string.IsNullOrWhiteSpace(leagueCategory)) continue;

                var occurredAt = EnsureUtc(occurredAtUtc).ToString("o");
                var placeStr = place.HasValue ? place.Value.ToString(CultureInfo.InvariantCulture) : "";
                var baseText = $"slug[{slug}] badge[{badgeLabel}] cat[{leagueCategory}] val[{leagueValue ?? ""}] place[{placeStr}]";

                if (becameSoloOwner) baseText += " solo[1]";

                string text;

                if (!string.IsNullOrWhiteSpace(replacedSlug))
                {
                    text = $"{baseText} prev[{replacedSlug}]";
                }
                else if (replacedSlugs is { Count: > 0 })
                {
                    text = $"{baseText} prevs[{string.Join(",", replacedSlugs)}]";
                }
                else
                {
                    text = baseText;
                }

                var shouldInsert = true;
                if (skipIfExists)
                {
                    exType.Value = (int)EventType.BadgeAward;
                    exText.Value = text;
                    exOcc.Value = occurredAt;
                    shouldInsert = existsCmd.ExecuteScalar() == null;
                }

                if (!shouldInsert) continue;

                pId.Value = Guid.NewGuid().ToString("N");
                pType.Value = (int)EventType.BadgeAward;
                pText.Value = text;
                pOcc.Value = occurredAt;
                pRel.Value = defaultRelevance;
                insertCmd.ExecuteNonQuery();
                created++;
            }

            tx.Commit();
        });

        if (created > 0)
        {
            ReloadIntoCache();
        }
    }

    public bool HasAnySeasonFinalResults(int seasonId)
    {
        var pat = $"%season[{seasonId}]%";

        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Events WHERE Type=@t AND Text LIKE @pat LIMIT 1;";
            cmd.Parameters.AddWithValue("@t", (int)EventType.SeasonFinalResult);
            cmd.Parameters.AddWithValue("@pat", pat);
            return cmd.ExecuteScalar() != null;
        });
    }

    public bool HasCustomEventWithTitle(string titleRaw)
    {
        if (string.IsNullOrWhiteSpace(titleRaw)) return false;

        var patLf = titleRaw + "\n\n%";
        var patCrLf = titleRaw + "\r\n\r\n%";

        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM Events WHERE Type=@t AND (Text LIKE @patLf OR Text LIKE @patCrLf) LIMIT 1;";
            cmd.Parameters.AddWithValue("@t", (int)EventType.CustomEvent);
            cmd.Parameters.AddWithValue("@patLf", patLf);
            cmd.Parameters.AddWithValue("@patCrLf", patCrLf);
            return cmd.ExecuteScalar() != null;
        });
    }

    public void UpsertSeasonFinalResults(
        int seasonId,
        DateTime closesAtUtc,
        string clockId,
        IReadOnlyList<SeasonFinalResultRow> rows,
        double defaultRelevance = 2d)
    {
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        if (string.IsNullOrWhiteSpace(clockId)) throw new ArgumentNullException(nameof(clockId));

        var occurredAt = EnsureUtc(closesAtUtc).ToString("o");
        int created = 0;

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var insertCmd = sqlite.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance, SlackProcessed) " +
                "SELECT @id, @type, @text, @occ, @rel, 1 " +
                "WHERE NOT EXISTS (SELECT 1 FROM Events WHERE Type=@type AND Text=@text AND OccurredAt=@occ LIMIT 1);";
            var pId = insertCmd.Parameters.Add("@id", SqliteType.Text);
            var pType = insertCmd.Parameters.Add("@type", SqliteType.Integer);
            var pText = insertCmd.Parameters.Add("@text", SqliteType.Text);
            var pOcc = insertCmd.Parameters.Add("@occ", SqliteType.Text);
            var pRel = insertCmd.Parameters.Add("@rel", SqliteType.Real);

            pType.Value = (int)EventType.SeasonFinalResult;
            pOcc.Value = occurredAt;
            pRel.Value = defaultRelevance;

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                if (string.IsNullOrWhiteSpace(r.AthleteSlug)) continue;
                if (r.Place < 1) continue;

                var ageDiffText = r.AgeDiff.ToString("0.00", CultureInfo.InvariantCulture);
                var text = $"slug[{r.AthleteSlug}] season[{seasonId}] place[{r.Place}] clock[{clockId}] ageDiff[{ageDiffText}]";

                pId.Value = Guid.NewGuid().ToString("N");
                pText.Value = text;

                var affected = insertCmd.ExecuteNonQuery();
                if (affected == 1) created++;
            }

            tx.Commit();
        });

        if (created > 0)
        {
            ReloadIntoCache();
        }
    }

    public void CreateCustomEvent(string titleRaw, string contentRaw, DateTime? occurredAtUtc = null, double relevance = 15d)
    {
        if (string.IsNullOrWhiteSpace(titleRaw)) throw new ArgumentNullException(nameof(titleRaw));
        contentRaw ??= "";

        var combinedRaw = titleRaw + "\n\n" + contentRaw;
        var occurredAt = EnsureUtc(occurredAtUtc ?? DateTime.UtcNow).ToString("o");

        int created = 0;

        _db.Run(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();

            using var insertCmd = sqlite.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText =
                "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance) VALUES (@id, @type, @text, @occ, @rel);";
            insertCmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            insertCmd.Parameters.AddWithValue("@type", (int)EventType.CustomEvent);
            insertCmd.Parameters.AddWithValue("@text", combinedRaw);
            insertCmd.Parameters.AddWithValue("@occ", occurredAt);
            insertCmd.Parameters.AddWithValue("@rel", relevance);

            insertCmd.ExecuteNonQuery();
            created = 1;

            tx.Commit();
        });

        if (created > 0)
        {
            ReloadIntoCache();
        }
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
            $" ORDER BY OccurredAt {(newestFirst ? "DESC" : "ASC")}, CASE " +
            $"WHEN Type = {(int)EventType.Joined} THEN 0 " +
            $"WHEN Type = {(int)EventType.NewRank} THEN 1 " +
            $"WHEN Type = {(int)EventType.SeasonFinalResult} THEN 2 " +
            $"WHEN Type = {(int)EventType.BadgeAward} THEN 3 " +
            $"ELSE 4 END ASC" +
            (limit.HasValue ? " LIMIT @limit OFFSET @offset" : "");

        return _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
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
        });
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

    private void FireAndForgetSlack(EventType type, string rawText)
    {
        if (type == EventType.NewRank)
        {
            if (!EventHelpers.TryExtractRank(rawText, out var rank) || rank > 10) return;
            _ = _slackEvents.BufferAsync(type, rawText);
            return;
        }

        if (type == EventType.CustomEvent)
        {
            if (!EventHelpers.TryExtractCustomEventTitle(rawText, out var title))
                return;

            if (string.Equals(title, "Test", StringComparison.Ordinal))
                return;

            _ = _slackEvents.SendImmediateAsync(type, rawText);
            return;
        }

        if (type == EventType.DonationReceived || type == EventType.AthleteCountMilestone)
        {
            _ = _slackEvents.SendImmediateAsync(type, rawText);
            return;
        }

        if (type == EventType.BadgeAward)
        {
            if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label)) return;

            var norm = EventHelpers.NormalizeBadgeLabel(label);

            if (string.Equals(norm, "Podcast", StringComparison.OrdinalIgnoreCase))
            {
                _ = _slackEvents.BufferAsync(type, rawText);
                return;
            }

            if (!EventHelpers.TryExtractPlace(rawText, out var place) || place != 1) return;

            if (string.Equals(norm, "Age Reduction", StringComparison.OrdinalIgnoreCase))
            {
                _ = _slackEvents.BufferAsync(type, rawText);
                return;
            }

            if (string.Equals(norm, "Chronological Age - Oldest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(norm, "Chronological Age - Youngest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(norm, "PhenoAge - Lowest", StringComparison.OrdinalIgnoreCase)
                || string.Equals(norm, "Bortz Age - Lowest", StringComparison.OrdinalIgnoreCase))
            {
                _ = _slackEvents.BufferAsync(type, rawText);
                return;
            }

            return;
        }
    }

    public void Dispose()
    {
        _db.DatabaseChanged -= OnDatabaseChanged;
        GC.SuppressFinalize(this);
    }
}
