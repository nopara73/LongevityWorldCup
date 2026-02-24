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
    private static readonly string[] DomainKeys = ["liver", "kidney", "metabolic", "inflammation", "immune"];
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
                    Text       TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            cmd.CommandText = $"CREATE INDEX IF NOT EXISTS IX_{TableName}_Type_PostedAtUtc ON {TableName}(Type, PostedAtUtc);";
            cmd.ExecuteNonQuery();
        });
    }

    public void LogPost(DateTime postedAtUtc, FillerType type, string text)
    {
        var infoToken = text ?? "";
        _db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = $"INSERT INTO {TableName} (PostedAtUtc, Type, Text) VALUES (@at, @type, @text)";
            cmd.Parameters.AddWithValue("@at", postedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@type", (int)type);
            cmd.Parameters.AddWithValue("@text", infoToken);
            cmd.ExecuteNonQuery();
        });
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

    private static bool TokenBelongsToOption(FillerType type, string payloadText, string tokenText)
    {
        var token = tokenText ?? "";
        var payload = payloadText ?? "";

        return type switch
        {
            FillerType.Top3Leaderboard => token.StartsWith(payload + " ", StringComparison.OrdinalIgnoreCase),
            FillerType.DomainTop => token.StartsWith(payload + " ", StringComparison.OrdinalIgnoreCase),
            FillerType.CrowdGuesses => token.StartsWith("slugs[", StringComparison.OrdinalIgnoreCase),
            FillerType.Newcomers => token.StartsWith("slugs[", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}
