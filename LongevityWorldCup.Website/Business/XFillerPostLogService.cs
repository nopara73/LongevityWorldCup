using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public enum FillerType
{
    Top3Leaderboard,
    CrowdGuesses,
    Newcomers,
    DomainTop
}

public class XFillerPostLogService
{
    private const string TableName = "XFillerPostLog";
    private static readonly string[] Top3LeagueSlugs = ["ultimate", "amateur", "mens", "womens", "open", "silent-generation", "baby-boomers", "gen-x", "millennials", "gen-z", "gen-alpha", "prosperan"];
    private static readonly string[] DomainKeys = ["liver", "kidney", "metabolic", "inflammation", "immune", "vitamin_d"];
    private readonly DatabaseManager _db;

    public XFillerPostLogService(DatabaseManager db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {TableName} (
                    PostedAtUtc TEXT NOT NULL,
                    Type       INTEGER NOT NULL,
                    Text       TEXT NOT NULL,
                    SubjectSlug TEXT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            var addedSubjectSlug = false;
            cmd.CommandText = $"ALTER TABLE {TableName} ADD COLUMN SubjectSlug TEXT NULL;";
            try
            {
                cmd.ExecuteNonQuery();
                addedSubjectSlug = true;
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
            {
            }

            if (addedSubjectSlug)
            {
                cmd.CommandText = $"UPDATE {TableName} SET SubjectSlug = NULL WHERE SubjectSlug IS NULL;";
                cmd.ExecuteNonQuery();
            }

            cmd.CommandText = $"CREATE INDEX IF NOT EXISTS IX_{TableName}_Type_PostedAtUtc ON {TableName}(Type, PostedAtUtc);";
            cmd.ExecuteNonQuery();
            cmd.CommandText = $"CREATE INDEX IF NOT EXISTS IX_{TableName}_SubjectSlug_PostedAtUtc ON {TableName}(SubjectSlug, PostedAtUtc);";
            cmd.ExecuteNonQuery();
        });
    }

    public void LogPost(DateTime postedAtUtc, FillerType type, string text, string? subjectSlug = null)
    {
        var infoToken = text ?? "";
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"INSERT INTO {TableName} (PostedAtUtc, Type, Text, SubjectSlug) VALUES (@at, @type, @text, @subjectSlug)";
            cmd.Parameters.AddWithValue("@at", postedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@type", (int)type);
            cmd.Parameters.AddWithValue("@text", infoToken);
            cmd.Parameters.AddWithValue("@subjectSlug", string.IsNullOrWhiteSpace(subjectSlug) ? DBNull.Value : subjectSlug.Trim());
            cmd.ExecuteNonQuery();
        });
    }

    public void LogSubjectPost(DateTime postedAtUtc, string sourceText, string? subjectSlug)
    {
        if (string.IsNullOrWhiteSpace(subjectSlug))
            return;

        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"INSERT INTO {TableName} (PostedAtUtc, Type, Text, SubjectSlug) VALUES (@at, @type, @text, @subjectSlug)";
            cmd.Parameters.AddWithValue("@at", postedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@type", -1);
            cmd.Parameters.AddWithValue("@text", sourceText ?? "");
            cmd.Parameters.AddWithValue("@subjectSlug", subjectSlug.Trim());
            cmd.ExecuteNonQuery();
        });
    }

    public bool IsSubjectOnCooldown(string subjectSlug, TimeSpan cooldown, DateTime? nowUtc = null)
    {
        if (cooldown <= TimeSpan.Zero || string.IsNullOrWhiteSpace(subjectSlug))
            return false;

        var normalized = subjectSlug.Trim();
        var lastAt = _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"""
                SELECT PostedAtUtc
                FROM {TableName}
                WHERE SubjectSlug = @subjectSlug
                ORDER BY PostedAtUtc DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@subjectSlug", normalized);
            var raw = cmd.ExecuteScalar();
            if (raw is string s &&
                DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return (DateTime?)dt;
            return (DateTime?)null;
        });

        if (!lastAt.HasValue)
            return false;

        var now = nowUtc ?? DateTime.UtcNow;
        return now - lastAt.Value < cooldown;
    }

    public (FillerType Type, string PayloadText) GetSuggestedNextFiller()
    {
        var candidates = GetSuggestedFillersOrdered();
        return candidates.Count > 0 ? candidates[0] : (FillerType.Top3Leaderboard, "league[ultimate]");
    }

    public IReadOnlyList<(FillerType Type, string PayloadText)> GetSuggestedFillersOrdered()
    {
        var options = new List<(FillerType Type, string Text)>();
        foreach (var slug in Top3LeagueSlugs)
            options.Add((FillerType.Top3Leaderboard, $"league[{slug}]"));
        options.Add((FillerType.CrowdGuesses, ""));
        foreach (var dk in DomainKeys)
            options.Add((FillerType.DomainTop, $"domain[{dk}]"));

        var lastByOption = _db.Run(sqlite =>
        {
            var rows = new List<(FillerType Type, string Text, DateTime PostedAtUtc)>();
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"SELECT Type, Text, PostedAtUtc FROM {TableName}";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var typeInt = r.GetInt32(0);
                var text = r.IsDBNull(1) ? "" : r.GetString(1);
                var type = Enum.IsDefined(typeof(FillerType), typeInt) ? (FillerType)typeInt : (FillerType)(-1);
                if (type < 0) continue;
                if (DateTime.TryParse(r.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    rows.Add((type, text, dt));
            }
            return rows;
        });

        var dict = new Dictionary<(FillerType, string), DateTime>();
        foreach (var (type, payloadText) in options)
        {
            var last = lastByOption
                .Where(x => x.Type == type && TokenBelongsToOption(type, payloadText, x.Text))
                .Select(x => x.PostedAtUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            dict[(type, payloadText)] = last;
        }

        return options
            .OrderBy(x => dict.TryGetValue((x.Type, x.Text), out var t) ? t : DateTime.MinValue)
            .Select(x => (x.Type, x.Text))
            .ToList();
    }

    public bool IsUnchangedFromLastForOption(FillerType type, string payloadText, string infoToken)
    {
        var payload = payloadText ?? "";
        var token = infoToken ?? "";
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var candidates = _db.Run(sqlite =>
        {
            var rows = new List<(string Text, DateTime PostedAtUtc)>();
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"""
                SELECT Text, PostedAtUtc
                FROM {TableName}
                WHERE Type = @type
                ORDER BY PostedAtUtc DESC
                """;
            cmd.Parameters.AddWithValue("@type", (int)type);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var text = r.IsDBNull(0) ? "" : r.GetString(0);
                var dt = DateTime.MinValue;
                if (!r.IsDBNull(1))
                    DateTime.TryParse(r.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind, out dt);
                rows.Add((text, dt));
            }
            return rows;
        });

        var last = candidates
            .Where(x => TokenBelongsToOption(type, payload, x.Text))
            .Select(x => x.Text)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(last))
            return false;
        return string.Equals(last, token, StringComparison.Ordinal);
    }

    public bool IsOnCooldownForOption(FillerType type, string payloadText, TimeSpan cooldown, DateTime? nowUtc = null)
    {
        if (cooldown <= TimeSpan.Zero)
            return false;

        var payload = payloadText ?? "";
        var lastAt = _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"""
                SELECT Text, PostedAtUtc
                FROM {TableName}
                WHERE Type = @type
                ORDER BY PostedAtUtc DESC
                """;
            cmd.Parameters.AddWithValue("@type", (int)type);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var text = r.IsDBNull(0) ? "" : r.GetString(0);
                if (!TokenBelongsToOption(type, payload, text))
                    continue;

                if (r.IsDBNull(1))
                    return (DateTime?)null;

                if (DateTime.TryParse(r.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    return (DateTime?)dt;
            }

            return (DateTime?)null;
        });

        if (!lastAt.HasValue)
            return false;

        var now = nowUtc ?? DateTime.UtcNow;
        return now - lastAt.Value < cooldown;
    }

    public bool IsOnCooldownForType(FillerType type, TimeSpan cooldown, DateTime? nowUtc = null)
    {
        if (cooldown <= TimeSpan.Zero)
            return false;

        var lastAt = _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"""
                SELECT PostedAtUtc
                FROM {TableName}
                WHERE Type = @type
                ORDER BY PostedAtUtc DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@type", (int)type);
            var raw = cmd.ExecuteScalar();
            if (raw is string s &&
                DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return (DateTime?)dt;
            return (DateTime?)null;
        });

        if (!lastAt.HasValue)
            return false;

        var now = nowUtc ?? DateTime.UtcNow;
        return now - lastAt.Value < cooldown;
    }

    private static bool TokenBelongsToOption(FillerType type, string payloadText, string tokenText)
    {
        var token = tokenText ?? "";
        var payload = payloadText ?? "";

        return type switch
        {
            FillerType.Top3Leaderboard => token.StartsWith(payload + " ", StringComparison.OrdinalIgnoreCase),
            FillerType.DomainTop => token.StartsWith(payload + " ", StringComparison.OrdinalIgnoreCase),
            FillerType.CrowdGuesses => token.StartsWith("podium[", StringComparison.OrdinalIgnoreCase),
            FillerType.Newcomers => token.StartsWith("slugs[", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
